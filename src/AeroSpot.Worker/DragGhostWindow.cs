using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AeroSpot.Core.Native;

namespace AeroSpot.Worker
{
    public class DragGhostWindow : Window
    {
        public DragGhostWindow(ImageSource? icon, string title)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            IsHitTestVisible = false;
            Focusable = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 30, 35, 43)), // #EB1E232B Fluent Dark
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")), // #38BDF8 Sky Cyan
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 12, 6),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 14,
                    ShadowDepth = 3,
                    Opacity = 0.55
                }
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (icon != null)
            {
                var img = new Image
                {
                    Source = icon,
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                stack.Children.Add(img);
            }

            var text = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                MaxWidth = 260,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(text);

            border.Child = stack;
            Content = border;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                // WS_EX_TRANSPARENT (0x00000020): Passa tutti gli hit test del cursore alla finestra sottostante (nessun lag su XAML Islands di Explorer)
                // WS_EX_TOOLWINDOW (0x00000080): Esclude la finestra da Alt+Tab
                // WS_EX_NOACTIVATE (0x08000000): Non riceve mai il focus
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle | 0x00000020 | 0x00000080 | 0x08000000);
            }
        }
    }
}
