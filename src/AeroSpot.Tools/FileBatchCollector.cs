using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

namespace AeroSpot.Tools
{
    public static class FileBatchCollector
    {
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
            string lockPath = Path.Combine(Path.GetTempPath(), $"AeroSpot_BatchLock_{safeKey}_{userSanitized}.lock");
            string pipeName = $"AeroSpot_BatchPipe_{safeKey}_{userSanitized}";

            bool isLeader = false;
            FileStream? lockStream = null;

            try
            {
                // FileStream con FileShare.None non ha thread affinity ed è rilasciato automaticamente dal kernel OS alla chiusura/crash
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                isLeader = true;
            }
            catch (IOException)
            {
                isLeader = false;
            }
            catch (UnauthorizedAccessException)
            {
                isLeader = false;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"FileBatchCollector: Impossibile acquisire lock {lockPath}: {ex.Message}");
                return new List<string> { singleFile };
            }

            if (!isLeader)
            {
                // Istanza Follower: invia il proprio file al Leader tramite Named Pipe ed esce immediatamente
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                    await client.ConnectAsync(1200).ConfigureAwait(false);
                    using var writer = new StreamWriter(client, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(singleFile).ConfigureAwait(false);
                    AppLogger.Info($"FileBatchCollector: Follower ha consegnato '{singleFile}' al Leader.");
                    return null; // Istanza completata, esci
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"FileBatchCollector: Follower non è riuscito a connettersi al Leader ({ex.Message}), procedo standalone.");
                    return new List<string> { singleFile };
                }
            }

            // Istanza Leader: raccoglie il proprio file e attende i follower su un'unica NamedPipeServerStream persistente
            var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { singleFile };

            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                var lastArrival = DateTime.UtcNow;

                while (true)
                {
                    int remaining = debounceMs - (int)(DateTime.UtcNow - lastArrival).TotalMilliseconds;
                    if (remaining <= 0) break;

                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(remaining);
                        await server.WaitForConnectionAsync(timeoutCts.Token).ConfigureAwait(false);

                        try
                        {
                            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
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
                        finally
                        {
                            try { server.Disconnect(); } catch { }
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
            catch (Exception ex)
            {
                AppLogger.Warn($"FileBatchCollector: Errore avvio server pipe {pipeName}: {ex.Message}");
            }
            finally
            {
                lockStream?.Dispose();
            }

            return SortFiles(collected);
        }

        private static List<string> SortFiles(IEnumerable<string> files)
        {
            var list = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            list.Sort((a, b) => Win32.StrCmpLogicalW(a, b));
            return list;
        }
    }
}
