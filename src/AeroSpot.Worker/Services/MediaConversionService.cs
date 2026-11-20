using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Preview;
using FFMpegCore;
using FFMpegCore.Enums;

namespace AeroSpot.Worker.Services
{
    public static class MediaConversionService
    {
        /// <summary>
        /// Verifica se il formato pixel dell'immagine contiene canale alfa, trasparenza o tavolozza indicizzata.
        /// </summary>
        public static bool HasAlphaOrPalette(BitmapSource frame)
        {
            return frame.Format == PixelFormats.Bgra32 ||
                   frame.Format == PixelFormats.Pbgra32 ||
                   frame.Format == PixelFormats.Rgba64 ||
                   frame.Format == PixelFormats.Prgba64 ||
                   frame.Format == PixelFormats.Rgba128Float ||
                   frame.Format == PixelFormats.Prgba128Float ||
                   frame.Format == PixelFormats.Indexed8 ||
                   frame.Format == PixelFormats.Indexed4 ||
                   frame.Format == PixelFormats.Indexed2 ||
                   frame.Format == PixelFormats.Indexed1 ||
                   frame.Palette != null ||
                   frame.Format.BitsPerPixel == 32;
        }

        /// <summary>
        /// Esegue il compositing alfa a livello di pixel su sfondo bianco puro (#FFFFFF).
        /// Opera direttamente sull'array di byte grezzi Pbgra32, evitando DrawingVisual e RenderTargetBitmap
        /// che causavano ritagli o zoom anomali su immagini ad alto DPI (> 96 DPI).
        /// </summary>
        public static BitmapSource CompositeOverWhite(BitmapSource frame)
        {
            var pbgra = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);

            int width = pbgra.PixelWidth;
            int height = pbgra.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            pbgra.CopyPixels(pixels, stride, 0);

            // In Pbgra32 i canali colore sono premoltiplicati per alfa: c_pre = c * a / 255.
            // La fusione su bianco puro (255) risulta: c_out = c_pre + (255 - a).
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i + 3];
                if (a == 255) continue;
                if (a == 0)
                {
                    pixels[i] = 255;
                    pixels[i + 1] = 255;
                    pixels[i + 2] = 255;
                }
                else
                {
                    int invA = 255 - a;
                    pixels[i] = (byte)Math.Min(255, pixels[i] + invA);
                    pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + invA);
                    pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + invA);
                }
                pixels[i + 3] = 255;
            }

            double dpiX = frame.DpiX > 0 ? frame.DpiX : 96.0;
            double dpiY = frame.DpiY > 0 ? frame.DpiY : 96.0;

            var result = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgr32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Converte un'immagine (PNG, WEBP, BMP, GIF, ecc.) in formato JPEG ad alta qualità.
        /// Se l'immagine ha canale alfa/trasparenza, viene applicato uno sfondo bianco puro senza alcun ritaglio DPI.
        /// </summary>
        public static async Task<string> ConvertImageToJpgAsync(string sourcePath, string? targetPath = null, int quality = 95)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("File sorgente non trovato.", sourcePath);

                string destPath = targetPath ?? GenerateNonConflictingPath(sourcePath, ".jpg");

                using (var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(inStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) throw new InvalidOperationException("Nessun fotogramma trovato nell'immagine.");
                    var frame = decoder.Frames[0];

                    BitmapSource sourceToEncode = frame;
                    if (HasAlphaOrPalette(frame))
                    {
                        sourceToEncode = CompositeOverWhite(frame);
                    }

                    var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 10, 100) };
                    encoder.Frames.Add(BitmapFrame.Create(sourceToEncode));

                    using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(outStream);
                }

                AppLogger.Info($"MediaConversionService: Immagine convertita in JPG: {destPath}");
                return destPath;
            });
        }

        /// <summary>
        /// Converte un'immagine (PNG, JPG, BMP) in un documento PDF a pagina singola conforme allo standard PDF 1.4.
        /// </summary>
        public static Task<string> ConvertImageToPdfAsync(string sourcePath, string? targetPath = null)
        {
            return ConvertImagesToMultiPagePdfAsync(new[] { sourcePath }, targetPath);
        }

        /// <summary>
        /// Codifica un'immagine in stream JPEG ad alta definizione per l'incorporamento nativo nei PDF,
        /// preservando le proporzioni effettive e applicando background bianco per trasparenze.
        /// </summary>
        public static (byte[] JpegBytes, int WidthPx, int HeightPx) EncodeImageForPdf(string sourcePath)
        {
            using var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(inStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidOperationException("Nessun fotogramma trovato nell'immagine.");
            var frame = decoder.Frames[0];

            int pixelWidth = frame.PixelWidth;
            int pixelHeight = frame.PixelHeight;

            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            bool isStandardJpg = (ext == ".jpg" || ext == ".jpeg") &&
                                 (frame.Format == PixelFormats.Bgr24 || frame.Format == PixelFormats.Rgb24);

            if (isStandardJpg)
            {
                byte[] rawBytes = File.ReadAllBytes(sourcePath);
                return (rawBytes, pixelWidth, pixelHeight);
            }

            BitmapSource sourceToEncode = frame;
            if (HasAlphaOrPalette(frame))
            {
                sourceToEncode = CompositeOverWhite(frame);
            }

            using var ms = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            encoder.Frames.Add(BitmapFrame.Create(sourceToEncode));
            encoder.Save(ms);
            return (ms.ToArray(), pixelWidth, pixelHeight);
        }

        /// <summary>
        /// Converte una o più immagini selezionate in un unico file PDF multi-pagina nativo conforme allo standard PDF 1.4.
        /// </summary>
        public static async Task<string> ConvertImagesToMultiPagePdfAsync(IEnumerable<string> imagePaths, string? targetPath = null, IProgress<double>? progress = null)
        {
            var list = imagePaths.Where(File.Exists).ToList();
            if (list.Count == 0) throw new InvalidOperationException("Nessuna immagine valida specificata per la conversione in PDF.");

            string destPath = targetPath ?? (list.Count == 1
                ? GenerateNonConflictingPath(list[0], ".pdf")
                : GenerateNonConflictingPath(list[0], "_album.pdf"));

            return await Task.Run(() =>
            {
                var encodedList = new List<(byte[] JpegBytes, int WidthPx, int HeightPx)>();
                int total = list.Count;

                for (int i = 0; i < total; i++)
                {
                    string imgPath = list[i];
                    var (jpegBytes, w, h) = EncodeImageForPdf(imgPath);
                    encodedList.Add((jpegBytes, w, h));
                    double pct = (double)(i + 1) / total * 100.0;
                    progress?.Report(pct);
                }

                byte[] pdfData = BuildMultiImagePdf(encodedList);
                File.WriteAllBytes(destPath, pdfData);
                AppLogger.Info($"MediaConversionService: {list.Count} immagini convertite con successo in PDF a pagine: {destPath}");
                return destPath;
            });
        }

        /// <summary>
        /// Unisce più documenti PDF in un unico file PDF ordinato.
        /// </summary>
        public static async Task<string> MergePdfsAsync(IEnumerable<string> pdfPaths, string? targetPath = null, IProgress<double>? progress = null)
        {
            var list = pdfPaths.Where(File.Exists).ToList();
            if (list.Count == 0) throw new InvalidOperationException("Nessun file PDF valido specificato per l'unione.");

            string destPath = targetPath ?? GenerateNonConflictingPath(list[0], "_unito.pdf");

            return await Task.Run(() =>
            {
                using var outputDocument = new PdfSharp.Pdf.PdfDocument();
                int totalFiles = list.Count;
                for (int f = 0; f < totalFiles; f++)
                {
                    string pdfFile = list[f];
                    using var inputDoc = PdfSharp.Pdf.IO.PdfReader.Open(pdfFile, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                    int pageCount = inputDoc.PageCount;
                    for (int p = 0; p < pageCount; p++)
                    {
                        outputDocument.AddPage(inputDoc.Pages[p]);
                    }
                    double pct = (double)(f + 1) / totalFiles * 100.0;
                    progress?.Report(pct);
                }

                outputDocument.Save(destPath);
                AppLogger.Info($"MediaConversionService: PDF uniti con successo ({list.Count} file) in: {destPath}");
                return destPath;
            });
        }

        /// <summary>
        /// Converte un'immagine (JPG, WEBP, BMP, GIF, ecc.) in formato PNG ad alta definizione con supporto alla trasparenza.
        /// </summary>
        public static async Task<string> ConvertImageToPngAsync(string sourcePath, string? targetPath = null)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("File sorgente non trovato.", sourcePath);

                string destPath = targetPath ?? GenerateNonConflictingPath(sourcePath, ".png");

                using (var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(inStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) throw new InvalidOperationException("Nessun fotogramma trovato nell'immagine.");
                    var frame = decoder.Frames[0];

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frame));

                    using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(outStream);
                }

                AppLogger.Info($"MediaConversionService: Immagine convertita in PNG: {destPath}");
                return destPath;
            });
        }

        /// <summary>
        /// Converte o estrae la traccia audio da un file multimediale (video o audio) in formato MP3 tramite Windows Media Foundation.
        /// </summary>
        public static async Task<string> ExtractAudioToMp3Async(string sourcePath, string? targetPath = null, IProgress<double>? progress = null)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("File multimediale sorgente non trovato.", sourcePath);

            string destPath = targetPath ?? GenerateNonConflictingPath(sourcePath, ".mp3");

            var sourceFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(sourcePath));
            string destDir = Path.GetDirectoryName(destPath)!;
            string destFileName = Path.GetFileName(destPath);
            var destFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(destDir);
            var targetFile = await destFolder.CreateFileAsync(destFileName, Windows.Storage.CreationCollisionOption.ReplaceExisting);

            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp3(Windows.Media.MediaProperties.AudioEncodingQuality.High);
            var transcoder = new Windows.Media.Transcoding.MediaTranscoder();

            var prepareOp = await transcoder.PrepareFileTranscodeAsync(sourceFile, targetFile, profile);
            if (!prepareOp.CanTranscode)
            {
                throw new InvalidOperationException($"Transcoding non supportato per questo file: {prepareOp.FailureReason}");
            }

            var progressHandler = new Windows.Foundation.AsyncActionProgressHandler<double>((info, p) =>
            {
                progress?.Report(p);
            });

            var transcodeOp = prepareOp.TranscodeAsync();
            transcodeOp.Progress = progressHandler;
            await transcodeOp;

            AppLogger.Info($"MediaConversionService: Audio convertito con successo in MP3: {destPath}");
            return destPath;
        }

        /// <summary>
        /// Converte o estrae la traccia audio da un file multimediale (video o audio) in formato WAV non compresso tramite Windows Media Foundation.
        /// </summary>
        public static async Task<string> ConvertAudioToWavAsync(string sourcePath, string? targetPath = null, IProgress<double>? progress = null)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("File multimediale sorgente non trovato.", sourcePath);

            string destPath = targetPath ?? GenerateNonConflictingPath(sourcePath, ".wav");

            var sourceFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(sourcePath));
            string destDir = Path.GetDirectoryName(destPath)!;
            string destFileName = Path.GetFileName(destPath);
            var destFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(destDir);
            var targetFile = await destFolder.CreateFileAsync(destFileName, Windows.Storage.CreationCollisionOption.ReplaceExisting);

            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.High);
            var transcoder = new Windows.Media.Transcoding.MediaTranscoder();

            var prepareOp = await transcoder.PrepareFileTranscodeAsync(sourceFile, targetFile, profile);
            if (!prepareOp.CanTranscode)
            {
                throw new InvalidOperationException($"Transcoding in WAV non supportato per questo file: {prepareOp.FailureReason}");
            }

            var progressHandler = new Windows.Foundation.AsyncActionProgressHandler<double>((info, p) =>
            {
                progress?.Report(p);
            });

            var transcodeOp = prepareOp.TranscodeAsync();
            transcodeOp.Progress = progressHandler;
            await transcodeOp;

            AppLogger.Info($"MediaConversionService: Audio convertito con successo in WAV: {destPath}");
            return destPath;
        }

        /// <summary>
        /// Converte un file video (MKV, AVI, MOV, WMV, WEBM, ecc.) in formato MP4 standard compatibile tramite FFmpeg.
        /// Se il flusso video è già H.264, viene copiato senza ricodifica (lossless sub-second), altrimenti transcodificato con libx264.
        /// </summary>
        public static async Task<string> ConvertVideoToMp4Async(string sourcePath, string? targetPath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("File video sorgente non trovato.", sourcePath);

            string destPath = targetPath ?? GenerateNonConflictingPath(sourcePath, ".mp4");

            if (!FFmpegLocator.ConfigureGlobalFFOptions())
            {
                throw new FileNotFoundException("Eseguibile FFmpeg non trovato per la conversione video MP4.");
            }

            var meta = await VideoMetadataService.GetMetadataAsync(sourcePath).ConfigureAwait(false);
            var duration = (meta != null && meta.Duration.TotalSeconds > 0) ? meta.Duration : TimeSpan.FromSeconds(60);

            bool isH264 = false;
            try
            {
                var analysis = await FFProbe.AnalyseAsync(sourcePath).ConfigureAwait(false);
                var vCodec = analysis?.PrimaryVideoStream?.CodecName?.ToLowerInvariant() ?? "";
                if (vCodec.Contains("h264") || vCodec.Contains("avc")) isH264 = true;
            }
            catch { }

            bool success = await FFMpegArguments
                .FromFileInput(sourcePath)
                .OutputToFile(destPath, overwrite: true, opt =>
                {
                    if (isH264)
                    {
                        opt.CopyChannel(Channel.Video)
                           .WithAudioCodec(AudioCodec.Aac)
                           .WithAudioBitrate(192)
                           .WithCustomArgument("-map 0:v:0 -map 0:a:0? -sn -threads 0 -ac 2 -movflags +faststart");
                    }
                    else
                    {
                        opt.WithVideoCodec(VideoCodec.LibX264)
                           .WithConstantRateFactor(22)
                           .WithSpeedPreset(Speed.Fast)
                           .WithAudioCodec(AudioCodec.Aac)
                           .WithAudioBitrate(192)
                           .WithCustomArgument("-map 0:v:0 -map 0:a:0? -sn -threads 0 -pix_fmt yuv420p -ac 2 -movflags +faststart");
                    }
                })
                .NotifyOnProgress(pct => progress?.Report(pct), duration)
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            if (!success || !File.Exists(destPath) || new FileInfo(destPath).Length < 1024)
            {
                throw new InvalidOperationException("Conversione in formato MP4 fallita tramite FFmpeg.");
            }

            AppLogger.Info($"MediaConversionService: Video convertito con successo in MP4: {destPath}");
            return destPath;
        }

        private static byte[] BuildMultiImagePdf(IReadOnlyList<(byte[] jpegBytes, int widthPx, int heightPx)> images)
        {
            if (images == null || images.Count == 0) throw new ArgumentException("Nessuna immagine fornita per il PDF.", nameof(images));

            int totalPages = images.Count;
            using var ms = new MemoryStream();
            var offsets = new System.Collections.Generic.List<long>();

            void WriteAscii(string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                ms.Write(b, 0, b.Length);
            }

            WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

            // Obj 1: Catalog
            offsets.Add(ms.Position);
            WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            // Obj 2: Pages (Kids array)
            // Per ogni pagina p (0 .. totalPages - 1):
            // Page Obj: 3 + 3*p
            // Content Obj: 4 + 3*p
            // Image Obj: 5 + 3*p
            var kids = new StringBuilder();
            for (int p = 0; p < totalPages; p++)
            {
                int pageObjId = 3 + 3 * p;
                kids.Append($"{pageObjId} 0 R ");
            }

            offsets.Add(ms.Position);
            WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {totalPages} >>\nendobj\n");

            for (int p = 0; p < totalPages; p++)
            {
                var (jpegBytes, widthPx, heightPx) = images[p];
                int pageObjId = 3 + 3 * p;
                int contentObjId = 4 + 3 * p;
                int imgObjId = 5 + 3 * p;

                double widthPt = Math.Round(widthPx * 72.0 / 96.0, 2);
                double heightPt = Math.Round(heightPx * 72.0 / 96.0, 2);
                string widthStr = widthPt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string heightStr = heightPt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                // Page obj
                offsets.Add(ms.Position);
                WriteAscii($"{pageObjId} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthStr} {heightStr}] /Contents {contentObjId} 0 R /Resources << /XObject << /Im0 {imgObjId} 0 R >> >> >>\nendobj\n");

                // Content stream (Draw image scaled to page)
                offsets.Add(ms.Position);
                string contentOps = $"q {widthStr} 0 0 {heightStr} 0 0 cm /Im0 Do Q\n";
                byte[] contentBytes = Encoding.ASCII.GetBytes(contentOps);
                WriteAscii($"{contentObjId} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                ms.Write(contentBytes, 0, contentBytes.Length);
                WriteAscii("endstream\nendobj\n");

                // Image XObject (JPEG DCTDecode)
                offsets.Add(ms.Position);
                WriteAscii($"{imgObjId} 0 obj\n<< /Type /XObject /Subtype /Image /Width {widthPx} /Height {heightPx} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
                ms.Write(jpegBytes, 0, jpegBytes.Length);
                WriteAscii("\nendstream\nendobj\n");
            }

            // XRef Table
            long xrefOffset = ms.Position;
            WriteAscii($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
            foreach (long off in offsets)
            {
                WriteAscii($"{off:D10} 00000 n \n");
            }

            // Trailer
            WriteAscii($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            return ms.ToArray();
        }

        public static async Task<string> ConvertDocumentToPdfAsync(string docPath, string? destPath = null)
        {
            if (!File.Exists(docPath))
            {
                throw new FileNotFoundException("File di origine non trovato.", docPath);
            }

            destPath ??= GenerateNonConflictingPath(docPath, ".pdf");

            string ext = Path.GetExtension(docPath).ToLowerInvariant();
            string text = ext switch
            {
                ".docx" or ".docm" or ".dotx" => DocxReader.ReadText(docPath),
                ".rtf" => RtfReader.ReadText(docPath),
                ".odt" => DocxReader.ReadOdt(docPath)?.TextContent ?? string.Empty,
                _ => await File.ReadAllTextAsync(docPath).ConfigureAwait(false)
            };

            string title = Path.GetFileNameWithoutExtension(docPath);
            byte[] pdfBytes = BuildTextPdf(text, title);

            await File.WriteAllBytesAsync(destPath, pdfBytes).ConfigureAwait(false);
            AppLogger.Info($"MediaConversionService: Documento convertito con successo in PDF: {destPath}");
            return destPath;
        }

        private static byte[] BuildTextPdf(string text, string title)
        {
            var wrappedLines = new List<string>();
            foreach (var rawLine in text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                if (string.IsNullOrEmpty(rawLine))
                {
                    wrappedLines.Add(string.Empty);
                    continue;
                }

                string line = rawLine;
                while (line.Length > 85)
                {
                    int splitAt = line.LastIndexOf(' ', 85);
                    if (splitAt <= 20) splitAt = 85;
                    wrappedLines.Add(line.Substring(0, splitAt));
                    line = line.Substring(splitAt).TrimStart();
                }
                wrappedLines.Add(line);
            }

            if (wrappedLines.Count == 0) wrappedLines.Add(string.Empty);

            const int linesPerPage = 48;
            int totalPages = Math.Max(1, (int)Math.Ceiling(wrappedLines.Count / (double)linesPerPage));

            using var ms = new MemoryStream();
            var offsets = new List<long>();

            void WriteAscii(string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                ms.Write(b, 0, b.Length);
            }

            WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

            // Obj 1: Catalog
            offsets.Add(ms.Position);
            WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            // Obj 3: Font Helvetica
            // Obj 4: Font Helvetica-Bold
            // Obj 2: Pages (Kids array)
            // Page objects start at obj 5
            // Each page has a Page Obj and a Content Obj:
            // Page i: obj (5 + 2*i), Content i: obj (6 + 2*i)

            var kids = new StringBuilder();
            for (int p = 0; p < totalPages; p++)
            {
                int pageObjId = 5 + 2 * p;
                kids.Append($"{pageObjId} 0 R ");
            }

            // Obj 2: Pages
            offsets.Add(ms.Position);
            WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {totalPages} >>\nendobj\n");

            // Obj 3: Helvetica
            offsets.Add(ms.Position);
            WriteAscii("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

            // Obj 4: Helvetica-Bold
            offsets.Add(ms.Position);
            WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n");

            for (int p = 0; p < totalPages; p++)
            {
                int pageObjId = 5 + 2 * p;
                int contentObjId = 6 + 2 * p;

                // Page obj
                offsets.Add(ms.Position);
                WriteAscii($"{pageObjId} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Contents {contentObjId} 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>\nendobj\n");

                // Content stream
                var sb = new StringBuilder();
                string headerTitle = EscapePdfString(title);
                string pageNum = $"Pagina {p + 1} di {totalPages}";

                // Header
                sb.Append("BT\n/F2 9 Tf\n50 805 Td\n(").Append(headerTitle).Append(") Tj\nET\n");
                sb.Append("BT\n/F1 9 Tf\n480 805 Td\n(").Append(pageNum).Append(") Tj\nET\n");
                // Horizontal rule
                sb.Append("0.5 w\n50 798 m 545 798 l S\n");

                // Body text
                sb.Append("BT\n/F1 10 Tf\n50 780 Td\n15 TL\n");

                int startLine = p * linesPerPage;
                int endLine = Math.Min(wrappedLines.Count, startLine + linesPerPage);

                for (int l = startLine; l < endLine; l++)
                {
                    string escapedLine = EscapePdfString(wrappedLines[l]);
                    if (l == startLine)
                    {
                        sb.Append('(').Append(escapedLine).Append(") Tj\n");
                    }
                    else
                    {
                        sb.Append("T* (").Append(escapedLine).Append(") Tj\n");
                    }
                }
                sb.Append("ET\n");

                byte[] contentBytes = Encoding.ASCII.GetBytes(sb.ToString());

                // Content obj
                offsets.Add(ms.Position);
                WriteAscii($"{contentObjId} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                ms.Write(contentBytes, 0, contentBytes.Length);
                WriteAscii("\nendstream\nendobj\n");
            }

            // XRef Table
            long xrefOffset = ms.Position;
            WriteAscii($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
            foreach (long off in offsets)
            {
                WriteAscii($"{off:D10} 00000 n \n");
            }

            // Trailer
            WriteAscii($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            return ms.ToArray();
        }

        private static string EscapePdfString(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\' || c == '(' || c == ')')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c >= 32 && c <= 126)
                {
                    sb.Append(c);
                }
                else
                {
                    char mapped = c switch
                    {
                        'à' or 'á' => 'a',
                        'è' or 'é' => 'e',
                        'ì' or 'í' => 'i',
                        'ò' or 'ó' => 'o',
                        'ù' or 'ú' => 'u',
                        'À' or 'Á' => 'A',
                        'È' or 'É' => 'E',
                        'Ì' or 'Í' => 'I',
                        'Ò' or 'Ó' => 'O',
                        'Ù' or 'Ú' => 'U',
                        '°' => ' ',
                        '–' or '—' => '-',
                        '“' or '”' => '"',
                        '‘' or '’' => '\'',
                        '«' or '»' => '"',
                        _ => c < 128 ? c : ' '
                    };
                    if (mapped == '\\' || mapped == '(' || mapped == ')') sb.Append('\\').Append(mapped);
                    else sb.Append(mapped);
                }
            }
            return sb.ToString();
        }

        public static string GenerateNonConflictingPath(string sourcePath, string newExtension)
        {
            string dir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string nameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);

            int dotIdx = newExtension.LastIndexOf('.');
            string suffix = dotIdx > 0 ? newExtension.Substring(0, dotIdx) : string.Empty;
            string actualExt = dotIdx >= 0 ? newExtension.Substring(dotIdx) : newExtension;

            string candidate = Path.Combine(dir, nameWithoutExt + suffix + actualExt);

            if (!File.Exists(candidate)) return candidate;

            int count = 1;
            while (true)
            {
                string nextCandidate = Path.Combine(dir, $"{nameWithoutExt}{suffix} ({count}){actualExt}");
                if (!File.Exists(nextCandidate)) return nextCandidate;
                count++;
            }
        }
    }
}
