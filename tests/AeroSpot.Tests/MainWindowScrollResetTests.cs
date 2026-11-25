using AeroSpot.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class MainWindowScrollResetTests
    {
        [TestMethod]
        public void MainWindow_ResetResultsScroll_ResetsSelectionAndScroll()
        {
            StaTestRunner.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    // Assicurati che ci siano elementi
                    Assert.IsNotEmpty(window.ViewModel.FilteredResults, "Dovrebbero esserci risultati nella lista.");

                    // Simula navigazione verso il basso (es. freccia giù due volte)
                    window.ViewModel.MoveSelectionDown();
                    window.ViewModel.MoveSelectionDown();
                    Assert.AreEqual(1, window.ViewModel.SelectedIndex, "L'indice selezionato dovrebbe essere 1 dopo due frecce giù.");

                    // Simula il reset all'apertura dello Spotlight
                    window.ViewModel.SearchText = string.Empty;
                    window.SearchBox.Text = string.Empty;
                    window.ViewModel.SelectedIndex = -1;
                    window.ViewModel.SelectedItem = null;
                    window.ResultsList.SelectedIndex = -1;
                    window.ResetResultsScroll();

                    Assert.AreEqual(-1, window.ViewModel.SelectedIndex, "Alla riapertura la selezione deve essere resettata a -1.");
                    Assert.IsNull(window.ViewModel.SelectedItem, "Alla riapertura SelectedItem deve essere null.");
                    Assert.AreEqual(-1, window.ResultsList.SelectedIndex, "ResultsList.SelectedIndex deve essere -1.");
                }
                finally
                {
                    window.ViewModel.Dispose();
                    window.Hide();
                }
            });
        }
    }
}
