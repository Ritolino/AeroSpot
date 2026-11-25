using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Config
{
    public enum SpotlightHomeMode
    {
        MostFrequentlyOpened = 0,
        MostFrequentSearches = 1,
        RecentOpened = 2,
        None = 3,
        Alphabetical = 4
    }

    public enum IndexUpdateFrequency
    {
        EveryStartup = 0,
        Every15Minutes = 1,
        Every30Minutes = 2,
        EveryHour = 3,
        Every2Hours = 4,
        ManualOnly = 5
    }

    public enum SpotlightDragDropMode
    {
        WindowsDefault = 0,
        AlwaysCopy = 1,
        AlwaysMove = 2
    }

    public sealed class AppConfig
    {
        private List<string>? _includedDrives;

        public int SchemaVersion { get; set; } = 5;

        public string Language { get; set; } = "auto";
        public bool StartWithWindows { get; set; } = true;
        public bool OpenFolderAfterConversion { get; set; } = true;
        public bool IntegrateWindowsContextMenu { get; set; } = false;
        public SpotlightHomeMode HomeDisplayMode { get; set; } = SpotlightHomeMode.MostFrequentSearches;
        public IndexUpdateFrequency IndexUpdateFrequency { get; set; } = IndexUpdateFrequency.EveryHour;
        public SpotlightDragDropMode DragDropMode { get; set; } = SpotlightDragDropMode.WindowsDefault;
        public bool EnableRealtimeWatcher { get; set; } = true;

        public bool EnableRecentHistory
        {
            get => HomeDisplayMode != SpotlightHomeMode.None;
            set
            {
                if (!value) HomeDisplayMode = SpotlightHomeMode.None;
                else if (HomeDisplayMode == SpotlightHomeMode.None) HomeDisplayMode = SpotlightHomeMode.MostFrequentSearches;
            }
        }

        public int WorkerIdleTimeoutSeconds { get; set; } = 30;
        public bool KeepWorkerWarm { get; set; } = true;

        // Gestione aggiornamenti da GitHub
        public string UpdateCheckFrequency { get; set; } = "Startup"; // Startup, Daily, Weekly, Monthly, Manual
        public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
        public string LastKnownVersion { get; set; } = string.Empty;

        // Customizable hotkeys
        public string SpotlightHotkeyText { get; set; } = "Alt + Space";
        public string QuickLookHotkeyText { get; set; } = "Space";
        public uint QuickLookKey { get; set; } = Native.Win32.VK_SPACE;
        public uint QuickLookModifiers { get; set; } = 0;

        public string DeleteHotkeyText { get; set; } = "Canc";
        public uint DeleteKey { get; set; } = Native.Win32.VK_DELETE;
        public uint DeleteModifiers { get; set; } = 0;

        public uint HotkeyModifiers { get; set; } = Native.Win32.MOD_ALT | Native.Win32.MOD_NOREPEAT;
        public uint HotkeyKey { get; set; } = Native.Win32.VK_SPACE;
        public uint FallbackHotkeyModifiers { get; set; } = Native.Win32.MOD_CONTROL | Native.Win32.MOD_NOREPEAT;
        public uint FallbackHotkeyKey { get; set; } = Native.Win32.VK_SPACE;
        public uint EmergencyHotkeyKey { get; set; } = Native.Win32.VK_F12;

        public string WorkerRelativePath { get; set; } = "AeroSpot.Worker.exe";

        // Saved window bounds for QuickLook preview window (-1 means automatic content-based sizing)
        public double PreviewWindowLeft { get; set; } = -1;
        public double PreviewWindowTop { get; set; } = -1;
        public double PreviewWindowWidth { get; set; } = -1;
        public double PreviewWindowHeight { get; set; } = -1;

        // QuickLook per-category persistent dimensions (disabled by default)
        public bool RememberSizeImages { get; set; } = false;
        public double SavedWidthImage { get; set; } = -1;
        public double SavedHeightImage { get; set; } = -1;

        public bool RememberSizeVideos { get; set; } = false;
        public double SavedWidthVideo { get; set; } = -1;
        public double SavedHeightVideo { get; set; } = -1;

        public bool RememberSizeAudio { get; set; } = false;
        public double SavedWidthAudio { get; set; } = -1;
        public double SavedHeightAudio { get; set; } = -1;

        public bool RememberSizePdf { get; set; } = false;
        public double SavedWidthPdf { get; set; } = -1;
        public double SavedHeightPdf { get; set; } = -1;

        public bool RememberSizeDocuments { get; set; } = false;
        public double SavedWidthDocument { get; set; } = -1;
        public double SavedHeightDocument { get; set; } = -1;

        public bool RememberSizeGeneric { get; set; } = false;
        public double SavedWidthGeneric { get; set; } = -1;
        public double SavedHeightGeneric { get; set; } = -1;

        // QuickLook behavior: Always on top (true) vs Contextual Topmost (false, default: active on open, backgroundable on deactivation)
        public bool QuickLookAlwaysOnTop { get; set; } = false;

        // Drive selection (lazily initialized on first access when not supplied by JSON)
        public List<string> IncludedDrives
        {
            get => _includedDrives ??= GetDefaultIncludedDrives();
            set => _includedDrives = value;
        }

        // Excluded folders and directories
        public List<string> ExcludedDirectoryNames { get; set; } = GetDefaultExcludedDirectories();

        // Spotlight search category hierarchy / priority order
        public List<string> CategoryPriority { get; set; } = GetDefaultCategoryPriority();

        // Gerarchia mista: se attiva, dà priorità assoluta solo alle corrispondenze esatte al 100% del nome (disattivata di default)
        public bool PrioritizeExactNameMatches { get; set; } = false;

        public static List<string> GetDefaultCategoryPriority()
        {
            return new List<string>
            {
                "Impostazioni",
                "Applicazione",
                "Documento",
                "Immagine",
                "Video",
                "Audio",
                "Cartella",
                "Archivio",
                "File"
            };
        }

        public static List<string> GetDefaultIncludedDrives()
        {
            var list = new List<string>();
            try
            {
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";
                list.Add(sysDrive);

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        string letter = drive.Name.TrimEnd('\\', ':').ToUpperInvariant();
                        if (!list.Contains(letter, StringComparer.OrdinalIgnoreCase))
                        {
                            list.Add(letter);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("AppConfig.GetDefaultIncludedDrives failed", ex);
                if (!list.Contains("C", StringComparer.OrdinalIgnoreCase)) list.Add("C");
            }

            if (list.Count == 0) list.Add("C");
            return list;
        }

        public static List<string> GetDefaultExcludedDirectories()
        {
            return new List<string>
            {
                "SteamLibrary",
                "Epic Games",
                @"AppData\Local\Temp",
                "$Recycle.Bin",
                ".git",
                "node_modules",
                "obj",
                "bin"
            };
        }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static string GetConfigPath()
        {
            return AppDataPath.GetSettingsPath();
        }

        public static AppConfig Load(string? customPath = null)
        {
            const int maxAttempts = 3;
            const int retryDelayMs = 40;

            string path = customPath ?? GetConfigPath();
            if (!File.Exists(path))
            {
                return new AppConfig();
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                    {
                        // Populate collection defaults if missing or empty after deserialization
                        if (cfg._includedDrives == null || cfg._includedDrives.Count == 0)
                        {
                            cfg._includedDrives = GetDefaultIncludedDrives();
                        }
                        if (cfg.ExcludedDirectoryNames == null || cfg.ExcludedDirectoryNames.Count == 0)
                        {
                            cfg.ExcludedDirectoryNames = GetDefaultExcludedDirectories();
                        }
                        if (cfg.CategoryPriority == null || cfg.CategoryPriority.Count == 0)
                        {
                            cfg.CategoryPriority = GetDefaultCategoryPriority();
                        }

                        // Automatic migration from previous schemas (without SchemaVersion or SchemaVersion < 2)
                        bool needsSave = false;
                        if (!json.Contains("\"SchemaVersion\"", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.SchemaVersion = 1;
                        }

                        if (cfg.SchemaVersion < 2)
                        {
                            cfg.SchemaVersion = 2;
                            needsSave = true;
                        }

                        if (cfg.SchemaVersion < 3)
                        {
                            cfg.SchemaVersion = 3;
                            cfg.EnableRecentHistory = true;
                            needsSave = true;
                        }

                        if (cfg.SchemaVersion < 4)
                        {
                            cfg.SchemaVersion = 4;
                            cfg.HomeDisplayMode = cfg.EnableRecentHistory ? SpotlightHomeMode.MostFrequentSearches : SpotlightHomeMode.None;
                            if (!cfg.CategoryPriority.Contains("Impostazioni", StringComparer.OrdinalIgnoreCase))
                            {
                                cfg.CategoryPriority.Insert(0, "Impostazioni");
                            }
                            needsSave = true;
                        }

                        if (cfg.SchemaVersion < 5)
                        {
                            cfg.SchemaVersion = 5;
                            cfg.PrioritizeExactNameMatches = false;
                            needsSave = true;
                        }

                        if (needsSave && customPath == null)
                        {
                            cfg.Save(path);
                        }
                        return cfg;
                    }
                    break;
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    AppLogger.Warn($"AppConfig.Load I/O error on attempt {attempt}/{maxAttempts} for '{path}'", ex);
                    Thread.Sleep(retryDelayMs);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"AppConfig.Load failed for '{path}', falling back to defaults", ex);
                    break;
                }
            }

            return new AppConfig();
        }

        public void Save(string? customPath = null)
        {
            string path = customPath ?? GetConfigPath();
            string tmpPath = path + ".tmp";
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"AppConfig.Save failed for '{path}'", ex);
                try
                {
                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                }
                catch { }
            }
        }
    }
}
