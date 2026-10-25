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
    }
}
