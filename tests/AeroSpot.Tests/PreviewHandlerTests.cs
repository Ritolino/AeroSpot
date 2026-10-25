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

        public TestContext? TestContext { get; set; }
    }
}
