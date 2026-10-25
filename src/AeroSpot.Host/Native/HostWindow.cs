using System;
using System.Runtime.InteropServices;
using AeroSpot.Core.Config;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Host.Services;

namespace AeroSpot.Host.Native
{
    public sealed class HostWindow : IDisposable
    {
        private const string WindowClassName = "AeroSpot_Host_MessageClass";
        private const int HOTKEY_ID_PRIMARY = 9001;
        private const int HOTKEY_ID_FALLBACK = 9002;

        private AppConfig _config;
        private readonly WorkerManager _workerManager;
        private readonly Win32.WndProc _wndProcDelegate;

        private IntPtr _hWnd;
        private TrayManager? _trayManager;
        private KeyboardHookManager? _hookManager;
        private int _registeredHotkeyId;
        private System.Threading.CancellationTokenSource? _dynamicTrackingCts;
        private string? _lastActivePreviewPath;
        private bool _isDisposed;

        public HostWindow(AppConfig config, WorkerManager workerManager)
        {
            _config = config;
            _workerManager = workerManager;
            _wndProcDelegate = CustomWndProc;
        }

        public bool Initialize()
        {
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = _wndProcDelegate,
                lpszClassName = WindowClassName,
                hInstance = IntPtr.Zero
            };

            ushort regResult = Win32.RegisterClassEx(ref wc);
            if (regResult == 0)
            {
                int err = Marshal.GetLastWin32Error();
                // 1410 = ERROR_CLASS_ALREADY_EXISTS
                if (err != 1410)
                {
                    AppLogger.Error($"RegisterClassEx fallita con errore Win32: {err}");
                    return false;
                }
            }

            _hWnd = Win32.CreateWindowEx(
                0,
                WindowClassName,
                "AeroSpot.Host.HiddenWindow",
                0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hWnd == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                AppLogger.Error($"CreateWindowEx fallita con errore Win32: {err}");
                return false;
            }

            AppLogger.Info($"Finestra Win32 Host creata: HWND=0x{_hWnd:X}");

            // Inizializza Tray
            _trayManager = new TrayManager(_hWnd);
            _trayManager.GetWorkerStatusFunc = () => _workerManager.GetStatusString();
            _trayManager.ShowLauncherRequested += OnShowLauncherRequested;
            _trayManager.SettingsRequested += OnSettingsRequested;
            _trayManager.ExitRequested += OnExitRequested;
            _trayManager.AddTrayIcon(AeroSpot.Core.Localization.LocalizationService.Get("Tray_Tooltip"));

            _workerManager.ConfigReloadRequested += OnConfigReloadRequested;
            _workerManager.PreviewOpened += StartDynamicTracking;
            _workerManager.PreviewClosed += StopDynamicTracking;

            // Registra Hotkey globale
            RegisterHotkeys();

            // Inizializza hook globale per QuickLook (intercettazione Spazio/hotkey in Explorer e Desktop)
            _hookManager = new KeyboardHookManager(_config.QuickLookKey, _config.QuickLookModifiers, OnQuickLookPreviewRequested);
            if (!_hookManager.Install())
            {
                AppLogger.Warn("Installazione dell'hook per QuickLook non riuscita. L'anteprima da Explorer potrebbe non funzionare.");
            }

            return true;
        }

        private void OnQuickLookPreviewRequested(string filePath)
        {
            AppLogger.Info($"Richiesta anteprima QuickLook da Explorer per: {filePath}");
            _ = Task.Run(async () =>
            {
                bool ok = await _workerManager.ShowPreviewAsync(filePath);
                if (!ok)
                {
                    AppLogger.Warn($"Impossibile mostrare l'anteprima per {filePath}.");
                }
            });
        }

        private void StartDynamicTracking(string initialPath)
        {
            StopDynamicTracking();

            _lastActivePreviewPath = initialPath;
            _dynamicTrackingCts = new System.Threading.CancellationTokenSource();
            var token = _dynamicTrackingCts.Token;

            var thread = new System.Threading.Thread(() =>
            {
                AppLogger.Info($"Avvio monitoraggio dinamico selezione Explorer/Desktop per QuickLook. File iniziale: {initialPath}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        System.Threading.Thread.Sleep(120);
                        if (token.IsCancellationRequested) break;

                        IntPtr fg = Win32.GetForegroundWindow();
                        if (AeroSpot.Core.Shell.ExplorerFocusResolver.IsExplorerOrDesktop(fg) && 
                            !AeroSpot.Core.Shell.ExplorerFocusResolver.IsUserTypingOrRenaming(fg))
                        {
                            if (AeroSpot.Core.Shell.ExplorerFocusResolver.TryGetFocusedPath(fg, out string? newPath) && !string.IsNullOrEmpty(newPath))
                            {
                                if (!string.Equals(newPath, _lastActivePreviewPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    _lastActivePreviewPath = newPath;
                                    AppLogger.Info($"Selezione dinamica cambiata in Explorer: {newPath}");
                                    _ = _workerManager.ShowPreviewAsync(newPath);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug($"Errore nel ciclo di monitoraggio selezione dinamica: {ex.Message}");
                    }
                }
                AppLogger.Info("Monitoraggio dinamico selezione Explorer/Desktop terminato.");
            })
            {
                IsBackground = true,
                Name = "DynamicExplorerSelectionTracker"
            };
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
        }

        private void StopDynamicTracking()
        {
            if (_dynamicTrackingCts != null)
            {
                try
                {
                    _dynamicTrackingCts.Cancel();
                    _dynamicTrackingCts.Dispose();
                }
                catch { }
                _dynamicTrackingCts = null;
                _lastActivePreviewPath = null;
            }
        }

        private void RegisterHotkeys()
        {
            if (_registeredHotkeyId != 0)
            {
                Win32.UnregisterHotKey(_hWnd, _registeredHotkeyId);
                _registeredHotkeyId = 0;
            }

            // 1. Prova combinazione primaria (es. Alt + Spazio o Win + Spazio)
            bool ok = Win32.RegisterHotKey(_hWnd, HOTKEY_ID_PRIMARY, _config.HotkeyModifiers, _config.HotkeyKey);
            if (ok)
            {
                _registeredHotkeyId = HOTKEY_ID_PRIMARY;
                AppLogger.Info("Hotkey primaria registrata con successo.");
                return;
            }

            int err = Marshal.GetLastWin32Error();
            AppLogger.Warn($"Scorciatoia primaria non disponibile (LastError: {err}). Tentativo con fallback (Ctrl + Spazio)...");

            // 2. Prova combinazione secondaria (Ctrl + Spazio)
            bool fallbackOk = Win32.RegisterHotKey(_hWnd, HOTKEY_ID_FALLBACK, _config.FallbackHotkeyModifiers, _config.FallbackHotkeyKey);
            if (fallbackOk)
            {
                _registeredHotkeyId = HOTKEY_ID_FALLBACK;
                AppLogger.Info("Hotkey di fallback (Ctrl + Spazio) registrata con successo.");
                _trayManager?.ShowBalloonNotification(
                    AeroSpot.Core.Localization.LocalizationService.Get("Tray_HotkeyActive"),
                    "Alt/Win+Spazio è occupata dal sistema o da Listary. Scorciatoia attiva: Ctrl + Spazio.",
                    Win32.NIIF_INFO);
                return;
            }

            // 3. Prova combinazione di emergenza (Alt + F12)
            bool emergencyOk = Win32.RegisterHotKey(_hWnd, HOTKEY_ID_FALLBACK, Win32.MOD_ALT | Win32.MOD_NOREPEAT, _config.EmergencyHotkeyKey);
            if (emergencyOk)
            {
                _registeredHotkeyId = HOTKEY_ID_FALLBACK;
                AppLogger.Info("Hotkey di emergenza (Alt + F12) registrata con successo.");
                _trayManager?.ShowBalloonNotification(
                    AeroSpot.Core.Localization.LocalizationService.Get("Tray_EmergencyHotkey"),
                    "Scorciatoia attiva: Alt + F12.",
                    Win32.NIIF_WARNING);
            }
            else
            {
                AppLogger.Error($"Nessuna hotkey è stata registrata con successo (LastError: {Marshal.GetLastWin32Error()}).");
            }
        }

        private const uint WM_RELOAD_CONFIG = Win32.WM_USER + 102;

        private void OnShowLauncherRequested()
        {
            AppLogger.Info("Richiesta apertura Launcher da utente.");
            Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
            _ = Task.Run(async () =>
            {
                bool ok = await _workerManager.ShowLauncherAsync();
                if (!ok)
                {
                    _trayManager?.ShowBalloonNotification("AeroSpot", AeroSpot.Core.Localization.LocalizationService.Get("Tray_WorkerNotReady"), Win32.NIIF_ERROR);
                }
            });
        }

        private void OnSettingsRequested()
        {
            AppLogger.Info("Richiesta apertura Impostazioni da utente.");
            Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
            _ = Task.Run(async () =>
            {
                bool ok = await _workerManager.ShowSettingsAsync();
                if (!ok)
                {
                    _trayManager?.ShowBalloonNotification("AeroSpot", AeroSpot.Core.Localization.LocalizationService.Get("Tray_WorkerNotReady"), Win32.NIIF_ERROR);
                }
            });
        }

        private void OnConfigReloadRequested()
        {
            AppLogger.Info("Ricevuta richiesta ricarica configurazione. Invio messaggio al thread Host...");
            if (_hWnd != IntPtr.Zero)
            {
                Win32.PostMessage(_hWnd, WM_RELOAD_CONFIG, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void ExecuteConfigReload()
        {
            AppLogger.Info("Ricarica impostazioni Host eseguita sul thread Win32...");
            _config = AppConfig.Load();
            AeroSpot.Core.Localization.LocalizationService.Initialize(_config.Language);
            RegisterHotkeys();

            // Riconfigura hook se il tasto o i modificatori di QuickLook sono cambiati
            _hookManager?.Dispose();
            _hookManager = new KeyboardHookManager(_config.QuickLookKey, _config.QuickLookModifiers, OnQuickLookPreviewRequested);
            _hookManager.Install();

            if (_config.KeepWorkerWarm)
            {
                _ = Task.Run(() => _workerManager.EnsureWorkerStartedAsync());
            }
        }

        private void OnExitRequested()
        {
            AppLogger.Info("Richiesta uscita completa dall'applicazione.");
            Win32.DestroyWindow(_hWnd);
        }

        public void RunMessageLoop()
        {
            AppLogger.Info("Avvio del message loop Win32 nativo.");
            while (Win32.GetMessage(out Win32.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessage(ref msg);
            }
            AppLogger.Info("Message loop terminato.");
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Win32.WM_HOTKEY:
                    int id = wParam.ToInt32();
                    if (id == HOTKEY_ID_PRIMARY || id == HOTKEY_ID_FALLBACK)
                    {
                        AppLogger.Info($"WM_HOTKEY ricevuto (ID={id}).");
                        Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
                        OnShowLauncherRequested();
                        return IntPtr.Zero;
                    }
                    break;

                case WM_RELOAD_CONFIG:
                    ExecuteConfigReload();
                    return IntPtr.Zero;

                case Win32.WM_TRAYICON:
                    uint mouseMsg = (uint)lParam.ToInt32();
                    if (mouseMsg == Win32.WM_RBUTTONUP)
                    {
                        _trayManager?.ShowContextMenu();
                        return IntPtr.Zero;
                    }
                    else if (mouseMsg == Win32.WM_LBUTTONUP || mouseMsg == Win32.WM_LBUTTONDBLCLK)
                    {
                        OnShowLauncherRequested();
                        return IntPtr.Zero;
                    }
                    break;

                case Win32.WM_COMMAND:
                    uint cmd = (uint)wParam.ToInt32();
                    _trayManager?.HandleCommand(cmd);
                    return IntPtr.Zero;

                case Win32.WM_DESTROY:
                    StopDynamicTracking();
                    if (_registeredHotkeyId != 0)
                    {
                        Win32.UnregisterHotKey(_hWnd, _registeredHotkeyId);
                        _registeredHotkeyId = 0;
                    }
                    _hookManager?.Dispose();
                    _hookManager = null;
                    _trayManager?.Dispose();
                    Win32.PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopDynamicTracking();
            _hookManager?.Dispose();
            _hookManager = null;
            if (_hWnd != IntPtr.Zero)
            {
                Win32.DestroyWindow(_hWnd);
                _hWnd = IntPtr.Zero;
            }
        }
    }
}
