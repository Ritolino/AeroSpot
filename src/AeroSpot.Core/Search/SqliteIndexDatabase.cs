using System;
using System.Collections.Generic;
using System.IO;
using AeroSpot.Core.Logging;
using Microsoft.Data.Sqlite;

namespace AeroSpot.Core.Search
{
    public sealed class SqliteIndexDatabase : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private SqliteConnection? _connection;
        private readonly object _lock = new object();

        public SqliteIndexDatabase(string? dbPath = null)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                _dbPath = Config.AppDataPath.GetDatabasePath();
                string? dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            else
            {
                _dbPath = dbPath;
                string? dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default
            };
            _connectionString = builder.ToString();
        }

        private static string EscapeLikePattern(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
        }

        public void Initialize()
        {
            lock (_lock)
            {
                if (_connection == null)
                {
                    _connection = new SqliteConnection(_connectionString);
                    _connection.Open();

                    using var cmd = _connection.CreateCommand();
                    // Prestazioni ottimali WAL (Write-Ahead Logging) e sincronizzazione bilanciata
                    cmd.CommandText = @"
                        PRAGMA busy_timeout = 5000;
                        PRAGMA journal_mode = WAL;
                        PRAGMA synchronous = NORMAL;
                        PRAGMA cache_size = 5000;
                        PRAGMA temp_store = MEMORY;

                        CREATE TABLE IF NOT EXISTS IndexedItems (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Title TEXT NOT NULL COLLATE NOCASE,
                            FullPath TEXT NOT NULL UNIQUE COLLATE NOCASE,
                            Category TEXT NOT NULL,
                            Extension TEXT NOT NULL,
                            ExecutionTarget TEXT NOT NULL,
                            LastModifiedUtcTicks INTEGER NOT NULL,
                            IsApp INTEGER NOT NULL
                        );

                        CREATE INDEX IF NOT EXISTS idx_items_title ON IndexedItems(Title);
                        CREATE INDEX IF NOT EXISTS idx_items_category ON IndexedItems(Category);

                        CREATE TABLE IF NOT EXISTS RecentHistory (
                            FullPath TEXT PRIMARY KEY COLLATE NOCASE,
                            LastOpenedUtcTicks INTEGER NOT NULL,
                            OpenCount INTEGER NOT NULL DEFAULT 1
                        );
                        CREATE INDEX IF NOT EXISTS idx_recent_ticks ON RecentHistory(LastOpenedUtcTicks DESC);

                        CREATE TABLE IF NOT EXISTS SearchHistory (
                            NormalizedQuery TEXT PRIMARY KEY COLLATE NOCASE,
                            RawQuery TEXT NOT NULL,
                            SearchCount INTEGER NOT NULL DEFAULT 1,
                            LastSearchedUtcTicks INTEGER NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_search_freq ON SearchHistory(SearchCount DESC, LastSearchedUtcTicks DESC);

                        CREATE TABLE IF NOT EXISTS IndexMeta (
                            Key TEXT PRIMARY KEY,
                            Value TEXT NOT NULL
                        );
                    ";
                    cmd.ExecuteNonQuery();

                    // Purga una-tantum di file ombra, spazzatura macOS/Windows e .lnk/.url obsoleti
                    using var metaCheckCmd = _connection.CreateCommand();
                    metaCheckCmd.CommandText = "SELECT Value FROM IndexMeta WHERE Key = 'PurgedShortcutsV1';";
                    var purgedFlag = metaCheckCmd.ExecuteScalar() as string;
                    if (!string.Equals(purgedFlag, "1", StringComparison.Ordinal))
                    {
                        using var hasRowsCmd = _connection.CreateCommand();
                        hasRowsCmd.CommandText = "SELECT 1 FROM IndexedItems LIMIT 1;";
                        if (hasRowsCmd.ExecuteScalar() != null)
                        {
                            using var purgeCmd = _connection.CreateCommand();
                            purgeCmd.CommandText = "DELETE FROM IndexedItems WHERE Title LIKE '._%' OR Title = '.DS_Store' OR Title = 'Thumbs.db' OR Title = 'desktop.ini' OR Title LIKE '~$%' OR lower(Extension) IN ('.lnk', '.url') OR lower(FullPath) LIKE '%.lnk' OR lower(FullPath) LIKE '%.url';";
                            purgeCmd.ExecuteNonQuery();

                            using var setMetaCmd = _connection.CreateCommand();
                            setMetaCmd.CommandText = "INSERT INTO IndexMeta (Key, Value) VALUES ('PurgedShortcutsV1', '1') ON CONFLICT(Key) DO UPDATE SET Value = '1';";
                            setMetaCmd.ExecuteNonQuery();
                        }
                    }

                    // Pulizia retroattiva: elimina eventuali espressioni di calcolo salvate in precedenza nella cronologia
                    CleanupCalculatorSearchesInternal();
                }
            }
        }

        public int UpsertBatch(IEnumerable<IndexedItem> items)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var tx = _connection!.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO IndexedItems (Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp)
                    VALUES ($title, $path, $cat, $ext, $exec, $mod, $isApp)
                    ON CONFLICT(FullPath) DO UPDATE SET
                        Title = excluded.Title,
                        Category = excluded.Category,
                        Extension = excluded.Extension,
                        ExecutionTarget = excluded.ExecutionTarget,
                        LastModifiedUtcTicks = excluded.LastModifiedUtcTicks,
                        IsApp = excluded.IsApp;
                ";

                var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
                var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
                var pCat = cmd.Parameters.Add("$cat", SqliteType.Text);
                var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
                var pExec = cmd.Parameters.Add("$exec", SqliteType.Text);
                var pMod = cmd.Parameters.Add("$mod", SqliteType.Integer);
                var pIsApp = cmd.Parameters.Add("$isApp", SqliteType.Integer);

                int count = 0;
                foreach (var item in items)
                {
                    // Verifica perimetro prima della scrittura nel database persistente
                    if (!PathFilter.IsPathAllowed(item.FullPath))
                    {
                        if (item.FullPath.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.FullPath.IndexOf(@"\Epic Games\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                item.FullPath.IndexOf(@"\SteamLibrary\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                item.FullPath.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }

                    pTitle.Value = item.Title;
                    pPath.Value = item.FullPath;
                    pCat.Value = item.Category;
                    pExt.Value = item.Extension;
                    pExec.Value = item.ExecutionTarget;
                    pMod.Value = item.LastModifiedUtcTicks;
                    pIsApp.Value = item.IsApp ? 1 : 0;

                    cmd.ExecuteNonQuery();
                    count++;
                }

                tx.Commit();
                return count;
            }
        }

        public int DeleteBatch(IEnumerable<string> paths)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var tx = _connection!.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM IndexedItems WHERE FullPath = $path;";
                var pPath = cmd.Parameters.Add("$path", SqliteType.Text);

                int count = 0;
                foreach (var path in paths)
                {
                    pPath.Value = path;
                    count += cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return count;
            }
        }

        public int DeleteByPathPrefix(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath)) return 0;
            return DeleteByPathPrefixes(new[] { directoryPath });
        }

        public int DeleteByPathPrefixes(IEnumerable<string> paths)
        {
            if (paths == null) return 0;

            lock (_lock)
            {
                EnsureOpenLocked();
                using var tx = _connection!.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"DELETE FROM IndexedItems WHERE FullPath = $exact OR FullPath LIKE $prefix ESCAPE '\';";
                var pExact = cmd.Parameters.Add("$exact", SqliteType.Text);
                var pPrefix = cmd.Parameters.Add("$prefix", SqliteType.Text);

                int count = 0;
                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    string exact = path.TrimEnd('\\', '/');
                    if (string.IsNullOrEmpty(exact)) continue;
                    pExact.Value = exact;
                    pPrefix.Value = EscapeLikePattern(exact + "\\") + "%";
                    count += cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return count;
            }
        }

        /// <summary>
        /// Rimuove dal database SQLite tutti i file indicizzati appartenenti a lettere di disco non più presenti nella lista consentita.
        /// </summary>
        public int PurgeDisallowedDrives(IEnumerable<string> allowedDrives)
        {
            var allowedLetters = new HashSet<char>();
            if (allowedDrives != null)
            {
                foreach (var d in allowedDrives)
                {
                    if (!string.IsNullOrWhiteSpace(d))
                    {
                        char c = d.Trim()[0];
                        if (char.IsLetter(c))
                        {
                            allowedLetters.Add(char.ToUpperInvariant(c));
                        }
                    }
                }
            }

            if (allowedLetters.Count == 0) return 0;

            lock (_lock)
            {
                EnsureOpenLocked();
                using var tx = _connection!.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;

                var paramNames = new List<string>();
                int i = 0;
                foreach (var letter in allowedLetters)
                {
                    string pName = $"$d{i++}";
                    paramNames.Add(pName);
                    cmd.Parameters.AddWithValue(pName, letter.ToString());
                }

                cmd.CommandText = $@"
                    DELETE FROM IndexedItems 
                    WHERE substr(FullPath, 2, 1) = ':' 
                      AND UPPER(substr(FullPath, 1, 1)) NOT IN ({string.Join(", ", paramNames)});
                ";

                int purgedCount = cmd.ExecuteNonQuery();
                tx.Commit();

                if (purgedCount > 0)
                {
                    try
                    {
                        using var ckCmd = _connection.CreateCommand();
                        ckCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        ckCmd.ExecuteNonQuery();
                    }
                    catch { }

                    AppLogger.Info($"SqliteIndexDatabase: Eliminati {purgedCount} record di dischi non più selezionati.");
                }

                return purgedCount;
            }
        }

        /// <summary>
        /// Rimuove dal database SQLite tutti i file indicizzati che contengono uno dei segmenti di percorso esclusi.
        /// </summary>
        public int PurgeExcludedSegments(IEnumerable<string> excludedSegments)
        {
            if (excludedSegments == null) return 0;

            lock (_lock)
            {
                EnsureOpenLocked();
                using var tx = _connection!.BeginTransaction();
                int totalPurged = 0;

                foreach (var seg in excludedSegments)
                {
                    if (string.IsNullOrWhiteSpace(seg)) continue;
                    string trimmedSeg = seg.Trim();
                    string pattern = $"%{EscapeLikePattern(trimmedSeg)}%";

                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = tx;
                    if (string.Equals(trimmedSeg, @"\bin\", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmedSeg, @"\obj\", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmedSeg, "bin", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmedSeg, "obj", StringComparison.OrdinalIgnoreCase))
                    {
                        cmd.CommandText = @"DELETE FROM IndexedItems WHERE FullPath LIKE $pat ESCAPE '\' AND FullPath NOT LIKE '%:\Program Files\%' AND FullPath NOT LIKE '%:\Program Files (x86)\%';";
                    }
                    else
                    {
                        cmd.CommandText = @"DELETE FROM IndexedItems WHERE FullPath LIKE $pat ESCAPE '\';";
                    }
                    cmd.Parameters.AddWithValue("$pat", pattern);
                    totalPurged += cmd.ExecuteNonQuery();
                }

                tx.Commit();

                if (totalPurged > 0)
                {
                    try
                    {
                        using var ckCmd = _connection.CreateCommand();
                        ckCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        ckCmd.ExecuteNonQuery();
                    }
                    catch { }

                    AppLogger.Info($"SqliteIndexDatabase: Eliminati {totalPurged} record corrispondenti alle esclusioni.");
                }

                return totalPurged;
            }
        }

        /// <summary>
        /// Rimuove dal database SQLite tutti i file di sistema spazzatura (es. file ombra macOS ._*, Thumbs.db, .DS_Store, desktop.ini).
        /// </summary>
        public int PurgeSystemJunkFiles()
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM IndexedItems WHERE Title LIKE '._%' OR Title = '.DS_Store' OR Title = 'Thumbs.db' OR Title = 'desktop.ini' OR Title LIKE '~$%' OR lower(Extension) IN ('.lnk', '.url') OR lower(FullPath) LIKE '%.lnk' OR lower(FullPath) LIKE '%.url';";
                int purged = cmd.ExecuteNonQuery();
                if (purged > 0)
                {
                    try
                    {
                        using var ckCmd = _connection.CreateCommand();
                        ckCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        ckCmd.ExecuteNonQuery();
                    }
                    catch { }
                    AppLogger.Info($"SqliteIndexDatabase: Eliminati {purged} file spazzatura / metadati (._*, Thumbs.db, .DS_Store).");
                }
                return purged;
            }
        }

        public List<IndexedItem> Search(string query, int maxResults, bool enableRecentHistory)
        {
            if (string.IsNullOrWhiteSpace(query) && !enableRecentHistory)
            {
                lock (_lock)
                {
                    EnsureOpenLocked();
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = @"
                        SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                        FROM IndexedItems
                        WHERE IsApp = 1
                        ORDER BY Title
                        LIMIT $limit;
                    ";
                    cmd.Parameters.AddWithValue("$limit", maxResults);
                    var apps = new List<IndexedItem>();
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        apps.Add(new IndexedItem
                        {
                            Id = reader.GetInt64(0),
                            Title = reader.GetString(1),
                            FullPath = reader.GetString(2),
                            Category = reader.GetString(3),
                            Extension = reader.GetString(4),
                            ExecutionTarget = reader.GetString(5),
                            LastModifiedUtcTicks = reader.GetInt64(6),
                            IsApp = reader.GetInt32(7) == 1
                        });
                    }
                    return apps;
                }
            }
            return Search(query, maxResults, null, null, false, enableRecentHistory ? Config.SpotlightHomeMode.RecentOpened : Config.SpotlightHomeMode.None);
        }

        public List<IndexedItem> Search(string query, int maxResults, string? categoryFilter, List<string>? categoryPriority, bool prioritizeExactMatches, bool enableRecentHistory)
        {
            return Search(query, maxResults, categoryFilter, categoryPriority, prioritizeExactMatches, enableRecentHistory ? Config.SpotlightHomeMode.RecentOpened : Config.SpotlightHomeMode.None);
        }

        public List<IndexedItem> Search(string query, int maxResults = 10, string? categoryFilter = null, List<string>? categoryPriority = null, bool prioritizeExactMatches = false, Config.SpotlightHomeMode homeMode = Config.SpotlightHomeMode.RecentOpened)
        {
            var candidates = new List<IndexedItem>();

            lock (_lock)
            {
                EnsureOpenLocked();
                using var cmd = _connection!.CreateCommand();
                bool hasFilter = !string.IsNullOrWhiteSpace(categoryFilter) &&
                                 !string.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(categoryFilter, "Tutti", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(query))
                {
                    if (hasFilter)
                    {
                        if (string.Equals(categoryFilter, "Applicazione", StringComparison.OrdinalIgnoreCase))
                        {
                            cmd.CommandText = @"
                                SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                                FROM IndexedItems
                                WHERE IsApp = 1 OR Category = 'Applicazione'
                                ORDER BY Title
                                LIMIT $limit;
                            ";
                        }
                        else
                        {
                            cmd.CommandText = @"
                                SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                                FROM IndexedItems
                                WHERE Category = $catFilter
                                ORDER BY Title
                                LIMIT $limit;
                            ";
                            cmd.Parameters.AddWithValue("$catFilter", categoryFilter);
                        }
                    }
                    else
                    {
                        switch (homeMode)
                        {
                            case Config.SpotlightHomeMode.None:
                                return new List<IndexedItem>();

                            case Config.SpotlightHomeMode.Alphabetical:
                                // Ordine alfabetico delle applicazioni (comportamento classico originale di AeroSpot a Spotlight vuoto)
                                break;

                            case Config.SpotlightHomeMode.MostFrequentSearches:
                                var searches = GetMostFrequentSearchesInternal(maxResults);
                                if (searches.Count > 0)
                                {
                                    var searchItems = new List<IndexedItem>();
                                    foreach (var s in searches)
                                    {
                                        searchItems.Add(new IndexedItem(s, s, "Ricerca", s, isApp: false));
                                    }
                                    return searchItems;
                                }
                                break;

                            case Config.SpotlightHomeMode.MostFrequentlyOpened:
                                var frequents = GetMostFrequentItemsInternal(maxResults);
                                if (frequents.Count > 0)
                                {
                                    return frequents;
                                }
                                break;

                            case Config.SpotlightHomeMode.RecentOpened:
                            default:
                                var recents = GetRecentItemsInternal(maxResults);
                                if (recents.Count > 0)
                                {
                                    return recents;
                                }
                                break;
                        }

                        // Se la query è vuota e nessun elemento recente (o cronologia vuota), restituisce le applicazioni principali
                        cmd.CommandText = @"
                            SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                            FROM IndexedItems
                            WHERE IsApp = 1
                            ORDER BY Title
                            LIMIT $limit;
                        ";
                    }
                    cmd.Parameters.AddWithValue("$limit", maxResults);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        candidates.Add(new IndexedItem
                        {
                            Id = reader.GetInt64(0),
                            Title = reader.GetString(1),
                            FullPath = reader.GetString(2),
                            Category = reader.GetString(3),
                            Extension = reader.GetString(4),
                            ExecutionTarget = reader.GetString(5),
                            LastModifiedUtcTicks = reader.GetInt64(6),
                            IsApp = reader.GetInt32(7) == 1
                        });
                    }
                }
                else
                {
                    string trimmedQuery = query.Trim();
                    string[] terms = trimmedQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var titleWhereClauses = new List<string>();

                    for (int i = 0; i < terms.Length; i++)
                    {
                        string paramName = $"$term{i}";
                        string escapedTerm = EscapeLikePattern(terms[i]);
                        if (terms.Length == 1 && terms[0].Length >= 2 && terms[0].Length <= 5)
                        {
                            var acrChars = terms[0].ToCharArray();
                            var acrParts = new string[acrChars.Length];
                            for (int c = 0; c < acrChars.Length; c++)
                            {
                                acrParts[c] = EscapeLikePattern(acrChars[c].ToString());
                            }
                            string acrPattern = string.Join("%", acrParts) + "%";
                            cmd.Parameters.AddWithValue("$acr0", acrPattern);
                            titleWhereClauses.Add($@"(Title LIKE {paramName} ESCAPE '\' OR (IsApp = 1 AND Title LIKE $acr0 ESCAPE '\'))");
                        }
                        else
                        {
                            titleWhereClauses.Add($@"Title LIKE {paramName} ESCAPE '\'");
                        }
                        cmd.Parameters.AddWithValue(paramName, $"%{escapedTerm}%");
                    }

                    if (hasFilter)
                    {
                        if (string.Equals(categoryFilter, "Applicazione", StringComparison.OrdinalIgnoreCase))
                        {
                            titleWhereClauses.Add("(IsApp = 1 OR Category = 'Applicazione')");
                        }
                        else
                        {
                            titleWhereClauses.Add("Category = $catFilter");
                            cmd.Parameters.AddWithValue("$catFilter", categoryFilter);
                        }
                    }

                    cmd.Parameters.AddWithValue("$exactQuery", trimmedQuery);
                    cmd.Parameters.AddWithValue("$prefixQuery", $"{EscapeLikePattern(trimmedQuery)}%");

                    // Fase 1: Candidati il cui Nome (Title) contiene tutti i termini cercati (non vengono mai saturati da match sul solo percorso)
                    cmd.CommandText = $@"
                        SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                        FROM IndexedItems
                        WHERE {string.Join(" AND ", titleWhereClauses)}
                        ORDER BY
                            CASE WHEN Title = $exactQuery COLLATE NOCASE THEN 0
                                 WHEN Title LIKE $prefixQuery ESCAPE '\' THEN 1
                                 ELSE 2 END,
                            IsApp DESC,
                            LastModifiedUtcTicks DESC
                        LIMIT 250;
                    ";

                    var seenIds = new HashSet<long>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            long id = reader.GetInt64(0);
                            seenIds.Add(id);
                            candidates.Add(new IndexedItem
                            {
                                Id = id,
                                Title = reader.GetString(1),
                                FullPath = reader.GetString(2),
                                Category = reader.GetString(3),
                                Extension = reader.GetString(4),
                                ExecutionTarget = reader.GetString(5),
                                LastModifiedUtcTicks = reader.GetInt64(6),
                                IsApp = reader.GetInt32(7) == 1
                            });
                        }
                    }

                    // Fase 2 (Fallback sul FullPath): eseguita solo se i match sul Nome sono inferiori a maxResults (o 15)
                    int minTitleCandidates = Math.Max(maxResults, 15);
                    if (candidates.Count < minTitleCandidates)
                    {
                        using var pathCmd = _connection!.CreateCommand();
                        var pathWhereClauses = new List<string>();

                        for (int i = 0; i < terms.Length; i++)
                        {
                            string paramName = $"$pterm{i}";
                            pathWhereClauses.Add($@"(Title LIKE {paramName} ESCAPE '\' OR FullPath LIKE {paramName} ESCAPE '\')");
                            pathCmd.Parameters.AddWithValue(paramName, $"%{EscapeLikePattern(terms[i])}%");
                        }

                        if (hasFilter)
                        {
                            if (string.Equals(categoryFilter, "Applicazione", StringComparison.OrdinalIgnoreCase))
                            {
                                pathWhereClauses.Add("(IsApp = 1 OR Category = 'Applicazione')");
                            }
                            else
                            {
                                pathWhereClauses.Add("Category = $catFilter");
                                pathCmd.Parameters.AddWithValue("$catFilter", categoryFilter);
                            }
                        }

                        pathCmd.CommandText = $@"
                            SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                            FROM IndexedItems
                            WHERE {string.Join(" AND ", pathWhereClauses)}
                            ORDER BY IsApp DESC, LastModifiedUtcTicks DESC
                            LIMIT 100;
                        ";

                        using var pathReader = pathCmd.ExecuteReader();
                        while (pathReader.Read())
                        {
                            long id = pathReader.GetInt64(0);
                            if (seenIds.Add(id))
                            {
                                candidates.Add(new IndexedItem
                                {
                                    Id = id,
                                    Title = pathReader.GetString(1),
                                    FullPath = pathReader.GetString(2),
                                    Category = pathReader.GetString(3),
                                    Extension = pathReader.GetString(4),
                                    ExecutionTarget = pathReader.GetString(5),
                                    LastModifiedUtcTicks = pathReader.GetInt64(6),
                                    IsApp = pathReader.GetInt32(7) == 1
                                });
                            }
                        }
                    }
                }
            }

            // Applica il ranking avanzato a multi-segnale
            return SearchRanker.Rank(candidates, query, maxResults, categoryPriority, prioritizeExactMatches);
        }

        public int GetTotalCount()
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM IndexedItems;";
                object? res = cmd.ExecuteScalar();
                return res != null ? Convert.ToInt32(res) : 0;
            }
        }

        /// <summary>
        /// Registra un elemento nella cronologia delle aperture recenti (AeroSpot Home).
        /// </summary>
        public void RecordRecentItem(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            lock (_lock)
            {
                EnsureOpenLocked();
                try
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO RecentHistory (FullPath, LastOpenedUtcTicks, OpenCount)
                        VALUES ($path, $ticks, 1)
                        ON CONFLICT(FullPath) DO UPDATE SET
                            LastOpenedUtcTicks = $ticks,
                            OpenCount = OpenCount + 1;
                    ";
                    cmd.Parameters.AddWithValue("$path", fullPath);
                    cmd.Parameters.AddWithValue("$ticks", DateTime.UtcNow.Ticks);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"SqliteIndexDatabase.RecordRecentItem fallito: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Restituisce la lista degli elementi aperti di recente ordinati per recenza decrescente.
        /// </summary>
        public List<IndexedItem> GetRecentItems(int limit = 10)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                return GetRecentItemsInternal(limit);
            }
        }

        private List<IndexedItem> GetRecentItemsInternal(int limit)
        {
            var recents = new List<IndexedItem>();
            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    SELECT i.Id, i.Title, i.FullPath, i.Category, i.Extension, i.ExecutionTarget, i.LastModifiedUtcTicks, i.IsApp
                    FROM RecentHistory r
                    INNER JOIN IndexedItems i ON (r.FullPath = i.FullPath OR r.FullPath = i.ExecutionTarget)
                    ORDER BY r.LastOpenedUtcTicks DESC
                    LIMIT $limit;
                ";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string fullPath = reader.GetString(2);
                    bool isApp = reader.GetInt32(7) == 1;

                    // Coerenza indice: se non è un'app registrata e il percorso fisico non esiste più, ignora
                    if (!isApp && !File.Exists(fullPath) && !Directory.Exists(fullPath))
                    {
                        continue;
                    }

                    recents.Add(new IndexedItem
                    {
                        Id = reader.GetInt64(0),
                        Title = reader.GetString(1),
                        FullPath = fullPath,
                        Category = "Recente",
                        Extension = reader.GetString(4),
                        ExecutionTarget = reader.GetString(5),
                        LastModifiedUtcTicks = reader.GetInt64(6),
                        IsApp = isApp
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"SqliteIndexDatabase.GetRecentItems fallito: {ex.Message}");
            }
            return recents;
        }

        /// <summary>
        /// Restituisce la lista degli elementi aperti più frequentemente ordinati per conteggio decrescente.
        /// </summary>
        public List<IndexedItem> GetMostFrequentItems(int limit = 10)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                return GetMostFrequentItemsInternal(limit);
            }
        }

        private List<IndexedItem> GetMostFrequentItemsInternal(int limit)
        {
            var frequents = new List<IndexedItem>();
            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    SELECT i.Id, i.Title, i.FullPath, i.Category, i.Extension, i.ExecutionTarget, i.LastModifiedUtcTicks, i.IsApp
                    FROM RecentHistory r
                    INNER JOIN IndexedItems i ON (r.FullPath = i.FullPath OR r.FullPath = i.ExecutionTarget)
                    ORDER BY r.OpenCount DESC, r.LastOpenedUtcTicks DESC
                    LIMIT $limit;
                ";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string fullPath = reader.GetString(2);
                    bool isApp = reader.GetInt32(7) == 1;

                    if (!isApp && !File.Exists(fullPath) && !Directory.Exists(fullPath))
                    {
                        continue;
                    }

                    frequents.Add(new IndexedItem
                    {
                        Id = reader.GetInt64(0),
                        Title = reader.GetString(1),
                        FullPath = fullPath,
                        Category = "Recente",
                        Extension = reader.GetString(4),
                        ExecutionTarget = reader.GetString(5),
                        LastModifiedUtcTicks = reader.GetInt64(6),
                        IsApp = isApp
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"SqliteIndexDatabase.GetMostFrequentItems fallito: {ex.Message}");
            }
            return frequents;
        }

        /// <summary>
        /// Registra una query di ricerca nella cronologia delle ricerche frequenti.
        /// Ignora stringhe con meno di 2 caratteri ed esclude espressioni di calcolo/conversione.
        /// </summary>
        public void RecordSearch(string rawQuery)
        {
            if (string.IsNullOrWhiteSpace(rawQuery)) return;
            string trimmed = rawQuery.Trim();

            // Esclude le espressioni di calcolo matematico o conversione di unità (Bugfix Novembre 2026 - P2)
            if (ExpressionEvaluator.TryEvaluate(trimmed) != null)
            {
                return;
            }

            string normalized = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ").ToLowerInvariant();
            if (normalized.Length < 2) return;

            lock (_lock)
            {
                EnsureOpenLocked();
                try
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO SearchHistory (NormalizedQuery, RawQuery, SearchCount, LastSearchedUtcTicks)
                        VALUES ($norm, $raw, 1, $ticks)
                        ON CONFLICT(NormalizedQuery) DO UPDATE SET
                            RawQuery = $raw,
                            SearchCount = SearchCount + 1,
                            LastSearchedUtcTicks = $ticks;
                    ";
                    cmd.Parameters.AddWithValue("$norm", normalized);
                    cmd.Parameters.AddWithValue("$raw", trimmed);
                    cmd.Parameters.AddWithValue("$ticks", DateTime.UtcNow.Ticks);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"SqliteIndexDatabase.RecordSearch fallito: {ex.Message}");
                }
            }
        }

        public void CleanupCalculatorSearches()
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                CleanupCalculatorSearchesInternal();
            }
        }

        private void CleanupCalculatorSearchesInternal()
        {
            try
            {
                var toDelete = new List<string>();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT NormalizedQuery, RawQuery FROM SearchHistory;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string norm = reader.GetString(0);
                    string raw = reader.GetString(1);
                    if (ExpressionEvaluator.TryEvaluate(raw) != null)
                    {
                        toDelete.Add(norm);
                    }
                }
                reader.Close();

                if (toDelete.Count > 0)
                {
                    using var delCmd = _connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM SearchHistory WHERE NormalizedQuery = $norm;";
                    var p = delCmd.CreateParameter();
                    p.ParameterName = "$norm";
                    delCmd.Parameters.Add(p);
                    foreach (var norm in toDelete)
                    {
                        p.Value = norm;
                        delCmd.ExecuteNonQuery();
                    }
                    AppLogger.Info($"SqliteIndexDatabase: Eliminate {toDelete.Count} espressioni matematiche/calcolo da SearchHistory.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"SqliteIndexDatabase.CleanupCalculatorSearchesInternal fallito: {ex.Message}");
            }
        }

        /// <summary>
        /// Restituisce l'elenco dei testi delle ricerche più frequenti ordinati per conteggio e recenza decrescente.
        /// </summary>
        public List<string> GetMostFrequentSearches(int limit = 10)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                return GetMostFrequentSearchesInternal(limit);
            }
        }

        private List<string> GetMostFrequentSearchesInternal(int limit)
        {
            var searches = new List<string>();
            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    SELECT RawQuery
                    FROM SearchHistory
                    ORDER BY SearchCount DESC, LastSearchedUtcTicks DESC
                    LIMIT $limit;
                ";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    searches.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"SqliteIndexDatabase.GetMostFrequentSearches fallito: {ex.Message}");
            }
            return searches;
        }

        /// <summary>
        /// Cancella completamente la cronologia delle aperture recenti.
        /// </summary>
        public void ClearRecentHistory()
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                try
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = "DELETE FROM RecentHistory;";
                    cmd.ExecuteNonQuery();
                    AppLogger.Info("SqliteIndexDatabase: Cronologia aperture recenti cancellata con successo.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"SqliteIndexDatabase.ClearRecentHistory fallito: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cancella completamente sia la cronologia degli elementi aperti sia la cronologia dei testi cercati.
        /// </summary>
        public void ClearAllHistory()
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                try
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = "DELETE FROM RecentHistory; DELETE FROM SearchHistory;";
                    cmd.ExecuteNonQuery();
                    AppLogger.Info("SqliteIndexDatabase: Tutta la cronologia (aperture e ricerche) cancellata con successo.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"SqliteIndexDatabase.ClearAllHistory fallito: {ex.Message}");
                }
            }
        }

        public void SetMeta(string key, string value)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO IndexMeta (Key, Value) VALUES ($k, $v)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                ";
                cmd.Parameters.AddWithValue("$k", key);
                cmd.Parameters.AddWithValue("$v", value);
                cmd.ExecuteNonQuery();
            }
        }

        public string? GetMeta(string key)
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT Value FROM IndexMeta WHERE Key = $k;";
                cmd.Parameters.AddWithValue("$k", key);
                object? res = cmd.ExecuteScalar();
                return res as string;
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                EnsureOpenLocked();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM IndexedItems; DELETE FROM IndexMeta;";
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureOpenLocked()
        {
            if (_connection == null)
            {
                Initialize();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    var conn = _connection;
                    _connection = null;
                    if (conn != null)
                    {
                        conn.Close();
                        SqliteConnection.ClearPool(conn);
                        conn.Dispose();
                    }
                }
                catch { }
            }
        }
    }
}
