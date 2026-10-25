using System;
using System.IO;
using Microsoft.Win32;

namespace AeroSpot.Core.Config
{
    public static class StartupManager
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string AppName = "AeroSpot";

        public static string GetCurrentHostExecutablePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Check if root launcher AeroSpot.exe exists in parent folder (portable clean layout)
            string rootLauncher = Path.GetFullPath(Path.Combine(baseDir, "..", "AeroSpot.exe"));
            if (File.Exists(rootLauncher))
            {
                return rootLauncher;
            }

            string hostExe = Path.Combine(baseDir, "AeroSpot.Host.exe");
            if (File.Exists(hostExe))
            {
                return hostExe;
            }

            // Fallback to current process if running as Host
            var currentProc = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(currentProc) && currentProc.EndsWith("AeroSpot.Host.exe", StringComparison.OrdinalIgnoreCase))
            {
                return currentProc;
            }

            return hostExe;
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                if (runKey == null) return false;

                object? value = runKey.GetValue(AppName);
                if (value == null) return false;

                // Check if user disabled it via Task Manager or Windows Settings
                if (!IsStartupApprovedByUser())
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsStartupApprovedByUser()
        {
            try
            {
                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, false);
                if (approvedKey == null) return true;

                object? rawValue = approvedKey.GetValue(AppName);
                if (rawValue is byte[] bytes && bytes.Length > 0)
                {
                    // In Windows StartupApproved\Run:
                    // First byte 02 = Enabled
                    // First byte 03, 01, etc. (odd) = Disabled by user in Task Manager / Settings
                    if ((bytes[0] & 1) != 0)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        public static void SetStartupEnabled(bool enabled)
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, true);

                if (enabled)
                {
                    string hostPath = GetCurrentHostExecutablePath();
                    string runValue = $"\"{hostPath}\"";

                    runKey?.SetValue(AppName, runValue, RegistryValueKind.String);

                    // If user explicitly enables startup from within the app, remove any previous
                    // disabled flag recorded by Windows Task Manager / Settings
                    approvedKey?.DeleteValue(AppName, false);
                }
                else
                {
                    runKey?.DeleteValue(AppName, false);
                    approvedKey?.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        public static void AutoRepairPathIfEnabled()
        {
            try
            {
                // If user disabled the app outside via Task Manager, respect that choice and do not touch anything
                if (!IsStartupApprovedByUser())
                {
                    return;
                }

                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (runKey == null) return;

                object? value = runKey.GetValue(AppName);
                if (value is string registeredPath)
                {
                    string currentHostPath = GetCurrentHostExecutablePath();
                    string expectedRunValue = $"\"{currentHostPath}\"";

                    if (!string.Equals(registeredPath.Trim(), expectedRunValue.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        runKey.SetValue(AppName, expectedRunValue, RegistryValueKind.String);
                    }
                }
            }
            catch { }
        }

        public static void Cleanup()
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
    }
}
