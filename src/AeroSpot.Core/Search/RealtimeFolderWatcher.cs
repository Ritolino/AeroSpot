using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Search
{
    /// <summary>
    /// Monitora in tempo reale le cartelle attive dell'utente (Downloads, Desktop, Documenti, ecc.)
    /// tramite eventi Win32 IOCP ReadDirectoryChangesW, indicizzando istantaneamente i file
    /// creati, rinominati, modificati o eliminati senza consumare CPU a riposo.
    /// </summary>
    public sealed class RealtimeFolderWatcher : IDisposable
    {
        private readonly SqliteIndexDatabase _db;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly ConcurrentDictionary<string, byte> _pendingUpserts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _pendingDeletes = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Timers.Timer _debounceTimer;
        private readonly object _processLock = new();
        private bool _isDisposed;

        public event Action? ItemsChanged;

        private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".crdownload",
            ".part",
            ".tmp",
            ".temp",
            ".opdownload",
            ".lock",
            ".log"
        };

        public RealtimeFolderWatcher(SqliteIndexDatabase db, IEnumerable<string>? customFolders = null, int debounceMs = 250)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));

            _debounceTimer = new System.Timers.Timer(debounceMs)
            {
                AutoReset = false
            };
            _debounceTimer.Elapsed += (s, e) => ProcessPendingChanges();

            var foldersToWatch = (customFolders != null) 
                ? customFolders.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : GetDefaultUserFolders();

            foreach (var folder in foldersToWatch)
            {
                try
                {
                    var fsw = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        InternalBufferSize = 65536
                    };

                    fsw.Created += OnFileSystemEvent;
                    fsw.Changed += OnFileSystemEvent;
                    fsw.Deleted += OnFileSystemDeleted;
                    fsw.Renamed += OnFileSystemRenamed;
                    fsw.Error += (s, e) =>
                    {
                        AppLogger.Warn($"RealtimeFolderWatcher error in '{folder}': {e.GetException()?.Message}");
                    };

                    fsw.EnableRaisingEvents = true;
                    _watchers.Add(fsw);
                    AppLogger.Info($"RealtimeFolderWatcher: Monitoraggio attivo su '{folder}'");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"RealtimeFolderWatcher: Impossibile monitorare '{folder}': {ex.Message}");
                }
            }
        }

        public static List<string> GetDefaultUserFolders()
        {
            var list = new List<string>();

            // 1. Cartella Downloads (supporta percorso personalizzato da registro)
            string downloads = GetDownloadsPath();
            if (Directory.Exists(downloads)) list.Add(downloads);

            // 2. Desktop
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop)) list.Add(desktop);

            // 3. Documenti
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(documents)) list.Add(documents);

            // 4. Immagini
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures)) list.Add(pictures);

            // 5. Video
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (Directory.Exists(videos)) list.Add(videos);

            // 6. Musica
            string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (Directory.Exists(music)) list.Add(music);

            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string GetDownloadsPath()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                if (key != null)
                {
                    var val = key.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                    if (!string.IsNullOrEmpty(val))
                    {
                        string expanded = Environment.ExpandEnvironmentVariables(val);
                        if (Directory.Exists(expanded)) return expanded;
                    }
                }
            }
            catch { }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (_isDisposed) return;
            string path = e.FullPath;
            if (ShouldIgnorePath(path)) return;

            _pendingDeletes.TryRemove(path, out _);
            _pendingUpserts[path] = 1;
            RestartDebounce();
        }

        private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
        {
            if (_isDisposed) return;
            string path = e.FullPath;
            if (ShouldIgnorePath(path)) return;

            _pendingUpserts.TryRemove(path, out _);
            _pendingDeletes[path] = 1;
            RestartDebounce();
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            if (_isDisposed) return;

            string oldPath = e.OldFullPath;
            string newPath = e.FullPath;

            if (!string.IsNullOrEmpty(oldPath))
            {
                _pendingUpserts.TryRemove(oldPath, out _);
                _pendingDeletes[oldPath] = 1;
            }

            if (!ShouldIgnorePath(newPath))
            {
                _pendingDeletes.TryRemove(newPath, out _);
                _pendingUpserts[newPath] = 1;
            }

            RestartDebounce();
        }

        private static bool ShouldIgnorePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return true;

            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(fileName)) return true;

            // Ignora file di sistema, lock Office e file temporanei Unix
            if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(".", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string ext = Path.GetExtension(fullPath);
            if (IgnoredExtensions.Contains(ext))
            {
                return true;
            }

            if (!PathFilter.IsPathAllowed(fullPath))
            {
                return true;
            }

            return false;
        }

        private void RestartDebounce()
        {
            if (_isDisposed) return;
            try
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            catch { }
        }

        private void ProcessPendingChanges()
        {
            if (_isDisposed) return;

            lock (_processLock)
            {
                if (_isDisposed) return;

                var deletes = _pendingDeletes.Keys.ToList();
                _pendingDeletes.Clear();

                var upserts = _pendingUpserts.Keys.ToList();
                _pendingUpserts.Clear();

                bool anyChange = false;

                // 1. Esegui eliminazioni (include eventuali elementi figli in caso di cartella eliminata/rinominata)
                if (deletes.Count > 0)
                {
                    try
                    {
                        int deleted = _db.DeleteByPathPrefixes(deletes);
                        if (deleted > 0)
                        {
                            anyChange = true;
                            AppLogger.Info($"RealtimeFolderWatcher: Rimossi {deleted} elementi dall'indice.");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"RealtimeFolderWatcher DeleteByPathPrefixes error: {ex.Message}");
                    }
                }

                // 2. Esegui inserimenti/aggiornamenti
                if (upserts.Count > 0)
                {
                    var itemsToUpsert = new List<IndexedItem>();

                    foreach (var path in upserts)
                    {
                        try
                        {
                            if (File.Exists(path))
                            {
                                var fi = new FileInfo(path);
                                string ext = fi.Extension;
                                if (IgnoredExtensions.Contains(ext)) continue;

                                string category = FileCrawler.CategorizeExtension(ext);
                                bool isApp = category == "Applicazione";

                                itemsToUpsert.Add(new IndexedItem(
                                    title: Path.GetFileNameWithoutExtension(fi.Name),
                                    fullPath: fi.FullName,
                                    category: category,
                                    executionTarget: fi.FullName,
                                    isApp: isApp,
                                    extension: ext.ToLowerInvariant(),
                                    lastModifiedUtcTicks: fi.LastWriteTimeUtc.Ticks
                                ));
                            }
                            else if (Directory.Exists(path))
                            {
                                var di = new DirectoryInfo(path);
                                if (!PathFilter.IsReparsePointOrJunction(di.Attributes))
                                {
                                    itemsToUpsert.Add(new IndexedItem(
                                        title: di.Name,
                                        fullPath: di.FullName,
                                        category: "Cartella",
                                        executionTarget: di.FullName,
                                        isApp: false
                                    ));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"RealtimeFolderWatcher item inspect error for '{path}': {ex.Message}");
                        }
                    }

                    if (itemsToUpsert.Count > 0)
                    {
                        try
                        {
                            int upserted = _db.UpsertBatch(itemsToUpsert);
                            if (upserted > 0)
                            {
                                anyChange = true;
                                AppLogger.Info($"RealtimeFolderWatcher: Aggiornati {upserted} elementi in tempo reale.");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"RealtimeFolderWatcher UpsertBatch error: {ex.Message}");
                        }
                    }
                }

                if (anyChange)
                {
                    ItemsChanged?.Invoke();
                }
            }
        }

        public void Dispose()
        {
            lock (_processLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                try
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Dispose();
                }
                catch { }

                foreach (var fsw in _watchers)
                {
                    try
                    {
                        fsw.EnableRaisingEvents = false;
                        fsw.Dispose();
                    }
                    catch { }
                }
                _watchers.Clear();
                _pendingUpserts.Clear();
                _pendingDeletes.Clear();
            }
        }
    }
}
