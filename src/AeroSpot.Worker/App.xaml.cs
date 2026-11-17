using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AeroSpot.Core.Config;
using AeroSpot.Core.IPC;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Core.Search;

namespace AeroSpot.Worker
{
    public partial class App : Application
    {
        public static bool IsShuttingDown { get; private set; }
        public static MainWindow? MainLauncherWindow => (Current as App)?._mainWindow;

        private AppConfig _config = null!;
        private NamedPipeWorkerClient? _ipcClient;
        private MainWindow? _mainWindow;
        private PreviewWindow? _previewWindow;
        private SettingsWindow? _settingsWindow;
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _ramGraceTimer;
        private int _remainingIdleSeconds;
        private long _lastShowMainWindowTicks;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppLogger.Initialize("Worker");
            AppLogger.Info("AeroSpot.Worker avviato (processo unificato Launcher + QuickLook + Impostazioni).");

            StartupManager.EnsureBrowserEmulation();
            _config = AppConfig.Load();
            AeroSpot.Core.Localization.LocalizationService.Initialize(_config.Language);
            PathFilter.Configure(_config.IncludedDrives, _config.ExcludedDirectoryNames);

            string? customPipeName = null;
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--timeout" && i + 1 < e.Args.Length && int.TryParse(e.Args[i + 1], out int t) && t > 0)
                {
                    _config.WorkerIdleTimeoutSeconds = t;
                    AppLogger.Info($"Timeout di inattività configurato da parametro: {t}s");
                }
                else if (e.Args[i] == "--no-keep-warm")
                {
                    _config.KeepWorkerWarm = false;
                    AppLogger.Info("Parametro --no-keep-warm: KeepWorkerWarm disabilitato.");
                }
                else if (e.Args[i] == "--keep-warm")
                {
                    _config.KeepWorkerWarm = true;
                    AppLogger.Info("Parametro --keep-warm: KeepWorkerWarm abilitato.");
                }
                else if (e.Args[i] == "--pipe" && i + 1 < e.Args.Length)
                {
                    customPipeName = e.Args[i + 1];
                    AppLogger.Info($"Pipe IPC personalizzata: {customPipeName}");
                }
            }

            _remainingIdleSeconds = _config.WorkerIdleTimeoutSeconds;

            // Inizializza la finestra del Launcher
            _mainWindow = new MainWindow();
            _mainWindow.HideRequested += HideMainWindow;
            _mainWindow.CloseAllRequested += OnPreviewCloseAllRequested;
            _mainWindow.PreviewRequested += (path) => ShowPreviewWindow(path, isFromSpotlight: true);
            _mainWindow.PreviewScrollRequested += (delta) => _previewWindow?.ScrollContent(delta);
            _mainWindow.SettingsRequested += ShowSettingsWindow;

            // Inizializza la finestra di QuickLook Preview
            _previewWindow = new PreviewWindow();
            _previewWindow.HideRequested += OnPreviewWindowHidden;
            _previewWindow.CloseAllRequested += OnPreviewCloseAllRequested;
            _previewWindow.NavigateSelectionRequested += OnPreviewNavigateSelection;
            _previewWindow.PreviewOpened += (path) =>
            {
                if (_ipcClient != null && _ipcClient.IsConnected)
                {
                    _ = _ipcClient.SendMessageAsync(new IpcMessage(IpcMessageType.PreviewOpened, path));
                }
            };
            _previewWindow.PreviewClosed += () =>
            {
                if (_ipcClient != null && _ipcClient.IsConnected)
                {
                    _ = _ipcClient.SendMessageAsync(new IpcMessage(IpcMessageType.PreviewClosed));
                }
            };
            _previewWindow.FileLaunched += (path) =>
            {
                _mainWindow?.ViewModel.RecordRecentItem(path);
            };

            if (Array.Exists(e.Args, a => string.Equals(a, "--benchmark-cold-start", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(0);
                return;
            }

            // Setup idle timer
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _idleTimer.Tick += IdleTimer_Tick;

            // Timer di grazia RAM (10 secondi dopo la chiusura dell'anteprima prima di forzare il cleanup)
            _ramGraceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _ramGraceTimer.Tick += (s, ev) =>
            {
                _ramGraceTimer.Stop();
                bool any = (_mainWindow != null && _mainWindow.IsVisible) 
                    || (_previewWindow != null && _previewWindow.IsPreviewVisible)
                    || (_settingsWindow != null && _settingsWindow.IsVisible);
                if (!any)
                {
                    try
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        Win32.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                        AppLogger.Info("RamGraceTimer: Memoria bonificata dopo 10s di inattività (~25-35MB).");
                    }
                    catch { }
                }
            };

            // Connect IPC
            _ipcClient = new NamedPipeWorkerClient(customPipeName);
            _ipcClient.Connected += OnIpcConnected;
            _ipcClient.Disconnected += OnIpcDisconnected;
            _ipcClient.MessageReceived += OnIpcMessageReceived;

            bool connected = await _ipcClient.ConnectAsync(3000);
            if (!connected)
            {
                AppLogger.Warn("AeroSpot.Worker non è riuscito a connettersi all'Host. Modalità autonoma/debug.");
                _mainWindow.SetIpcStatus("Non connesso all'Host (Modalità autonoma)");
                StartIdleCountdown();
            }
            else
            {
                _mainWindow.SetIpcStatus("Connesso all'Host");
                await _ipcClient.SendMessageAsync(new IpcMessage(IpcMessageType.WorkerReady, $"PID={Process.GetCurrentProcess().Id}"));
            }
        }

        private void OnIpcConnected()
        {
            AppLogger.Info("Worker: Connessione stabilita con l'Host.");
            _mainWindow?.SetIpcStatus("Connesso all'Host");
        }

        private void OnIpcDisconnected()
        {
            AppLogger.Info("Worker: Host disconnesso.");
            _mainWindow?.SetIpcStatus("Disconnesso dall'Host");
            Dispatcher.InvokeAsync(() => ShutdownWorker("Host disconnesso"));
        }

        private void OnIpcMessageReceived(IpcMessage msg)
        {
            AppLogger.Info($"Worker: Ricevuto messaggio IPC Type={msg.Type}, Payload={msg.Payload}");
            Dispatcher.InvokeAsync(() =>
            {
                switch (msg.Type)
                {
                    case IpcMessageType.ShowLauncher:
                        ShowMainWindow();
                        break;
                    case IpcMessageType.HideLauncher:
                        HideMainWindow();
                        break;
                    case IpcMessageType.ShowPreview:
                        ShowPreviewWindow(msg.Payload);
                        break;
                    case IpcMessageType.HidePreview:
                        _previewWindow?.HidePreview();
                        break;
                    case IpcMessageType.CloseAll:
                        OnPreviewCloseAllRequested();
                        break;
                    case IpcMessageType.ShowSettings:
                        ShowSettingsWindow();
                        break;
                    case IpcMessageType.CloseWorker:
                        ShutdownWorker("Richiesta di chiusura dall'Host");
                        break;
                    case IpcMessageType.Ping:
                        _ = _ipcClient?.SendMessageAsync(new IpcMessage(IpcMessageType.Pong));
                        break;
                }
            });
        }

        private void ShowMainWindow()
        {
            _ramGraceTimer?.Stop();
            StopIdleCountdown();
            if (_mainWindow != null)
            {
                if (_mainWindow.IsVisible)
                {
                    // Ignora impulsi doppi di ShowLauncher ricevuti entro 180ms dall'apertura
                    if (Environment.TickCount64 - _lastShowMainWindowTicks < 180)
                    {
                        AppLogger.Info("ShowMainWindow: ignorato impulso ShowLauncher ravvicinato (<180ms dall'apertura).");
                        return;
                    }
                    HideMainWindow();
                    return;
                }

                _lastShowMainWindowTicks = Environment.TickCount64;
                _mainWindow.ShowOnActiveMonitor();
                _mainWindow.SetIdleStatus("Inattivo (Finestra visibile e attiva)");
            }
        }

        private void HideMainWindow()
        {
            if (_mainWindow != null)
            {
                _mainWindow.ViewModel?.OnSpotlightHidden();
                _mainWindow.PrepareForHide();
                _mainWindow.Hide();
            }
            CheckIdleCountdown();

            bool any = (_mainWindow != null && _mainWindow.IsVisible) 
                || (_previewWindow != null && _previewWindow.IsPreviewVisible)
                || (_settingsWindow != null && _settingsWindow.IsVisible);
            if (!any)
            {
                _ramGraceTimer?.Stop();
                _ramGraceTimer?.Start();
            }
        }

        private void ShowPreviewWindow(string filePath, bool isFromSpotlight = false)
        {
            _ramGraceTimer?.Stop();
            StopIdleCountdown();
            _ = _previewWindow?.ShowPreviewForFileAsync(filePath, isFromSpotlight);
        }

        private void OnPreviewNavigateSelection(int delta)
        {
            if (_mainWindow != null && _mainWindow.IsVisible)
            {
                if (delta < 0) _mainWindow.ViewModel.MoveSelectionUp();
                else _mainWindow.ViewModel.MoveSelectionDown();

                var item = _mainWindow.ViewModel.SelectedItem;
                if (item != null && !string.IsNullOrWhiteSpace(item.TargetPath))
                {
                    _ = _previewWindow?.ShowPreviewForFileAsync(item.TargetPath, isFromSpotlight: true);
                }
            }
        }

        private void OnPreviewWindowHidden()
        {
            if (_mainWindow != null && _mainWindow.IsVisible)
            {
                _mainWindow.IsPreviewActive = false;
                _mainWindow.Activate();
                _mainWindow.SearchBox.Focus();
                System.Windows.Input.Keyboard.Focus(_mainWindow.SearchBox);
            }

            // Avvia periodo di grazia di 10 secondi per consentire navigazione rapida senza riallocazioni
            _ramGraceTimer?.Stop();
            _ramGraceTimer?.Start();

            CheckIdleCountdown();
        }

        private void OnPreviewCloseAllRequested()
        {
            if (_previewWindow != null)
            {
                _previewWindow.HidePreview();
            }
            HideMainWindow();
        }

        private void ShowSettingsWindow()
        {
            _ramGraceTimer?.Stop();
            StopIdleCountdown();
            HideMainWindow();

            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.ConfigSaved += OnConfigSaved;
                _settingsWindow.IsVisibleChanged += (s, ev) =>
                {
                    if (!_settingsWindow.IsVisible) CheckIdleCountdown();
                };
            }

            _settingsWindow.ResetStatusMessage();
            _settingsWindow.ReloadSettings();
            _settingsWindow.Show();
            _settingsWindow.Activate();

            IntPtr hWnd = new System.Windows.Interop.WindowInteropHelper(_settingsWindow).EnsureHandle();
            Win32.SetForegroundWindow(hWnd);
        }

        private async void OnConfigSaved(AppConfig newConfig)
        {
            _config = newConfig;
            _remainingIdleSeconds = _config.WorkerIdleTimeoutSeconds;
            AeroSpot.Core.Localization.LocalizationService.Initialize(_config.Language);
            PathFilter.Configure(_config.IncludedDrives, _config.ExcludedDirectoryNames);
            _mainWindow?.ViewModel?.ReloadConfig();
            _mainWindow?.UpdateConfig(_config);
            _previewWindow?.UpdateConfig(_config);
            if (_config.KeepWorkerWarm)
            {
                _idleTimer?.Stop();
                _mainWindow?.SetIdleStatus("Attivo (Sempre in memoria)");
            }
            else
            {
                CheckIdleCountdown();
            }
            AppLogger.Info("Configurazione applicata nel Worker. Invio notifica ConfigReloaded all'Host...");
            if (_ipcClient != null && _ipcClient.IsConnected)
            {
                await _ipcClient.SendMessageAsync(new IpcMessage(IpcMessageType.ConfigReloaded));
            }
        }

        private void CheckIdleCountdown()
        {
            bool anyVisible = (_mainWindow != null && _mainWindow.IsVisible) 
                || (_previewWindow != null && _previewWindow.IsPreviewVisible)
                || (_settingsWindow != null && _settingsWindow.IsVisible);
            if (!anyVisible)
            {
                StartIdleCountdown();
            }
        }

        private void StartIdleCountdown()
        {
            if (_config.KeepWorkerWarm)
            {
                _idleTimer?.Stop();
                _mainWindow?.SetIdleStatus("Attivo (Sempre in memoria)");
                return;
            }

            _remainingIdleSeconds = _config.WorkerIdleTimeoutSeconds;
            _idleTimer?.Start();
            UpdateIdleText();
        }

        private void StopIdleCountdown()
        {
            _idleTimer?.Stop();
            _remainingIdleSeconds = _config.WorkerIdleTimeoutSeconds;
            _mainWindow?.SetIdleStatus("Inattivo (Finestra attiva)");
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_config.KeepWorkerWarm)
            {
                _idleTimer?.Stop();
                _mainWindow?.SetIdleStatus("Attivo (Sempre in memoria)");
                return;
            }

            bool anyVisible = (_mainWindow != null && _mainWindow.IsVisible) 
                || (_previewWindow != null && _previewWindow.IsPreviewVisible)
                || (_settingsWindow != null && _settingsWindow.IsVisible);

            if (anyVisible)
            {
                StopIdleCountdown();
                return;
            }

            _remainingIdleSeconds--;
            UpdateIdleText();

            if (_remainingIdleSeconds <= 0)
            {
                _idleTimer?.Stop();
                AppLogger.Info($"Worker: Timeout di inattività raggiunto ({_config.WorkerIdleTimeoutSeconds}s). Arresto automatico per liberare RAM.");
                ShutdownWorker("Timeout inattività");
            }
        }

        private void UpdateIdleText()
        {
            if (_config.KeepWorkerWarm)
            {
                _mainWindow?.SetIdleStatus("Attivo (Sempre in memoria)");
            }
            else
            {
                _mainWindow?.SetIdleStatus($"In corso: arresto tra {_remainingIdleSeconds}s se inattivo");
            }
        }

        private void ShutdownWorker(string reason)
        {
            if (IsShuttingDown) return;
            IsShuttingDown = true;
            AppLogger.Info($"Worker in fase di arresto. Motivo: {reason}");

            try
            {
                _ramGraceTimer?.Stop();
                _idleTimer?.Stop();
                _mainWindow?.ViewModel?.Dispose();
                _previewWindow?.HidePreview();
                _settingsWindow?.Hide();
                _ipcClient?.Disconnect();
                _ipcClient?.Dispose();
            }
            catch { }

            Shutdown(0);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IsShuttingDown = true;
            try
            {
                _ramGraceTimer?.Stop();
                _mainWindow?.ViewModel?.Dispose();
                _previewWindow?.HidePreview();
                _settingsWindow?.Hide();
                _ipcClient?.Dispose();
            }
            catch { }
            base.OnExit(e);
        }
    }
}
