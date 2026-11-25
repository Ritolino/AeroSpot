using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
            PathFilter.Configure(new[] { "C", "D", "E" }, Array.Empty<string>());
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

        private static bool WaitForCondition(RealtimeFolderWatcher watcher, Func<bool> predicate, int timeoutMs = 5000)
        {
            if (predicate()) return true;

            using var signal = new ManualResetEventSlim(false);
            Action handler = () => signal.Set();
            watcher.ItemsChanged += handler;
            try
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (predicate()) return true;
                    int remaining = (int)(timeoutMs - sw.ElapsedMilliseconds);
                    if (remaining <= 0) break;
                    signal.Wait(Math.Min(150, remaining));
                    signal.Reset();
                }
                return predicate();
            }
            finally
            {
                watcher.ItemsChanged -= handler;
            }
        }

        [TestMethod]
        public void RealtimeFolderWatcher_Creation_InsertsFileIntoDatabase()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 100);

            string filePath = Path.Combine(_testDir, "documento_fiscale.pdf");
            File.WriteAllText(filePath, "%PDF-1.4 dummy test");

            bool found = WaitForCondition(watcher, () =>
                _db.Search("documento_fiscale", 10).Any(r => r.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)));

            Assert.IsTrue(found, "Il file creato nella cartella monitorata deve essere indicizzato automaticamente in tempo reale.");
            Assert.AreEqual("Documento", _db.Search("documento_fiscale", 10).First().Category);
        }

        [TestMethod]
        public void RealtimeFolderWatcher_BrowserDownload_IgnoresCrdownloadUntilRenamed()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 100);

            string crdownloadPath = Path.Combine(_testDir, "progetto_architettura.zip.crdownload");
            string finalZipPath = Path.Combine(_testDir, "progetto_architettura.zip");

            // 1. Browser crea il file temporaneo .crdownload
            using var crEvent = new ManualResetEventSlim(false);
            Action onChange = () => crEvent.Set();
            watcher.ItemsChanged += onChange;
            try
            {
                File.WriteAllText(crdownloadPath, "partial bytes...");
                // Attende brevemente oltre il debounce per confermare che nessun evento indicizzi il .crdownload
                crEvent.Wait(250);
            }
            finally
            {
                watcher.ItemsChanged -= onChange;
            }

            var initialResults = _db.Search("progetto_architettura", 10);
            Assert.IsEmpty(initialResults, "I file temporanei .crdownload non devono essere inseriti nell'indice.");

            // 2. Browser completa il download e rinomina nel file definitivo
            File.Move(crdownloadPath, finalZipPath);

            bool foundFinal = WaitForCondition(watcher, () =>
                _db.Search("progetto_architettura", 10).Any(r => r.FullPath.Equals(finalZipPath, StringComparison.OrdinalIgnoreCase)));

            Assert.IsTrue(foundFinal, "Il file rinominato dopo il download deve essere indicizzato tempestivamente.");
            Assert.AreEqual("Archivio", _db.Search("progetto_architettura", 10).First().Category);
        }

        [TestMethod]
        public void RealtimeFolderWatcher_Deletion_RemovesFileFromDatabase()
        {
            using var watcher = new RealtimeFolderWatcher(_db, new[] { _testDir }, debounceMs: 100);

            string filePath = Path.Combine(_testDir, "rimuovimi.txt");
            File.WriteAllText(filePath, "testo da eliminare");

            bool inserted = WaitForCondition(watcher, () => _db.Search("rimuovimi", 10).Count > 0);
            Assert.IsTrue(inserted, "Il file iniziale deve essere presente prima della cancellazione.");

            // Elimina il file fisico
            File.Delete(filePath);

            bool removed = WaitForCondition(watcher, () => _db.Search("rimuovimi", 10).Count == 0);
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

            bool allIndexed = WaitForCondition(watcher, () => _db.Search("batch_file", 20).Count >= fileCount);
            Assert.IsTrue(allIndexed, "Tutti i file creati in raffica devono essere indicizzati tramite micro-batching.");
        }
    }
}
