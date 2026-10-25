using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Worker.ViewModels;

namespace AeroSpot.Worker
{
    public partial class MainWindow : Window
    {
        public LauncherViewModel ViewModel { get; }

        public event Action? HideRequested;
        public event Action? CloseRequested;
        public event Action? CloseAllRequested;
        public event Action? SettingsRequested;
        public event Action<string>? PreviewRequested;
        public event Action<int>? PreviewScrollRequested;

        public bool IsPreviewActive { get; set; }

        private bool _isNavigatingResults;

        public MainWindow()
        {
            ViewModel = new LauncherViewModel();
            this.DataContext = ViewModel;

            InitializeComponent();

            SearchBox.TextChanged += (s, e) =>
            {
                _isNavigatingResults = false;
            };

            this.Deactivated += (s, e) =>
            {
                // Quando la finestra perde il focus (es. utente clicca fuori), nascondila
                // purché non ci sia un'anteprima QuickLook attualmente aperta
                if (!App.IsShuttingDown && !IsPreviewActive)
                {
                    HideRequested?.Invoke();
                }
            };
        }

        public void ShowOnActiveMonitor()
        {
            // Reset completo della ricerca precedente
            ViewModel.SearchText = string.Empty;
            SearchBox.Text = string.Empty;
            _isNavigatingResults = false;

            // 1. Trova il monitor su cui si trova attualmente il cursore del mouse
            Win32.GetCursorPos(out Win32.POINT cursorPt);
            IntPtr hMonitor = Win32.MonitorFromPoint(cursorPt, Win32.MONITOR_DEFAULTTONEAREST);

            var mi = new Win32.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>();

            double left = 100;
            double top = 100;

            if (Win32.GetMonitorInfo(hMonitor, ref mi))
            {
                // Converti da coordinate fisiche a coordinate WPF (DIPs)
                Matrix transform = Matrix.Identity;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    transform = source.CompositionTarget.TransformFromDevice;
                }

                Point workTopLeft = transform.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                Point workBottomRight = transform.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));

                double workWidth = workBottomRight.X - workTopLeft.X;
                double workHeight = workBottomRight.Y - workTopLeft.Y;

                // Centra orizzontalmente e posiziona a circa il 18% dall'alto del monitor attivo
                left = workTopLeft.X + (workWidth - this.Width) / 2.0;
                top = workTopLeft.Y + (workHeight * 0.18);
            }

            this.Left = left;
            this.Top = top;

            this.Show();

            // Forza il primo piano reale rispetto a finestre esterne (es. Chrome, Blocco note)
            IntPtr hWnd = new WindowInteropHelper(this).EnsureHandle();
            Win32.SetForegroundWindow(hWnd);
            this.Activate();
            this.Topmost = true;

            // Focalizza la casella di ricerca per digitazione immediata
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
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
                HideRequested?.Invoke();
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

        private void ContextOpenPath_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
            {
                string target = ViewModel.SelectedItem.TargetPath;
                try
                {
                    AppLogger.Info($"Apertura percorso contenitore per: {target}");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{target}\"",
                        UseShellExecute = true
                    });
                    HideRequested?.Invoke();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Impossibile aprire il percorso del file {target}", ex);
                }
            }
        }

        private void ContextOpen_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ExecuteSelected())
            {
                HideRequested?.Invoke();
            }
        }

        private void ContextPreview_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
            {
                IsPreviewActive = true;
                PreviewRequested?.Invoke(ViewModel.SelectedItem.TargetPath);
            }
        }

        private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedItem != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedItem.TargetPath))
            {
                try
                {
                    Clipboard.SetText(ViewModel.SelectedItem.TargetPath);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Impossibile copiare il percorso negli appunti: {ex.Message}");
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SearchText = string.Empty;
            SearchBox.Focus();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke();
        }

        private async void ReindexNowButton_Click(object sender, RoutedEventArgs e)
        {
            if (BtnReindexNow != null)
            {
                BtnReindexNow.Content = "⏳ Scansione in corso...";
                BtnReindexNow.IsEnabled = false;
            }

            try
            {
                await Task.Run(() =>
                {
                    ViewModel.TriggerFullReindex(force: true);
                });

                if (BtnReindexNow != null)
                {
                    BtnReindexNow.Content = "✓ Indice aggiornato";
                    await Task.Delay(2500);
                    BtnReindexNow.Content = "🔄 Aggiorna Indice";
                    BtnReindexNow.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"ReindexNowButton_Click: {ex.Message}");
                if (BtnReindexNow != null)
                {
                    BtnReindexNow.Content = "🔄 Aggiorna Indice";
                    BtnReindexNow.IsEnabled = true;
                }
            }
        }

        public void SetIpcStatus(string status)
        {
            // Telemetria/status diagnostico
        }

        public void SetIdleStatus(string status)
        {
            // Telemetria/status idle
        }

        public void RequestClose()
        {
            CloseRequested?.Invoke();
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