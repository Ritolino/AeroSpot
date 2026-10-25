using System;
using System.Diagnostics;
using System.Threading;
using AeroSpot.Core.Config;
using AeroSpot.Core.Logging;
using AeroSpot.Host.Native;
using AeroSpot.Host.Services;

namespace AeroSpot.Host
{
    internal static class Program
    {
        private const string MutexPrefix = "Local\\AeroSpot_Host_Mutex_";

        [STAThread]
        private static void Main(string[] args)
        {
            AppLogger.Initialize("Host");

            // Handle CLI argument --cleanup (M4.2)
            if (args != null && Array.Exists(args, a => string.Equals(a, "--cleanup", StringComparison.OrdinalIgnoreCase)))
            {
                AppLogger.Info("Executing --cleanup: removing Windows autostart registry key.");
                StartupManager.Cleanup();
                AppLogger.Info("Cleanup completed successfully. Exiting.");
                return;
            }

            AppLogger.Info("AeroSpot.Host starting up...");

            string mutexName = MutexPrefix + Environment.UserName.Replace("\\", "_");
            using var mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                AppLogger.Warn("Another instance of AeroSpot.Host is already running. Stopping this instance.");
                return;
            }

            try
            {
                var config = AppConfig.Load();
                AeroSpot.Core.Localization.LocalizationService.Initialize(config.Language);

                if (config.StartWithWindows)
                {
                    StartupManager.AutoRepairPathIfEnabled();
                }

                using var workerManager = new WorkerManager(config);
                using var hostWindow = new HostWindow(config, workerManager);

                if (!hostWindow.Initialize())
                {
                    AppLogger.Error("Inizializzazione della finestra Host fallita. Uscita.");
                    return;
                }

                // Se l'utente ha configurato sia l'avvio con Windows sia il mantenimento del motore attivo,
                // pre-avvia direttamente il Worker in background all'avvio.
                if (config.StartWithWindows && config.KeepWorkerWarm)
                {
                    AppLogger.Info("StartWithWindows e KeepWorkerWarm attivi: avvio anticipato del Worker all'avvio di Windows...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await workerManager.EnsureWorkerStartedAsync().ConfigureAwait(false);
                            AppLogger.Info("Worker pre-avviato con successo in background all'avvio.");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Pre-avvio del Worker all'avvio non riuscito: {ex.Message}");
                        }
                    });
                }

                // Log initial Host memory
                Process current = Process.GetCurrentProcess();
                current.Refresh();
                long wsMb = current.WorkingSet64 / (1024 * 1024);
                long privMb = current.PrivateMemorySize64 / (1024 * 1024);
                AppLogger.Info($"AeroSpot.Host active. Initial memory: WorkingSet={wsMb}MB, PrivateBytes={privMb}MB");

                // Execute message loop
                hostWindow.RunMessageLoop();

                AppLogger.Info("Stopping Worker...");
                workerManager.StopWorkerAsync(2000).GetAwaiter().GetResult();
                AppLogger.Info("AeroSpot.Host terminated cleanly.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Eccezione non gestita nell'Host.", ex);
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }
}
