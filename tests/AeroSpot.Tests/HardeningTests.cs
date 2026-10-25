using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Search;
using AeroSpot.Host.Native;
using AeroSpot.Host.Services;
using AeroSpot.Worker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class HardeningTests
    {
        [TestInitialize]
        public void Init()
        {
            PathFilter.ResetToDefault();
        }

        [TestMethod]
        public void HostAssembly_DoesNotReferenceWpfOrWinForms()
        {
            // Verifica che l'assembly dell'Host residente non contenga riferimenti né dipendenze da WPF o WinForms
            var hostAssembly = typeof(HostWindow).Assembly;
            var referenced = hostAssembly.GetReferencedAssemblies();

            var bannedPrefixes = new[]
            {
                "PresentationFramework",
                "PresentationCore",
                "WindowsBase",
                "System.Windows.Forms"
            };

            foreach (var banned in bannedPrefixes)
            {
                bool referencesBanned = referenced.Any(r => r.Name != null && r.Name.StartsWith(banned, StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(referencesBanned, $"AeroSpot.Host non deve referenziare {banned}. L'Host residente deve rimanere Win32 puro senza runtime UI pesanti.");
            }

            // Verifica che nessun tipo nell'Host erediti da controlli WPF o Form
            foreach (var type in hostAssembly.GetTypes())
            {
                Type? baseType = type.BaseType;
                while (baseType != null)
                {
                    Assert.AreNotEqual("System.Windows.Window", baseType.FullName, "Host non deve ereditare da System.Windows.Window.");
                    Assert.AreNotEqual("System.Windows.Forms.Form", baseType.FullName, "Host non deve ereditare da System.Windows.Forms.Form.");
                    baseType = baseType.BaseType;
                }
            }
        }

        [TestMethod]
        public async Task WorkerManager_CrashRecovery_RecoversAndRelaunches()
        {
            var config = new AppConfig
            {
                WorkerIdleTimeoutSeconds = 30,
                KeepWorkerWarm = false
            };

            using var manager = new WorkerManager(config);

            // 1. Avvia inizialmente il Worker
            bool startedFirst = await manager.ShowLauncherAsync();
            Assert.IsTrue(startedFirst, "Il Worker deve avviarsi correttamente la prima volta.");
            Assert.IsTrue(manager.IsWorkerRunning, "Il processo Worker deve essere in esecuzione.");

            // 2. Simula un crash improvviso del Worker (SIGKILL / Process.Kill)
            var workerProcessField = typeof(WorkerManager).GetField("_workerProcess", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(workerProcessField, "Il campo _workerProcess deve esistere per il test di resilienza.");

            var proc = workerProcessField.GetValue(manager) as System.Diagnostics.Process;
            Assert.IsNotNull(proc, "L'istanza di Process del Worker deve essere attiva.");

            proc.Kill();
            proc.WaitForExit(3000);
            Assert.IsTrue(proc.HasExited, "Il processo Worker deve essere terminato.");

            // Breve pausa per permettere al sistema operativo di notificare la chiusura
            await Task.Delay(200);

            // 3. Verifica che una successiva richiesta riavvii trasparentemente il Worker senza errori
            bool startedSecond = await manager.ShowLauncherAsync();
            Assert.IsTrue(startedSecond, "Host deve rilevare la disconnessione e rilanciare automaticamente un nuovo processo Worker.");
            Assert.IsTrue(manager.IsWorkerRunning, "Un nuovo processo Worker deve essere in esecuzione.");

            // Cleanup pulito
            await manager.StopWorkerAsync();
        }

        [TestMethod]
        public async Task PreviewLoader_FileDeletedImmediatelyBeforePreview_ReturnsSafeError()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"DeletedBeforePreview_{Guid.NewGuid():N}.txt");
            File.WriteAllText(tempFile, "File che viene rimosso prima dell'anteprima.");

            // Elimina immediatamente il file prima del caricamento
            File.Delete(tempFile);

            var model = await PreviewLoader.LoadAsync(tempFile);

            Assert.IsNotNull(model);
            Assert.AreEqual(PreviewType.Error, model.Type, "Un file rimosso prima dell'anteprima deve restituire PreviewType.Error.");
            Assert.IsNotNull(model.ErrorMessage);
            Assert.IsTrue(model.ErrorMessage.Contains("non esiste") || model.ErrorMessage.Contains("rimosso"));
        }

        [TestMethod]
        public void PathFilter_HandlesMissingOrDisconnectedDrives_RejectsCleanly()
        {
            // Dischi non consentiti o non collegati
            Assert.IsFalse(PathFilter.IsPathAllowed(@"Z:\MissingDrive\Folder\file.txt"), "Disco Z: non consentito deve essere rifiutato.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"D:\Games\Steam\steamapps\game.exe"), "Disco D: deve essere rifiutato.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"F:\Backups\Archive.zip"), "Disco F: deve essere rifiutato.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"G:\Media\Movie.mp4"), "Disco G: deve essere rifiutato.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"\\NetworkShare\Folder\file.txt"), "Percorsi di rete UNC devono essere rifiutati.");

            // Percorsi consentiti
            string sysDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";
            Assert.IsTrue(PathFilter.IsPathAllowed($@"{sysDrive}:\Windows\System32\cmd.exe"), "Percorso su unità di sistema deve essere accettato.");
            var config = AeroSpot.Core.Config.AppConfig.Load();
            if (config.IncludedDrives.Contains("E", StringComparer.OrdinalIgnoreCase))
            {
                Assert.IsTrue(PathFilter.IsPathAllowed(@"E:\Projects\Doc.txt"), "Percorso su disco E: configurato deve essere accettato.");
            }

            // Input anomali e null
            Assert.IsFalse(PathFilter.IsPathAllowed(null!), "Path null deve essere rifiutato.");
            Assert.IsFalse(PathFilter.IsPathAllowed(""), "Stringa vuota deve essere rifiutata.");
            Assert.IsFalse(PathFilter.IsPathAllowed("    "), "Spazi bianchi devono essere rifiutati.");
        }

        [TestMethod]
        public void SqliteIndexDatabase_NullOrMalformedQueries_HandlesWithoutCrash()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"AeroSpot_Hardening_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new SqliteIndexDatabase(tempDb);
                db.Initialize();

                // Inserimento di elementi di prova
                db.UpsertBatch(new List<IndexedItem>
                {
                    new IndexedItem("TestDoc.txt", @"C:\Test\TestDoc.txt", "Documento", "TestDoc.txt", isApp: false)
                });

                // Array di query potenzialmente problematiche o malevole (SQL injection, caratteri di escape)
                string?[] edgeCaseQueries = new string?[]
                {
                    null,
                    "",
                    "   ",
                    "' OR 1=1 --",
                    "\" UNION SELECT * --",
                    "%",
                    "_",
                    "[",
                    "]",
                    "\\",
                    "/* comment */",
                    "; DROP TABLE items; --",
                    "TestDoc.txt"
                };

                foreach (var q in edgeCaseQueries)
                {
                    var results = db.Search(q!);
                    Assert.IsNotNull(results, $"La ricerca per '{q}' non deve mai restituire null.");
                    // Non deve lanciare eccezioni
                }

                // Verifica che la tabella non sia stata corrotta da tentativi di injection
                var normalResults = db.Search("TestDoc");
                Assert.HasCount(1, normalResults, "La query normale dopo i test di injection deve funzionare correttamente.");
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        [TestMethod]
        public void AppConfig_CorruptedSettingsFile_LoadsDefaultsSafely()
        {
            string tempConfigPath = Path.Combine(Path.GetTempPath(), $"CorruptedSettings_{Guid.NewGuid():N}.json");
            try
            {
                // Scrive JSON palesemente non valido e corrotto
                File.WriteAllText(tempConfigPath, "{ \"WorkerIdleTimeoutSeconds\": 30, INVALID_JSON: [ }");

                var loaded = AppConfig.Load(tempConfigPath);

                Assert.IsNotNull(loaded, "Il caricamento di un file di configurazione corrotto non deve lanciare eccezioni ma restituire la configurazione predefinita.");
                Assert.AreEqual(30, loaded.WorkerIdleTimeoutSeconds);
                CollectionAssert.Contains(loaded.IncludedDrives, "C");
                CollectionAssert.Contains(loaded.IncludedDrives, "E");
            }
            finally
            {
                if (File.Exists(tempConfigPath))
                {
                    try { File.Delete(tempConfigPath); } catch { }
                }
            }
        }
    }
}
