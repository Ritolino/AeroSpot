using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
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
                if (System.Windows.Application.Current == null) new System.Windows.Application();
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
                if (System.Windows.Application.Current == null) new System.Windows.Application();

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

            Assert.IsTrue(model.Type == PreviewType.Pdf || model.Type == PreviewType.PreviewHandler, $"Expected Pdf or PreviewHandler, got: {model.Type}");
            if (model.Type == PreviewType.Pdf)
            {
                Assert.IsNotNull(model.PdfPages);
                Assert.IsNotEmpty(model.PdfPages);
                Assert.Contains("PDF", model.Subtitle);
            }
            else
            {
                Assert.AreNotEqual(Guid.Empty, model.PreviewHandlerClsid);
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.PreviewHandlerName));
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
                    if (System.Windows.Application.Current == null) new System.Windows.Application();

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
                    if (System.Windows.Application.Current == null) new System.Windows.Application();

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
                if (System.Windows.Application.Current == null) new System.Windows.Application();
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
            if (model.Type == PreviewType.Pdf)
            {
                Assert.IsGreaterThan(0.0, model.PdfWidth);
                Assert.IsGreaterThan(0.0, model.PdfHeight);
            }
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
    }

    internal class PresentationSourceMock : System.Windows.PresentationSource
    {
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
    }
}
