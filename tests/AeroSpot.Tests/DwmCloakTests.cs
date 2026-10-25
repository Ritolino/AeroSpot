using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class DwmCloakTests
    {
        private const int DWMWA_CLOAK = 13;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [TestMethod]
        public void DwmCloak_CanCloakAndUncloakWpfWindow()
        {
            var t = new Thread(() =>
            {
                if (Application.Current == null) new Application();

                var win = new Window
                {
                    Width = 400,
                    Height = 300,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    ShowInTaskbar = false
                };

                win.Show();
                var hwnd = new WindowInteropHelper(win).Handle;
                Assert.AreNotEqual(IntPtr.Zero, hwnd);

                // Cloak window
                int cloak = 1;
                int hrCloak = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));
                Console.WriteLine($"[DWM] Cloak result: 0x{hrCloak:X8}");
                Assert.AreEqual(0, hrCloak, $"DwmSetWindowAttribute Cloak fallito con HRESULT 0x{hrCloak:X8}");

                // Uncloak window
                int uncloak = 0;
                int hrUncloak = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref uncloak, sizeof(int));
                Console.WriteLine($"[DWM] Uncloak result: 0x{hrUncloak:X8}");
                Assert.AreEqual(0, hrUncloak, $"DwmSetWindowAttribute Uncloak fallito con HRESULT 0x{hrUncloak:X8}");

                win.Close();
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        public void DwmCloak_CanSwapImagesWhileCloaked_WithoutException()
        {
            var t = new Thread(() =>
            {
                if (Application.Current == null) new Application();

                var win = new Window
                {
                    Width = 400,
                    Height = 300,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    ShowInTaskbar = false
                };

                var imgControl = new System.Windows.Controls.Image();
                win.Content = imgControl;

                win.Show();
                var hwnd = new WindowInteropHelper(win).Handle;

                // 1. Cloak
                int cloak = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));

                // 2. Set image 1
                string? img1 = Directory.GetFiles(TestEnvironment.RepoRoot, "*.png", SearchOption.AllDirectories).FirstOrDefault();
                if (img1 != null && System.IO.File.Exists(img1))
                {
                    var bmp1 = new System.Windows.Media.Imaging.BitmapImage();
                    bmp1.BeginInit();
                    bmp1.UriSource = new Uri(img1);
                    bmp1.DecodePixelWidth = 800;
                    bmp1.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp1.EndInit();
                    bmp1.Freeze();
                    imgControl.Source = bmp1;
                }

                win.UpdateLayout();

                // 3. Uncloak
                int uncloak = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref uncloak, sizeof(int));

                // 4. Cloak again
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));

                // 5. Swap to image 2 while cloaked
                string? img2 = Directory.GetFiles(TestEnvironment.RepoRoot, "*.jpeg", SearchOption.AllDirectories).FirstOrDefault();
                if (img2 != null && System.IO.File.Exists(img2))
                {
                    var bmp2 = new System.Windows.Media.Imaging.BitmapImage();
                    bmp2.BeginInit();
                    bmp2.UriSource = new Uri(img2);
                    bmp2.DecodePixelWidth = 800;
                    bmp2.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp2.EndInit();
                    bmp2.Freeze();
                    imgControl.Source = bmp2;
                }

                win.UpdateLayout();

                // 6. Uncloak
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref uncloak, sizeof(int));

                win.Close();
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(10));
        }
    }
}
