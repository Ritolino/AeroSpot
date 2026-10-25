using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Search
{
    public sealed class FileCrawler
    {
        private readonly SqliteIndexDatabase _db;
        private readonly int _batchSize;

        public event Action<int>? ProgressUpdated;

        public FileCrawler(SqliteIndexDatabase db, int batchSize = 500)
        {
            _db = db;
            _batchSize = batchSize;
        }

        public int CrawlAll(CancellationToken ct = default)
        {
            AppLogger.Info("FileCrawler: Avvio scansione indicizzata del sistema...");
            int totalIndexed = 0;
            var buffer = new List<IndexedItem>(_batchSize);

            // 1. Scansiona prima tutte le applicazioni registrate e Start Menu
            try
            {
                var apps = AppsFolderScanner.ScanApps();
                totalIndexed += _db.UpsertBatch(apps);
                ProgressUpdated?.Invoke(totalIndexed);
                AppLogger.Info($"FileCrawler: Indicizzate {apps.Count} applicazioni iniziali.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("FileCrawler: Errore durante la scansione delle applicazioni", ex);
            }

            // 2. Scansiona le radici consentite (C: e intero E:)
            var roots = PathFilter.GetWhitelistedScanRoots();
            foreach (var root in roots)
            {
                if (ct.IsCancellationRequested) break;
                AppLogger.Info($"FileCrawler: Scansione radice consentita: {root}");
                CrawlDirectoryIterative(root, buffer, ref totalIndexed, ct);
            }

            // Scarica i restanti elementi nel buffer
            if (buffer.Count > 0 && !ct.IsCancellationRequested)
            {
                totalIndexed += _db.UpsertBatch(buffer);
                buffer.Clear();
                ProgressUpdated?.Invoke(totalIndexed);
            }

            _db.SetMeta("LastCompletedUtc", DateTime.UtcNow.ToString("o"));
            _db.SetMeta("TotalIndexedCount", totalIndexed.ToString());
            AppLogger.Info($"FileCrawler: Scansione completata. Totale elementi indicizzati: {totalIndexed}");

            return totalIndexed;
        }

        public void CrawlDirectoryIterative(string rootPath, List<IndexedItem> buffer, ref int totalIndexed, CancellationToken ct)
        {
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            while (queue.Count > 0)
            {
                if (ct.IsCancellationRequested) break;

                string currentDir = queue.Dequeue();

                DirectoryInfo dirInfo;
                try
                {
                    dirInfo = new DirectoryInfo(currentDir);
                    if (!dirInfo.Exists) continue;

                    // Ignora reparse points, giunzioni e collegamenti simbolici
                    if (PathFilter.IsReparsePointOrJunction(dirInfo.Attributes))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                // 1. Indicizza la directory stessa come Cartella
                if (!string.Equals(currentDir, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    string dirName = Path.GetFileName(currentDir);
                    if (!string.IsNullOrEmpty(dirName))
                    {
                        buffer.Add(new IndexedItem(dirName, currentDir, "Cartella", currentDir, isApp: false));
                        CheckFlushBuffer(buffer, ref totalIndexed);
                    }
                }

                // 2. Indicizza i file della cartella corrente
                try
                {
                    var files = dirInfo.EnumerateFiles();
                    foreach (var file in files)
                    {
                        if (ct.IsCancellationRequested) break;

                        string filePath = file.FullName;
                        if (!PathFilter.IsPathAllowed(filePath))
                        {
                            continue;
                        }

                        string category = CategorizeExtension(file.Extension);
                        bool isApp = category == "Applicazione";

                        buffer.Add(new IndexedItem(
                            title: Path.GetFileNameWithoutExtension(file.Name),
                            fullPath: filePath,
                            category: category,
                            executionTarget: filePath,
                            isApp: isApp,
                            extension: file.Extension,
                            lastModifiedUtcTicks: file.LastWriteTimeUtc.Ticks
                        ));

                        CheckFlushBuffer(buffer, ref totalIndexed);
                    }
                }
                catch { }

                // 3. Accoda le sottodirectory permesse
                try
                {
                    var subDirs = dirInfo.EnumerateDirectories();
                    foreach (var sub in subDirs)
                    {
                        if (ct.IsCancellationRequested) break;

                        string subPath = sub.FullName;
                        if (PathFilter.IsPathAllowed(subPath) && !PathFilter.IsReparsePointOrJunction(sub.Attributes))
                        {
                            queue.Enqueue(subPath);
                        }
                    }
                }
                catch { }
            }
        }

        private void CheckFlushBuffer(List<IndexedItem> buffer, ref int totalIndexed)
        {
            if (buffer.Count >= _batchSize)
            {
                totalIndexed += _db.UpsertBatch(buffer);
                buffer.Clear();
                ProgressUpdated?.Invoke(totalIndexed);
            }
        }

        public static string CategorizeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return "File";

            switch (ext.ToLowerInvariant())
            {
                case ".exe":
                case ".lnk":
                case ".bat":
                case ".cmd":
                    return "Applicazione";

                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".webp":
                case ".bmp":
                case ".gif":
                case ".ico":
                case ".svg":
                    return "Immagine";

                case ".mp4":
                case ".mov":
                case ".mkv":
                case ".avi":
                case ".wmv":
                case ".webm":
                case ".m4v":
                case ".flv":
                case ".ts":
                    return "Video";

                case ".mp3":
                case ".wav":
                case ".flac":
                case ".aac":
                case ".ogg":
                case ".wma":
                case ".m4a":
                    return "Audio";

                case ".zip":
                case ".rar":
                case ".7z":
                case ".tar":
                case ".gz":
                    return "Archivio";

                case ".txt":
                case ".md":
                case ".py":
                case ".json":
                case ".log":
                case ".csv":
                case ".ini":
                case ".pdf":
                case ".docx":
                case ".xlsx":
                case ".pptx":
                case ".cs":
                case ".html":
                case ".xml":
                    return "Documento";

                default:
                    return "File";
            }
        }
    }
}
