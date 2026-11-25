using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;
using FFMpegCore;
using FFMpegCore.Enums;

namespace AeroSpot.Worker.Services
{
    public static class FFmpegLocator
    {
        private static string? _cachedExecutablePath;
        private static readonly object _lock = new object();

        public static string? FindExecutable()
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedExecutablePath) && File.Exists(_cachedExecutablePath))
                {
                    return _cachedExecutablePath;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // 1. Cartella binaria ffmpeg/bin adiacente (distribuzione pacchettizzata)
                string p1 = Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe");
                if (File.Exists(p1)) return _cachedExecutablePath = p1;

                // 2. Eseguibile ffmpeg.exe direttamente adiacente
                string p2 = Path.Combine(baseDir, "ffmpeg.exe");
                if (File.Exists(p2)) return _cachedExecutablePath = p2;

                // 3. Struttura cartella Tools/ffmpeg/bin adiacente
                string p3 = Path.Combine(baseDir, "tools", "ffmpeg", "bin", "ffmpeg.exe");
                if (File.Exists(p3)) return _cachedExecutablePath = p3;

                // 4. Cartelle di sviluppo (Debug/Release di AeroSpot.Tools)
                string devRelease = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "AeroSpot.Tools", "bin", "Release", "net10.0-windows10.0.19041.0", "ffmpeg", "bin", "ffmpeg.exe"));
                if (File.Exists(devRelease)) return _cachedExecutablePath = devRelease;

                string devDebug = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "AeroSpot.Tools", "bin", "Debug", "net10.0-windows10.0.19041.0", "ffmpeg", "bin", "ffmpeg.exe"));
                if (File.Exists(devDebug)) return _cachedExecutablePath = devDebug;

                // 5. LocalAppData AeroSpot tools
                string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroSpot", "tools", "ffmpeg", "bin", "ffmpeg.exe");
                if (File.Exists(localApp)) return _cachedExecutablePath = localApp;

                // 6. Cerca nel PATH di sistema
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    foreach (string segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        try
                        {
                            string candidate = Path.Combine(segment.Trim(), "ffmpeg.exe");
                            if (File.Exists(candidate)) return _cachedExecutablePath = candidate;
                        }
                        catch { }
                    }
                }

                return null;
            }
        }

        public static bool ConfigureGlobalFFOptions()
        {
            string? exe = FindExecutable();
            if (string.IsNullOrEmpty(exe)) return false;

            string dir = Path.GetDirectoryName(exe)!;
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = dir });
            return true;
        }
    }

    public static class MediaCompatibilityService
    {
        private static readonly ConcurrentDictionary<string, Task<string>> _inFlightRemuxes = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsBitstreamAudioCodec(string? audioCodec)
        {
            string codec = (audioCodec ?? string.Empty).ToUpperInvariant();
            return codec.Contains("AC3") || codec.Contains("AC-3") ||
                   codec.Contains("EC3") || codec.Contains("E-AC3") ||
                   codec.Contains("DTS") || codec.Contains("TRUEHD") ||
                   codec.Contains("DOLBY");
        }

        public static bool IsRemuxNeeded(bool isBitstream, string? audioCodec, int channels)
        {
            if (isBitstream) return true;
            return IsBitstreamAudioCodec(audioCodec);
        }

        public static bool IsRemuxNeeded(VideoMetadata? meta)
        {
            if (meta == null) return false;
            return IsRemuxNeeded(meta.IsBitstreamAudio, meta.AudioCodec, meta.AudioChannels);
        }

        public static string GetCacheDirectory()
        {
            string dir = Path.Combine(AppDataPath.GetDataDirectory(), "MediaCache");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public static string ComputeCacheKey(string sourcePath, string? audioCodec, int channels)
        {
            var fi = new FileInfo(sourcePath);
            long length = fi.Exists ? fi.Length : 0;
            long ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
            string codec = audioCodec ?? "unknown";

            string descriptor = $"{sourcePath}|{length}|{ticks}|{codec}|{channels}|aac_192k_stereo_faststart";
            byte[] bytes = Encoding.UTF8.GetBytes(descriptor);
            byte[] hash = SHA256.HashData(bytes);

            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString().Substring(0, 32);
        }

        public static string ComputeCacheKey(string sourcePath, VideoMetadata? meta) =>
            ComputeCacheKey(sourcePath, meta?.AudioCodec, meta?.AudioChannels ?? 2);

        public static string GetCacheFilePath(string sourcePath, string? audioCodec, int channels)
        {
            string key = ComputeCacheKey(sourcePath, audioCodec, channels);
            return Path.Combine(GetCacheDirectory(), $"{key}.mp4");
        }

        public static string GetCacheFilePath(string sourcePath, VideoMetadata? meta) =>
            GetCacheFilePath(sourcePath, meta?.AudioCodec, meta?.AudioChannels ?? 2);

        public static bool TryGetCachedCompatibleVideo(string sourcePath, string? audioCodec, int channels, out string? cachedPath)
        {
            cachedPath = null;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return false;

            try
            {
                string target = GetCacheFilePath(sourcePath, audioCodec, channels);
                if (File.Exists(target))
                {
                    var fi = new FileInfo(target);
                    if (fi.Length > 1024)
                    {
                        cachedPath = target;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"TryGetCachedCompatibleVideo: Controllo cache non riuscito per {sourcePath}: {ex.Message}");
            }

            return false;
        }

        public static bool TryGetCachedCompatibleVideo(string sourcePath, VideoMetadata? meta, out string? cachedPath) =>
            TryGetCachedCompatibleVideo(sourcePath, meta?.AudioCodec, meta?.AudioChannels ?? 2, out cachedPath);

        public static async Task<string> EnsureCompatibleVideoAsync(
            string sourcePath,
            string? audioCodec,
            int channels,
            TimeSpan duration,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            bool isIt = LocalizationService.CurrentLanguage == "it";
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(isIt ? "File video originale non trovato." : "Source video file not found.", sourcePath);
            }

            // 1. Controllo cache hit immediato
            if (TryGetCachedCompatibleVideo(sourcePath, audioCodec, channels, out string? cached) && !string.IsNullOrEmpty(cached))
            {
                AppLogger.Info($"MediaCompatibilityService: Cache hit per {sourcePath} -> {cached}");
                return cached;
            }

            string cacheKey = ComputeCacheKey(sourcePath, audioCodec, channels);

            // 2. Protezione da concorrenza: una sola conversione per cacheKey
            return await _inFlightRemuxes.GetOrAdd(cacheKey, key => Task.Run(async () =>
            {
                string cacheDir = GetCacheDirectory();
                string targetPath = Path.Combine(cacheDir, $"{key}.mp4");
                string tempPath = Path.Combine(cacheDir, $"{key}.tmp.mp4");

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceFi = new FileInfo(sourcePath);
                    var drive = new DriveInfo(Path.GetPathRoot(cacheDir) ?? "C:\\");
                    long requiredBytes = (long)(sourceFi.Length * 1.1) + 150_000_000L;
                    if (drive.AvailableFreeSpace < requiredBytes)
                    {
                        long requiredMb = requiredBytes / (1024 * 1024);
                        throw new InvalidOperationException(isIt
                            ? $"Spazio su disco insufficiente sul volume {drive.Name} per completare il remux audio compatibile (richiesti ~{requiredMb} MB)."
                            : $"Insufficient disk space on drive {drive.Name} to complete compatible audio remux (~{requiredMb} MB required).");
                    }

                    if (!FFmpegLocator.ConfigureGlobalFFOptions())
                    {
                        throw new FileNotFoundException(isIt
                            ? "Eseguibile FFmpeg non trovato per il remux compatibile."
                            : "FFmpeg executable not found for compatible remux.");
                    }

                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }

                    var totalDuration = duration.TotalSeconds > 0 ? duration : TimeSpan.FromSeconds(60);

                    AppLogger.Info($"MediaCompatibilityService: Inizio remux audio compatibile per {sourcePath} -> {tempPath}");
                    var sw = Stopwatch.StartNew();

                    bool success = await FFMpegArguments
                        .FromFileInput(sourcePath)
                        .OutputToFile(tempPath, overwrite: true, opt => opt
                            .CopyChannel(Channel.Video)
                            .WithAudioCodec(AudioCodec.Aac)
                            .WithAudioBitrate(192)
                            .WithCustomArgument("-map 0:v:0 -map 0:a:0? -sn -threads 0 -ac 2 -movflags +faststart"))
                        .NotifyOnProgress(pct => progress?.Report(pct), totalDuration)
                        .CancellableThrough(cancellationToken)
                        .ProcessAsynchronously();

                    sw.Stop();

                    if (!success || !File.Exists(tempPath) || new FileInfo(tempPath).Length < 1024)
                    {
                        throw new InvalidOperationException(isIt
                            ? "Il processo FFmpeg non ha generato un file video compatibile valido."
                            : "FFmpeg did not generate a valid compatible video file.");
                    }

                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); } catch { }
                    }
                    File.Move(tempPath, targetPath);

                    AppLogger.Info($"MediaCompatibilityService: Remux completato con successo in {sw.ElapsedMilliseconds} ms ({targetPath}, {new FileInfo(targetPath).Length / (1024 * 1024)} MB)");

                    _ = Task.Run(() => PruneCacheSafely(cacheDir, maxSizeBytes: 8L * 1024 * 1024 * 1024));

                    return targetPath;
                }
                finally
                {
                    _inFlightRemuxes.TryRemove(key, out _);
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }));
        }

        public static Task<string> EnsureCompatibleVideoAsync(
            string sourcePath,
            VideoMetadata? meta,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            EnsureCompatibleVideoAsync(sourcePath, meta?.AudioCodec, meta?.AudioChannels ?? 2, meta?.Duration ?? TimeSpan.Zero, progress, cancellationToken);

        public static async Task<string> RemuxVideoAsync(string sourcePath, string? targetPath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var meta = await VideoMetadataService.GetMetadataAsync(sourcePath);
            string cachedPath = await EnsureCompatibleVideoAsync(sourcePath, meta, progress, cancellationToken);
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                string? destDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                if (!string.Equals(Path.GetFullPath(cachedPath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(cachedPath, targetPath, overwrite: true);
                }
                return targetPath;
            }
            return cachedPath;
        }

        private static void PruneCacheSafely(string cacheDir, long maxSizeBytes)
        {
            try
            {
                if (!Directory.Exists(cacheDir)) return;

                var dirInfo = new DirectoryInfo(cacheDir);
                var files = dirInfo.GetFiles("*.mp4");

                long totalBytes = 0;
                foreach (var f in files)
                {
                    totalBytes += f.Length;
                }

                if (totalBytes <= maxSizeBytes) return;

                // Ordina per LastAccessTime ascendente (i file usati meno di recente vengono eliminati per primi)
                Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

                foreach (var f in files)
                {
                    if (totalBytes <= maxSizeBytes * 0.75) break; // Riduzione al 75% della soglia

                    try
                    {
                        long len = f.Length;
                        f.Delete();
                        totalBytes -= len;
                        AppLogger.Debug($"MediaCompatibilityService: Rimosso file obsoleto dalla cache: {f.Name}");
                    }
                    catch (IOException)
                    {
                        // File attualmente in riproduzione/aperto da un processo: non toccare
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"MediaCompatibilityService: Errore durante la pulizia della cache: {ex.Message}");
            }
        }
    }
}
