using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Services
{
    public sealed class UpdateCheckResult
    {
        public bool IsSuccess { get; set; }
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseTitle { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? AssetFileName { get; set; }
        public long AssetSizeBytes { get; set; }
        public bool IsInstaller { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public static class UpdateService
    {
        public static string CurrentVersion =>
            typeof(UpdateService).Assembly.GetName().Version is Version v
                ? $"{v.Major}.{v.Minor}.{v.Build}"
                : "1.5.3";

        public const string GitHubRepo = "Ritolino/AeroSpot";

        /// <summary>
        /// Determina se è trascorso il tempo necessario per una nuova verifica automatica
        /// in base alla frequenza configurata dall'utente.
        /// </summary>
        public static bool ShouldCheckForUpdates(AppConfig config)
        {
            if (config == null) return false;
            string freq = config.UpdateCheckFrequency ?? "Startup";

            if (string.Equals(freq, "Manual", StringComparison.OrdinalIgnoreCase))
                return false;

            var elapsed = DateTime.UtcNow - config.LastUpdateCheckUtc;

            if (string.Equals(freq, "Startup", StringComparison.OrdinalIgnoreCase))
            {
                // Verifica a ogni avvio, con un throttle di 15 minuti per evitare richieste eccessive in caso di riavvii ravvicinati
                return elapsed >= TimeSpan.FromMinutes(15);
            }
            if (string.Equals(freq, "Daily", StringComparison.OrdinalIgnoreCase))
            {
                return elapsed >= TimeSpan.FromDays(1);
            }
            if (string.Equals(freq, "Weekly", StringComparison.OrdinalIgnoreCase))
            {
                return elapsed >= TimeSpan.FromDays(7);
            }
            if (string.Equals(freq, "Monthly", StringComparison.OrdinalIgnoreCase))
            {
                return elapsed >= TimeSpan.FromDays(30);
            }

            return false;
        }

        /// <summary>
        /// Confronta due stringhe di versione semantica (es. "v1.6.0" vs "1.5.0").
        /// Restituisce true se latestVersion è strettamente più recente di currentVersion.
        /// </summary>
        public static bool IsNewerVersion(string latestTag, string currentVer)
        {
            if (string.IsNullOrWhiteSpace(latestTag)) return false;
            if (string.IsNullOrWhiteSpace(currentVer)) return true;

            string cleanLatest = latestTag.TrimStart('v', 'V').Trim();
            string cleanCurrent = currentVer.TrimStart('v', 'V').Trim();

            // Rimuove eventuali suffissi come -beta, -rc
            string verPartLatest = cleanLatest.Split('-')[0];
            string verPartCurrent = cleanCurrent.Split('-')[0];

            if (Version.TryParse(verPartLatest, out var vLatest) && Version.TryParse(verPartCurrent, out var vCurrent))
            {
                return vLatest > vCurrent;
            }

            // Fallback su confronto lessicografico per formati non standard
            return string.Compare(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
        }

        /// <summary>
        /// Interroga l'API di GitHub Releases per individuare l'ultima versione rilasciata.
        /// </summary>
        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(
            string? currentVersion = null,
            string repo = GitHubRepo,
            HttpClient? httpClient = null,
            CancellationToken ct = default)
        {
            currentVersion ??= CurrentVersion;
            var result = new UpdateCheckResult
            {
                CurrentVersion = currentVersion
            };

            bool ownClient = httpClient == null;
            var client = httpClient ?? new HttpClient();

            try
            {
                if (client.DefaultRequestHeaders.UserAgent.Count == 0)
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd($"AeroSpot-App/{currentVersion}");
                }

                string url = $"https://api.github.com/repos/{repo}/releases/latest";
                AppLogger.Info($"UpdateService: Verifica aggiornamenti su {url}...");

                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"GitHub API HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                    AppLogger.Warn($"UpdateService: {result.ErrorMessage}");
                    return result;
                }

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                result.LatestVersion = tagName.TrimStart('v', 'V').Trim();
                result.ReleaseTitle = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                result.ReleaseNotes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                result.ReleaseUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : "";

                result.HasUpdate = IsNewerVersion(tagName, currentVersion);
                result.IsSuccess = true;

                // Cerca l'asset ottimale per il download
                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    JsonElement? chosenAsset = null;
                    bool isInstaller = false;

                    // 1. Priorità: installer eseguibile (.exe)
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                        {
                            chosenAsset = asset;
                            isInstaller = true;
                            break;
                        }
                    }

                    // 2. Seconda scelta: pacchetto portable (.zip)
                    if (chosenAsset == null)
                    {
                        foreach (var asset in assetsProp.EnumerateArray())
                        {
                            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = asset;
                                isInstaller = false;
                                break;
                            }
                        }
                    }

                    if (chosenAsset.HasValue)
                    {
                        result.DownloadUrl = chosenAsset.Value.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                        result.AssetFileName = chosenAsset.Value.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                        result.AssetSizeBytes = chosenAsset.Value.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        result.IsInstaller = isInstaller;
                    }
                }

                // Fallback se nessun file diretto è associato alla release
                if (string.IsNullOrEmpty(result.DownloadUrl))
                {
                    result.DownloadUrl = result.ReleaseUrl;
                }

                AppLogger.Info($"UpdateService: Verifica completata. Current={currentVersion}, Latest={result.LatestVersion}, HasUpdate={result.HasUpdate}");
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                AppLogger.Error("UpdateService: Errore durante la verifica aggiornamenti", ex);
                return result;
            }
            finally
            {
                if (ownClient) client.Dispose();
            }
        }

        /// <summary>
        /// Scarica asincronamente il file dell'aggiornamento con notifica di avanzamento in percentuale.
        /// </summary>
        public static async Task<string> DownloadUpdateAsync(
            string downloadUrl,
            string fileName,
            IProgress<double>? progress = null,
            HttpClient? httpClient = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentNullException(nameof(downloadUrl));

            string tempDir = Path.Combine(Path.GetTempPath(), "AeroSpot_Updates");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string targetFilePath = Path.Combine(tempDir, fileName);
            if (File.Exists(targetFilePath))
            {
                try { File.Delete(targetFilePath); } catch { }
            }

            bool ownClient = httpClient == null;
            var client = httpClient ?? new HttpClient();

            try
            {
                if (client.DefaultRequestHeaders.UserAgent.Count == 0)
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd($"AeroSpot-Updater/{CurrentVersion}");
                }

                AppLogger.Info($"UpdateService: Inizio download da {downloadUrl} in {targetFilePath}...");
                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double pct = (double)totalRead / totalBytes.Value * 100.0;
                        progress?.Report(pct);
                    }
                }

                progress?.Report(100.0);
                AppLogger.Info($"UpdateService: Download completato con successo ({totalRead} bytes).");
                return targetFilePath;
            }
            finally
            {
                if (ownClient) client.Dispose();
            }
        }

        /// <summary>
        /// Avvia l'eseguibile di aggiornamento scaricato e notifica la richiesta di chiusura dell'applicazione.
        /// </summary>
        public static void ApplyUpdate(string filePath, Action? onShutdownRequested = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("File di aggiornamento non trovato", filePath);
            }

            AppLogger.Info($"UpdateService: Applicazione aggiornamento con file: {filePath}");

            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(psi);

                onShutdownRequested?.Invoke();
            }
            else
            {
                // Se pacchetto zip, apri la cartella di download evidenziando il file
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }
    }
}
