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
                Info($"Logger initialized for {componentName}");
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
