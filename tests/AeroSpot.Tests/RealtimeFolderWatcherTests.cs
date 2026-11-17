using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AeroSpot.Core.Config;
using AeroSpot.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class RealtimeFolderWatcherTests
    {
        private string _testDir = null!;
        private string _dbPath = null!;
        private SqliteIndexDatabase _db = null!;

        [TestInitialize]
        public void SetUp()
        {
            PathFilter.Configure(new[] { "C", "D", "E" }, new string[0]);
            _testDir = Path.Combine(Path.GetTempPath(), "AeroSpot_WatcherTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _dbPath = Path.Combine(_testDir, "test_index.db");
            _db = new SqliteIndexDatabase(_dbPath);
            _db.Initialize();
        }

        [TestCleanup]
        public void TearDown()
        {
            _db.Dispose();
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
            PathFilter.ResetToDefault();
        }

        [TestMethod]
        public void RealtimeFolderWatcher_Creation_InsertsFileIntoDatabase()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 100);

            string filePath = Path.Combine(_testDir, "documento_fiscale.pdf");
            File.WriteAllText(filePath, "%PDF-1.4 dummy test");

            // Attende che scatti il debounce (100ms) ed esegua l'upsert
            bool found = false;
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(50);
                var results = _db.Search("documento_fiscale", 10);
                if (results.Any(r => r.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    found = true;
                    Assert.AreEqual("Documento", results.First().Category);
                    break;
                }
            }

            Assert.IsTrue(found, "Il file creato nella cartella monitorata deve essere indicizzato automaticamente in tempo reale.");
        }

        [TestMethod]
        public void RealtimeFolderWatcher_BrowserDownload_IgnoresCrdownloadUntilRenamed()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 100);

            string crdownloadPath = Path.Combine(_testDir, "progetto_architettura.zip.crdownload");
            string finalZipPath = Path.Combine(_testDir, "progetto_architettura.zip");

            // 1. Browser crea il file temporaneo .crdownload
            File.WriteAllText(crdownloadPath, "partial bytes...");
            Thread.Sleep(200);

            var initialResults = _db.Search("progetto_architettura", 10);
            Assert.IsEmpty(initialResults, "I file temporanei .crdownload non devono essere inseriti nell'indice.");

            // 2. Browser completa il download e rinomina nel file definitivo
            File.Move(crdownloadPath, finalZipPath);

            bool foundFinal = false;
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(50);
                var results = _db.Search("progetto_architettura", 10);
                if (results.Any(r => r.FullPath.Equals(finalZipPath, StringComparison.OrdinalIgnoreCase)))
                {
                    foundFinal = true;
                    Assert.AreEqual("Archivio", results.First().Category);
                    break;
                }
            }

            Assert.IsTrue(foundFinal, "Il file rinominato dopo il download deve essere indicizzato tempestivamente.");
        }

        [TestMethod]
        public void RealtimeFolderWatcher_Deletion_RemovesFileFromDatabase()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 100);

            string filePath = Path.Combine(_testDir, "rimuovimi.txt");
            File.WriteAllText(filePath, "testo da eliminare");

            // Attende inserimento iniziale
            bool inserted = false;
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(50);
                if (_db.Search("rimuovimi", 10).Count > 0)
                {
                    inserted = true;
                    break;
                }
            }
            Assert.IsTrue(inserted, "Il file iniziale deve essere presente prima della cancellazione.");

            // Elimina il file fisico
            File.Delete(filePath);

            // Attende rimozione dall'indice
            bool removed = false;
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(50);
                if (_db.Search("rimuovimi", 10).Count == 0)
                {
                    removed = true;
                    break;
                }
            }

            Assert.IsTrue(removed, "Il file eliminato fisicamente deve essere rimosso dall'indice in tempo reale.");
        }

        [TestMethod]
        public void RealtimeFolderWatcher_Batching_HandlesMultipleFilesEfficiently()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 150);

            const int fileCount = 10;
            for (int i = 0; i < fileCount; i++)
            {
                string path = Path.Combine(_testDir, $"batch_file_{i}.png");
                File.WriteAllText(path, "image bytes");
            }

            bool allIndexed = false;
            for (int i = 0; i < 25; i++)
            {
                Thread.Sleep(50);
                var results = _db.Search("batch_file", 20);
                if (results.Count >= fileCount)
                {
                    allIndexed = true;
                    break;
                }
            }

            Assert.IsTrue(allIndexed, "Tutti i file creati in raffica devono essere indicizzati tramite micro-batching.");
        }
    }
}
