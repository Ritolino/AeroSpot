using System;
using System.Threading;
using AeroSpot.Core.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace AeroSpot.Tests
{
    [TestClass]
    public class StartupAndSingleInstanceTests
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string AppName = "AeroSpot";

        [TestCleanup]
        public void CleanupRegistry()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                runKey?.DeleteValue(AppName, false);

                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, true);
                approvedKey?.DeleteValue(AppName, false);
            }
            catch { }
        }

        [TestMethod]
        public void StartupManager_SetStartupEnabled_TrueAndFalse_WorksCorrectly()
        {
            // 1. Enable startup
            StartupManager.SetStartupEnabled(true);
            Assert.IsTrue(StartupManager.IsStartupEnabled(), "Startup should be enabled after SetStartupEnabled(true).");

            using (var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
            {
                Assert.IsNotNull(runKey);
                object? val = runKey.GetValue(AppName);
                Assert.IsNotNull(val, "Registry value should exist in Run key.");
                string? strVal = val.ToString();
                StringAssert.Contains(strVal, "AeroSpot.Host.exe", $"Expected value to contain AeroSpot.Host.exe, got: {strVal}");
            }

            // 2. Disable startup
            StartupManager.SetStartupEnabled(false);
            Assert.IsFalse(StartupManager.IsStartupEnabled(), "Startup should be disabled after SetStartupEnabled(false).");

            using (var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
            {
                Assert.IsNotNull(runKey);
                object? val = runKey.GetValue(AppName);
                Assert.IsNull(val, "Registry value should be deleted after SetStartupEnabled(false).");
            }
        }

        [TestMethod]
        public void StartupManager_RespectsUserDisablingInTaskManager()
        {
            // Setup: Enable startup in Run key
            StartupManager.SetStartupEnabled(true);
            Assert.IsTrue(StartupManager.IsStartupEnabled());

            // Simulate Windows Task Manager disabling the app:
            // Windows writes a binary value with odd byte[0] (e.g. 0x03) in StartupApproved\Run
            using (var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedKey, true))
            {
                byte[] disabledFlag = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                approvedKey.SetValue(AppName, disabledFlag, RegistryValueKind.Binary);
            }

            // Verify AeroSpot detects user disabled it outside the app
            Assert.IsFalse(StartupManager.IsStartupApprovedByUser(), "Should detect user disabled entry in Task Manager.");
            Assert.IsFalse(StartupManager.IsStartupEnabled(), "IsStartupEnabled() must return false when Task Manager disabled it.");

            // Verify AutoRepair respects this and does NOT re-enable or touch it
            StartupManager.AutoRepairPathIfEnabled();
            Assert.IsFalse(StartupManager.IsStartupEnabled());

            // If user re-enables explicitly via AeroSpot settings UI:
            StartupManager.SetStartupEnabled(true);
            Assert.IsTrue(StartupManager.IsStartupApprovedByUser(), "Should clear task manager disabled flag on explicit enable.");
            Assert.IsTrue(StartupManager.IsStartupEnabled());
        }

        [TestMethod]
        public void StartupManager_Cleanup_RemovesAllKeys()
        {
            StartupManager.SetStartupEnabled(true);
            using (var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedKey, true))
            {
                approvedKey.SetValue(AppName, new byte[] { 0x02 }, RegistryValueKind.Binary);
            }

            // Execute cleanup
            StartupManager.Cleanup();

            using (var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
            {
                Assert.IsNull(runKey?.GetValue(AppName));
            }

            using (var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, false))
            {
                Assert.IsNull(approvedKey?.GetValue(AppName));
            }
        }

        [TestMethod]
        public void SingleInstance_Mutex_BlocksSecondInstance()
        {
            string mutexName = "Local\\AeroSpot_Host_Mutex_" + Environment.UserName.Replace("\\", "_");

            using var primaryMutex = new Mutex(true, mutexName, out bool createdNewPrimary);
            Assert.IsTrue(createdNewPrimary, "First mutex creation should succeed.");

            // Simulate second instance (e.g. from another directory) trying to start
            using var secondaryMutex = new Mutex(true, mutexName, out bool createdNewSecondary);
            Assert.IsFalse(createdNewSecondary, "Second instance must detect existing mutex and createdNew should be false.");
        }
    }
}
