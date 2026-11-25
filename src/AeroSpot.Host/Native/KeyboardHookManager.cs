using System;
using System.Collections.Concurrent;
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
        private readonly Action? _onClosePreviewRequested;
        private readonly Action? _onCloseAllRequested;
        private readonly Func<bool>? _isPreviewActiveFunc;
        private readonly Func<int>? _getWorkerPidFunc;
        private readonly Win32.HookProc _hookProcDelegate;
        private readonly object _staLock = new object();

        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _isQuickLookKeyDown;
        private bool _suppressNextKeyUp;
        private uint _lastQuickLookKeyDownTime;
        private BlockingCollection<Action>? _staQueue;
        private Thread? _staThread;
        private volatile bool _isDisposed;

        public bool IsHookInstalled => _hookHandle != IntPtr.Zero;

        public KeyboardHookManager(
            uint quickLookKey,
            uint quickLookModifiers,
            Action<string> onPreviewRequested,
            Action? onClosePreviewRequested = null,
            Func<bool>? isPreviewActiveFunc = null,
            Action? onCloseAllRequested = null,
            Func<int>? getWorkerPidFunc = null)
        {
            _quickLookKey = quickLookKey;
            _quickLookModifiers = quickLookModifiers;
            _onPreviewRequested = onPreviewRequested ?? throw new ArgumentNullException(nameof(onPreviewRequested));
            _onClosePreviewRequested = onClosePreviewRequested;
            _onCloseAllRequested = onCloseAllRequested;
            _isPreviewActiveFunc = isPreviewActiveFunc;
            _getWorkerPidFunc = getWorkerPidFunc;
            _hookProcDelegate = LowLevelKeyboardProc;
        }

        public KeyboardHookManager(
            uint quickLookKey,
            Action<string> onPreviewRequested,
            Action? onClosePreviewRequested = null,
            Func<bool>? isPreviewActiveFunc = null,
            Action? onCloseAllRequested = null,
            Func<int>? getWorkerPidFunc = null)
            : this(quickLookKey, 0, onPreviewRequested, onClosePreviewRequested, isPreviewActiveFunc, onCloseAllRequested, getWorkerPidFunc)
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

                    // Sanity check UIPI: se il tasto QuickLook è stato rilasciato su una finestra elevata (Admin),
                    // l'evento KEYUP non raggiunge questo hook. Resetta lo stato se non è più fisicamente premuto.
                    if ((_isQuickLookKeyDown || _suppressNextKeyUp) &&
                        kbd.vkCode != _quickLookKey &&
                        (Win32.GetAsyncKeyState((int)_quickLookKey) & 0x8000) == 0)
                    {
                        _isQuickLookKeyDown = false;
                        _suppressNextKeyUp = false;
                    }

                    if (kbd.vkCode == _quickLookKey)
                    {
                        if (isKeyDown)
                        {
                            if (_isQuickLookKeyDown)
                            {
                                if ((Win32.GetAsyncKeyState((int)_quickLookKey) & 0x8000) == 0 &&
                                    unchecked(kbd.time - _lastQuickLookKeyDownTime) > 1200)
                                {
                                    _isQuickLookKeyDown = false;
                                    _suppressNextKeyUp = false;
                                }
                                else
                                {
                                    _lastQuickLookKeyDownTime = kbd.time;
                                    // Auto-repeat soppresso: non avviare lavoro aggiuntivo se il tasto viene tenuto premuto
                                    return (IntPtr)1;
                                }
                            }

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

                            Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);

                            IntPtr fgHwnd = Win32.GetForegroundWindow();
                            bool isPreviewActive = _isPreviewActiveFunc?.Invoke() ?? false;
                            bool isQuickLookWindow = isPreviewActive && IsQuickLookOrChildWindow(fgHwnd);

                            // Scenario chiusura rapida quando la finestra attiva in primo piano è proprio QuickLook
                            if (isPreviewActive && isQuickLookWindow)
                            {
                                _isQuickLookKeyDown = true;
                                _lastQuickLookKeyDownTime = kbd.time;
                                _suppressNextKeyUp = true;
                                _onClosePreviewRequested?.Invoke();
                                return (IntPtr)1;
                            }

                            // 1. Verifica immediata: la finestra attiva è Explorer, Desktop o un Selettore File (Apri/Salva/Allega)?
                            if (ExplorerFocusResolver.IsExplorerOrDesktop(fgHwnd) || ExplorerFocusResolver.IsFileDialog(fgHwnd))
                            {
                                // 2. Verifica che l'utente non stia digitando o rinominando
                                if (!ExplorerFocusResolver.IsUserTypingOrRenaming(fgHwnd))
                                {
                                    // CRITICO: Non invocare chiamate COM sincrone (IShellWindows) all'interno del callback
                                    // WH_KEYBOARD_LL (causa COMException 0x8001010D RPC_E_CANTCALLOUT_ININPUTSYNCCALL).
                                    // Sopprimi immediatamente l'evento Spazio nativo e delega la risoluzione al thread STA dedicato.
                                    _isQuickLookKeyDown = true;
                                    _lastQuickLookKeyDownTime = kbd.time;
                                    _suppressNextKeyUp = true;

                                    EnqueueStaWork(() =>
                                    {
                                        try
                                        {
                                            if (ExplorerFocusResolver.TryGetFocusedPath(fgHwnd, out string? filePath) && !string.IsNullOrEmpty(filePath))
                                            {
                                                AppLogger.Info($"QuickLook triggerato da tastiera Shell per: {filePath}");
                                                _onPreviewRequested(filePath);
                                            }
                                            else if (isPreviewActive)
                                            {
                                                _onClosePreviewRequested?.Invoke();
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
                                    });

                                    return (IntPtr)1; // Sopprimi l'evento Spazio nativo immediatamente
                                }
                                else
                                {
                                    AppLogger.Debug($"Hook: Spazio premuto in Explorer ma rilevata digitazione o rinomina attiva.");
                                }
                            }
                        }
                        else if (isKeyUp)
                        {
                            _isQuickLookKeyDown = false;
                            if (_suppressNextKeyUp)
                            {
                                _suppressNextKeyUp = false;
                                return (IntPtr)1; // Sopprimi il corrispondente key-up per non lasciare eventi orfani
                            }
                        }
                    }
                    else if (kbd.vkCode == 0x1B) // VK_ESCAPE
                    {
                        if (isKeyDown)
                        {
                            IntPtr fgHwnd = Win32.GetForegroundWindow();
                            bool isPreviewActive = _isPreviewActiveFunc?.Invoke() ?? false;
                            if (isPreviewActive && IsQuickLookOrChildWindow(fgHwnd))
                            {
                                if (_onCloseAllRequested != null)
                                {
                                    _onCloseAllRequested.Invoke();
                                }
                                else
                                {
                                    _onClosePreviewRequested?.Invoke();
                                }
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }

            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void EnqueueStaWork(Action action)
        {
            lock (_staLock)
            {
                if (_isDisposed) return;
                if (_staQueue == null)
                {
                    _staQueue = new BlockingCollection<Action>(boundedCapacity: 8);
                    _staThread = new Thread(RunStaWorkerLoop)
                    {
                        IsBackground = true,
                        Name = "ShellFocusResolver"
                    };
                    _staThread.SetApartmentState(ApartmentState.STA);
                    _staThread.Start();
                }

                _staQueue.TryAdd(action);
            }
        }

        private void RunStaWorkerLoop()
        {
            var queue = _staQueue;
            if (queue == null) return;

            try
            {
                foreach (var action in queue.GetConsumingEnumerable())
                {
                    if (_isDisposed) break;
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Hook STA: eccezione durante l'esecuzione del task STA", ex);
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private bool IsQuickLookOrChildWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            try
            {
                Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return false;

                int workerPid = _getWorkerPidFunc?.Invoke() ?? 0;
                if (workerPid > 0)
                {
                    return pid == (uint)workerPid;
                }

                using var proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName;
                if (string.Equals(procName, "AeroSpot.Worker", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(procName, "prevhost", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Uninstall();

            BlockingCollection<Action>? queue;
            Thread? thread;
            lock (_staLock)
            {
                queue = _staQueue;
                thread = _staThread;
                _staQueue = null;
                _staThread = null;
            }

            if (queue != null)
            {
                try { queue.CompleteAdding(); } catch { }
                bool joined = thread == null || !thread.IsAlive || Thread.CurrentThread == thread || thread.Join(500);
                if (joined)
                {
                    try { queue.Dispose(); } catch { }
                }
            }
        }
    }
}
