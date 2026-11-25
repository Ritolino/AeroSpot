using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

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

        public static List<IndexedItem> ScanApps()
        {
            var apps = new List<IndexedItem>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Aggiungi utilità di sistema Windows essenziali
            AddSystemUtilities(apps, seenTitles);

            // 2. Scansione COM shell:AppsFolder (copre sia UWP che app registrate)
            object? appsFolderObj = null;
            IShellItem? appsFolder = null;
            IntPtr pEnum = IntPtr.Zero;
            IEnumShellItems? enumerator = null;
            try
            {
                Win32.SHCreateItemFromParsingName(@"shell:AppsFolder", IntPtr.Zero, IID_IShellItem, out appsFolderObj);
                appsFolder = appsFolderObj as IShellItem;
                if (appsFolder != null)
                {
                    int hr = appsFolder.BindToHandler(IntPtr.Zero, BHID_EnumItems, IID_IEnumShellItems, out pEnum);
                    if (hr == 0 && pEnum != IntPtr.Zero)
                    {
                        enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(pEnum);
                        while (enumerator.Next(1, out IShellItem? item, out uint fetched) == 0 && fetched == 1 && item != null)
                        {
                            try
                            {
                                int hrName = item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out string name);
                                if (hrName != 0 || string.IsNullOrWhiteSpace(name)) continue;

                                int hrParse = item.GetDisplayName(SIGDN.SIGDN_PARENTRELATIVEPARSING, out string parsingName);
                                if (hrParse != 0) parsingName = string.Empty;

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

                                    // Se parsingName contiene una lettera di unità (es. D:\...), consenti solo i dischi abilitati in PathFilter
                                    int colonIdx = parsingName.IndexOf(':');
                                    if (colonIdx > 0 && colonIdx < parsingName.Length - 1 && parsingName[colonIdx + 1] == '\\')
                                    {
                                        char drive = char.ToUpperInvariant(parsingName[colonIdx - 1]);
                                        bool allowedDrive = false;
                                        foreach (var d in PathFilter.GetAllowedDriveLetters())
                                        {
                                            if (!string.IsNullOrEmpty(d) && char.ToUpperInvariant(d[0]) == drive)
                                            {
                                                allowedDrive = true;
                                                break;
                                            }
                                        }
                                        if (!allowedDrive)
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
                                if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Scansione COM shell:AppsFolder non riuscita: {ex.Message}");
            }
            finally
            {
                if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
                if (pEnum != IntPtr.Zero) Marshal.Release(pEnum);
                if (appsFolder != null && Marshal.IsComObject(appsFolder)) Marshal.ReleaseComObject(appsFolder);
                else if (appsFolderObj != null && Marshal.IsComObject(appsFolderObj)) Marshal.ReleaseComObject(appsFolderObj);
            }

            return apps;
        }

        private static void AddSystemUtilities(List<IndexedItem> apps, HashSet<string> seen)
        {
            string sysDir = Environment.SystemDirectory;
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(winDir))
            {
                winDir = Directory.GetParent(sysDir)?.FullName ?? @"C:\Windows";
            }

            var utilities = new (string Title, string Path)[]
            {
                ("Blocco note", Path.Combine(sysDir, "notepad.exe")),
                ("Calcolatrice", "calc.exe"),
                ("Terminale di Windows", "wt.exe"),
                ("Prompt dei comandi", Path.Combine(sysDir, "cmd.exe")),
                ("PowerShell", Path.Combine(sysDir, "WindowsPowerShell", "v1.0", "powershell.exe")),
                ("Gestione attività", Path.Combine(sysDir, "taskmgr.exe")),
                ("Esplora file", Path.Combine(winDir, "explorer.exe")),
                ("Editor del Registro di sistema", Path.Combine(winDir, "regedit.exe")),
                ("Paint", Path.Combine(sysDir, "mspaint.exe")),
                ("Strumento di cattura", Path.Combine(sysDir, "SnippingTool.exe"))
            };

            foreach (var u in utilities)
            {
                if (seen.Add(u.Title))
                {
                    apps.Add(new IndexedItem(u.Title, u.Path, "Applicazione", u.Path, isApp: true, extension: ".exe"));
                }
            }
        }
    }
}
