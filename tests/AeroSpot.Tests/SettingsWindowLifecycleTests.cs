using System;
using System.Linq;
using System.Threading;
using System.Windows;
using AeroSpot.Core.Search;
using AeroSpot.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class SettingsWindowLifecycleTests
    {
        [TestMethod]
        public void SettingsWindow_ReloadSettings_ClearsStatusMessages()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var window = new SettingsWindow();
                    window.TxtStatus.Text = "✓ Impostazioni salvate correttamente";
                    Assert.IsFalse(string.IsNullOrEmpty(window.TxtStatus.Text));

                    // Calling ReloadSettings simulates reopening the window via ShowSettingsWindow
                    window.ReloadSettings();

                    Assert.AreEqual(string.Empty, window.TxtStatus.Text, "TxtStatus.Text should be cleared when ReloadSettings is called upon reopening.");
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(5));

            if (threadEx != null)
            {
                throw new AssertFailedException("SettingsWindow lifecycle test failed with exception: " + threadEx.Message, threadEx);
            }
        }

        [TestMethod]
        public void SettingsWindow_SelectTab_SwitchesSections()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var window = new SettingsWindow();

                    // Tab 0: Generali
                    window.SelectTab(0);
                    Assert.AreEqual(Visibility.Visible, window.GeneralSettingsPanel.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, window.QuickLookSettingsPanel.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, window.SpotSettingsPanel.Visibility);

                    // Tab 1: QuickLook
                    window.SelectTab(1);
                    Assert.AreEqual(Visibility.Collapsed, window.GeneralSettingsPanel.Visibility);
                    Assert.AreEqual(Visibility.Visible, window.QuickLookSettingsPanel.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, window.SpotSettingsPanel.Visibility);

                    // Tab 2: Spot
                    window.SelectTab(2);
                    Assert.AreEqual(Visibility.Collapsed, window.GeneralSettingsPanel.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, window.QuickLookSettingsPanel.Visibility);
                    Assert.AreEqual(Visibility.Visible, window.SpotSettingsPanel.Visibility);

                    // SelectTab by name string
                    window.SelectTab("quicklook");
                    Assert.AreEqual(Visibility.Visible, window.QuickLookSettingsPanel.Visibility);

                    window.SelectTab("spot");
                    Assert.AreEqual(Visibility.Visible, window.SpotSettingsPanel.Visibility);

                    window.SelectTab("general");
                    Assert.AreEqual(Visibility.Visible, window.GeneralSettingsPanel.Visibility);
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(5));

            if (threadEx != null)
            {
                throw new AssertFailedException("SettingsWindow tab switching test failed with exception: " + threadEx.Message, threadEx);
            }
        }

        [TestMethod]
        public void SettingsSearchCatalog_IndexesThreeSettingsSections()
        {
            var generalResults = SettingsSearchCatalog.Search("generale", "it");
            Assert.IsTrue(generalResults.Any(r => r.Uri == "aerospot:settings:general"));

            var qlResults = SettingsSearchCatalog.Search("quicklook", "it");
            Assert.IsTrue(qlResults.Any(r => r.Uri == "aerospot:settings:quicklook"));

            var spotResults = SettingsSearchCatalog.Search("spotlight", "it");
            Assert.IsTrue(spotResults.Any(r => r.Uri == "aerospot:settings:spot"));

            var enGeneral = SettingsSearchCatalog.Search("general", "en");
            Assert.IsTrue(enGeneral.Any(r => r.Uri == "aerospot:settings:general"));
        }

        [TestMethod]
        public void AppConfig_QuickLookAlwaysOnTop_DefaultsToFalse()
        {
            var config = new AeroSpot.Core.Config.AppConfig();
            Assert.IsFalse(config.QuickLookAlwaysOnTop, "QuickLookAlwaysOnTop deve essere false di default per consentire il comportamento contestuale.");
        }

        [TestMethod]
        public void SettingsWindow_QuickLookAlwaysOnTop_LoadsAndBindsProperly()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var window = new SettingsWindow();
                    Assert.IsNotNull(window.ChkAlwaysOnTop, "ChkAlwaysOnTop deve essere presente in SettingsWindow.");
                    Assert.IsFalse(window.ChkAlwaysOnTop.IsChecked ?? false);
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(5));

            if (threadEx != null)
            {
                throw new AssertFailedException("SettingsWindow AlwaysOnTop binding test failed: " + threadEx.Message, threadEx);
            }
        }

        [TestMethod]
        public void SettingsSearchCatalog_IndexesAlwaysOnTopKeywords()
        {
            var itResults = SettingsSearchCatalog.Search("primo piano", "it");
            Assert.IsTrue(itResults.Any(r => r.Uri == "aerospot:settings:quicklook"), "La ricerca 'primo piano' deve rimandare a quicklook.");

            var enResults = SettingsSearchCatalog.Search("always on top", "en");
            Assert.IsTrue(enResults.Any(r => r.Uri == "aerospot:settings:quicklook"), "La ricerca 'always on top' deve rimandare a quicklook.");
        }
    }
}
