using System;
using System.IO;
using System.Threading;
using System.Windows;
using AeroSpot.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class MainWindowPositioningTests
    {
        [TestMethod]
        public void CenteringMath_WithVariousDpiScales_CalculatesSymmetricMargins()
        {
            // Test 100%, 125%, 150%, 175%, 200% scaling on common resolutions
            var testCases = new[]
            {
                new { Width = 1920, Height = 1080, Scale = 1.0 },
                new { Width = 1920, Height = 1200, Scale = 1.25 },
                new { Width = 2560, Height = 1600, Scale = 1.5 },   // 16" laptop (Gabriele's config)
                new { Width = 2560, Height = 1440, Scale = 1.25 },
                new { Width = 3840, Height = 2160, Scale = 1.75 },
                new { Width = 3840, Height = 2160, Scale = 2.0 }
            };

            const double windowWidthDips = 680.0;

            foreach (var tc in testCases)
            {
                double workWidthDips = tc.Width / tc.Scale;
                double leftDips = (workWidthDips - windowWidthDips) / 2.0;

                double leftPixels = leftDips * tc.Scale;
                double rightEdgePixels = leftPixels + (windowWidthDips * tc.Scale);
                double rightMarginPixels = tc.Width - rightEdgePixels;

                // Left margin and right margin must be equal within 0.001 pixel
                Assert.AreEqual(leftPixels, rightMarginPixels, 0.001,
                    $"Failed for {tc.Width}x{tc.Height} @ {tc.Scale * 100}% DPI. Left: {leftPixels}, Right: {rightMarginPixels}");
            }
        }

        [TestMethod]
        public void B1_ShadowBenchmark_MeasuresVariantsPerformance()
        {
            var t = new Thread(() =>
            {
                var win = new MainWindow();
                win.Opacity = 0;
                win.Show();
                win.UpdateLayout();

                var variants = new[]
                {
                    new { Name = "Originale (Dura)", Blur = 28.0, Depth = 4.0, Opacity = 0.75 },
                    new { Name = "Variante 1 (Lieve)", Blur = 20.0, Depth = 2.0, Opacity = 0.30 },
                    new { Name = "Variante 2 (Media/Fluent)", Blur = 30.0, Depth = 3.0, Opacity = 0.42 },
                    new { Name = "Variante 3 (Morbida/macOS)", Blur = 40.0, Depth = 3.0, Opacity = 0.35 }
                };

                var border = (System.Windows.Controls.Border)win.Content;

                foreach (var v in variants)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 20; i++)
                    {
                        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            BlurRadius = v.Blur,
                            ShadowDepth = v.Depth,
                            Direction = 270,
                            Color = System.Windows.Media.Colors.Black,
                            Opacity = v.Opacity
                        };
                        win.UpdateLayout();
                    }
                    sw.Stop();
                    double avgMs = sw.ElapsedMilliseconds / 20.0;
                    Console.WriteLine($"Shadow {v.Name} (Blur={v.Blur}, Depth={v.Depth}, Opacity={v.Opacity:F2}): {avgMs:F2} ms per layout/render");
                }

                win.Close();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        public void B1_GenerateShadowScreenshots_SavesVariantsForUserSelection()
        {
            var t = new Thread(() =>
            {
                string outputDir = @"E:\Antigravity\AeroSpot\Correzioni_Schermate";
                Directory.CreateDirectory(outputDir);

                var variants = new[]
                {
                    new { Id = "Originale_Dura", Name = "Originale (Dura)", Blur = 28.0, Depth = 4.0, Opacity = 0.75, Margin = 14 },
                    new { Id = "Variante_1_Lieve", Name = "Variante 1 (Lieve)", Blur = 20.0, Depth = 2.0, Opacity = 0.30, Margin = 14 },
                    new { Id = "Variante_2_Media", Name = "Variante 2 (Media - Fluent)", Blur = 30.0, Depth = 3.0, Opacity = 0.42, Margin = 16 },
                    new { Id = "Variante_3_Morbida", Name = "Variante 3 (Morbida - macOS)", Blur = 40.0, Depth = 3.0, Opacity = 0.35, Margin = 20 }
                };

                foreach (var v in variants)
                {
                    foreach (bool isDark in new[] { true, false })
                    {
                        var win = new MainWindow();
                        win.ViewModel.SearchText = "calcolatrice";
                        win.SearchBox.Text = "calcolatrice";

                        var outerBorder = (System.Windows.Controls.Border)win.Content;
                        outerBorder.Margin = new Thickness(v.Margin);
                        outerBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            BlurRadius = v.Blur,
                            ShadowDepth = v.Depth,
                            Direction = 270,
                            Color = System.Windows.Media.Colors.Black,
                            Opacity = v.Opacity
                        };

                        double w = 740;
                        double h = 480;

                        var container = new System.Windows.Controls.Grid
                        {
                            Width = w,
                            Height = h,
                            Background = isDark 
                                ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#18191C")) 
                                : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E2E8F0"))
                        };

                        win.Content = null;
                        outerBorder.HorizontalAlignment = HorizontalAlignment.Center;
                        outerBorder.VerticalAlignment = VerticalAlignment.Center;
                        container.Children.Add(outerBorder);

                        container.Measure(new Size(w, h));
                        container.Arrange(new Rect(0, 0, w, h));
                        container.UpdateLayout();

                        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w, (int)h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        rtb.Render(container);

                        string bgName = isDark ? "dark" : "light";
                        string outFile = Path.Combine(outputDir, $"B1_Ombra_{v.Id}_{bgName}.png");

                        using (var fs = new FileStream(outFile, FileMode.Create))
                        {
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                            enc.Save(fs);
                        }

                        Assert.IsTrue(File.Exists(outFile), "Lo screenshot della variante deve essere generato: " + outFile);
                    }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(15));
        }
    }
}
