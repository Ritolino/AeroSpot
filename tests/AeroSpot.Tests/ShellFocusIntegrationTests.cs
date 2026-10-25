using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Native;
using AeroSpot.Core.Shell;
using AeroSpot.Host.Services;
using AeroSpot.Worker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class ShellFocusIntegrationTests
    {
        [TestMethod]
        public async Task WorkerManager_ShowPreviewAsync_LaunchesWorkerAndSendsIpc()
        {
            var config = AppConfig.Load();
            config.WorkerIdleTimeoutSeconds = 4; // Breve timeout per il test

            using var manager = new WorkerManager(config);

            string sampleFile = Path.Combine(AppContext.BaseDirectory, "AeroSpot.Core.dll");
            Assert.IsTrue(File.Exists(sampleFile), "Sample file for preview must exist.");

            // 1. Prima chiamata: avvio a freddo e invio comando
            var swCold = Stopwatch.StartNew();
            bool coldResult = await manager.ShowPreviewAsync(sampleFile);
            swCold.Stop();

            Assert.IsTrue(coldResult, "ShowPreviewAsync (Cold) must return true.");
            Assert.IsTrue(manager.IsWorkerRunning, "Worker process must be running.");
            Assert.IsTrue(manager.IsIpcConnected, "IPC must be connected.");

            // 2. Seconda chiamata: avvio a caldo
            var swWarm = Stopwatch.StartNew();
            bool warmResult = await manager.ShowPreviewAsync(sampleFile);
            swWarm.Stop();

            Assert.IsTrue(warmResult, "ShowPreviewAsync (Warm) must return true.");
            Assert.IsLessThan(500L, swWarm.ElapsedMilliseconds, $"Warm IPC dispatch should complete under 500ms, actual: {swWarm.ElapsedMilliseconds}ms");

            // Arresto pulito
            await manager.StopWorkerAsync(3000);
            Assert.IsFalse(manager.IsWorkerRunning, "Worker must stop cleanly.");
        }

        [TestMethod]
        public async Task PreviewLoader_FolderPreview_ReturnsFolderMetadata()
        {
            string projectDir = TestEnvironment.RepoRoot;
            Assert.IsTrue(Directory.Exists(projectDir));

            var model = await PreviewLoader.LoadAsync(projectDir);

            Assert.AreEqual(PreviewType.Generic, model.Type);
            Assert.IsTrue(model.IsFolder, "model.IsFolder deve essere true per le cartelle.");
            Assert.IsTrue(model.Subtitle == "Cartella di file" || model.Subtitle == "File folder");
            Assert.IsTrue(model.DetailsText.Contains("Percorso:") || model.DetailsText.Contains("Path:"));

            // Test del calcolo asincrono dimensioni cartella
            using var cts = new System.Threading.CancellationTokenSource();
            var sizeResult = await PreviewLoader.CalculateDirectorySizeAsync(projectDir, cts.Token);
            Assert.IsTrue(sizeResult.HasValue);
#pragma warning disable MSTEST0037
            Assert.IsTrue(sizeResult.Value.TotalBytes > 0, "TotalBytes deve essere maggiore di zero.");
            Assert.IsTrue(sizeResult.Value.FileCount > 0, "FileCount deve essere maggiore di zero.");
#pragma warning restore MSTEST0037
        }

        [TestMethod]
        public void PreviewLoader_VideoPreview_IdentifiesVideoFormats()
        {
            Assert.IsTrue(PreviewLoader.IsVideoExtension(".mp4"));
            Assert.IsTrue(PreviewLoader.IsVideoExtension(".mkv"));
            Assert.IsTrue(PreviewLoader.IsVideoExtension(".avi"));
            Assert.IsTrue(PreviewLoader.IsVideoExtension(".mov"));
            Assert.IsTrue(PreviewLoader.IsVideoExtension(".h264"));
            Assert.IsTrue(PreviewLoader.IsVideoExtension(".h265"));
            Assert.IsFalse(PreviewLoader.IsVideoExtension(".txt"));
        }

        [TestMethod]
        public async Task PreviewLoader_DeletedOrMissingPath_ReturnsErrorSafely()
        {
            string missingPath = Path.Combine(TestEnvironment.RepoRoot, "NonExistentFile_" + Guid.NewGuid().ToString("N") + ".txt");

            var model = await PreviewLoader.LoadAsync(missingPath);

            Assert.AreEqual(PreviewType.Error, model.Type);
            Assert.IsNotNull(model.ErrorMessage);
            Assert.IsTrue(model.ErrorMessage.Contains("non esiste") || model.ErrorMessage.Contains("rimosso") || model.ErrorMessage.Contains("not exist") || model.ErrorMessage.Contains("removed"));
        }

        [TestMethod]
        public void LiveExplorer_OpenWindowCheck_ReportsState()
        {
            // Ispeziona l'eventuale finestra Explorer aperta sul sistema di test
            IntPtr fg = Win32.GetForegroundWindow();
            string fgClass = ExplorerFocusResolver.GetWindowClassName(fg);
            bool isShell = ExplorerFocusResolver.IsExplorerOrDesktop(fg);
            bool isTyping = ExplorerFocusResolver.IsUserTypingOrRenaming(fg);

            TestContext?.WriteLine($"Foreground HWND: 0x{fg:X}, Class: '{fgClass}', IsShell: {isShell}, IsTyping: {isTyping}");

            if (isShell && !isTyping)
            {
                bool resolved = ExplorerFocusResolver.TryGetFocusedPath(fg, out string? path);
                TestContext?.WriteLine($"Focus resolved: {resolved}, Path: '{path ?? "(none)"}'");
            }
        }

        [TestMethod]
        public void LiveShell_EnumerateAllOpenExplorerWindows_ResolvesFocusWithoutCrashing()
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            Assert.IsNotNull(shellType, "Shell.Application COM Type must be resolvable.");

            object? shell = Activator.CreateInstance(shellType);
            Assert.IsNotNull(shell);

            object? windows = ExplorerFocusResolver.InvokeComMethod(shell, "Windows");
            Assert.IsNotNull(windows);

            object? countObj = ExplorerFocusResolver.GetComProperty(windows, "Count");
            int count = countObj is int c ? c : 0;
            TestContext?.WriteLine($"Numero finestre aperte in Shell.Windows: {count}");

            for (int i = 0; i < count; i++)
            {
                object? win = ExplorerFocusResolver.InvokeComMethod(windows, "Item", i);
                if (win == null) continue;

                object? hwndObj = ExplorerFocusResolver.GetComProperty(win, "HWND");
                if (hwndObj == null) continue;

                long hwndVal = Convert.ToInt64(hwndObj);
                IntPtr hwnd = new IntPtr(hwndVal);
                string loc = ExplorerFocusResolver.GetComProperty(win, "LocationName") as string ?? "(none)";

                string? focusedPath = ExplorerFocusResolver.GetFocusedPathForExplorer(hwnd);
                TestContext?.WriteLine($"[Finestra {i}] HWND: 0x{hwnd:X}, Loc: {loc}, FocusedPath: '{focusedPath ?? "(none)"}'");
            }
        }

        public TestContext? TestContext { get; set; }
    }
}
