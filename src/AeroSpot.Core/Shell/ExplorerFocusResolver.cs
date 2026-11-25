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

    [ComImport]
    [Guid("cde725b0-ccc9-4519-917e-325d72fab4ce")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out Win32.POINT ppt);
        [PreserveSig] int GetSpacing(ref Win32.POINT ppt);
        [PreserveSig] int GetDefaultSpacing(out Win32.POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
    }

    public static class ExplorerFocusResolver
    {
        private const int SWC_DESKTOP = 0x00000008;
        private const int SWFO_NEEDDISPATCH = 0x00000001;
        private const uint SVGIO_ALLVIEW = 0x00000002;
        private const uint SVSI_SELECT_FOCUS_SINGLE = 0x00000001 | 0x00000004 | 0x00000008 | 0x00000010;
        private static readonly Guid ClsidShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IID_IFolderView = new Guid("cde725b0-ccc9-4519-917e-325d72fab4ce");

        private static void ReleaseCom(object? obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
            {
                try { Marshal.ReleaseComObject(obj); } catch { }
            }
        }

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
                        object? browserObj = null;
                        try
                        {
                            browserObj = Marshal.GetObjectForIUnknown(pBrowser);
                            var browser = (IShellBrowser)browserObj;
                            browser.GetWindow(out IntPtr tabHwnd);
                            return tabHwnd;
                        }
                        finally
                        {
                            if (!ReferenceEquals(browserObj, win)) ReleaseCom(browserObj);
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

        public static bool IsFileDialog(IntPtr hwnd)
        {
            return FileDialogResolver.IsFileDialog(hwnd);
        }

        public static bool TryGetFocusedPath(IntPtr foregroundHwnd, out string? filePath)
        {
            filePath = null;

            if (foregroundHwnd == IntPtr.Zero || (!IsExplorerOrDesktop(foregroundHwnd) && !IsFileDialog(foregroundHwnd)))
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
                if (IsExplorerOrDesktop(foregroundHwnd))
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
                else if (IsFileDialog(foregroundHwnd))
                {
                    return FileDialogResolver.TryGetFocusedPath(foregroundHwnd, out filePath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Errore durante il recupero dell'elemento con focus dalla Shell.", ex);
                return false;
            }

            return false;
        }

        public static string? GetFocusedPathForDesktop()
        {
            object? shellWindows = null;
            object? dispatch = null;
            object? doc = null;
            try
            {
                Type? shellWindowsType = Type.GetTypeFromCLSID(ClsidShellWindows);
                if (shellWindowsType == null) return null;

                shellWindows = Activator.CreateInstance(shellWindowsType);
                if (shellWindows == null) return null;

                object pvarLoc = 0; // CSIDL_DESKTOP
                object pvarLocRoot = 0;
                int phwnd = 0;

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
                    doc = GetComProperty(dispatch, "Document");
                    return ExtractPathFromShellDocument(doc);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Errore recupero elemento Desktop via COM.", ex);
            }
            finally
            {
                if (!ReferenceEquals(doc, dispatch)) ReleaseCom(doc);
                if (!ReferenceEquals(dispatch, shellWindows)) ReleaseCom(dispatch);
                ReleaseCom(shellWindows);
            }

            return null;
        }

        public static string? GetFocusedPathForExplorer(IntPtr foregroundHwnd)
        {
            object? shell = null;
            object? windows = null;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;

                windows = InvokeComMethod(shell, "Windows");
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

                IntPtr hActiveTab = FindActiveVisibleTabHwnd(rootFg);
                if (hActiveTab == IntPtr.Zero && foregroundHwnd != rootFg)
                {
                    hActiveTab = FindActiveVisibleTabHwnd(foregroundHwnd);
                }

                // Raccolta candidati per correlazione accurata (supporto multi-scheda Windows 11)
                string? hwndAndTitleCandidate = null;
                string? tabAndSelectionCandidate = null;
                string? titleAndSelectionCandidate = null;
                string? hwndAndSelectionCandidate = null;

                for (int i = 0; i < count; i++)
                {
                    object? win = null;
                    object? doc = null;
                    object? folder = null;
                    try
                    {
                        win = InvokeComMethod(windows, "Item", i);
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
                        doc = GetComProperty(win, "Document");

                        string? folderTitle = null;
                        if (doc != null)
                        {
                            folder = GetComProperty(doc, "Folder");
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
                            // Se la scheda attualmente visibile/attiva NON ha alcun elemento selezionato,
                            // interrompi subito per evitare di ripiegare sulla selezione di una scheda inattiva in background.
                            if (isExactTabMatch)
                            {
                                return null;
                            }
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

                        // 2. Fallback prioritario: stesso HWND top-level e titolo corrispondente
                        // Non fare return anticipato dentro il ciclo: se due schede hanno lo stesso nome
                        // (es. "AeroSpot" e "AeroSpot"), la scheda inattiva [0] potrebbe fare match prima
                        // che il ciclo raggiunga la vera scheda attiva visibile (isExactTabMatch) in [1].
                        if (isHwndMatch && isTitleMatch && hwndAndTitleCandidate == null)
                        {
                            hwndAndTitleCandidate = path;
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
                    finally
                    {
                        if (!ReferenceEquals(folder, doc) && !ReferenceEquals(folder, win)) ReleaseCom(folder);
                        if (!ReferenceEquals(doc, win)) ReleaseCom(doc);
                        ReleaseCom(win);
                    }
                }

                if (!string.IsNullOrEmpty(hwndAndTitleCandidate))
                {
                    AppLogger.Info($"GetFocusedPathForExplorer: Risolto percorso da corrispondenza HWND + Titolo '{fgTitle}': {hwndAndTitleCandidate}");
                    return hwndAndTitleCandidate;
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
            finally
            {
                if (!ReferenceEquals(windows, shell)) ReleaseCom(windows);
                ReleaseCom(shell);
            }
        }

        /// <summary>
        /// Sposta la selezione nella finestra di Esplora Risorse o sul Desktop senza rubare il focus a QuickLook,
        /// rispettando esattamente l'ordinamento corrente della vista (IFolderView).
        /// </summary>
        public static bool TryNavigateSelection(IntPtr explorerOrDesktopHwnd, int delta, out string? newPath, out bool atBoundary)
        {
            newPath = null;
            atBoundary = false;

            if (delta == 0 || explorerOrDesktopHwnd == IntPtr.Zero || !Win32.IsWindow(explorerOrDesktopHwnd))
            {
                return false;
            }

            if (!IsExplorerOrDesktop(explorerOrDesktopHwnd))
            {
                return false;
            }

            object? shellWindows = null;
            object? dispatch = null;
            object? shell = null;
            object? windows = null;
            object? exactTabWin = null;
            object? sameTopLevelFallback = null;

            try
            {
                if (IsDesktop(explorerOrDesktopHwnd))
                {
                    Type? shellWindowsType = Type.GetTypeFromCLSID(ClsidShellWindows);
                    if (shellWindowsType == null) return false;

                    shellWindows = Activator.CreateInstance(shellWindowsType);
                    if (shellWindows == null) return false;

                    object pvarLoc = 0;
                    object pvarLocRoot = 0;
                    int phwnd = 0;
                    try
                    {
                        var isw = (IShellWindows)shellWindows;
                        dispatch = isw.FindWindowSW(ref pvarLoc, ref pvarLocRoot, SWC_DESKTOP, out phwnd, SWFO_NEEDDISPATCH);
                    }
                    catch
                    {
                        dispatch = InvokeComMethod(shellWindows, "FindWindowSW", pvarLoc, pvarLocRoot, SWC_DESKTOP, phwnd, SWFO_NEEDDISPATCH);
                    }

                    return dispatch != null && TryStepFolderView(dispatch, delta, out newPath, out atBoundary);
                }

                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;

                shell = Activator.CreateInstance(shellType);
                if (shell == null) return false;

                windows = InvokeComMethod(shell, "Windows");
                if (windows == null) return false;

                object? countObj = GetComProperty(windows, "Count");
                if (countObj is not int count || count <= 0) return false;

                IntPtr rootFg = Win32.GetAncestor(explorerOrDesktopHwnd, Win32.GA_ROOT);
                if (rootFg == IntPtr.Zero) rootFg = explorerOrDesktopHwnd;

                IntPtr hActiveTab = FindActiveVisibleTabHwnd(rootFg);
                if (hActiveTab == IntPtr.Zero && explorerOrDesktopHwnd != rootFg)
                {
                    hActiveTab = FindActiveVisibleTabHwnd(explorerOrDesktopHwnd);
                }

                for (int i = 0; i < count; i++)
                {
                    object? win = null;
                    bool keepWin = false;
                    try
                    {
                        win = InvokeComMethod(windows, "Item", i);
                        if (win == null) continue;

                        object? hwndValObj = GetComProperty(win, "HWND");
                        IntPtr winHwnd = hwndValObj != null ? new IntPtr(Convert.ToInt64(hwndValObj)) : IntPtr.Zero;
                        if (winHwnd == IntPtr.Zero || !Win32.IsWindow(winHwnd)) continue;

                        IntPtr rootWin = Win32.GetAncestor(winHwnd, Win32.GA_ROOT);
                        if (rootWin == IntPtr.Zero) rootWin = winHwnd;

                        bool isSameTopLevel = (rootWin == rootFg || winHwnd == explorerOrDesktopHwnd || winHwnd == rootFg);
                        if (!isSameTopLevel) continue;

                        if (hActiveTab != IntPtr.Zero)
                        {
                            IntPtr tabHwnd = GetTabHwndFromShellWindow(win);
                            if (tabHwnd != IntPtr.Zero && tabHwnd == hActiveTab)
                            {
                                exactTabWin = win;
                                keepWin = true;
                                break;
                            }
                        }

                        if (sameTopLevelFallback == null)
                        {
                            sameTopLevelFallback = win;
                            keepWin = true;
                        }
                    }
                    catch { }
                    finally
                    {
                        if (!keepWin) ReleaseCom(win);
                    }
                }

                object? targetWin = exactTabWin ?? sameTopLevelFallback;
                return targetWin != null && TryStepFolderView(targetWin, delta, out newPath, out atBoundary);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"TryNavigateSelection: errore navigazione Shell: {ex.Message}");
                return false;
            }
            finally
            {
                if (!ReferenceEquals(exactTabWin, sameTopLevelFallback)) ReleaseCom(exactTabWin);
                ReleaseCom(sameTopLevelFallback);
                if (!ReferenceEquals(windows, shell)) ReleaseCom(windows);
                ReleaseCom(shell);
                if (!ReferenceEquals(dispatch, shellWindows)) ReleaseCom(dispatch);
                ReleaseCom(shellWindows);
            }
        }

        private static bool TryStepFolderView(object shellWindow, int delta, out string? newPath, out bool atBoundary)
        {
            newPath = null;
            atBoundary = false;

            if (shellWindow is not IServiceProvider sp) return false;

            IntPtr pBrowser = IntPtr.Zero;
            IntPtr pShellView = IntPtr.Zero;
            IntPtr pFolderView = IntPtr.Zero;
            object? browserObj = null;
            object? folderViewObj = null;
            object? doc = null;

            try
            {
                var sid = SID_STopLevelBrowser;
                var iidBrowser = IID_IShellBrowser;
                if (sp.QueryService(ref sid, ref iidBrowser, out pBrowser) != 0 || pBrowser == IntPtr.Zero)
                {
                    return false;
                }

                browserObj = Marshal.GetObjectForIUnknown(pBrowser);
                var browser = (IShellBrowser)browserObj;
                browser.QueryActiveShellView(out pShellView);
                if (pShellView == IntPtr.Zero) return false;

                var iidFolderView = IID_IFolderView;
                if (Marshal.QueryInterface(pShellView, in iidFolderView, out pFolderView) != 0 || pFolderView == IntPtr.Zero)
                {
                    return false;
                }

                folderViewObj = Marshal.GetObjectForIUnknown(pFolderView);
                var folderView = (IFolderView)folderViewObj;
                if (folderView.ItemCount(SVGIO_ALLVIEW, out int totalCount) != 0 || totalCount <= 0)
                {
                    return false;
                }

                int hrFocus = folderView.GetFocusedItem(out int currentIndex);
                if (hrFocus != 0 || currentIndex < 0 || currentIndex >= totalCount)
                {
                    currentIndex = 0;
                }

                int targetIndex = Math.Clamp(currentIndex + delta, 0, totalCount - 1);
                if (targetIndex == currentIndex)
                {
                    atBoundary = true;
                    return false;
                }

                folderView.SelectItem(targetIndex, SVSI_SELECT_FOCUS_SINGLE);

                doc = GetComProperty(shellWindow, "Document");
                newPath = ExtractPathFromShellDocument(doc);
                return !string.IsNullOrEmpty(newPath);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"TryStepFolderView: errore IFolderView: {ex.Message}");
                return false;
            }
            finally
            {
                if (!ReferenceEquals(doc, shellWindow)) ReleaseCom(doc);
                if (!ReferenceEquals(folderViewObj, shellWindow)) ReleaseCom(folderViewObj);
                if (!ReferenceEquals(browserObj, shellWindow)) ReleaseCom(browserObj);
                if (pFolderView != IntPtr.Zero) Marshal.Release(pFolderView);
                if (pShellView != IntPtr.Zero) Marshal.Release(pShellView);
                if (pBrowser != IntPtr.Zero) Marshal.Release(pBrowser);
            }
        }

        /// <summary>
        /// Fallback per dialoghi #32770 o cartelle non gestite da IShellWindows:
        /// naviga l'elemento adiacente nella stessa directory con ordinamento naturale Windows (StrCmpLogicalW).
        /// </summary>
        public static bool TryGetAdjacentItemInDirectory(string? currentPath, int delta, out string? nextPath)
        {
            nextPath = null;
            if (string.IsNullOrWhiteSpace(currentPath) || delta == 0) return false;

            try
            {
                string? dir = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;

                var entries = new System.Collections.Generic.List<string>();
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    try
                    {
                        var attrs = File.GetAttributes(entry);
                        if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch { continue; }
                    entries.Add(entry);
                }

                if (entries.Count <= 1) return false;

                entries.Sort((a, b) => Win32.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));

                int idx = entries.FindIndex(p => string.Equals(p, currentPath, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return false;

                int targetIdx = Math.Clamp(idx + delta, 0, entries.Count - 1);
                if (targetIdx == idx) return false;

                nextPath = entries[targetIdx];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr FindActiveVisibleTabHwnd(IntPtr parentHwnd)
        {
            if (parentHwnd == IntPtr.Zero) return IntPtr.Zero;

            IntPtr firstTab = IntPtr.Zero;
            IntPtr candidate = IntPtr.Zero;
            while ((candidate = Win32.FindWindowEx(parentHwnd, candidate, "ShellTabWindowClass", null)) != IntPtr.Zero)
            {
                if (firstTab == IntPtr.Zero)
                {
                    firstTab = candidate;
                }

                if (Win32.IsWindowVisible(candidate))
                {
                    return candidate;
                }
            }

            return firstTab;
        }

        private static bool HasSelectedItems(object? doc)
        {
            if (doc == null) return false;
            object? selected = null;
            try
            {
                selected = GetComPropertyOrMethod(doc, "SelectedItems");
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
            finally
            {
                if (!ReferenceEquals(selected, doc)) ReleaseCom(selected);
            }
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
            object? focused = null;
            try
            {
                focused = GetComPropertyOrMethod(doc, "FocusedItem");
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
            finally
            {
                if (!ReferenceEquals(focused, doc)) ReleaseCom(focused);
            }

            // 2. Fallback su SelectedItems (se FocusedItem non è disponibile o non valido)
            object? selected = null;
            object? first = null;
            try
            {
                selected = GetComPropertyOrMethod(doc, "SelectedItems");
                if (selected != null)
                {
                    object? countObj = GetComPropertyOrMethod(selected, "Count");
                    if (countObj is int count && count > 0)
                    {
                        first = GetComPropertyOrMethod(selected, "Item", 0);
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
            finally
            {
                if (!ReferenceEquals(first, selected) && !ReferenceEquals(first, doc)) ReleaseCom(first);
                if (!ReferenceEquals(selected, doc)) ReleaseCom(selected);
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
