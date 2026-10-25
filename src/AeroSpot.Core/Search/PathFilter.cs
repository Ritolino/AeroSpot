using System;
using System.Collections.Generic;
using System.IO;

namespace AeroSpot.Core.Search
{
    public static class PathFilter
    {
        private static HashSet<char> _allowedDriveLetters = InitializeDefaultDrives();

        private static HashSet<char> InitializeDefaultDrives()
        {
            var set = new HashSet<char>();
            foreach (var d in Config.AppConfig.GetDefaultIncludedDrives())
            {
                if (!string.IsNullOrWhiteSpace(d))
                {
                    char c = char.ToUpperInvariant(d.Trim()[0]);
                    set.Add(c);
                    set.Add(char.ToLowerInvariant(c));
                }
            }
            return set;
        }

        private static List<string> _excludedSegments = new List<string>
        {
            @"\steamlibrary\",
            @"\epic games\",
            @"\appdata\local\temp\",
            @"\$recycle.bin\",
            @"\system volume information\",
            @"\.git\",
            @"\node_modules\",
            @"\obj\",
            @"\bin\"
        };

        private static readonly string[] AllowedWindowsExecutables = new[]
        {
            "notepad.exe",
            "calc.exe",
            "cmd.exe",
            "explorer.exe",
            "taskmgr.exe",
            "regedit.exe",
            "mspaint.exe",
            "wt.exe",
            "cleanmgr.exe",
            "snippingtool.exe"
        };

        public static void Configure(IEnumerable<string> drives, IEnumerable<string>? exclusions = null)
        {
            var newDrives = new HashSet<char>();
            foreach (var d in drives)
            {
                if (!string.IsNullOrWhiteSpace(d))
                {
                    char c = char.ToUpperInvariant(d.Trim()[0]);
                    newDrives.Add(c);
                    newDrives.Add(char.ToLowerInvariant(c));
                }
            }
            if (newDrives.Count > 0)
            {
                _allowedDriveLetters = newDrives;
            }

            if (exclusions != null)
            {
                var newExclusions = new List<string>();
                foreach (var exc in exclusions)
                {
                    if (!string.IsNullOrWhiteSpace(exc))
                    {
                        string s = exc.Trim().Replace('/', '\\').ToLowerInvariant();
                        if (!s.StartsWith('\\')) s = "\\" + s;
                        if (!s.EndsWith('\\')) s = s + "\\";
                        newExclusions.Add(s);
                    }
                }
                _excludedSegments = newExclusions;
            }
        }

        public static void ResetToDefault()
        {
            _allowedDriveLetters = new HashSet<char> { 'C', 'c', 'E', 'e' };
            _excludedSegments = new List<string>
            {
                @"\steamlibrary\",
                @"\epic games\",
                @"\appdata\local\temp\",
                @"\$recycle.bin\",
                @"\system volume information\",
                @"\.git\",
                @"\node_modules\",
                @"\obj\",
                @"\bin\"
            };
        }

        public static bool IsPathAllowed(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;

            string normalized = fullPath.Trim().Replace('/', '\\');

            // 1. Vincolo volumi: ammessi rigorosamente ed esclusivamente i dischi configurati
            if (normalized.Length < 3 || normalized[1] != ':' || normalized[2] != '\\')
            {
                return false;
            }

            char driveLetter = normalized[0];
            if (!_allowedDriveLetters.Contains(driveLetter))
            {
                return false;
            }

            string lower = normalized.ToLowerInvariant();
            string lowerWithSlash = lower.EndsWith('\\') ? lower : lower + "\\";

            // 2. Esclusioni di specifica sui confini di segmento
            foreach (var segment in _excludedSegments)
            {
                if (lowerWithSlash.Contains(segment))
                {
                    return false;
                }
            }

            // 3. Trattamento speciale cartella Windows: escluso dalla scansione generale
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            if (lower.StartsWith(winDir + "\\") || lower == winDir)
            {
                string fileName = Path.GetFileName(lower);
                foreach (var exe in AllowedWindowsExecutables)
                {
                    if (string.Equals(fileName, exe, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            return true;
        }

        public static bool IsReparsePointOrJunction(FileAttributes attributes)
        {
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }

        public static IEnumerable<string> GetWhitelistedScanRoots()
        {
            var roots = new List<string>();
            char sysDriveChar = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant()[0] ?? 'C';

            // Aggiungi le radici dei dischi configurati abilitati (escludendo l'unità di sistema gestita selettivamente sotto)
            foreach (var d in _allowedDriveLetters)
            {
                if (char.IsUpper(d))
                {
                    string rootPath = $"{d}:\\";
                    if (d != sysDriveChar && Directory.Exists(rootPath))
                    {
                        roots.Add(rootPath);
                    }
                }
            }

            // Radici pertinenti sull'unità di sistema (applicazioni, menu start e cartelle utente)
            if (_allowedDriveLetters.Contains(sysDriveChar) || _allowedDriveLetters.Contains(char.ToLowerInvariant(sysDriveChar)))
            {
                AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
                AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
                AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
                AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    AddIfExists(roots, Path.Combine(userProfile, "Downloads"));
                    AddIfExists(roots, Path.Combine(userProfile, "Pictures"));
                    AddIfExists(roots, Path.Combine(userProfile, "Videos"));
                    AddIfExists(roots, Path.Combine(userProfile, "Music"));
                }
            }

            return roots;
        }

        private static void AddIfExists(List<string> list, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && IsPathAllowed(path))
            {
                list.Add(path);
            }
        }
    }
}
