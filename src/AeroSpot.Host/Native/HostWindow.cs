using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Localization;
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
        private static readonly uint WM_TASKBARCREATED = Win32.RegisterWindowMessage("TaskbarCreated");

        private readonly object _configLock = new object();
        private readonly object _trackingLock = new object();
        private AppConfig _config;
        private readonly WorkerManager _workerManager;
        private readonly Win32.WndProc _wndProcDelegate;

        private IntPtr _hWnd;
        private uint _uiThreadId;
        private TrayManager? _trayManager;
        private KeyboardHookManager? _hookManager;
        private int _registeredHotkeyId;
        private string? _activeFallbackHotkeyLabel;
        private CancellationTokenSource? _dynamicTrackingCts;
        private volatile bool _isPreviewActive;
        private volatile bool _isDisposed;

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

            _uiThreadId = Win32.GetCurrentThreadId();
            AppLogger.Info($"Finestra Win32 Host creata: HWND=0x{_hWnd:X}");

            // Inizializza Tray
            AppConfig initialConfig;
            lock (_configLock)
            {
                initialConfig = _config;
            }
            _trayManager = new TrayManager(_hWnd, config: initialConfig);
            _trayManager.GetWorkerStatusFunc = () => _workerManager.GetStatusString();
            _trayManager.GetHotkeyLabelFunc = GetActiveHotkeyLabel;
            _trayManager.ShowLauncherRequested += OnShowLauncherRequested;
            _trayManager.SettingsRequested += OnSettingsRequested;
            _trayManager.ExitRequested += OnExitRequested;
            _trayManager.AddTrayIcon(LocalizationService.Get("Tray_Tooltip"));

            _workerManager.ConfigReloadRequested += OnConfigReloadRequested;
            _workerManager.ShutdownHostRequested += OnShutdownHostRequested;
            _workerManager.PreviewOpened += (path) =>
            {
                _isPreviewActive = true;
                StartDynamicTracking(path);
            };
            _workerManager.PreviewClosed += () =>
            {
                _isPreviewActive = false;
                StopDynamicTracking();
            };

            // Registra Hotkey globale
            RegisterHotkeys();

            // Inizializza hook globale per QuickLook (intercettazione Spazio/hotkey in Explorer e Desktop)
            _hookManager = new KeyboardHookManager(
                initialConfig.QuickLookKey,
                initialConfig.QuickLookModifiers,
                OnQuickLookPreviewRequested,
                OnQuickLookCloseRequested,
                () => _isPreviewActive,
                OnCloseAllRequested,
                () => _workerManager.WorkerProcessId);
            if (!_hookManager.Install())
            {
                AppLogger.Warn("Installazione dell'hook per QuickLook non riuscita. L'anteprima da Explorer potrebbe non funzionare.");
            }

            // Avvia controllo automatico aggiornamenti in background
            ScheduleAutomaticUpdateCheck();

            return true;
        }

        private string GetActiveHotkeyLabel()
        {
            if (!string.IsNullOrWhiteSpace(_activeFallbackHotkeyLabel))
            {
                return _activeFallbackHotkeyLabel;
            }

            string? configured;
            lock (_configLock)
            {
                configured = _config.SpotlightHotkeyText;
            }
            return !string.IsNullOrWhiteSpace(configured)
                ? configured
                : $"Alt + {LocalizationService.Get("Settings_Key_Space")}";
        }

        private void OnQuickLookPreviewRequested(string filePath)
        {
            AppLogger.Info($"Richiesta anteprima QuickLook da Explorer per: {filePath}");
            Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
            _isPreviewActive = true;
            _ = Task.Run(async () =>
            {
                bool ok = await _workerManager.ShowPreviewAsync(filePath);
                if (!ok)
                {
                    _isPreviewActive = false;
                    AppLogger.Warn($"Impossibile mostrare l'anteprima per {filePath}.");
                }
            });
        }

        private void OnQuickLookCloseRequested()
        {
            AppLogger.Info("Richiesta chiusura rapida QuickLook da hook di tastiera.");
            _isPreviewActive = false;
            StopDynamicTracking();
            _ = Task.Run(async () =>
            {
                await _workerManager.HidePreviewAsync();
            });
        }

        private void OnCloseAllRequested()
        {
            AppLogger.Info("Richiesta chiusura unificata (CloseAll) da hook di tastiera Escape.");
            _isPreviewActive = false;
            StopDynamicTracking();
            _ = Task.Run(async () =>
            {
                await _workerManager.CloseAllAsync();
            });
        }

        private void StartDynamicTracking(string initialPath)
        {
            CancellationToken token;
            lock (_trackingLock)
            {
                if (_isDisposed) return;

                if (_dynamicTrackingCts != null)
                {
                    try { _dynamicTrackingCts.Cancel(); } catch { }
                    try { _dynamicTrackingCts.Dispose(); } catch { }
                    _dynamicTrackingCts = null;
                }

                _dynamicTrackingCts = new CancellationTokenSource();
                token = _dynamicTrackingCts.Token;
            }

            string trackedPath = initialPath;

            var thread = new Thread(() =>
            {
                AppLogger.Info($"Avvio monitoraggio dinamico selezione Explorer/Desktop per QuickLook. File iniziale: {initialPath}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        Thread.Sleep(120);
                        if (token.IsCancellationRequested || !_isPreviewActive) break;

                        IntPtr fg = Win32.GetForegroundWindow();
                        if ((AeroSpot.Core.Shell.ExplorerFocusResolver.IsExplorerOrDesktop(fg) ||
                             AeroSpot.Core.Shell.ExplorerFocusResolver.IsFileDialog(fg)) &&
                            !AeroSpot.Core.Shell.ExplorerFocusResolver.IsUserTypingOrRenaming(fg))
                        {
                            if (AeroSpot.Core.Shell.ExplorerFocusResolver.TryGetFocusedPath(fg, out string? newPath) && !string.IsNullOrEmpty(newPath))
                            {
                                if (token.IsCancellationRequested || !_isPreviewActive) break;

                                if (!string.Equals(newPath, trackedPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    trackedPath = newPath;
                                    AppLogger.Info($"Selezione dinamica cambiata in Explorer: {newPath}");
                                    Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
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
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void StopDynamicTracking()
        {
            lock (_trackingLock)
            {
                if (_dynamicTrackingCts != null)
                {
                    try { _dynamicTrackingCts.Cancel(); } catch { }
                    try { _dynamicTrackingCts.Dispose(); } catch { }
                    _dynamicTrackingCts = null;
                }
            }
        }

        private void RegisterHotkeys()
        {
            if (_registeredHotkeyId != 0)
            {
                Win32.UnregisterHotKey(_hWnd, _registeredHotkeyId);
                _registeredHotkeyId = 0;
            }

            uint hotkeyModifiers;
            uint hotkeyKey;
            uint fallbackModifiers;
            uint fallbackKey;
            uint emergencyKey;
            lock (_configLock)
            {
                hotkeyModifiers = _config.HotkeyModifiers;
                hotkeyKey = _config.HotkeyKey;
                fallbackModifiers = _config.FallbackHotkeyModifiers;
                fallbackKey = _config.FallbackHotkeyKey;
                emergencyKey = _config.EmergencyHotkeyKey;
            }

            // 1. Prova combinazione primaria (es. Alt + Spazio o Win + Spazio)
            bool ok = Win32.RegisterHotKey(_hWnd, HOTKEY_ID_PRIMARY, hotkeyModifiers, hotkeyKey);
            if (ok)
            {
                _registeredHotkeyId = HOTKEY_ID_PRIMARY;
                _activeFallbackHotkeyLabel = null;
                AppLogger.Info("Hotkey primaria registrata con successo.");
                return;
            }

            int err = Marshal.GetLastWin32Error();
            AppLogger.Warn($"Scorciatoia primaria non disponibile (LastError: {err}). Tentativo con fallback (Ctrl + Spazio)...");

            // 2. Prova combinazione secondaria (Ctrl + Spazio)
            bool fallbackOk = Win32.RegisterHotKey(_hWnd, HOTKEY_ID_FALLBACK, fallbackModifiers, fallbackKey);
            if (fallbackOk)
            {
                _registeredHotkeyId = HOTKEY_ID_FALLBACK;
                _activeFallbackHotkeyLabel = $"Ctrl + {LocalizationService.Get("Settings_Key_Space")}";
                AppLogger.Info("Hotkey di fallback (Ctrl + Spazio) registrata con successo.");
                _trayManager?.ShowBalloonNotification(
                    LocalizationService.Get("Tray_HotkeyActive"),
                    LocalizationService.Get("Host_Hotkey_Fallback_CtrlSpace"),
                    Win32.NIIF_INFO);
                return;
            }

            // 3. Prova combinazione di emergenza (Alt + F12)
            bool emergencyOk = Win32.RegisterHotKey(_hWnd, HOTKEY_ID_FALLBACK, Win32.MOD_ALT | Win32.MOD_NOREPEAT, emergencyKey);
            if (emergencyOk)
            {
                _registeredHotkeyId = HOTKEY_ID_FALLBACK;
                _activeFallbackHotkeyLabel = "Alt + F12";
                AppLogger.Info("Hotkey di emergenza (Alt + F12) registrata con successo.");
                _trayManager?.ShowBalloonNotification(
                    LocalizationService.Get("Tray_EmergencyHotkey"),
                    LocalizationService.Get("Host_Hotkey_Fallback_AltF12"),
                    Win32.NIIF_WARNING);
            }
            else
            {
                _activeFallbackHotkeyLabel = null;
                AppLogger.Error($"Nessuna hotkey è stata registrata con successo (LastError: {Marshal.GetLastWin32Error()}).");
            }
        }

        private const uint WM_RELOAD_CONFIG = Win32.WM_USER + 102;
        private const uint WM_SHUTDOWN_HOST = Win32.WM_USER + 103;

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

        private void OnShutdownHostRequested()
        {
            AppLogger.Info("Ricevuta richiesta di chiusura completa dell'Host (aggiornamento). Invio WM_SHUTDOWN_HOST...");
            if (_hWnd != IntPtr.Zero)
            {
                Win32.PostMessage(_hWnd, WM_SHUTDOWN_HOST, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void ScheduleAutomaticUpdateCheck()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string currentVer = AeroSpot.Core.Services.UpdateService.CurrentVersion;
                    string? upgradedFrom = null;
                    bool shouldCheck;

                    lock (_configLock)
                    {
                        if (string.IsNullOrWhiteSpace(_config.LastKnownVersion))
                        {
                            _config.LastKnownVersion = currentVer;
                            _config.Save();
                        }
                        else if (AeroSpot.Core.Services.UpdateService.IsNewerVersion(currentVer, _config.LastKnownVersion))
                        {
                            upgradedFrom = _config.LastKnownVersion;
                            _config.LastKnownVersion = currentVer;
                            _config.Save();
                        }

                        shouldCheck = AeroSpot.Core.Services.UpdateService.ShouldCheckForUpdates(_config);
                    }

                    if (upgradedFrom != null)
                    {
                        AppLogger.Info($"Rilevato aggiornamento completato: v{upgradedFrom} -> v{currentVer}. Invio pillola di conferma...");
                        await Task.Delay(3000).ConfigureAwait(false);
                        await _workerManager.SendUpdateNotificationAsync($"UPDATED|{currentVer}").ConfigureAwait(false);
                    }

                    if (!shouldCheck)
                    {
                        return;
                    }

                    // Tentativi progressivi (10s -> 45s -> 120s dall'avvio) per gestire reti/Wi-Fi non ancora pronte al boot
                    int[] retryDelaysMs = { 10000, 35000, 75000 };
                    for (int attempt = 0; attempt < retryDelaysMs.Length; attempt++)
                    {
                        await Task.Delay(retryDelaysMs[attempt]).ConfigureAwait(false);
                        if (_isDisposed) return;

                        var result = await AeroSpot.Core.Services.UpdateService.CheckForUpdatesAsync(ct: CancellationToken.None).ConfigureAwait(false);
                        if (result.IsSuccess)
                        {
                            lock (_configLock)
                            {
                                _config.LastUpdateCheckUtc = DateTime.UtcNow;
                                _config.Save();
                            }

                            if (result.HasUpdate)
                            {
                                AppLogger.Info($"UpdateService: Trovato aggiornamento automatico v{result.LatestVersion}!");
                                _trayManager?.ShowBalloonNotification(
                                    LocalizationService.Get("Update_Notification_Title"),
                                    LocalizationService.Get("Update_Notification_Available", result.LatestVersion),
                                    Win32.NIIF_INFO);

                                string payload = string.Join("|",
                                    result.LatestVersion ?? string.Empty,
                                    result.DownloadUrl ?? string.Empty,
                                    result.ReleaseUrl ?? string.Empty,
                                    result.AssetFileName ?? string.Empty,
                                    result.Sha256SumsUrl ?? string.Empty);

                                await _workerManager.SendUpdateNotificationAsync(payload).ConfigureAwait(false);
                            }

                            return;
                        }

                        AppLogger.Warn($"UpdateService: Tentativo {attempt + 1}/{retryDelaysMs.Length} fallito ({result.ErrorMessage}).");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Verifica automatica aggiornamenti: {ex.Message}");
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
            var reloaded = AppConfig.Load();
            lock (_configLock)
            {
                _config = reloaded;
            }
            _workerManager.UpdateConfig(reloaded);
            _trayManager?.UpdateConfig(reloaded);
            LocalizationService.Initialize(reloaded.Language);
            RegisterHotkeys();

            // Riconfigura hook se il tasto o i modificatori di QuickLook sono cambiati
            _hookManager?.Dispose();
            _hookManager = new KeyboardHookManager(
                reloaded.QuickLookKey,
                reloaded.QuickLookModifiers,
                OnQuickLookPreviewRequested,
                OnQuickLookCloseRequested,
                () => _isPreviewActive,
                OnCloseAllRequested,
                () => _workerManager.WorkerProcessId);
            _hookManager.Install();

            if (reloaded.KeepWorkerWarm)
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
            if (msg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0)
            {
                AppLogger.Info("Messaggio TaskbarCreated ricevuto: ripristino icona nella System Tray...");
                _trayManager?.AddTrayIcon(LocalizationService.Get("Tray_Tooltip"), forceReadd: true);
                return IntPtr.Zero;
            }

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

                case WM_SHUTDOWN_HOST:
                    OnExitRequested();
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
                    else if (mouseMsg == 0x0405) // NIN_BALLOONUSERCLICK
                    {
                        OnSettingsRequested();
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
                    _trayManager = null;
                    _hWnd = IntPtr.Zero;
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
            _trayManager?.Dispose();
            _trayManager = null;
            if (_hWnd != IntPtr.Zero)
            {
                if (Win32.GetCurrentThreadId() == _uiThreadId)
                {
                    if (_registeredHotkeyId != 0)
                    {
                        Win32.UnregisterHotKey(_hWnd, _registeredHotkeyId);
                        _registeredHotkeyId = 0;
                    }
                    Win32.DestroyWindow(_hWnd);
                }
                _hWnd = IntPtr.Zero;
            }
        }
    }
}
