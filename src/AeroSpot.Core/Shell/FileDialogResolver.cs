using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

namespace AeroSpot.Core.Shell
{
    /// <summary>
    /// Risolve il file o la cartella selezionata all'interno dei selettori di sistema
    /// Apri / Salva / Allega (#32770 e FileDialog) ospitati in applicazioni terze
    /// (es. WhatsApp, Chrome, Blocco Note, Office, Discord, ecc.).
    /// Utilizza le API ufficiali Microsoft UI Automation (zero iniezioni DLL, zero falsi positivi antivirus).
    /// </summary>
    public static class FileDialogResolver
    {
        private static readonly Regex DrivePathRegex = new Regex(@"[A-Za-z]:\\[^""<>|:*?]+", RegexOptions.Compiled);
        private static readonly object CacheLock = new object();
        private const long CacheTtlMs = 250;
        private static IntPtr _lastRootHwnd;
        private static IntPtr _lastFocusHwnd;
        private static string _lastAddrText = string.Empty;
        private static string _lastEditText = string.Empty;
        private static string _lastFocusedName = string.Empty;
        private static string? _lastFilePath;
        private static bool _lastResult;
        private static long _lastQueryTickMs;

        /// <summary>
        /// Determina se l'handle specificato (o un suo antenato radice) appartiene a un selettore di file (#32770).
        /// </summary>
        public static bool IsFileDialog(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            IntPtr root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;

            string className = ExplorerFocusResolver.GetWindowClassName(root);
            if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Esclude le finestre native di Esplora Risorse o Desktop
            if (ExplorerFocusResolver.IsExplorerOrDesktop(root))
            {
                return false;
            }

            // Verifica identificatori di controllo standard dei dialoghi Common Item / FileDialog:
            // 1148 = cmb13 (ComboBox / Edit 'Nome file:' in modern dialogs)
            // 1152 = edt1 (Edit 'Nome file:' in classic dialogs)
            // 1001 = Barra degli indirizzi / Breadcrumb toolbar
            if (Win32.GetDlgItem(root, 1148) != IntPtr.Zero ||
                Win32.GetDlgItem(root, 1152) != IntPtr.Zero ||
                Win32.GetDlgItem(root, 1001) != IntPtr.Zero)
            {
                return true;
            }

            // Verifica presenza di DirectUIHWND (il contenitore della vista file) tra i controlli figli
            bool hasDirectUI = false;
            Win32.EnumChildWindows(root, (child, _) =>
            {
                string childClass = ExplorerFocusResolver.GetWindowClassName(child);
                if (childClass.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase) ||
                    childClass.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                    childClass.Equals("Address Band Root", StringComparison.OrdinalIgnoreCase))
                {
                    hasDirectUI = true;
                    return false; // Stop enumeration
                }
                return true;
            }, IntPtr.Zero);

            return hasDirectUI;
        }

        /// <summary>
        /// Tenta di estrarre il percorso completo del file attualmente selezionato nel selettore di file.
        /// </summary>
        public static bool TryGetFocusedPath(IntPtr dialogHwnd, out string? filePath)
        {
            filePath = null;
            if (dialogHwnd == IntPtr.Zero) return false;

            IntPtr root = Win32.GetAncestor(dialogHwnd, Win32.GA_ROOT);
            if (root == IntPtr.Zero) root = dialogHwnd;

            // Guardia / cache leggera per evitare ripetuti walk UIA Descendants se nulla è cambiato
            IntPtr focusHwnd = IntPtr.Zero;
            string addrText = string.Empty;
            string editText = string.Empty;
            string focusedName = string.Empty;

            try
            {
                uint tid = Win32.GetWindowThreadProcessId(root, out _);
                var gui = new Win32.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.GUITHREADINFO>() };
                if (tid != 0 && Win32.GetGUIThreadInfo(tid, ref gui))
                {
                    focusHwnd = gui.hwndFocus;
                }

                IntPtr h1001 = Win32.GetDlgItem(root, 1001);
                if (h1001 != IntPtr.Zero) addrText = ExplorerFocusResolver.GetWindowTitle(h1001);

                IntPtr h1148 = Win32.GetDlgItem(root, 1148);
                if (h1148 != IntPtr.Zero) editText = ExplorerFocusResolver.GetWindowTitle(h1148);

                var focusedEl = AutomationElement.FocusedElement;
                if (focusedEl != null) focusedName = focusedEl.Current.Name ?? string.Empty;

                lock (CacheLock)
                {
                    long nowMs = Environment.TickCount64;
                    if (_lastRootHwnd == root &&
                        nowMs - _lastQueryTickMs <= CacheTtlMs &&
                        _lastFocusHwnd == focusHwnd &&
                        string.Equals(_lastAddrText, addrText, StringComparison.Ordinal) &&
                        string.Equals(_lastEditText, editText, StringComparison.Ordinal) &&
                        string.Equals(_lastFocusedName, focusedName, StringComparison.Ordinal))
                    {
                        filePath = _lastFilePath;
                        return _lastResult;
                    }
                }
            }
            catch { }

            bool resolved = false;
            try
            {
                var dlg = AutomationElement.FromHandle(root);
                if (dlg == null) return false;

                // 1. Risolvi il percorso della cartella corrente dalla barra degli indirizzi (Toolbar 1001)
                string? folderPath = ResolveFolderPath(root, dlg);

                // 2. Risolvi il nome dell'elemento selezionato nella vista file
                string? fileName = ResolveSelectedFileName(root, dlg);

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    AppLogger.Debug("FileDialogResolver: Nessun file selezionato nella vista del dialogo.");
                    return false;
                }

                // 3. Combina cartella e nome file per ottenere il percorso assoluto verificato
                if (!string.IsNullOrEmpty(folderPath))
                {
                    string direct = Path.Combine(folderPath, fileName);
                    if (File.Exists(direct) || Directory.Exists(direct))
                    {
                        filePath = direct;
                        resolved = true;
                        AppLogger.Info($"FileDialogResolver: File selezionato risolto con successo: {filePath}");
                        return true;
                    }

                    // Se l'estensione è nascosta da Windows Explorer, ricerca per corrispondenza di nome base
                    try
                    {
                        var matches = Directory.GetFiles(folderPath, fileName + ".*");
                        if (matches.Length > 0)
                        {
                            filePath = matches[0];
                            resolved = true;
                            AppLogger.Info($"FileDialogResolver: File risolto tramite corrispondenza estensione nascosta: {filePath}");
                            return true;
                        }

                        // Ricerca su tutti gli elementi della cartella con confronto NameWithoutExtension
                        foreach (string entry in Directory.EnumerateFileSystemEntries(folderPath))
                        {
                            string entryName = Path.GetFileNameWithoutExtension(entry);
                            if (string.Equals(entryName, fileName, StringComparison.OrdinalIgnoreCase))
                            {
                                filePath = entry;
                                resolved = true;
                                AppLogger.Info($"FileDialogResolver: File risolto tramite confronto NameWithoutExtension: {filePath}");
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug($"FileDialogResolver: Errore scansione corrispondenze estensione in {folderPath}: {ex.Message}");
                    }
                }

                // Se fileName è già un percorso assoluto esistente (es. incollato per intero nel campo di testo)
                if (File.Exists(fileName) || Directory.Exists(fileName))
                {
                    filePath = fileName;
                    resolved = true;
                    AppLogger.Info($"FileDialogResolver: Percorso assoluto diretto risolto: {filePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"FileDialogResolver: Eccezione durante l'ispezione UI Automation del dialogo 0x{dialogHwnd:X}: {ex.Message}");
            }
            finally
            {
                lock (CacheLock)
                {
                    _lastRootHwnd = root;
                    _lastFocusHwnd = focusHwnd;
                    _lastAddrText = addrText;
                    _lastEditText = editText;
                    _lastFocusedName = focusedName;
                    _lastFilePath = resolved ? filePath : null;
                    _lastResult = resolved;
                    _lastQueryTickMs = Environment.TickCount64;
                }
            }

            return false;
        }

        private static string? ResolveFolderPath(IntPtr root, AutomationElement dlg)
        {
            try
            {
                // Accesso diretto tramite HWND del controllo 1001 per evitare TreeScope.Descendants quando possibile
                AutomationElement? tb = null;
                IntPtr hAddr = Win32.GetDlgItem(root, 1001);
                if (hAddr != IntPtr.Zero)
                {
                    tb = AutomationElement.FromHandle(hAddr);
                }
                tb ??= dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"));
                if (tb != null)
                {
                    string name = tb.Current.Name ?? string.Empty;
                    int colon = name.IndexOf(':');
                    string raw = colon >= 0 ? name.Substring(colon + 1).Trim() : name.Trim();

                    if (Directory.Exists(raw))
                    {
                        return raw;
                    }

                    // Verifica mapping cartelle speciali note (es. "Documenti", "Download", "Desktop")
                    string? mapped = MapSpecialFolder(raw);
                    if (!string.IsNullOrEmpty(mapped) && Directory.Exists(mapped))
                    {
                        return mapped;
                    }

                    // Regex per estrarre percorso con lettera di unità (es. C:\Cartella...)
                    var m = DrivePathRegex.Match(name);
                    if (m.Success && Directory.Exists(m.Value))
                    {
                        return m.Value;
                    }
                }

                // Fallback: cerca controlli ToolbarWindow32 dentro l'Address Band
                var toolbars = dlg.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));
                foreach (AutomationElement item in toolbars)
                {
                    string tbName = item.Current.Name ?? string.Empty;
                    if (tbName.Contains(":") && (tbName.Contains("\\") || tbName.Contains("/")))
                    {
                        int c = tbName.IndexOf(':');
                        string candidate = c >= 0 ? tbName.Substring(c + 1).Trim() : tbName.Trim();
                        if (Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"FileDialogResolver: Errore risoluzione cartella: {ex.Message}");
            }

            return null;
        }

        private static string? ResolveSelectedFileName(IntPtr root, AutomationElement dlg)
        {
            try
            {
                // 1. Controllo rapido: elemento attualmente con focus UI se di tipo ListItem / DataItem (evita Descendants walk)
                var focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    var ct = focused.Current.ControlType;
                    if (ct == ControlType.ListItem || ct == ControlType.DataItem)
                    {
                        string name = focused.Current.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name.Trim();
                        }
                    }
                }

                // 2. Cerca elemento selezionato nel controllo List (vista elementi file)
                var list = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
                if (list != null && list.TryGetCurrentPattern(SelectionPattern.Pattern, out object selPatternObj))
                {
                    var selItems = ((SelectionPattern)selPatternObj).Current.GetSelection();
                    if (selItems != null && selItems.Length > 0)
                    {
                        string name = selItems[0].Current.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name.Trim();
                        }
                    }
                }

                // 3. Cerca elemento selezionato in DataGrid (se visualizzazione dettagliata)
                var grid = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid));
                if (grid != null && grid.TryGetCurrentPattern(SelectionPattern.Pattern, out object gridSelObj))
                {
                    var selItems = ((SelectionPattern)gridSelObj).Current.GetSelection();
                    if (selItems != null && selItems.Length > 0)
                    {
                        string name = selItems[0].Current.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name.Trim();
                        }
                    }
                }

                // 4. Fallback: campo di testo 'Nome file:' (AutomationId 1148)
                AutomationElement? edit = null;
                IntPtr hEdit = Win32.GetDlgItem(root, 1148);
                if (hEdit != IntPtr.Zero)
                {
                    edit = AutomationElement.FromHandle(hEdit);
                }
                edit ??= dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"));
                if (edit != null && edit.TryGetCurrentPattern(ValuePattern.Pattern, out object valPatternObj))
                {
                    string val = ((ValuePattern)valPatternObj).Current.Value;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"FileDialogResolver: Errore risoluzione nome file selezionato: {ex.Message}");
            }

            return null;
        }

        private static string? MapSpecialFolder(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string lower = raw.Trim().ToLowerInvariant();
            switch (lower)
            {
                case "documenti":
                case "documents":
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "download":
                case "downloads":
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                case "desktop":
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                case "immagini":
                case "pictures":
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                case "musica":
                case "music":
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                case "video":
                case "videos":
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                case "recenti":
                case "recent":
                    return Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                default:
                    return null;
            }
        }
    }
}
