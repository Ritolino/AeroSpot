using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class MediaSeekingDiagnosticTests
    {
        private static readonly string TestVideoPath = Path.Combine(TestEnvironment.TestsDir, "test_video.mp4");

        [TestMethod]
        public void T21_Diagnostic_MediaElement_Seeking_OnRealVideo()
        {
            if (!File.Exists(TestVideoPath))
            {
                Assert.Inconclusive($"Video non trovato a: {TestVideoPath}");
                return;
            }

            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null) new Application();

                    var player = new MediaElement
                    {
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Manual,
                        ScrubbingEnabled = true,
                        Source = new Uri(TestVideoPath, UriKind.Absolute)
                    };

                    bool opened = false;
                    Exception? error = null;
                    TimeSpan duration = TimeSpan.Zero;

                    player.MediaOpened += (s, e) =>
                    {
                        opened = true;
                        if (player.NaturalDuration.HasTimeSpan)
                        {
                            duration = player.NaturalDuration.TimeSpan;
                        }
                    };

                    player.MediaFailed += (s, e) =>
                    {
                        error = e.ErrorException;
                    };

                    player.Play();

                    // Wait for MediaOpened
                    var startWait = DateTime.UtcNow;
                    while (!opened && error == null && (DateTime.UtcNow - startWait).TotalSeconds < 5)
                    {
                        DoEvents();
                        Thread.Sleep(20);
                    }

                    Console.WriteLine($"[T2.1 DIAGNOSTIC] MediaOpened: {opened}, Duration: {duration}, Error: {error?.Message}");
                    Assert.IsTrue(opened, $"MediaOpened non scattato entro 5 secondi: {error?.Message}");
                    Assert.IsGreaterThan(1.0, duration.TotalSeconds, $"Durata video non valida: {duration}");

                    // TEST 1: Seek in Play mode (senza pause)
                    var target1 = TimeSpan.FromSeconds(duration.TotalSeconds * 0.5);
                    Console.WriteLine($"[T1] Seek diretto in Play mode al 50% ({target1})...");
                    player.Position = target1;

                    Thread.Sleep(50); DoEvents();
                    Console.WriteLine($"[T1] Pos dopo 50ms: {player.Position}");

                    Thread.Sleep(150); DoEvents();
                    Console.WriteLine($"[T1] Pos dopo 200ms: {player.Position}");

                    Thread.Sleep(300); DoEvents();
                    Console.WriteLine($"[T1] Pos dopo 500ms: {player.Position}");

                    Thread.Sleep(1000); DoEvents();
                    Console.WriteLine($"[T1] Pos dopo 1500ms: {player.Position}");

                    double diff1 = Math.Abs((player.Position - target1).TotalSeconds);
                    Console.WriteLine($"[T1 RESULT] Differenza rispetto al target: {diff1:F2} s");

                    // TEST 2: Flusso rapido Pause -> Seek -> Play immediato (<10ms, tipico del click sullo slider)
                    var target2 = TimeSpan.FromSeconds(duration.TotalSeconds * 0.75);
                    Console.WriteLine($"[T2] Flusso rapido: Pause() -> Position = {target2} -> Play() immediato...");
                    player.Pause();
                    DoEvents();
                    player.Position = target2;
                    DoEvents();
                    player.Play();
                    DoEvents();

                    Thread.Sleep(50); DoEvents();
                    Console.WriteLine($"[T2] Pos dopo 50ms: {player.Position}");

                    Thread.Sleep(150); DoEvents();
                    Console.WriteLine($"[T2] Pos dopo 200ms: {player.Position}");

                    Thread.Sleep(300); DoEvents();
                    Console.WriteLine($"[T2] Pos dopo 500ms: {player.Position}");

                    Thread.Sleep(1000); DoEvents();
                    Console.WriteLine($"[T2] Pos dopo 1500ms: {player.Position}");

                    double diff2 = Math.Abs((player.Position - target2).TotalSeconds);
                    Console.WriteLine($"[T2 RESULT] Differenza rispetto al target: {diff2:F2} s");

                    // TEST 3: Flusso in Pausa con ScrubbingEnabled = true
                    player.Pause();
                    DoEvents();
                    Thread.Sleep(200); DoEvents();

                    var target3 = TimeSpan.FromSeconds(duration.TotalSeconds * 0.25);
                    Console.WriteLine($"[T3] Seek in Pausa con ScrubbingEnabled = true a {target3}...");
                    player.Position = target3;

                    Thread.Sleep(100); DoEvents();
                    Console.WriteLine($"[T3] Pos dopo 100ms: {player.Position}");

                    Thread.Sleep(400); DoEvents();
                    Console.WriteLine($"[T3] Pos dopo 500ms: {player.Position}");

                    double diff3 = Math.Abs((player.Position - target3).TotalSeconds);
                    Console.WriteLine($"[T3 RESULT] Differenza rispetto al target: {diff3:F2} s");

                    player.Stop();
                    player.Close();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(TimeSpan.FromSeconds(15));

            if (threadEx != null)
            {
                throw new Exception("Errore nel thread di test diagnostico video", threadEx);
            }
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static object? ExitFrame(object frame)
        {
            ((DispatcherFrame)frame).Continue = false;
            return null;
        }
    }
}
