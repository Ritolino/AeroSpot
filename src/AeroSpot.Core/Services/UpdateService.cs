using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        public string? Sha256SumsUrl { get; set; }
        public long AssetSizeBytes { get; set; }
        public bool IsInstaller { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public static class UpdateService
    {
        private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"AeroSpot-App/{CurrentVersion}");
            return client;
        }

        public static string CurrentVersion =>
            typeof(UpdateService).Assembly.GetName().Version is Version v
                ? $"{v.Major}.{v.Minor}.{v.Build}"
                : "1.6.1";

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
        /// Confronta due stringhe di versione semantica (es. "v1.6.1" vs "1.6.0").
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
        /// Interroga l'API di GitHub Releases per individuare l'ultima versione rilasciata
        /// e seleziona l'asset più adatto (portable .zip vs setup .exe) oltre a SHA256SUMS.txt.
        /// </summary>
        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(
            string? currentVersion = null,
            string repo = GitHubRepo,
            HttpClient? httpClient = null,
            CancellationToken ct = default,
            bool? isPortable = null)
        {
            currentVersion ??= CurrentVersion;
            bool preferPortable = isPortable ?? AppDataPath.IsPortable;

            var result = new UpdateCheckResult
            {
                CurrentVersion = currentVersion
            };

            var client = httpClient ?? SharedHttpClient;

            try
            {
                if (httpClient != null && client.DefaultRequestHeaders.UserAgent.Count == 0)
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd($"AeroSpot-App/{currentVersion}");
                }

                string url = $"https://api.github.com/repos/{repo}/releases/latest";
                AppLogger.Info($"UpdateService: Verifica aggiornamenti su {url} (preferPortable={preferPortable})...");

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

                // Cerca gli asset ottimali (.exe, .zip e SHA256SUMS.txt)
                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    JsonElement? exeAsset = null;
                    JsonElement? zipAsset = null;
                    JsonElement? portableZipAsset = null;

                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

                        if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Sha256SumsUrl = asset.TryGetProperty("browser_download_url", out var shaDl) ? shaDl.GetString() : null;
                        }
                        else if (exeAsset == null &&
                                 name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                 !name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                        {
                            exeAsset = asset;
                        }
                        else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            if (name.Contains("portable", StringComparison.OrdinalIgnoreCase))
                            {
                                portableZipAsset ??= asset;
                            }
                            zipAsset ??= asset;
                        }
                    }

                    JsonElement? preferredZip = portableZipAsset ?? zipAsset;
                    JsonElement? chosenAsset = null;
                    bool isInstaller = false;

                    if (preferPortable)
                    {
                        if (preferredZip.HasValue)
                        {
                            chosenAsset = preferredZip;
                            isInstaller = false;
                        }
                        else if (exeAsset.HasValue)
                        {
                            chosenAsset = exeAsset;
                            isInstaller = true;
                        }
                    }
                    else
                    {
                        if (exeAsset.HasValue)
                        {
                            chosenAsset = exeAsset;
                            isInstaller = true;
                        }
                        else if (preferredZip.HasValue)
                        {
                            chosenAsset = preferredZip;
                            isInstaller = false;
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

                AppLogger.Info($"UpdateService: Verifica completata. Current={currentVersion}, Latest={result.LatestVersion}, HasUpdate={result.HasUpdate}, Asset={result.AssetFileName}");
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                AppLogger.Error("UpdateService: Errore durante la verifica aggiornamenti", ex);
                return result;
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

            string safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                throw new ArgumentException("Nome file di aggiornamento non valido.", nameof(fileName));

            string tempDir = Path.Combine(Path.GetTempPath(), "AeroSpot_Updates");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string targetFilePath = Path.Combine(tempDir, safeFileName);
            if (File.Exists(targetFilePath))
            {
                try { File.Delete(targetFilePath); } catch { }
            }

            var client = httpClient ?? SharedHttpClient;

            if (httpClient != null && client.DefaultRequestHeaders.UserAgent.Count == 0)
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

        /// <summary>
        /// Estrae l'hash SHA-256 atteso per un file specifico dal contenuto di SHA256SUMS.txt.
        /// Supporta formati standard ("<hash>  <filename>", "<hash> *<filename>").
        /// </summary>
        public static string? ParseExpectedSha256(string sha256SumsContent, string assetFileName)
        {
            if (string.IsNullOrWhiteSpace(sha256SumsContent) || string.IsNullOrWhiteSpace(assetFileName))
                return null;

            string targetName = Path.GetFileName(assetFileName).Trim();
            using var reader = new StringReader(sha256SumsContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string hash = parts[0].Trim().ToLowerInvariant();
                    string file = parts[parts.Length - 1].Trim().TrimStart('*');
                    if (hash.Length == 64 && string.Equals(Path.GetFileName(file), targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return hash;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Calcola l'hash SHA-256 esadecimale (minuscolo) di un file locale.
        /// </summary>
        public static string ComputeFileSha256(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Verifica l'integrità SHA-256 del file scaricato rispetto a SHA256SUMS.txt pubblicato nella release.
        /// </summary>
        public static async Task VerifyIntegrityAsync(
            string filePath,
            string assetFileName,
            string? sha256SumsUrl,
            HttpClient? httpClient = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sha256SumsUrl))
            {
                AppLogger.Info("UpdateService: SHA256SUMS.txt non presente nella release, verifica hash saltata.");
                return;
            }

            var client = httpClient ?? SharedHttpClient;
            if (httpClient != null && client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"AeroSpot-Updater/{CurrentVersion}");
            }

            string content = await client.GetStringAsync(sha256SumsUrl, ct).ConfigureAwait(false);
            string? expected = ParseExpectedSha256(content, assetFileName);
            if (string.IsNullOrEmpty(expected))
            {
                throw new InvalidDataException($"Voce SHA-256 mancante per '{assetFileName}' in SHA256SUMS.txt.");
            }

            string actual = ComputeFileSha256(filePath);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Verifica integrità SHA-256 fallita per '{assetFileName}'. Atteso: {expected}, Calcolato: {actual}");
            }

            AppLogger.Info($"UpdateService: Integrità SHA-256 verificata con successo per '{assetFileName}'.");
        }

        /// <summary>
        /// Risolve la directory radice di installazione (genitore se l'eseguibile risiede in \app o \bin).
        /// </summary>
        public static string ResolveInstallRoot(string? baseDirectory = null)
        {
            string dir = (baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string folderName = Path.GetFileName(dir);
            if (string.Equals(folderName, "app", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, "bin", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(dir);
                if (parent != null)
                {
                    return parent.FullName;
                }
            }

            return dir;
        }

        /// <summary>
        /// Protezione anti-sovrascrittura: rileva se la cartella target è una directory di sviluppo (presenza di AeroSpot.sln).
        /// </summary>
        public static bool IsDevelopmentDirectory(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot)) return false;

            if (File.Exists(Path.Combine(installRoot, "AeroSpot.sln")))
                return true;

            var parent = Directory.GetParent(installRoot);
            if (parent != null && File.Exists(Path.Combine(parent.FullName, "AeroSpot.sln")))
                return true;

            string normalized = installRoot.Replace('/', '\\');
            if (normalized.Contains(@"\src\AeroSpot.", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Estrae un pacchetto .zip in una directory temporanea di staging e verifica che contenga i binari essenziali.
        /// </summary>
        public static string StageZipUpdate(string zipPath, string? stagingParentDir = null)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                throw new FileNotFoundException("Archivio .zip di aggiornamento non trovato", zipPath);

            string parent = stagingParentDir ?? Path.Combine(Path.GetTempPath(), "AeroSpot_Updates");
            string stagingRoot = Path.Combine(parent, $"staging_{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingRoot);

            ZipFile.ExtractToDirectory(zipPath, stagingRoot, overwriteFiles: true);

            string sourceDir = stagingRoot;
            var rootFiles = Directory.GetFiles(stagingRoot);
            var rootDirs = Directory.GetDirectories(stagingRoot);
            if (rootFiles.Length == 0 && rootDirs.Length == 1)
            {
                sourceDir = rootDirs[0];
            }

            string launcherPath = Path.Combine(sourceDir, "AeroSpot.exe");
            string hostPath = Path.Combine(sourceDir, "app", "AeroSpot.Host.exe");
            if (!File.Exists(launcherPath) || !File.Exists(hostPath))
            {
                try { Directory.Delete(stagingRoot, recursive: true); } catch { }
                throw new InvalidDataException("Pacchetto di aggiornamento .zip non valido o incompleto (AeroSpot.exe o app\\AeroSpot.Host.exe mancanti).");
            }

            return sourceDir;
        }

        /// <summary>
        /// Verifica l'integrità, prepara lo staging e applica l'aggiornamento in 1 clic (sia .zip portable sia .exe setup),
        /// quindi invoca onShutdownRequested per chiudere Host + Worker in modo coordinato.
        /// </summary>
        public static async Task PrepareAndApplyUpdateAsync(
            string downloadedFilePath,
            string? sha256SumsUrl = null,
            string? assetFileName = null,
            Action? onShutdownRequested = null,
            string? installRootOverride = null,
            HttpClient? httpClient = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(downloadedFilePath) || !File.Exists(downloadedFilePath))
            {
                throw new FileNotFoundException("File di aggiornamento non trovato", downloadedFilePath);
            }

            string effectiveAssetName = assetFileName ?? Path.GetFileName(downloadedFilePath);
            await VerifyIntegrityAsync(downloadedFilePath, effectiveAssetName, sha256SumsUrl, httpClient, ct).ConfigureAwait(false);

            if (downloadedFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"UpdateService: Avvio installer setup: {downloadedFilePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = downloadedFilePath,
                    UseShellExecute = true
                });

                onShutdownRequested?.Invoke();
                return;
            }

            if (downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string installRoot = installRootOverride ?? ResolveInstallRoot();
                if (IsDevelopmentDirectory(installRoot))
                {
                    throw new InvalidOperationException(
                        $"Aggiornamento automatico bloccato per sicurezza nella cartella di sviluppo ({installRoot}).");
                }

                string stagingSourceDir = StageZipUpdate(downloadedFilePath);
                string stagingRootToClean = stagingSourceDir;
                var parentDir = Directory.GetParent(stagingSourceDir);
                if (parentDir != null && parentDir.Name.StartsWith("staging_", StringComparison.OrdinalIgnoreCase))
                {
                    stagingRootToClean = parentDir.FullName;
                }

                bool wasPortable = File.Exists(Path.Combine(installRoot, "portable.txt"));
                string updatesDir = Path.Combine(Path.GetTempPath(), "AeroSpot_Updates");
                Directory.CreateDirectory(updatesDir);
                string scriptPath = Path.Combine(updatesDir, "apply_update.ps1");

                string scriptContent = BuildApplyUpdateScript(
                    stagingSourceDir,
                    stagingRootToClean,
                    downloadedFilePath,
                    installRoot,
                    wasPortable);

                await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct).ConfigureAwait(false);

                AppLogger.Info($"UpdateService: Avvio script di aggiornamento atomico ({scriptPath}) verso {installRoot}...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                onShutdownRequested?.Invoke();
                return;
            }

            throw new NotSupportedException($"Formato pacchetto di aggiornamento non supportato: {Path.GetExtension(downloadedFilePath)}");
        }

        private static string BuildApplyUpdateScript(
            string stagingSourceDir,
            string stagingRootToClean,
            string zipPath,
            string installRoot,
            bool wasPortable)
        {
            string Esc(string s) => s.Replace("'", "''");
            return $@"$ErrorActionPreference = 'SilentlyContinue'
$stagingSource = '{Esc(stagingSourceDir)}'
$stagingRoot = '{Esc(stagingRootToClean)}'
$zipFile = '{Esc(zipPath)}'
$installRoot = '{Esc(installRoot)}'
$normalizedRoot = $installRoot.TrimEnd('\', '/') + '\'
$wasPortable = ${(wasPortable ? "true" : "false")}
$processNames = @('AeroSpot.Tools', 'AeroSpot.Worker', 'AeroSpot.Host', 'AeroSpot')

function Get-TargetProcesses {{
    Get-Process -Name $processNames -ErrorAction SilentlyContinue | Where-Object {{
        try {{
            $procPath = $_.Path
            if ([string]::IsNullOrWhiteSpace($procPath)) {{ return $true }}
            return $procPath.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)
        }} catch {{
            return $true
        }}
    }}
}}

# 1. Attendi chiusura ordinata fino a 15 secondi (30 x 500ms)
for ($i = 0; $i -lt 30; $i++) {{
    $alive = Get-TargetProcesses
    if (-not $alive) {{ break }}
    Start-Sleep -Milliseconds 500
}}

# Fallback forzato se qualche processo e rimasto appeso
$remaining = Get-TargetProcesses
if ($remaining) {{
    $remaining | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}}

# 2. Copia atomica da staging a installRoot escludendo tassativamente Data\ (con 5 retry anti-Defender)
for ($attempt = 1; $attempt -le 5; $attempt++) {{
    & robocopy $stagingSource $installRoot /E /XD 'Data' /NP /NFL /NDL /R:3 /W:1 | Out-Null
    if ($LASTEXITCODE -le 7) {{ break }}
    Start-Sleep -Milliseconds 500
}}

# 3. Preserva marcatore portable.txt se l'installazione originale era portable
if ($wasPortable) {{
    $portableMarker = Join-Path $installRoot 'portable.txt'
    if (-not (Test-Path $portableMarker)) {{
        New-Item -ItemType File -Path $portableMarker -Force | Out-Null
    }}
}}

# 4. Riavvio automatico di AeroSpot
$launcher = Join-Path $installRoot 'AeroSpot.exe'
if (Test-Path $launcher) {{
    Start-Process -FilePath $launcher -WorkingDirectory $installRoot
}}

# 5. Pulizia file temporanei di staging, archivio zip e auto-rimozione dello script
Start-Sleep -Seconds 2
if (Test-Path $stagingRoot) {{ Remove-Item -Path $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue }}
if (Test-Path $zipFile) {{ Remove-Item -Path $zipFile -Force -ErrorAction SilentlyContinue }}
if ($PSCommandPath -and (Test-Path $PSCommandPath)) {{ Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue }}
";
        }

        /// <summary>
        /// Compatibilità legacy: applica un aggiornamento locale senza verificare SHA-256 esterno.
        /// </summary>
        public static void ApplyUpdate(string filePath, Action? onShutdownRequested = null)
        {
            PrepareAndApplyUpdateAsync(filePath, sha256SumsUrl: null, assetFileName: null, onShutdownRequested: onShutdownRequested)
                .GetAwaiter()
                .GetResult();
        }
    }
}
