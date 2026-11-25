using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("AeroSpot Uninstaller")]
[assembly: AssemblyProduct("AeroSpot")]
[assembly: AssemblyDescription("Disinstallatore Completo per AeroSpot")]
[assembly: AssemblyCompany("Ritolino")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Ritolino")]
[assembly: AssemblyVersion("1.6.2.0")]
[assembly: AssemblyFileVersion("1.6.2.0")]

internal static class Program
{
    private static bool IsItalian()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        bool it = IsItalian();
        bool silent = false;
        if (args != null)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, "/silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/force", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                }
            }
        }

        if (!silent)
        {
            string confirmText = it
                ? "Sei sicuro di voler disinstallare completamente AeroSpot da questo computer?\n\n" +
                  "Verranno eliminati definitivamente:\n" +
                  " • Tutti i processi attivi di AeroSpot (Host, Worker, Tools)\n" +
                  " • Tutte le impostazioni personali e l'indice di ricerca\n" +
                  " • Tutti i modelli AI scaricati e file temporanei (%LOCALAPPDATA%\\AeroSpot)\n" +
                  " • L'avvio automatico con Windows e le azioni nel menu contestuale\n" +
                  " • Tutti i file e la cartella stessa del programma.\n\n" +
                  "Procedere con la rimozione totale?"
                : "Are you sure you want to completely uninstall AeroSpot from this computer?\n\n" +
                  "The following will be permanently deleted:\n" +
                  " • All running AeroSpot processes (Host, Worker, Tools)\n" +
                  " • All personal settings and search index\n" +
                  " • All downloaded AI models and temporary files (%LOCALAPPDATA%\\AeroSpot)\n" +
                  " • Windows startup entry and Explorer context menu actions\n" +
                  " • All program files and the application folder itself.\n\n" +
                  "Proceed with complete removal?";
            string confirmTitle = it
                ? "AeroSpot — Disinstallazione Completa"
                : "AeroSpot — Complete Uninstall";

            DialogResult dr = MessageBox.Show(
                confirmText,
                confirmTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (dr != DialogResult.Yes)
            {
                return 0;
            }
        }

        try
        {
            // 1. Termina tutti i processi attivi di AeroSpot
            string[] procNames = new string[] { "AeroSpot", "AeroSpot.Host", "AeroSpot.Worker", "AeroSpot.Tools" };
            int currentPid;
            using (Process currentProc = Process.GetCurrentProcess())
            {
                currentPid = currentProc.Id;
            }

            foreach (string name in procNames)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(name);
                    foreach (Process p in procs)
                    {
                        using (p)
                        {
                            if (p.Id != currentPid)
                            {
                                try
                                {
                                    p.Kill();
                                    p.WaitForExit(2000);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. Pulizia Registro di Windows e Collegamento Avvio Automatico
            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null) runKey.DeleteValue("AeroSpot", false);
                }
            }
            catch { }

            try
            {
                using (var approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                {
                    if (approvedKey != null) approvedKey.DeleteValue("AeroSpot", false);
                }
            }
            catch { }

            try
            {
                string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrEmpty(startupDir))
                {
                    string startupLnk = Path.Combine(startupDir, "AeroSpot.lnk");
                    if (File.Exists(startupLnk))
                    {
                        File.Delete(startupLnk);
                    }
                }
            }
            catch { }

            string[] assocTypes = new string[] { "image", "video", "audio", ".pdf", ".docx", ".txt", ".md", ".rtf", ".odt" };
            foreach (string assoc in assocTypes)
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\" + assoc + @"\shell\AeroSpot", false);
                }
                catch { }
            }

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\AeroSpot", false);
            }
            catch { }

            // 3. Eliminazione cartella dati, modelli AI e cache (%LOCALAPPDATA%\AeroSpot)
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string aeroSpotLocal = Path.Combine(localApp, "AeroSpot");
                if (Directory.Exists(aeroSpotLocal))
                {
                    Directory.Delete(aeroSpotLocal, true);
                }
            }
            catch
            {
                try
                {
                    string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string aeroSpotLocal = Path.Combine(localApp, "AeroSpot");
                    using (Process pDel = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c rd /s /q \"" + aeroSpotLocal + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }))
                    {
                        if (pDel != null)
                        {
                            pDel.WaitForExit(3000);
                        }
                    }
                }
                catch { }
            }

            // Roaming se presente
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string aeroSpotRoaming = Path.Combine(appData, "AeroSpot");
                if (Directory.Exists(aeroSpotRoaming))
                {
                    Directory.Delete(aeroSpotRoaming, true);
                }
            }
            catch { }

            // 4. Eliminazione della cartella del programma
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            // Protezione di sicurezza per l'ambiente di sviluppo
            bool isDevFolder = File.Exists(Path.Combine(baseDir, "AeroSpot.sln")) ||
                               File.Exists(Path.Combine(baseDir, "..", "AeroSpot.sln")) ||
                               Directory.Exists(Path.Combine(baseDir, "src", "AeroSpot.Core")) ||
                               Directory.Exists(Path.Combine(baseDir, ".git"));

            if (isDevFolder)
            {
                if (!silent)
                {
                    string devMsg = it
                        ? "Protezione Sviluppo Attiva:\n" +
                          "Tutte le impostazioni in %LOCALAPPDATA%\\AeroSpot, i modelli AI, la cache e le chiavi di registro sono state rimosse con successo.\n\n" +
                          "La cartella corrente contiene i sorgenti del progetto (AeroSpot.sln) e non è stata eliminata per tutelare i file di sviluppo."
                        : "Development Protection Active:\n" +
                          "All settings in %LOCALAPPDATA%\\AeroSpot, AI models, cache, and registry keys have been successfully removed.\n\n" +
                          "The current folder contains the project source files (AeroSpot.sln) and was not deleted to protect development files.";
                    string devTitle = it
                        ? "AeroSpot — Pulizia Dati Eseguita"
                        : "AeroSpot — Data Cleanup Complete";
                    MessageBox.Show(
                        devMsg,
                        devTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return 0;
            }

            // Creazione dello script batch temporaneo per la cancellazione asincrona della cartella
            string tempBat = Path.Combine(Path.GetTempPath(), "aerospot_uninstall_" + Guid.NewGuid().ToString("N") + ".bat");
            string doneText = it
                ? "AeroSpot e tutti i file correlati sono stati rimossi con successo da questo computer."
                : "AeroSpot and all related files have been successfully removed from this computer.";
            string doneTitle = it
                ? "AeroSpot — Disinstallazione Completata"
                : "AeroSpot — Uninstall Complete";
            string completionMsg = "powershell -NoProfile -WindowStyle Hidden -Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('" + doneText + "', '" + doneTitle + "', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)\"";

            string batContent =
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                "set /a retries=0\r\n" +
                ":wait_exit\r\n" +
                "set /a retries+=1\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "taskkill /f /pid " + currentPid + " >nul 2>&1\r\n" +
                "rd /s /q \"" + baseDir + "\" >nul 2>&1\r\n" +
                "if exist \"" + baseDir + "\" if %retries% LSS 30 goto wait_exit\r\n" +
                (silent ? "" : completionMsg + "\r\n") +
                "(goto) 2>nul & del \"%~f0\"\r\n";

            File.WriteAllText(tempBat, batContent, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + tempBat + "\"",
                WorkingDirectory = Path.GetTempPath(),
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (Process p = Process.Start(psi))
            {
            }

            // Esci immediatamente per liberare qualsiasi lock su Uninstall.exe e sulla cartella baseDir
            Environment.Exit(0);
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                string errPrefix = it
                    ? "Si è verificato un errore durante la disinstallazione:\n"
                    : "An error occurred during uninstallation:\n";
                string errTitle = it
                    ? "AeroSpot — Errore Disinstallazione"
                    : "AeroSpot — Uninstall Error";
                MessageBox.Show(
                    errPrefix + ex.Message,
                    errTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }
    }
}
