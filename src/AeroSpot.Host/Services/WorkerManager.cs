using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.IPC;
using AeroSpot.Core.Logging;

namespace AeroSpot.Host.Services
{
    public sealed class WorkerManager : IDisposable
    {
        private readonly AppConfig _config;
        private readonly NamedPipeHostServer _pipeServer;
        private Process? _workerProcess;
        private readonly object _lock = new object();
        private readonly TaskCompletionSource<bool> _workerReadyTcs = new TaskCompletionSource<bool>();
        private bool _isDisposed;

        public bool IsWorkerRunning => _workerProcess != null && !_workerProcess.HasExited;
        public bool IsIpcConnected => _pipeServer.IsClientConnected;

        public event Action<string>? WorkerStatusChanged;
        public event Action? ConfigReloadRequested;
        public event Action<string>? PreviewOpened;
        public event Action? PreviewClosed;

        public WorkerManager(AppConfig config)
        {
            _config = config;
            _pipeServer = new NamedPipeHostServer();
            _pipeServer.ClientConnected += OnClientConnected;
            _pipeServer.ClientDisconnected += OnClientDisconnected;
            _pipeServer.MessageReceived += OnMessageReceived;
            _pipeServer.Start();
        }

        public string GetStatusString()
        {
            lock (_lock)
            {
                if (!IsWorkerRunning) return "Worker a riposo (Non avviato)";
                if (IsIpcConnected) return $"Worker attivo (PID: {_workerProcess!.Id})";
                return "Worker in avvio...";
            }
        }

        private void OnClientConnected()
        {
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
                    _workerReadyTcs.TrySetResult(true);
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
            }
        }

        public async Task<bool> EnsureWorkerStartedAsync(int timeoutMs = 8000)
        {
            lock (_lock)
            {
                if (IsWorkerRunning && IsIpcConnected)
                {
                    return true;
                }

                if (_workerProcess != null && _workerProcess.HasExited)
                {
                    _workerProcess = null;
                }

                if (!IsWorkerRunning)
                {
                    string workerExe = ResolveWorkerExecutable();
                    if (!File.Exists(workerExe))
                    {
                        AppLogger.Error($"Eseguibile del Worker non trovato al percorso: {workerExe}");
                        return false;
                    }

                    AppLogger.Info($"Avvio processo Worker: {workerExe}");
                    var psi = new ProcessStartInfo
                    {
                        FileName = workerExe,
                        Arguments = $"--timeout {_config.WorkerIdleTimeoutSeconds}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(workerExe)
                    };

                    try
                    {
                        _workerProcess = Process.Start(psi);
                        if (_workerProcess != null)
                        {
                            _workerProcess.EnableRaisingEvents = true;
                            _workerProcess.Exited += (s, e) =>
                            {
                                int exitCode = -1;
                                try { exitCode = _workerProcess.ExitCode; } catch { }
                                AppLogger.Info($"Processo Worker terminato. ExitCode: {exitCode}");
                                lock (_lock) { _workerProcess = null; }
                                WorkerStatusChanged?.Invoke(GetStatusString());
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Errore durante l'avvio del processo Worker.", ex);
                        return false;
                    }
                }
            }

            // Attesa della connessione IPC
            var cts = new CancellationTokenSource(timeoutMs);
            while (!IsIpcConnected && !cts.IsCancellationRequested)
            {
                await Task.Delay(50, cts.Token).ConfigureAwait(false);
            }

            return IsIpcConnected;
        }

        public long LastColdStartMs { get; private set; }
        public long LastWarmStartMs { get; private set; }

        public async Task<bool> ShowLauncherAsync()
        {
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

        public async Task<bool> StopWorkerAsync(int timeoutMs = 3000)
        {
            Process? proc;
            lock (_lock)
            {
                proc = _workerProcess;
            }

            if (proc == null || proc.HasExited)
            {
                return true;
            }

            AppLogger.Info("Invio comando CloseWorker al Worker...");
            await _pipeServer.SendMessageAsync(new IpcMessage(IpcMessageType.CloseWorker), timeoutMs).ConfigureAwait(false);

            // Attesa uscita pulita
            bool exited = proc.WaitForExit(timeoutMs);
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
                _workerProcess = null;
            }

            return true;
        }

        private string ResolveWorkerExecutable()
        {
            string baseDir = AppContext.BaseDirectory;
            string exeName = "AeroSpot.Worker.exe";

            // 1. Stessa cartella (produzione o pacchetto unificato)
            string candidate = Path.Combine(baseDir, _config.WorkerRelativePath);
            if (File.Exists(candidate))
            {
                AppLogger.Info($"Worker localizzato in: {candidate}");
                return candidate;
            }

            string sameDir = Path.Combine(baseDir, exeName);
            if (File.Exists(sameDir))
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

            // 3. Ricerca risalendo l'albero fino alla radice della solution
            try
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null && dir.Exists)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "AeroSpot.sln")))
                    {
                        var found = dir.GetFiles(exeName, SearchOption.AllDirectories)
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

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                _pipeServer.Dispose();
                if (IsWorkerRunning)
                {
                    _workerProcess?.Kill();
                }
            }
            catch { }
        }
    }
}
