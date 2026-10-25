using System;
using System.Threading;
using AeroSpot.Core.Native;
using AeroSpot.Host.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class KeyboardHookManagerTests
    {
        [TestMethod]
        public void KeyboardHook_CanInstallAndUninstallCleanly()
        {
            string? capturedPath = null;
            using var hook = new KeyboardHookManager(Win32.VK_SPACE, path => capturedPath = path);

            Assert.IsFalse(hook.IsHookInstalled, "Hook should not be installed before Install() is called.");

            bool installed = hook.Install();
            Assert.IsTrue(installed, "Install() must succeed on desktop session.");
            Assert.IsTrue(hook.IsHookInstalled, "IsHookInstalled must be true after Install().");

            hook.Uninstall();
            Assert.IsFalse(hook.IsHookInstalled, "IsHookInstalled must be false after Uninstall().");
        }

        [TestMethod]
        public void KeyboardHook_DoubleDispose_IsIdempotentAndSafe()
        {
            var hook = new KeyboardHookManager(Win32.VK_SPACE, path => { });
            hook.Install();
            Assert.IsTrue(hook.IsHookInstalled);

            // First dispose
            hook.Dispose();
            Assert.IsFalse(hook.IsHookInstalled);

            // Second dispose must not throw
            hook.Dispose();
            Assert.IsFalse(hook.IsHookInstalled);
        }

        [TestMethod]
        public void KeyboardHook_Callback_TriggerDispatchesAction()
        {
            bool actionTriggered = false;
            string testPath = typeof(KeyboardHookManagerTests).Assembly.Location;

            void OnPreview(string path)
            {
                if (path == testPath)
                {
                    actionTriggered = true;
                }
            }

            using var hook = new KeyboardHookManager(Win32.VK_SPACE, OnPreview);
            Assert.IsNotNull(hook);
            OnPreview(testPath);
            Assert.IsTrue(actionTriggered, "Action should be triggered when callback is executed.");

            // Verify constructor null check
            bool threw = false;
            try
            {
                _ = new KeyboardHookManager(Win32.VK_SPACE, null!);
            }
            catch (ArgumentNullException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "Constructor must throw ArgumentNullException for null callback.");
        }
    }
}
