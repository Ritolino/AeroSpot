using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Search;
using AeroSpot.Worker.Models;
using AeroSpot.Worker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class Round2FixesTests
    {
        [TestMethod]
        public void Localization_CompleteParity_AllKeysPresentInBothLanguages()
        {
            var dictEn = LocalizationService.English;
            var dictIt = LocalizationService.Italian;

            Assert.IsNotNull(dictEn);
            Assert.IsNotNull(dictIt);

            var missingInIt = dictEn.Keys.Except(dictIt.Keys).ToList();
            var missingInEn = dictIt.Keys.Except(dictEn.Keys).ToList();

            Assert.IsEmpty(missingInIt, $"Chiavi presenti in EN ma mancanti in IT: {string.Join(", ", missingInIt)}");
            Assert.IsEmpty(missingInEn, $"Chiavi presenti in IT ma mancanti in EN: {string.Join(", ", missingInEn)}");

            // Verifica che le nuove chiavi per C1 e C2 siano presenti
            Assert.IsTrue(dictIt.ContainsKey("Settings_HomeDisplayMode_Label"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_HomeMode_FrequentItems"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_HomeMode_FrequentSearches"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_HomeMode_RecentOpened"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_HomeMode_Alphabetical"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_HomeMode_None"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_RecentHistory_Info_Tooltip"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_RecentHistory_Info_Desc"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFrequency_Label"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFreq_Startup"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFreq_15Min"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFreq_30Min"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFreq_1Hour"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFreq_2Hours"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_IndexFreq_Manual"));
            Assert.IsTrue(dictIt.ContainsKey("Settings_DataDir_Btn_ReindexNow"));
            Assert.IsTrue(dictIt.ContainsKey("Spotlight_Category_Search"));

            // Verifica che l'indicazione del valore predefinito sia uniforme
            StringAssert.Contains(dictIt["Settings_HomeMode_FrequentSearches"], "(predefinito)");
            StringAssert.Contains(dictIt["Settings_Updates_Freq_Startup"], "(predefinito)");
            StringAssert.Contains(dictIt["Settings_IndexFreq_1Hour"], "(predefinito)");
            StringAssert.Contains(dictEn["Settings_HomeMode_FrequentSearches"], "(default)");
            StringAssert.Contains(dictEn["Settings_Updates_Freq_Startup"], "(default)");
            StringAssert.Contains(dictEn["Settings_IndexFreq_1Hour"], "(default)");
        }

        [TestMethod]
        public void SpotlightHomeMode_AllFourModes_BehaveAsExpected()
        {
            PathFilter.Configure(new[] { "C", "E" }, new string[0]);
            string tempDbDir = Path.Combine(Path.GetTempPath(), "AeroSpot_Round2_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDbDir);
            string dbPath = Path.Combine(tempDbDir, "index_round2.db");

            try
            {
                using var db = new SqliteIndexDatabase(dbPath);
                db.Initialize();

                // Popola app e documenti
                string dummyAppPath = Path.Combine(tempDbDir, "testapp.exe");
                File.WriteAllText(dummyAppPath, "binary");
                string dummyDocPath = Path.Combine(tempDbDir, "report.pdf");
                File.WriteAllText(dummyDocPath, "pdf content");

                db.UpsertBatch(new List<IndexedItem>
                {
                    new IndexedItem("Test App", dummyAppPath, "Applicazione", dummyAppPath, isApp: true),
                    new IndexedItem("Report Annuale", dummyDocPath, "Documento", dummyDocPath, isApp: false)
                });

                // 1. Nessuna cronologia registrata ancora
                // Modalità 4: None -> deve restituire lista vuota
                var noneEmpty = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.None);
                Assert.IsEmpty(noneEmpty, "Modalità None deve restituire 0 risultati a query vuota.");

                // Modalità 3: RecentOpened senza aperture -> fallback ad app
                var recentFallback = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.RecentOpened);
                Assert.HasCount(1, recentFallback);
                Assert.AreEqual("Test App", recentFallback[0].Title);

                // Registra aperture e ricerche
                db.RecordRecentItem(dummyDocPath); // aperto 1 volta
                db.RecordRecentItem(dummyAppPath);
                db.RecordRecentItem(dummyAppPath); // dummyAppPath aperto 2 volte
                db.RecordSearch("visual studio");
                db.RecordSearch("visual studio");
                db.RecordSearch("aero");

                // Modalità 1: MostFrequentlyOpened -> dummyAppPath ha 2 aperture, dummyDocPath 1
                var freqItems = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.MostFrequentlyOpened);
                Assert.HasCount(2, freqItems);
                Assert.AreEqual("Test App", freqItems[0].Title);
                Assert.AreEqual("Report Annuale", freqItems[1].Title);

                // Modalità 2: MostFrequentSearches -> 'visual studio' ha 2 ricerche, 'aero' 1
                var freqSearches = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.MostFrequentSearches);
                Assert.HasCount(2, freqSearches);
                Assert.AreEqual("visual studio", freqSearches[0].Title);
                Assert.AreEqual("Ricerca", freqSearches[0].Category);
                Assert.AreEqual("aero", freqSearches[1].Title);

                // Modalità 3: RecentOpened -> elementi aperti di recente
                var recents = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.RecentOpened);
                Assert.HasCount(2, recents);

                // Modalità 4: None -> resta sempre vuoto
                var noneWithHistory = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.None);
                Assert.IsEmpty(noneWithHistory);

                // Modalità 5: Alphabetical -> restituisce app ordinate alfabeticamente
                var alphabetical = db.Search(string.Empty, 10, homeMode: SpotlightHomeMode.Alphabetical);
                Assert.HasCount(1, alphabetical);
                Assert.AreEqual("Test App", alphabetical[0].Title);

                // Cancellazione cronologia totale
                db.ClearAllHistory();
                Assert.IsEmpty(db.GetRecentItems());
                Assert.IsEmpty(db.GetMostFrequentSearches());
            }
            finally
            {
                PathFilter.ResetToDefault();
                try { Directory.Delete(tempDbDir, true); } catch { }
            }
        }

        [TestMethod]
        public void CategoryPriority_SettingsRanking_RespectsUserHierarchy()
        {
            // Verifica che GetDefaultCategoryPriority contenga Impostazioni in posizione 0
            var defaultPriority = AppConfig.GetDefaultCategoryPriority();
            Assert.AreEqual("Impostazioni", defaultPriority[0], "Impostazioni deve essere prima di default.");

            // Verifica che il rank bonus rifletta la posizione nella gerarchia
            var itemApp = new IndexedItem("Settings Manager", @"C:\App\settings.exe", "Applicazione", @"C:\App\settings.exe", isApp: true);
            var itemDoc = new IndexedItem("Settings Guide", @"C:\Docs\settings.pdf", "Documento", @"C:\Docs\settings.pdf", isApp: false);

            var priorityAppFirst = new List<string> { "Applicazione", "Impostazioni", "Documento" };
            var priorityDocFirst = new List<string> { "Documento", "Applicazione", "Impostazioni" };

            int scoreAppWhenAppFirst = SearchRanker.CalculateScore(itemApp, "settings", priorityAppFirst);
            int scoreDocWhenAppFirst = SearchRanker.CalculateScore(itemDoc, "settings", priorityAppFirst);
            Assert.IsGreaterThan(scoreDocWhenAppFirst, scoreAppWhenAppFirst, "Quando Applicazione è prima di Documento, il suo punteggio deve essere superiore.");

            int scoreAppWhenDocFirst = SearchRanker.CalculateScore(itemApp, "settings", priorityDocFirst);
            int scoreDocWhenDocFirst = SearchRanker.CalculateScore(itemDoc, "settings", priorityDocFirst);
            Assert.IsGreaterThan(scoreAppWhenDocFirst, scoreDocWhenDocFirst, "Quando Documento è primo di Applicazione, il suo punteggio deve essere superiore.");
        }

        [TestMethod]
        public async Task PreviewLoader_AiFiles_RoutedToGenericCardWithoutBlackScreen()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "AeroSpot_AiTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string aiFilePath = Path.Combine(tempDir, "illustration.ai");
            File.WriteAllText(aiFilePath, "%!PS-Adobe-3.0\n%%Creator: Adobe Illustrator\n");

            try
            {
                var model = await PreviewLoader.LoadAsync(aiFilePath);
                Assert.IsNotNull(model);
                Assert.AreEqual(PreviewType.Generic, model.Type, "I file .ai devono essere instradati alla scheda generica sicura, mai a PreviewHandler COM difettosi.");
                Assert.IsNull(model.Image, "Nessuna immagine deve essere prodotta per evitare schermate nere.");
                Assert.Contains("Adobe Illustrator", model.Subtitle, "Il sottotitolo deve identificare chiaramente il formato AI.");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void PreviewLoader_RawExtensions_RecognizedCorrectly()
        {
            Assert.IsTrue(PreviewLoader.IsRawExtension(".arw"));
            Assert.IsTrue(PreviewLoader.IsRawExtension(".dng"));
            Assert.IsTrue(PreviewLoader.IsRawExtension(".cr2"));
            Assert.IsTrue(PreviewLoader.IsRawExtension(".cr3"));
            Assert.IsTrue(PreviewLoader.IsRawExtension(".nef"));
            Assert.IsFalse(PreviewLoader.IsRawExtension(".png"));
            Assert.IsFalse(PreviewLoader.IsRawExtension(".jpg"));
            Assert.IsFalse(PreviewLoader.IsRawExtension(".ai"));
        }

        [TestMethod]
        public void IndexUpdateFrequency_DefaultAndSerialization_BehavesCorrectly()
        {
            var config = new AppConfig();
            Assert.AreEqual(IndexUpdateFrequency.EveryHour, config.IndexUpdateFrequency, "La frequenza predefinita deve essere EveryHour.");

            string tempDir = Path.Combine(Path.GetTempPath(), "AeroSpot_IndexFreq_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string jsonPath = Path.Combine(tempDir, "config.json");

            try
            {
                config.IndexUpdateFrequency = IndexUpdateFrequency.Every15Minutes;
                config.Save(jsonPath);

                var loaded = AppConfig.Load(jsonPath);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(IndexUpdateFrequency.Every15Minutes, loaded.IndexUpdateFrequency);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void DeleteHotkey_DefaultAndCustomization_BehavesCorrectly()
        {
            var config = new AppConfig();
            Assert.AreEqual("Canc", config.DeleteHotkeyText);
            Assert.AreEqual(AeroSpot.Core.Native.Win32.VK_DELETE, config.DeleteKey);

            string tempDir = Path.Combine(Path.GetTempPath(), "AeroSpot_DeleteHotkey_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string jsonPath = Path.Combine(tempDir, "config.json");

            try
            {
                config.DeleteKey = 0x08; // Backspace
                config.DeleteHotkeyText = "Backspace";
                config.Save(jsonPath);

                var loaded = AppConfig.Load(jsonPath);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(0x08u, loaded.DeleteKey);
                Assert.AreEqual("Backspace", loaded.DeleteHotkeyText);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void IconService_SettingsIcons_DistinctAeroSpotAndWindows()
        {
            var windowsIcon = IconService.GetSettingsIcon();
            var aeroSpotIcon = IconService.GetAeroSpotSettingsIcon();

            Assert.IsNotNull(windowsIcon, "L'icona per le impostazioni Windows deve essere generata con successo.");
            Assert.IsNotNull(aeroSpotIcon, "L'icona per le impostazioni AeroSpot deve essere generata con successo.");
            Assert.AreNotSame(windowsIcon, aeroSpotIcon, "Le due icone devono essere istanze distinte con colorazioni diverse.");
        }
    }
}
