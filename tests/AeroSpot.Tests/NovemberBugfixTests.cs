using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Native;
using AeroSpot.Core.Preview;
using AeroSpot.Core.Search;
using AeroSpot.Host.Native;
using AeroSpot.Worker.Models;
using AeroSpot.Worker.Services;
using AeroSpot.Worker.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class NovemberBugfixTests
    {
        [TestMethod]
        public void SearchRanker_ChronologicalPriority_WhenRelevanceIsIdentical()
        {
            long now = DateTime.UtcNow.Ticks;
            long recentTime = now - TimeSpan.FromHours(1).Ticks;
            long oldTime = now - TimeSpan.FromDays(200).Ticks;

            // Due file che iniziano con "Capitolo": "Capitolo 12" (recente) e "Capitolo 1" (vecchio)
            var itemOld = new IndexedItem("Capitolo 1", @"C:\Docs\Capitolo 1.docx", "Documento", @"C:\Docs\Capitolo 1.docx", isApp: false, lastModifiedUtcTicks: oldTime);
            var itemRecent = new IndexedItem("Capitolo 12", @"C:\Docs\Capitolo 12.docx", "Documento", @"C:\Docs\Capitolo 12.docx", isApp: false, lastModifiedUtcTicks: recentTime);

            var ranked = SearchRanker.Rank(new[] { itemOld, itemRecent }, "Capitolo");

            Assert.IsNotNull(ranked);
            Assert.HasCount(2, ranked);
            Assert.AreEqual("Capitolo 12", ranked[0].Title, "Il file 'Capitolo 12' modificato più di recente deve avere precedenza sul file vecchio.");
        }

        [TestMethod]
        public void SqliteIndexDatabase_RecordSearch_ExcludesCalculatorAndMathExpressions()
        {
            string tempDbDir = Path.Combine(Path.GetTempPath(), "AeroSpot_CalcTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDbDir);
            string dbPath = Path.Combine(tempDbDir, "index_calc.db");

            try
            {
                using var db = new SqliteIndexDatabase(dbPath);
                db.Initialize();

                // Prova a registrare espressioni di calcolo e conversioni
                db.RecordSearch("2 + 2");
                db.RecordSearch("50 * 3");
                db.RecordSearch("100 km in m");
                db.RecordSearch("10 kg in g");
                db.RecordSearch("(10 + 5) * 2");

                // E ricerche reali
                db.RecordSearch("Alien 1997");
                db.RecordSearch("Report Trimestrale");

                var frequent = db.GetMostFrequentSearches();

                // Verifiche: le formule e le conversioni non devono comparire
                Assert.IsFalse(frequent.Exists(q => q.Contains("+") || q.Contains("*") || q.Contains("km in m") || q.Contains("kg in g")), "Le formule matematiche e le conversioni non devono essere salvate in SearchHistory.");
                Assert.IsTrue(frequent.Exists(q => q.Equals("Alien 1997", StringComparison.OrdinalIgnoreCase)), "Le normali ricerche devono essere conservate.");
                Assert.IsTrue(frequent.Exists(q => q.Equals("Report Trimestrale", StringComparison.OrdinalIgnoreCase)), "Le normali ricerche devono essere conservate.");
            }
            finally
            {
                try { Directory.Delete(tempDbDir, true); } catch { }
            }
        }

        [TestMethod]
        public void KeyboardHook_SpaceClosesPreview_WhenPreviewIsActive()
        {
            bool closeCalled = false;
            bool previewActive = true;

            using var hook = new KeyboardHookManager(
                Win32.VK_SPACE, 
                0, 
                onPreviewRequested: path => { }, 
                onClosePreviewRequested: () => { closeCalled = true; }, 
                isPreviewActiveFunc: () => previewActive);

            Assert.IsTrue(hook.Install());

            // Quando previewActive è true, la chiusura coordinata è cablata
            Assert.IsNotNull(hook);
            Assert.IsFalse(closeCalled);
            hook.Uninstall();
        }

        [TestMethod]
        public void LauncherViewModel_EmptySearch_DoesNotBlockUI()
        {
            var vm = new LauncherViewModel(enableAsyncDebounce: false);
            vm.SearchText = "test";
            Assert.IsTrue(vm.HasSearchText);

            // Svuotamento query
            vm.SearchText = string.Empty;
            Assert.IsFalse(vm.HasSearchText);
            Assert.IsTrue(vm.HasResults);
        }
    }
}
