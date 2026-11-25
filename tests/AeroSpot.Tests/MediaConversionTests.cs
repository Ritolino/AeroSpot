using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Worker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class MediaConversionTests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"AeroSpot_MediaTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        private string CreateSamplePng(int width = 100, int height = 100)
        {
            string pngPath = Path.Combine(_tempDir, "sample.png");
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.DeepSkyBlue, null, new System.Windows.Rect(0, 0, width, height));
                dc.DrawEllipse(Brushes.Crimson, null, new System.Windows.Point(width / 2.0, height / 2.0), 25, 25);
            }
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = new FileStream(pngPath, FileMode.Create))
            {
                encoder.Save(fs);
            }

            return pngPath;
        }

        [TestMethod]
        public async Task ConvertImageToJpg_ProducesValidJpegFile()
        {
            string srcPng = CreateSamplePng(120, 80);
            string resultJpg = await MediaConversionService.ConvertImageToJpgAsync(srcPng);

            Assert.IsTrue(File.Exists(resultJpg), "Il file JPG convertito deve esistere.");
            Assert.AreEqual(".jpg", Path.GetExtension(resultJpg).ToLowerInvariant());

            // Verifica che il file sia un JPEG decodificabile
            using (var fs = new FileStream(resultJpg, FileMode.Open, FileAccess.Read))
            {
                var decoder = new JpegBitmapDecoder(fs, BitmapCreateOptions.None, BitmapCacheOption.None);
                Assert.HasCount(1, decoder.Frames);
                Assert.AreEqual(120, decoder.Frames[0].PixelWidth);
                Assert.AreEqual(80, decoder.Frames[0].PixelHeight);
            }
        }

        [TestMethod]
        public async Task ConvertImageToPdf_ProducesValidPdfDocument()
        {
            string srcPng = CreateSamplePng(200, 150);
            string resultPdf = await MediaConversionService.ConvertImageToPdfAsync(srcPng);

            Assert.IsTrue(File.Exists(resultPdf), "Il file PDF convertito deve esistere.");
            Assert.AreEqual(".pdf", Path.GetExtension(resultPdf).ToLowerInvariant());

            byte[] bytes = File.ReadAllBytes(resultPdf);
            string header = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);
            StringAssert.StartsWith(header, "%PDF-1.", "Il file deve iniziare con l'intestazione standard %PDF-1.");

            string content = System.Text.Encoding.ASCII.GetString(bytes);
            StringAssert.Contains(content, "/Type /Catalog", "Il PDF deve contenere il dizionario Catalog.");
            StringAssert.Contains(content, "/Type /Pages", "Il PDF deve contenere il dizionario Pages.");
            StringAssert.Contains(content, "/Subtype /Image", "Il PDF deve contenere l'XObject Image.");
            StringAssert.Contains(content, "startxref", "Il PDF deve terminare con la tabella xref.");
        }

        [TestMethod]
        public async Task ConvertImageToPng_ProducesValidPngFile()
        {
            // Convert a sample JPG to PNG
            string srcPng = CreateSamplePng(100, 60);
            string srcJpg = await MediaConversionService.ConvertImageToJpgAsync(srcPng);
            Assert.IsTrue(File.Exists(srcJpg));

            string resultPng = await MediaConversionService.ConvertImageToPngAsync(srcJpg);
            Assert.IsTrue(File.Exists(resultPng), "Il file PNG convertito deve esistere.");
            Assert.AreEqual(".png", Path.GetExtension(resultPng).ToLowerInvariant());

            using (var fs = new FileStream(resultPng, FileMode.Open, FileAccess.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.None, BitmapCacheOption.None);
                Assert.HasCount(1, decoder.Frames);
                Assert.AreEqual(100, decoder.Frames[0].PixelWidth);
                Assert.AreEqual(60, decoder.Frames[0].PixelHeight);
            }
        }

        [TestMethod]
        public async Task ConvertImageToJpg_HighDpiTransparentPng_DoesNotCropBottomRight()
        {
            // Crea un'immagine PNG 600x600 a 288 DPI con sfondo trasparente e un quadrato rosso nell'angolo in basso a destra (500..600)
            int w = 600, h = 600;
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];

            for (int y = 450; y < h; y++)
            {
                for (int x = 450; x < w; x++)
                {
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = 0;   // B
                    pixels[idx + 1] = 0;   // G
                    pixels[idx + 2] = 255; // R
                    pixels[idx + 3] = 255; // A
                }
            }

            var bmp = BitmapSource.Create(w, h, 288.0, 288.0, PixelFormats.Bgra32, null, pixels, stride);
            string highDpiPng = Path.Combine(_tempDir, "highdpi_288.png");
            using (var fs = new FileStream(highDpiPng, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }

            string resultJpg = await MediaConversionService.ConvertImageToJpgAsync(highDpiPng);
            Assert.IsTrue(File.Exists(resultJpg));

            using var outFs = new FileStream(resultJpg, FileMode.Open, FileAccess.Read);
            var dec = new JpegBitmapDecoder(outFs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var outFrame = dec.Frames[0];
            Assert.AreEqual(w, outFrame.PixelWidth);
            Assert.AreEqual(h, outFrame.PixelHeight);

            var outBgr = new FormatConvertedBitmap(outFrame, PixelFormats.Bgr32, null, 0);
            byte[] outPixels = new byte[h * stride];
            outBgr.CopyPixels(outPixels, stride, 0);

            // Pixel (10, 10) trasparente deve essere diventato bianco puro
            int tlIdx = 10 * stride + 10 * 4;
            Assert.IsGreaterThan(245, outPixels[tlIdx + 2], "Sfondo trasparente deve essere fuso su bianco.");
            Assert.IsGreaterThan(245, outPixels[tlIdx + 1]);
            Assert.IsGreaterThan(245, outPixels[tlIdx + 0]);

            // Pixel (550, 550) in basso a destra deve essere ROSSO (non tagliato dal bug DPI!)
            int brIdx = 550 * stride + 550 * 4;
            Assert.IsGreaterThan(200, outPixels[brIdx + 2], $"L'angolo in basso a destra è stato ritagliato! R={outPixels[brIdx + 2]}");
            Assert.IsLessThan(50, outPixels[brIdx + 1], $"L'angolo in basso a destra deve essere rosso puro! G={outPixels[brIdx + 1]}");
            Assert.IsLessThan(50, outPixels[brIdx + 0], $"L'angolo in basso a destra deve essere rosso puro! B={outPixels[brIdx + 0]}");
        }

        [TestMethod]
        public async Task ConvertImagesToMultiPagePdf_CombinesMultipleImagesIntoOrderedPages()
        {
            string img1 = CreateSamplePng(120, 100);
            string img2 = Path.Combine(_tempDir, "sample2.png");
            File.Copy(img1, img2);
            string img3 = Path.Combine(_tempDir, "sample3.png");
            File.Copy(img1, img3);

            string multiPdf = await MediaConversionService.ConvertImagesToMultiPagePdfAsync(new[] { img1, img2, img3 });
            Assert.IsTrue(File.Exists(multiPdf));

            using var doc = PdfSharp.Pdf.IO.PdfReader.Open(multiPdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.AreEqual(3, doc.PageCount, "Il PDF generato da 3 immagini deve avere esattamente 3 pagine.");
        }

        [TestMethod]
        public async Task MergePdfs_CombinesMultiplePdfsPreservingAllPages()
        {
            string img1 = CreateSamplePng(150, 150);
            string img2 = Path.Combine(_tempDir, "sample2.png");
            File.Copy(img1, img2);

            string pdf1 = await MediaConversionService.ConvertImageToPdfAsync(img1);
            string pdf2 = await MediaConversionService.ConvertImagesToMultiPagePdfAsync(new[] { img1, img2 });

            string mergedPdf = await MediaConversionService.MergePdfsAsync(new[] { pdf1, pdf2 });
            Assert.IsTrue(File.Exists(mergedPdf));

            using var doc = PdfSharp.Pdf.IO.PdfReader.Open(mergedPdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.AreEqual(3, doc.PageCount, "L'unione di 1 PDF (1 pagina) + 1 PDF (2 pagine) deve produrre esattamente 3 pagine.");
        }

        [TestMethod]
        public async Task ConvertImageToPdf_HighDpiTransparentPng_PreservesFullContent()
        {
            int w = 600, h = 600;
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];

            for (int y = 450; y < h; y++)
            {
                for (int x = 450; x < w; x++)
                {
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = 0;   // B
                    pixels[idx + 1] = 0;   // G
                    pixels[idx + 2] = 255; // R
                    pixels[idx + 3] = 255; // A
                }
            }

            var bmp = BitmapSource.Create(w, h, 288.0, 288.0, PixelFormats.Bgra32, null, pixels, stride);
            string highDpiPng = Path.Combine(_tempDir, "highdpi_pdf_288.png");
            using (var fs = new FileStream(highDpiPng, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }

            string resultPdf = await MediaConversionService.ConvertImageToPdfAsync(highDpiPng);
            Assert.IsTrue(File.Exists(resultPdf));

            using var session = PdfService.Open(resultPdf, defaultScale: 1.0);
            Assert.IsNotNull(session);
            Assert.AreEqual(1, session.PageCount);

            var rendered = session.RenderPage(0, customScale: 1.0);
            Assert.IsNotNull(rendered);

            var outBgr = new FormatConvertedBitmap(rendered, PixelFormats.Bgr32, null, 0);
            int rw = outBgr.PixelWidth;
            int rh = outBgr.PixelHeight;
            int rStride = rw * 4;
            byte[] rPixels = new byte[rh * rStride];
            outBgr.CopyPixels(rPixels, rStride, 0);

            // Verifica pixel nell'angolo in basso a destra (90% di X, 90% di Y) -> deve essere ROSSO
            int sampleX = (int)(rw * 0.90);
            int sampleY = (int)(rh * 0.90);
            int idxSample = sampleY * rStride + sampleX * 4;
            Assert.IsGreaterThan(180, rPixels[idxSample + 2], $"L'angolo in basso a destra del PDF è stato ritagliato! R={rPixels[idxSample + 2]}");
            Assert.IsLessThan(60, rPixels[idxSample + 1]);
            Assert.IsLessThan(60, rPixels[idxSample + 0]);
        }

        [TestMethod]
        public void ShellContextMenuIntegration_RegisterAndUnregister_SetsMultiSelectPlayer()
        {
            string testRootKey = $@"Software\AeroSpot_Tests_{Guid.NewGuid():N}";
            string testBaseKey = $@"{testRootKey}\Classes\SystemFileAssociations";
            try
            {
                string dirWithTrailingSlash = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                AeroSpot.Core.Native.ShellContextMenuIntegration.Register(dirWithTrailingSlash, "it", testBaseKey);
                Assert.IsTrue(AeroSpot.Core.Native.ShellContextMenuIntegration.IsRegistered(testBaseKey));

                using var imgKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{testBaseKey}\image\shell\AeroSpot");
                Assert.IsNotNull(imgKey);
                Assert.AreEqual("Player", imgKey.GetValue("MultiSelectModel"));
                Assert.AreEqual("AeroSpot", imgKey.GetValue("MUIVerb"));

                using var pdfKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{testBaseKey}\.pdf\shell\AeroSpot\shell\01_MergePdf");
                Assert.IsNotNull(pdfKey);
                Assert.AreEqual("Player", pdfKey.GetValue("MultiSelectModel"));
                Assert.AreEqual("Unisci PDF selezionati", pdfKey.GetValue(""));

                AeroSpot.Core.Native.ShellContextMenuIntegration.Unregister(testBaseKey);
                Assert.IsFalse(AeroSpot.Core.Native.ShellContextMenuIntegration.IsRegistered(testBaseKey));
            }
            finally
            {
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testRootKey, throwOnMissingSubKey: false);
                }
                catch { }
            }
        }
    }
}
