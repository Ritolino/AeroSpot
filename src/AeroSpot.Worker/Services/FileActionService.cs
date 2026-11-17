using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Localization;
using AeroSpot.Worker;

namespace AeroSpot.Worker.Services
{
    public static class FileActionService
    {
        public static void RevealFileInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path))) return;

            try
            {
                AppLogger.Info($"Apertura percorso contenitore in Explorer: {path}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Impossibile aprire percorso per {path}", ex);
            }
        }

        public static async Task ExecuteConvertJpgAsync(string path, AppConfig config)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Converting"), "🖼️");
            try
            {
                string result = await MediaConversionService.ConvertImageToJpgAsync(path);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione in JPG fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteConvertPngAsync(string path, AppConfig config)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Converting"), "🖼️");
            try
            {
                string result = await MediaConversionService.ConvertImageToPngAsync(path);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione in PNG fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteConvertPdfAsync(string path, AppConfig config)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Converting"), "📕");
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string result = (ext is ".md" or ".txt" or ".docx")
                    ? await MediaConversionService.ConvertDocumentToPdfAsync(path)
                    : await MediaConversionService.ConvertImageToPdfAsync(path);

                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione in PDF fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteExtractMp3Async(string path, AppConfig config)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_ExtractingAudio"), "🎵");
            var progress = new Progress<double>(pct =>
            {
                toast.UpdateProgress(pct, $"{pct:0.0}%");
            });

            try
            {
                string result = await MediaConversionService.ExtractAudioToMp3Async(path, progress: progress);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione o estrazione in MP3 fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteConvertWavAsync(string path, AppConfig config)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_ExtractingAudio"), "🎵");
            var progress = new Progress<double>(pct =>
            {
                toast.UpdateProgress(pct, $"{pct:0.0}%");
            });

            try
            {
                string result = await MediaConversionService.ConvertAudioToWavAsync(path, progress: progress);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione in WAV fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteConvertMp4Async(string path, AppConfig config)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Converting"), "🎬");
            var progress = new Progress<double>(pct =>
            {
                toast.UpdateProgress(pct, $"{pct:0.0}%");
            });

            try
            {
                string result = await MediaConversionService.ConvertVideoToMp4Async(path, progress: progress);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione in MP4 fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteMergePdfsAsync(System.Collections.Generic.IEnumerable<string> paths, AppConfig config)
        {
            var list = System.Linq.Enumerable.ToList(paths);
            if (list.Count == 0) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Converting"), "📑");
            var progress = new Progress<double>(pct =>
            {
                toast.UpdateProgress(pct, $"{pct:0.0}%");
            });

            try
            {
                string result = await MediaConversionService.MergePdfsAsync(list, progress: progress);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unione PDF fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static async Task ExecuteImagesToPdfAsync(System.Collections.Generic.IEnumerable<string> paths, AppConfig config)
        {
            var list = System.Linq.Enumerable.ToList(paths);
            if (list.Count == 0) return;

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Converting"), "📕");
            var progress = new Progress<double>(pct =>
            {
                toast.UpdateProgress(pct, $"{pct:0.0}%");
            });

            try
            {
                string result = await MediaConversionService.ConvertImagesToMultiPagePdfAsync(list, progress: progress);
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (config.OpenFolderAfterConversion && File.Exists(result))
                {
                    RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Conversione immagini in PDF multi-pagina fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }

        public static string? GetToolsExecutablePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string adjacent = Path.Combine(baseDir, "AeroSpot.Tools.exe");
            if (File.Exists(adjacent)) return adjacent;

            // In ambiente di sviluppo, cerca nella cartella bin del progetto AeroSpot.Tools
            string devRelease = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "AeroSpot.Tools", "bin", "Release", "net10.0-windows10.0.19041.0", "AeroSpot.Tools.exe"));
            if (File.Exists(devRelease)) return devRelease;

            string devDebug = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "AeroSpot.Tools", "bin", "Debug", "net10.0-windows10.0.19041.0", "AeroSpot.Tools.exe"));
            if (File.Exists(devDebug)) return devDebug;

            return null;
        }

        public static async Task ExecuteRemoveBgAsync(string path, AppConfig config, string? toolsExe = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            string? exe = toolsExe ?? GetToolsExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                var errToast = FloatingNotificationWindow.Spawn("AeroSpot Tools", "⚠️");
                errToast.SetError("Componente AeroSpot.Tools non trovato", "AeroSpot.Tools.exe mancante.");
                return;
            }

            var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_RemovingBg"), "✨");
            toast.UpdateProgress(-1, "Avvio motore di intelligenza artificiale...");

            try
            {
                string? resultPath = null;
                string? errorMsg = null;
                bool isDownloadingModel = false;

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--remove-bg \"{path}\" --worker-ipc",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (s, ev) =>
                {
                    if (string.IsNullOrWhiteSpace(ev.Data)) return;
                    string line = ev.Data.Trim();

                    if (line.StartsWith("STAGE:"))
                    {
                        string st = line.Substring(6).Trim();
                        isDownloadingModel = string.Equals(st, "DOWNLOADING", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (line.StartsWith("PROGRESS:"))
                    {
                        string valStr = line.Substring(9).Trim();
                        if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p))
                        {
                            string detail = isDownloadingModel
                                ? LocalizationService.Get("Notification_DownloadingModel", p.ToString("0"))
                                : $"{LocalizationService.Get("Notification_RemovingBg")} ({p:0}%)";
                            toast.UpdateProgress(p, detail);
                        }
                    }
                    else if (line.StartsWith("DONE:"))
                    {
                        resultPath = line.Substring(5).Trim();
                        toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(resultPath)), resultPath, resultPath);

                        if (config.OpenFolderAfterConversion && File.Exists(resultPath))
                        {
                            RevealFileInExplorer(resultPath);
                        }
                    }
                    else if (line.StartsWith("ERROR:"))
                    {
                        errorMsg = line.Substring(6).Trim();
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                await proc.WaitForExitAsync();

                if (string.IsNullOrEmpty(resultPath) || !File.Exists(resultPath))
                {
                    string msg = errorMsg ?? "Processo AI terminato senza produrre file.";
                    toast.SetError(LocalizationService.Get("Notification_Error", msg), msg);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Rimozione sfondo AI fallita", ex);
                toast.SetError(LocalizationService.Get("Notification_Error", ex.Message), ex.Message);
            }
        }
    }
}
