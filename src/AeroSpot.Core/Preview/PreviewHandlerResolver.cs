using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Win32;

namespace AeroSpot.Core.Preview
{
    public static class PreviewHandlerResolver
    {
        private const string PreviewHandlerSubKey = @"ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}";
        private const string PreviewHandlersListKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers";

        private static readonly ConcurrentDictionary<string, Guid?> _clsidCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<Guid, string> _nameCache = new();

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
                if (cached.HasValue)
                {
                    clsid = cached.Value;
                    return true;
                }
                return false;
            }

            Guid? resolved = ResolveClsidFromRegistry(ext);
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
