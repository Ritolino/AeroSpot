using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;

namespace AeroSpot.Worker.Services
{
    public static class IconService
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new ConcurrentDictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? GetIcon(string path, string category)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string key;
            if (path.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
            {
                key = path;
            }
            else if (category == "Cartella" || Directory.Exists(path))
            {
                key = ":folder:";
            }
            else if (category is "Calcolatrice" or "Calculator")
            {
                return GetCalculatorIcon();
            }
            else if (category is "Conversione" or "Conversion")
            {
                return GetConversionIcon();
            }
            else if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                key = path;
            }
            else
            {
                key = Path.GetExtension(path).ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) key = ":file:";
            }

            return _cache.GetOrAdd(key, _ => LoadNativeIcon(path, category));
        }

        private static ImageSource? LoadNativeIcon(string path, string category)
        {
            // 0. Cartelle: recupera l'icona di cartella di sistema tramite attributo FILE_ATTRIBUTE_DIRECTORY
            if (category == "Cartella" || path == ":folder:" || Directory.Exists(path))
            {
                try
                {
                    var shinfo = new Win32.SHFILEINFO();
                    uint flags = Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON | Win32.SHGFI_USEFILEATTRIBUTES;
                    const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

                    IntPtr res = Win32.SHGetFileInfo("folder", FILE_ATTRIBUTE_DIRECTORY, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                    if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var img = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            img.Freeze();
                            return img;
                        }
                        finally
                        {
                            Win32.DestroyIcon(shinfo.hIcon);
                        }
                    }
                }
                catch { }
            }

            // 1. Per app UWP / Store (es. WhatsApp, Impostazioni, Calcolatrice) usa IShellItemImageFactory
            if (path.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
            {
                var uwpIcon = LoadShellItemIcon(path, 32);
                if (uwpIcon != null) return uwpIcon;
            }

            // 2. Icona standard da SHGetFileInfo
            try
            {
                var shinfo = new Win32.SHFILEINFO();
                uint flags = Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON;

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    flags |= Win32.SHGFI_USEFILEATTRIBUTES;
                }

                IntPtr res = Win32.SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var img = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        img.Freeze();
                        return img;
                    }
                    finally
                    {
                        Win32.DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }

            // 3. Fallback con IShellItemImageFactory per file regolari
            if (File.Exists(path) || Directory.Exists(path))
            {
                return LoadShellItemIcon(path, 32);
            }

            return null;
        }

        private static ImageSource? LoadShellItemIcon(string parsingName, int size = 32)
        {
            try
            {
                Win32.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, Win32.IID_IShellItemImageFactory, out object itemObj);
                if (itemObj is Win32.IShellItemImageFactory factory)
                {
                    var iconSize = new Win32.SIZE(size, size);
                    int hr = factory.GetImage(iconSize, Win32.SIIGBF.SIIGBF_RESIZETOFIT | Win32.SIIGBF.SIIGBF_ICONONLY, out IntPtr hBitmap);
                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            var img = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            img.Freeze();
                            return img;
                        }
                        finally
                        {
                            Win32.DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static BitmapSource? GetImageFactoryBitmap(string filePath, int size = 1200)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            if (System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA)
            {
                return ExtractImageFactoryBitmapInternal(filePath, size);
            }

            BitmapSource? result = null;
            var thread = new System.Threading.Thread(() =>
            {
                result = ExtractImageFactoryBitmapInternal(filePath, size);
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join(1500);
            return result;
        }

        private static BitmapSource? ExtractImageFactoryBitmapInternal(string filePath, int size)
        {
            try
            {
                Win32.SHCreateItemFromParsingName(filePath, IntPtr.Zero, Win32.IID_IShellItemImageFactory, out object itemObj);
                if (itemObj is Win32.IShellItemImageFactory factory)
                {
                    int[] sizes = new[] { size, 800, 512, 256 };
                    foreach (int sz in sizes)
                    {
                        var iconSize = new Win32.SIZE(sz, sz);
                        int hr = factory.GetImage(iconSize, Win32.SIIGBF.SIIGBF_RESIZETOFIT, out IntPtr hBitmap);
                        if (hr != 0 || hBitmap == IntPtr.Zero)
                        {
                            hr = factory.GetImage(iconSize, Win32.SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
                        }
                        if (hr != 0 || hBitmap == IntPtr.Zero)
                        {
                            hr = factory.GetImage(iconSize, 0, out hBitmap);
                        }

                        if (hr == 0 && hBitmap != IntPtr.Zero)
                        {
                            try
                            {
                                var img = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                img.Freeze();
                                return img;
                            }
                            finally
                            {
                                Win32.DeleteObject(hBitmap);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"GetImageFactoryBitmap non riuscito per {filePath}: {ex.Message}");
            }

            return null;
        }

        private static ImageSource? _calcIcon;
        private static ImageSource? _convIcon;

        public static ImageSource? GetCalculatorIcon()
        {
            if (_calcIcon != null) return _calcIcon;
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Icona calcolatrice vettoriale nitida
                    var bgBrush = new SolidColorBrush(Color.FromRgb(30, 74, 56));
                    var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(78, 201, 176)), 1.2);
                    dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(1, 1, 18, 18), 3, 3);

                    var textBrush = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                    var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    var formattedText = new FormattedText("=", System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, font, 11, textBrush, 1.25);
                    dc.DrawText(formattedText, new Point(6, 3));
                }
                var rtb = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                _calcIcon = rtb;
                return _calcIcon;
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource? GetConversionIcon()
        {
            if (_convIcon != null) return _convIcon;
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Icona conversione vettoriale nitida (frecce o simbolo scambio)
                    var bgBrush = new SolidColorBrush(Color.FromRgb(75, 46, 88));
                    var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 220, 170)), 1.2);
                    dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(1, 1, 18, 18), 3, 3);

                    var textBrush = new SolidColorBrush(Color.FromRgb(220, 220, 170));
                    var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    var formattedText = new FormattedText("⇄", System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, font, 11, textBrush, 1.25);
                    dc.DrawText(formattedText, new Point(4, 2));
                }
                var rtb = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                _convIcon = rtb;
                return _convIcon;
            }
            catch
            {
                return null;
            }
        }
    }
}
