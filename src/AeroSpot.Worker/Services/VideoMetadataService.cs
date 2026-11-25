using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using AeroSpot.Core.Logging;
using FFMpegCore;

namespace AeroSpot.Worker.Services
{
    public sealed class VideoMetadata
    {
        public uint Width { get; set; }
        public uint Height { get; set; }
        public TimeSpan Duration { get; set; }
        public int RotationDegrees { get; set; } // 0, 90, 180, 270
        public string? VideoCodec { get; set; }
        public string AudioCodec { get; set; } = string.Empty;
        public int AudioChannels { get; set; } = 2;
        public uint SampleRate { get; set; } = 48000;
        public string AudioTrackName { get; set; } = string.Empty;
        public bool IsBitstreamAudio { get; set; }
    }

    public static class VideoMetadataService
    {
        public static async Task<VideoMetadata?> GetMetadataAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            // 1. Prova prima con FFProbe per un'analisi esaustiva e affidabile delle tracce audio
            try
            {
                if (FFmpegLocator.ConfigureGlobalFFOptions())
                {
                    var analysis = await FFProbe.AnalyseAsync(filePath).ConfigureAwait(false);
                    if (analysis != null)
                    {
                        var vStream = analysis.PrimaryVideoStream;
                        var aStream = analysis.AudioStreams.FirstOrDefault();

                        uint w = vStream != null ? (uint)Math.Max(0, vStream.Width) : 0;
                        uint h = vStream != null ? (uint)Math.Max(0, vStream.Height) : 0;
                        int rotation = vStream != null ? vStream.Rotation : 0;
                        var duration = analysis.Duration;

                        string? videoCodec = vStream?.CodecName;
                        string codec = aStream?.CodecName ?? string.Empty;
                        int channels = aStream != null ? aStream.Channels : 2;
                        uint sampleRate = aStream != null ? (uint)aStream.SampleRateHz : 48000;
                        string trackName = aStream?.Tags?.FirstOrDefault(t => t.Key.Equals("title", StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

                        bool isBitstream = MediaCompatibilityService.IsBitstreamAudioCodec(codec);

                        return new VideoMetadata
                        {
                            Width = w,
                            Height = h,
                            Duration = duration,
                            RotationDegrees = rotation,
                            VideoCodec = videoCodec,
                            AudioCodec = codec,
                            AudioChannels = channels,
                            SampleRate = sampleRate,
                            AudioTrackName = trackName,
                            IsBitstreamAudio = isBitstream
                        };
                    }
                }
            }
            catch (Exception exFfprobe)
            {
                AppLogger.Debug($"VideoMetadataService: FFProbe non disponibile o fallito per {filePath}: {exFfprobe.Message}");
            }

            // 2. Fallback su Windows StorageFile / WIC Properties native
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
                var vidProp = await file.Properties.GetVideoPropertiesAsync();

                int rotation = vidProp.Orientation switch
                {
                    VideoOrientation.Rotate90 => 90,
                    VideoOrientation.Rotate180 => 180,
                    VideoOrientation.Rotate270 => 270,
                    _ => 0
                };

                return new VideoMetadata
                {
                    Width = vidProp.Width,
                    Height = vidProp.Height,
                    Duration = vidProp.Duration,
                    RotationDegrees = rotation,
                    AudioCodec = string.Empty,
                    AudioChannels = 2,
                    SampleRate = 48000,
                    AudioTrackName = string.Empty,
                    IsBitstreamAudio = false
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"VideoMetadataService: Errore lettura metadati video per {filePath}: {ex.Message}");
                return null;
            }
        }
    }
}
