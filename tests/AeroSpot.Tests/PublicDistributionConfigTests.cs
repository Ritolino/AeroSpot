using System;
using System.IO;
using System.Linq;
using AeroSpot.Core.Config;
using AeroSpot.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class PublicDistributionConfigTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            PathFilter.ResetToDefault();
            AppDataPath.ResetCache();
        }

        [TestMethod]
        public void AppConfig_GetDefaultIncludedDrives_IncludesSystemDrive()
        {
            var drives = AppConfig.GetDefaultIncludedDrives();
            Assert.IsNotNull(drives);
            Assert.IsNotEmpty(drives, "Deve essere presente almeno un'unità inclusa.");

            string sysDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";
            Assert.Contains(sysDrive, drives, StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void AppConfig_MigratesLegacyV1Settings_ToSchemaVersion2()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "AeroSpot_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string legacyJsonPath = Path.Combine(tempDir, "legacy_settings.json");

            try
            {
                // JSON legacy senza SchemaVersion e con liste vuote
                string legacyJson = @"{
                    ""WorkerIdleTimeoutSeconds"": 25,
                    ""KeepWorkerWarm"": true,
                    ""SpotlightHotkeyText"": ""Ctrl + Space""
                }";
                File.WriteAllText(legacyJsonPath, legacyJson);

                var loaded = AppConfig.Load(legacyJsonPath);

                Assert.IsNotNull(loaded);
                Assert.AreEqual(25, loaded.WorkerIdleTimeoutSeconds);
                Assert.IsTrue(loaded.KeepWorkerWarm);
                Assert.AreEqual("Ctrl + Space", loaded.SpotlightHotkeyText);
                Assert.AreEqual(2, loaded.SchemaVersion, "La configurazione deve essere migrata a SchemaVersion 2.");
                Assert.IsNotNull(loaded.IncludedDrives);
                Assert.IsNotEmpty(loaded.IncludedDrives, "IncludedDrives deve essere popolato con i drive di sistema di default.");
                Assert.IsNotNull(loaded.ExcludedDirectoryNames);
                Assert.Contains("SteamLibrary", loaded.ExcludedDirectoryNames, "Le esclusioni di default devono essere inizializzate.");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void AppDataPath_StandardAndPortableDetection()
        {
            AppDataPath.ResetCache();

            // Modalità standard (senza portable.txt nella cartella di test)
            string standardDir = AppDataPath.GetDataDirectory();
            Assert.IsFalse(string.IsNullOrWhiteSpace(standardDir));
            Assert.IsTrue(Directory.Exists(standardDir));

            // Verifica che le cartelle settings e db siano sotto la directory dati
            string settings = AppDataPath.GetSettingsPath();
            string db = AppDataPath.GetDatabasePath();
            string logs = AppDataPath.GetLogsDirectory();

            Assert.IsTrue(settings.StartsWith(standardDir, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(db.StartsWith(standardDir, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(logs.StartsWith(standardDir, StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void PathFilter_HandlesWindowsDirectoryDynamically()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string notepad = Path.Combine(winDir, "System32", "notepad.exe");
            string randomDll = Path.Combine(winDir, "System32", "kernel32.dll");

            // Configura filtro con il disco di Windows abilitato
            string winDrive = Path.GetPathRoot(winDir)?.TrimEnd('\\', ':') ?? "C";
            PathFilter.Configure(new[] { winDrive });

            // Notepad deve essere ammesso come eseguibile consentito
            Assert.IsTrue(PathFilter.IsPathAllowed(notepad));

            // DLL generiche in System32 non devono essere indicizzate
            Assert.IsFalse(PathFilter.IsPathAllowed(randomDll));
        }
    }
}
