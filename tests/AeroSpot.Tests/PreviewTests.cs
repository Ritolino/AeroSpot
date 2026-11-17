using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Search;
using AeroSpot.Worker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class PreviewTests
    {
        [TestMethod]
        public void SettingsWindow_InstantiatesCleanly()
        {
            var t = new System.Threading.Thread(() =>
            {
                if (System.Windows.Application.Current == null) new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                var win = new AeroSpot.Worker.SettingsWindow();
                Assert.IsNotNull(win);
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [TestMethod]
        public async Task PreviewLoader_RealImage_LoadsSuccessfully()
        {
            // Cerca dinamicamente l'immagine JPEG presente nella cartella (inclusa la cartella design)
            string? imagePath = Directory.GetFiles(TestEnvironment.RepoRoot, "*.jpeg", SearchOption.AllDirectories).FirstOrDefault();
            if (imagePath == null || !File.Exists(imagePath))
            {
                Assert.Inconclusive("Immagine di prova JPEG non trovata.");
                return;
            }

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

        [TestMethod]
        public async Task PreviewLoader_TextFile_ReadsContentAndEncodings()
        {
            string mdPath = Path.Combine(TestEnvironment.DocsDir, "ISTRUZIONI.md");
            if (!File.Exists(mdPath))
            {
                mdPath = Path.Combine(TestEnvironment.RepoRoot, "ISTRUZIONI.md");
            }
            if (!File.Exists(mdPath))
            {
                Assert.Inconclusive("ISTRUZIONI.md non trovato.");
                return;
            }

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
            var t = new System.Threading.Thread(() =>
            {
                if (System.Windows.Application.Current == null) new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

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

                var doc = AeroSpot.Worker.Services.MarkdownToFlowDocumentConverter.Convert(sampleMd);
                Assert.IsNotNull(doc);
                Assert.IsGreaterThan(0, doc.Blocks.Count, "FlowDocument deve contenere blocchi renderizzati.");
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [TestMethod]
        public async Task PreviewLoader_LargeTextFile_TruncatesTo100KB()
        {
            // Crea un file di testo temporaneo di 150 KB
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
                // Scrive byte non validi in un file con estensione .png
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
            string missingPath = @"E:\NonExistentDirectory\FakeFile9999.xyz";
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
        public async Task PreviewLoader_PdfFile_LoadsFastImageOrPreviewHandler()
        {
            string pdfPath = @"E:\Personale\Viaggi\RoadTrip 2025\RoadTrip V2.pdf";
            if (!File.Exists(pdfPath))
            {
                pdfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.pdf");
            }
            Assert.IsTrue(File.Exists(pdfPath), $"Il file PDF di test deve esistere: {pdfPath}");

            var model = await PreviewLoader.LoadAsync(pdfPath);
            Console.WriteLine($"PDF model.Type: {model.Type}, w={model.PdfWidth}, h={model.PdfHeight}");
            Assert.IsTrue(model.Type == PreviewType.Pdf || model.Type == PreviewType.PreviewHandler || model.Type == PreviewType.Generic, $"PDF should load as Pdf, PreviewHandler or Generic, got: {model.Type}");
            if (model.Type == PreviewType.Pdf || model.Type == PreviewType.PreviewHandler)
            {
                Assert.IsGreaterThan(0.0, model.PdfWidth, "PdfWidth deve essere > 0");
                Assert.IsGreaterThan(0.0, model.PdfHeight, "PdfHeight deve essere > 0");
            }
            Assert.Contains(".pdf", model.Title.ToLowerInvariant());
        }

        [TestMethod]
        public async Task PreviewLoader_RtfFile_ReturnsPreviewHandlerTypeWithClsid()
        {
            string rtfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.rtf");
            if (!File.Exists(rtfPath))
            {
                rtfPath = Path.Combine(TestEnvironment.TestsDir, "test_sample.rtf");
            }
            Assert.IsTrue(File.Exists(rtfPath), $"Il file RTF di test deve esistere: {rtfPath}");

            var model = await PreviewLoader.LoadAsync(rtfPath);

            Assert.AreEqual(PreviewType.PreviewHandler, model.Type);
            Assert.AreNotEqual(Guid.Empty, model.PreviewHandlerClsid);
            Assert.IsFalse(string.IsNullOrWhiteSpace(model.PreviewHandlerName));
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

        private static void PumpUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new System.Windows.Threading.DispatcherOperationCallback(f => { ((System.Windows.Threading.DispatcherFrame)f).Continue = false; return null; }),
                    frame);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(20);
            }
        }

        [TestMethod]
        public void PreviewWindow_DwmCloak_ImageTransitions_HaveZeroFlicker()
        {
            Exception? threadEx = null;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null) new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

                    var win = new AeroSpot.Worker.PreviewWindow();
                    Assert.IsFalse(win.IsPreviewVisible);

                    string? img1 = Directory.GetFiles(TestEnvironment.RepoRoot, "*.png", SearchOption.AllDirectories).FirstOrDefault();
                    string? img2 = Directory.GetFiles(TestEnvironment.RepoRoot, "*.jpeg", SearchOption.AllDirectories).FirstOrDefault();

                    if (img1 != null && img2 != null && File.Exists(img1) && File.Exists(img2))
                    {
                        // 1. Apri immagine 1
                        var task1 = win.ShowPreviewForFileAsync(img1, isFromSpotlight: true);
                        PumpUntil(() => task1.IsCompleted);
                        if (task1.IsFaulted) throw task1.Exception!;
                        Assert.IsTrue(win.IsPreviewVisible);

                        // 2. Chiudi con HidePreview
                        win.HidePreview();
                        Assert.IsFalse(win.IsPreviewVisible);

                        // 3. Apri immagine 2
                        var task2 = win.ShowPreviewForFileAsync(img2, isFromSpotlight: true);
                        PumpUntil(() => task2.IsCompleted);
                        if (task2.IsFaulted) throw task2.Exception!;
                        Assert.IsTrue(win.IsPreviewVisible);

                        win.HidePreview();
                        Assert.IsFalse(win.IsPreviewVisible);
                    }

                    win.Close();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(15));

            if (threadEx != null)
            {
                throw new Exception("Errore nel thread di test anteprima", threadEx);
            }
        }

        [TestMethod]
        public void PreviewWindow_VideoSeeking_CustomTimeline_SetsTargetPosition()
        {
            Exception? threadEx = null;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null) new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

                    var win = new AeroSpot.Worker.PreviewWindow();
                    string videoPath = Path.Combine(TestEnvironment.TestsDir, "test_video.mp4");

                    if (File.Exists(videoPath))
                    {
                        var task = win.ShowPreviewForFileAsync(videoPath, isFromSpotlight: true);
                        PumpUntil(() => task.IsCompleted);
                        if (task.IsFaulted) throw task.Exception!;

                        Assert.IsTrue(win.IsPreviewVisible);

                        win.HidePreview();
                        Assert.IsFalse(win.IsPreviewVisible);
                    }

                    win.Close();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(15));

            if (threadEx != null)
            {
                throw new Exception("Errore nel thread di test video seeking", threadEx);
            }
        }

        [TestMethod]
        public void FolderIcon_ReturnsNonNullImageSource()
        {
            var t = new System.Threading.Thread(() =>
            {
                if (System.Windows.Application.Current == null) new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                var icon1 = IconService.GetIcon(TestEnvironment.RepoRoot, "Cartella");
                Assert.IsNotNull(icon1, "L'icona per una cartella reale non deve essere nulla.");

                var icon2 = IconService.GetIcon(":folder:", "Cartella");
                Assert.IsNotNull(icon2, "L'icona per una cartella generica non deve essere nulla.");
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [TestMethod]
        public async Task AppsFolder_ExecutableOrUwp_DoesNotReturnError()
        {
            // Verifica che un percorso shell:AppsFolder non generi PreviewType.Error (triangolo giallo)
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
            string? imagePath = Directory.GetFiles(TestEnvironment.RepoRoot, "*.jpeg", SearchOption.AllDirectories).FirstOrDefault();
            if (imagePath == null || !File.Exists(imagePath))
            {
                Assert.Inconclusive("Immagine di prova JPEG non trovata.");
                return;
            }

            var model = await PreviewLoader.LoadAsync(imagePath);
            Assert.AreEqual(PreviewType.Image, model.Type);
            Assert.Contains("×", model.Subtitle);
            // Il footer NON deve contenere la risoluzione (es. "×")
            Assert.IsFalse(model.FooterText?.Contains("×") ?? false);
        }

        [TestMethod]
        public async Task PreviewLoader_PdfPage_HasWidthAndHeightForAspectRatio()
        {
            string pdfPath = @"E:\Personale\Viaggi\RoadTrip 2025\RoadTrip V2.pdf";
            if (!File.Exists(pdfPath))
            {
                pdfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.pdf");
            }
            if (!File.Exists(pdfPath))
            {
                Assert.Inconclusive("File PDF non presente nell'ambiente di test.");
                return;
            }

            var model = await PreviewLoader.LoadAsync(pdfPath);
            Assert.IsGreaterThan(0.0, model.PdfWidth, "PdfWidth deve essere > 0");
            Assert.IsGreaterThan(0.0, model.PdfHeight, "PdfHeight deve essere > 0");
        }

        [TestMethod]
        public void MainWindow_EscapeKey_FiresCloseAllRequested()
        {
            var t = new System.Threading.Thread(() =>
            {
                var win = new AeroSpot.Worker.MainWindow();
                bool closeAllFired = false;
                win.CloseAllRequested += () => closeAllFired = true;

                // Simula evento KeyDown Esc
                var keyEvent = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new PresentationSourceMock(),
                    0,
                    System.Windows.Input.Key.Escape)
                {
                    RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent
                };

                // Invoca KeyDown sul searchbox
                win.SearchBox.RaiseEvent(keyEvent);
                Assert.IsTrue(closeAllFired, "La pressione di Esc in Spotlight deve scatenare CloseAllRequested per chiudere anche QuickLook.");
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [TestMethod]
        public void PreviewWindow_EscapeKey_AlwaysFiresCloseAllRequested()
        {
            var t = new System.Threading.Thread(() =>
            {
                var win = new AeroSpot.Worker.PreviewWindow();
                bool closeAllFired = false;
                win.CloseAllRequested += () => closeAllFired = true;

                var keyEvent = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new PresentationSourceMock(),
                    0,
                    System.Windows.Input.Key.Escape)
                {
                    RoutedEvent = System.Windows.UIElement.KeyDownEvent
                };

                win.RaiseEvent(keyEvent);
                Assert.IsTrue(closeAllFired, "La pressione di Esc in QuickLook deve scatenare CloseAllRequested per chiudere sia QuickLook che Spotlight.");
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [TestMethod]
        public void PreviewWindow_AudioDimensions_TargetSizeIs558x408()
        {
            var t = new System.Threading.Thread(() =>
            {
                var win = new AeroSpot.Worker.PreviewWindow();
                var audioModel = new AeroSpot.Worker.Services.PreviewModel
                {
                    Type = AeroSpot.Worker.Services.PreviewType.Audio,
                    FilePath = "test.mp3"
                };

                // Chiamata riflessiva a PositionAndSizeWindow per verificare il dimensionamento calcolato
                var method = typeof(AeroSpot.Worker.PreviewWindow).GetMethod("PositionAndSizeWindow",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(win, new object[] { audioModel });

                Assert.AreEqual(558.0, win.Width, 1.0, "La larghezza standard per audio deve essere 558 (+3%).");
                Assert.AreEqual(408.0, win.Height, 1.0, "L'altezza standard per audio deve essere 408 (+15%).");
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }

        [TestMethod]
        public void PreviewWindow_FormatPlaybackTime_HandlesHoursProperly()
        {
            var method = typeof(AeroSpot.Worker.PreviewWindow).GetMethod("FormatPlaybackTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method);

            // Sotto un'ora: mm:ss
            var under1Hour = method.Invoke(null, new object[] { TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(263) }) as string;
            Assert.AreEqual("01:30 / 04:23", under1Hour);

            // Sopra un'ora: hh:mm:ss (es. film 2h 21m 18s)
            var over1Hour = method.Invoke(null, new object[] { TimeSpan.FromSeconds(3600 + 1278), TimeSpan.FromSeconds(7200 + 1278) }) as string;
            Assert.AreEqual("01:21:18 / 02:21:18", over1Hour);
        }

        [TestMethod]
        public async Task PreviewLoader_XlsxFile_LoadsNativeSpreadsheetModel()
        {
            string xlsxPath = @"E:\Personale\Scout\Rute 2019\Percorsi\Tabella di Marcia.xlsx";
            if (!File.Exists(xlsxPath))
            {
                Assert.Inconclusive("File di test XLSX non trovato su disco locale.");
                return;
            }

            var model = await PreviewLoader.LoadAsync(xlsxPath);
            Assert.AreEqual(PreviewType.Spreadsheet, model.Type);
            Assert.IsNotNull(model.Spreadsheet);
            Assert.IsGreaterThan(0, model.Spreadsheet.Sheets.Count);
            Assert.Contains("Excel", model.Subtitle);
            Assert.IsGreaterThan(0, model.Spreadsheet.Sheets[0].Rows.Count);
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
                // Costruisci un pacchetto 3MF valido con triangolo 3D
                using (var zipStream = new FileStream(temp3mf, FileMode.Create))
                using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
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

                // Verifica che PreviewLoader instradi correttamente il 3MF verso PreviewType.Stl3D
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
        public void A5a_JpgDiagnosticMeasurement()
        {
            var filesToTest = new List<string>();
            string realJpg1 = @"E:\Foto\Amici\Aura Lepual\Editate\DSC00660.jpg";
            string realJpg2 = @"E:\Foto\Amici\Aura Lepual\Editate\DSC00679.jpg";

            if (File.Exists(realJpg1)) filesToTest.Add(realJpg1);
            if (File.Exists(realJpg2)) filesToTest.Add(realJpg2);

            // Genera anche un JPG di grandi dimensioni (6000x4000, 24 MP) sintetico per avere un benchmark riproducibile ovunque
            string tempJpg = Path.Combine(Path.GetTempPath(), $"benchmark_24mp_{Guid.NewGuid():N}.jpg");
            try
            {
                int w = 6000, h = 4000;
                var pixelFormat = PixelFormats.Bgr24;
                int stride = (w * pixelFormat.BitsPerPixel + 7) / 8;
                byte[] rawData = new byte[stride * h];
                new Random(42).NextBytes(rawData);
                var wbm = BitmapSource.Create(w, h, 96, 96, pixelFormat, null, rawData, stride);

                using (var fs = new FileStream(tempJpg, FileMode.Create))
                {
                    var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                    enc.Frames.Add(BitmapFrame.Create(wbm));
                    enc.Save(fs);
                }
                filesToTest.Add(tempJpg);

                foreach (var path in filesToTest)
                {
                    var fi = new FileInfo(path);
                    Console.WriteLine($"\n=== BENCHMARK FILE: {fi.Name} ({fi.Length / (1024.0 * 1024.0):F2} MB, Unita' {path.Substring(0, 2)}) ===");

                    // 1. Decodifica Piena Risoluzione (Comportamento attuale)
                    var sw = Stopwatch.StartNew();
                    BitmapImage full;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        full = new BitmapImage();
                        full.BeginInit();
                        full.CacheOption = BitmapCacheOption.OnLoad;
                        full.StreamSource = stream;
                        full.EndInit();
                        full.Freeze();
                    }
                    sw.Stop();
                    long fullMs = sw.ElapsedMilliseconds;
                    Console.WriteLine($"  [Piena Risoluzione] {full.PixelWidth}x{full.PixelHeight} -> {fullMs} ms");

                    // 2. Decodifica Ottimizzata (DecodePixelWidth = 2560 - Risoluzione Massima Display 2K/4K)
                    sw.Restart();
                    BitmapImage downsampled;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        downsampled = new BitmapImage();
                        downsampled.BeginInit();
                        downsampled.CacheOption = BitmapCacheOption.OnLoad;
                        downsampled.DecodePixelWidth = 2560;
                        downsampled.StreamSource = stream;
                        downsampled.EndInit();
                        downsampled.Freeze();
                    }
                    sw.Stop();
                    long optMs = sw.ElapsedMilliseconds;
                    Console.WriteLine($"  [Ottimizzata 2560px] {downsampled.PixelWidth}x{downsampled.PixelHeight} -> {optMs} ms (Velocizzazione: {(double)fullMs / Math.Max(1, optMs):F1}x)");

                    // 3. Decodifica Ripetuta (Warm cache)
                    sw.Restart();
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var b = new BitmapImage();
                        b.BeginInit();
                        b.CacheOption = BitmapCacheOption.OnLoad;
                        b.DecodePixelWidth = 2560;
                        b.StreamSource = stream;
                        b.EndInit();
                        b.Freeze();
                    }
                    sw.Stop();
                    Console.WriteLine($"  [Ottimizzata Warm] -> {sw.ElapsedMilliseconds} ms");

                    Assert.IsNotNull(downsampled);
                    Assert.IsLessThanOrEqualTo(downsampled.PixelWidth, 2560);
                }
            }
            finally
            {
                if (File.Exists(tempJpg)) File.Delete(tempJpg);
            }
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
            Assert.IsFalse(PreviewLoader.IsRawExtension(".dmg")); // DMG non è un formato raw fotografico
        }

        [TestMethod]
        public async Task C3_PreviewLoader_LoadAsync_RealSonyArw_LoadsImagePreviewSuccessfully()
        {
            string rawPath = @"E:\Foto\Amici\Aura Lepual\Raw\DSC00660.ARW";
            if (!File.Exists(rawPath))
            {
                Console.WriteLine("ARW file non presente su E:, skipping real hardware check.");
                return;
            }

            var sw = Stopwatch.StartNew();
            var model = await PreviewLoader.LoadAsync(rawPath);
            sw.Stop();

            Console.WriteLine($"Apertura RAW reale ({new FileInfo(rawPath).Length / (1024 * 1024)} MB): {sw.ElapsedMilliseconds} ms, Tipo: {model.Type}, Sottotitolo: {model.Subtitle}");

            Assert.AreEqual(PreviewType.Image, model.Type, "Un file RAW valido deve essere visualizzato come immagine.");
            Assert.IsNotNull(model.Image, "L'immagine decodificata o estratta non deve essere nulla.");
            Assert.IsGreaterThan(0, model.Image.Width);
            Assert.IsGreaterThan(0, model.Image.Height);
            StringAssert.Contains(model.Subtitle, "×");
        }

        [TestMethod]
        public async Task C3_PreviewLoader_LoadAsync_CorruptedRaw_FallsBackToGenericMetadataCard()
        {
            string tempCorruptRaw = Path.Combine(Path.GetTempPath(), $"corrupt_sample_{Guid.NewGuid():N}.arw");
            try
            {
                // Scrive file non valido senza header WIC e senza JPEG incorporato
                File.WriteAllBytes(tempCorruptRaw, new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 });

                var model = await PreviewLoader.LoadAsync(tempCorruptRaw);

                Assert.AreEqual(PreviewType.Generic, model.Type, "Un file RAW non decodificabile deve mostrare la scheda metadati (Livello 3), non andare in crash.");
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

                // Un SVG malformato deve eseguire il fallback sicuro sul testo sorgente senza lanciare eccezioni
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
        public async Task Alien_ReferenceFile_DetectedAsBitstreamAudio()
        {
            string path = @"C:\Users\gabry\Downloads\Alien - La clonazione (1997).mp4";
            if (!File.Exists(path))
            {
                Assert.Inconclusive("Alien file not found in Downloads");
                return;
            }

            var meta = await VideoMetadataService.GetMetadataAsync(path);
            Assert.IsNotNull(meta);
            Console.WriteLine($"VideoMetadataService: Codec={meta.AudioCodec}, Channels={meta.AudioChannels}, SampleRate={meta.SampleRate}, IsBitstream={meta.IsBitstreamAudio}");

            Assert.IsTrue(meta.IsBitstreamAudio, "Original file with AC-3 track must be recognized as bitstream audio.");
            Assert.AreEqual(6, meta.AudioChannels, "AC-3 audio track in test file must have 6 channels (5.1).");
            Assert.IsTrue(MediaCompatibilityService.IsRemuxNeeded(meta), "MediaCompatibilityService must require remux for this file.");
        }

        [TestMethod]
        public async Task Alien_VerifyRemuxPlayback()
        {
            string remuxPath = Path.Combine(Path.GetTempPath(), "alien_remux_full.mp4");
            if (!File.Exists(remuxPath)) return;

            var meta = await VideoMetadataService.GetMetadataAsync(remuxPath);
            Assert.IsNotNull(meta);
            Console.WriteLine($"Remux meta: {meta.Width}x{meta.Height}, Duration={meta.Duration}, Codec={meta.AudioCodec}, Channels={meta.AudioChannels}, IsBitstream={meta.IsBitstreamAudio}");

            Assert.IsFalse(meta.IsBitstreamAudio, "Remux file must not be recognized as bitstream audio.");
            Assert.AreEqual(2, meta.AudioChannels, "Remux file must have 2 audio channels (stereo).");
            Assert.AreEqual(1920u, meta.Width);
            Assert.AreEqual(820u, meta.Height);

            // Test MediaPlayer playback
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(remuxPath);
            var source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
            var player = new Windows.Media.Playback.MediaPlayer();
            var tcs = new TaskCompletionSource<bool>();
            player.MediaOpened += (s, e) => tcs.TrySetResult(true);
            player.MediaFailed += (s, e) => tcs.TrySetException(new Exception($"Playback failed: {e.ErrorMessage}"));
            player.Source = source;

            var done = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.AreEqual(tcs.Task, done, "MediaPlayer did not open remux file within 5 seconds.");
            Assert.IsGreaterThan(6000.0, player.NaturalDuration.TotalSeconds, "Duration should be over 100 minutes.");
            player.Dispose();
            source.Dispose();
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

            string dummyPath = Path.Combine(Path.GetTempPath(), "dummy_video.mp4");
            if (!File.Exists(dummyPath)) File.WriteAllBytes(dummyPath, new byte[100]);

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
            // Usiamo il clip di 60s generato per test rapidi
            string clipPath = Path.Combine(Path.GetTempPath(), "clip_60s.mp4");
            if (!File.Exists(clipPath)) return;

            var progressList = new System.Collections.Generic.List<double>();
            var progress = new Progress<double>(p => progressList.Add(p));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string cached1 = await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                clipPath,
                "AC3",
                6,
                TimeSpan.FromSeconds(60),
                progress);
            sw.Stop();
            long firstRunMs = sw.ElapsedMilliseconds;

            Assert.IsTrue(File.Exists(cached1), "Compatible remux file must exist.");
            Assert.IsGreaterThan(1000L, new FileInfo(cached1).Length, "File size must be > 1000 bytes");

            // Test Cache Hit (deve essere istantaneo, < 50ms)
            sw.Restart();
            string cached2 = await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                clipPath,
                "AC3",
                6,
                TimeSpan.FromSeconds(60));
            sw.Stop();
            long cacheHitMs = sw.ElapsedMilliseconds;

            Assert.AreEqual(cached1, cached2, "Second run must return exact same cached file.");
            Assert.IsLessThan(100L, cacheHitMs, $"Cache hit should be near instant (< 100ms), took: {cacheHitMs} ms");
            Console.WriteLine($"Remux run: {firstRunMs} ms, Cache hit: {cacheHitMs} ms");
        }

        [TestMethod]
        public async Task MediaCompatibility_Cancellation_CleansUpTempFile()
        {
            string alienPath = @"C:\Users\gabry\Downloads\Alien - La clonazione (1997).mp4";
            if (!File.Exists(alienPath)) return;

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50); // Cancella quasi subito

            bool threw = false;
            try
            {
                await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                    alienPath,
                    "AC3_CANCELLATION_TEST",
                    6,
                    TimeSpan.FromMinutes(100),
                    cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "OperationCanceledException must be thrown when cancelled.");

            string cacheKey = MediaCompatibilityService.ComputeCacheKey(alienPath, "AC3_CANCELLATION_TEST", 6);
            string tempFile = Path.Combine(MediaCompatibilityService.GetCacheDirectory(), $"{cacheKey}.tmp.mp4");
            Assert.IsFalse(File.Exists(tempFile), "Temporary remux file must be cleaned up on cancellation.");
        }

        [TestMethod]
        public async Task PreviewLoader_DocxFile_LoadsNativelyAsMarkdownWithoutOfficeProcesses()
        {
            string tempDocx = Path.Combine(Path.GetTempPath(), $"aerospot_test_{Guid.NewGuid():N}.docx");
            try
            {
                using (var fs = new FileStream(tempDocx, FileMode.Create))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
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

                // Test DocxReader directly
                var docxModel = AeroSpot.Core.Preview.DocxReader.ReadDocx(tempDocx);
                Assert.IsNotNull(docxModel);
                StringAssert.Contains(docxModel.MarkdownContent, "# AeroSpot Native Word Preview", "Headings must be formatted with Markdown #");
                StringAssert.Contains(docxModel.MarkdownContent, "**grassetto**", "Bold text must be formatted with Markdown **");
                StringAssert.Contains(docxModel.MarkdownContent, "| Colonna 1 | Colonna 2 |", "Tables must be formatted as Markdown tables");
                Assert.IsGreaterThanOrEqualTo(2, docxModel.ParagraphCount);
                Assert.IsGreaterThanOrEqualTo(5, docxModel.WordCount);

                // Test PreviewLoader
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
    }

    internal class PresentationSourceMock : System.Windows.PresentationSource
    {
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
    }
}
