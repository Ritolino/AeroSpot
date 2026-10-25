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
                Cache = SqliteCacheMode.Shared
            };
            _connectionString = builder.ToString();
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
                        CREATE INDEX IF NOT EXISTS idx_items_path ON IndexedItems(FullPath);
                        CREATE INDEX IF NOT EXISTS idx_items_category ON IndexedItems(Category);

                        CREATE TABLE IF NOT EXISTS IndexMeta (
                            Key TEXT PRIMARY KEY,
                            Value TEXT NOT NULL
                        );
                    ";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public int UpsertBatch(IEnumerable<IndexedItem> items)
        {
            EnsureOpen();
            lock (_lock)
            {
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
            EnsureOpen();
            lock (_lock)
            {
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

        public List<IndexedItem> Search(string query, int maxResults = 10)
        {
            EnsureOpen();
            var candidates = new List<IndexedItem>();

            lock (_lock)
            {
                using var cmd = _connection!.CreateCommand();

                if (string.IsNullOrWhiteSpace(query))
                {
                    // Se la query è vuota, restituisce le applicazioni principali
                    cmd.CommandText = @"
                        SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                        FROM IndexedItems
                        WHERE IsApp = 1
                        ORDER BY Title
                        LIMIT $limit;
                    ";
                    cmd.Parameters.AddWithValue("$limit", maxResults);
                }
                else
                {
                    string[] terms = query.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var whereClauses = new List<string>();

                    for (int i = 0; i < terms.Length; i++)
                    {
                        string paramName = $"$term{i}";
                        whereClauses.Add($"(Title LIKE {paramName} OR FullPath LIKE {paramName})");
                        cmd.Parameters.AddWithValue(paramName, $"%{terms[i]}%");
                    }

                    cmd.CommandText = $@"
                        SELECT Id, Title, FullPath, Category, Extension, ExecutionTarget, LastModifiedUtcTicks, IsApp
                        FROM IndexedItems
                        WHERE {string.Join(" AND ", whereClauses)}
                        LIMIT 150;
                    ";
                }

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

            // Applica il ranking avanzato a multi-segnale
            return SearchRanker.Rank(candidates, query, maxResults);
        }

        public int GetTotalCount()
        {
            EnsureOpen();
            lock (_lock)
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM IndexedItems;";
                object? res = cmd.ExecuteScalar();
                return res != null ? Convert.ToInt32(res) : 0;
            }
        }

        public void SetMeta(string key, string value)
        {
            EnsureOpen();
            lock (_lock)
            {
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
            EnsureOpen();
            lock (_lock)
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT Value FROM IndexMeta WHERE Key = $k;";
                cmd.Parameters.AddWithValue("$k", key);
                object? res = cmd.ExecuteScalar();
                return res as string;
            }
        }

        public void ClearAll()
        {
            EnsureOpen();
            lock (_lock)
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM IndexedItems; DELETE FROM IndexMeta;";
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureOpen()
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
                    _connection?.Close();
                    _connection?.Dispose();
                    _connection = null;
                }
                catch { }
            }
        }
    }
}
