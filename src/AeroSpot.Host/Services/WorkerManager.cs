using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.IPC;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;

namespace AeroSpot.Host.Services
{
    public sealed class WorkerManager : IDisposable
    {
        private AppConfig _config;
        private readonly NamedPipeHostServer _pipeServer;
        private readonly string? _customPipeName;
        private Process? _workerProcess;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _showLauncherLock = new SemaphoreSlim(1, 1);
        private int _pendingShowLauncherCount;
        private TaskCompletionSource<bool>? _ipcConnectedTcs;
        private volatile bool _isDisposed;

        public bool IsWorkerRunning
        {
            get
            {
                lock (_lock)
                {
                    if (_workerProcess == null) return false;
                    try { return !_workerProcess.HasExited; }
                    catch { return false; }
                }
            }
        }

        public int WorkerProcessId
        {
            get
            {
                lock (_lock)
                {
                    if (_workerProcess == null) return 0;
                    try { return _workerProcess.HasExited ? 0 : _workerProcess.Id; }
                    catch { return 0; }
                }
            }
        }

        public bool IsIpcConnected => _pipeServer.IsClientConnected;

        public event Action<string>? WorkerStatusChanged;
        public event Action? ConfigReloadRequested;
        public event Action<string>? PreviewOpened;
        public event Action? PreviewClosed;
        public event Action? ShutdownHostRequested;

        public void UpdateConfig(AppConfig config)
        {
            lock (_lock)
            {
                _config = config;
            }
        }

        public WorkerManager(AppConfig config, string? customPipeName = null)
        {
            _config = config;
            _customPipeName = customPipeName;
            _pipeServer = new NamedPipeHostServer(customPipeName);
            _pipeServer.ClientConnected += OnClientConnected;
            _pipeServer.ClientDisconnected += OnClientDisconnected;
            _pipeServer.MessageReceived += OnMessageReceived;
            _pipeServer.Start();
        }

        public string GetStatusString()
        {
            lock (_lock)
            {
                if (_workerProcess == null) return LocalizationService.Get("Tray_Worker_Idle");
                bool running = false;
                int pid = 0;
                try
                {
                    running = !_workerProcess.HasExited;
                    if (running) pid = _workerProcess.Id;
                }
                catch { running = false; }

                if (!running) return LocalizationService.Get("Tray_Worker_Idle");
                if (IsIpcConnected) return $"{LocalizationService.Get("Tray_Worker_Active")} (PID: {pid})";
                return LocalizationService.Get("Tray_Worker_Starting");
            }
        }

        private void OnClientConnected()
        {
            TaskCompletionSource<bool>? tcs;
            lock (_lock)
            {
                tcs = _ipcConnectedTcs;
            }
            tcs?.TrySetResult(true);
            AppLogger.Info("IPC: Worker connesso alla Named Pipe.");
            WorkerStatusChanged?.Invoke(GetStatusString());
        }

        private void OnClientDisconnected()
        {
            AppLogger.Info("IPC: Worker disconnesso dalla Named Pipe.");
            WorkerStatusChanged?.Invoke(GetStatusString());
        }

        private void OnMessageReceived(IpcMessage msg)
        {
            AppLogger.Info($"IPC: Ricevuto messaggio dal Worker: Type={msg.Type}, Payload={msg.Payload}");
            switch (msg.Type)
            {
                case IpcMessageType.WorkerReady:
                    break;
                case IpcMessageType.WorkerState:
                    WorkerStatusChanged?.Invoke(msg.Payload);
                    break;
                case IpcMessageType.CloseWorker:
                    // Worker signals it is shutting down due to timeout
                    AppLogger.Info("Worker ha segnalato arresto per inattività.");
                    break;
                case IpcMessageType.ConfigReloaded:
                    AppLogger.Info("IPC: Notifica ConfigReloaded ricevuta dal Worker.");
                    ConfigReloadRequested?.Invoke();
                    break;
                case IpcMessageType.PreviewOpened:
                    PreviewOpened?.Invoke(msg.Payload);
                    break;
                case IpcMessageType.PreviewClosed:
                    PreviewClosed?.Invoke();
                    break;
                case IpcMessageType.ShutdownHost:
                    AppLogger.Info("IPC: Richiesta ShutdownHost ricevuta dal Worker.");
                    ShutdownHostRequested?.Invoke();
                    break;
            }
        }

        public async Task<bool> EnsureWorkerStartedAsync(int timeoutMs = 25000)
        {
            TaskCompletionSource<bool> waitTcs;
            lock (_lock)
            {
                if (_isDisposed) return false;

                if (_workerProcess != null)
                {
                    bool exited = false;
                    try { exited = _workerProcess.HasExited; } catch { exited = true; }
                    if (exited)
                    {
                        try { _workerProcess.Dispose(); } catch { }
                        _workerProcess = null;
                    }
                }

                bool workerRunning = _workerProcess != null;
                if (workerRunning && IsIpcConnected)
                {
                    return true;
                }

                if (!workerRunning)
                {
                    string workerExe = ResolveWorkerExecutable();
                    if (!File.Exists(workerExe))
                    {
                        AppLogger.Error($"Eseguibile del Worker non trovato al percorso: {workerExe}");
                        return false;
                    }

                    AppLogger.Info($"Avvio processo Worker: {workerExe}");
                    string launchArgs = $"--timeout {_config.WorkerIdleTimeoutSeconds}";
                    launchArgs += _config.KeepWorkerWarm ? " --keep-warm" : " --no-keep-warm";
                    if (!string.IsNullOrEmpty(_customPipeName))
                    {
                        launchArgs += $" --pipe {_customPipeName}";
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = workerExe,
                        Arguments = launchArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(workerExe)
                    };

                    _ipcConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    try
                    {
                        var proc = Process.Start(psi);
                        _workerProcess = proc;
                        if (proc != null)
                        {
                            proc.EnableRaisingEvents = true;
                            proc.Exited += (s, e) =>
                            {
                                var exitedProc = s as Process;
                                int exitCode = -1;
                                if (exitedProc != null)
                                {
                                    try { exitCode = exitedProc.ExitCode; } catch { }
                                }
                                AppLogger.Info($"Processo Worker terminato. ExitCode: {exitCode}");
                                TaskCompletionSource<bool>? pendingTcs = null;
                                lock (_lock)
                                {
                                    if (ReferenceEquals(_workerProcess, exitedProc))
                                    {
                                        try { _workerProcess?.Dispose(); } catch { }
                                        _workerProcess = null;
                                        pendingTcs = _ipcConnectedTcs;
                                    }
                                }
                                pendingTcs?.TrySetResult(false);
                                WorkerStatusChanged?.Invoke(GetStatusString());
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Errore durante l'avvio del processo Worker.", ex);
                        _ipcConnectedTcs = null;
                        return false;
                    }
                }
                else if (_ipcConnectedTcs == null || _ipcConnectedTcs.Task.IsCompleted)
                {
                    _ipcConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                waitTcs = _ipcConnectedTcs!;
            }

            if (IsIpcConnected) return true;

            // Attesa event-driven della connessione IPC senza lanciare eccezioni se il timeout scade
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await waitTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout attesa completato
            }

            return IsIpcConnected;
        }

        public long LastColdStartMs { get; private set; }
        public long LastWarmStartMs { get; private set; }

        public async Task<bool> ShowLauncherAsync()
        {
            if (Interlocked.Increment(ref _pendingShowLauncherCount) > 1)
            {
                AppLogger.Info("Richiesta ShowLauncher ignorata: un'operazione ShowLauncher è già in corso.");
                Interlocked.Decrement(ref _pendingShowLauncherCount);
                return true;
            }

            await _showLauncherLock.WaitAsync().ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            bool wasCold = !IsWorkerRunning || !IsIpcConnected;
            try
            {
                bool ready = await EnsureWorkerStartedAsync().ConfigureAwait(false);
                if (!ready)
                {
                    AppLogger.Warn("Impossibile mostrare il launcher: Worker non pronto.");
                    return false;
                }

                lock (_lock)
                {
                    if (_workerProcess != null && !_workerProcess.HasExited)
                    {
                        try { AeroSpot.Core.Native.Win32.AllowSetForegroundWindow((uint)_workerProcess.Id); } catch { }
                    }
                }

                bool sent = await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.ShowLauncher)).ConfigureAwait(false);
                sw.Stop();
                if (wasCold)
                {
                    LastColdStartMs = sw.ElapsedMilliseconds;
                    AppLogger.Info($"Avvio a FREDDO del Worker completato in {LastColdStartMs} ms.");
                }
                else
                {
                    LastWarmStartMs = sw.ElapsedMilliseconds;
                    AppLogger.Info($"Avvio a CALDO del Worker completato in {LastWarmStartMs} ms.");
                }
                return sent;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.Error("Errore nell'invio del comando ShowLauncher.", ex);
                return false;
            }
            finally
            {
                Interlocked.Decrement(ref _pendingShowLauncherCount);
                _showLauncherLock.Release();
            }
        }

        public async Task<bool> ShowPreviewAsync(string filePath)
        {
            var sw = Stopwatch.StartNew();
            bool wasCold = !IsWorkerRunning || !IsIpcConnected;
            try
            {
                bool ready = await EnsureWorkerStartedAsync().ConfigureAwait(false);
                if (!ready)
                {
                    AppLogger.Warn("Impossibile mostrare l'anteprima: Worker non pronto.");
                    return false;
                }

                lock (_lock)
                {
                    if (_workerProcess != null && !_workerProcess.HasExited)
                    {
                        try { AeroSpot.Core.Native.Win32.AllowSetForegroundWindow((uint)_workerProcess.Id); } catch { }
                    }
                }

                bool sent = await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.ShowPreview, filePath)).ConfigureAwait(false);
                sw.Stop();
                if (wasCold)
                {
                    LastColdStartMs = sw.ElapsedMilliseconds;
                    AppLogger.Info($"Avvio a FREDDO del Worker per anteprima completato in {LastColdStartMs} ms. File: {filePath}");
                }
                else
                {
                    LastWarmStartMs = sw.ElapsedMilliseconds;
                    AppLogger.Info($"Avvio a CALDO del Worker per anteprima completato in {LastWarmStartMs} ms. File: {filePath}");
                }
                return sent;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.Error($"Errore nell'invio del comando ShowPreview per {filePath}.", ex);
                return false;
            }
        }

        public async Task<bool> HidePreviewAsync()
        {
            if (!IsWorkerRunning || !IsIpcConnected) return false;
            try
            {
                return await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.HidePreview)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Errore nell'invio del comando HidePreview.", ex);
                return false;
            }
        }

        public async Task<bool> CloseAllAsync()
        {
            if (!IsWorkerRunning || !IsIpcConnected) return false;
            try
            {
                return await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.CloseAll)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Errore nell'invio del comando CloseAll.", ex);
                return false;
            }
        }

        public async Task<bool> ShowSettingsAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                bool ready = await EnsureWorkerStartedAsync().ConfigureAwait(false);
                if (!ready)
                {
                    AppLogger.Warn("Impossibile mostrare le impostazioni: Worker non pronto.");
                    return false;
                }

                bool sent = await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.ShowSettings)).ConfigureAwait(false);
                sw.Stop();
                AppLogger.Info($"Comando ShowSettings inviato in {sw.ElapsedMilliseconds} ms.");
                return sent;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.Error("Errore nell'invio del comando ShowSettings.", ex);
                return false;
            }
        }

        public async Task<bool> SendUpdateNotificationAsync(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return false;

            try
            {
                bool ready = await EnsureWorkerStartedAsync().ConfigureAwait(false);
                if (!ready)
                {
                    AppLogger.Warn("Impossibile inviare notifica aggiornamento: Worker non pronto.");
                    return false;
                }

                bool sent = await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.ShowUpdateNotification, payload)).ConfigureAwait(false);
                AppLogger.Info($"Comando ShowUpdateNotification inviato (sent={sent}).");
                return sent;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Errore nell'invio del comando ShowUpdateNotification.", ex);
                return false;
            }
        }

        public async Task<bool> StopWorkerAsync(int timeoutMs = 3000)
        {
            Process? proc;
            lock (_lock)
            {
                proc = _workerProcess;
            }

            if (proc == null)
            {
                return true;
            }

            bool alreadyExited = false;
            try { alreadyExited = proc.HasExited; } catch { alreadyExited = true; }
            if (alreadyExited)
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_workerProcess, proc))
                    {
                        try { _workerProcess.Dispose(); } catch { }
                        _workerProcess = null;
                    }
                }
                return true;
            }

            AppLogger.Info("Invio comando CloseWorker al Worker...");
            await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.CloseWorker), timeoutMs).ConfigureAwait(false);

            // Attesa uscita pulita
            bool exited = false;
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    exited = true;
                }
                catch (OperationCanceledException)
                {
                    exited = false;
                }
                catch
                {
                    exited = true;
                }
            }

            if (!exited)
            {
                AppLogger.Warn("Il Worker non e' terminato entro il timeout. Terminazione forzata (Kill)...");
                try
                {
                    proc.Kill();
                }
                catch { }
            }

            lock (_lock)
            {
                if (ReferenceEquals(_workerProcess, proc))
                {
                    try { _workerProcess.Dispose(); } catch { }
                    _workerProcess = null;
                }
            }

            return true;
        }

        private string ResolveWorkerExecutable()
        {
            string baseDir = AppContext.BaseDirectory;
            string exeName = "AeroSpot.Worker.exe";

            // 1. Stessa cartella (produzione o pacchetto unificato)
            string relativePath;
            lock (_lock)
            {
                relativePath = _config.WorkerRelativePath;
            }

            string candidate = Path.Combine(baseDir, relativePath);
            if (File.Exists(candidate))
            {
                AppLogger.Info($"Worker localizzato in: {candidate}");
                return candidate;
            }

            string sameDir = Path.Combine(baseDir, exeName);
            if (!string.Equals(sameDir, candidate, StringComparison.OrdinalIgnoreCase) && File.Exists(sameDir))
            {
                AppLogger.Info($"Worker localizzato in: {sameDir}");
                return sameDir;
            }

            // 2. Percorsi relativi di sviluppo e build noti
            string[] devPaths = new[]
            {
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\src\AeroSpot.Worker\bin\Release\net10.0-windows10.0.19041.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\src\AeroSpot.Worker\bin\Debug\net10.0-windows10.0.19041.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\AeroSpot.Worker\bin\Release\net10.0-windows10.0.19041.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\AeroSpot.Worker\bin\Debug\net10.0-windows10.0.19041.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\AeroSpot.Worker\bin\Release\net10.0-windows10.0.19041.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\AeroSpot.Worker\bin\Debug\net10.0-windows10.0.19041.0", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\src\AeroSpot.Worker\bin\Release\net10.0-windows", exeName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\src\AeroSpot.Worker\bin\Debug\net10.0-windows", exeName))
            };

            foreach (var path in devPaths)
            {
                if (File.Exists(path))
                {
                    AppLogger.Info($"Worker localizzato in percorso di sviluppo: {path}");
                    return path;
                }
            }

            // 3. Ricerca risalendo l'albero fino alla radice della solution (escludendo obj/ e release/)
            try
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null && dir.Exists)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "AeroSpot.sln")))
                    {
                        var found = dir.GetFiles(exeName, SearchOption.AllDirectories)
                            .Where(f => !IsExcludedScanPath(dir.FullName, f.FullName))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
                        if (found != null && File.Exists(found.FullName))
                        {
                            AppLogger.Info($"Worker localizzato tramite scansione solution: {found.FullName}");
                            return found.FullName;
                        }
                        break;
                    }
                    dir = dir.Parent;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Errore durante la scansione dell'albero solution per {exeName}: {ex.Message}");
            }

            AppLogger.Warn($"Impossibile localizzare {exeName}, fallback al percorso predefinito: {candidate}");
            return candidate;
        }

        private static bool IsExcludedScanPath(string rootDir, string filePath)
        {
            string relative = Path.GetRelativePath(rootDir, filePath);
            string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string seg in segments)
            {
                if (string.Equals(seg, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(seg, "release", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(seg, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                _pipeServer.Dispose();
                lock (_lock)
                {
                    _ipcConnectedTcs?.TrySetResult(false);
                    if (_workerProcess != null)
                    {
                        try
                        {
                            if (!_workerProcess.HasExited)
                            {
                                _workerProcess.Kill();
                            }
                        }
                        catch { }
                        try { _workerProcess.Dispose(); } catch { }
                        _workerProcess = null;
                    }
                }
            }
            catch { }
        }
    }
}
