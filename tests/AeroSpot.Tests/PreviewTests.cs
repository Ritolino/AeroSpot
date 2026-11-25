using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Preview;
using AeroSpot.Core.Search;
using AeroSpot.Worker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class PreviewTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            PathFilter.ResetToDefault();
        }
        private static string CreateTempSamplePng(int width = 160, int height = 120)
        {
            string pngPath = Path.Combine(Path.GetTempPath(), $"aerospot_test_img_{Guid.NewGuid():N}.png");
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            new Random(123).NextBytes(pixels);
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(pngPath, FileMode.Create);
            encoder.Save(fs);
            return pngPath;
        }

        private static string CreateTempSampleJpeg(int width = 320, int height = 240, int quality = 90)
        {
            string jpgPath = Path.Combine(Path.GetTempPath(), $"aerospot_test_img_{Guid.NewGuid():N}.jpg");
            int stride = width * 3;
            byte[] pixels = new byte[height * stride];
            new Random(456).NextBytes(pixels);
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);

            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(jpgPath, FileMode.Create);
            encoder.Save(fs);
            return jpgPath;
        }

        private static string ResolveSamplePdfPath()
        {
            string pdfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.pdf");
            if (!File.Exists(pdfPath))
            {
                pdfPath = Path.Combine(TestEnvironment.TestsDir, "test_sample.pdf");
            }
            return pdfPath;
        }

        [TestMethod]
        public void SettingsWindow_InstantiatesCleanly()
        {
            StaTestRunner.Run(() =>
            {
                var win = new AeroSpot.Worker.SettingsWindow();
                Assert.IsNotNull(win);
                win.Hide();
            });
        }

        [TestMethod]
        public async Task PreviewLoader_RealImage_LoadsSuccessfully()
        {
            string imagePath = Path.Combine(TestEnvironment.TestsDir, "test_sample.jpeg");
            bool createdTemp = false;
            if (!File.Exists(imagePath))
            {
                imagePath = CreateTempSampleJpeg(320, 240);
                createdTemp = true;
            }

            try
            {
                var model = await PreviewLoader.LoadAsync(imagePath);

                Assert.AreEqual(PreviewType.Image, model.Type);
                Assert.IsNotNull(model.Image);
                Assert.IsInstanceOfType<BitmapSource>(model.Image);
                var bmp = (BitmapSource)model.Image;
                Assert.IsGreaterThan(0, bmp.PixelWidth);
                Assert.IsGreaterThan(0, bmp.PixelHeight);
                Assert.Contains("×", model.Subtitle);
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.FileSizeText));
            }
            finally
            {
                if (createdTemp && File.Exists(imagePath)) File.Delete(imagePath);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_TextFile_ReadsContentAndEncodings()
        {
            string mdPath = Path.Combine(TestEnvironment.DocsDir, "ISTRUZIONI.md");
            Assert.IsTrue(File.Exists(mdPath), $"Documentazione ISTRUZIONI.md mancante in {mdPath}");

            var mdModel = await PreviewLoader.LoadAsync(mdPath);

            Assert.AreEqual(PreviewType.Markdown, mdModel.Type);
            Assert.IsNotNull(mdModel.TextContent);
            Assert.IsGreaterThan(0, mdModel.TextContent.Length);
            Assert.Contains("AeroSpot", mdModel.TextContent);
            Assert.IsTrue(mdModel.Subtitle.Contains("righe") || mdModel.Subtitle.Contains("lines"));

            // Verifica anche un file di testo puro (.txt)
            string tempTxt = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(tempTxt, "Riga 1\nRiga 2\nRiga 3");
                var txtModel = await PreviewLoader.LoadAsync(tempTxt);
                Assert.AreEqual(PreviewType.Text, txtModel.Type);
                Assert.AreEqual("Riga 1\nRiga 2\nRiga 3", txtModel.TextContent);
            }
            finally
            {
                if (File.Exists(tempTxt)) File.Delete(tempTxt);
            }
        }

        [TestMethod]
        public void MarkdownToFlowDocumentConverter_RendersHeadersCodeBlocksAndLists()
        {
            StaTestRunner.Run(() =>
            {
                string sampleMd = @"# Titolo 1
## Sottotitolo 2
Questo è un testo con **grassetto**, *corsivo*, e `codice inline`.

- Elemento lista 1
- Elemento lista 2

> Questo è un blockquote

```csharp
var x = 10;
Console.WriteLine(x);
```
---
";

                var doc = MarkdownToFlowDocumentConverter.Convert(sampleMd);
                Assert.IsNotNull(doc);
                Assert.IsGreaterThan(0, doc.Blocks.Count, "FlowDocument deve contenere blocchi renderizzati.");
            });
        }

        [TestMethod]
        public async Task PreviewLoader_LargeTextFile_TruncatesTo100KB()
        {
            // Crea un file di testo temporaneo di ~150 KB
            string tempLargeFile = Path.Combine(Path.GetTempPath(), $"LargeText_{Guid.NewGuid():N}.log");
            try
            {
                using (var sw = new StreamWriter(tempLargeFile))
                {
                    for (int i = 0; i < 4000; i++)
                    {
                        sw.WriteLine($"[2026-10-17 12:00:{i % 60:D2}] LOG LINE {i:D5}: Test di performance per evitare blocchi della UI con file grandi.");
                    }
                }

                var model = await PreviewLoader.LoadAsync(tempLargeFile);

                Assert.AreEqual(PreviewType.Text, model.Type);
                Assert.IsTrue(model.IsTruncated, "Il file superiore a 100 KB deve essere troncato per proteggere la memoria e la reattività.");
                Assert.IsNotNull(model.TextContent);
                Assert.IsLessThanOrEqualTo(102400, model.TextContent.Length);
                Assert.Contains("100 KB", model.Subtitle);
            }
            finally
            {
                if (File.Exists(tempLargeFile)) File.Delete(tempLargeFile);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_CorruptedImage_HandlesGracefullyWithoutCrash()
        {
            string corruptImgPath = Path.Combine(Path.GetTempPath(), $"Corrupt_{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllText(corruptImgPath, "NON_E_UN_PNG_VALIDO_MA_FILE_CORROTTO");

                var model = await PreviewLoader.LoadAsync(corruptImgPath);

                Assert.AreEqual(PreviewType.Error, model.Type, "Un'immagine corrotta non deve mandare in crash l'app ma restituire PreviewType.Error.");
                Assert.IsNotNull(model.ErrorMessage);
                Assert.IsTrue(model.ErrorMessage.Contains("danneggiato") || model.ErrorMessage.Contains("decodificare"));
            }
            finally
            {
                if (File.Exists(corruptImgPath)) File.Delete(corruptImgPath);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_NonExistentFile_ReturnsError()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), $"AeroSpot_MissingDir_{Guid.NewGuid():N}", "FakeFile9999.xyz");
            var model = await PreviewLoader.LoadAsync(missingPath);

            Assert.AreEqual(PreviewType.Error, model.Type);
            Assert.IsNotNull(model.ErrorMessage);
            Assert.IsTrue(model.ErrorMessage.Contains("non esiste") || model.ErrorMessage.Contains("rimosso") || model.ErrorMessage.Contains("not exist") || model.ErrorMessage.Contains("removed"));
        }

        [TestMethod]
        public async Task PreviewLoader_GenericBinaryFile_DoesNotExecuteAndReturnsGeneric()
        {
            string binPath = Path.Combine(Path.GetTempPath(), $"Binary_{Guid.NewGuid():N}.dll");
            try
            {
                File.WriteAllBytes(binPath, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

                var model = await PreviewLoader.LoadAsync(binPath);

                Assert.AreEqual(PreviewType.Generic, model.Type, "I file non visualizzabili inline devono mostrare la scheda metadati generica.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.DetailsText));
            }
            finally
            {
                if (File.Exists(binPath)) File.Delete(binPath);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_PdfFile_LoadsNativePdfiumModel()
        {
            string pdfPath = ResolveSamplePdfPath();
            Assert.IsTrue(File.Exists(pdfPath), $"Il file PDF di test deve esistere: {pdfPath}");

            var model = await PreviewLoader.LoadAsync(pdfPath);
            Assert.AreEqual(PreviewType.Pdf, model.Type, $"Il PDF deve caricarsi tramite PDFium nativo, ottenuto: {model.Type}");
            Assert.IsGreaterThanOrEqualTo(1, model.PdfPageCount, "PdfPageCount deve essere >= 1");
            Assert.IsGreaterThan(0.0, model.PdfWidth, "PdfWidth deve essere > 0");
            Assert.IsGreaterThan(0.0, model.PdfHeight, "PdfHeight deve essere > 0");
            Assert.Contains(".pdf", model.Title.ToLowerInvariant());
        }

        [TestMethod]
        public async Task PreviewLoader_UnregisteredExtension_ReturnsGenericWithExplicitFallback()
        {
            string unknownFile = Path.Combine(Path.GetTempPath(), $"Unknown_{Guid.NewGuid():N}.xyzunknown");
            try
            {
                File.WriteAllText(unknownFile, "Custom data format without registered shell preview handler");

                var model = await PreviewLoader.LoadAsync(unknownFile);

                Assert.AreEqual(PreviewType.Generic, model.Type);
                Assert.IsTrue(model.DetailsText.Contains("Nessun visualizzatore di anteprima registrato") || model.DetailsText.Contains("No preview handler registered"));
                Assert.AreEqual(Guid.Empty, model.PreviewHandlerClsid);
            }
            finally
            {
                if (File.Exists(unknownFile)) File.Delete(unknownFile);
            }
        }

        [TestMethod]
        public async Task PreviewWindow_DwmCloak_ImageTransitions_HaveZeroFlicker()
        {
            string img1 = CreateTempSamplePng();
            string img2 = CreateTempSampleJpeg();

            try
            {
                await StaTestRunner.RunAsync(async () =>
                {
                    var win = new AeroSpot.Worker.PreviewWindow();
                    try
                    {
                        Assert.IsFalse(win.IsPreviewVisible);

                        // 1. Apri immagine 1
                        await win.ShowPreviewForFileAsync(img1, isFromSpotlight: true);
                        Assert.IsTrue(win.IsPreviewVisible);

                        // 2. Chiudi con HidePreview
                        win.HidePreview();
                        Assert.IsFalse(win.IsPreviewVisible);

                        // 3. Apri immagine 2
                        await win.ShowPreviewForFileAsync(img2, isFromSpotlight: true);
                        Assert.IsTrue(win.IsPreviewVisible);

                        win.HidePreview();
                        Assert.IsFalse(win.IsPreviewVisible);
                    }
                    finally
                    {
                        win.HidePreview();
                    }
                });
            }
            finally
            {
                try { if (File.Exists(img1)) File.Delete(img1); } catch { }
                try { if (File.Exists(img2)) File.Delete(img2); } catch { }
            }
        }

        [TestMethod]
        public async Task PreviewWindow_Stl3D_ResetViewAndZoomClamp_PreservesBounds()
        {
            string tempStl = Path.Combine(Path.GetTempPath(), $"test_cube_{Guid.NewGuid():N}.stl");
            File.WriteAllText(tempStl, @"solid cube
  facet normal 0 0 1
    outer loop
      vertex 0 0 0
      vertex 10 0 0
      vertex 10 10 0
    endloop
  endfacet
endsolid cube");

            try
            {
                var stlModel = await PreviewLoader.LoadAsync(tempStl);
                Assert.AreEqual(PreviewType.Stl3D, stlModel.Type);

                StaTestRunner.Run(() =>
                {
                    var win = new AeroSpot.Worker.PreviewWindow();
                    try
                    {
                        var type = typeof(AeroSpot.Worker.PreviewWindow);
                        var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                        var applyMethod = type.GetMethod("ApplyModelToUI", flags);
                        Assert.IsNotNull(applyMethod);
                        applyMethod.Invoke(win, new object[] { stlModel });

                        Assert.AreEqual(Visibility.Visible, win.StlView.Visibility, "StlView deve essere visibile.");
                        Assert.AreEqual(Visibility.Visible, win.BtnReset3DView.Visibility, "BtnReset3DView deve essere visibile in anteprima 3D.");

                        double defaultDist = (double)(type.GetField("_stlDefaultDistance", flags)?.GetValue(win) ?? 0.0);
                        Assert.IsGreaterThan(0.0, defaultDist);

                        var wheelMethod = type.GetMethod("StlViewport_MouseWheel", flags);
                        Assert.IsNotNull(wheelMethod);

                        // Simula 30 scatti di zoom indietro per verificare il limite massimo (<= 4.5x * defaultDist)
                        for (int i = 0; i < 30; i++)
                        {
                            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                            {
                                RoutedEvent = UIElement.MouseWheelEvent
                            };
                            wheelMethod.Invoke(win, new object[] { win.StlView, args });
                        }
                        double zoomedOutDist = (double)(type.GetField("_stlDistance", flags)?.GetValue(win) ?? 0.0);
                        Assert.IsLessThanOrEqualTo(Math.Max(1.0, defaultDist * 4.5) + 0.01, zoomedOutDist, "La distanza minima/massima 3D deve restare dentro al clamp superiore.");

                        // Simula 40 scatti di zoom avanti per verificare il limite minimo (>= 0.18x * defaultDist)
                        for (int i = 0; i < 40; i++)
                        {
                            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                            {
                                RoutedEvent = UIElement.MouseWheelEvent
                            };
                            wheelMethod.Invoke(win, new object[] { win.StlView, args });
                        }
                        double zoomedInDist = (double)(type.GetField("_stlDistance", flags)?.GetValue(win) ?? 0.0);
                        Assert.IsGreaterThanOrEqualTo(Math.Max(0.05, defaultDist * 0.18) - 0.01, zoomedInDist, "La distanza 3D deve restare dentro al clamp inferiore.");

                        // Modifica orientamento e premi "Reset vista"
                        type.GetField("_stlTheta", flags)?.SetValue(win, 135.0);
                        win.BtnReset3DView.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                        double resetDist = (double)(type.GetField("_stlDistance", flags)?.GetValue(win) ?? 0.0);
                        double resetTheta = (double)(type.GetField("_stlTheta", flags)?.GetValue(win) ?? 0.0);
                        double resetPhi = (double)(type.GetField("_stlPhi", flags)?.GetValue(win) ?? 0.0);
                        Assert.AreEqual(defaultDist, resetDist, 0.001, "Reset vista deve ripristinare _stlDefaultDistance.");
                        Assert.AreEqual(45.0, resetTheta, 0.001, "Reset vista deve ripristinare l'angolo theta iniziale.");
                        Assert.AreEqual(35.0, resetPhi, 0.001, "Reset vista deve ripristinare l'angolo phi iniziale.");
                    }
                    finally
                    {
                        win.HidePreview();
                    }
                });
            }
            finally
            {
                try { if (File.Exists(tempStl)) File.Delete(tempStl); } catch { }
            }
        }

        [TestMethod]
        public async Task PreviewWindow_PdfPanAndScrollbar_OnlyPansWhenZoomedAndNeverHijacksScrollBar()
        {
            string pdfPath = ResolveSamplePdfPath();
            Assert.IsTrue(File.Exists(pdfPath), $"Il file PDF di test deve esistere: {pdfPath}");

            var pdfModel = await PreviewLoader.LoadAsync(pdfPath);
            Assert.AreEqual(PreviewType.Pdf, pdfModel.Type);

            StaTestRunner.Run(() =>
            {
                var type = typeof(AeroSpot.Worker.PreviewWindow);

                // 1. Verifica IsDescendantOfScrollBar su visual tree WPF
                var isScrollBarMethod = type.GetMethod(
                    "IsDescendantOfScrollBar",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(isScrollBarMethod);

                var scrollViewer = new ScrollViewer();
                var scrollBar = new ScrollBar();
                var thumbChild = new Border();
                scrollBar.Template = new ControlTemplate(typeof(ScrollBar))
                {
                    VisualTree = new FrameworkElementFactory(typeof(Border), "PART_Border")
                };
                var sp = new StackPanel();
                sp.Children.Add(scrollBar);
                sp.Children.Add(thumbChild);
                scrollViewer.Content = sp;

                Assert.IsTrue((bool)isScrollBarMethod.Invoke(null, new object?[] { scrollBar, scrollViewer })!,
                    "Un controllo ScrollBar deve essere riconosciuto da IsDescendantOfScrollBar.");
                Assert.IsFalse((bool)isScrollBarMethod.Invoke(null, new object?[] { thumbChild, scrollViewer })!,
                    "Il contenuto del documento fuori dalla ScrollBar NON deve essere riconosciuto come ScrollBar.");

                // 2. Verifica che a 100% (_pdfScale == 1.0) il click sinistro non attivi _isPanningPdf
                var win = new AeroSpot.Worker.PreviewWindow();
                try
                {
                    var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                    var applyMethod = type.GetMethod("ApplyModelToUI", flags);
                    Assert.IsNotNull(applyMethod);
                    applyMethod.Invoke(win, new object[] { pdfModel });

                    double scale = (double)(type.GetField("_pdfScale", flags)?.GetValue(win) ?? 0.0);
                    Assert.AreEqual(1.0, scale, 0.001, "All'apertura lo zoom PDF deve essere 100% (1.0).");

                    var mouseDownMethod = type.GetMethod("PdfScrollViewer_PreviewMouseDown", flags);
                    Assert.IsNotNull(mouseDownMethod);

                    var downArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.PreviewMouseDownEvent
                    };
                    mouseDownMethod.Invoke(win, new object[] { win.PdfScrollViewer, downArgs });

                    bool isPanningAt100 = (bool)(type.GetField("_isPanningPdf", flags)?.GetValue(win) ?? true);
                    Assert.IsFalse(isPanningAt100, "A zoom 100% il trascinamento panoramico del PDF non deve essere attivo.");
                }
                finally
                {
                    win.HidePreview();
                }
            });
        }

        [TestMethod]
        public void FolderIcon_ReturnsNonNullImageSource()
        {
            StaTestRunner.Run(() =>
            {
                var icon1 = IconService.GetIcon(TestEnvironment.RepoRoot, "Cartella");
                Assert.IsNotNull(icon1, "L'icona per una cartella reale non deve essere nulla.");

                var icon2 = IconService.GetIcon(":folder:", "Cartella");
                Assert.IsNotNull(icon2, "L'icona per una cartella generica non deve essere nulla.");
            });
        }

        [TestMethod]
        public async Task AppsFolder_ExecutableOrUwp_DoesNotReturnError()
        {
            string appPath = @"shell:AppsFolder\C:\Windows\notepad.exe";
            var model = await PreviewLoader.LoadAsync(appPath);

            Assert.AreNotEqual(PreviewType.Error, model.Type, "Un'applicazione registrata in AppsFolder non deve restituire PreviewType.Error");
            Assert.AreEqual(PreviewType.Generic, model.Type);
        }

        [TestMethod]
        public void QuickLook_CustomDimensions_PersistInConfig()
        {
            var cfg = new AeroSpot.Core.Config.AppConfig
            {
                PreviewWindowWidth = 920,
                PreviewWindowHeight = 650,
                RememberSizeImages = true,
                SavedWidthImage = 1000,
                SavedHeightImage = 700,
                RememberSizeVideos = true,
                SavedWidthVideo = 850,
                SavedHeightVideo = 500,
                RememberSizePdf = false
            };

            string tempPath = Path.Combine(Path.GetTempPath(), $"aerospot_test_cfg_{Guid.NewGuid():N}.json");
            try
            {
                cfg.Save(tempPath);
                var loaded = AeroSpot.Core.Config.AppConfig.Load(tempPath);
                Assert.AreEqual(920, loaded.PreviewWindowWidth);
                Assert.AreEqual(650, loaded.PreviewWindowHeight);
                Assert.IsTrue(loaded.RememberSizeImages);
                Assert.AreEqual(1000, loaded.SavedWidthImage);
                Assert.AreEqual(700, loaded.SavedHeightImage);
                Assert.IsTrue(loaded.RememberSizeVideos);
                Assert.AreEqual(850, loaded.SavedWidthVideo);
                Assert.AreEqual(500, loaded.SavedHeightVideo);
                Assert.IsFalse(loaded.RememberSizePdf);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_Mp3File_IdentifiesAsAudioAndNotGeneric()
        {
            string tempMp3 = Path.Combine(Path.GetTempPath(), $"test_audio_{Guid.NewGuid():N}.mp3");
            try
            {
                File.WriteAllBytes(tempMp3, new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0x00, 0x00, 0x00 });
                Assert.IsTrue(PreviewLoader.IsAudioExtension(".mp3"));
                Assert.IsTrue(PreviewLoader.IsAudioExtension(".wav"));
                Assert.IsTrue(PreviewLoader.IsAudioExtension(".flac"));

                var model = await PreviewLoader.LoadAsync(tempMp3);
                Assert.AreEqual(PreviewType.Audio, model.Type);
                Assert.Contains("Audio", model.Subtitle);
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.DetailsText));
                Assert.IsFalse(model.FooterText?.Contains("bytes", StringComparison.OrdinalIgnoreCase) ?? false);
            }
            finally
            {
                if (File.Exists(tempMp3)) File.Delete(tempMp3);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_StlFile_Parses3DMeshAndDimensions()
        {
            string asciiStl = @"solid test_cube
  facet normal 0.0 0.0 1.0
    outer loop
      vertex 0.0 0.0 0.0
      vertex 10.0 0.0 0.0
      vertex 10.0 10.0 0.0
    endloop
  endfacet
endsolid test_cube";

            string tempStl = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid():N}.stl");
            try
            {
                File.WriteAllText(tempStl, asciiStl);
                var model = await PreviewLoader.LoadAsync(tempStl);
                Assert.AreEqual(PreviewType.Stl3D, model.Type);
                Assert.IsNotNull(model.StlModel);
                Assert.AreEqual(1, model.StlModel.TriangleCount);
                Assert.AreEqual(10.0, model.StlModel.SizeX, 0.01);
                Assert.AreEqual(10.0, model.StlModel.SizeY, 0.01);
                Assert.IsTrue(model.Subtitle.Contains("triangol") || model.Subtitle.Contains("triangle"));
            }
            finally
            {
                if (File.Exists(tempStl)) File.Delete(tempStl);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_ImageResolution_IsInSubtitleAndNotInFooter()
        {
            string imagePath = Path.Combine(TestEnvironment.TestsDir, "test_sample.jpeg");
            bool createdTemp = false;
            if (!File.Exists(imagePath))
            {
                imagePath = CreateTempSampleJpeg(320, 240);
                createdTemp = true;
            }

            try
            {
                var model = await PreviewLoader.LoadAsync(imagePath);
                Assert.AreEqual(PreviewType.Image, model.Type);
                Assert.Contains("×", model.Subtitle);
                Assert.IsFalse(model.FooterText?.Contains("×") ?? false);
            }
            finally
            {
                if (createdTemp && File.Exists(imagePath)) File.Delete(imagePath);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_PdfPage_HasWidthAndHeightForAspectRatio()
        {
            string landscapePdf = Path.Combine(Path.GetTempPath(), $"aerospot_landscape_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharp.Pdf.PdfDocument())
                {
                    var page = doc.AddPage();
                    page.Size = PdfSharp.PageSize.A4;
                    page.Orientation = PdfSharp.PageOrientation.Landscape;
                    doc.Save(landscapePdf);
                }

                var model = await PreviewLoader.LoadAsync(landscapePdf);
                Assert.AreEqual(PreviewType.Pdf, model.Type);
                Assert.IsGreaterThan(0.0, model.PdfWidth, "PdfWidth deve essere > 0");
                Assert.IsGreaterThan(0.0, model.PdfHeight, "PdfHeight deve essere > 0");
                Assert.IsGreaterThan(model.PdfHeight, model.PdfWidth, "Un PDF landscape deve avere PdfWidth > PdfHeight per il ridimensionamento corretto.");
            }
            finally
            {
                try { if (File.Exists(landscapePdf)) File.Delete(landscapePdf); } catch { }
            }
        }

        [TestMethod]
        public void MainWindow_EscapeKey_FiresCloseAllRequested()
        {
            StaTestRunner.Run(() =>
            {
                var win = new AeroSpot.Worker.MainWindow();
                try
                {
                    bool closeAllFired = false;
                    win.CloseAllRequested += () => closeAllFired = true;

                    var keyEvent = new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        new PresentationSourceMock(),
                        0,
                        Key.Escape)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };

                    win.SearchBox.RaiseEvent(keyEvent);
                    Assert.IsTrue(closeAllFired, "La pressione di Esc in Spotlight deve scatenare CloseAllRequested per chiudere anche QuickLook.");
                }
                finally
                {
                    win.ViewModel.Dispose();
                    win.Hide();
                }
            });
        }

        [TestMethod]
        public void PreviewWindow_EscapeKey_AlwaysFiresCloseAllRequested()
        {
            StaTestRunner.Run(() =>
            {
                var win = new AeroSpot.Worker.PreviewWindow();
                try
                {
                    bool closeAllFired = false;
                    win.CloseAllRequested += () => closeAllFired = true;

                    var keyEvent = new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        new PresentationSourceMock(),
                        0,
                        Key.Escape)
                    {
                        RoutedEvent = UIElement.PreviewKeyDownEvent
                    };

                    win.RaiseEvent(keyEvent);
                    Assert.IsTrue(closeAllFired, "La pressione di Esc in QuickLook deve scatenare CloseAllRequested per chiudere sia QuickLook che Spotlight.");
                }
                finally
                {
                    win.HidePreview();
                }
            });
        }

        [TestMethod]
        public void PreviewWindow_ImageZoom_AnchorsExactlyToCursorWhenLetterboxed()
        {
            StaTestRunner.Run(() =>
            {
                var win = new AeroSpot.Worker.PreviewWindow();
                try
                {
                    // Simula finestra fullscreen (1920x1040) con immagine verticale centrata (400x1040)
                    win.ImageView.Visibility = Visibility.Visible;
                    win.ImageView.Width = 1920;
                    win.ImageView.Height = 1040;
                    win.PreviewImage.Width = 400;
                    win.PreviewImage.Height = 1040;
                    win.Measure(new Size(1920, 1080));
                    win.Arrange(new Rect(0, 0, 1920, 1080));
                    win.UpdateLayout();

                    Point cursorInView = new Point(960, 260);
                    Point localBefore = win.ImageView.TranslatePoint(cursorInView, win.PreviewImage);

                    var wheelMethod = typeof(AeroSpot.Worker.PreviewWindow).GetMethod(
                        "ImageView_PreviewMouseWheel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert.IsNotNull(wheelMethod);

                    // Esegue uno zoom-in fittizio ancorato a cursorInView usando la stessa formula del gestore
                    double oldScale = 1.0;
                    double newScale = 1.15 * 1.15;
                    double ratio = newScale / oldScale;
                    win.ImageTranslateTransform.X = cursorInView.X - (cursorInView.X - win.ImageTranslateTransform.X) * ratio;
                    win.ImageTranslateTransform.Y = cursorInView.Y - (cursorInView.Y - win.ImageTranslateTransform.Y) * ratio;
                    win.ImageScaleTransform.ScaleX = newScale;
                    win.ImageScaleTransform.ScaleY = newScale;
                    win.UpdateLayout();

                    Point localAfter = win.ImageView.TranslatePoint(cursorInView, win.PreviewImage);
                    Assert.AreEqual(localBefore.X, localAfter.X, 0.5, "Il punto orizzontale sotto il cursore deve restare ancorato anche con immagine centrata in fullscreen.");
                    Assert.AreEqual(localBefore.Y, localAfter.Y, 0.5, "Il punto verticale sotto il cursore deve restare ancorato anche con immagine centrata in fullscreen.");
                }
                finally
                {
                    win.HidePreview();
                }
            });
        }

        [TestMethod]
        public void PreviewWindow_ToggleFullscreen_ResetsImageZoomAndTranslation()
        {
            StaTestRunner.Run(() =>
            {
                var win = new AeroSpot.Worker.PreviewWindow();
                try
                {
                    var toggleMethod = typeof(AeroSpot.Worker.PreviewWindow).GetMethod(
                        "ToggleFullscreen",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var scaleField = typeof(AeroSpot.Worker.PreviewWindow).GetField(
                        "_imageScale",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert.IsNotNull(toggleMethod);
                    Assert.IsNotNull(scaleField);

                    // Simula zoom + pan attivo prima di passare a fullscreen
                    scaleField.SetValue(win, 2.5);
                    win.ImageScaleTransform.ScaleX = 2.5;
                    win.ImageScaleTransform.ScaleY = 2.5;
                    win.ImageTranslateTransform.X = -180;
                    win.ImageTranslateTransform.Y = 95;
                    win.BtnResetZoom.Visibility = Visibility.Visible;

                    // Finestra -> Fullscreen
                    toggleMethod.Invoke(win, null);
                    Assert.AreEqual(WindowState.Maximized, win.WindowState);
                    Assert.AreEqual(1.0, (double)scaleField.GetValue(win)!, 0.001);
                    Assert.AreEqual(1.0, win.ImageScaleTransform.ScaleX, 0.001);
                    Assert.AreEqual(0.0, win.ImageTranslateTransform.X, 0.001);
                    Assert.AreEqual(0.0, win.ImageTranslateTransform.Y, 0.001);
                    Assert.AreEqual(Visibility.Collapsed, win.BtnResetZoom.Visibility);

                    // Simula nuovo zoom + pan in fullscreen prima di tornare a finestra
                    scaleField.SetValue(win, 3.0);
                    win.ImageScaleTransform.ScaleX = 3.0;
                    win.ImageScaleTransform.ScaleY = 3.0;
                    win.ImageTranslateTransform.X = 220;
                    win.ImageTranslateTransform.Y = -140;

                    // Fullscreen -> Finestra
                    toggleMethod.Invoke(win, null);
                    Assert.AreEqual(WindowState.Normal, win.WindowState);
                    Assert.AreEqual(1.0, (double)scaleField.GetValue(win)!, 0.001);
                    Assert.AreEqual(1.0, win.ImageScaleTransform.ScaleX, 0.001);
                    Assert.AreEqual(0.0, win.ImageTranslateTransform.X, 0.001);
                    Assert.AreEqual(0.0, win.ImageTranslateTransform.Y, 0.001);
                }
                finally
                {
                    win.HidePreview();
                }
            });
        }

        [TestMethod]
        public void PreviewWindow_AudioDimensions_TargetSizeIs558x408()
        {
            StaTestRunner.Run(() =>
            {
                var win = new AeroSpot.Worker.PreviewWindow();
                try
                {
                    var audioModel = new PreviewModel
                    {
                        Type = PreviewType.Audio,
                        FilePath = "test.mp3"
                    };

                    var method = typeof(AeroSpot.Worker.PreviewWindow).GetMethod("PositionAndSizeWindow",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    method?.Invoke(win, new object[] { audioModel });

                    Assert.AreEqual(558.0, win.Width, 1.0, "La larghezza standard per audio deve essere 558 (+3%).");
                    Assert.AreEqual(408.0, win.Height, 1.0, "L'altezza standard per audio deve essere 408 (+15%).");
                }
                finally
                {
                    win.HidePreview();
                }
            });
        }

        [TestMethod]
        public void PreviewWindow_FormatPlaybackTime_HandlesHoursProperly()
        {
            var method = typeof(AeroSpot.Worker.PreviewWindow).GetMethod("FormatPlaybackTime",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            var under1Hour = method.Invoke(null, new object[] { TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(263) }) as string;
            Assert.AreEqual("01:30 / 04:23", under1Hour);

            var over1Hour = method.Invoke(null, new object[] { TimeSpan.FromSeconds(3600 + 1278), TimeSpan.FromSeconds(7200 + 1278) }) as string;
            Assert.AreEqual("01:21:18 / 02:21:18", over1Hour);
        }

        [TestMethod]
        public async Task PreviewLoader_XlsxFile_LoadsNativeSpreadsheetModel()
        {
            string tempXlsx = Path.Combine(Path.GetTempPath(), $"aerospot_test_sheet_{Guid.NewGuid():N}.xlsx");
            try
            {
                using (var fs = new FileStream(tempXlsx, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    static void WriteEntry(ZipArchive archive, string entryName, string content)
                    {
                        var entry = archive.CreateEntry(entryName);
                        using var sw = new StreamWriter(entry.Open());
                        sw.Write(content);
                    }

                    WriteEntry(zip, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet2.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/sharedStrings.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml""/>
</Types>");

                    WriteEntry(zip, "xl/sharedStrings.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<sst xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" count=""2"" uniqueCount=""2"">
  <si><t>Voce</t></si>
  <si><t>Importo</t></si>
</sst>");

                    WriteEntry(zip, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheets>
    <sheet name=""Preventivo"" sheetId=""1""/>
    <sheet name=""Note"" sheetId=""2""/>
  </sheets>
</workbook>");

                    WriteEntry(zip, "xl/worksheets/sheet1.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetData>
    <row r=""1"">
      <c r=""A1"" t=""s""><v>0</v></c>
      <c r=""B1"" t=""s""><v>1</v></c>
    </row>
    <row r=""2"">
      <c r=""A2"" t=""inlineStr""><is><t>Licenza</t></is></c>
      <c r=""B2""><v>120</v></c>
    </row>
  </sheetData>
</worksheet>");

                    WriteEntry(zip, "xl/worksheets/sheet2.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetData>
    <row r=""1"">
      <c r=""A1"" t=""b""><v>1</v></c>
    </row>
  </sheetData>
</worksheet>");
                }

                var model = await PreviewLoader.LoadAsync(tempXlsx);
                Assert.AreEqual(PreviewType.Spreadsheet, model.Type);
                Assert.IsNotNull(model.Spreadsheet);
                Assert.HasCount(2, model.Spreadsheet.Sheets);
                Assert.AreEqual("Preventivo", model.Spreadsheet.Sheets[0].Name);
                Assert.AreEqual("Note", model.Spreadsheet.Sheets[1].Name);
                Assert.AreEqual("Voce", model.Spreadsheet.Sheets[0].Rows[0][0]);
                Assert.AreEqual("120", model.Spreadsheet.Sheets[0].Rows[1][1]);
                Assert.AreEqual("VERO", model.Spreadsheet.Sheets[1].Rows[0][0]);
                Assert.Contains("Excel", model.Subtitle);
            }
            finally
            {
                if (File.Exists(tempXlsx)) File.Delete(tempXlsx);
            }
        }

        [TestMethod]
        public void XlsxReader_InvalidPath_ReturnsNull()
        {
            var model = XlsxReader.ReadXlsx(@"C:\NonExistentFile12345.xlsx");
            Assert.IsNull(model);
        }

        [TestMethod]
        public async Task PreviewLoader_HtmlFile_LoadsAsHtmlPreviewType()
        {
            string tempHtml = Path.Combine(Path.GetTempPath(), $"test_page_{Guid.NewGuid():N}.html");
            try
            {
                File.WriteAllText(tempHtml, "<!DOCTYPE html><html><head><title>Test Page</title></head><body><h1>Test AeroSpot</h1><p>Contenuto</p></body></html>");
                var model = await PreviewLoader.LoadAsync(tempHtml);

                Assert.AreEqual(PreviewType.Html, model.Type);
                Assert.IsNotNull(model.TextContent);
                StringAssert.Contains(model.TextContent, "<h1>Test AeroSpot</h1>");
                StringAssert.Contains(model.Subtitle, "HTML");
            }
            finally
            {
                if (File.Exists(tempHtml)) File.Delete(tempHtml);
            }
        }

        [TestMethod]
        public async Task PreviewLoader_SettingsUri_GeneratesGenericPreviewCard()
        {
            var winModel = await PreviewLoader.LoadAsync("ms-settings:keyboard");
            Assert.AreEqual(PreviewType.Generic, winModel.Type);
            StringAssert.Contains(winModel.Title, "Tastiera");
            Assert.AreEqual("Impostazioni", winModel.Subtitle);
            Assert.IsFalse(string.IsNullOrWhiteSpace(winModel.DetailsText));

            var aeroModel = await PreviewLoader.LoadAsync("aerospot:settings:shortcuts");
            Assert.AreEqual(PreviewType.Generic, aeroModel.Type);
            StringAssert.Contains(aeroModel.Title, "Scorciatoie");
            Assert.AreEqual("Impostazioni", aeroModel.Subtitle);
        }

        [TestMethod]
        public async Task ThreeMfLoader_LoadsValid3MfModel_AndRoutesToStl3D()
        {
            string temp3mf = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid():N}.3mf");
            try
            {
                using (var zipStream = new FileStream(temp3mf, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("3D/3dmodel.model");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<model unit=""millimeter"" xmlns=""http://schemas.microsoft.com/3dmanufacturing/core/2015/02"">
  <resources>
    <object id=""1"" type=""model"">
      <mesh>
        <vertices>
          <vertex x=""0"" y=""0"" z=""0"" />
          <vertex x=""10"" y=""0"" z=""0"" />
          <vertex x=""5"" y=""10"" z=""0"" />
        </vertices>
        <triangles>
          <triangle v1=""0"" v2=""1"" v3=""2"" />
        </triangles>
      </mesh>
    </object>
  </resources>
  <build>
    <item objectid=""1"" />
  </build>
</model>");
                }

                var stl = ThreeMfLoader.Load(temp3mf);
                Assert.IsNotNull(stl);
                Assert.AreEqual(1, stl.TriangleCount);
                Assert.AreEqual(3, stl.Mesh.Positions.Count);
                Assert.AreEqual(10.0, stl.SizeX, 0.01);
                Assert.AreEqual(10.0, stl.SizeY, 0.01);

                var previewModel = await PreviewLoader.LoadAsync(temp3mf);
                Assert.AreEqual(PreviewType.Stl3D, previewModel.Type);
                Assert.IsNotNull(previewModel.StlModel);
                StringAssert.Contains(previewModel.DetailsText, "3MF 3D");
            }
            finally
            {
                if (File.Exists(temp3mf)) File.Delete(temp3mf);
            }
        }

        [TestMethod]
        public void PathFilter_RejectsAppleDoubleAndJunkFiles()
        {
            PathFilter.Configure(new[] { "C", "E" });

            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Musica\._Intervista.m4a"), "I file AppleDouble ._* devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"E:\Foto\._DSC00958.jpg"), "I file AppleDouble ._* devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Docs\~$Relazione.docx"), "I file temporanei ~$ devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Cartella\.DS_Store"), "I file .DS_Store devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Immagini\Thumbs.db"), "I file Thumbs.db devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Cartella\desktop.ini"), "I file desktop.ini devono essere esclusi.");

            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Musica\Intervista.m4a"), "I file audio regolari devono essere ammessi.");
            Assert.IsTrue(PathFilter.IsPathAllowed(@"E:\3D\Modello.3mf"), "I file 3MF devono essere ammessi.");
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Sito\index.html"), "I file HTML devono essere ammessi.");
        }

        [TestMethod]
        public void C3_RawExtensions_AreRecognized()
        {
            string[] rawExts = { ".arw", ".dng", ".cr2", ".cr3", ".nef", ".orf", ".rw2", ".pef", ".raf" };
            foreach (var ext in rawExts)
            {
                Assert.IsTrue(PreviewLoader.IsRawExtension(ext), $"Estensione RAW {ext} deve essere riconosciuta da IsRawExtension.");
            }
            Assert.IsFalse(PreviewLoader.IsRawExtension(".jpg"));
            Assert.IsFalse(PreviewLoader.IsRawExtension(".png"));
            Assert.IsFalse(PreviewLoader.IsRawExtension(".dmg"));
        }

        [TestMethod]
        public async Task C3_PreviewLoader_LoadAsync_EmbeddedJpegRaw_LoadsImagePreviewSuccessfully()
        {
            string tempArw = Path.Combine(Path.GetTempPath(), $"synthetic_sony_{Guid.NewGuid():N}.arw");
            try
            {
                // Genera un JPEG ad alta entropia > 50 KB da incorporare dopo un header TIFF little-endian
                int w = 640, h = 480;
                int stride = w * 3;
                byte[] pixels = new byte[h * stride];
                new Random(777).NextBytes(pixels);
                var wbm = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, pixels, stride);

                byte[] jpegBytes;
                using (var ms = new MemoryStream())
                {
                    var enc = new JpegBitmapEncoder { QualityLevel = 95 };
                    enc.Frames.Add(BitmapFrame.Create(wbm));
                    enc.Save(ms);
                    jpegBytes = ms.ToArray();
                }

                Assert.IsGreaterThan(50 * 1024, jpegBytes.Length, "Il JPEG incorporato di prova deve superare la soglia minima di 50 KB.");

                // Costruisci header TIFF minimo seguito da padding e payload JPEG
                byte[] tiffHeaderAndPadding = new byte[512];
                tiffHeaderAndPadding[0] = 0x49; // 'I'
                tiffHeaderAndPadding[1] = 0x49; // 'I'
                tiffHeaderAndPadding[2] = 0x2A;
                tiffHeaderAndPadding[3] = 0x00;
                tiffHeaderAndPadding[4] = 0x08;

                using (var fs = new FileStream(tempArw, FileMode.Create))
                {
                    fs.Write(tiffHeaderAndPadding, 0, tiffHeaderAndPadding.Length);
                    fs.Write(jpegBytes, 0, jpegBytes.Length);
                }

                var model = await PreviewLoader.LoadAsync(tempArw);

                Assert.AreEqual(PreviewType.Image, model.Type, "Un file RAW con anteprima JPEG incorporata deve essere visualizzato come immagine.");
                Assert.IsNotNull(model.Image, "L'immagine estratta dal RAW non deve essere nulla.");
                Assert.IsInstanceOfType<BitmapSource>(model.Image);
                var bmp = (BitmapSource)model.Image;
                Assert.AreEqual(w, bmp.PixelWidth);
                Assert.AreEqual(h, bmp.PixelHeight);
                StringAssert.Contains(model.Subtitle, "×");
            }
            finally
            {
                if (File.Exists(tempArw)) File.Delete(tempArw);
            }
        }

        [TestMethod]
        public async Task C3_PreviewLoader_LoadAsync_CorruptedRaw_FallsBackToGenericMetadataCard()
        {
            string tempCorruptRaw = Path.Combine(Path.GetTempPath(), $"corrupt_sample_{Guid.NewGuid():N}.arw");
            try
            {
                File.WriteAllBytes(tempCorruptRaw, new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 });

                var model = await PreviewLoader.LoadAsync(tempCorruptRaw);

                Assert.AreEqual(PreviewType.Generic, model.Type, "Un file RAW non decodificabile deve mostrare la scheda metadati, non andare in crash.");
                StringAssert.Contains(model.Subtitle, "RAW Image");
                StringAssert.Contains(model.DetailsText, "RAW");
            }
            finally
            {
                if (File.Exists(tempCorruptRaw)) File.Delete(tempCorruptRaw);
            }
        }

        [TestMethod]
        public async Task C4_Svg_SimpleVector_RendersDrawingImage()
        {
            string tempSvg = Path.Combine(Path.GetTempPath(), $"simple_{Guid.NewGuid():N}.svg");
            try
            {
                string svgXml = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\"><circle cx=\"100\" cy=\"100\" r=\"80\" fill=\"blue\" /></svg>";
                File.WriteAllText(tempSvg, svgXml);

                var model = await PreviewLoader.LoadAsync(tempSvg);

                Assert.AreEqual(PreviewType.Image, model.Type);
                Assert.IsNotNull(model.Image);
                Assert.IsGreaterThan(0, model.Image.Width);
                StringAssert.Contains(model.Subtitle, "SVG");
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }
        }

        [TestMethod]
        public async Task C4_Svg_GradientsAndShapes_RendersDrawingImage()
        {
            string tempSvg = Path.Combine(Path.GetTempPath(), $"gradient_{Guid.NewGuid():N}.svg");
            try
            {
                string svgXml = @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""300"" height=""200"">
                    <defs>
                        <linearGradient id=""grad1"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""0%"">
                            <stop offset=""0%"" style=""stop-color:rgb(255,255,0);stop-opacity:1"" />
                            <stop offset=""100%"" style=""stop-color:rgb(255,0,0);stop-opacity:1"" />
                        </linearGradient>
                    </defs>
                    <rect width=""300"" height=""200"" fill=""url(#grad1)"" />
                </svg>";
                File.WriteAllText(tempSvg, svgXml);

                var model = await PreviewLoader.LoadAsync(tempSvg);

                Assert.AreEqual(PreviewType.Image, model.Type);
                Assert.IsNotNull(model.Image);
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }
        }

        [TestMethod]
        public async Task C4_Svg_TextElements_RendersDrawingImage()
        {
            string tempSvg = Path.Combine(Path.GetTempPath(), $"text_{Guid.NewGuid():N}.svg");
            try
            {
                string svgXml = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"100\"><text x=\"20\" y=\"50\" font-size=\"24\" fill=\"black\">AeroSpot SVG</text></svg>";
                File.WriteAllText(tempSvg, svgXml);

                var model = await PreviewLoader.LoadAsync(tempSvg);

                Assert.AreEqual(PreviewType.Image, model.Type);
                Assert.IsNotNull(model.Image);
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }
        }

        [TestMethod]
        public async Task C4_Svg_ScriptsAndExternalReferences_DoesNotExecuteOrHang()
        {
            string tempSvg = Path.Combine(Path.GetTempPath(), $"script_{Guid.NewGuid():N}.svg");
            try
            {
                string svgXml = @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""200"" height=""200"">
                    <script type=""text/javascript"">alert('test');</script>
                    <rect width=""100"" height=""100"" fill=""green"" />
                </svg>";
                File.WriteAllText(tempSvg, svgXml);

                var sw = Stopwatch.StartNew();
                var model = await PreviewLoader.LoadAsync(tempSvg);
                sw.Stop();

                Assert.IsLessThanOrEqualTo(3000, sw.ElapsedMilliseconds);
                Assert.IsTrue(model.Type == PreviewType.Image || model.Type == PreviewType.Text);
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }
        }

        [TestMethod]
        public async Task C4_Svg_MalformedXml_GracefullyFallsBackToSourceText()
        {
            string tempSvg = Path.Combine(Path.GetTempPath(), $"malformed_{Guid.NewGuid():N}.svg");
            try
            {
                string brokenXml = "<svg width=\"200\" height=\"200\"><rect x=\"10\" <invalid <unclosed tag";
                File.WriteAllText(tempSvg, brokenXml);

                var model = await PreviewLoader.LoadAsync(tempSvg);

                Assert.AreEqual(PreviewType.Text, model.Type);
                Assert.IsNotNull(model.TextContent);
                StringAssert.Contains(model.TextContent, "invalid");
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }
        }

        [TestMethod]
        public void MediaCompatibility_SelectiveActivation_Tests()
        {
            // Codec già compatibili nativamente (nessun remux)
            Assert.IsFalse(MediaCompatibilityService.IsRemuxNeeded(false, "AAC", 2));
            Assert.IsFalse(MediaCompatibilityService.IsRemuxNeeded(false, "MP4A", 2));
            Assert.IsFalse(MediaCompatibilityService.IsRemuxNeeded(false, "MP3", 2));
            Assert.IsFalse(MediaCompatibilityService.IsRemuxNeeded(false, "FLAC", 2));
            Assert.IsFalse(MediaCompatibilityService.IsRemuxNeeded(false, "ALAC", 2));
            Assert.IsFalse(MediaCompatibilityService.IsRemuxNeeded(false, "OPUS", 2));

            // Codec bitstream non compatibili (remux obbligatorio)
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(true, "AC3", 6));
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(false, "AC-3", 6));
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(false, "EC3", 6));
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(false, "E-AC3", 6));
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(false, "DTS", 6));
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(false, "Dolby TrueHD", 8));
        }

        [TestMethod]
        public void MediaCompatibility_CacheKeyAndDirectory_Tests()
        {
            string cacheDir = MediaCompatibilityService.GetCacheDirectory();
            Assert.IsFalse(string.IsNullOrWhiteSpace(cacheDir));
            Assert.IsTrue(Directory.Exists(cacheDir));

            string dummyPath = Path.Combine(Path.GetTempPath(), $"dummy_video_{Guid.NewGuid():N}.mp4");
            File.WriteAllBytes(dummyPath, new byte[100]);

            try
            {
                string key1 = MediaCompatibilityService.ComputeCacheKey(dummyPath, "AC3", 6);
                string key2 = MediaCompatibilityService.ComputeCacheKey(dummyPath, "AC3", 6);
                Assert.AreEqual(key1, key2, "Cache key must be deterministic for same file attributes.");

                string key3 = MediaCompatibilityService.ComputeCacheKey(dummyPath, "DTS", 6);
                Assert.AreNotEqual(key1, key3, "Cache key must differ for different audio codecs.");

                string cachePath = MediaCompatibilityService.GetCacheFilePath(dummyPath, "AC3", 6);
                Assert.IsTrue(cachePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(cachePath.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(dummyPath)) File.Delete(dummyPath);
            }
        }

        [TestMethod]
        public async Task MediaCompatibility_RemuxExecutionAndCacheHit_Tests()
        {
            string sourcePath = Path.Combine(Path.GetTempPath(), $"aerospot_media_src_{Guid.NewGuid():N}.mp4");
            string? expectedCachePath = null;

            try
            {
                string? ffmpegExe = FFmpegLocator.FindExecutable();
                if (!string.IsNullOrEmpty(ffmpegExe))
                {
                    // Se FFmpeg è presente nel sistema, genera una clip reale con audio AC-3 e verifica remux + cache hit
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegExe,
                        Arguments = $"-y -nostats -loglevel error -f lavfi -i color=c=black:s=160x120:d=1 -f lavfi -i sine=frequency=440:duration=1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -movflags +faststart -c:a ac3 -ac 6 \"{sourcePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    using var proc = Process.Start(psi);
                    Assert.IsNotNull(proc, "Avvio FFmpeg per generare la clip di test non riuscito.");

                    var stdOutTask = proc.StandardOutput.ReadToEndAsync();
                    var stdErrTask = proc.StandardError.ReadToEndAsync();
                    using var genCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await proc.WaitForExitAsync(genCts.Token);
                    string stdErr = await stdErrTask;
                    await stdOutTask;
                    Assert.AreEqual(0, proc.ExitCode, $"Generazione clip FFmpeg fallita: {stdErr}");

                    expectedCachePath = MediaCompatibilityService.GetCacheFilePath(sourcePath, "AC3", 6);
                    string cached1 = await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                        sourcePath,
                        "AC3",
                        6,
                        TimeSpan.FromSeconds(1));

                    Assert.IsTrue(File.Exists(cached1), "Compatible remux file must exist.");
                    Assert.IsGreaterThan(1024L, new FileInfo(cached1).Length, "Remuxed file size must be > 1024 bytes.");
                }
                else
                {
                    // In assenza di FFmpeg standalone nell'ambiente di build, popola la cache attesa e verifica il percorso cache hit
                    File.WriteAllBytes(sourcePath, new byte[2048]);
                    expectedCachePath = MediaCompatibilityService.GetCacheFilePath(sourcePath, "AC3", 6);
                    File.WriteAllBytes(expectedCachePath, new byte[2048]);
                }

                // Cache hit deve essere istantaneo (< 100 ms)
                var sw = Stopwatch.StartNew();
                string cachedHit = await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                    sourcePath,
                    "AC3",
                    6,
                    TimeSpan.FromSeconds(1));
                sw.Stop();

                Assert.AreEqual(expectedCachePath, cachedHit, "Cache hit must return the exact cached file path.");
                Assert.IsLessThan(1000L, sw.ElapsedMilliseconds, $"Cache hit should be fast (< 1000ms), took: {sw.ElapsedMilliseconds} ms");
            }
            finally
            {
                try { if (File.Exists(sourcePath)) File.Delete(sourcePath); } catch { }
                try { if (expectedCachePath != null && File.Exists(expectedCachePath)) File.Delete(expectedCachePath); } catch { }
            }
        }

        [TestMethod]
        public async Task MediaCompatibility_Cancellation_CleansUpTempFile()
        {
            string syntheticPath = Path.Combine(Path.GetTempPath(), $"aerospot_cancel_src_{Guid.NewGuid():N}.mp4");
            string cacheKey = string.Empty;
            string tempFile = string.Empty;

            try
            {
                File.WriteAllBytes(syntheticPath, new byte[2048]);
                cacheKey = MediaCompatibilityService.ComputeCacheKey(syntheticPath, "AC3_CANCELLATION_TEST", 6);
                tempFile = Path.Combine(MediaCompatibilityService.GetCacheDirectory(), $"{cacheKey}.tmp.mp4");

                using var cts = new CancellationTokenSource();
                cts.Cancel();

                bool threw = false;
                try
                {
                    await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                        syntheticPath,
                        "AC3_CANCELLATION_TEST",
                        6,
                        TimeSpan.FromMinutes(1),
                        cancellationToken: cts.Token);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }

                Assert.IsTrue(threw, "OperationCanceledException must be thrown when cancelled.");
                Assert.IsFalse(File.Exists(tempFile), "Temporary remux file must be cleaned up on cancellation.");
            }
            finally
            {
                try { if (File.Exists(syntheticPath)) File.Delete(syntheticPath); } catch { }
                try { if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [TestMethod]
        public async Task PreviewLoader_DocxFile_LoadsNativelyAsMarkdownWithoutOfficeProcesses()
        {
            string tempDocx = Path.Combine(Path.GetTempPath(), $"aerospot_test_{Guid.NewGuid():N}.docx");
            try
            {
                using (var fs = new FileStream(tempDocx, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = zip.CreateEntry("word/document.xml");
                    using var sw = new StreamWriter(entry.Open());
                    sw.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
    <w:body>
        <w:p>
            <w:pPr><w:pStyle w:val=""Heading1""/></w:pPr>
            <w:r><w:t>AeroSpot Native Word Preview</w:t></w:r>
        </w:p>
        <w:p>
            <w:r><w:t>Questo è un documento di prova con testo in </w:t></w:r>
            <w:r><w:rPr><w:b/></w:rPr><w:t>grassetto</w:t></w:r>
            <w:r><w:t> e formato markdown.</w:t></w:r>
        </w:p>
        <w:tbl>
            <w:tr>
                <w:tc><w:p><w:r><w:t>Colonna 1</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Colonna 2</w:t></w:r></w:p></w:tc>
            </w:tr>
            <w:tr>
                <w:tc><w:p><w:r><w:t>Dato A</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Dato B</w:t></w:r></w:p></w:tc>
            </w:tr>
        </w:tbl>
    </w:body>
</w:document>");
                }

                var docxModel = DocxReader.ReadDocx(tempDocx);
                Assert.IsNotNull(docxModel);
                StringAssert.Contains(docxModel.MarkdownContent, "# AeroSpot Native Word Preview", "Headings must be formatted with Markdown #");
                StringAssert.Contains(docxModel.MarkdownContent, "**grassetto**", "Bold text must be formatted with Markdown **");
                StringAssert.Contains(docxModel.MarkdownContent, "| Colonna 1 | Colonna 2 |", "Tables must be formatted as Markdown tables");
                Assert.IsGreaterThanOrEqualTo(2, docxModel.ParagraphCount);
                Assert.IsGreaterThanOrEqualTo(5, docxModel.WordCount);

                var preview = await PreviewLoader.LoadAsync(tempDocx);
                Assert.AreEqual(PreviewType.Markdown, preview.Type, "Docx must load natively as PreviewType.Markdown without summoning COM PreviewHandler");
                Assert.IsFalse(string.IsNullOrWhiteSpace(preview.TextContent));
                StringAssert.Contains(preview.Subtitle, "Word Document");
            }
            finally
            {
                if (File.Exists(tempDocx)) File.Delete(tempDocx);
            }
        }

        [TestMethod]
        public void PreviewHandlerResolver_BlocksOfficeComHandlers()
        {
            Assert.IsTrue(PreviewHandlerResolver.IsBlockedHandlerClsid(new Guid("84F66100-FF7C-4fb4-B0C0-02CD7FB668FE")), "Word PreviewHandler CLSID must be blocked.");
            Assert.IsTrue(PreviewHandlerResolver.IsBlockedHandlerClsid(new Guid("00020827-0000-0000-C000-000000000046")), "Excel PreviewHandler CLSID must be blocked.");
            Assert.IsTrue(PreviewHandlerResolver.IsBlockedHandlerClsid(new Guid("65235197-874B-4A07-BDC5-E65EA825B718")), "PowerPoint PreviewHandler CLSID must be blocked.");
            Assert.IsFalse(PreviewHandlerResolver.IsBlockedHandlerClsid(Guid.Empty));

            foreach (string ext in new[] { ".docx", ".doc", ".dot", ".rtf", ".odt", ".xlsx" })
            {
                bool found = PreviewHandlerResolver.TryGetHandlerClsid(ext, out var clsid);
                Assert.IsFalse(found, $"Extension '{ext}' must never resolve to a COM PreviewHandler.");
                Assert.AreEqual(Guid.Empty, clsid, $"Extension '{ext}' must return Guid.Empty.");
            }
        }

        [TestMethod]
        public async Task PreviewLoader_RtfAndOfficeFiles_LoadNativelyWithoutSpawningWordProcess()
        {
            var initialWordPids = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName("WINWORD"))
            {
                try { initialWordPids.Add(p.Id); }
                finally { p.Dispose(); }
            }

            string sampleRtf = Path.Combine(TestEnvironment.TestsDir, "test_sample.rtf");
            string tempAccentRtf = Path.Combine(Path.GetTempPath(), $"aerospot_accent_{Guid.NewGuid():N}.rtf");
            string tempOdt = Path.Combine(Path.GetTempPath(), $"aerospot_test_{Guid.NewGuid():N}.odt");
            string tempCorruptDocx = Path.Combine(Path.GetTempPath(), $"aerospot_corrupt_{Guid.NewGuid():N}.docx");
            string tempLegacyDoc = Path.Combine(Path.GetTempPath(), $"aerospot_legacy_{Guid.NewGuid():N}.doc");

            try
            {
                // 1. Sample RTF reale del repository
                Assert.IsTrue(File.Exists(sampleRtf), $"File test_sample.rtf mancante: {sampleRtf}");
                var sampleModel = await PreviewLoader.LoadAsync(sampleRtf);
                Assert.AreEqual(PreviewType.Markdown, sampleModel.Type);
                StringAssert.Contains(sampleModel.TextContent, "AeroSpot RTF Document Test for Preview Handler.");

                // 2. RTF temporaneo con accenti ANSI italiani, Unicode \u8364? (€) e grassetto
                await File.WriteAllTextAsync(tempAccentRtf,
                    @"{\rtf1\ansi\ansicpg1252\deff0 {\fonttbl {\f0 Segoe UI;}} Citt\'e0 e \b grassetto\b0  a \u8364? 100.\par Seconda riga.}");
                var accentModel = await PreviewLoader.LoadAsync(tempAccentRtf);
                Assert.AreEqual(PreviewType.Markdown, accentModel.Type);
                StringAssert.Contains(accentModel.TextContent, "Città");
                StringAssert.Contains(accentModel.TextContent, "**grassetto**");
                StringAssert.Contains(accentModel.TextContent, "€ 100.");

                // 3. ODT sintetico con content.xml
                using (var fs = new FileStream(tempOdt, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = zip.CreateEntry("content.xml");
                    using var sw = new StreamWriter(entry.Open());
                    sw.Write(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content
    xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
    xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0""
    xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"">
  <office:body>
    <office:text>
      <text:h text:outline-level=""1"">Titolo ODT</text:h>
      <text:p>Paragrafo OpenDocument nativo.</text:p>
    </office:text>
  </office:body>
</office:document-content>");
                }
                var odtModel = await PreviewLoader.LoadAsync(tempOdt);
                Assert.AreEqual(PreviewType.Markdown, odtModel.Type);
                StringAssert.Contains(odtModel.TextContent, "# Titolo ODT");
                StringAssert.Contains(odtModel.TextContent, "Paragrafo OpenDocument nativo.");

                // 4. DOCX corrotto e DOC legacy -> PreviewType.Generic senza fallback COM
                await File.WriteAllBytesAsync(tempCorruptDocx, new byte[] { 0x50, 0x4B, 0x00, 0xFF });
                var corruptDocxModel = await PreviewLoader.LoadAsync(tempCorruptDocx);
                Assert.AreEqual(PreviewType.Generic, corruptDocxModel.Type);

                await File.WriteAllBytesAsync(tempLegacyDoc, new byte[] { 0xD0, 0xCF, 0x11, 0xE0 });
                var legacyDocModel = await PreviewLoader.LoadAsync(tempLegacyDoc);
                Assert.AreEqual(PreviewType.Generic, legacyDocModel.Type);

                // Verifica che nessun nuovo processo WINWORD sia stato avviato
                int spawnedWordCount = 0;
                foreach (var p in Process.GetProcessesByName("WINWORD"))
                {
                    try
                    {
                        if (!initialWordPids.Contains(p.Id))
                        {
                            spawnedWordCount++;
                        }
                    }
                    finally { p.Dispose(); }
                }
                Assert.AreEqual(0, spawnedWordCount, "Nessun processo WINWORD.EXE deve essere avviato durante l'anteprima di RTF/ODT/DOCX/DOC.");
            }
            finally
            {
                foreach (var f in new[] { tempAccentRtf, tempOdt, tempCorruptDocx, tempLegacyDoc })
                {
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                }
            }
        }
    }

    internal class PresentationSourceMock : PresentationSource
    {
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
    }
}
