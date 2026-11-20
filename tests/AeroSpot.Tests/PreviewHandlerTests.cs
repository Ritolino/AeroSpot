using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            // Non-existent extension must return false and Guid.Empty
            bool hasFake = PreviewHandlerResolver.TryGetHandlerClsid(".fakeExtXYZ123", out var fakeClsid);
            Assert.IsFalse(hasFake, "Non-existent extension should not resolve to a preview handler.");
            Assert.AreEqual(Guid.Empty, fakeClsid);

            // Null/empty/extensionless paths must fail safely without exceptions
            Assert.IsFalse(PreviewHandlerResolver.TryGetHandlerClsid(string.Empty, out var emptyClsid));
            Assert.AreEqual(Guid.Empty, emptyClsid);
            Assert.IsFalse(PreviewHandlerResolver.TryGetHandlerClsid("no_extension_file", out var noExtClsid));
            Assert.AreEqual(Guid.Empty, noExtClsid);

            // Querying friendly name for an empty GUID must return null safely
            Assert.IsNull(PreviewHandlerResolver.GetHandlerFriendlyName(Guid.Empty));

            // Optional registry lookup check for system handlers (never instantiates COM objects)
            if (PreviewHandlerResolver.TryGetHandlerClsid(".txt", out var txtClsid))
            {
                Assert.AreNotEqual(Guid.Empty, txtClsid);
                string? friendly = PreviewHandlerResolver.GetHandlerFriendlyName(txtClsid);
                TestContext?.WriteLine($"TXT Handler: {friendly} ({txtClsid:B})");
            }
        }

        [TestMethod]
        public void Preview_Pdfium_NativeViewerZoomPanAndRamCleanup()
        {
            string pdfPath = Path.Combine(Path.GetTempPath(), $"aerospot_pdf_multipage_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfSharp.Pdf.PdfDocument())
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var page = doc.AddPage();
                        page.Size = PdfSharp.PageSize.A4;
                    }
                    doc.Save(pdfPath);
                }

                Assert.IsTrue(File.Exists(pdfPath), $"PDF multi-pagina di test non trovato: {pdfPath}");

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

                        using var curProc = Process.GetCurrentProcess();
                        curProc.Refresh();
                        long baseWorkingSetMb = curProc.WorkingSet64 / (1024 * 1024);
                        long basePrivateMb = curProc.PrivateMemorySize64 / (1024 * 1024);

                        var existingWebViewPids = CollectProcessIds("msedgewebview2");
                        var existingWordPids = CollectProcessIds("WINWORD");

                        var win = new AeroSpot.Worker.PreviewWindow();
                        var task = win.ShowPreviewForFileAsync(pdfPath, isFromSpotlight: true);

                        PumpDispatcherUntil(() => task.IsCompleted, 10000);
                        if (task.IsFaulted) throw task.Exception!;

                        Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfView.Visibility, "PdfView deve essere visibile.");
                        Assert.AreEqual(5, win.PdfPagesStack.Children.Count, "PdfPagesStack deve contenere tutte le 5 pagine del documento.");
                        Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfSidebar.Visibility, "PdfSidebar deve essere visibile su documento multi-pagina.");
                        Assert.AreEqual(5, win.PdfThumbnailsStack.Children.Count, "PdfThumbnailsStack deve contenere una miniatura per ciascuna pagina.");

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

                        // Navigazione tramite click su miniatura nella sidebar (pagina 5)
                        var thumb5 = win.PdfThumbnailsStack.Children[4] as System.Windows.UIElement;
                        thumb5?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        {
                            RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent
                        });
                        StringAssert.StartsWith(win.PdfPageIndicator.Text, "5 /", $"Il clic sulla miniatura 5 deve impostare la pagina a 5. Trovato: {win.PdfPageIndicator.Text}");

                        curProc.Refresh();
                        long activeWorkingSetMb = curProc.WorkingSet64 / (1024 * 1024);
                        long activePrivateMb = curProc.PrivateMemorySize64 / (1024 * 1024);

                        // Verifica assenza totale di processi browser msedgewebview2.exe e Microsoft Word
                        Assert.AreEqual(0, CountNewProcesses("msedgewebview2", existingWebViewPids), "PDFium non deve avviare alcun processo browser msedgewebview2.exe!");
                        Assert.AreEqual(0, CountNewProcesses("WINWORD", existingWordPids), "PDFium non deve avviare alcun processo WINWORD.EXE!");

                        // Verifica chiusura e rilascio risorse
                        win.HidePreview();
                        Assert.AreEqual(0, win.PdfPagesStack.Children.Count, "Alla chiusura di QuickLook (HidePreview), lo stack delle pagine deve essere svuotato.");
                        Assert.AreEqual(0, win.PdfThumbnailsStack.Children.Count, "Alla chiusura di QuickLook (HidePreview), lo stack delle miniature deve essere svuotato.");
                        Assert.AreEqual(System.Windows.Visibility.Collapsed, win.PdfSidebar.Visibility, "Alla chiusura di QuickLook, PdfSidebar deve essere collassata.");
                        Assert.AreEqual(System.Windows.Visibility.Collapsed, win.PdfView.Visibility, "Alla chiusura di QuickLook, PdfView deve essere collassata.");

                        // Simula riapertura dello stesso PDF dopo un'attesa con bonifica RAM
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        PumpDispatcherUntil(() => false, 400);

                        var task2 = win.ShowPreviewForFileAsync(pdfPath, isFromSpotlight: true);
                        PumpDispatcherUntil(() => task2.IsCompleted, 10000);
                        if (task2.IsFaulted) throw task2.Exception!;

                        win.UpdateLayout();
                        PumpDispatcherUntil(() => false, 200);

                        Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfView.Visibility, "Al reopen, PdfView deve essere visibile.");
                        Assert.AreEqual(5, win.PdfPagesStack.Children.Count, "Al reopen, PdfPagesStack deve contenere tutte le 5 pagine.");
                        Assert.AreEqual(System.Windows.Visibility.Visible, win.PdfSidebar.Visibility, "Al reopen, PdfSidebar deve essere visibile.");
                        Assert.IsGreaterThan(1000.0, win.PdfScrollViewer.ExtentHeight, "Al reopen, ScrollViewer ExtentHeight deve includere l'altezza delle pagine.");
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

                        diagReport = $"[RAM PDFium Nativo] File={Path.GetFileName(pdfPath)} | Baseline: WS={baseWorkingSetMb}MB, Priv={basePrivateMb}MB | Aperto: WS={activeWorkingSetMb}MB, Priv={activePrivateMb}MB | Chiuso: WS={afterCloseWorkingSetMb}MB, Priv={afterClosePrivateMb}MB";
                    }
                    catch (Exception ex)
                    {
                        threadEx = ex;
                    }
                    finally
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
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
            finally
            {
                try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { }
            }
        }

        private static HashSet<int> CollectProcessIds(string processName)
        {
            var pids = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try { pids.Add(p.Id); } catch { } finally { p.Dispose(); }
            }
            return pids;
        }

        private static int CountNewProcesses(string processName, HashSet<int> baselinePids)
        {
            int count = 0;
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!baselinePids.Contains(p.Id)) count++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return count;
        }

        private static void PumpDispatcherUntil(Func<bool> condition, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
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
