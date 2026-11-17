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
                AppLogger.Info("Executing --cleanup: removing Windows autostart and context menu registry keys.");
                StartupManager.Cleanup();
                AeroSpot.Core.Native.ShellContextMenuIntegration.Unregister();
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

                if (config.StartWithWindows && StartupManager.IsStartupApprovedByUser())
                {
                    if (!StartupManager.IsStartupEnabled())
                    {
                        StartupManager.SetStartupEnabled(true);
                    }
                    else
                    {
                        StartupManager.AutoRepairPathIfEnabled();
                    }
                }

                if (config.IntegrateWindowsContextMenu)
                {
                    AeroSpot.Core.Native.ShellContextMenuIntegration.Register(AppDomain.CurrentDomain.BaseDirectory, config.Language);
                }

                using var workerManager = new WorkerManager(config);
                using var hostWindow = new HostWindow(config, workerManager);

                if (!hostWindow.Initialize())
                {
                    AppLogger.Error("Inizializzazione della finestra Host fallita. Uscita.");
                    return;
                }

                // Se il mantenimento del motore attivo (KeepWorkerWarm) è abilitato,
                // pre-avvia direttamente il Worker in background all'avvio dell'Host.
                if (config.KeepWorkerWarm)
                {
                    AppLogger.Info("KeepWorkerWarm attivo: avvio anticipato del Worker in background all'avvio dell'Host...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await workerManager.EnsureWorkerStartedAsync(25000).ConfigureAwait(false);
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
