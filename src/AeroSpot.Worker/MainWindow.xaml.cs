using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Worker.Models;
using AeroSpot.Worker.Services;
using AeroSpot.Worker.ViewModels;

namespace AeroSpot.Worker
{
    public partial class MainWindow : Window
    {
        public LauncherViewModel ViewModel { get; }

        public event Action? HideRequested;
        public event Action? CloseAllRequested;
        public event Action? SettingsRequested;
        public event Action<string>? PreviewRequested;
        public event Action<int>? PreviewScrollRequested;

        public bool IsPreviewActive { get; set; }

        private bool _isNavigatingResults;
        private bool _isConfirmingDelete;
        private AeroSpot.Core.Config.AppConfig _config = AeroSpot.Core.Config.AppConfig.Load();

        private static readonly SolidColorBrush FilterSelectedBg = CreateFrozenBrush("#0284C7");
        private static readonly SolidColorBrush FilterSelectedBorder = CreateFrozenBrush("#38BDF8");
        private static readonly SolidColorBrush FilterUnselectedBg = CreateFrozenBrush("#262C36");
        private static readonly SolidColorBrush FilterUnselectedFg = CreateFrozenBrush("#CBD5E1");
        private static readonly SolidColorBrush FilterUnselectedBorder = CreateFrozenBrush("#3B4252");
        private static readonly Thickness FilterSelectedThickness = new Thickness(1.8);
        private static readonly Thickness FilterUnselectedThickness = new Thickness(1.0);

        private static SolidColorBrush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public void UpdateConfig(AeroSpot.Core.Config.AppConfig config)
        {
            _config = config;
        }

        public MainWindow()
        {
            ViewModel = new LauncherViewModel(enableAsyncDebounce: true);
            ViewModel.SettingsRequested += () => SettingsRequested?.Invoke();
            this.DataContext = ViewModel;

            InitializeComponent();

            SearchBox.TextChanged += (s, e) =>
            {
                _isNavigatingResults = false;
                ResetResultsScroll();
            };

            ResultsList.Loaded += (s, e) =>
            {
                ResetResultsScroll();
            };

            this.Deactivated += (s, e) =>
            {
                // Quando la finestra perde il focus (es. utente clicca fuori), nascondila
                // purché non ci sia un'anteprima QuickLook o una finestra di conferma eliminazione aperta
                if (!App.IsShuttingDown && !IsPreviewActive && !_isConfirmingDelete)
                {
                    SetSelectedFilter("All");
                    HideRequested?.Invoke();
                }
            };

            InitializeFilterPills();
        }

        private void InitializeFilterPills()
        {
            SetSelectedFilter("All");
        }

        private void FilterPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                SetSelectedFilter(tag);
                SearchBox.Focus();
            }
        }

        private readonly string[] _filterTags = new[] { "All", "Applicazione", "Immagine", "Documento", "Video", "Audio", "Cartella", "Impostazioni" };

        public void CycleFilter(bool forward)
        {
            int currentIndex = Array.FindIndex(_filterTags, t => string.Equals(t, ViewModel.SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = forward
                ? (currentIndex + 1) % _filterTags.Length
                : (currentIndex - 1 + _filterTags.Length) % _filterTags.Length;

            SetSelectedFilter(_filterTags[newIndex]);
        }

        public void SetSelectedFilter(string categoryTag)
        {
            ViewModel.SelectedCategoryFilter = categoryTag;
            ResetResultsScroll();

            var buttons = new Button[] { FilterBtnAll, FilterBtnApps, FilterBtnPhotos, FilterBtnDocs, FilterBtnVideos, FilterBtnAudio, FilterBtnFolders, FilterBtnSettings };
            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                bool isSelected = string.Equals(btn.Tag as string, categoryTag, StringComparison.OrdinalIgnoreCase);
                if (isSelected)
                {
                    btn.Background = FilterSelectedBg;
                    btn.Foreground = Brushes.White;
                    btn.BorderBrush = FilterSelectedBorder;
                    btn.BorderThickness = FilterSelectedThickness;
                }
                else
                {
                    btn.Background = FilterUnselectedBg;
                    btn.Foreground = FilterUnselectedFg;
                    btn.BorderBrush = FilterUnselectedBorder;
                    btn.BorderThickness = FilterUnselectedThickness;
                }
            }
        }

        public void ShowOnActiveMonitor()
        {
            // Assicura l'handle e mantieni DWM Cloaking + Opacity 0 finché il frame pulito non è stato renderizzato
            IntPtr hWnd = new WindowInteropHelper(this).EnsureHandle();
            Win32.SetWindowCloak(hWnd, true);
            this.Opacity = 0;

            // Reset completo e immediato della ricerca precedente
            _isNavigatingResults = false;
            SearchBox.Text = string.Empty;
            ViewModel.ResetSearchImmediate();
            SetSelectedFilter("All");
            ResultsList.SelectedIndex = -1;
            ResetResultsScroll();
            UpdateLayout();

            // 1. Trova il monitor su cui si trova attualmente il cursore del mouse
            Win32.GetCursorPos(out Win32.POINT cursorPt);
            IntPtr hMonitor = Win32.MonitorFromPoint(cursorPt, Win32.MONITOR_DEFAULTTONEAREST);

            var mi = new Win32.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>();

            double left = 100;
            double top = 100;

            if (Win32.GetMonitorInfo(hMonitor, ref mi))
            {
                // Rileva il DPI effettivo del monitor per convertire da coordinate fisiche a DIPs WPF
                double dpiScaleX = 1.0;
                double dpiScaleY = 1.0;

                try
                {
                    if (Win32.GetDpiForMonitor(hMonitor, Win32.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
                    {
                        dpiScaleX = dpiX / 96.0;
                        dpiScaleY = dpiY / 96.0;
                    }
                    else
                    {
                        PresentationSource source = PresentationSource.FromVisual(this) ?? HwndSource.FromHwnd(hWnd);
                        if (source?.CompositionTarget != null)
                        {
                            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                        }
                    }
                }
                catch
                {
                    PresentationSource source = PresentationSource.FromVisual(this) ?? HwndSource.FromHwnd(hWnd);
                    if (source?.CompositionTarget != null)
                    {
                        dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                        dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                    }
                }

                if (dpiScaleX <= 0) dpiScaleX = 1.0;
                if (dpiScaleY <= 0) dpiScaleY = 1.0;

                double workLeft = mi.rcWork.Left / dpiScaleX;
                double workTop = mi.rcWork.Top / dpiScaleY;
                double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiScaleX;
                double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScaleY;

                double windowWidth = this.ActualWidth > 0 ? this.ActualWidth : (this.Width > 0 ? this.Width : 680.0);

                // Centra orizzontalmente e posiziona a circa il 18% dall'alto del monitor attivo
                left = workLeft + (workWidth - windowWidth) / 2.0;
                top = workTop + (workHeight * 0.18);
            }

            this.Left = left;
            this.Top = top;

            this.Show();
            this.UpdateLayout();

            // Svela la finestra in DWM solo a ContextIdle, cioè DOPO che i passaggi Loaded e Render
            // hanno aggiornato il buffer grafico con SearchBox vuota e lista risultati resettata.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!this.IsVisible) return;
                this.UpdateLayout();
                Win32.SetWindowCloak(hWnd, false);
                this.Opacity = 1.0;
                this.Topmost = false;
                this.Topmost = true;
                Win32.ForceForegroundWindow(hWnd, topmost: true);
                this.Activate();

                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        public void PrepareForHide()
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            if (hWnd != IntPtr.Zero)
            {
                Win32.SetWindowCloak(hWnd, true);
            }
            this.Opacity = 0;
            _isNavigatingResults = false;
            SearchBox.Text = string.Empty;
            ViewModel.ResetSearchImmediate();
            SetSelectedFilter("All");
            ResultsList.SelectedIndex = -1;
            ResetResultsScroll();
            UpdateLayout();
        }

        private void SearchBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DeselectResults();
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            DeselectResults();
        }

        private void DeselectResults()
        {
            _isNavigatingResults = false;
            ViewModel.SelectedIndex = -1;
            ViewModel.SelectedItem = null;
            ResultsList.SelectedIndex = -1;
        }

        private static uint GetCurrentWin32Modifiers()
        {
            uint mod = 0;
            var km = Keyboard.Modifiers;
            if ((km & ModifierKeys.Alt) != 0) mod |= Win32.MOD_ALT;
            if ((km & ModifierKeys.Control) != 0) mod |= Win32.MOD_CONTROL;
            if ((km & ModifierKeys.Shift) != 0) mod |= Win32.MOD_SHIFT;
            if ((km & ModifierKeys.Windows) != 0) mod |= Win32.MOD_WIN;
            return mod;
        }

        private bool IsDeleteShortcutPressed(KeyEventArgs e)
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
            uint currentMod = GetCurrentWin32Modifiers();
            uint configuredMod = _config.DeleteModifiers & (Win32.MOD_ALT | Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_WIN);

            if (_config.DeleteKey != 0)
            {
                return vk == _config.DeleteKey && currentMod == configuredMod;
            }

            return (e.Key == Key.Delete || vk == Win32.VK_DELETE) && currentMod == 0;
        }

        private bool TryDeleteSelectedItemWithConfirmation()
        {
            if (ViewModel.SelectedItem == null || ViewModel.SelectedItem.IsActionItem)
            {
                return false;
            }

            AppLogger.Info($"Richiesta eliminazione elemento: {ViewModel.SelectedItem.TargetPath}");
            _isConfirmingDelete = true;
            bool deleted;
            try
            {
                deleted = ViewModel.DeleteSelectedItem();
            }
            finally
            {
                _isConfirmingDelete = false;
            }

            if (deleted)
            {
                var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_FileRecycled"), "🗑️");
                toast.SetSuccess(LocalizationService.Get("Notification_FileRecycled"), string.Empty);
            }

            if (IsVisible)
            {
                Activate();
            }

            return true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsDeleteShortcutPressed(e))
            {
                uint configuredMod = _config.DeleteModifiers & (Win32.MOD_ALT | Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_WIN);
                bool isBareDeleteKey = configuredMod == 0 && (_config.DeleteKey == 0 || _config.DeleteKey == Win32.VK_DELETE);

                // Se la SearchBox è attiva e l'utente sta digitando (non sta scorrendo i risultati),
                // lasciamo che Delete senza modificatori cancelli il testo come un normale editor
                if (SearchBox.IsKeyboardFocused && !_isNavigatingResults && isBareDeleteKey)
                {
                    return;
                }

                if (TryDeleteSelectedItemWithConfirmation())
                {
                    e.Handled = true;
                }
            }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    _isNavigatingResults = true;
                    ViewModel.MoveSelectionDown();
                    ScrollSelectedIntoView();
                    if (IsPreviewActive && ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
                    {
                        PreviewRequested?.Invoke(ViewModel.SelectedItem.TargetPath);
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    _isNavigatingResults = true;
                    ViewModel.MoveSelectionUp();
                    ScrollSelectedIntoView();
                    if (IsPreviewActive && ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
                    {
                        PreviewRequested?.Invoke(ViewModel.SelectedItem.TargetPath);
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (ViewModel.ExecuteSelected())
                    {
                        CloseAllRequested?.Invoke();
                        HideRequested?.Invoke();
                    }
                    e.Handled = true;
                    break;

                case Key.Escape:
                    // Chiusura globale su Esc (chiude sia Spotlight che QuickLook se aperto)
                    CloseAllRequested?.Invoke();
                    HideRequested?.Invoke();
                    e.Handled = true;
                    break;

                case Key.Tab:
                    // Navigazione ciclica dei filtri con Tab (avanti) e Shift+Tab (indietro)
                    bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                    CycleFilter(forward: !isShift);
                    e.Handled = true;
                    break;

                case Key.Space:
                    // Se l'utente si è spostato con le frecce sui risultati (o preme Shift+Spazio), apre l'anteprima QuickLook!
                    if ((_isNavigatingResults || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) 
                        && ViewModel.SelectedItem != null 
                        && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
                    {
                        IsPreviewActive = true;
                        PreviewRequested?.Invoke(ViewModel.SelectedItem.TargetPath);
                        e.Handled = true;
                    }
                    // Altrimenti durante la normale digitazione dello scriba inserisce il carattere spazio
                    break;
            }
        }

        private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
                {
                    IsPreviewActive = true;
                    PreviewRequested?.Invoke(ViewModel.SelectedItem.TargetPath);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                CloseAllRequested?.Invoke();
                HideRequested?.Invoke();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                CycleFilter(forward: !isShift);
                SearchBox.Focus();
                e.Handled = true;
            }
        }

        private ScrollViewer? _resultsScrollViewer;

        public ScrollViewer? GetResultsScrollViewer()
        {
            if (_resultsScrollViewer != null) return _resultsScrollViewer;
            _resultsScrollViewer = FindVisualChild<ScrollViewer>(ResultsList);
            return _resultsScrollViewer;
        }

        public void ResetResultsScroll()
        {
            var sv = GetResultsScrollViewer();
            if (sv != null)
            {
                sv.ScrollToTop();
                sv.ScrollToVerticalOffset(0);
            }
            if (ResultsList.Items.Count > 0)
            {
                ResultsList.ScrollIntoView(ResultsList.Items[0]);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var sub = FindVisualChild<T>(child);
                if (sub != null) return sub;
            }
            return null;
        }

        private void ScrollSelectedIntoView()
        {
            if (ResultsList.SelectedItem != null)
            {
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.ExecuteSelected())
            {
                CloseAllRequested?.Invoke();
                HideRequested?.Invoke();
            }
        }

        private Point _dragStartPoint;
        private Point _dragStartListPoint;
        private bool _isDragging;

        private void ResultsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _dragStartListPoint = e.GetPosition(ResultsList);
        }

        private void ResultsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point currentPoint = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    SearchResultItem? item = null;
                    var hit = VisualTreeHelper.HitTest(ResultsList, _dragStartListPoint);
                    DependencyObject? current = hit?.VisualHit;
                    while (current != null && current != ResultsList)
                    {
                        if (current is ListBoxItem lbi && lbi.DataContext is SearchResultItem hitItem)
                        {
                            item = hitItem;
                            lbi.IsSelected = true;
                            break;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }

                    if (item != null && !item.IsActionItem && !string.IsNullOrWhiteSpace(item.TargetPath) &&
                        (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath)))
                    {
                        _isDragging = true;
                        DragGhostWindow? ghost = null;
                        try
                        {
                            ghost = new DragGhostWindow(item.Icon, item.Title);
                            new WindowInteropHelper(ghost).EnsureHandle();
                            ghost.Show();
                            ghost.MoveToCursor(14, 14);

                            GiveFeedbackEventHandler feedbackHandler = (s, args) =>
                            {
                                args.UseDefaultCursors = true;
                                ghost.MoveToCursor(16, 16);
                            };

                            ResultsList.GiveFeedback += feedbackHandler;
                            try
                            {
                                var data = new DataObject(DataFormats.FileDrop, new string[] { item.TargetPath });
                                DragDropEffects allowedEffects;
                                if (IsProtectedSystemOrAppItem(item))
                                {
                                    allowedEffects = DragDropEffects.Copy | DragDropEffects.Link;
                                }
                                else
                                {
                                    switch (_config.DragDropMode)
                                    {
                                        case AeroSpot.Core.Config.SpotlightDragDropMode.AlwaysCopy:
                                            allowedEffects = DragDropEffects.Copy | DragDropEffects.Link;
                                            break;
                                        case AeroSpot.Core.Config.SpotlightDragDropMode.AlwaysMove:
                                            allowedEffects = DragDropEffects.Move | DragDropEffects.Copy;
                                            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
                                            break;
                                        case AeroSpot.Core.Config.SpotlightDragDropMode.WindowsDefault:
                                        default:
                                            allowedEffects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
                                            break;
                                    }
                                }
                                DragDrop.DoDragDrop(ResultsList, data, allowedEffects);
                            }
                            finally
                            {
                                ResultsList.GiveFeedback -= feedbackHandler;
                            }

                            if (!File.Exists(item.TargetPath) && !Directory.Exists(item.TargetPath))
                            {
                                ViewModel.NotifyItemMovedOrDeleted(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"DragDrop error: {ex.Message}");
                        }
                        finally
                        {
                            try { ghost?.Close(); } catch { }
                            _isDragging = false;
                        }
                    }
                }
            }
        }

        private static bool IsProtectedSystemOrAppItem(SearchResultItem item)
        {
            if (string.Equals(item.Category, "Applicazione", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, "Application", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string path = item.TargetPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return true;

            string[] protectedRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };

            foreach (var root in protectedRoots)
            {
                if (!string.IsNullOrWhiteSpace(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(ResultsList, e.GetPosition(ResultsList));
            DependencyObject? current = hit?.VisualHit;
            while (current != null && current != ResultsList)
            {
                if (current is ListBoxItem lbi && lbi.DataContext is SearchResultItem sri && (sri.Category == "Ricerca" || sri.Category == "Search"))
                {
                    ViewModel.ExecuteSelected();
                    SearchBox.Focus();
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                    e.Handled = true;
                    return;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(ResultsList, e.GetPosition(ResultsList));
            DependencyObject? current = hit?.VisualHit;
            while (current != null && current != ResultsList)
            {
                if (current is ListBoxItem item)
                {
                    item.IsSelected = true;
                    _isNavigatingResults = true;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void ResultsContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var item = ViewModel.SelectedItem;
            string? path = item?.TargetPath;
            bool exists = !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

            bool canDelete = item != null && !item.IsActionItem && exists;
            MenuSeparatorDelete.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
            MenuRecycleFile.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;

            if (!exists || !File.Exists(path))
            {
                MenuSeparatorMedia.Visibility = Visibility.Collapsed;
                MenuConvertJpg.Visibility = Visibility.Collapsed;
                MenuConvertPng.Visibility = Visibility.Collapsed;
                MenuConvertPdf.Visibility = Visibility.Collapsed;
                MenuConvertMp3.Visibility = Visibility.Collapsed;
                MenuConvertWav.Visibility = Visibility.Collapsed;
                MenuConvertMp4.Visibility = Visibility.Collapsed;
                MenuRemoveBg.Visibility = Visibility.Collapsed;
                return;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool isJpg = ext is ".jpg" or ".jpeg";
            bool isPng = ext is ".png";
            bool isPdf = ext is ".pdf";
            bool isDoc = ext is ".md" or ".txt" or ".docx" or ".rtf" or ".odt";
            bool isImage = isJpg || isPng || ext is ".webp" or ".bmp" or ".gif" or ".tiff" or ".ico";
            bool isVideo = ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".flv" or ".m4v" or ".webm" or ".ts";
            bool isMp3 = ext is ".mp3";
            bool isWav = ext is ".wav";
            bool isAudio = isMp3 || isWav || ext is ".m4a" or ".flac" or ".aac" or ".wma" or ".ogg" or ".aiff" or ".opus";

            bool canConvertJpg = isImage && !isJpg;
            bool canConvertPng = isImage && !isPng;
            bool canConvertPdf = (isImage || isDoc) && !isPdf;
            bool canRemoveBg = isImage;
            bool canConvertMp3 = (isVideo || isAudio) && !isMp3;
            bool canConvertWav = (isVideo || isAudio) && !isWav;
            bool canConvertMp4 = isVideo && ext != ".mp4";

            bool hasMediaAction = canConvertJpg || canConvertPng || canConvertPdf || canConvertMp3 || canConvertWav || canConvertMp4 || canRemoveBg;

            MenuSeparatorMedia.Visibility = hasMediaAction ? Visibility.Visible : Visibility.Collapsed;
            MenuConvertJpg.Visibility = canConvertJpg ? Visibility.Visible : Visibility.Collapsed;
            MenuConvertPng.Visibility = canConvertPng ? Visibility.Visible : Visibility.Collapsed;
            MenuConvertPdf.Visibility = canConvertPdf ? Visibility.Visible : Visibility.Collapsed;
            MenuConvertMp3.Visibility = canConvertMp3 ? Visibility.Visible : Visibility.Collapsed;
            MenuConvertWav.Visibility = canConvertWav ? Visibility.Visible : Visibility.Collapsed;
            MenuConvertMp4.Visibility = canConvertMp4 ? Visibility.Visible : Visibility.Collapsed;
            MenuRemoveBg.Visibility = canRemoveBg ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ContextConvertJpg_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteConvertJpgAsync(path, _config);
        }

        private void ContextConvertPng_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteConvertPngAsync(path, _config);
        }

        private void ContextConvertPdf_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteConvertPdfAsync(path, _config);
        }

        private void ContextConvertMp3_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteExtractMp3Async(path, _config);
        }

        private void ContextConvertWav_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteConvertWavAsync(path, _config);
        }

        private void ContextConvertMp4_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteConvertMp4Async(path, _config);
        }

        private void ContextRemoveBg_Click(object sender, RoutedEventArgs e)
        {
            string? path = ViewModel.SelectedItem?.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            HideRequested?.Invoke();
            _ = FileActionService.ExecuteRemoveBgAsync(path, _config);
        }

        private void ContextRecycleFile_Click(object sender, RoutedEventArgs e)
        {
            TryDeleteSelectedItemWithConfirmation();
        }

        private void ContextOpenPath_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
            {
                string target = ViewModel.SelectedItem.TargetPath;
                FileActionService.RevealFileInExplorer(target);
                HideRequested?.Invoke();
            }
        }

        private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
            {
                try
                {
                    Clipboard.SetText(ViewModel.SelectedItem.TargetPath);
                    var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_PathCopied"), "📋");
                    toast.SetSuccess(LocalizationService.Get("Notification_PathCopied"), ViewModel.SelectedItem.TargetPath);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Impossibile copiare il percorso negli appunti: {ex.Message}");
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            ViewModel.ResetSearchImmediate();
            SearchBox.Focus();
            ResetResultsScroll();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke();
        }

        private async void ReindexNowButton_Click(object sender, RoutedEventArgs e)
        {
            if (BtnReindexNow != null)
            {
                BtnReindexNow.Content = LocalizationService.Get("Spotlight_Btn_Reindexing");
                BtnReindexNow.IsEnabled = false;
            }

            try
            {
                await ViewModel.TriggerFullReindexAsync(force: true);

                if (BtnReindexNow != null)
                {
                    BtnReindexNow.Content = LocalizationService.Get("Spotlight_Btn_Reindexed");
                    await Task.Delay(2500);
                    BtnReindexNow.Content = LocalizationService.Get("Spotlight_Btn_Reindex");
                    BtnReindexNow.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"ReindexNowButton_Click: {ex.Message}");
                if (BtnReindexNow != null)
                {
                    BtnReindexNow.Content = LocalizationService.Get("Spotlight_Btn_Reindex");
                    BtnReindexNow.IsEnabled = true;
                }
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (IsPreviewActive)
            {
                PreviewScrollRequested?.Invoke(e.Delta);
                e.Handled = true;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsShuttingDown)
            {
                e.Cancel = true;
                HideRequested?.Invoke();
            }
        }
    }
}