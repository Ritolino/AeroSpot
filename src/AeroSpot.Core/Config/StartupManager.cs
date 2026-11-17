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

        public static bool IsStartupEnabled()
        {
            try
            {
                // Controlla sia il registro Run sia la cartella Startup
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string lnkPath = Path.Combine(startupFolder, $"{AppName}.lnk");
                bool shortcutExists = File.Exists(lnkPath);

                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                bool registryExists = runKey?.GetValue(AppName) != null;

                if (!shortcutExists && !registryExists) return false;

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

                string hostPath = GetCurrentHostExecutablePath();

                if (enabled)
                {
                    string runValue = $"\"{hostPath}\"";
                    runKey?.SetValue(AppName, runValue, RegistryValueKind.String);

                    // If user explicitly enables startup from within the app, remove any previous
                    // disabled flag recorded by Windows Task Manager / Settings
                    approvedKey?.DeleteValue(AppName, false);

                    UpdateStartupShortcut(true, hostPath);
                }
                else
                {
                    runKey?.DeleteValue(AppName, false);
                    approvedKey?.DeleteValue(AppName, false);
                    UpdateStartupShortcut(false, hostPath);
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

                string currentHostPath = GetCurrentHostExecutablePath();
                string expectedRunValue = $"\"{currentHostPath}\"";

                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
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

                UpdateStartupShortcut(true, currentHostPath);
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

                UpdateStartupShortcut(false, string.Empty);
            }
            catch { }
        }

        private static void UpdateStartupShortcut(bool enabled, string targetPath)
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string lnkPath = Path.Combine(startupFolder, $"{AppName}.lnk");

                if (enabled && !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                {
                    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic? shell = Activator.CreateInstance(shellType);
                        if (shell != null)
                        {
                            dynamic shortcut = shell.CreateShortcut(lnkPath);
                            shortcut.TargetPath = targetPath;
                            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                            shortcut.Description = "AeroSpot Background Host";
                            shortcut.Save();
                        }
                    }
                }
                else
                {
                    if (File.Exists(lnkPath))
                    {
                        File.Delete(lnkPath);
                    }
                }
            }
            catch { }
        }

        public static void EnsureBrowserEmulation()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
                key?.SetValue("AeroSpot.Worker.exe", 11001, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
