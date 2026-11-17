using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AeroSpot.Core.Config;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;
using AeroSpot.Worker;
using AeroSpot.Worker.Services;

namespace AeroSpot.Tools
{
    public static class Program
    {
        [STAThread]
        public static async Task Main(string[] args)
        {
            AppLogger.Initialize("Tools");
            AppLogger.Info($"AeroSpot.Tools avviato con argomenti: {string.Join(" ", args)}");

            var config = AppConfig.Load();
            LocalizationService.Initialize(config.Language);

            string? command = null;
            var rawFiles = new List<string>();
            string? outputFile = null;
            bool isWorkerIpc = Console.IsOutputRedirected;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--remove-bg":
                    case "--convert-jpg":
                    case "--convert-png":
                    case "--convert-pdf":
                    case "--images-to-pdf":
                    case "--merge-pdf":
                    case "--extract-mp3":
                    case "--extract-wav":
                    case "--convert-mp4":
                    case "--remux-compatible":
                        command = arg.ToLowerInvariant();
                        break;

                    case "--output":
                        if (i + 1 < args.Length) outputFile = args[++i];
                        break;

                    case "--timeout":
                        // Mantenuto solo per retrocompatibilità argomenti legacy
                        if (i + 1 < args.Length) i++;
                        break;

                    case "--worker-ipc":
                        isWorkerIpc = true;
                        break;

                    default:
                        if (!arg.StartsWith("--"))
                        {
                            rawFiles.Add(arg);
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(command) || rawFiles.Count == 0)
            {
                Console.WriteLine("ERROR: Specificare un comando valido e almeno un file (--remove-bg, --convert-jpg, --convert-png, --convert-pdf, --images-to-pdf, --merge-pdf, --extract-mp3, --extract-wav, --convert-mp4, --remux-compatible)");
                return;
            }

            // Raccoglie eventuali altri file avviati in parallelo da Windows Explorer (Multi-Select Shell)
            List<string>? files = await FileBatchCollector.CollectFilesAsync(command, rawFiles, isWorkerIpc);
            if (files == null)
            {
                // Questa istanza è un Follower che ha già consegnato il file al Leader: uscita pulita immediata
                return;
            }

            if (files.Count == 0)
            {
                Console.WriteLine("ERROR: Nessun file valido trovato.");
                return;
            }

            if (isWorkerIpc)
            {
                await ExecuteWorkAsync(
                    command,
                    files,
                    outputFile,
                    config,
                    onStage: st => Console.WriteLine($"STAGE: {st}"),
                    onProgress: pct => Console.WriteLine($"PROGRESS: {pct.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"),
                    onDone: (generatedPath, summary) =>
                    {
                        if (!string.IsNullOrEmpty(generatedPath))
                        {
                            Console.WriteLine($"DONE: {generatedPath}");
                        }
                    },
                    onError: msg => Console.WriteLine($"ERROR: {msg}")
                );
                return;
            }

            // Modalità standalone (lanciata da Menu Contestuale Windows Explorer): mostra toast nativo WPF
            var app = Application.Current ?? new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var (initialTitle, initialIcon) = GetCommandBanner(command, files.Count, config.Language);
            var toast = FloatingNotificationWindow.Spawn(initialTitle, initialIcon);
            toast.UpdateProgress(files.Count > 1 ? 0 : -1, files.Count > 1
                ? $"{files.Count} file selezionati..."
                : Path.GetFileName(files[0]));

            toast.Closed += (_, _) =>
            {
                try { app.Dispatcher.InvokeShutdown(); } catch { }
            };

            bool isDownloadingAiModel = false;
            _ = Task.Run(async () =>
            {
                await ExecuteWorkAsync(
                    command,
                    files,
                    outputFile,
                    config,
                    onStage: st =>
                    {
                        isDownloadingAiModel = string.Equals(st, "DOWNLOADING", StringComparison.OrdinalIgnoreCase);
                    },
                    onProgress: pct =>
                    {
                        try
                        {
                            string detail = (command == "--remove-bg" && isDownloadingAiModel)
                                ? LocalizationService.Get("Notification_DownloadingModel", pct.ToString("0"))
                                : $"{pct:0}%";
                            toast.UpdateProgress(pct, detail);
                        }
                        catch { }
                    },
                    onDone: (generatedPath, summary) =>
                    {
                        try
                        {
                            string successTitle = LocalizationService.Get("Notification_Success", Path.GetFileName(generatedPath));
                            toast.SetSuccess(successTitle, summary, generatedPath);

                            if (config.OpenFolderAfterConversion && !string.IsNullOrEmpty(generatedPath) && File.Exists(generatedPath))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = $"/select,\"{generatedPath}\"",
                                    UseShellExecute = true
                                });
                            }
                        }
                        catch { }
                    },
                    onError: msg =>
                    {
                        try
                        {
                            toast.SetError(LocalizationService.Get("Notification_Error", msg), msg);
                        }
                        catch { }
                    }
                );
            });

            app.Run();
        }

        private static (string title, string icon) GetCommandBanner(string command, int count, string language)
        {
            string lang = LocalizationService.ResolveLanguageCode(language);
            bool isIt = lang == "it";
            return command switch
            {
                "--remove-bg" => (LocalizationService.Get("Notification_RemovingBg"), "✨"),
                "--extract-mp3" => (LocalizationService.Get("Notification_ExtractingAudio"), "🎵"),
                "--extract-wav" => (LocalizationService.Get("Notification_ExtractingAudio"), "🎵"),
                "--merge-pdf" => (isIt ? $"Unione {count} PDF in corso..." : $"Merging {count} PDFs...", "📑"),
                "--images-to-pdf" => (isIt ? $"Creazione PDF da {count} immagini..." : $"Creating PDF from {count} images...", "📕"),
                "--convert-mp4" or "--remux-compatible" => (isIt ? "Conversione MP4 in corso..." : "Converting to MP4...", "🎬"),
                _ => (LocalizationService.Get("Notification_Converting"), "🖼️")
            };
        }

        private static async Task ExecuteWorkAsync(
            string command,
            List<string> files,
            string? outputFile,
            AppConfig config,
            Action<double> onProgress,
            Action<string, string> onDone,
            Action<string> onError,
            Action<string>? onStage = null)
        {
            string lang = LocalizationService.ResolveLanguageCode(config.Language);
            bool isIt = lang == "it";

            try
            {
                string? lastGeneratedPath = null;
                int total = files.Count;

                switch (command)
                {
                    case "--remove-bg":
                        {
                            using var remover = new OnnxBackgroundRemover();
                            for (int i = 0; i < total; i++)
                            {
                                int fileIdx = i;
                                string? target = total == 1 ? outputFile : null;
                                lastGeneratedPath = await remover.RemoveBackgroundAsync(files[i], target, pct =>
                                {
                                    double overall = ((fileIdx + pct / 100.0) / total) * 100.0;
                                    onProgress(overall);
                                }, onStage);
                            }
                            string summary = total == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"Sfondo rimosso da {total} immagini" : $"Removed background from {total} images");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--convert-jpg":
                        {
                            var targets = files.Where(f =>
                            {
                                string e = Path.GetExtension(f).ToLowerInvariant();
                                return e != ".jpg" && e != ".jpeg";
                            }).ToList();

                            if (targets.Count == 0) targets = files; // Se forzato esplicitamente
                            for (int i = 0; i < targets.Count; i++)
                            {
                                string? target = targets.Count == 1 ? outputFile : null;
                                lastGeneratedPath = await MediaConversionService.ConvertImageToJpgAsync(targets[i], target);
                                onProgress((double)(i + 1) / targets.Count * 100.0);
                            }
                            string summary = targets.Count == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{targets.Count} immagini convertite in JPG" : $"{targets.Count} images converted to JPG");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--convert-png":
                        {
                            var targets = files.Where(f => Path.GetExtension(f).ToLowerInvariant() != ".png").ToList();
                            if (targets.Count == 0) targets = files;
                            for (int i = 0; i < targets.Count; i++)
                            {
                                string? target = targets.Count == 1 ? outputFile : null;
                                lastGeneratedPath = await MediaConversionService.ConvertImageToPngAsync(targets[i], target);
                                onProgress((double)(i + 1) / targets.Count * 100.0);
                            }
                            string summary = targets.Count == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{targets.Count} immagini convertite in PNG" : $"{targets.Count} images converted to PNG");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--convert-pdf":
                        {
                            for (int i = 0; i < total; i++)
                            {
                                string? target = total == 1 ? outputFile : null;
                                string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                                lastGeneratedPath = (ext is ".docx" or ".txt" or ".md")
                                    ? await MediaConversionService.ConvertDocumentToPdfAsync(files[i], target)
                                    : await MediaConversionService.ConvertImageToPdfAsync(files[i], target);
                                onProgress((double)(i + 1) / total * 100.0);
                            }
                            string summary = total == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{total} file convertiti in PDF" : $"{total} files converted to PDF");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--images-to-pdf":
                        {
                            var prog = new Progress<double>(onProgress);
                            lastGeneratedPath = await MediaConversionService.ConvertImagesToMultiPagePdfAsync(files, outputFile, prog);
                            string summary = isIt
                                ? $"Creato PDF da {total} {(total == 1 ? "immagine" : "immagini")}: {Path.GetFileName(lastGeneratedPath)}"
                                : $"Created PDF from {total} {(total == 1 ? "image" : "images")}: {Path.GetFileName(lastGeneratedPath)}";
                            onDone(lastGeneratedPath, summary);
                            break;
                        }

                    case "--merge-pdf":
                        {
                            if (files.Count < 2)
                            {
                                onError(isIt ? "Seleziona almeno 2 file PDF da unire." : "Select at least 2 PDF files to merge.");
                                return;
                            }
                            var prog = new Progress<double>(onProgress);
                            lastGeneratedPath = await MediaConversionService.MergePdfsAsync(files, outputFile, prog);
                            string summary = isIt
                                ? $"{files.Count} PDF uniti in {Path.GetFileName(lastGeneratedPath)}"
                                : $"Merged {files.Count} PDFs into {Path.GetFileName(lastGeneratedPath)}";
                            onDone(lastGeneratedPath, summary);
                            break;
                        }

                    case "--extract-mp3":
                        {
                            for (int i = 0; i < total; i++)
                            {
                                int fileIdx = i;
                                string? target = total == 1 ? outputFile : null;
                                var prog = new Progress<double>(pct => onProgress(((fileIdx + pct / 100.0) / total) * 100.0));
                                lastGeneratedPath = await MediaConversionService.ExtractAudioToMp3Async(files[i], target, progress: prog);
                            }
                            string summary = total == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{total} file estratti in MP3" : $"{total} files converted to MP3");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--extract-wav":
                        {
                            for (int i = 0; i < total; i++)
                            {
                                int fileIdx = i;
                                string? target = total == 1 ? outputFile : null;
                                var prog = new Progress<double>(pct => onProgress(((fileIdx + pct / 100.0) / total) * 100.0));
                                lastGeneratedPath = await MediaConversionService.ConvertAudioToWavAsync(files[i], target, progress: prog);
                            }
                            string summary = total == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{total} file convertiti in WAV" : $"{total} files converted to WAV");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--convert-mp4":
                        {
                            for (int i = 0; i < total; i++)
                            {
                                int fileIdx = i;
                                string? target = total == 1 ? outputFile : null;
                                var prog = new Progress<double>(pct => onProgress(((fileIdx + pct / 100.0) / total) * 100.0));
                                lastGeneratedPath = await MediaConversionService.ConvertVideoToMp4Async(files[i], target, progress: prog);
                            }
                            string summary = total == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{total} video convertiti in MP4" : $"{total} videos converted to MP4");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }

                    case "--remux-compatible":
                        {
                            for (int i = 0; i < total; i++)
                            {
                                int fileIdx = i;
                                string? target = total == 1 ? outputFile : null;
                                var prog = new Progress<double>(pct => onProgress(((fileIdx + pct / 100.0) / total) * 100.0));
                                lastGeneratedPath = await MediaCompatibilityService.RemuxVideoAsync(files[i], target, progress: prog);
                            }
                            string summary = total == 1
                                ? lastGeneratedPath!
                                : (isIt ? $"{total} video resi compatibili" : $"{total} videos remuxed");
                            onDone(lastGeneratedPath!, summary);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Errore durante l'esecuzione del task in AeroSpot.Tools", ex);
                onError(ex.Message);
            }
        }
    }
}
