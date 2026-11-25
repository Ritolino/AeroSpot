using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using AeroSpot.Core.Logging;
using AeroSpot.Worker.Services;

namespace AeroSpot.Tools
{
    public sealed class OnnxBackgroundRemover : IDisposable
    {
        private const string DefaultModelUrl = "https://huggingface.co/briaai/RMBG-1.4/resolve/main/onnx/model.onnx";
        private const string FallbackModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        private readonly string _modelPath;
        private InferenceSession? _session;
        private readonly object _lock = new object();

        public OnnxBackgroundRemover(string? modelPath = null)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string modelDir = Path.Combine(localApp, "AeroSpot", "models");
                if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
                _modelPath = Path.Combine(modelDir, "rmbg-1.4.onnx");
            }
            else
            {
                _modelPath = modelPath;
            }
        }

        public async Task EnsureModelDownloadedAsync(Action<double>? progressCallback = null)
        {
            if (File.Exists(_modelPath) && new FileInfo(_modelPath).Length > 1024 * 1024)
            {
                progressCallback?.Invoke(100.0);
                return;
            }

            string tempFile = _modelPath + ".download";
            string downloadUrl = DefaultModelUrl;
            HttpResponseMessage? response = null;

            try
            {
                try
                {
                    response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex)
                {
                    response?.Dispose();
                    response = null;
                    AppLogger.Warn($"Download da {downloadUrl} fallito ({ex.Message}), tentativo con fallback mirror...");
                    downloadUrl = FallbackModelUrl;
                    response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                }

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[64 * 1024];
                    long bytesReadTotal = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        bytesReadTotal += read;
                        if (totalBytes > 0)
                        {
                            double pct = (double)bytesReadTotal / totalBytes * 100.0;
                            progressCallback?.Invoke(pct);
                        }
                    }
                }

                if (File.Exists(_modelPath)) File.Delete(_modelPath);
                File.Move(tempFile, _modelPath);

                AppLogger.Info($"AeroSpot.Tools: Modello AI scaricato con successo in {_modelPath}");
            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
                throw;
            }
            finally
            {
                response?.Dispose();
            }
        }

        public void InitializeSession()
        {
            lock (_lock)
            {
                if (_session != null) return;
                if (!File.Exists(_modelPath)) throw new FileNotFoundException("Modello ONNX non trovato", _modelPath);

                using var options = new SessionOptions
                {
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                try
                {
                    // Accelerazione hardware DirectX 12 DirectML
                    options.AppendExecutionProvider_DML(0);
                    AppLogger.Info("AeroSpot.Tools: DirectML Execution Provider (GPU DirectX 12) inizializzato.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"AeroSpot.Tools: Inizializzazione DirectML non riuscita ({ex.Message}), fallback CPU attivo.");
                    options.AppendExecutionProvider_CPU();
                }

                _session = new InferenceSession(_modelPath, options);
            }
        }

        public async Task<string> RemoveBackgroundAsync(string inputImagePath, string? outputImagePath = null, Action<double>? progressCallback = null, Action<string>? stageCallback = null)
        {
            if (!File.Exists(inputImagePath)) throw new FileNotFoundException("File immagine non trovato", inputImagePath);

            bool isModelCached = File.Exists(_modelPath) && new FileInfo(_modelPath).Length > 1024 * 1024;

            if (!isModelCached)
            {
                stageCallback?.Invoke("DOWNLOADING");
                await EnsureModelDownloadedAsync(pct =>
                {
                    // Se download necessario, mappa il download tra 0% e 60%
                    progressCallback?.Invoke(pct * 0.6);
                });
                stageCallback?.Invoke("INFERENCE");
                progressCallback?.Invoke(65.0);
            }
            else
            {
                stageCallback?.Invoke("INFERENCE");
                progressCallback?.Invoke(15.0);
            }

            InitializeSession();
            progressCallback?.Invoke(isModelCached ? 30.0 : 70.0);

            return await Task.Run(() =>
            {
                // 1. Carica, legge orientamento EXIF e prepara immagine sorgente
                BitmapSource originalSource;
                using (var fs = new FileStream(inputImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    (originalSource, _) = MediaConversionService.ApplyExifOrientation(decoder.Frames[0]);
                }

                int origW = originalSource.PixelWidth;
                int origH = originalSource.PixelHeight;

                progressCallback?.Invoke(isModelCached ? 45.0 : 78.0);

                // 2. Determina risoluzione attesa dal modello (1024x1024 per RMBG-1.4 o 320x320 per U2Netp)
                int targetSize = 1024;
                if (_session != null && _session.InputMetadata.Count > 0)
                {
                    foreach (var meta in _session.InputMetadata.Values)
                    {
                        if (meta.Dimensions.Length >= 4 && meta.Dimensions[2] > 0)
                        {
                            targetSize = meta.Dimensions[2];
                            break;
                        }
                    }
                }

                // 3. Scala a dimensione target per inferenza
                var resized = new TransformedBitmap(originalSource, new ScaleTransform((double)targetSize / origW, (double)targetSize / origH));
                var formatConverted = new FormatConvertedBitmap(resized, PixelFormats.Rgb24, null, 0);

                int stride = targetSize * 3;
                byte[] rgbPixels = new byte[stride * targetSize];
                formatConverted.CopyPixels(rgbPixels, stride, 0);

                // 4. Prepara Tensor [1, 3, targetSize, targetSize] con accesso lineare su Buffer.Span
                bool isU2Net = targetSize == 320 || Path.GetFileName(_modelPath).Contains("u2net", StringComparison.OrdinalIgnoreCase);
                float meanR = isU2Net ? 0.485f : 0.5f;
                float meanG = isU2Net ? 0.456f : 0.5f;
                float meanB = isU2Net ? 0.406f : 0.5f;
                float invStdR = isU2Net ? (1.0f / 0.229f) : 1.0f;
                float invStdG = isU2Net ? (1.0f / 0.224f) : 1.0f;
                float invStdB = isU2Net ? (1.0f / 0.225f) : 1.0f;
                const float inv255 = 1.0f / 255.0f;

                var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
                Span<float> tensorSpan = tensor.Buffer.Span;
                int planeSize = targetSize * targetSize;
                int greenOffset = planeSize;
                int blueOffset = planeSize * 2;

                for (int i = 0; i < planeSize; i++)
                {
                    int srcIdx = i * 3;
                    tensorSpan[i] = (rgbPixels[srcIdx] * inv255 - meanR) * invStdR;
                    tensorSpan[greenOffset + i] = (rgbPixels[srcIdx + 1] * inv255 - meanG) * invStdG;
                    tensorSpan[blueOffset + i] = (rgbPixels[srcIdx + 2] * inv255 - meanB) * invStdB;
                }

                progressCallback?.Invoke(isModelCached ? 65.0 : 85.0);

                // 5. Esegui Inferenza ONNX (DirectML GPU)
                string inputName = "input";
                if (_session != null && _session.InputNames.Count > 0)
                {
                    inputName = _session.InputNames[0];
                }

                var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
                using var results = _session!.Run(inputs);

                var outputTensor = results[0].AsTensor<float>();
                progressCallback?.Invoke(isModelCached ? 85.0 : 92.0);

                // 6. Crea maschera Alpha e scala a dimensioni originali
                int outH = outputTensor.Dimensions[2];
                int outW = outputTensor.Dimensions[3];
                int outLen = outW * outH;
                byte[] maskBytes = new byte[outLen];

                if (outputTensor is DenseTensor<float> denseOut)
                {
                    ReadOnlySpan<float> outSpan = denseOut.Buffer.Span;
                    for (int i = 0; i < outLen; i++)
                    {
                        float val = Math.Clamp(outSpan[i], 0.0f, 1.0f);
                        maskBytes[i] = (byte)(val * 255.0f);
                    }
                }
                else
                {
                    for (int y = 0; y < outH; y++)
                    {
                        for (int x = 0; x < outW; x++)
                        {
                            float val = Math.Clamp(outputTensor[0, 0, y, x], 0.0f, 1.0f);
                            maskBytes[y * outW + x] = (byte)(val * 255.0f);
                        }
                    }
                }

                var maskBitmap = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Gray8, null, maskBytes, outW);
                var scaledMask = new TransformedBitmap(maskBitmap, new ScaleTransform((double)origW / outW, (double)origH / outH));
                var finalMask = new FormatConvertedBitmap(scaledMask, PixelFormats.Gray8, null, 0);

                byte[] fullAlpha = new byte[origW * origH];
                finalMask.CopyPixels(fullAlpha, origW, 0);

                // 7. Componi l'immagine finale RGBA a 32 bit con la maschera applicata
                var bgraOriginal = new FormatConvertedBitmap(originalSource, PixelFormats.Bgra32, null, 0);
                int bgraStride = origW * 4;
                byte[] bgraPixels = new byte[bgraStride * origH];
                bgraOriginal.CopyPixels(bgraPixels, bgraStride, 0);

                for (int i = 0; i < origW * origH; i++)
                {
                    int alphaIdx = i;
                    int bgraIdx = i * 4;
                    byte maskVal = fullAlpha[alphaIdx];
                    byte origAlpha = bgraPixels[bgraIdx + 3];

                    // Moltiplica canale alpha
                    bgraPixels[bgraIdx + 3] = (byte)((origAlpha * maskVal) / 255);
                }

                var outputBitmap = BitmapSource.Create(origW, origH, 96, 96, PixelFormats.Bgra32, null, bgraPixels, bgraStride);
                progressCallback?.Invoke(isModelCached ? 95.0 : 97.0);

                // 8. Salva come PNG trasparente
                string destPath = outputImagePath ?? MediaConversionService.GenerateNonConflictingPath(inputImagePath, "_nobg.png");
                using (var outFs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(outputBitmap));
                    encoder.Save(outFs);
                }

                progressCallback?.Invoke(100.0);
                AppLogger.Info($"AeroSpot.Tools: Sfondo rimosso con successo: {destPath}");
                return destPath;
            });
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _session?.Dispose();
                _session = null;
            }
        }
    }
}
