using System;
using System.IO;

namespace AeroSpot.Core.Logging
{
    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private static string? _logFilePath;

        public static void Initialize(string componentName)
        {
            try
            {
                string logDir = Config.AppDataPath.GetLogsDirectory();
                _logFilePath = Path.Combine(logDir, $"{componentName.ToLowerInvariant()}.log");

                // Bonifica asincrona: mantiene esclusivamente i log delle ultime 24 ore
                System.Threading.Tasks.Task.Run(() => PruneLogs(logDir, TimeSpan.FromHours(24)));

                Info($"Logger initialized for {componentName}");
            }
            catch { }
        }

        /// <summary>
        /// Elimina o tronca i record di log antecedenti all'intervallo massimo specificato (predefinito: 24 ore).
        /// </summary>
        public static void PruneLogs(string logDir, TimeSpan maxAge)
        {
            try
            {
                if (!Directory.Exists(logDir)) return;

                DateTime cutoff = DateTime.Now - maxAge;
                var files = Directory.GetFiles(logDir, "*.log");

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (!fileInfo.Exists) continue;

                        // Se il file non è stato toccato da più di 24 ore, azzeralo
                        if (fileInfo.LastWriteTime < cutoff)
                        {
                            lock (_lock)
                            {
                                File.WriteAllText(file, string.Empty);
                            }
                            continue;
                        }

                        // Leggi le righe e rimuovi quelle più vecchie del cutoff
                        string[] lines;
                        lock (_lock)
                        {
                            lines = File.ReadAllLines(file);
                        }

                        if (lines.Length == 0) continue;

                        int firstKeptIndex = -1;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string l = lines[i];
                            // Cerca timestamp nel formato [yyyy-MM-dd HH:mm:ss
                            if (l.StartsWith("[") && l.Length >= 20 &&
                                DateTime.TryParse(l.Substring(1, 19), out DateTime lineDt))
                            {
                                if (lineDt >= cutoff)
                                {
                                    firstKeptIndex = i;
                                    break;
                                }
                            }
                        }

                        // Se le prime righe sono vecchie, riscrivi il file mantenendo solo quelle recenti
                        if (firstKeptIndex > 0)
                        {
                            var keptLines = new string[lines.Length - firstKeptIndex];
                            Array.Copy(lines, firstKeptIndex, keptLines, 0, keptLines.Length);

                            lock (_lock)
                            {
                                File.WriteAllLines(file, keptLines);
                            }
                        }
                        else if (firstKeptIndex == -1 && lines.Length > 0)
                        {
                            // Tutte le righe con timestamp erano più vecchie del cutoff
                            lock (_lock)
                            {
                                File.WriteAllText(file, string.Empty);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void Debug(string message) => Log("DEBUG", message);
        public static void Info(string message) => Log("INFO", message);
        public static void Warn(string message, Exception? ex = null)
        {
            string fullMsg = ex != null ? $"{message} - {ex.GetType().Name}: {ex.Message}" : message;
            Log("WARN", fullMsg);
        }
        public static void Error(string message, Exception? ex = null)
        {
            string fullMsg = ex != null ? $"{message} - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
            Log("ERROR", fullMsg);
        }

        private static void Log(string level, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
            try
            {
                lock (_lock)
                {
                    if (_logFilePath != null)
                    {
                        File.AppendAllText(_logFilePath, line + Environment.NewLine);
                    }
                }
            }
            catch { }
        }
    }
}
