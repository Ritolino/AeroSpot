using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class PreviewWindowStateMachineTests
    {
        [TestMethod]
        public void StateMachine_TransitionsCleanly_OnRapidToggles()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var win = new PreviewWindow();
                    Assert.AreEqual(PreviewWindow.PreviewState.Hidden, win.CurrentState);
                    Assert.IsFalse(win.IsPreviewVisible);

                    // Crea un file finto di test
                    string tempFile = Path.Combine(Path.GetTempPath(), $"state_machine_test_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(tempFile, "Hello Preview State Machine");

                    try
                    {
                        // 1. Simula 10 chiamate rapide consecutive a ShowPreviewForFileAsync (come premere Spazio ripetutamente)
                        for (int i = 0; i < 10; i++)
                        {
                            var task = win.ShowPreviewForFileAsync(tempFile, isFromSpotlight: false);
                            // Alcune chiamate subito toggle (annullano il caricamento), altre chiudono
                        }

                        // Alla fine, HidePreview() deve sempre riportare lo stato a Hidden in modo deterministico
                        win.HidePreview();
                        Assert.AreEqual(PreviewWindow.PreviewState.Hidden, win.CurrentState);
                        Assert.IsFalse(win.IsPreviewVisible);
                    }
                    finally
                    {
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
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
                throw new AssertFailedException("PreviewWindow state machine test failed: " + threadEx.Message, threadEx);
            }
        }

        [TestMethod]
        public void StateMachine_HidePreview_CancelsInFlightLoading()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var win = new PreviewWindow();

                    string tempFile = Path.Combine(Path.GetTempPath(), $"state_cancel_test_{Guid.NewGuid():N}.txt");
                    File.WriteAllText(tempFile, "Testing cancellation");

                    try
                    {
                        // Avvia il caricamento
                        var loadTask = win.ShowPreviewForFileAsync(tempFile, isFromSpotlight: false);

                        // Chiudi immediatamente prima che il task completi
                        win.HidePreview();

                        Assert.AreEqual(PreviewWindow.PreviewState.Hidden, win.CurrentState);
                        Assert.IsFalse(win.IsPreviewVisible);
                    }
                    finally
                    {
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
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
                throw new AssertFailedException("HidePreview cancellation test failed: " + threadEx.Message, threadEx);
            }
        }

        [TestMethod]
        public void PreviewWindow_AlwaysOnTop_ConfigurationAffectsTopmostOnDeactivated()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var win = new PreviewWindow();

                    // Case 1: QuickLookAlwaysOnTop = false -> OnDeactivated turns Topmost off
                    var configDefault = new AeroSpot.Core.Config.AppConfig { QuickLookAlwaysOnTop = false };
                    win.UpdateConfig(configDefault);
                    win.Topmost = true;

                    // Invoca OnDeactivated tramite reflection per testare la logica interna
                    var onDeactivatedMethod = typeof(PreviewWindow).GetMethod("OnDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Assert.IsNotNull(onDeactivatedMethod);
                    onDeactivatedMethod.Invoke(win, new object[] { EventArgs.Empty });

                    Assert.IsFalse(win.Topmost, "Quando QuickLookAlwaysOnTop è false, Topmost deve essere rilasciato alla deattivazione.");

                    // Case 2: QuickLookAlwaysOnTop = true -> OnDeactivated retains Topmost = true
                    var configPinned = new AeroSpot.Core.Config.AppConfig { QuickLookAlwaysOnTop = true };
                    win.UpdateConfig(configPinned);
                    Assert.IsTrue(win.Topmost, "UpdateConfig con QuickLookAlwaysOnTop = true deve impostare Topmost = true.");

                    onDeactivatedMethod.Invoke(win, new object[] { EventArgs.Empty });
                    Assert.IsTrue(win.Topmost, "Quando QuickLookAlwaysOnTop è true, Topmost deve rimanere true anche dopo OnDeactivated.");
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
                throw new AssertFailedException("PreviewWindow AlwaysOnTop deactivation test failed: " + threadEx.Message, threadEx);
            }
        }
    }
}
