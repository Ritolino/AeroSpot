using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace AeroSpot.Core.Preview
{
    public static class PreviewHandlerResolver
    {
        private const string PreviewHandlerSubKey = @"ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}";
        private const string PreviewHandlersListKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers";

        // Blacklist globale di CLSID COM Office che avviano processi zombie (-Embedding / /Embedding)
        // o che sono già sostituiti dai lettori nativi in-process di AeroSpot.
        private static readonly HashSet<Guid> BlockedHandlerClsids = new()
        {
            new Guid("84F66100-FF7C-4fb4-B0C0-02CD7FB668FE"), // Microsoft Word previewer (WINWORD.EXE -Embedding)
            new Guid("00020827-0000-0000-C000-000000000046"), // Microsoft Excel previewer (EXCEL.EXE /Embedding)
            new Guid("65235197-874B-4A07-BDC5-E65EA825B718"), // Microsoft PowerPoint previewer
        };

        private static readonly ConcurrentDictionary<string, Guid?> _clsidCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<Guid, string> _nameCache = new();

        public static bool IsBlockedHandlerClsid(Guid clsid) =>
            clsid != Guid.Empty && BlockedHandlerClsids.Contains(clsid);

        public static bool TryGetHandlerClsid(string filePathOrExtension, out Guid clsid)
        {
            clsid = Guid.Empty;
            string ext = Path.GetExtension(filePathOrExtension);
            if (string.IsNullOrWhiteSpace(ext)) return false;

            if (!ext.StartsWith('.'))
            {
                ext = "." + ext;
            }

            if (_clsidCache.TryGetValue(ext, out var cached))
            {
                if (cached.HasValue && !IsBlockedHandlerClsid(cached.Value))
                {
                    clsid = cached.Value;
                    return true;
                }
                return false;
            }

            Guid? resolved = ResolveClsidFromRegistry(ext);
            if (resolved.HasValue && IsBlockedHandlerClsid(resolved.Value))
            {
                resolved = null;
            }

            _clsidCache[ext] = resolved;

            if (resolved.HasValue)
            {
                clsid = resolved.Value;
                return true;
            }

            return false;
        }

        private static Guid? ResolveClsidFromRegistry(string ext)
        {
            try
            {
                // 1. Prova diretta su HKCR\<.ext>\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}
                using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
                if (extKey != null)
                {
                    using var shellExKey = extKey.OpenSubKey(PreviewHandlerSubKey);
                    if (shellExKey != null)
                    {
                        string? guidStr = shellExKey.GetValue(string.Empty) as string;
                        if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out var g))
                        {
                            return g;
                        }
                    }

                    // 2. Prova tramite ProgID puntato da HKCR\<.ext>
                    string? progId = extKey.GetValue(string.Empty) as string;
                    if (!string.IsNullOrEmpty(progId))
                    {
                        using var progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
                        if (progIdKey != null)
                        {
                            using var progShellExKey = progIdKey.OpenSubKey(PreviewHandlerSubKey);
                            if (progShellExKey != null)
                            {
                                string? guidStr = progShellExKey.GetValue(string.Empty) as string;
                                if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out var g))
                                {
                                    return g;
                                }
                            }
                        }
                    }
                }

                // 3. Prova su HKCR\SystemFileAssociations\<.ext>\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}
                using var sysAssocKey = Registry.ClassesRoot.OpenSubKey(@"SystemFileAssociations\" + ext + @"\" + PreviewHandlerSubKey);
                if (sysAssocKey != null)
                {
                    string? guidStr = sysAssocKey.GetValue(string.Empty) as string;
                    if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out var g))
                    {
                        return g;
                    }
                }
            }
            catch { }

            return null;
        }

        public static string? GetHandlerFriendlyName(Guid clsid)
        {
            if (_nameCache.TryGetValue(clsid, out var name)) return name;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PreviewHandlersListKey);
                if (key != null)
                {
                    string guidStr = clsid.ToString("B").ToUpperInvariant();
                    string? friendly = key.GetValue(guidStr) as string;
                    if (!string.IsNullOrEmpty(friendly))
                    {
                        _nameCache[clsid] = friendly;
                        return friendly;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
