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
        private const string AppName = "AeroSpot";

        private string _testRootKey = null!;
        private string _runRegistryKey = null!;
        private string _startupApprovedKey = null!;

        [TestInitialize]
        public void SetupIsolatedRegistry()
        {
            _testRootKey = $@"Software\AeroSpot_Tests_{Guid.NewGuid():N}";
            _runRegistryKey = $@"{_testRootKey}\Run";
            _startupApprovedKey = $@"{_testRootKey}\StartupApproved\Run";
        }

        [TestCleanup]
        public void CleanupIsolatedRegistry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_testRootKey, throwOnMissingSubKey: false);
            }
            catch { }
        }

        [TestMethod]
        public void StartupManager_SetStartupEnabled_TrueAndFalse_WorksCorrectly()
        {
            // 1. Enable startup
            StartupManager.SetStartupEnabled(true, _runRegistryKey, _startupApprovedKey);
            Assert.IsTrue(StartupManager.IsStartupEnabled(_runRegistryKey, _startupApprovedKey), "Startup should be enabled after SetStartupEnabled(true).");

            using (var runKey = Registry.CurrentUser.OpenSubKey(_runRegistryKey, false))
            {
                Assert.IsNotNull(runKey);
                object? val = runKey.GetValue(AppName);
                Assert.IsNotNull(val, "Registry value should exist in Run key.");
                string? strVal = val.ToString();
                StringAssert.Contains(strVal, "AeroSpot.Host.exe", $"Expected value to contain AeroSpot.Host.exe, got: {strVal}");
            }

            // 2. Disable startup
            StartupManager.SetStartupEnabled(false, _runRegistryKey, _startupApprovedKey);
            Assert.IsFalse(StartupManager.IsStartupEnabled(_runRegistryKey, _startupApprovedKey), "Startup should be disabled after SetStartupEnabled(false).");

            using (var runKey = Registry.CurrentUser.OpenSubKey(_runRegistryKey, false))
            {
                Assert.IsNotNull(runKey);
                object? val = runKey.GetValue(AppName);
                Assert.IsNull(val, "Registry value should be deleted after SetStartupEnabled(false).");
            }
        }

        [TestMethod]
        public void StartupManager_RespectsUserDisablingInTaskManager()
        {
            // Setup: Enable startup in isolated Run key
            StartupManager.SetStartupEnabled(true, _runRegistryKey, _startupApprovedKey);
            Assert.IsTrue(StartupManager.IsStartupEnabled(_runRegistryKey, _startupApprovedKey));

            // Simulate Windows Task Manager disabling the app:
            // Windows writes a binary value with odd byte[0] (e.g. 0x03) in StartupApproved\Run
            using (var approvedKey = Registry.CurrentUser.CreateSubKey(_startupApprovedKey, true))
            {
                byte[] disabledFlag = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                approvedKey.SetValue(AppName, disabledFlag, RegistryValueKind.Binary);
            }

            // Verify AeroSpot detects user disabled it outside the app
            Assert.IsFalse(StartupManager.IsStartupApprovedByUser(_startupApprovedKey), "Should detect user disabled entry in Task Manager.");
            Assert.IsFalse(StartupManager.IsStartupEnabled(_runRegistryKey, _startupApprovedKey), "IsStartupEnabled() must return false when Task Manager disabled it.");

            // Verify AutoRepair respects this and does NOT re-enable or touch it
            StartupManager.AutoRepairPathIfEnabled(_runRegistryKey, _startupApprovedKey);
            Assert.IsFalse(StartupManager.IsStartupEnabled(_runRegistryKey, _startupApprovedKey));

            // If user re-enables explicitly via AeroSpot settings UI:
            StartupManager.SetStartupEnabled(true, _runRegistryKey, _startupApprovedKey);
            Assert.IsTrue(StartupManager.IsStartupApprovedByUser(_startupApprovedKey), "Should clear task manager disabled flag on explicit enable.");
            Assert.IsTrue(StartupManager.IsStartupEnabled(_runRegistryKey, _startupApprovedKey));
        }

        [TestMethod]
        public void StartupManager_Cleanup_RemovesAllKeys()
        {
            StartupManager.SetStartupEnabled(true, _runRegistryKey, _startupApprovedKey);
            using (var approvedKey = Registry.CurrentUser.CreateSubKey(_startupApprovedKey, true))
            {
                approvedKey.SetValue(AppName, new byte[] { 0x02 }, RegistryValueKind.Binary);
            }

            // Execute cleanup
            StartupManager.Cleanup(_runRegistryKey, _startupApprovedKey);

            using (var runKey = Registry.CurrentUser.OpenSubKey(_runRegistryKey, false))
            {
                Assert.IsNull(runKey?.GetValue(AppName));
            }

            using (var approvedKey = Registry.CurrentUser.OpenSubKey(_startupApprovedKey, false))
            {
                Assert.IsNull(approvedKey?.GetValue(AppName));
            }
        }

        [TestMethod]
        public void SingleInstance_Mutex_BlocksSecondInstance()
        {
            string mutexName = "Local\\AeroSpot_Test_Mutex_" + Guid.NewGuid().ToString("N");

            using var primaryMutex = new Mutex(true, mutexName, out bool createdNewPrimary);
            Assert.IsTrue(createdNewPrimary, "First mutex creation should succeed.");

            // Simulate second instance (e.g. from another directory) trying to start
            using var secondaryMutex = new Mutex(true, mutexName, out bool createdNewSecondary);
            Assert.IsFalse(createdNewSecondary, "Second instance must detect existing mutex and createdNew should be false.");
        }
    }
}
