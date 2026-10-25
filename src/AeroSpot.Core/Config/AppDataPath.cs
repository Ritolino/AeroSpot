using System;
using System.IO;

namespace AeroSpot.Core.Config
{
    /// <summary>
    /// Resolves the application data directory based on portable marker (portable.txt) or %LocalAppData%.
    /// </summary>
    public static class AppDataPath
    {
        private static readonly object _lock = new object();
        private static string? _cachedDataDir;
        private static bool _isPortable;
        private static bool _fallbackOccurred;

        public static bool IsPortable
        {
            get
            {
                EnsureInitialized();
                return _isPortable;
            }
        }

        public static bool FallbackOccurred
        {
            get
            {
                EnsureInitialized();
                return _fallbackOccurred;
            }
        }

        public static string GetDataDirectory()
        {
            EnsureInitialized();
            return _cachedDataDir!;
        }

        public static string GetSettingsPath() => Path.Combine(GetDataDirectory(), "settings.json");

        public static string GetDatabasePath() => Path.Combine(GetDataDirectory(), "index.db");

        public static string GetLogsDirectory()
        {
            string dir = Path.Combine(GetDataDirectory(), "logs");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        /// <summary>
        /// Resets cached path (useful for testing or when moving folders).
        /// </summary>
        public static void ResetCache()
        {
            lock (_lock)
            {
                _cachedDataDir = null;
                _isPortable = false;
                _fallbackOccurred = false;
            }
        }

        private static void EnsureInitialized()
        {
            if (_cachedDataDir != null) return;

            lock (_lock)
            {
                if (_cachedDataDir != null) return;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string markerPath = Path.Combine(baseDir, "portable.txt");
                string parentMarkerPath = Path.Combine(baseDir, "..", "portable.txt");

                bool isPortableMarker = File.Exists(markerPath) || File.Exists(parentMarkerPath);

                if (isPortableMarker)
                {
                    string folderName = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string portableData;
                    if (folderName.Equals("app", StringComparison.OrdinalIgnoreCase) || folderName.Equals("bin", StringComparison.OrdinalIgnoreCase))
                    {
                        portableData = Path.GetFullPath(Path.Combine(baseDir, "..", "Data"));
                    }
                    else
                    {
                        portableData = Path.Combine(baseDir, "Data");
                    }

                    try
                    {
                        if (!Directory.Exists(portableData))
                        {
                            Directory.CreateDirectory(portableData);
                        }

                        // Verify write permissions
                        string probeFile = Path.Combine(portableData, $".write_test_{Guid.NewGuid():N}.tmp");
                        File.WriteAllText(probeFile, "test");
                        File.Delete(probeFile);

                        _isPortable = true;
                        _cachedDataDir = portableData;
                        return;
                    }
                    catch
                    {
                        _fallbackOccurred = true;
                    }
                }

                // Default standard mode: %LocalAppData%\AeroSpot
                _isPortable = false;
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Path.Combine(localAppData, "AeroSpot");
                if (!Directory.Exists(appData))
                {
                    try { Directory.CreateDirectory(appData); } catch { }
                }
                _cachedDataDir = appData;
            }
        }
    }
}
