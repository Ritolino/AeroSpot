using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Preview;
using AeroSpot.Core.Localization;

namespace AeroSpot.Worker.Services
{
    public enum PreviewType
    {
        Image,
        Video,
        Audio,
        Text,
        Markdown,
        Pdf,
        Spreadsheet,
        PreviewHandler,
        Stl3D,
        Generic,
        Error
    }

    public sealed class PreviewModel
    {
        public PreviewType Type { get; set; } = PreviewType.Generic;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileSizeText { get; set; } = string.Empty;
        public string DetailsText { get; set; } = string.Empty;
        public string? FooterText { get; set; }
        public ImageSource? Image { get; set; }
        public System.Collections.Generic.List<BitmapSource>? PdfPages { get; set; }
        public int PdfPageCount { get; set; }
        public double PdfWidth { get; set; }
        public double PdfHeight { get; set; }
        public SpreadsheetModel? Spreadsheet { get; set; }
        public StlModel? StlModel { get; set; }
        public string? TextContent { get; set; }
        public bool IsTruncated { get; set; }
        public bool IsFolder { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid PreviewHandlerClsid { get; set; } = Guid.Empty;
        public string? PreviewHandlerName { get; set; }
    }

    public struct FolderSizeResult
    {
        public long TotalBytes;
        public int FileCount;
        public int DirCount;
    }

    public static class PreviewLoader
    {
        private const int MaxTextPreviewBytes = 100 * 1024; // 100 KB max per file di testo

        private static string FormatDate(DateTime dt)
        {
            return LocalizationService.CurrentLanguage == "it"
                ? dt.ToString("dd/MM/yyyy HH:mm")
                : dt.ToString("yyyy-MM-dd HH:mm");
        }

        public static async Task<PreviewModel> LoadAsync(string path)
        {
            var model = new PreviewModel
            {
                FilePath = path,
                Title = Path.GetFileName(path)
            };

            // Gestione speciale per le applicazioni shell:AppsFolder\...
            if (!string.IsNullOrWhiteSpace(path) && path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                string unwrapped = path.Substring("shell:AppsFolder\\".Length);
                if (File.Exists(unwrapped))
                {
                    path = unwrapped;
                    model.FilePath = unwrapped;
                    model.Title = Path.GetFileName(unwrapped);
                }
                else
                {
                    // È un'app UWP o registrata
                    model.Type = PreviewType.Generic;
                    model.Subtitle = LocalizationService.Get("Spotlight_Category_App");
                    model.DetailsText = $"{LocalizationService.Get("Spotlight_Category_App")}\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"{LocalizationService.Get("Spotlight_Category_App")}";
                    return model;
                }
            }

            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                model.Type = PreviewType.Error;
                model.ErrorMessage = LocalizationService.Get("Preview_FileNotFound");
                model.Subtitle = string.Empty;
                return model;
            }

            if (Directory.Exists(path))
            {
                try
                {
                    var dir = new DirectoryInfo(path);
                    model.Type = PreviewType.Generic;
                    model.IsFolder = true;
                    model.Subtitle = LocalizationService.Get("Preview_Folder_Subtitle");
                    // Gerarchia Cartelle: percorso al centro, data di modifica solo nel footer
                    model.DetailsText = $"{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(dir.LastWriteTime)}";
                }
                catch (Exception ex)
                {
                    model.Type = PreviewType.Generic;
                    model.IsFolder = true;
                    model.Subtitle = LocalizationService.Get("Preview_Folder_Subtitle");
                    model.DetailsText = ex.Message;
                }
                return model;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                string ext = fileInfo.Extension.ToLowerInvariant();
                string sizeText = FormatFileSize(fileInfo.Length);
                model.FileSizeText = sizeText;

                // 1. Gestione Immagini
                if (IsImageExtension(ext))
                {
                    // Gestione speciale per file SVG (vettoriali)
                    if (ext == ".svg")
                    {
                        var svgModel = await Task.Run(() =>
                        {
                            try
                            {
                                // Tentativo rendering grafico raster ad alta definizione via IShellItemImageFactory
                                var shellBitmap = IconService.GetImageFactoryBitmap(path, 1200);
                                if (shellBitmap != null)
                                {
                                    model.Type = PreviewType.Image;
                                    model.Image = shellBitmap;
                                    model.Subtitle = $"SVG Vector Graphic • {sizeText}";
                                    model.DetailsText = $"Format: SVG • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    return model;
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn($"Rendering shell SVG non riuscito per {path}: {ex.Message}");
                            }

                            // Fallback: visualizzazione codice sorgente XML dell'SVG
                            try
                            {
                                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                int bytesToRead = (int)Math.Min(stream.Length, MaxTextPreviewBytes);
                                byte[] buffer = new byte[bytesToRead];
                                int read = stream.Read(buffer, 0, bytesToRead);
                                var encoding = DetectEncoding(buffer, read);
                                string text = encoding.GetString(buffer, 0, read);

                                model.Type = PreviewType.Text;
                                model.TextContent = text;
                                model.IsTruncated = stream.Length > MaxTextPreviewBytes;
                                model.Subtitle = $"SVG Vector XML • {sizeText}";
                                model.DetailsText = $"Format: SVG (Source) • Encoding: {encoding.EncodingName} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                return model;
                            }
                            catch (Exception ex)
                            {
                                model.Type = PreviewType.Error;
                                model.ErrorMessage = $"Impossibile visualizzare file SVG: {ex.Message}";
                                return model;
                            }
                        });

                        return svgModel;
                    }

                    return await Task.Run(() =>
                    {
                        try
                        {
                            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                            bitmap.Freeze(); // Consente il passaggio cross-thread sicuro

                            model.Type = PreviewType.Image;
                            model.Image = bitmap;
                            model.Subtitle = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} • {sizeText}";
                            model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            return model;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Decodifica immagine non riuscita per {path}: {ex.Message}");
                            model.Type = PreviewType.Error;
                            model.ErrorMessage = "Impossibile decodificare l'anteprima dell'immagine. Il file potrebbe essere danneggiato o non supportato.";
                            model.Subtitle = $"Error • {sizeText}";
                            return model;
                        }
                    });
                }

                // 2. Gestione Rapida e Reale PDF via Windows.Data.Pdf nativo (vettoriale ad altissima definizione)
                if (ext == ".pdf")
                {
                    try
                    {
                        var pdfModel = await RenderPdfPageAsync(path, fileInfo);
                        if (pdfModel != null)
                        {
                            return pdfModel;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Rendering PDF fallito per {path}: {ex.Message}");
                    }
                }

                // 3. Gestione Video (.mp4, .mkv, .avi, .mov, .wmv, .webm, ecc.)
                if (IsVideoExtension(ext))
                {
                    model.Type = PreviewType.Video;
                    model.Subtitle = $"{LocalizationService.Get("Spotlight_Category_Video")} • {sizeText}";
                    model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 3b. Gestione Audio (.mp3, .wav, .m4a, .aac, .flac, .wma, ecc.)
                if (IsAudioExtension(ext))
                {
                    model.Type = PreviewType.Audio;
                    model.Subtitle = $"{LocalizationService.Get("Spotlight_Category_Audio")} • {sizeText}";
                    model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 3c. Gestione Modelli 3D (.stl)
                if (string.Equals(ext, ".stl", StringComparison.OrdinalIgnoreCase))
                {
                    return await Task.Run(() =>
                    {
                        try
                        {
                            var stl = StlLoader.Load(path);
                            if (stl != null && stl.TriangleCount > 0)
                            {
                                model.Type = PreviewType.Stl3D;
                                model.StlModel = stl;
                                model.Subtitle = $"{LocalizationService.Get("Preview_Stl_Triangles", stl.TriangleCount)} • {sizeText}";
                                model.DetailsText = $"STL 3D • Dim: {stl.SizeX:F1} × {stl.SizeY:F1} × {stl.SizeZ:F1} mm • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                return model;
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Errore parsing STL '{path}': {ex.Message}");
                        }

                        model.Type = PreviewType.Error;
                        model.ErrorMessage = "Impossibile caricare il modello 3D STL (file vuoto o corrotto).";
                        model.Subtitle = $"Error • {sizeText}";
                        return model;
                    });
                }

                // 4. Gestione Testo e Codice
                if (IsTextExtension(ext))
                {
                    return await Task.Run(() =>
                    {
                        try
                        {
                            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            long totalBytes = stream.Length;
                            int bytesToRead = (int)Math.Min(totalBytes, MaxTextPreviewBytes);
                            byte[] buffer = new byte[bytesToRead];
                            int read = stream.Read(buffer, 0, bytesToRead);

                            var encoding = DetectEncoding(buffer, read);
                            string text = encoding.GetString(buffer, 0, read);

                            int lineCount = CountLines(text);
                            bool truncated = totalBytes > MaxTextPreviewBytes;

                            bool isMarkdown = string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase);
                            model.Type = isMarkdown ? PreviewType.Markdown : PreviewType.Text;
                            model.TextContent = text;
                            model.IsTruncated = truncated;
                            string linesLabel = LocalizationService.CurrentLanguage == "it" ? "righe" : "lines";
                            string typeLabel = isMarkdown ? "Markdown" : (LocalizationService.CurrentLanguage == "it" ? "Testo" : "Text");
                            model.Subtitle = truncated
                                ? $"{LocalizationService.Get("Preview_PartialContent", 100)} ({lineCount} {linesLabel})"
                                : $"{sizeText} • {lineCount} {linesLabel}";
                            model.DetailsText = $"{typeLabel} • Encoding: {encoding.EncodingName} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            model.FooterText = $"{lineCount} {linesLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            return model;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Lettura testo non riuscita per {path}: {ex.Message}");
                            model.Type = PreviewType.Error;
                            model.ErrorMessage = $"Impossibile leggere il file di testo: {ex.Message}";
                            model.Subtitle = $"Error • {sizeText}";
                            return model;
                        }
                    });
                }

                // 4b. Gestione Fogli di Calcolo Excel Nativi (.xlsx, .xlsm, .xltx)
                if (ext is ".xlsx" or ".xlsm" or ".xltx")
                {
                    var sheetModel = await Task.Run(() => XlsxReader.ReadXlsx(path));
                    if (sheetModel != null && sheetModel.Sheets.Count > 0)
                    {
                        model.Type = PreviewType.Spreadsheet;
                        model.Spreadsheet = sheetModel;
                        string sheetsLabel = LocalizationService.CurrentLanguage == "it" ? "fogli" : "sheets";
                        model.Subtitle = $"Excel • {sheetModel.Sheets.Count} {sheetsLabel} • {sizeText}";
                        model.DetailsText = $"Format: XLSX • {sheetModel.Sheets.Count} {sheetsLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        model.FooterText = $"{sheetModel.Sheets.Count} {sheetsLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        return model;
                    }
                }

                // 5. Gestione Documenti tramite Preview Handler di sistema (Office, RTF, ecc.)
                if (PreviewHandlerResolver.TryGetHandlerClsid(ext, out Guid clsid))
                {
                    string friendlyName = PreviewHandlerResolver.GetHandlerFriendlyName(clsid) ?? "System Handler";
                    model.Type = PreviewType.PreviewHandler;
                    model.PreviewHandlerClsid = clsid;
                    model.PreviewHandlerName = friendlyName;
                    model.Subtitle = $"{friendlyName} • {sizeText}";
                    model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    model.FooterText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 6. File generico / formato senza visualizzatore registrato / collegamenti / app (.lnk, .exe)
                model.Type = PreviewType.Generic;
                if (ext == ".lnk")
                {
                    // P9: Collegamenti - mostra dimensione solo in alto, senza duplicazioni in basso
                    model.Subtitle = $"{LocalizationService.Get("Spotlight_Category_File")} • {sizeText}";
                    model.DetailsText = $"{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }
                else if (ext == ".exe")
                {
                    model.Subtitle = $"{LocalizationService.Get("Spotlight_Category_App")} • {sizeText}";
                    model.DetailsText = $"{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                model.Subtitle = $"{ext.ToUpperInvariant().TrimStart('.')} • {sizeText}";
                string noViewerMsg = LocalizationService.Get("Preview_NoViewer_Title", ext.ToUpperInvariant());
                model.DetailsText = $"{noViewerMsg}.\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                return model;
            }
            catch (Exception ex)
            {
                model.Type = PreviewType.Error;
                model.ErrorMessage = $"Errore durante il caricamento del file: {ex.Message}";
                return model;
            }
        }

        private static async Task<PreviewModel?> RenderPdfPageAsync(string path, FileInfo fileInfo)
        {
            try
            {
                var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(storageFile);
                if (pdfDoc.PageCount == 0) return null;

                double pdfWidth = 0;
                double pdfHeight = 0;
                try
                {
                    using var firstPage = pdfDoc.GetPage(0);
                    pdfWidth = firstPage.Size.Width;
                    pdfHeight = firstPage.Size.Height;
                }
                catch { }

                var pages = new System.Collections.Generic.List<BitmapSource>();
                // Rendering rapido delle prime pagine per apertura istantanea (< 100ms) ad altissima definizione
                int maxPages = Math.Min((int)pdfDoc.PageCount, 5);

                uint renderWidth = (uint)Math.Clamp(pdfWidth > 0 ? pdfWidth * 2.5 : 1800, 1600, 2200);
                var options = new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = renderWidth
                };

                for (uint i = 0; i < maxPages; i++)
                {
                    using var page = pdfDoc.GetPage(i);
                    using var randomStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(randomStream, options);

                    using var netStream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(randomStream);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = netStream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    pages.Add(bitmap);
                }

                string sizeText = FormatFileSize(fileInfo.Length);
                string pageSuffix = LocalizationService.CurrentLanguage == "it"
                    ? (pdfDoc.PageCount > maxPages ? $" (prime {maxPages} di {pdfDoc.PageCount} pagine)" : $" ({pdfDoc.PageCount} {(pdfDoc.PageCount == 1 ? "pagina" : "pagine")})")
                    : (pdfDoc.PageCount > maxPages ? $" (first {maxPages} of {pdfDoc.PageCount} pages)" : $" ({pdfDoc.PageCount} {(pdfDoc.PageCount == 1 ? "page" : "pages")})");

                string pdfTitle = LocalizationService.Get("Preview_Pdf_Document");

                return new PreviewModel
                {
                    Type = PreviewType.Pdf,
                    PdfPages = pages,
                    PdfPageCount = (int)pdfDoc.PageCount,
                    PdfWidth = pdfWidth,
                    PdfHeight = pdfHeight,
                    FilePath = path,
                    Title = fileInfo.Name,
                    Subtitle = $"{pdfTitle}{pageSuffix} • {sizeText}",
                    DetailsText = $"Format: PDF • {pdfDoc.PageCount} pages • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}",
                    FooterText = $"{pdfDoc.PageCount} {(LocalizationService.CurrentLanguage == "it" ? "pagine" : "pages")} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}"
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"RenderPdfPageAsync error for {path}: {ex.Message}");
                return null;
            }
        }

        private static bool IsImageExtension(string ext)
        {
            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".webp":
                case ".bmp":
                case ".gif":
                case ".ico":
                case ".svg":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsVideoExtension(string ext)
        {
            switch (ext)
            {
                case ".mp4":
                case ".mkv":
                case ".avi":
                case ".mov":
                case ".wmv":
                case ".webm":
                case ".h264":
                case ".h265":
                case ".m4v":
                case ".flv":
                case ".3gp":
                case ".ts":
                case ".mpg":
                case ".mpeg":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsAudioExtension(string ext)
        {
            switch (ext)
            {
                case ".mp3":
                case ".wav":
                case ".m4a":
                case ".aac":
                case ".flac":
                case ".wma":
                case ".ogg":
                case ".mid":
                case ".midi":
                case ".aiff":
                case ".opus":
                    return true;
                default:
                    return false;
            }
        }

        public static async Task<FolderSizeResult?> CalculateDirectorySizeAsync(string dirPath, System.Threading.CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                long totalBytes = 0;
                int fileCount = 0;
                int dirCount = 0;

                var dirsToVisit = new System.Collections.Generic.Stack<string>();
                dirsToVisit.Push(dirPath);

                while (dirsToVisit.Count > 0)
                {
                    if (ct.IsCancellationRequested) return (FolderSizeResult?)null;
                    string currentDir = dirsToVisit.Pop();

                    try
                    {
                        var dirInfo = new DirectoryInfo(currentDir);
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0 && !string.Equals(currentDir, dirPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foreach (var file in dirInfo.EnumerateFiles())
                        {
                            if (ct.IsCancellationRequested) return null;
                            try
                            {
                                totalBytes += file.Length;
                                fileCount++;
                            }
                            catch { }
                        }

                        foreach (var subDir in dirInfo.EnumerateDirectories())
                        {
                            if (ct.IsCancellationRequested) return null;
                            try
                            {
                                if ((subDir.Attributes & FileAttributes.ReparsePoint) == 0)
                                {
                                    dirsToVisit.Push(subDir.FullName);
                                    dirCount++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                return new FolderSizeResult
                {
                    TotalBytes = totalBytes,
                    FileCount = fileCount,
                    DirCount = dirCount
                };
            }, ct);
        }

        private static bool IsTextExtension(string ext)
        {
            switch (ext)
            {
                case ".txt":
                case ".md":
                case ".py":
                case ".json":
                case ".log":
                case ".csv":
                case ".ini":
                case ".cs":
                case ".xml":
                case ".html":
                case ".htm":
                case ".yaml":
                case ".yml":
                case ".toml":
                case ".sql":
                case ".sh":
                case ".bat":
                case ".cmd":
                case ".config":
                case ".cpp":
                case ".c":
                case ".h":
                case ".hpp":
                case ".rs":
                case ".go":
                case ".ts":
                case ".js":
                    return true;
                default:
                    return false;
            }
        }

        private static Encoding DetectEncoding(byte[] buffer, int length)
        {
            if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                return Encoding.UTF8;
            }
            if (length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            {
                return Encoding.Unicode; // UTF-16 LE
            }
            if (length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode; // UTF-16 BE
            }
            return Encoding.UTF8;
        }

        private static int CountLines(string text)
        {
            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') count++;
            }
            return count;
        }

        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }
    }
}
