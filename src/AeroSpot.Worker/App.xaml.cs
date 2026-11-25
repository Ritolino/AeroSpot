using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AeroSpot.Core.Config;
using AeroSpot.Core.IPC;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Core.Search;
using AeroSpot.Worker.Services;

namespace AeroSpot.Worker
{
    public partial class App : Application
    {
        public static bool IsShuttingDown { get; private set; }
        public static MainWindow? MainLauncherWindow => (Current as App)?._mainWindow;

        /// <summary>
        /// Garantisce la disponibilità della risorsa SlimDarkScrollBar quando una finestra viene istanziata
        /// in contesti isolati (es. unit test senza App.xaml caricato).
        /// </summary>
        internal static void EnsureSharedScrollBarResources(ResourceDictionary targetResources)
        {
            if (Current?.Resources.Contains("SlimDarkScrollBar") == true)
            {
                return;
            }

            if (!targetResources.Contains("SlimDarkScrollBar"))
            {
                targetResources["SlimDarkScrollBar"] = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            }
        }

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

            try
            {
                AppLogger.Initialize("Worker");
                AppLogger.Info("AeroSpot.Worker avviato (processo unificato Launcher + QuickLook + Impostazioni).");

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
                            IconService.TrimCache();
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
                    StartIdleCountdown();
                }
                else
                {
                    await _ipcClient.SendMessageAsync(new IpcMessage(IpcMessageType.WorkerReady, $"PID={Process.GetCurrentProcess().Id}"));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Errore fatale durante l'avvio di AeroSpot.Worker", ex);
                Shutdown(1);
            }
        }

        private void OnIpcConnected()
        {
            AppLogger.Info("Worker: Connessione stabilita con l'Host.");
        }

        private void OnIpcDisconnected()
        {
            AppLogger.Info("Worker: Host disconnesso.");
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
                    case IpcMessageType.ShowUpdateNotification:
                        HandleUpdateNotification(msg.Payload);
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

        public async Task RequestFullSuiteShutdownForUpdateAsync()
        {
            AppLogger.Info("Invio comando ShutdownHost all'Host per applicazione aggiornamento...");
            try
            {
                if (_ipcClient != null && _ipcClient.IsConnected)
                {
                    await _ipcClient.SendMessageAsync(new IpcMessage(IpcMessageType.ShutdownHost));
                    await Task.Delay(150);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Errore invio ShutdownHost: {ex.Message}");
            }

            Dispatcher.Invoke(() => ShutdownWorker("Aggiornamento automatico in corso"));
        }

        private void HandleUpdateNotification(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;

            try
            {
                if (payload.StartsWith("UPDATED|", StringComparison.OrdinalIgnoreCase))
                {
                    string ver = payload.Substring("UPDATED|".Length).Trim();
                    var toast = FloatingNotificationWindow.Spawn(
                        AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Updated_Title", ver),
                        "✓");
                    toast.SetSuccess(
                        AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Updated_Title", ver),
                        AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Updated_Detail"),
                        resultFilePath: null,
                        autoCloseMs: 5000);
                    return;
                }

                string[] parts = payload.Split('|');
                string latestVer = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                string downloadUrl = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                string releaseUrl = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                string assetFileName = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                string sha256SumsUrl = parts.Length > 4 ? parts[4].Trim() : string.Empty;

                if (string.IsNullOrEmpty(latestVer)) return;

                string title = AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Available_Title", latestVer);
                string detail = AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Available_Detail");
                string btnText = AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Btn_UpdateNow");

                var updateToast = FloatingNotificationWindow.Spawn(title, "★");
                updateToast.SetAction(title, detail, btnText, () =>
                {
                    if (string.IsNullOrEmpty(downloadUrl) || string.Equals(downloadUrl, releaseUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(releaseUrl))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true });
                            }
                            catch { }
                        }
                        updateToast.Close();
                        return;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string dlTitle = AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Downloading_Title", latestVer);
                            updateToast.UpdateProgress(0, AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Downloading", 0), dlTitle, "⬇️");

                            var progress = new Progress<double>(pct =>
                            {
                                updateToast.UpdateProgress(
                                    pct,
                                    AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Downloading", (int)pct),
                                    dlTitle,
                                    "⬇️");
                            });

                            string fileName = !string.IsNullOrWhiteSpace(assetFileName)
                                ? assetFileName
                                : $"AeroSpot-v{latestVer}-win-x64-portable.zip";

                            string downloadedPath = await AeroSpot.Core.Services.UpdateService.DownloadUpdateAsync(
                                downloadUrl, fileName, progress);

                            updateToast.UpdateProgress(
                                -1,
                                AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Installing_Detail"),
                                AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Installing_Title"),
                                "🚀");

                            await AeroSpot.Core.Services.UpdateService.PrepareAndApplyUpdateAsync(
                                downloadedPath,
                                string.IsNullOrWhiteSpace(sha256SumsUrl) ? null : sha256SumsUrl,
                                fileName,
                                onShutdownRequested: () => { _ = RequestFullSuiteShutdownForUpdateAsync(); });
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Errore durante l'aggiornamento 1-clic dalla pillola", ex);
                            updateToast.SetError(
                                AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Error_Title"),
                                ex.Message);
                        }
                    });
                }, icon: "★", autoCloseMs: 20000);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"HandleUpdateNotification error: {ex.Message}");
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
                return;
            }

            _remainingIdleSeconds = _config.WorkerIdleTimeoutSeconds;
            _idleTimer?.Start();
        }

        private void StopIdleCountdown()
        {
            _idleTimer?.Stop();
            _remainingIdleSeconds = _config.WorkerIdleTimeoutSeconds;
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_config.KeepWorkerWarm)
            {
                _idleTimer?.Stop();
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

            if (_remainingIdleSeconds <= 0)
            {
                _idleTimer?.Stop();
                AppLogger.Info($"Worker: Timeout di inattività raggiunto ({_config.WorkerIdleTimeoutSeconds}s). Arresto automatico per liberare RAM.");
                ShutdownWorker("Timeout inattività");
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

    /// <summary>
    /// Converte ViewportSize per WPF Track in modo da garantire una dimensione minima reale del Thumb
    /// (WPF Track ignora Thumb.MinHeight/MinWidth e usa solo ~8px di sistema, tagliando il Thumb con LayoutClip).
    /// </summary>
    public sealed class MinThumbViewportConverter : System.Windows.Data.IMultiValueConverter
    {
        public double MinThumbLength { get; set; } = 44.0;

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 6)
            {
                return double.NaN;
            }

            if (values[0] is not double viewportSize ||
                values[1] is not double maximum ||
                values[2] is not double minimum ||
                values[3] is not double actualHeight ||
                values[4] is not double actualWidth ||
                values[5] is not Orientation orientation)
            {
                return double.NaN;
            }

            if (double.IsNaN(viewportSize) || double.IsInfinity(viewportSize) || viewportSize <= 0)
            {
                return viewportSize;
            }

            double range = maximum - minimum;
            double trackLength = orientation == Orientation.Vertical ? actualHeight : actualWidth;
            double minLen = MinThumbLength;

            if (range <= 0 || trackLength <= minLen)
            {
                return viewportSize;
            }

            // Formula esatta inversa di WPF Track: thumbLength = trackLength * V / (range + V)
            double minViewport = (minLen * range) / (trackLength - minLen);
            return Math.Max(viewportSize, minViewport);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
