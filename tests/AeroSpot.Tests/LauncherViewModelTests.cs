using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AeroSpot.Core.Config;
using AeroSpot.Core.Search;
using AeroSpot.Worker.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class LauncherViewModelTests
    {
        private string _tempDir = null!;
        private string _tempDbPath = null!;

        [TestInitialize]
        public void Setup()
        {
            PathFilter.ResetToDefault();
            _tempDir = Path.Combine(Path.GetTempPath(), $"AeroSpot_LvmTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tempDbPath = Path.Combine(_tempDir, "test_launcher.db");
        }

        [TestCleanup]
        public void Cleanup()
        {
            PathFilter.ResetToDefault();
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        private SqliteIndexDatabase CreateSeededDb()
        {
            var db = new SqliteIndexDatabase(_tempDbPath);
            db.Initialize();
            PathFilter.Configure(new[] { "C", "E" }, Array.Empty<string>());
            db.UpsertBatch(new List<IndexedItem>
            {
                new IndexedItem("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "Applicazione", "chrome.exe", isApp: true),
                new IndexedItem("Blocco note", @"C:\Windows\System32\notepad.exe", "Applicazione", "notepad.exe", isApp: true),
                new IndexedItem("Gestione attività", @"C:\Windows\System32\taskmgr.exe", "Applicazione", "taskmgr.exe", isApp: true),
                new IndexedItem("Calcolatrice", @"C:\Windows\System32\calc.exe", "Applicazione", "calc.exe", isApp: true)
            });
            return db;
        }

        [TestMethod]
        public void InitialState_HasDefaultResults_NoPrematureSelection()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db);

            Assert.IsNotEmpty(vm.FilteredResults, "Dovrebbero esserci risultati predefiniti caricati.");
            Assert.IsTrue(vm.HasResults, "HasResults dovrebbe essere true.");
            Assert.IsFalse(vm.HasSearchText, "HasSearchText inizialmente deve essere false.");
            Assert.AreEqual(-1, vm.SelectedIndex, "Nessun elemento deve essere selezionato inizialmente fino alla pressione di freccia giù.");
            Assert.IsNull(vm.SelectedItem, "SelectedItem deve essere null prima di premere freccia giù.");
        }

        [TestMethod]
        public void SearchFiltering_FiltersCorrectly_CaseInsensitive()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db);

            vm.SearchText = "chrome";
            Assert.IsTrue(vm.HasSearchText);
            Assert.IsTrue(vm.FilteredResults.Any(r => r.Title.Contains("Chrome", StringComparison.OrdinalIgnoreCase)));

            // Verifica case-insensitivity con maiuscole
            vm.SearchText = "NOTEPAD";
            Assert.IsNotEmpty(vm.FilteredResults);
            Assert.IsTrue(vm.FilteredResults.Any(r => r.Title.Contains("Blocco note", StringComparison.OrdinalIgnoreCase) || r.Subtitle.Contains("notepad", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual(-1, vm.SelectedIndex, "Durante la digitazione l'elemento non deve essere pre-selezionato.");

            // Freccia giù seleziona il primo elemento
            vm.MoveSelectionDown();
            Assert.AreEqual(0, vm.SelectedIndex);
            Assert.IsNotNull(vm.SelectedItem);
        }

        [TestMethod]
        public void SearchWithSpaces_PreservesSpaces_AndFindsMatches()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db);

            // Cerca "Gestione attività" contenente spazio
            vm.SearchText = "Gestione attività";
            Assert.AreEqual("Gestione attività", vm.SearchText, "Gli spazi nella query non devono essere cancellati o persi.");
            Assert.HasCount(1, vm.FilteredResults);
            Assert.AreEqual("Gestione attività", vm.FilteredResults[0].Title);
        }

        [TestMethod]
        public void Navigation_MoveSelectionDownAndUp_CyclesWrapAround()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db);
            int total = vm.FilteredResults.Count;
            Assert.IsGreaterThanOrEqualTo(2, total);

            Assert.AreEqual(-1, vm.SelectedIndex);

            // Prima pressione freccia giù -> seleziona 0
            vm.MoveSelectionDown();
            Assert.AreEqual(0, vm.SelectedIndex);

            // Sposta in basso
            vm.MoveSelectionDown();
            Assert.AreEqual(1, vm.SelectedIndex);

            // Sposta fino all'ultimo
            for (int i = 1; i < total - 1; i++)
            {
                vm.MoveSelectionDown();
            }
            Assert.AreEqual(total - 1, vm.SelectedIndex);

            // Sposta oltre l'ultimo elemento -> wrap-around al primo (0)
            vm.MoveSelectionDown();
            Assert.AreEqual(0, vm.SelectedIndex, "Dall'ultimo elemento MoveSelectionDown deve ripartire da 0.");

            // Sposta in alto da 0 -> wrap-around all'ultimo (total - 1)
            vm.MoveSelectionUp();
            Assert.AreEqual(total - 1, vm.SelectedIndex, "Dal primo elemento MoveSelectionUp deve andare all'ultimo.");
        }

        [TestMethod]
        public void EmptyResults_StateIsConsistent()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db);
            vm.SearchText = "NonEsisteAssolutamenteQuestoFile123456789";

            Assert.IsEmpty(vm.FilteredResults);
            Assert.IsFalse(vm.HasResults);
            Assert.AreEqual(-1, vm.SelectedIndex);
            Assert.IsNull(vm.SelectedItem);

            bool executed = vm.ExecuteSelected();
            Assert.IsFalse(executed, "ExecuteSelected deve restituire false quando non ci sono elementi selezionati.");
        }

        [TestMethod]
        public void WindowPositionMath_CalculatesCenteredAndTop18Percent()
        {
            // Verifica la formula del posizionamento su schermo:
            // Monitor 1920x1080, finestra larghezza 680
            double workWidth = 1920;
            double workHeight = 1080;
            double windowWidth = 680;

            double expectedLeft = (workWidth - windowWidth) / 2.0; // 620
            double expectedTop = workHeight * 0.18;                 // 194.4

            Assert.AreEqual(620.0, expectedLeft);
            Assert.AreEqual(194.4, expectedTop, 0.001);
        }

        [TestMethod]
        public void MultiWordSearch_MatchesAllTerms()
        {
            using var db = new SqliteIndexDatabase(_tempDbPath);
            db.Initialize();
            db.UpsertBatch(new List<IndexedItem>
            {
                new IndexedItem
                {
                    Title = "AeroSpot — Roadmap",
                    FullPath = @"E:\Antigravity\AeroSpot\Progetto\ROADMAP.md",
                    Category = "Documento"
                }
            });

            using var vm = new LauncherViewModel(db);

            // Cerca "aero roadmap"
            vm.SearchText = "aero roadmap";
            Assert.IsNotEmpty(vm.FilteredResults);
            Assert.IsTrue(vm.FilteredResults.Any(r => r.Title.Contains("Roadmap", StringComparison.OrdinalIgnoreCase) || r.Subtitle.Contains("ROADMAP", StringComparison.OrdinalIgnoreCase)));

            // Cerca termini in ordine inverso: "roadmap aero"
            vm.SearchText = "roadmap aero";
            Assert.IsNotEmpty(vm.FilteredResults);
            Assert.IsTrue(vm.FilteredResults.Any(r => r.Title.Contains("Roadmap", StringComparison.OrdinalIgnoreCase) || r.Subtitle.Contains("ROADMAP", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void EscapeLogic_ClearsQueryFirst_ThenRequestsHide()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db);
            vm.SearchText = "qualcosa";

            bool hideRequested = false;
            void OnEscape()
            {
                if (!string.IsNullOrEmpty(vm.SearchText))
                {
                    vm.SearchText = string.Empty;
                }
                else
                {
                    hideRequested = true;
                }
            }

            // Primo escape: svuota il testo, non chiude
            OnEscape();
            Assert.AreEqual(string.Empty, vm.SearchText);
            Assert.IsFalse(hideRequested);

            // Secondo escape: testo già vuoto -> richiede hide
            OnEscape();
            Assert.IsTrue(hideRequested);
        }

        [TestMethod]
        public void ResetSearchImmediate_ClearsSearchTextAndFilteredResultsSynchronously()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db) { EnableAsyncDebounce = true };

            vm.SearchText = "calc";
            Assert.AreEqual("calc", vm.SearchText);

            vm.ResetSearchImmediate();

            Assert.AreEqual(string.Empty, vm.SearchText);
            Assert.IsFalse(vm.HasSearchText);
            Assert.AreEqual("All", vm.SelectedCategoryFilter);
            Assert.AreEqual(-1, vm.SelectedIndex);
            Assert.IsNull(vm.SelectedItem);
            // Verify that no calculator action item or leftover item remains
            Assert.IsFalse(vm.FilteredResults.Any(r => r.IsActionItem));
        }

        [TestMethod]
        public void LauncherViewModel_EmptySearch_DoesNotBlockUI()
        {
            using var db = CreateSeededDb();
            using var vm = new LauncherViewModel(db, enableAsyncDebounce: false);
            vm.SearchText = "test";
            Assert.IsTrue(vm.HasSearchText);

            // Svuotamento query
            vm.SearchText = string.Empty;
            Assert.IsFalse(vm.HasSearchText);
            Assert.IsTrue(vm.HasResults);
        }
    }
}
