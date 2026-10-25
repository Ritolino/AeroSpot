using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Core.Shell;

namespace AeroSpot.Host.Native
{
    public sealed class KeyboardHookManager : IDisposable
    {
        private readonly uint _quickLookKey;
        private readonly uint _quickLookModifiers;
        private readonly Action<string> _onPreviewRequested;
        private readonly Win32.HookProc _hookProcDelegate;

        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _suppressNextKeyUp;
        private bool _isDisposed;

        public bool IsHookInstalled => _hookHandle != IntPtr.Zero;

        public KeyboardHookManager(uint quickLookKey, uint quickLookModifiers, Action<string> onPreviewRequested)
        {
            _quickLookKey = quickLookKey;
            _quickLookModifiers = quickLookModifiers;
            _onPreviewRequested = onPreviewRequested ?? throw new ArgumentNullException(nameof(onPreviewRequested));
            _hookProcDelegate = LowLevelKeyboardProc;
        }

        public KeyboardHookManager(uint quickLookKey, Action<string> onPreviewRequested)
            : this(quickLookKey, 0, onPreviewRequested)
        {
        }

        public bool Install()
        {
            if (_hookHandle != IntPtr.Zero) return true;

            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr hMod = Win32.GetModuleHandle(curModule?.ModuleName);

                _hookHandle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _hookProcDelegate, hMod, 0);

                if (_hookHandle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    AppLogger.Error($"SetWindowsHookEx (WH_KEYBOARD_LL) fallito con errore Win32: {err}");
                    return false;
                }

                AppLogger.Info($"WH_KEYBOARD_LL hook installato con successo. Handle=0x{_hookHandle:X}, QuickLookKey=0x{_quickLookKey:X}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Eccezione durante l'installazione dell'hook di tastiera.", ex);
                return false;
            }
        }

        public void Uninstall()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_hookHandle);
                AppLogger.Info($"WH_KEYBOARD_LL hook rimosso (0x{_hookHandle:X}).");
                _hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                uint msg = (uint)wParam.ToInt64();
                bool isKeyDown = (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN);
                bool isKeyUp = (msg == Win32.WM_KEYUP || msg == Win32.WM_SYSKEYUP);

                if (isKeyDown || isKeyUp)
                {
                    var kbd = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

                    if (kbd.vkCode == _quickLookKey)
                    {
                        if (isKeyDown)
                        {
                            // Verifica corrispondenza tasti modificatori (Ctrl, Alt, Shift, Win)
                            bool ctrlDown = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
                            bool altDown = (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;
                            bool shiftDown = (Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0;
                            bool winDown = (Win32.GetAsyncKeyState(Win32.VK_LWIN) & 0x8000) != 0 || (Win32.GetAsyncKeyState(Win32.VK_RWIN) & 0x8000) != 0;

                            bool needCtrl = (_quickLookModifiers & Win32.MOD_CONTROL) != 0;
                            bool needAlt = (_quickLookModifiers & Win32.MOD_ALT) != 0;
                            bool needShift = (_quickLookModifiers & Win32.MOD_SHIFT) != 0;
                            bool needWin = (_quickLookModifiers & Win32.MOD_WIN) != 0;

                            if (ctrlDown != needCtrl || altDown != needAlt || shiftDown != needShift || winDown != needWin)
                            {
                                return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                            }

                            // 1. Verifica immediata: la finestra attiva è Explorer o Desktop?
                            IntPtr fgHwnd = Win32.GetForegroundWindow();
                            if (ExplorerFocusResolver.IsExplorerOrDesktop(fgHwnd))
                            {
                                // 2. Verifica che l'utente non stia digitando o rinominando
                                if (!ExplorerFocusResolver.IsUserTypingOrRenaming(fgHwnd))
                                {
                                    // CRITICO: Non invocare chiamate COM sincrone (IShellWindows) all'interno del callback
                                    // WH_KEYBOARD_LL (causa COMException 0x8001010D RPC_E_CANTCALLOUT_ININPUTSYNCCALL).
                                    // Sopprimi immediatamente l'evento Spazio nativo e delega la risoluzione a un thread STA dedicato.
                                    _suppressNextKeyUp = true;

                                    var thread = new Thread(() =>
                                    {
                                        try
                                        {
                                            if (ExplorerFocusResolver.TryGetFocusedPath(fgHwnd, out string? filePath) && !string.IsNullOrEmpty(filePath))
                                            {
                                                AppLogger.Info($"QuickLook triggerato da tastiera Shell per: {filePath}");
                                                _onPreviewRequested(filePath);
                                            }
                                            else
                                            {
                                                AppLogger.Debug($"Hook STA: Spazio premuto in Explorer (HWND=0x{fgHwnd:X}), ma nessun percorso con focus trovato.");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Error("Hook STA: eccezione durante la risoluzione del percorso con focus Shell", ex);
                                        }
                                    })
                                    {
                                        IsBackground = true,
                                        Name = "ShellFocusResolver"
                                    };
                                    thread.SetApartmentState(ApartmentState.STA);
                                    thread.Start();

                                    return (IntPtr)1; // Sopprimi l'evento Spazio nativo immediatamente
                                }
                                else
                                {
                                    AppLogger.Debug($"Hook: Spazio premuto in Explorer ma rilevata digitazione o rinomina attiva.");
                                }
                            }
                        }
                        else if (isKeyUp && _suppressNextKeyUp)
                        {
                            _suppressNextKeyUp = false;
                            return (IntPtr)1; // Sopprimi il corrispondente key-up per non lasciare eventi orfani
                        }
                    }
                }
            }

            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Uninstall();
        }
    }
}
