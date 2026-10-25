using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Search
{
    public static class AppsFolderScanner
    {
        private static readonly Guid BHID_EnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
        private static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        private static readonly Guid IID_IEnumShellItems = new Guid("70629033-e363-4a28-a567-0db78006e6d7");

        public enum SIGDN : uint
        {
            SIGDN_NORMALDISPLAY = 0x00000000,
            SIGDN_PARENTRELATIVEPARSING = 0x80018001,
            SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
            SIGDN_FILESYSPATH = 0x80058000
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig]
            int BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            [PreserveSig]
            int GetParent(out IShellItem ppsi);
            [PreserveSig]
            int GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            [PreserveSig]
            int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig]
            int Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport]
        [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumShellItems
        {
            [PreserveSig]
            int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem? rgelt, out uint pceltFetched);
            [PreserveSig]
            int Skip(uint celt);
            [PreserveSig]
            int Reset();
            [PreserveSig]
            int Clone(out IEnumShellItems ppenum);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

        public static List<IndexedItem> ScanApps()
        {
            var apps = new List<IndexedItem>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Aggiungi utilità di sistema Windows essenziali
            AddSystemUtilities(apps, seenTitles);

            // 2. Scansione COM shell:AppsFolder (copre sia UWP che app registrate)
            try
            {
                int hr = SHCreateItemFromParsingName(@"shell:AppsFolder", IntPtr.Zero, IID_IShellItem, out IShellItem? appsFolder);
                if (hr == 0 && appsFolder != null)
                {
                    hr = appsFolder.BindToHandler(IntPtr.Zero, BHID_EnumItems, IID_IEnumShellItems, out IntPtr pEnum);
                    if (hr == 0 && pEnum != IntPtr.Zero)
                    {
                        var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(pEnum);
                        while (enumerator.Next(1, out IShellItem? item, out uint fetched) == 0 && fetched == 1 && item != null)
                        {
                            try
                            {
                                string name = string.Empty;
                                string parsingName = string.Empty;

                                item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out name);
                                item.GetDisplayName(SIGDN.SIGDN_PARENTRELATIVEPARSING, out parsingName);

                                if (string.IsNullOrWhiteSpace(name)) continue;

                                // Escludi disinstallatori o helper poco pertinenti
                                if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("Disinstalla", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                // Escludi se il parsing name o target ricade in directory o volumi esclusi
                                if (!string.IsNullOrEmpty(parsingName))
                                {
                                    if (parsingName.IndexOf(@"\Epic Games\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        parsingName.IndexOf(@"\SteamLibrary\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        parsingName.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        continue;
                                    }

                                    // Se parsingName contiene una lettera di unità (es. D:\...), consenti solo C: ed E:
                                    int colonIdx = parsingName.IndexOf(':');
                                    if (colonIdx > 0 && colonIdx < parsingName.Length - 1)
                                    {
                                        char drive = char.ToUpperInvariant(parsingName[colonIdx - 1]);
                                        if (drive != 'C' && drive != 'E')
                                        {
                                            continue;
                                        }
                                    }
                                }

                                if (seenTitles.Add(name))
                                {
                                    // Target di esecuzione tramite shell:AppsFolder
                                    string fullTarget = !string.IsNullOrEmpty(parsingName) ? $@"shell:AppsFolder\{parsingName}" : name;
                                    apps.Add(new IndexedItem(name, fullTarget, "Applicazione", fullTarget, isApp: true, extension: ".exe"));
                                }
                            }
                            catch { }
                            finally
                            {
                                Marshal.ReleaseComObject(item);
                            }
                        }
                        Marshal.Release(pEnum);
                    }
                    Marshal.ReleaseComObject(appsFolder);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Scansione COM shell:AppsFolder non riuscita, ricorso a Start Menu: {ex.Message}");
            }

            // 3. Scansione di fallback/arricchimento dai collegamenti .lnk del Menu Start
            ScanStartMenuShortcuts(apps, seenTitles);

            return apps;
        }

        private static void AddSystemUtilities(List<IndexedItem> apps, HashSet<string> seen)
        {
            var utilities = new (string Title, string Path)[]
            {
                ("Blocco note", @"C:\Windows\System32\notepad.exe"),
                ("Calcolatrice", "calc.exe"),
                ("Terminale di Windows", "wt.exe"),
                ("Prompt dei comandi", @"C:\Windows\System32\cmd.exe"),
                ("PowerShell", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
                ("Gestione attività", @"C:\Windows\System32\taskmgr.exe"),
                ("Esplora file", @"C:\Windows\explorer.exe"),
                ("Editor del Registro di sistema", @"C:\Windows\regedit.exe"),
                ("Paint", @"C:\Windows\System32\mspaint.exe"),
                ("Strumento di cattura", @"C:\Windows\System32\SnippingTool.exe")
            };

            foreach (var u in utilities)
            {
                if (seen.Add(u.Title))
                {
                    apps.Add(new IndexedItem(u.Title, u.Path, "Applicazione", u.Path, isApp: true, extension: ".exe"));
                }
            }
        }

        private static void ScanStartMenuShortcuts(List<IndexedItem> apps, HashSet<string> seen)
        {
            var startPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            foreach (var root in startPaths)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                try
                {
                    var files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            if (!PathFilter.IsPathAllowed(file))
                            {
                                continue;
                            }

                            string title = Path.GetFileNameWithoutExtension(file);
                            if (string.IsNullOrWhiteSpace(title)) continue;

                            if (title.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                title.StartsWith("Disinstalla", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (seen.Add(title))
                            {
                                apps.Add(new IndexedItem(title, file, "Applicazione", file, isApp: true, extension: ".lnk"));
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }
}
