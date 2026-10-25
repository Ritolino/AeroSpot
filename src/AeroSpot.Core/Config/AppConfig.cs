using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AeroSpot.Core.Config
{
    public sealed class AppConfig
    {
        public int SchemaVersion { get; set; } = 2;

        public string Language { get; set; } = "auto";
        public bool StartWithWindows { get; set; } = false;

        public int WorkerIdleTimeoutSeconds { get; set; } = 30;
        public bool KeepWorkerWarm { get; set; } = true;

        // Customizable hotkeys
        public string SpotlightHotkeyText { get; set; } = "Alt + Space";
        public string QuickLookHotkeyText { get; set; } = "Space";
        public uint QuickLookKey { get; set; } = Native.Win32.VK_SPACE;
        public uint QuickLookModifiers { get; set; } = 0;

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

        // Drive selection (default: system drive + detected local fixed drives)
        public List<string> IncludedDrives { get; set; } = GetDefaultIncludedDrives();

        // Excluded folders and directories
        public List<string> ExcludedDirectoryNames { get; set; } = GetDefaultExcludedDirectories();

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
            catch
            {
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
            try
            {
                string path = customPath ?? GetConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                    {
                        // Automatic migration from previous schemas (without SchemaVersion or SchemaVersion < 2)
                        bool needsSave = false;
                        if (cfg.SchemaVersion < 2)
                        {
                            cfg.SchemaVersion = 2;
                            if (cfg.IncludedDrives == null || cfg.IncludedDrives.Count == 0)
                            {
                                cfg.IncludedDrives = GetDefaultIncludedDrives();
                            }
                            if (cfg.ExcludedDirectoryNames == null || cfg.ExcludedDirectoryNames.Count == 0)
                            {
                                cfg.ExcludedDirectoryNames = GetDefaultExcludedDirectories();
                            }
                            needsSave = true;
                        }

                        if (needsSave && customPath == null)
                        {
                            cfg.Save(path);
                        }
                        return cfg;
                    }
                }
            }
            catch { }
            return new AppConfig();
        }

        public void Save(string? customPath = null)
        {
            try
            {
                string path = customPath ?? GetConfigPath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
