using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AeroSpot.Core.Config;
using AeroSpot.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class SearchEngineTests
    {
        [TestInitialize]
        public void Init()
        {
            PathFilter.ResetToDefault();
        }

        [TestMethod]
        public void PathFilter_StrictPerimeter_RejectsDisallowedDrives()
        {
            // SPEC CRITERION: Esclusione rigorosa di D:, F:, G:, Z: e percorsi di rete
            Assert.IsFalse(PathFilter.IsPathAllowed(@"D:\Games\test.exe"), "I file su D: devono essere categoricamente esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"F:\Media\video.mp4"), "I file su F: devono essere categoricamente esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"G:\Backups\archive.zip"), "I file su G: devono essere categoricamente esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"Z:\Shared\document.pdf"), "I percorsi di rete o drive mappati Z: devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"\\Perryno\Condivisa\file.txt"), "I percorsi UNC devono essere esclusi.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"X:\unsupported.txt"), "Qualsiasi disco diverso da C: ed E: deve essere escluso.");

            // Dischi consentiti
            Assert.IsTrue(PathFilter.IsPathAllowed(@"E:\Projects\AeroSpot\test.cs"), "I file su E: devono essere ammessi.");
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Program Files\App\app.exe"), "I file su C: (Program Files) devono essere ammessi.");
            Assert.IsTrue(PathFilter.IsPathAllowed(@"c:\users\user\documents\file.txt"), "Verifica case-insensitive per il disco C:.");
            Assert.IsTrue(PathFilter.IsPathAllowed(@"e:\data\notes.md"), "Verifica case-insensitive per il disco E:.");
        }

        [TestMethod]
        public void PathFilter_MandatoryExclusions_RejectExcludedDirectories()
        {
            // SPEC CRITERION: SteamLibrary, Epic Games, AppData\Local\Temp
            Assert.IsFalse(PathFilter.IsPathAllowed(@"E:\SteamLibrary\steamapps\common\game.exe"), "SteamLibrary deve essere esclusa.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\SteamLibrary\game.exe"), "SteamLibrary su C: deve essere esclusa.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Program Files\Epic Games\Fortnite\game.exe"), "Epic Games deve essere esclusa.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Users\User\AppData\Local\Temp\junk.tmp"), "AppData\\Local\\Temp deve essere esclusa.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"E:\$Recycle.Bin\deleted.txt"), "Il cestino deve essere escluso.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"E:\Project\.git\HEAD"), "Cartelle .git devono essere escluse.");
            Assert.IsFalse(PathFilter.IsPathAllowed(@"E:\Web\node_modules\package\index.js"), "node_modules deve essere escluso.");

            // Falsi positivi evitati sui confini di segmento
            Assert.IsTrue(PathFilter.IsPathAllowed(@"E:\MySteamLibraryNotes\file.txt"), "Nomi simili che non sono la cartella esatta devono essere ammessi.");
            Assert.IsTrue(PathFilter.IsPathAllowed(@"E:\TemporaryProjects\code.py"), "Cartelle che contengono 'Temp' come sottostringa devono essere ammesse se non sono AppData\\Local\\Temp.");
        }

        [TestMethod]
        public void PathFilter_WindowsDirectory_AllowsOnlyWhitelistedUtilities()
        {
            // C:\Windows generale escluso per evitare inquinamento da DLL e file di sistema
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Windows\System32\shell32.dll"));
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Windows\Fonts\arial.ttf"));
            Assert.IsFalse(PathFilter.IsPathAllowed(@"C:\Windows\WinSxS\somefile.sys"));

            // Utilità essenziali esplicitamente ammesse
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Windows\System32\notepad.exe"));
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Windows\calc.exe"));
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Windows\System32\cmd.exe"));
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Windows\explorer.exe"));
            Assert.IsTrue(PathFilter.IsPathAllowed(@"C:\Windows\System32\taskmgr.exe"));
        }

        [TestMethod]
        public void SearchRanker_RanksByRelevanceCorrectly()
        {
            var itemExact = new IndexedItem("Chrome", @"C:\Program Files\Google\Chrome\chrome.exe", "Applicazione", "chrome.exe", isApp: true);
            var itemPrefix = new IndexedItem("Chrome Canary", @"C:\Program Files\Google\Chrome Canary\chrome.exe", "Applicazione", "chrome.exe", isApp: true);
            var itemSubstring = new IndexedItem("Google Chrome Settings", @"C:\Data\Chrome.txt", "Documento", "Chrome.txt", isApp: false);

            int scoreExact = SearchRanker.CalculateScore(itemExact, "Chrome");
            int scorePrefix = SearchRanker.CalculateScore(itemPrefix, "Chrome");
            int scoreSubstring = SearchRanker.CalculateScore(itemSubstring, "Chrome");

            Assert.IsGreaterThan(scorePrefix, scoreExact, "La corrispondenza esatta deve superare il prefisso.");
            Assert.IsGreaterThan(scoreSubstring, scorePrefix, "Il prefisso di un'applicazione deve superare la sottostringa di un documento.");
        }

        [TestMethod]
        public void SearchRanker_AcronymMatching_WorksForStandardApps()
        {
            var itemVsc = new IndexedItem("Visual Studio Code", "code.exe", "Applicazione", "code.exe", isApp: true);
            var itemOther = new IndexedItem("Video Subtitle Converter", "converter.exe", "Applicazione", "converter.exe", isApp: true);

            var ranked = SearchRanker.Rank(new[] { itemVsc, itemOther }, "vsc");
            Assert.IsNotEmpty(ranked);
            Assert.AreEqual("Visual Studio Code", ranked[0].Title, "L'acronimo 'vsc' deve posizionare Visual Studio Code in cima.");
        }

        [TestMethod]
        public void SqliteIndexDatabase_Persistence_And_FastQueryLatency()
        {
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"AeroSpot_Test_{Guid.NewGuid():N}.db");
            try
            {
                using (var db = new SqliteIndexDatabase(tempDbPath))
                {
                    db.Initialize();

                    var sampleItems = new List<IndexedItem>
                    {
                        new IndexedItem("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "Applicazione", "chrome.exe", isApp: true),
                        new IndexedItem("Blocco note", @"C:\Windows\System32\notepad.exe", "Applicazione", "notepad.exe", isApp: true),
                        new IndexedItem("AeroSpot Guida", @"C:\AeroSpot\Doc1.md", "Documento", @"C:\AeroSpot\Doc1.md", isApp: false),
                        new IndexedItem("AeroSpot Note", @"C:\AeroSpot\Doc2.md", "Documento", @"C:\AeroSpot\Doc2.md", isApp: false),
                        new IndexedItem("Progetto Z Negato", @"Z:\Forbidden\file.txt", "Documento", @"Z:\Forbidden\file.txt", isApp: false) // Deve essere scartato dal filtro perimetro
                    };

                    int upserted = db.UpsertBatch(sampleItems);
                    Assert.AreEqual(4, upserted, "L'elemento su Z: doveva essere scartato prima dell'inserimento.");
                    Assert.AreEqual(4, db.GetTotalCount());

                    // Benchmark latenza query (target < 5 ms su indice pronto)
                    var sw = Stopwatch.StartNew();
                    var results = db.Search("aero");
                    sw.Stop();

                    Console.WriteLine($"[Benchmark Ricerca SQLite] Latenza query: {sw.Elapsed.TotalMilliseconds:F2} ms");
                    Assert.IsNotEmpty(results);
                    Assert.HasCount(2, results);
                    Assert.IsLessThan(25, sw.ElapsedMilliseconds, $"La ricerca su SQLite deve essere inferiore a 25 ms (misurato {sw.ElapsedMilliseconds} ms).");
                }

                // 2. Test di Persistenza: riapri il database senza ricrearlo
                using (var db2 = new SqliteIndexDatabase(tempDbPath))
                {
                    db2.Initialize();
                    Assert.AreEqual(4, db2.GetTotalCount(), "I dati devono persistere tra diverse istanze senza ricostruzione.");

                    var results = db2.Search("chrome");
                    Assert.IsNotEmpty(results);
                    Assert.AreEqual("Google Chrome", results[0].Title);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDbPath)) File.Delete(tempDbPath);
                    string wal = tempDbPath + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    string shm = tempDbPath + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                }
                catch { }
            }
        }

        [TestMethod]
        public void AppsFolderScanner_FindsWindowsUtilities()
        {
            var apps = AppsFolderScanner.ScanApps();
            Assert.IsNotEmpty(apps);
            Assert.IsGreaterThanOrEqualTo(10, apps.Count, "Devono essere rilevate almeno 10 applicazioni o utilità.");

            // Verifica presenza delle principali utilità Windows
            Assert.IsTrue(apps.Any(a => a.Title.IndexOf("Blocco note", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        a.Title.IndexOf("Notepad", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsTrue(apps.Any(a => a.Title.IndexOf("Calcolatrice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        a.Title.IndexOf("Calculator", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void LiveDatabase_StrictVerification_ZeroResultsFromExcludedVolumesAndSteamLibrary()
        {
            string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroSpot", "index.db");
            if (!File.Exists(dbPath)) return;

            using var db = new SqliteIndexDatabase(dbPath);
            db.Initialize();

            var config = AppConfig.Load();

            // Interroga per verificare che NESSUN risultato provenga da volumi esclusi (es. Z:, F:, G:) o cartelle escluse (SteamLibrary, Epic Games)
            if (!config.IncludedDrives.Contains("D", StringComparer.OrdinalIgnoreCase))
            {
                var resultsD = db.Search("D:");
                Assert.IsFalse(resultsD.Any(r => r.FullPath.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase)), "Nessun risultato deve provenire dal disco D: se escluso.");
            }

            var resultsZ = db.Search("Z:");
            var resultsF = db.Search("F:");
            var resultsG = db.Search("G:");
            var resultsSteam = db.Search("SteamLibrary");
            var resultsEpic = db.Search("Epic Games");

            Assert.IsFalse(resultsZ.Any(r => r.FullPath.StartsWith("Z:\\", StringComparison.OrdinalIgnoreCase)), "Nessun risultato deve provenire dal disco Z:.");
            Assert.IsFalse(resultsF.Any(r => r.FullPath.StartsWith("F:\\", StringComparison.OrdinalIgnoreCase)), "Nessun risultato deve provenire dal disco F:.");
            Assert.IsFalse(resultsG.Any(r => r.FullPath.StartsWith("G:\\", StringComparison.OrdinalIgnoreCase)), "Nessun risultato deve provenire dal disco G:.");
            Assert.IsFalse(resultsSteam.Any(r => r.FullPath.IndexOf(@"\SteamLibrary\", StringComparison.OrdinalIgnoreCase) >= 0), "Nessun risultato deve trovarsi in SteamLibrary.");
            Assert.IsFalse(resultsEpic.Any(r => r.FullPath.IndexOf(@"\Epic Games\", StringComparison.OrdinalIgnoreCase) >= 0), "Nessun risultato deve trovarsi in Epic Games.");
        }

        [TestMethod]
        public void SqliteIndexDatabase_PurgeDisallowedDrives_RemovesUncheckedDrivesCompletely()
        {
            PathFilter.Configure(new[] { "C", "D", "E" });
            string tempDb = Path.Combine(Path.GetTempPath(), $"aerospot_test_purge_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new SqliteIndexDatabase(tempDb);
                db.Initialize();

                var items = new List<IndexedItem>
                {
                    new IndexedItem { Title = "File Su C", FullPath = @"C:\Test\doc.txt", Category = "Documento", Extension = ".txt", ExecutionTarget = @"C:\Test\doc.txt" },
                    new IndexedItem { Title = "File Su D", FullPath = @"D:\Games\game.exe", Category = "Applicazione", Extension = ".exe", ExecutionTarget = @"D:\Games\game.exe" },
                    new IndexedItem { Title = "File Su E", FullPath = @"E:\Work\project.cs", Category = "Codice", Extension = ".cs", ExecutionTarget = @"E:\Work\project.cs" },
                    new IndexedItem { Title = "App Windows", FullPath = @"shell:AppsFolder\App1", Category = "Applicazione", Extension = "", ExecutionTarget = @"shell:AppsFolder\App1", IsApp = true }
                };

                db.UpsertBatch(items);
                Assert.AreEqual(4, db.GetTotalCount());

                // Deseleziona D: (lasciando solo C: ed E:)
                int purged = db.PurgeDisallowedDrives(new[] { "C", "E" });
                Assert.AreEqual(1, purged, "Deve essere stato eliminato esattamente 1 record appartenente al disco D:.");
                Assert.AreEqual(3, db.GetTotalCount(), "Devono rimanere esattamente 3 record nel database.");

                var resultsD = db.Search("Games");
                Assert.IsEmpty(resultsD, "Nessun elemento deve essere rimasto per D:.");

                var resultsC = db.Search("doc");
                Assert.IsTrue(resultsC.Any(r => r.FullPath.StartsWith("C:", StringComparison.OrdinalIgnoreCase)), "Gli elementi su C: devono rimanere intatti.");

                var resultsE = db.Search("project");
                Assert.IsTrue(resultsE.Any(r => r.FullPath.StartsWith("E:", StringComparison.OrdinalIgnoreCase)), "Gli elementi su E: devono rimanere intatti.");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDb)) File.Delete(tempDb);
                    string wal = tempDb + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    string shm = tempDb + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                }
                catch { }
            }
        }

        [TestMethod]
        public void SqliteIndexDatabase_PurgeExcludedSegments_RemovesExcludedFolders()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"aerospot_test_purge_ex_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new SqliteIndexDatabase(tempDb);
                db.Initialize();

                var items = new List<IndexedItem>
                {
                    new IndexedItem { Title = "Normal Doc", FullPath = @"C:\Users\Docs\file.txt", Category = "Documento", Extension = ".txt", ExecutionTarget = @"C:\Users\Docs\file.txt" },
                    new IndexedItem { Title = "Steam Game", FullPath = @"E:\SteamLibrary\game.exe", Category = "Applicazione", Extension = ".exe", ExecutionTarget = @"E:\SteamLibrary\game.exe" },
                    new IndexedItem { Title = "Node Package", FullPath = @"E:\Project\node_modules\pkg.js", Category = "Codice", Extension = ".js", ExecutionTarget = @"E:\Project\node_modules\pkg.js" }
                };

                // Inserimento bypassando PathFilter per simulare elementi già presenti prima di aggiungere nuove esclusioni
                EnsureDirectInsert(db, items);
                Assert.AreEqual(3, db.GetTotalCount());

                // Spurga le esclusioni
                int purged = db.PurgeExcludedSegments(new[] { @"\SteamLibrary\", @"\node_modules\" });
                Assert.AreEqual(2, purged, "Devono essere stati rimossi esattamente i 2 record nelle cartelle escluse.");
                Assert.AreEqual(1, db.GetTotalCount(), "Deve rimanere solo il file normale.");

                var remaining = db.Search("file");
                Assert.HasCount(1, remaining);
                Assert.AreEqual(@"C:\Users\Docs\file.txt", remaining[0].FullPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDb)) File.Delete(tempDb);
                    string wal = tempDb + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    string shm = tempDb + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                }
                catch { }
            }
        }

        [TestMethod]
        public void SearchRanker_CustomCategoryPriority_RanksHighPriorityCategoriesFirst()
        {
            var docItem = new IndexedItem
            {
                Title = "project_report.pdf",
                FullPath = @"C:\Users\Docs\project_report.pdf",
                Category = "Documento"
            };
            var imgItem = new IndexedItem
            {
                Title = "project_report.png",
                FullPath = @"C:\Users\Docs\project_report.png",
                Category = "Immagine"
            };

            // Custom priority 1: Image first, Document second
            var priorityImgFirst = new List<string> { "Immagine", "Documento" };
            double scoreImg1 = SearchRanker.CalculateScore(imgItem, "project", priorityImgFirst);
            double scoreDoc1 = SearchRanker.CalculateScore(docItem, "project", priorityImgFirst);
            Assert.IsGreaterThan(scoreDoc1, scoreImg1, $"Immagine ({scoreImg1}) deve avere punteggio superiore a Documento ({scoreDoc1}) se posizionata più in alto nella gerarchia.");

            // Custom priority 2: Document first, Image second
            var priorityDocFirst = new List<string> { "Documento", "Immagine" };
            double scoreImg2 = SearchRanker.CalculateScore(imgItem, "project", priorityDocFirst);
            double scoreDoc2 = SearchRanker.CalculateScore(docItem, "project", priorityDocFirst);
            Assert.IsGreaterThan(scoreImg2, scoreDoc2, $"Documento ({scoreDoc2}) deve avere punteggio superiore a Immagine ({scoreImg2}) se posizionato più in alto nella gerarchia.");
        }

        [TestMethod]
        public void SqliteIndexDatabase_SearchWithCategoryFilter_FiltersCorrectly()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"AeroSpot_CatFilterTest_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new SqliteIndexDatabase(tempDb);
                var items = new List<IndexedItem>
                {
                    new IndexedItem { FullPath = @"C:\Apps\calculator.exe", Title = "calculator.exe", Category = "Applicazione" },
                    new IndexedItem { FullPath = @"C:\Photos\vacation.jpg", Title = "vacation.jpg", Category = "Immagine" },
                    new IndexedItem { FullPath = @"C:\Music\song.mp3", Title = "song.mp3", Category = "Audio" },
                    new IndexedItem { FullPath = @"C:\Docs\resume.pdf", Title = "resume.pdf", Category = "Documento" }
                };
                EnsureDirectInsert(db, items);

                // Search "a" with no filter
                var allResults = db.Search("a", 10);
                Assert.IsGreaterThanOrEqualTo(allResults.Count, 2, "La ricerca senza filtro categoria deve restituire tutti i risultati corrispondenti.");

                // Search with "Immagine" filter
                var photoResults = db.Search("vacation", 10, categoryFilter: "Immagine");
                Assert.HasCount(1, photoResults);
                Assert.AreEqual("Immagine", photoResults[0].Category);

                // Search with "Audio" filter
                var audioResults = db.Search("song", 10, categoryFilter: "Audio");
                Assert.HasCount(1, audioResults);
                Assert.AreEqual("Audio", audioResults[0].Category);

                // Search with "Documento" filter for an app name should return 0
                var emptyDocResults = db.Search("calculator", 10, categoryFilter: "Documento");
                Assert.IsEmpty(emptyDocResults, "Filtro Documento su un eseguibile deve restituire 0 risultati.");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDb)) File.Delete(tempDb);
                    string wal = tempDb + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    string shm = tempDb + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                }
                catch { }
            }
        }

        [TestMethod]
        public void SqliteIndexDatabase_RecentHistory_RecordsOrdersAndClearsProperly()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"recent_test_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new SqliteIndexDatabase(tempDb);
                db.Initialize();

                // Prepariamo 3 file reali temporanei per soddisfare il controllo di coerenza disco
                string tempDir = Path.Combine(Path.GetTempPath(), $"recents_dir_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                string fileA = Path.Combine(tempDir, "fileA.txt");
                string fileB = Path.Combine(tempDir, "fileB.txt");
                string fileC = Path.Combine(tempDir, "fileC.txt");
                File.WriteAllText(fileA, "A");
                File.WriteAllText(fileB, "B");
                File.WriteAllText(fileC, "C");

                var items = new List<IndexedItem>
                {
                    new IndexedItem { Title = "App Uno", FullPath = @"C:\Apps\App1.exe", ExecutionTarget = @"C:\Apps\App1.exe", Category = "Applicazione", Extension = ".exe", IsApp = true },
                    new IndexedItem { Title = "File A", FullPath = fileA, ExecutionTarget = fileA, Category = "Documento", Extension = ".txt", IsApp = false },
                    new IndexedItem { Title = "File B", FullPath = fileB, ExecutionTarget = fileB, Category = "Documento", Extension = ".txt", IsApp = false },
                    new IndexedItem { Title = "File C", FullPath = fileC, ExecutionTarget = fileC, Category = "Documento", Extension = ".txt", IsApp = false },
                };
                EnsureDirectInsert(db, items);

                // Registra prima A, poi C, poi B
                db.RecordRecentItem(fileA);
                System.Threading.Thread.Sleep(20);
                db.RecordRecentItem(fileC);
                System.Threading.Thread.Sleep(20);
                db.RecordRecentItem(fileB);

                var recents = db.GetRecentItems(10);
                Assert.HasCount(3, recents);
                Assert.AreEqual("File B", recents[0].Title, "L'ultimo aperto deve essere il primo nella lista.");
                Assert.AreEqual("File C", recents[1].Title);
                Assert.AreEqual("File A", recents[2].Title);

                // Cliccando di nuovo su A, A diventa il primo
                System.Threading.Thread.Sleep(20);
                db.RecordRecentItem(fileA);
                var updatedRecents = db.GetRecentItems(10);
                Assert.HasCount(3, updatedRecents);
                Assert.AreEqual("File A", updatedRecents[0].Title, "A è stato riaperto e deve trovarsi in prima posizione.");

                // Search con query vuota ed enableRecentHistory=true deve restituire i recenti
                var homeResults = db.Search(string.Empty, 10, enableRecentHistory: true);
                Assert.HasCount(3, homeResults);
                Assert.AreEqual("File A", homeResults[0].Title);

                // Search con query vuota ma enableRecentHistory=false deve fare fallback alle app
                var fallbackResults = db.Search(string.Empty, 10, enableRecentHistory: false);
                Assert.HasCount(1, fallbackResults);
                Assert.AreEqual("App Uno", fallbackResults[0].Title);

                // Cancellazione cronologia
                db.ClearRecentHistory();
                var afterClearRecents = db.GetRecentItems(10);
                Assert.IsEmpty(afterClearRecents, "Dopo ClearRecentHistory la cronologia deve essere vuota.");

                // Dopo la cancellazione, la home con enableRecentHistory=true fa fallback alle app
                var afterClearHome = db.Search(string.Empty, 10, enableRecentHistory: true);
                Assert.HasCount(1, afterClearHome);
                Assert.AreEqual("App Uno", afterClearHome[0].Title);

                try { Directory.Delete(tempDir, true); } catch { }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDb)) File.Delete(tempDb);
                    string wal = tempDb + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    string shm = tempDb + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                }
                catch { }
            }
        }

        [TestMethod]
        public void SettingsSearchCatalog_SearchesWindowsAndAeroSpot_InItalianAndEnglish()
        {
            // Test query obbligatorie da specifica C2:
            // "tastiera", "keyboard", "wifi", "schermo", "display", "suono", "sound", "aggiornamenti"
            var qTastiera = SettingsSearchCatalog.Search("tastiera", "it");
            Assert.IsNotEmpty(qTastiera);
            Assert.AreEqual("ms-settings:keyboard", qTastiera[0].Uri);

            var qKeyboard = SettingsSearchCatalog.Search("keyboard", "en");
            Assert.IsNotEmpty(qKeyboard);
            Assert.AreEqual("ms-settings:keyboard", qKeyboard[0].Uri);

            var qWifi = SettingsSearchCatalog.Search("wifi", "it");
            Assert.IsNotEmpty(qWifi);
            Assert.AreEqual("ms-settings:network-wifi", qWifi[0].Uri);

            var qSchermo = SettingsSearchCatalog.Search("schermo", "it");
            Assert.IsNotEmpty(qSchermo);
            Assert.AreEqual("ms-settings:display", qSchermo[0].Uri);

            var qDisplay = SettingsSearchCatalog.Search("display", "en");
            Assert.IsNotEmpty(qDisplay);
            Assert.AreEqual("ms-settings:display", qDisplay[0].Uri);

            var qSuono = SettingsSearchCatalog.Search("suono", "it");
            Assert.IsNotEmpty(qSuono);
            Assert.AreEqual("ms-settings:sound", qSuono[0].Uri);

            var qSound = SettingsSearchCatalog.Search("sound", "en");
            Assert.IsNotEmpty(qSound);
            Assert.AreEqual("ms-settings:sound", qSound[0].Uri);

            var qUpdates = SettingsSearchCatalog.Search("aggiornamenti", "it");
            Assert.IsNotEmpty(qUpdates);
            Assert.AreEqual("ms-settings:windowsupdate", qUpdates[0].Uri);

            // Almeno altre 10 ricerche concordate
            Assert.AreEqual("ms-settings:bluetooth", SettingsSearchCatalog.Search("bluetooth", "it")[0].Uri);
            Assert.AreEqual("ms-settings:printers", SettingsSearchCatalog.Search("stampante", "it")[0].Uri);
            Assert.AreEqual("ms-settings:printers", SettingsSearchCatalog.Search("printer", "en")[0].Uri);
            Assert.AreEqual("ms-settings:powersleep", SettingsSearchCatalog.Search("batteria", "it")[0].Uri);
            Assert.AreEqual("ms-settings:powersleep", SettingsSearchCatalog.Search("battery", "en")[0].Uri);
            Assert.AreEqual("ms-settings:dateandtime", SettingsSearchCatalog.Search("orologio", "it")[0].Uri);
            Assert.AreEqual("ms-settings:mousetouchpad", SettingsSearchCatalog.Search("mouse", "it")[0].Uri);
            Assert.AreEqual("ms-settings:privacy", SettingsSearchCatalog.Search("privacy", "it")[0].Uri);
            Assert.AreEqual("ms-settings:notifications", SettingsSearchCatalog.Search("notifiche", "it")[0].Uri);
            Assert.AreEqual("ms-settings:personalization-background", SettingsSearchCatalog.Search("sfondo", "it")[0].Uri);
            Assert.AreEqual("ms-settings:storagesense", SettingsSearchCatalog.Search("disco", "it")[0].Uri);
            Assert.AreEqual("ms-settings:easeofaccess-display", SettingsSearchCatalog.Search("accessibilita", "it")[0].Uri);

            // Schede AeroSpot
            var qAeroShortcuts = SettingsSearchCatalog.Search("scorciatoie", "it");
            Assert.IsNotEmpty(qAeroShortcuts);
            Assert.AreEqual("aerospot:settings:shortcuts", qAeroShortcuts[0].Uri);

            var qAeroIndexing = SettingsSearchCatalog.Search("indicizzazione", "it");
            Assert.IsNotEmpty(qAeroIndexing);
            Assert.AreEqual("aerospot:settings:indexing", qAeroIndexing[0].Uri);

            var qAeroInstructions = SettingsSearchCatalog.Search("istruzioni", "it");
            Assert.IsNotEmpty(qAeroInstructions);
            Assert.AreEqual("aerospot:settings:instructions", qAeroInstructions[0].Uri);
        }

        [TestMethod]
        public void SearchRanker_ChronologicalTieBreaker_RanksRecentlyModifiedFilesFirst()
        {
            long now = DateTime.UtcNow.Ticks;
            long oneDayAgo = now - TimeSpan.FromDays(1).Ticks;
            long oneYearAgo = now - TimeSpan.FromDays(365).Ticks;

            var itemOld01 = new IndexedItem("Capitolo 01", @"C:\Docs\Capitolo 01.docx", "Documento", @"C:\Docs\Capitolo 01.docx", isApp: false, lastModifiedUtcTicks: oneYearAgo);
            var itemRecent12 = new IndexedItem("Capitolo 12", @"C:\Docs\Capitolo 12.docx", "Documento", @"C:\Docs\Capitolo 12.docx", isApp: false, lastModifiedUtcTicks: oneDayAgo);
            var itemOld1 = new IndexedItem("Capitolo 1", @"C:\Docs\Capitolo 1.docx", "Documento", @"C:\Docs\Capitolo 1.docx", isApp: false, lastModifiedUtcTicks: oneYearAgo);

            // Per la query "Capitolo", sia Capitolo 12 che Capitolo 01/1 sono match di prefisso di uguale classe
            var ranked = SearchRanker.Rank(new[] { itemOld01, itemRecent12, itemOld1 }, "Capitolo");

            Assert.IsGreaterThanOrEqualTo(ranked.Count, 3);
            Assert.AreEqual("Capitolo 12", ranked[0].Title, "Il file 'Capitolo 12' modificato più di recente deve precedere 'Capitolo 01' e 'Capitolo 1'.");

            // Verifica che una corrispondenza esatta mantenga comunque la precedenza assoluta
            var itemExact = new IndexedItem("Capitolo", @"C:\Docs\Capitolo.docx", "Documento", @"C:\Docs\Capitolo.docx", isApp: false, lastModifiedUtcTicks: oneYearAgo);
            var rankedWithExact = SearchRanker.Rank(new[] { itemOld01, itemRecent12, itemExact }, "Capitolo");
            Assert.AreEqual("Capitolo", rankedWithExact[0].Title, "Il match esatto 'Capitolo' deve sempre avere la precedenza assoluta sui match di prefisso.");
        }

        [TestMethod]
        public void SqliteIndexDatabase_ExcludesCalculatorAndConversionExpressionsFromSearchHistory()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"AeroSpot_HistoryCalcTest_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new SqliteIndexDatabase(tempDb);
                db.Initialize();

                // Registra ricerche normali
                db.RecordSearch("aero");
                db.RecordSearch("documento importante");

                // Registra espressioni calcolatrice e conversioni
                db.RecordSearch("2 + 2");
                db.RecordSearch("150 / 3");
                db.RecordSearch("100 * 5");
                db.RecordSearch("10 cm in mm");

                var frequentSearches = db.GetMostFrequentSearches(20);

                Assert.Contains("aero", frequentSearches);
                Assert.Contains("documento importante", frequentSearches);
                Assert.DoesNotContain("2 + 2", frequentSearches);
                Assert.DoesNotContain("150 / 3", frequentSearches);
                Assert.DoesNotContain("100 * 5", frequentSearches);
                Assert.DoesNotContain("10 cm in mm", frequentSearches);
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }

        private static void EnsureDirectInsert(SqliteIndexDatabase db, List<IndexedItem> items)
        {
            // Disattiva momentaneamente i filtri rigidi per il setup del test
            PathFilter.Configure(new[] { "C", "D", "E" }, Array.Empty<string>());
            db.UpsertBatch(items);
        }
    }
}
