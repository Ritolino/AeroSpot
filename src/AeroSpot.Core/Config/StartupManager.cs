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

            // Se siamo dentro app\, l'eseguibile Host da avviare è direttamente qui
            string hostExe = Path.Combine(baseDir, "AeroSpot.Host.exe");
            if (File.Exists(hostExe))
            {
                return hostExe;
            }

            // Se siamo nella cartella radice, il motore Host è in app\AeroSpot.Host.exe
            string subHost = Path.Combine(baseDir, "app", "AeroSpot.Host.exe");
            if (File.Exists(subHost))
            {
                return subHost;
            }

            // Fallback al processo corrente se in esecuzione come Host
            var currentProc = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(currentProc) && currentProc.EndsWith("AeroSpot.Host.exe", StringComparison.OrdinalIgnoreCase))
            {
                return currentProc;
            }

            // Fallback al launcher radice AeroSpot.exe se presente
            string rootLauncher = Path.GetFullPath(Path.Combine(baseDir, "..", "AeroSpot.exe"));
            if (File.Exists(rootLauncher))
            {
                return rootLauncher;
            }

            return hostExe;
        }

        public static bool IsStartupEnabled(string? runKeyPathOverride = null, string? approvedKeyPathOverride = null)
        {
            try
            {
                string runKeyPath = runKeyPathOverride ?? RunRegistryKey;
                using var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, false);
                bool registryExists = runKey?.GetValue(AppName) != null;

                if (!registryExists) return false;

                // Check if user disabled it via Task Manager or Windows Settings
                if (!IsStartupApprovedByUser(approvedKeyPathOverride))
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

        public static bool IsStartupApprovedByUser(string? approvedKeyPathOverride = null)
        {
            try
            {
                string approvedKeyPath = approvedKeyPathOverride ?? StartupApprovedKey;
                using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, false);
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

        public static void SetStartupEnabled(bool enabled, string? runKeyPathOverride = null, string? approvedKeyPathOverride = null)
        {
            try
            {
                string runKeyPath = runKeyPathOverride ?? RunRegistryKey;
                string approvedKeyPath = approvedKeyPathOverride ?? StartupApprovedKey;

                using var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath, true);
                using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true);

                string hostPath = GetCurrentHostExecutablePath();

                if (enabled)
                {
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

                if (runKeyPathOverride == null)
                {
                    RemoveLegacyStartupShortcut();
                }
            }
            catch { }
        }

        public static void AutoRepairPathIfEnabled(string? runKeyPathOverride = null, string? approvedKeyPathOverride = null)
        {
            try
            {
                // If user disabled the app outside via Task Manager, respect that choice and do not touch anything
                if (!IsStartupApprovedByUser(approvedKeyPathOverride))
                {
                    return;
                }

                string runKeyPath = runKeyPathOverride ?? RunRegistryKey;
                string currentHostPath = GetCurrentHostExecutablePath();
                string expectedRunValue = $"\"{currentHostPath}\"";

                using var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
                if (runKey != null)
                {
                    object? value = runKey.GetValue(AppName);
                    if (value is string registeredPath)
                    {
                        if (!string.Equals(registeredPath.Trim(), expectedRunValue.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            runKey.SetValue(AppName, expectedRunValue, RegistryValueKind.String);
                        }
                    }
                }

                if (runKeyPathOverride == null)
                {
                    RemoveLegacyStartupShortcut();
                }
            }
            catch { }
        }

        public static void Cleanup(string? runKeyPathOverride = null, string? approvedKeyPathOverride = null)
        {
            try
            {
                string runKeyPath = runKeyPathOverride ?? RunRegistryKey;
                string approvedKeyPath = approvedKeyPathOverride ?? StartupApprovedKey;

                using var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
                runKey?.DeleteValue(AppName, false);

                using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true);
                approvedKey?.DeleteValue(AppName, false);

                if (runKeyPathOverride == null)
                {
                    RemoveLegacyStartupShortcut();
                }
            }
            catch { }
        }

        private static void RemoveLegacyStartupShortcut()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string lnkPath = Path.Combine(startupFolder, $"{AppName}.lnk");
                if (File.Exists(lnkPath))
                {
                    File.Delete(lnkPath);
                }
            }
            catch { }
        }

        // Temporary no-op until Wave 2 removes the caller in AeroSpot.Worker/App.xaml.cs.
        public static void EnsureBrowserEmulation()
        {
        }
    }
}
