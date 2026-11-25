using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Native;

namespace AeroSpot.Worker.Services
{
    public static class IconService
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new ConcurrentDictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        private sealed record StaIconRequest(string ParsingName, int Size, TaskCompletionSource<ImageSource?> Tcs);
        private static readonly BlockingCollection<StaIconRequest> _staQueue = new BlockingCollection<StaIconRequest>();
        private static readonly object _staThreadLock = new object();
        private static Thread? _staWorkerThread;
        private static readonly object _vectorIconLock = new object();

        public static void TrimCache()
        {
            _cache.Clear();
        }

        private static void EnsureStaWorkerStarted()
        {
            if (_staWorkerThread != null) return;
            lock (_staThreadLock)
            {
                if (_staWorkerThread != null) return;
                var t = new Thread(StaWorkerLoop)
                {
                    IsBackground = true,
                    Name = "AeroSpot_IconService_STA"
                };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                _staWorkerThread = t;
            }
        }

        private static void StaWorkerLoop()
        {
            foreach (var req in _staQueue.GetConsumingEnumerable())
            {
                try
                {
                    req.Tcs.TrySetResult(LoadShellItemIconInternal(req.ParsingName, req.Size));
                }
                catch
                {
                    req.Tcs.TrySetResult(null);
                }
            }
        }

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
            else if (path.StartsWith("aerospot:settings", StringComparison.OrdinalIgnoreCase))
            {
                return GetAeroSpotSettingsIcon();
            }
            else if (category is "Impostazioni" or "Settings" || path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            {
                return GetSettingsIcon();
            }
            else if (category is "Ricerca" or "Search")
            {
                return GetSearchIcon();
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
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                return LoadShellItemIconInternal(parsingName, size);
            }

            EnsureStaWorkerStarted();
            var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _staQueue.Add(new StaIconRequest(parsingName, size, tcs));
            return tcs.Task.Wait(1200) ? tcs.Task.Result : null;
        }

        private static ImageSource? LoadShellItemIconInternal(string parsingName, int size = 32)
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

        private static ImageSource? _calcIcon;
        private static ImageSource? _convIcon;

        public static ImageSource? GetCalculatorIcon()
        {
            if (_calcIcon != null) return _calcIcon;
            lock (_vectorIconLock)
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
        }

        public static ImageSource? GetConversionIcon()
        {
            if (_convIcon != null) return _convIcon;
            lock (_vectorIconLock)
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

        private static ImageSource? _settingsIcon;
        private static ImageSource? _aeroSpotSettingsIcon;

        public static ImageSource? GetAeroSpotSettingsIcon()
        {
            if (_aeroSpotSettingsIcon != null) return _aeroSpotSettingsIcon;
            lock (_vectorIconLock)
            {
                if (_aeroSpotSettingsIcon != null) return _aeroSpotSettingsIcon;
                try
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        var bgBrush = new SolidColorBrush(Color.FromRgb(12, 74, 110)); // #0C4A6E
                        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1.2); // #38BDF8
                        dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(1, 1, 18, 18), 3, 3);

                        var gearPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1.4);
                        dc.DrawEllipse(null, gearPen, new Point(10, 10), 3.5, 3.5);
                        for (int i = 0; i < 4; i++)
                        {
                            double angle = i * Math.PI / 2.0;
                            dc.DrawLine(gearPen, new Point(10 + Math.Cos(angle) * 4.2, 10 + Math.Sin(angle) * 4.2),
                                                 new Point(10 + Math.Cos(angle) * 6.2, 10 + Math.Sin(angle) * 6.2));
                            double diag = angle + Math.PI / 4.0;
                            dc.DrawLine(gearPen, new Point(10 + Math.Cos(diag) * 4.2, 10 + Math.Sin(diag) * 4.2),
                                                 new Point(10 + Math.Cos(diag) * 6.2, 10 + Math.Sin(diag) * 6.2));
                        }
                    }
                    var rtb = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.Freeze();
                    _aeroSpotSettingsIcon = rtb;
                    return _aeroSpotSettingsIcon;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static ImageSource? GetSettingsIcon()
        {
            if (_settingsIcon != null) return _settingsIcon;
            lock (_vectorIconLock)
            {
                if (_settingsIcon != null) return _settingsIcon;
                try
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        var bgBrush = new SolidColorBrush(Color.FromRgb(45, 55, 72));
                        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(167, 139, 250)), 1.2);
                        dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(1, 1, 18, 18), 3, 3);

                        var gearPen = new Pen(new SolidColorBrush(Color.FromRgb(167, 139, 250)), 1.4);
                        dc.DrawEllipse(null, gearPen, new Point(10, 10), 3.5, 3.5);
                        for (int i = 0; i < 4; i++)
                        {
                            double angle = i * Math.PI / 2.0;
                            dc.DrawLine(gearPen, new Point(10 + Math.Cos(angle) * 4.2, 10 + Math.Sin(angle) * 4.2),
                                                 new Point(10 + Math.Cos(angle) * 6.2, 10 + Math.Sin(angle) * 6.2));
                            double diag = angle + Math.PI / 4.0;
                            dc.DrawLine(gearPen, new Point(10 + Math.Cos(diag) * 4.2, 10 + Math.Sin(diag) * 4.2),
                                                 new Point(10 + Math.Cos(diag) * 6.2, 10 + Math.Sin(diag) * 6.2));
                        }
                    }
                    var rtb = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.Freeze();
                    _settingsIcon = rtb;
                    return _settingsIcon;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static ImageSource? _searchIcon;

        public static ImageSource? GetSearchIcon()
        {
            if (_searchIcon != null) return _searchIcon;
            lock (_vectorIconLock)
            {
                if (_searchIcon != null) return _searchIcon;
                try
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        var bgBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.2);
                        dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(1, 1, 18, 18), 3, 3);

                        var lensPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.4);
                        dc.DrawEllipse(null, lensPen, new Point(8.5, 8.5), 3.5, 3.5);
                        dc.DrawLine(lensPen, new Point(11, 11), new Point(14.5, 14.5));
                    }
                    var rtb = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.Freeze();
                    _searchIcon = rtb;
                    return _searchIcon;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
