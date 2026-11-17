using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AeroSpot.Core.Native;

namespace AeroSpot.Worker
{
    public partial class FloatingNotificationWindow : Window
    {
        private DispatcherTimer? _autoCloseTimer;
        private string? _targetFilePath;
        private readonly IntPtr _targetMonitor;

        public FloatingNotificationWindow()
        {
            InitializeComponent();
            BtnShowInFolder.Content = AeroSpot.Core.Localization.LocalizationService.Get("Notification_OpenFolder");

            // Individua e blocca il monitor su cui si trova attualmente il puntatore
            Win32.GetCursorPos(out Win32.POINT cursorPt);
            _targetMonitor = Win32.MonitorFromPoint(cursorPt, Win32.MONITOR_DEFAULTTONEAREST);

            Loaded += OnLoaded;
            SizeChanged += (_, _) => PositionInBottomRight();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionInBottomRight();
        }

        public void PositionInBottomRight()
        {
            try
            {
                var mi = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
                IntPtr hMon = _targetMonitor;
                if (hMon == IntPtr.Zero)
                {
                    Win32.GetCursorPos(out Win32.POINT pt);
                    hMon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
                }

                if (Win32.GetMonitorInfo(hMon, ref mi))
                {
                    double dpiScaleX = 1.0;
                    double dpiScaleY = 1.0;

                    try
                    {
                        if (Win32.GetDpiForMonitor(hMon, Win32.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
                        {
                            dpiScaleX = dpiX / 96.0;
                            dpiScaleY = dpiY / 96.0;
                        }
                    }
                    catch { }

                    if (dpiScaleX <= 0) dpiScaleX = 1.0;
                    if (dpiScaleY <= 0) dpiScaleY = 1.0;

                    int marginX = (int)Math.Round(16 * dpiScaleX);
                    int marginY = (int)Math.Round(16 * dpiScaleY);

                    IntPtr hWnd = new WindowInteropHelper(this).Handle;
                    if (hWnd != IntPtr.Zero && Win32.GetWindowRect(hWnd, out Win32.RECT winRect))
                    {
                        int physW = winRect.Right - winRect.Left;
                        int physH = winRect.Bottom - winRect.Top;

                        if (physW > 0 && physH > 0)
                        {
                            int targetX = Math.Max(mi.rcWork.Left + marginX, mi.rcWork.Right - physW - marginX);
                            int targetY = Math.Max(mi.rcWork.Top + marginY, mi.rcWork.Bottom - physH - marginY);

                            Win32.SetWindowPos(
                                hWnd,
                                IntPtr.Zero,
                                targetX,
                                targetY,
                                0,
                                0,
                                Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                            return;
                        }
                    }

                    // Fallback pre-rendering con stima in DIPs
                    double workRightDip = mi.rcWork.Right / dpiScaleX;
                    double workBottomDip = mi.rcWork.Bottom / dpiScaleY;
                    double w = ActualWidth > 0 ? ActualWidth : 340;
                    double h = ActualHeight > 0 ? ActualHeight : 110;
                    Left = Math.Max(mi.rcWork.Left / dpiScaleX + 16, workRightDip - w - 16);
                    Top = Math.Max(mi.rcWork.Top / dpiScaleY + 16, workBottomDip - h - 16);
                    return;
                }
            }
            catch { }

            var workArea = SystemParameters.WorkArea;
            double fw = ActualWidth > 0 ? ActualWidth : 340;
            double fh = ActualHeight > 0 ? ActualHeight : 110;
            Left = Math.Max(workArea.Left + 16, workArea.Right - fw - 16);
            Top = Math.Max(workArea.Top + 16, workArea.Bottom - fh - 16);
        }

        public void UpdateProgress(double percentage, string detailText, string? title = null, string? icon = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (title != null) TxtTitle.Text = title;
                if (icon != null) TxtIcon.Text = icon;
                TxtDetail.Text = detailText;

                ProgressBar.IsIndeterminate = percentage < 0;
                if (percentage >= 0)
                {
                    ProgressBar.Value = Math.Clamp(percentage, 0, 100);
                }

                UpdateLayout();
                PositionInBottomRight();
            });
        }

        public void SetSuccess(string title, string detailText, string? resultFilePath = null)
        {
            Dispatcher.Invoke(() =>
            {
                TxtIcon.Text = "✓";
                TxtTitle.Text = title;
                TxtDetail.Text = detailText;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
                ProgressBar.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;

                _targetFilePath = resultFilePath;
                if (!string.IsNullOrEmpty(resultFilePath) && File.Exists(resultFilePath))
                {
                    BtnShowInFolder.Visibility = Visibility.Visible;
                }

                UpdateLayout();
                PositionInBottomRight();
                ScheduleAutoClose(4000);
            });
        }

        public void SetError(string title, string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                TxtIcon.Text = "⚠️";
                TxtTitle.Text = title;
                TxtDetail.Text = errorMessage;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
                ProgressBar.Foreground = System.Windows.Media.Brushes.Crimson;

                UpdateLayout();
                PositionInBottomRight();
                ScheduleAutoClose(5000);
            });
        }

        private void ScheduleAutoClose(int milliseconds)
        {
            _autoCloseTimer?.Stop();
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            _autoCloseTimer.Tick += (s, ev) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            _autoCloseTimer.Start();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            Close();
        }

        private void BtnShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_targetFilePath) && File.Exists(_targetFilePath))
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{_targetFilePath}\"");
                }
                catch { }
            }
            Close();
        }

        public static FloatingNotificationWindow Spawn(string initialTitle, string initialIcon = "✨")
        {
            var win = new FloatingNotificationWindow();
            win.TxtTitle.Text = initialTitle;
            win.TxtIcon.Text = initialIcon;
            win.PositionInBottomRight();
            win.Show();
            win.PositionInBottomRight();
            return win;
        }
    }
}
