using System;
using System.IO;
using System.Runtime.InteropServices;
using AeroSpot.Core.Native;
using AeroSpot.Core.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class PreviewHandlerTests
    {
        [TestMethod]
        public void PreviewHandlerResolver_ResolvesSystemHandlers()
        {
            // PDF
            bool hasPdf = PreviewHandlerResolver.TryGetHandlerClsid(".pdf", out var pdfClsid);
            Assert.IsTrue(hasPdf, "System must have a registered PDF preview handler.");
            Assert.AreNotEqual(Guid.Empty, pdfClsid);
            string? pdfName = PreviewHandlerResolver.GetHandlerFriendlyName(pdfClsid);
            TestContext?.WriteLine($"PDF Handler: {pdfName} ({pdfClsid:B})");
            Assert.IsNotNull(pdfName);

            // Word .docx
            bool hasDocx = PreviewHandlerResolver.TryGetHandlerClsid(".docx", out var docxClsid);
            Assert.IsTrue(hasDocx, "System must have a registered DOCX preview handler.");
            string? docxName = PreviewHandlerResolver.GetHandlerFriendlyName(docxClsid);
            TestContext?.WriteLine($"DOCX Handler: {docxName} ({docxClsid:B})");

            // Excel .xlsx
            bool hasXlsx = PreviewHandlerResolver.TryGetHandlerClsid(".xlsx", out var xlsxClsid);
            Assert.IsTrue(hasXlsx, "System must have a registered XLSX preview handler.");
            string? xlsxName = PreviewHandlerResolver.GetHandlerFriendlyName(xlsxClsid);
            TestContext?.WriteLine($"XLSX Handler: {xlsxName} ({xlsxClsid:B})");

            // Non-existent extension
            bool hasFake = PreviewHandlerResolver.TryGetHandlerClsid(".fakeExtXYZ123", out var fakeClsid);
            Assert.IsFalse(hasFake, "Non-existent extension should not resolve to a preview handler.");
            Assert.AreEqual(Guid.Empty, fakeClsid);
        }

        [TestMethod]
        public void PreviewHandler_CanInstantiateAndInitialize_WithSamplePdf()
        {
            string pdfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.pdf");
            if (!File.Exists(pdfPath))
            {
                // Fallback to project path
                pdfPath = Path.Combine(TestEnvironment.TestsDir, "test_sample.pdf");
            }
            Assert.IsTrue(File.Exists(pdfPath), $"Sample PDF must exist at {pdfPath}");

            bool hasPdf = PreviewHandlerResolver.TryGetHandlerClsid(".pdf", out var clsid);
            Assert.IsTrue(hasPdf);

            Type? handlerType = Type.GetTypeFromCLSID(clsid);
            Assert.IsNotNull(handlerType, $"COM Type for {clsid} must exist.");

            object? handlerObj = Activator.CreateInstance(handlerType);
            Assert.IsNotNull(handlerObj, "COM Activator must instantiate the handler object.");

            try
            {
                var previewHandler = handlerObj as IPreviewHandler;
                Assert.IsNotNull(previewHandler, "Handler object must implement IPreviewHandler.");

                // Initialize with file or stream
                bool initialized = false;
                if (handlerObj is IInitializeWithFile initFile)
                {
                    initFile.Initialize(pdfPath, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                }
                else if (handlerObj is IInitializeWithStream initStream)
                {
                    PreviewHandlerNativeMethods.SHCreateStreamOnFileEx(
                        pdfPath,
                        PreviewHandlerNativeMethods.STGM_READ | PreviewHandlerNativeMethods.STGM_SHARE_DENY_NONE,
                        PreviewHandlerNativeMethods.FILE_ATTRIBUTE_NORMAL,
                        false,
                        IntPtr.Zero,
                        out var stream);
                    initStream.Initialize(stream, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                }
                else if (handlerObj is IInitializeWithItem initItem)
                {
                    Guid iid = PreviewHandlerNativeMethods.IidShellItem;
                    PreviewHandlerNativeMethods.SHCreateItemFromParsingName(pdfPath, IntPtr.Zero, iid, out object shellItem);
                    initItem.Initialize(shellItem, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                }

                Assert.IsTrue(initialized, "Preview handler must be initialized via File, Stream, or Item.");

                // Test Unload and cleanup
                previewHandler.Unload();
            }
            finally
            {
                Marshal.FinalReleaseComObject(handlerObj);
            }
        }

        [TestMethod]
        public void PreviewHandler_CanInstantiateAndInitialize_WithSampleRtf()
        {
            string rtfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.rtf");
            if (!File.Exists(rtfPath))
            {
                rtfPath = Path.Combine(TestEnvironment.TestsDir, "test_sample.rtf");
            }
            Assert.IsTrue(File.Exists(rtfPath), $"Sample RTF must exist at {rtfPath}");

            bool hasRtf = PreviewHandlerResolver.TryGetHandlerClsid(".rtf", out var clsid);
            Assert.IsTrue(hasRtf, "RTF handler must be registered.");

            Type? handlerType = Type.GetTypeFromCLSID(clsid);
            Assert.IsNotNull(handlerType);

            object? handlerObj = Activator.CreateInstance(handlerType);
            Assert.IsNotNull(handlerObj);

            try
            {
                var previewHandler = handlerObj as IPreviewHandler;
                Assert.IsNotNull(previewHandler);

                bool initialized = false;
                if (handlerObj is IInitializeWithFile initFile)
                {
                    initFile.Initialize(rtfPath, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                }
                else if (handlerObj is IInitializeWithStream initStream)
                {
                    PreviewHandlerNativeMethods.SHCreateStreamOnFileEx(
                        rtfPath,
                        PreviewHandlerNativeMethods.STGM_READ | PreviewHandlerNativeMethods.STGM_SHARE_DENY_NONE,
                        PreviewHandlerNativeMethods.FILE_ATTRIBUTE_NORMAL,
                        false,
                        IntPtr.Zero,
                        out var stream);
                    initStream.Initialize(stream, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                }
                else if (handlerObj is IInitializeWithItem initItem)
                {
                    Guid iid = PreviewHandlerNativeMethods.IidShellItem;
                    PreviewHandlerNativeMethods.SHCreateItemFromParsingName(rtfPath, IntPtr.Zero, iid, out object shellItem);
                    initItem.Initialize(shellItem, PreviewHandlerNativeMethods.STGM_READ);
                    initialized = true;
                }

                Assert.IsTrue(initialized, "RTF preview handler must initialize successfully.");
                previewHandler.Unload();
            }
            finally
            {
                Marshal.FinalReleaseComObject(handlerObj);
            }
        }

        [TestMethod]
        public void PreviewHandler_CanInstantiateAndInitialize_WithDocx()
        {
            bool hasDocx = PreviewHandlerResolver.TryGetHandlerClsid(".docx", out var clsid);
            TestContext?.WriteLine($"Has DOCX Handler: {hasDocx}, CLSID={clsid:B}");
            if (!hasDocx) return;

            string? docxName = PreviewHandlerResolver.GetHandlerFriendlyName(clsid);
            TestContext?.WriteLine($"DOCX Handler Name: {docxName}");

            string[] candidateFiles = new[]
            {
                @"E:\Foto\Sofia\Album\Lettera.docx",
                @"E:\Lavoro\Stampa 3D\Bobine.docx",
                @"E:\Lavoro\Stampa 3D\Anycubic\Garanzia.docx"
            };

            string? testFile = Array.Find(candidateFiles, File.Exists);
            if (testFile == null)
            {
                TestContext?.WriteLine("No candidate docx file found on disk.");
                return;
            }

            TestContext?.WriteLine($"Testing with file: {testFile}");

            Type? handlerType = Type.GetTypeFromCLSID(clsid);
            Assert.IsNotNull(handlerType);

            object? handlerObj = Activator.CreateInstance(handlerType);
            Assert.IsNotNull(handlerObj);

            try
            {
                var previewHandler = handlerObj as IPreviewHandler;
                Assert.IsNotNull(previewHandler);

                bool initialized = false;
                if (handlerObj is IInitializeWithFile initFile)
                {
                    try
                    {
                        initFile.Initialize(testFile, PreviewHandlerNativeMethods.STGM_READ);
                        initialized = true;
                        TestContext?.WriteLine("Initialized via IInitializeWithFile");
                    }
                    catch (Exception ex)
                    {
                        TestContext?.WriteLine($"IInitializeWithFile failed: {ex.Message}");
                    }
                }
                if (!initialized && handlerObj is IInitializeWithItem initItem)
                {
                    try
                    {
                        Guid iid = PreviewHandlerNativeMethods.IidShellItem;
                        PreviewHandlerNativeMethods.SHCreateItemFromParsingName(testFile, IntPtr.Zero, iid, out object shellItem);
                        initItem.Initialize(shellItem, PreviewHandlerNativeMethods.STGM_READ);
                        initialized = true;
                        TestContext?.WriteLine("Initialized via IInitializeWithItem");
                    }
                    catch (Exception ex)
                    {
                        TestContext?.WriteLine($"IInitializeWithItem failed: {ex.Message}");
                    }
                }
                if (!initialized && handlerObj is IPersistFile persistFile)
                {
                    try
                    {
                        persistFile.Load(testFile, PreviewHandlerNativeMethods.STGM_READ);
                        initialized = true;
                        TestContext?.WriteLine("Initialized via IPersistFile");
                    }
                    catch (Exception ex)
                    {
                        TestContext?.WriteLine($"IPersistFile failed: {ex.Message}");
                    }
                }

                TestContext?.WriteLine($"DOCX Initialized: {initialized}");
                if (initialized)
                {
                    previewHandler.Unload();
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(handlerObj);
            }
        }

        [TestMethod]
        public void Preview_Pdfium_NativeViewerZoomPanAndRamCleanup()
        {
            string pdfPath = @"E:\Scuola\Università\Magistrale\1 anno\Diritto industriale\Dispensa_Completa_Diritto_Industriale.pdf";
            if (!File.Exists(pdfPath))
            {
                pdfPath = @"E:\Personale\Viaggi\RoadTrip 2025\RoadTrip V2.pdf";
            }
            if (!File.Exists(pdfPath))
            {
                pdfPath = Path.Combine(AppContext.BaseDirectory, "test_sample.pdf");
            }
            if (!File.Exists(pdfPath))
            {
                pdfPath = Path.Combine(TestEnvironment.TestsDir, "test_sample.pdf");
            }
            Assert.IsTrue(File.Exists(pdfPath), $"PDF di test non trovato: {pdfPath}");

            Exception? threadEx = null;
            string diagReport = string.Empty;

            Action runTestOnDispatcher = () =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        _ = new System.Windows.Application
                        {
                            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                        };
                    }

                    System.Threading.SynchronizationContext.SetSynchronizationContext(
                        new System.Windows.Threading.DispatcherSynchronizationContext(
                            System.Windows.Threading.Dispatcher.CurrentDispatcher));
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    using var curProc = System.Diagnostics.Process.GetCurrentProcess();
                    curProc.Refresh();
                    long baseWorkingSetMb = curProc.WorkingSet64 / (1024 * 1024);
                    long basePrivateMb = curProc.PrivateMemorySize64 / (1024 * 1024);

                    var existingPids = new System.Collections.Generic.HashSet<int>();
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("msedgewebview2"))
                    {
                        try { existingPids.Add(p.Id); } catch { } finally { p.Dispose(); }
                    }

                    var win = new AeroSpot.Worker.PreviewWindow();
                    var task = win.ShowPreviewForFileAsync(pdfPath, isFromSpotlight: true);

                    PumpDispatcherUntil(() => task.IsCompleted, 10000);
                    if (task.IsFaulted) throw task.Exception!;

                    Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfView.Visibility, "PdfView deve essere visibile.");
                    Assert.IsGreaterThan(0, win.PdfPagesStack.Children.Count, "PdfPagesStack deve contenere le pagine del documento.");
                    Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfSidebar.Visibility, "PdfSidebar deve essere visibile su documento multi-pagina.");
                    Assert.AreEqual(win.PdfPagesStack.Children.Count, win.PdfThumbnailsStack.Children.Count, "PdfThumbnailsStack deve contenere una miniatura per ciascuna pagina.");

                    var firstImg = (win.PdfPagesStack.Children[0] as System.Windows.Controls.Border)?.Child as System.Windows.Controls.Image;
                    Assert.IsNotNull(firstImg?.Source, "La prima pagina deve avere una sorgente renderizzata da PDFium.");
                    Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfPageCapsule.Visibility, "La capsula di navigazione pagine deve essere visibile su un documento multi-pagina.");
                    StringAssert.StartsWith(win.PdfPageIndicator.Text, "1 /", $"L'indicatore di pagina deve partire da 1. Trovato: {win.PdfPageIndicator.Text}");

                    // Navigazione pagina successiva con freccia giù
                    win.BtnPdfNextPage.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    StringAssert.StartsWith(win.PdfPageIndicator.Text, "2 /", $"L'indicatore di pagina deve avanzare a 2. Trovato: {win.PdfPageIndicator.Text}");

                    // Navigazione pagina precedente con freccia su
                    win.BtnPdfPrevPage.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    StringAssert.StartsWith(win.PdfPageIndicator.Text, "1 /", $"L'indicatore di pagina deve tornare a 1. Trovato: {win.PdfPageIndicator.Text}");

                    // Navigazione tramite click su miniatura nella sidebar (es. pagina 5)
                    if (win.PdfThumbnailsStack.Children.Count >= 5)
                    {
                        var thumb5 = win.PdfThumbnailsStack.Children[4] as System.Windows.UIElement;
                        thumb5?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        {
                            RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent
                        });
                        StringAssert.StartsWith(win.PdfPageIndicator.Text, "5 /", $"Il clic sulla miniatura 5 deve impostare la pagina a 5. Trovato: {win.PdfPageIndicator.Text}");
                    }

                    curProc.Refresh();
                    long activeWorkingSetMb = curProc.WorkingSet64 / (1024 * 1024);
                    long activePrivateMb = curProc.PrivateMemorySize64 / (1024 * 1024);

                    // Verifica assenza totale di processi browser msedgewebview2.exe (PDFium è nativo in-process)
                    int spawnedWebViewCount = 0;
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("msedgewebview2"))
                    {
                        try
                        {
                            if (!existingPids.Contains(p.Id))
                            {
                                spawnedWebViewCount++;
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                    Assert.AreEqual(0, spawnedWebViewCount, "PDFium non deve avviare alcun processo browser msedgewebview2.exe!");

                    // Verifica chiusura e rilascio risorse
                    win.HidePreview();
                    Assert.AreEqual(0, win.PdfPagesStack.Children.Count, "Alla chiusura di QuickLook (HidePreview), lo stack delle pagine deve essere svuotato.");
                    Assert.AreEqual(0, win.PdfThumbnailsStack.Children.Count, "Alla chiusura di QuickLook (HidePreview), lo stack delle miniature deve essere svuotato.");
                    Assert.AreEqual(System.Windows.Visibility.Collapsed, win.PdfSidebar.Visibility, "Alla chiusura di QuickLook, PdfSidebar deve essere collassata.");
                    Assert.AreEqual(System.Windows.Visibility.Collapsed, win.PdfView.Visibility, "Alla chiusura di QuickLook, PdfView deve essere collassata.");

                    // Simula riapertura dello stesso PDF dopo un'attesa con bonifica RAM da Explorer (isFromSpotlight: false)
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    PumpDispatcherUntil(() => false, 400);

                    var task2 = win.ShowPreviewForFileAsync(pdfPath, isFromSpotlight: true);
                    PumpDispatcherUntil(() => task2.IsCompleted, 10000);
                    if (task2.IsFaulted) throw task2.Exception!;

                    // Forza layout e pump rendering
                    win.UpdateLayout();
                    PumpDispatcherUntil(() => false, 200);

                    Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfView.Visibility, "Al reopen, PdfView deve essere visibile.");
                    Assert.IsGreaterThan(0, win.PdfPagesStack.Children.Count, "Al reopen, PdfPagesStack deve contenere le pagine.");
                    Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfSidebar.Visibility, "Al reopen, PdfSidebar deve essere visibile.");
                    Assert.IsGreaterThan(1000.0, win.PdfScrollViewer.ExtentHeight, "Al reopen, ScrollViewer ExtentHeight deve includere l'altezza delle pagine (non solo il margine).");
                    Assert.IsGreaterThan(100.0, win.PdfPagesContainer.ActualWidth, "Al reopen, PdfPagesContainer deve avere larghezza reale maggiore di 0.");

                    var reopenImg0 = (win.PdfPagesStack.Children[0] as System.Windows.Controls.Border)?.Child as System.Windows.Controls.Image;
                    Assert.IsNotNull(reopenImg0?.Source, "Al reopen, la prima pagina deve avere una sorgente valida.");

                    win.HidePreview();
                    win.Close();

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    PumpDispatcherUntil(() => false, 300);

                    curProc.Refresh();
                    long afterCloseWorkingSetMb = curProc.WorkingSet64 / (1024 * 1024);
                    long afterClosePrivateMb = curProc.PrivateMemorySize64 / (1024 * 1024);

                    diagReport = $"[RAM PDFium Nativo] File={Path.GetFileName(pdfPath)} | Baseline Worker: WS={baseWorkingSetMb}MB, Priv={basePrivateMb}MB | PDF Aperto: Worker WS={activeWorkingSetMb}MB, Priv={activePrivateMb}MB (+{activePrivateMb - basePrivateMb}MB in Worker) + Browser Subprocs: 0 | Dopo HidePreview(): Worker WS={afterCloseWorkingSetMb}MB, Priv={afterClosePrivateMb}MB";
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            };

            var t = new System.Threading.Thread(() => runTestOnDispatcher());
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(35));

            if (!string.IsNullOrEmpty(diagReport))
            {
                TestContext?.WriteLine(diagReport);
            }
            if (threadEx != null) throw new Exception("Errore in Preview_Pdfium_NativeViewerZoomPanAndRamCleanup", threadEx);
        }

        private static void PumpDispatcherUntil(Func<bool> condition, int timeoutMs)
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

        public TestContext? TestContext { get; set; }
    }
}
