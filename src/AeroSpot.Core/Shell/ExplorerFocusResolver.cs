using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

namespace AeroSpot.Core.Shell
{
    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IShellWindows
    {
        [DispId(1610743808)]
        int Count { get; }
        [DispId(1610743809)]
        object Item([In, Optional] object index);
        [DispId(1610743810)]
        object _NewEnum();
        [DispId(1610743811)]
        void Register([In, MarshalAs(UnmanagedType.IDispatch)] object pid, [In] int hwnd, [In] int swClass, out int pdwCookie);
        [DispId(1610743812)]
        void RegisterPending([In] int lThreadId, [In] ref object pvarloc, [In] ref object pvarlocRoot, [In] int swClass, out int pdwCookie);
        [DispId(1610743813)]
        void Revoke([In] int dwCookie);
        [DispId(1610743814)]
        void OnNavigate([In] int dwCookie, [In] ref object pvarloc);
        [DispId(1610743815)]
        void OnActivated([In] int dwCookie, [In] bool fActive);
        [DispId(1610743816)]
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([In] ref object pvarloc, [In] ref object pvarlocRoot, [In] int swClass, out int phwnd, [In] int swflags);
        [DispId(1610743817)]
        void OnCreated([In] int dwCookie, [In, MarshalAs(UnmanagedType.IUnknown)] object punk);
        [DispId(1610743818)]
        void ProcessAttachDetach([In] bool fAttach);
    }

    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellBrowser
    {
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp(bool fEnterMode);
        void InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        void SetMenuSB(IntPtr hmenuShared, IntPtr holemenu, IntPtr hwndActiveObject);
        void RemoveMenusSB(IntPtr hmenuShared);
        void SetStatusTextSB(IntPtr pszStatusText);
        void EnableModelessSB(bool fEnable);
        void TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        void BrowseObject(IntPtr pidl, uint wFlags);
        void GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        void GetControlWindow(uint id, out IntPtr phwnd);
        void SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        void QueryActiveShellView(out IntPtr ppshv);
        void OnViewWindowActive(IntPtr pshv);
        void SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
    }

    public static class ExplorerFocusResolver
    {
        private const int SWC_DESKTOP = 0x00000008;
        private const int SWFO_NEEDDISPATCH = 0x00000001;
        private static readonly Guid ClsidShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");

        public static IntPtr GetTabHwndFromShellWindow(object? win)
        {
            if (win == null) return IntPtr.Zero;
            try
            {
                if (win is IServiceProvider sp)
                {
                    var sid = SID_STopLevelBrowser;
                    var iid = IID_IShellBrowser;
                    int hr = sp.QueryService(ref sid, ref iid, out IntPtr pBrowser);
                    if (hr == 0 && pBrowser != IntPtr.Zero)
                    {
                        try
                        {
                            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
                            browser.GetWindow(out IntPtr tabHwnd);
                            return tabHwnd;
                        }
                        finally
                        {
                            Marshal.Release(pBrowser);
                        }
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        public static string GetWindowClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            var sb = new StringBuilder(256);
            Win32.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            var sb = new StringBuilder(256);
            Win32.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsExplorerOrDesktop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            if (hwnd == Win32.GetShellWindow()) return true;

            string className = GetWindowClassName(hwnd);
            if (className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Se l'handle appartiene a un controllo/scheda figlia, controlla l'antenato radice
            IntPtr rootHwnd = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            if (rootHwnd != IntPtr.Zero && rootHwnd != hwnd)
            {
                string rootClassName = GetWindowClassName(rootHwnd);
                if (rootClassName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                    rootClassName.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase) ||
                    rootClassName.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    rootClassName.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsDesktop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (hwnd == Win32.GetShellWindow()) return true;

            string className = GetWindowClassName(hwnd);
            if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            IntPtr rootHwnd = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            if (rootHwnd != IntPtr.Zero && rootHwnd != hwnd)
            {
                string rootClassName = GetWindowClassName(rootHwnd);
                return rootClassName.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                       rootClassName.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static bool IsUserTypingOrRenaming(IntPtr foregroundHwnd)
        {
            if (foregroundHwnd == IntPtr.Zero) return false;

            try
            {
                uint threadId = Win32.GetWindowThreadProcessId(foregroundHwnd, out _);
                var gui = new Win32.GUITHREADINFO();
                gui.cbSize = Marshal.SizeOf<Win32.GUITHREADINFO>();

                if ((threadId == 0 || !Win32.GetGUIThreadInfo(threadId, ref gui)) && !Win32.GetGUIThreadInfo(0, ref gui))
                {
                    return false;
                }

                // 1. Se il cursore testo/caret è attivo, l'utente sta sicuramente digitando o rinominando
                if (gui.hwndCaret != IntPtr.Zero)
                {
                    AppLogger.Debug("IsUserTypingOrRenaming: Caret attivo rilevato (hwndCaret != 0).");
                    return true;
                }

                // 2. Se il controllo con focus è un campo di testo (es. barra indirizzi, ricerca, edit di rinomina)
                if (gui.hwndFocus != IntPtr.Zero)
                {
                    string focusClass = GetWindowClassName(gui.hwndFocus);

                    // Nelle isole XAML di Windows 11 (Windows.UI.Core.CoreWindow), il contenitore ospita anche tab e header.
                    // Senza un cursore caret attivo (già verificato sopra), l'utente sta solo navigando e non digitando.
                    if (focusClass.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                        focusClass.Equals("Xaml_WindowedPopupClass", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (IsInputFieldClass(focusClass))
                    {
                        AppLogger.Debug($"IsUserTypingOrRenaming: Controllo di input con focus: '{focusClass}'.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Errore durante la verifica dello stato di digitazione.", ex);
            }

            return false;
        }

        public static bool IsInputFieldClass(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) return false;

            if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("SearchEditBox", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Address Band Root", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("UniversalSearchBand", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("ComboBoxEx32", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("ModernSearchBar", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (className.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        public static bool TryGetFocusedPath(IntPtr foregroundHwnd, out string? filePath)
        {
            filePath = null;

            if (foregroundHwnd == IntPtr.Zero || !IsExplorerOrDesktop(foregroundHwnd))
            {
                return false;
            }

            if (IsUserTypingOrRenaming(foregroundHwnd))
            {
                AppLogger.Debug("TryGetFocusedPath: Utente in fase di digitazione o rinomina, richiesta ignorata.");
                return false;
            }

            try
            {
                if (IsDesktop(foregroundHwnd))
                {
                    filePath = GetFocusedPathForDesktop();
                    return !string.IsNullOrEmpty(filePath);
                }
                else
                {
                    filePath = GetFocusedPathForExplorer(foregroundHwnd);
                    return !string.IsNullOrEmpty(filePath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Errore durante il recupero dell'elemento con focus dalla Shell.", ex);
                return false;
            }
        }

        public static string? GetFocusedPathForDesktop()
        {
            try
            {
                Type? shellWindowsType = Type.GetTypeFromCLSID(ClsidShellWindows);
                if (shellWindowsType == null) return null;

                object? shellWindows = Activator.CreateInstance(shellWindowsType);
                if (shellWindows == null) return null;

                object pvarLoc = 0; // CSIDL_DESKTOP
                object pvarLocRoot = 0;
                int phwnd = 0;

                object? dispatch = null;
                try
                {
                    var isw = (IShellWindows)shellWindows;
                    dispatch = isw.FindWindowSW(ref pvarLoc, ref pvarLocRoot, SWC_DESKTOP, out phwnd, SWFO_NEEDDISPATCH);
                }
                catch
                {
                    dispatch = InvokeComMethod(shellWindows, "FindWindowSW", pvarLoc, pvarLocRoot, SWC_DESKTOP, phwnd, SWFO_NEEDDISPATCH);
                }

                if (dispatch != null)
                {
                    object? doc = GetComProperty(dispatch, "Document");
                    return ExtractPathFromShellDocument(doc);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Errore recupero elemento Desktop via COM.", ex);
            }

            return null;
        }

        public static string? GetFocusedPathForExplorer(IntPtr foregroundHwnd)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                object? shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;

                object? windows = InvokeComMethod(shell, "Windows");
                if (windows == null) return null;

                object? countObj = GetComProperty(windows, "Count");
                if (countObj is not int count || count <= 0) return null;

                IntPtr rootFg = Win32.GetAncestor(foregroundHwnd, Win32.GA_ROOT);
                if (rootFg == IntPtr.Zero) rootFg = foregroundHwnd;

                // Titolo della finestra attiva di Esplora Risorse (es. "AeroSpot" o "Immagini")
                string fgTitle = GetWindowTitle(rootFg);
                if (string.IsNullOrEmpty(fgTitle) && rootFg != foregroundHwnd)
                {
                    fgTitle = GetWindowTitle(foregroundHwnd);
                }

                if (!string.IsNullOrEmpty(fgTitle))
                {
                    int dashIdx = fgTitle.IndexOf(" - ", StringComparison.Ordinal);
                    if (dashIdx > 0)
                    {
                        fgTitle = fgTitle.Substring(0, dashIdx).Trim();
                    }
                }

                AppLogger.Debug($"GetFocusedPathForExplorer: FG HWND=0x{foregroundHwnd:X}, Root=0x{rootFg:X}, Title='{fgTitle}', OpenWindowsCount={count}");

                IntPtr hActiveTab = Win32.FindWindowEx(rootFg, IntPtr.Zero, "ShellTabWindowClass", null);
                if (hActiveTab == IntPtr.Zero && foregroundHwnd != rootFg)
                {
                    hActiveTab = Win32.FindWindowEx(foregroundHwnd, IntPtr.Zero, "ShellTabWindowClass", null);
                }

                // Raccolta candidati per correlazione accurata (supporto multi-scheda Windows 11)
                string? tabAndSelectionCandidate = null;
                string? titleAndSelectionCandidate = null;
                string? hwndAndSelectionCandidate = null;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        object? win = InvokeComMethod(windows, "Item", i);
                        if (win == null) continue;

                        object? hwndValObj = GetComProperty(win, "HWND");
                        IntPtr winHwnd = IntPtr.Zero;
                        if (hwndValObj != null)
                        {
                            winHwnd = new IntPtr(Convert.ToInt64(hwndValObj));
                        }

                        // Scarta finestre fantasma o distrutte rimaste nella cache COM
                        if (winHwnd == IntPtr.Zero || !Win32.IsWindow(winHwnd)) continue;

                        IntPtr rootWin = winHwnd != IntPtr.Zero ? Win32.GetAncestor(winHwnd, Win32.GA_ROOT) : IntPtr.Zero;
                        if (rootWin == IntPtr.Zero) rootWin = winHwnd;

                        // La finestra shell deve appartenere alla finestra top-level attiva in primo piano
                        bool isSameTopLevel = (rootWin == rootFg || winHwnd == foregroundHwnd || winHwnd == rootFg);
                        if (!isSameTopLevel) continue;

                        string? locName = GetComProperty(win, "LocationName")?.ToString();
                        object? doc = GetComProperty(win, "Document");

                        string? folderTitle = null;
                        if (doc != null)
                        {
                            object? folder = GetComProperty(doc, "Folder");
                            if (folder != null)
                            {
                                folderTitle = GetComProperty(folder, "Title")?.ToString();
                            }
                        }

                        // Verifica HWND tradizionale
                        bool isHwndMatch = isSameTopLevel || (hActiveTab != IntPtr.Zero && winHwnd == hActiveTab);

                        // Verifica corrispondenza Titolo cartella (Windows 11 con schede)
                        bool isTitleMatch = !string.IsNullOrEmpty(fgTitle) && (
                            (!string.IsNullOrEmpty(locName) && (fgTitle.Equals(locName, StringComparison.OrdinalIgnoreCase) || fgTitle.Contains(locName, StringComparison.OrdinalIgnoreCase) || locName.Contains(fgTitle, StringComparison.OrdinalIgnoreCase))) ||
                            (!string.IsNullOrEmpty(folderTitle) && (fgTitle.Equals(folderTitle, StringComparison.OrdinalIgnoreCase) || fgTitle.Contains(folderTitle, StringComparison.OrdinalIgnoreCase) || folderTitle.Contains(fgTitle, StringComparison.OrdinalIgnoreCase)))
                        );

                        // Verifica specifica della scheda attiva Windows 11 tramite IShellBrowser
                        bool isExactTabMatch = false;
                        if (hActiveTab != IntPtr.Zero)
                        {
                            IntPtr tabHwnd = GetTabHwndFromShellWindow(win);
                            if (tabHwnd != IntPtr.Zero && tabHwnd == hActiveTab)
                            {
                                isExactTabMatch = true;
                            }
                        }

                        bool hasSelection = HasSelectedItems(doc);
                        if (!hasSelection)
                        {
                            // Se la scheda/cartella non ha elementi selezionati, non considerare per l'anteprima
                            continue;
                        }

                        string? path = ExtractPathFromShellDocument(doc);
                        if (string.IsNullOrEmpty(path)) continue;

                        // 1. Corrispondenza perfetta: scheda attiva esatta di Windows 11 con selezione
                        if (isExactTabMatch)
                        {
                            AppLogger.Info($"GetFocusedPathForExplorer: Risolto percorso da scheda attiva Windows 11 (ShellTabWindowClass) [{i}]: {path}");
                            return path;
                        }

                        // 2. Corrispondenza perfetta: stesso HWND top-level e titolo corrispondente
                        if (isHwndMatch && isTitleMatch)
                        {
                            AppLogger.Info($"GetFocusedPathForExplorer: Risolto percorso da corrispondenza perfetta (HWND + Titolo '{fgTitle}') [{i}]: {path}");
                            return path;
                        }

                        if (isTitleMatch && titleAndSelectionCandidate == null)
                        {
                            titleAndSelectionCandidate = path;
                        }

                        if (isHwndMatch && hwndAndSelectionCandidate == null)
                        {
                            hwndAndSelectionCandidate = path;
                        }

                        if (tabAndSelectionCandidate == null && isSameTopLevel)
                        {
                            tabAndSelectionCandidate = path;
                        }
                    }
                    catch (Exception exItem)
                    {
                        AppLogger.Debug($"Errore ispezione finestra shell [{i}]: {exItem.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(titleAndSelectionCandidate))
                {
                    AppLogger.Info($"GetFocusedPathForExplorer: Risolto percorso da Titolo + Selezione attiva: {titleAndSelectionCandidate}");
                    return titleAndSelectionCandidate;
                }

                if (!string.IsNullOrEmpty(hwndAndSelectionCandidate))
                {
                    AppLogger.Info($"GetFocusedPathForExplorer: Risolto percorso da HWND + Selezione attiva: {hwndAndSelectionCandidate}");
                    return hwndAndSelectionCandidate;
                }

                if (!string.IsNullOrEmpty(tabAndSelectionCandidate))
                {
                    AppLogger.Info($"GetFocusedPathForExplorer: Risolto percorso da finestra correlata: {tabAndSelectionCandidate}");
                    return tabAndSelectionCandidate;
                }

                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Errore enumerazione Shell.Windows.", ex);
                return null;
            }
        }

        private static bool HasSelectedItems(object? doc)
        {
            if (doc == null) return false;
            try
            {
                object? selected = GetComPropertyOrMethod(doc, "SelectedItems");
                if (selected != null)
                {
                    object? countObj = GetComPropertyOrMethod(selected, "Count");
                    if (countObj is int count && count > 0)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static string? ExtractPathFromShellDocument(object? doc)
        {
            if (doc == null) return null;

            // In Esplora Risorse o sul Desktop, se non ci sono elementi selezionati (SelectedItems.Count == 0),
            // non deve essere aperta alcuna anteprima, evitando che un vecchio FocusedItem de-selezionato venga mostrato.
            bool hasSelection = HasSelectedItems(doc);
            if (!hasSelection)
            {
                return null;
            }

            // 1. Prova prima con FocusedItem (l'elemento con focus della tastiera o attivo)
            try
            {
                object? focused = GetComPropertyOrMethod(doc, "FocusedItem");
                if (focused != null)
                {
                    object? pathObj = GetComPropertyOrMethod(focused, "Path");
                    string? p = NormalizePath(pathObj?.ToString());
                    if (!string.IsNullOrEmpty(p) && IsValidDiskPath(p))
                    {
                        AppLogger.Debug($"ExtractPathFromShellDocument: Trovato da FocusedItem: {p}");
                        return p;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"ExtractPathFromShellDocument: FocusedItem errore: {ex.Message}");
            }

            // 2. Fallback su SelectedItems (se FocusedItem non è disponibile o non valido)
            try
            {
                object? selected = GetComPropertyOrMethod(doc, "SelectedItems");
                if (selected != null)
                {
                    object? countObj = GetComPropertyOrMethod(selected, "Count");
                    if (countObj is int count && count > 0)
                    {
                        object? first = GetComPropertyOrMethod(selected, "Item", 0);
                        if (first != null)
                        {
                            object? pathObj = GetComPropertyOrMethod(first, "Path");
                            string? p = NormalizePath(pathObj?.ToString());
                            if (!string.IsNullOrEmpty(p) && IsValidDiskPath(p))
                            {
                                AppLogger.Debug($"ExtractPathFromShellDocument: Trovato da SelectedItems: {p}");
                                return p;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"ExtractPathFromShellDocument: SelectedItems errore: {ex.Message}");
            }

            return null;
        }

        public static string? NormalizePath(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return new Uri(raw).LocalPath;
                }
                catch { }
            }
            return raw;
        }

        public static bool IsValidDiskPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Escludi cartelle virtuali di Shell come ::{20D04FE0-3AEA-1069-A2D8-08002B30309D}
            if (path.StartsWith("::{", StringComparison.Ordinal)) return false;

            // Verifica esistenza reale su disco (file o cartella)
            return File.Exists(path) || Directory.Exists(path);
        }

        public static object? GetComProperty(object? target, string propertyName)
        {
            return GetComPropertyOrMethod(target, propertyName);
        }

        public static object? InvokeComMethod(object? target, string methodName, params object[]? args)
        {
            return GetComPropertyOrMethod(target, methodName, args);
        }

        public static object? GetComPropertyOrMethod(object? target, string memberName, params object[]? args)
        {
            if (target == null) return null;
            try
            {
                return target.GetType().InvokeMember(
                    memberName,
                    BindingFlags.GetProperty | BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null,
                    target,
                    args);
            }
            catch
            {
                return null;
            }
        }
    }
}
