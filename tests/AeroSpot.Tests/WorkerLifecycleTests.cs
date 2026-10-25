using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AeroSpot.Core.IPC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class WorkerLifecycleTests
    {
        private static string GetWorkerExePath()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AeroSpot.sln")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }

            if (dir == null) throw new DirectoryNotFoundException("Could not locate solution root with AeroSpot.sln");

            string workerBin = Path.Combine(dir, "src", "AeroSpot.Worker", "bin");
            if (Directory.Exists(workerBin))
            {
                var matches = Directory.GetFiles(workerBin, "AeroSpot.Worker.exe", SearchOption.AllDirectories);
                var releaseMatch = matches.FirstOrDefault(m => m.Contains("Release"));
                if (releaseMatch != null) return releaseMatch;
                if (matches.Length > 0) return matches[0];
            }

            throw new FileNotFoundException($"AeroSpot.Worker.exe not found in Release or Debug under {dir}");
        }

        [TestMethod]
        public async Task Worker_AutoShutsDown_AfterIdleTimeout()
        {
            string workerExe = GetWorkerExePath();

            // Avvia Worker con timeout di 2 secondi
            var psi = new ProcessStartInfo
            {
                FileName = workerExe,
                Arguments = "--timeout 2 --no-keep-warm",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            Assert.IsNotNull(proc, "Failed to start worker process.");

            try
            {
                // In modalità autonoma (senza pipe dell'Host di produzione attiva in questo test),
                // il worker attende fino a 3s la pipe e poi attiva il countdown di 2 secondi
                bool exited = proc.WaitForExit(9000);
                Assert.IsTrue(exited, "Worker did not auto-shutdown within the idle timeout period.");
                Assert.AreEqual(0, proc.ExitCode, "Worker exited with non-zero exit code.");
            }
            finally
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
        }

        [TestMethod]
        public async Task Benchmark_ColdStart_Vs_WarmStart()
        {
            string workerExe = GetWorkerExePath();
            using var server = new NamedPipeHostServer();
            var readyTcs = new TaskCompletionSource<bool>();
            server.MessageReceived += msg =>
            {
                if (msg.Type == IpcMessageType.WorkerReady) readyTcs.TrySetResult(true);
            };
            server.Start();

            // 1. Cold Start Benchmark
            var swCold = Stopwatch.StartNew();
            var psi = new ProcessStartInfo
            {
                FileName = workerExe,
                Arguments = "--timeout 10",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            Assert.IsNotNull(proc);

            try
            {
                var readyCompleted = await Task.WhenAny(readyTcs.Task, Task.Delay(5000));
                swCold.Stop();
                long coldStartMs = swCold.ElapsedMilliseconds;
                Assert.AreEqual(readyTcs.Task, readyCompleted, "Worker did not connect and signal ready in time.");

                Console.WriteLine($"[Benchmark] Cold Start: {coldStartMs} ms");

                // 2. Warm Start Benchmark
                var swWarm = Stopwatch.StartNew();
                bool sent = await server.SendMessageAsync(new IpcMessage(IpcMessageType.ShowLauncher));
                swWarm.Stop();
                long warmStartMs = swWarm.ElapsedMilliseconds;

                Console.WriteLine($"[Benchmark] Warm Start: {warmStartMs} ms");
                Assert.IsTrue(sent);
                Assert.IsLessThan(50, warmStartMs, $"Warm start took {warmStartMs} ms, expected < 50 ms");

                // Clean shutdown
                await server.SendMessageAsync(new IpcMessage(IpcMessageType.CloseWorker));
                proc.WaitForExit(3000);
            }
            finally
            {
                if (!proc.HasExited) proc.Kill();
            }
        }
    }
}
