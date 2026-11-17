using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Logging;

namespace AeroSpot.Tools
{
    public static class FileBatchCollector
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);

        /// <summary>
        /// Raccoglie i file selezionati sia se passati contemporaneamente da riga di comando
        /// sia se avviati in istanze multiple parallele da Windows Explorer (%1 Shell Verb).
        /// Ritorna la lista completa e ordinata dei file per l'istanza Leader, oppure null per le istanze Follower (che devono terminare subito).
        /// </summary>
        public static async Task<List<string>?> CollectFilesAsync(string actionKey, IReadOnlyList<string> directFiles, bool isWorkerIpc = false, int debounceMs = 350)
        {
            // 1. Se ci sono già più file passati da riga di comando o se è un'invocazione Worker IPC diretta, non c'è attesa batch
            if (directFiles.Count > 1 || isWorkerIpc)
            {
                return SortFiles(directFiles);
            }

            if (directFiles.Count == 0)
            {
                return new List<string>();
            }

            string singleFile = directFiles[0];
            string safeKey = actionKey.TrimStart('-').Replace("-", "_");
            string userSanitized = Environment.UserName.Replace("\\", "_");
            string mutexName = $"Local\\AeroSpot_BatchLock_{safeKey}_{userSanitized}";
            string pipeName = $"AeroSpot_BatchPipe_{safeKey}_{userSanitized}";

            bool isLeader = false;
            Mutex? mutex = null;

            try
            {
                mutex = new Mutex(false, mutexName);
                isLeader = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                isLeader = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"FileBatchCollector: Impossibile acquisire mutex {mutexName}: {ex.Message}");
                return new List<string> { singleFile };
            }

            if (!isLeader)
            {
                // Istanza Follower: invia il proprio file al Leader tramite Named Pipe ed esce immediatamente
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    await client.ConnectAsync(1200).ConfigureAwait(false);
                    using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(singleFile).ConfigureAwait(false);
                    AppLogger.Info($"FileBatchCollector: Follower ha consegnato '{singleFile}' al Leader.");
                    return null; // Istanza completata, esci
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"FileBatchCollector: Follower non è riuscito a connettersi al Leader ({ex.Message}), procedo standalone.");
                    return new List<string> { singleFile };
                }
                finally
                {
                    mutex?.Dispose();
                }
            }

            // Istanza Leader: raccoglie il proprio file e attende i follower
            var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { singleFile };

            try
            {
                var lastArrival = DateTime.UtcNow;

                while (true)
                {
                    int remaining = debounceMs - (int)(DateTime.UtcNow - lastArrival).TotalMilliseconds;
                    if (remaining <= 0) break;

                    try
                    {
                        using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        using var timeoutCts = new CancellationTokenSource(remaining);

                        await server.WaitForConnectionAsync(timeoutCts.Token).ConfigureAwait(false);

                        using var reader = new StreamReader(server);
                        string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            string receivedPath = line.Trim();
                            if (File.Exists(receivedPath))
                            {
                                collected.Add(receivedPath);
                                lastArrival = DateTime.UtcNow; // Reset debounce
                                AppLogger.Info($"FileBatchCollector: Leader ha raccolto file batch: {receivedPath}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout debounce raggiunto, nessun nuovo file in arrivo
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug($"FileBatchCollector: Errore ciclo server pipe: {ex.Message}");
                        break;
                    }
                }
            }
            finally
            {
                try
                {
                    mutex?.ReleaseMutex();
                }
                catch { }
                mutex?.Dispose();
            }

            return SortFiles(collected);
        }

        private static List<string> SortFiles(IEnumerable<string> files)
        {
            var list = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            list.Sort((a, b) => StrCmpLogicalW(a, b));
            return list;
        }
    }
}
