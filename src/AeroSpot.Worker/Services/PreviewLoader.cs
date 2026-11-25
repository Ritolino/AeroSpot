using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Preview;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Search;

namespace AeroSpot.Worker.Services
{
    public enum PreviewType
    {
        Image,
        Video,
        Audio,
        Text,
        Markdown,
        Html,
        Spreadsheet,
        PreviewHandler,
        Pdf,
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
        public int PdfPageCount { get; set; }
        public double PdfWidth { get; set; }
        public double PdfHeight { get; set; }
        public PdfSession? PdfSession { get; set; }
        public SpreadsheetModel? Spreadsheet { get; set; }
        public StlModel? StlModel { get; set; }
        public string? TextContent { get; set; }
        public bool IsTruncated { get; set; }
        public bool IsFolder { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid PreviewHandlerClsid { get; set; } = Guid.Empty;
        public string? PreviewHandlerName { get; set; }
        public int VideoRotation { get; set; }
        public uint VideoWidth { get; set; }
        public uint VideoHeight { get; set; }
        public TimeSpan VideoDuration { get; set; }
        public string? VideoAudioCodec { get; set; }
        public int VideoAudioChannels { get; set; } = 2;
        public bool IsBitstreamAudio { get; set; }
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

            // Gestione schede Impostazioni (ms-settings: e aerospot:settings:)
            if (!string.IsNullOrWhiteSpace(path) && (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("aerospot:settings", StringComparison.OrdinalIgnoreCase)))
            {
                model.Type = PreviewType.Generic;
                string lookupKey = path.Replace("ms-settings:", "").Replace("aerospot:settings:", "");
                var settingMatch = SettingsSearchCatalog.Search(lookupKey, LocalizationService.CurrentLanguage, 1).FirstOrDefault();
                model.Title = settingMatch != null ? settingMatch.GetTitle(LocalizationService.CurrentLanguage) : LocalizationService.Get("Spotlight_Category_Settings");
                model.Subtitle = LocalizationService.Get("Spotlight_Category_Settings");
                model.DetailsText = settingMatch != null ? settingMatch.GetDescription(LocalizationService.CurrentLanguage) : path;
                model.FooterText = "↵ " + LocalizationService.Get("Preview_Hint_Open");
                return model;
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
                            // 1. Rendering vettoriale nativo ad alta fedeltà tramite SharpVectors
                            try
                            {
                                var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
                                {
                                    IncludeRuntime = false,
                                    TextAsGeometry = false
                                };
                                var reader = new SharpVectors.Converters.FileSvgReader(settings);
                                var drawing = reader.Read(path);
                                if (drawing != null)
                                {
                                    drawing.Freeze();
                                    var drawingImage = new DrawingImage(drawing);
                                    drawingImage.Freeze();

                                    model.Type = PreviewType.Image;
                                    model.Image = drawingImage;
                                    int w = (int)Math.Round(drawingImage.Width);
                                    int h = (int)Math.Round(drawingImage.Height);
                                    model.Subtitle = w > 0 && h > 0 ? $"{w} × {h} • {sizeText} (SVG)" : $"SVG Vector • {sizeText}";
                                    model.DetailsText = $"Format: SVG (Vector) • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    return model;
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn($"Rendering SharpVectors SVG non riuscito per {path}: {ex.Message}");
                            }

                            // 2. Fallback: visualizzazione codice sorgente XML dell'SVG
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
                                model.ErrorMessage = LocalizationService.Get("Preview_Error_Svg", ex.Message);
                                return model;
                            }
                        });

                        return svgModel;
                    }

                    return await Task.Run(() =>
                    {
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            // Per i file RAW fotografici, tenta prima l'estrazione ultra-rapida della preview JPEG incorporata
                            if (IsRawExtension(ext))
                            {
                                if (TryExtractEmbeddedRawJpeg(path, out var rawBmp, out int rw, out int rh) && rawBmp != null)
                                {
                                    sw.Stop();
                                    AppLogger.Debug($"[PreviewDiagnostic] RAW JPEG incorporato caricato in {sw.ElapsedMilliseconds} ms ({rw}x{rh}) da {path}");
                                    model.Type = PreviewType.Image;
                                    model.Image = rawBmp;
                                    model.Subtitle = $"{rw} × {rh} • {sizeText} (RAW)";
                                    model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • Embedded Preview • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    return model;
                                }
                            }

                            int origWidth = 0;
                            int origHeight = 0;
                            Rotation rotation = Rotation.Rotate0;

                            // Riusa un unico FileStream per sonda header EXIF e decodifica ottimizzata (max 2560px)
                            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                            try
                            {
                                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                                if (decoder.Frames.Count > 0)
                                {
                                    var frame = decoder.Frames[0];
                                    origWidth = frame.PixelWidth;
                                    origHeight = frame.PixelHeight;

                                    if (frame.Metadata is BitmapMetadata metadata)
                                    {
                                        object? orientObj = null;
                                        if (metadata.ContainsQuery("System.Photo.Orientation"))
                                        {
                                            orientObj = metadata.GetQuery("System.Photo.Orientation");
                                        }
                                        else if (metadata.ContainsQuery("/app1/ifd/{ushort=274}"))
                                        {
                                            orientObj = metadata.GetQuery("/app1/ifd/{ushort=274}");
                                        }

                                        if (orientObj is ushort orVal)
                                        {
                                            rotation = orVal switch
                                            {
                                                3 => Rotation.Rotate180,
                                                6 => Rotation.Rotate90,
                                                8 => Rotation.Rotate270,
                                                _ => Rotation.Rotate0
                                            };
                                        }
                                    }
                                }
                            }
                            catch { }

                            stream.Position = 0;
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = stream;

                            const int maxPreviewDimension = 2560;
                            if (origWidth > maxPreviewDimension || origHeight > maxPreviewDimension)
                            {
                                if (origWidth >= origHeight)
                                {
                                    bitmap.DecodePixelWidth = maxPreviewDimension;
                                }
                                else
                                {
                                    bitmap.DecodePixelHeight = maxPreviewDimension;
                                }
                            }

                            if (rotation != Rotation.Rotate0)
                            {
                                bitmap.Rotation = rotation;
                            }

                            bitmap.EndInit();
                            bitmap.Freeze(); // Consente il passaggio cross-thread sicuro

                            sw.Stop();
                            AppLogger.Debug($"[PreviewDiagnostic] Immagine caricata in {sw.ElapsedMilliseconds} ms ({origWidth}x{origHeight} -> {bitmap.PixelWidth}x{bitmap.PixelHeight}) da {path}");

                            model.Type = PreviewType.Image;
                            model.Image = bitmap;
                            int displayW = origWidth > 0 ? origWidth : bitmap.PixelWidth;
                            int displayH = origHeight > 0 ? origHeight : bitmap.PixelHeight;
                            if (rotation == Rotation.Rotate90 || rotation == Rotation.Rotate270)
                            {
                                int temp = displayW;
                                displayW = displayH;
                                displayH = temp;
                            }
                            model.Subtitle = $"{displayW} × {displayH} • {sizeText}";
                            model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            return model;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Decodifica WIC immagine non riuscita per {path}: {ex.Message}");

                            // Gestione speciale a 3 livelli per formati RAW fotografici
                            if (IsRawExtension(ext))
                            {
                                // Livello 2: Estrazione dell'anteprima JPEG incorporata
                                if (TryExtractEmbeddedRawJpeg(path, out var rawBmp, out int rw, out int rh) && rawBmp != null)
                                {
                                    model.Type = PreviewType.Image;
                                    model.Image = rawBmp;
                                    model.Subtitle = $"{rw} × {rh} • {sizeText} (RAW)";
                                    model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • Embedded Preview • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    return model;
                                }

                                // Livello 3: Scheda metadati elegante e chiara
                                model.Type = PreviewType.Generic;
                                model.Subtitle = $"RAW Image ({ext.ToUpperInvariant().TrimStart('.')}) • {sizeText}";
                                model.DetailsText = $"Formato: Foto RAW ({ext.ToUpperInvariant().TrimStart('.')})\nCodec WIC di sistema non disponibile.\n{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                return model;
                            }

                            model.Type = PreviewType.Error;
                            model.ErrorMessage = LocalizationService.Get("Preview_Error_ImageDecode");
                            model.Subtitle = $"Error • {sizeText}";
                            return model;
                        }
                    });
                }

                // 2. Gestione Documenti PDF (Motore nativo Google PDFium ultra-veloce stile QuickLook)
                if (ext == ".pdf")
                {
                    model.Type = PreviewType.Pdf;
                    model.Subtitle = $"PDF Document • {sizeText}";
                    model.DetailsText = $"Format: PDF • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";

                    // Inizializzazione istantanea (10-20ms) tramite Google PDFium (Docnet.Core)
                    try
                    {
                        var session = PdfService.Open(path, defaultScale: 2.0);
                        if (session != null)
                        {
                            model.PdfSession = session;
                            model.PdfPageCount = session.PageCount;
                            model.PdfWidth = session.OriginalWidth;
                            model.PdfHeight = session.OriginalHeight;

                            // Renderizza la prima pagina in ~12-15 ms
                            model.Image = session.RenderPage(0);

                            string pagesLabel = LocalizationService.Get("Preview_Pages_Count");
                            model.Subtitle = $"PDF Document ({session.PageCount} {pagesLabel}) • {sizeText}";
                            model.DetailsText = $"Format: PDF • {session.PageCount} {pagesLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                            model.FooterText = $"{session.PageCount} {pagesLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug($"Inizializzazione PDFium non riuscita per {path}: {ex.Message}");
                    }

                    return model;
                }

                // 3. Gestione Video (.mp4, .mkv, .avi, .mov, .wmv, .webm, ecc.)
                if (IsVideoExtension(ext))
                {
                    model.Type = PreviewType.Video;
                    try
                    {
                        var meta = await VideoMetadataService.GetMetadataAsync(path).ConfigureAwait(false);
                        if (meta != null)
                        {
                            model.VideoRotation = meta.RotationDegrees;
                            model.VideoWidth = meta.Width;
                            model.VideoHeight = meta.Height;
                            model.VideoDuration = meta.Duration;
                            model.VideoAudioCodec = meta.AudioCodec;
                            model.VideoAudioChannels = meta.AudioChannels;
                            model.IsBitstreamAudio = meta.IsBitstreamAudio;
                        }
                    }
                    catch { }

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

                // 3c. Gestione Modelli 3D (.stl, .3mf)
                if (string.Equals(ext, ".stl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".3mf", StringComparison.OrdinalIgnoreCase))
                {
                    return await Task.Run(() =>
                    {
                        try
                        {
                            bool is3Mf = string.Equals(ext, ".3mf", StringComparison.OrdinalIgnoreCase);
                            var stl = is3Mf ? ThreeMfLoader.Load(path) : StlLoader.Load(path);
                            if (stl != null && stl.TriangleCount > 0)
                            {
                                model.Type = PreviewType.Stl3D;
                                model.StlModel = stl;
                                string formatLabel = is3Mf ? "3MF 3D" : "STL 3D";
                                model.Subtitle = $"{LocalizationService.Get("Preview_Stl_Triangles", stl.TriangleCount)} • {sizeText}";
                                model.DetailsText = $"{formatLabel} • Dim: {stl.SizeX:F1} × {stl.SizeY:F1} × {stl.SizeZ:F1} mm • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                return model;
                            }

                            // Fallback per file 3MF: se il parsing della geometria fallisce, tenta l'estrazione della thumbnail generata dallo slicer
                            if (is3Mf)
                            {
                                var thumb = ThreeMfLoader.TryExtractThumbnail(path);
                                if (thumb != null)
                                {
                                    model.Type = PreviewType.Image;
                                    model.Image = thumb;
                                    model.Subtitle = $"3MF Model (Thumbnail) • {sizeText}";
                                    model.DetailsText = $"Format: 3MF • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                                    return model;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Errore parsing 3D '{path}': {ex.Message}");
                        }

                        model.Type = PreviewType.Error;
                        model.ErrorMessage = LocalizationService.Get("Preview_Error_3DModel");
                        model.Subtitle = $"Error • {sizeText}";
                        return model;
                    });
                }

                // 3d. Gestione Documenti Web HTML (.html, .htm, .xhtml) con visualizzatore DirectX nativo
                if (ext is ".html" or ".htm" or ".xhtml")
                {
                    model.Type = PreviewType.Html;
                    model.Subtitle = $"HTML Document • {sizeText}";
                    model.DetailsText = $"Format: HTML • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";

                    try
                    {
                        if (fileInfo.Length < 4 * 1024 * 1024)
                        {
                            model.TextContent = await File.ReadAllTextAsync(path);
                        }
                        else
                        {
                            using var reader = new StreamReader(path);
                            char[] buffer = new char[64 * 1024];
                            int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                            model.TextContent = new string(buffer, 0, read) + "\n\n" + LocalizationService.Get("Preview_Html_Truncated", 64);
                        }
                    }
                    catch (Exception ex)
                    {
                        model.TextContent = $"<!-- {LocalizationService.Get("Preview_Error_TextRead", ex.Message)} -->";
                    }

                    return model;
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
                            string linesLabel = LocalizationService.Get("Preview_Lines_Count");
                            string typeLabel = isMarkdown ? "Markdown" : LocalizationService.Get("Preview_Type_Text");
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
                            model.ErrorMessage = LocalizationService.Get("Preview_Error_TextRead", ex.Message);
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
                        string sheetsLabel = LocalizationService.Get("Preview_Sheets_Count");
                        model.Subtitle = $"Excel • {sheetModel.Sheets.Count} {sheetsLabel} • {sizeText}";
                        model.DetailsText = $"Format: XLSX • {sheetModel.Sheets.Count} {sheetsLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        model.FooterText = $"{sheetModel.Sheets.Count} {sheetsLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        return model;
                    }

                    // Fallback sicuro: non proseguire mai verso COM PreviewHandler per file Excel vuoti/corrotti
                    model.Type = PreviewType.Generic;
                    model.Subtitle = $"Excel Spreadsheet • {sizeText}";
                    model.DetailsText = $"{LocalizationService.Get("Preview_NoViewer_Title", ext.ToUpperInvariant())}\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 4c. Gestione Documenti Word Nativi (.docx, .docm, .dotx) ad alte prestazioni (zero processi Office / aihost.exe)
                if (ext is ".docx" or ".docm" or ".dotx")
                {
                    var docxData = await Task.Run(() => DocxReader.ReadDocx(path));
                    if (docxData != null)
                    {
                        model.Type = PreviewType.Markdown;
                        model.TextContent = !string.IsNullOrWhiteSpace(docxData.MarkdownContent)
                            ? docxData.MarkdownContent
                            : docxData.TextContent;
                        string wordCountLabel = LocalizationService.Get("Preview_Word_Count");
                        string paraCountLabel = LocalizationService.Get("Preview_Para_Count");
                        model.Subtitle = $"Word Document • {sizeText} • {docxData.WordCount} {wordCountLabel}";
                        model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {docxData.ParagraphCount} {paraCountLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        model.FooterText = $"{docxData.ParagraphCount} {paraCountLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        return model;
                    }

                    // Fallback sicuro: non proseguire mai verso COM PreviewHandler per .docx/.docm/.dotx corrotti o protetti
                    model.Type = PreviewType.Generic;
                    model.Subtitle = $"Word Document • {sizeText}";
                    model.DetailsText = $"{LocalizationService.Get("Preview_NoViewer_Title", ext.ToUpperInvariant())}\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 4d. Gestione Documenti RTF Nativi (.rtf) in-process (previene WINWORD.EXE -Embedding)
                if (ext == ".rtf")
                {
                    var rtfData = await Task.Run(() => RtfReader.ReadRtf(path));
                    if (rtfData != null)
                    {
                        model.Type = PreviewType.Markdown;
                        model.TextContent = !string.IsNullOrWhiteSpace(rtfData.MarkdownContent)
                            ? rtfData.MarkdownContent
                            : rtfData.TextContent;
                        string wordCountLabel = LocalizationService.Get("Preview_Word_Count");
                        string paraCountLabel = LocalizationService.Get("Preview_Para_Count");
                        model.Subtitle = $"RTF Document • {sizeText} • {rtfData.WordCount} {wordCountLabel}";
                        model.DetailsText = $"Format: RTF • {rtfData.ParagraphCount} {paraCountLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        model.FooterText = $"{rtfData.ParagraphCount} {paraCountLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        return model;
                    }

                    model.Type = PreviewType.Generic;
                    model.Subtitle = $"RTF Document • {sizeText}";
                    model.DetailsText = $"{LocalizationService.Get("Preview_NoViewer_Title", "RTF")}\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"Format: RTF • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 4e. Gestione Documenti OpenDocument Nativi (.odt) in-process (previene WINWORD.EXE -Embedding)
                if (ext == ".odt")
                {
                    var odtData = await Task.Run(() => DocxReader.ReadOdt(path));
                    if (odtData != null)
                    {
                        model.Type = PreviewType.Markdown;
                        model.TextContent = !string.IsNullOrWhiteSpace(odtData.MarkdownContent)
                            ? odtData.MarkdownContent
                            : odtData.TextContent;
                        string wordCountLabel = LocalizationService.Get("Preview_Word_Count");
                        string paraCountLabel = LocalizationService.Get("Preview_Para_Count");
                        model.Subtitle = $"OpenDocument Text • {sizeText} • {odtData.WordCount} {wordCountLabel}";
                        model.DetailsText = $"Format: ODT • {odtData.ParagraphCount} {paraCountLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        model.FooterText = $"{odtData.ParagraphCount} {paraCountLabel} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                        return model;
                    }

                    model.Type = PreviewType.Generic;
                    model.Subtitle = $"OpenDocument Text • {sizeText}";
                    model.DetailsText = $"{LocalizationService.Get("Preview_NoViewer_Title", "ODT")}\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"Format: ODT • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 5. Gestione esclusione file .ai da PreviewHandler COM difettosi (evita schermata nera)
                if (ext == ".ai")
                {
                    model.Type = PreviewType.Generic;
                    model.Subtitle = $"Adobe Illustrator Artwork • {sizeText}";
                    string aiNoViewerMsg = LocalizationService.Get("Preview_NoViewer_Title", "AI");
                    model.DetailsText = $"{aiNoViewerMsg}.\n{LocalizationService.Get("Preview_Path_Label")}: {path}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 5a. Blocco esplicito formati Office legacy / non nativi per prevenire processi zombie COM
                if (ext is ".doc" or ".dot" or ".xls" or ".ppt" or ".pptx")
                {
                    model.Type = PreviewType.Generic;
                    model.Subtitle = ext switch
                    {
                        ".doc" or ".dot" => $"Word Document • {sizeText}",
                        ".xls" => $"Excel Spreadsheet • {sizeText}",
                        _ => $"PowerPoint Presentation • {sizeText}"
                    };
                    model.DetailsText = $"Format: {ext.ToUpperInvariant().TrimStart('.')} • {LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    model.FooterText = $"{LocalizationService.Get("Preview_Modified_Label")}: {FormatDate(fileInfo.LastWriteTime)}";
                    return model;
                }

                // 5b. Gestione Documenti tramite Preview Handler di sistema (esclusi handler Office in blacklist)
                if (PreviewHandlerResolver.TryGetHandlerClsid(ext, out Guid clsid) &&
                    !PreviewHandlerResolver.IsBlockedHandlerClsid(clsid))
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
                model.ErrorMessage = LocalizationService.Get("Preview_Error_TextRead", ex.Message);
                return model;
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
                case ".arw":
                case ".dng":
                case ".cr2":
                case ".cr3":
                case ".nef":
                case ".orf":
                case ".rw2":
                case ".pef":
                case ".raf":
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryExtractEmbeddedRawJpeg(string path, out BitmapSource? image, out int width, out int height)
        {
            image = null;
            width = 0;
            height = 0;
            byte[]? bytes = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int bytesToRead = (int)Math.Min(stream.Length, 16 * 1024 * 1024);
                if (bytesToRead < 50 * 1024) return false;

                bytes = ArrayPool<byte>.Shared.Rent(bytesToRead);
                int totalRead = stream.Read(bytes, 0, bytesToRead);

                int longestStart = -1;
                int longestLen = 0;

                ReadOnlySpan<byte> soiMarker = [0xFF, 0xD8, 0xFF];
                ReadOnlySpan<byte> eoiMarker = [0xFF, 0xD9];
                ReadOnlySpan<byte> span = bytes.AsSpan(0, totalRead);

                int pos = 0;
                while (pos < totalRead - 4)
                {
                    int relSoi = span.Slice(pos).IndexOf(soiMarker);
                    if (relSoi < 0) break;
                    int i = pos + relSoi;

                    int searchStart = i + 1024;
                    if (searchStart < totalRead - 1)
                    {
                        int relEoi = span.Slice(searchStart).IndexOf(eoiMarker);
                        if (relEoi >= 0)
                        {
                            int j = searchStart + relEoi;
                            int len = (j + 2) - i;
                            if (len > longestLen && len >= 50 * 1024)
                            {
                                longestStart = i;
                                longestLen = len;
                            }
                        }
                    }

                    pos = i + 3;
                }

                if (longestStart >= 0 && longestLen > 0)
                {
                    // Rilevamento orientamento EXIF (prima dal container TIFF del RAW, poi dall'APP1 del JPEG)
                    int orientation = ExtractOrientationFromTiff(bytes, 0, totalRead);
                    if (orientation <= 1)
                    {
                        orientation = ExtractOrientationFromJpegApp1(bytes, longestStart, longestLen);
                    }

                    Rotation rotation = orientation switch
                    {
                        3 => Rotation.Rotate180,
                        6 => Rotation.Rotate90,
                        8 => Rotation.Rotate270,
                        _ => Rotation.Rotate0
                    };

                    using var ms = new MemoryStream(bytes, longestStart, longestLen, writable: false);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    if (rotation != Rotation.Rotate0)
                    {
                        bmp.Rotation = rotation;
                    }
                    bmp.EndInit();
                    bmp.Freeze();

                    width = bmp.PixelWidth;
                    height = bmp.PixelHeight;
                    image = bmp;
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"TryExtractEmbeddedRawJpeg fallito per {path}: {ex.Message}");
            }
            finally
            {
                if (bytes != null)
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
            }
            return false;
        }

        private static int ExtractOrientationFromTiff(byte[] data, int offset, int length)
        {
            try
            {
                if (data == null || offset < 0 || offset + 8 > length) return 1;

                bool isLittleEndian;
                if (data[offset] == 0x49 && data[offset + 1] == 0x49)
                {
                    isLittleEndian = true;
                }
                else if (data[offset] == 0x4D && data[offset + 1] == 0x4D)
                {
                    isLittleEndian = false;
                }
                else
                {
                    return 1;
                }

                ushort ReadUInt16(int pos)
                {
                    if (pos + 2 > length) return 0;
                    return isLittleEndian
                        ? (ushort)(data[pos] | (data[pos + 1] << 8))
                        : (ushort)((data[pos] << 8) | data[pos + 1]);
                }

                uint ReadUInt32(int pos)
                {
                    if (pos + 4 > length) return 0;
                    return isLittleEndian
                        ? (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24))
                        : (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                }

                uint ifdOffset = ReadUInt32(offset + 4);
                if (ifdOffset == 0 || offset + ifdOffset + 2 > length)
                {
                    return 1;
                }

                int ifdPos = (int)(offset + ifdOffset);
                ushort numEntries = ReadUInt16(ifdPos);
                ifdPos += 2;

                for (int i = 0; i < numEntries && ifdPos + 12 <= length; i++, ifdPos += 12)
                {
                    ushort tag = ReadUInt16(ifdPos);
                    if (tag == 0x0112) // Tag Orientation
                    {
                        ushort val = ReadUInt16(ifdPos + 8);
                        if (val >= 1 && val <= 8)
                        {
                            return val;
                        }
                    }
                }
            }
            catch
            {
                // Fallback silenzioso su orientamento standard
            }
            return 1;
        }

        private static int ExtractOrientationFromJpegApp1(byte[] data, int jpegStart, int jpegLen)
        {
            try
            {
                if (data == null || jpegStart < 0 || jpegLen < 16) return 1;
                int limit = Math.Min(data.Length, jpegStart + jpegLen);
                int p = jpegStart;
                if (data[p] != 0xFF || data[p + 1] != 0xD8) return 1;
                p += 2;

                while (p + 4 <= limit)
                {
                    if (data[p] != 0xFF) break;
                    byte marker = data[p + 1];
                    if (marker == 0xDA || marker == 0xD9) break; // SOS o EOI
                    ushort segLen = (ushort)((data[p + 2] << 8) | data[p + 3]);
                    if (segLen < 2) break;

                    if (marker == 0xE1 && p + 10 <= limit)
                    {
                        if (data[p + 4] == 'E' && data[p + 5] == 'x' && data[p + 6] == 'i' && data[p + 7] == 'f' &&
                            data[p + 8] == 0 && data[p + 9] == 0)
                        {
                            int orient = ExtractOrientationFromTiff(data, p + 10, limit);
                            if (orient > 1) return orient;
                        }
                    }

                    p += 2 + segLen;
                }
            }
            catch
            {
                // Fallback silenzioso
            }
            return 1;
        }

        public static bool IsRawExtension(string ext)
        {
            switch (ext?.ToLowerInvariant())
            {
                case ".arw":
                case ".dng":
                case ".cr2":
                case ".cr3":
                case ".nef":
                case ".orf":
                case ".rw2":
                case ".pef":
                case ".raf":
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
