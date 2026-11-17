using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class UpdateServiceTests
    {
        [TestMethod]
        public void IsNewerVersion_ComparesCorrectly()
        {
            // Nuove versioni
            Assert.IsTrue(UpdateService.IsNewerVersion("v1.5.1", "1.5.0"), "v1.5.1 deve essere considerata più recente di 1.5.0");
            Assert.IsTrue(UpdateService.IsNewerVersion("v1.6.0", "1.5.0"), "v1.6.0 deve essere considerata più recente di 1.5.0");
            Assert.IsTrue(UpdateService.IsNewerVersion("v2.0.0", "1.5.0"), "v2.0.0 deve essere considerata più recente di 1.5.0");
            Assert.IsTrue(UpdateService.IsNewerVersion("2.1.0", "1.5.0"), "2.1.0 (senza v) deve essere considerata più recente di 1.5.0");
            Assert.IsTrue(UpdateService.IsNewerVersion("v1.5.1-beta", "1.5.0"), "v1.5.1-beta deve essere considerata più recente di 1.5.0");

            // Stessa versione o versione inferiore
            Assert.IsFalse(UpdateService.IsNewerVersion("v1.5.0", "1.5.0"), "v1.5.0 non deve essere più recente di 1.5.0");
            Assert.IsFalse(UpdateService.IsNewerVersion("1.5.0", "1.5.0"), "1.5.0 non deve essere più recente di 1.5.0");
            Assert.IsFalse(UpdateService.IsNewerVersion("v1.0.0", "1.5.0"), "v1.0.0 non deve essere più recente di 1.5.0");
            Assert.IsFalse(UpdateService.IsNewerVersion("v0.9.5", "1.5.0"), "v0.9.5 non deve essere più recente di 1.5.0");
            Assert.IsFalse(UpdateService.IsNewerVersion("", "1.5.0"), "Stringa vuota non deve essere considerata nuova versione");
        }

        [TestMethod]
        public void ShouldCheckForUpdates_RespectsConfigFrequencies()
        {
            var config = new AppConfig();

            // Modalità Manuale
            config.UpdateCheckFrequency = "Manual";
            config.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-100);
            Assert.IsFalse(UpdateService.ShouldCheckForUpdates(config), "In modalità manuale non deve mai effettuare controlli automatici.");

            // Modalità Startup (throttling 15 minuti)
            config.UpdateCheckFrequency = "Startup";
            config.LastUpdateCheckUtc = DateTime.UtcNow.AddMinutes(-5);
            Assert.IsFalse(UpdateService.ShouldCheckForUpdates(config), "In modalità Startup con ultimo controllo recente (<15m) non deve scattare.");

            config.LastUpdateCheckUtc = DateTime.UtcNow.AddMinutes(-20);
            Assert.IsTrue(UpdateService.ShouldCheckForUpdates(config), "In modalità Startup con ultimo controllo >15m deve scattare.");

            // Modalità Daily
            config.UpdateCheckFrequency = "Daily";
            config.LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-12);
            Assert.IsFalse(UpdateService.ShouldCheckForUpdates(config), "In modalità Daily dopo 12 ore non deve ancora scattare.");

            config.LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-25);
            Assert.IsTrue(UpdateService.ShouldCheckForUpdates(config), "In modalità Daily dopo 25 ore deve scattare.");

            // Modalità Weekly
            config.UpdateCheckFrequency = "Weekly";
            config.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-4);
            Assert.IsFalse(UpdateService.ShouldCheckForUpdates(config), "In modalità Weekly dopo 4 giorni non deve ancora scattare.");

            config.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-8);
            Assert.IsTrue(UpdateService.ShouldCheckForUpdates(config), "In modalità Weekly dopo 8 giorni deve scattare.");

            // Modalità Monthly
            config.UpdateCheckFrequency = "Monthly";
            config.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-15);
            Assert.IsFalse(UpdateService.ShouldCheckForUpdates(config), "In modalità Monthly dopo 15 giorni non deve ancora scattare.");

            config.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-32);
            Assert.IsTrue(UpdateService.ShouldCheckForUpdates(config), "In modalità Monthly dopo 32 giorni deve scattare.");
        }

        private class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _responseContent;
            private readonly HttpStatusCode _statusCode;

            public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                _responseContent = responseContent;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }
        }

        [TestMethod]
        public async Task CheckForUpdatesAsync_ParsesGitHubApiResponse_AndSelectsInstallerAsset()
        {
            string fakeJson = @"
            {
                ""tag_name"": ""v1.6.0"",
                ""name"": ""AeroSpot v1.6.0 — Spring Update"",
                ""html_url"": ""https://github.com/Ritolino/AeroSpot/releases/tag/v1.6.0"",
                ""body"": ""- Nuove funzionalità\n- Miglioramenti grafici"",
                ""assets"": [
                    {
                        ""name"": ""AeroSpot-v1.6.0-win-x64-portable.zip"",
                        ""browser_download_url"": ""https://github.com/Ritolino/AeroSpot/releases/download/v1.6.0/AeroSpot-v1.6.0-win-x64-portable.zip"",
                        ""size"": 78000000
                    },
                    {
                        ""name"": ""AeroSpot-Setup-v1.6.0.exe"",
                        ""browser_download_url"": ""https://github.com/Ritolino/AeroSpot/releases/download/v1.6.0/AeroSpot-Setup-v1.6.0.exe"",
                        ""size"": 82000000
                    }
                ]
            }";

            using var httpClient = new HttpClient(new FakeHttpMessageHandler(fakeJson));
            var result = await UpdateService.CheckForUpdatesAsync(
                currentVersion: "1.5.0",
                httpClient: httpClient);

            Assert.IsTrue(result.IsSuccess, "La verifica deve avere successo.");
            Assert.IsTrue(result.HasUpdate, "Deve rilevare che 1.6.0 > 1.5.0.");
            Assert.AreEqual("1.6.0", result.LatestVersion);
            Assert.AreEqual("AeroSpot v1.6.0 — Spring Update", result.ReleaseTitle);
            Assert.AreEqual("AeroSpot-Setup-v1.6.0.exe", result.AssetFileName, "Deve dare priorità al file setup installer (.exe).");
            Assert.IsTrue(result.IsInstaller, "L'asset selezionato deve essere marcato come installer.");
            Assert.AreEqual("https://github.com/Ritolino/AeroSpot/releases/download/v1.6.0/AeroSpot-Setup-v1.6.0.exe", result.DownloadUrl);
        }

        [TestMethod]
        public async Task CheckForUpdatesAsync_WhenCurrentVersionIsLatest_ReportsNoUpdate()
        {
            string fakeJson = @"
            {
                ""tag_name"": ""v1.5.0"",
                ""name"": ""AeroSpot v1.5.0"",
                ""html_url"": ""https://github.com/Ritolino/AeroSpot/releases/tag/v1.5.0"",
                ""body"": ""Note di rilascio"",
                ""assets"": []
            }";

            using var httpClient = new HttpClient(new FakeHttpMessageHandler(fakeJson));
            var result = await UpdateService.CheckForUpdatesAsync(
                currentVersion: "1.5.0",
                httpClient: httpClient);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate, "Non deve segnalare aggiornamento quando la versione è uguale.");
            Assert.AreEqual("1.5.0", result.LatestVersion);
        }
    }
}
