using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("AeroSpot Uninstaller")]
[assembly: AssemblyProduct("AeroSpot")]
[assembly: AssemblyDescription("Disinstallatore Completo per AeroSpot")]
[assembly: AssemblyCompany("Gabriele Clemente")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Gabriele Clemente")]
[assembly: AssemblyVersion("1.6.1.0")]
[assembly: AssemblyFileVersion("1.6.1.0")]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
            DialogResult dr = MessageBox.Show(
                "Sei sicuro di voler disinstallare completamente AeroSpot da questo computer?\n\n" +
                "Verranno eliminati definitivamente:\n" +
                " • Tutti i processi attivi di AeroSpot (Host, Worker, Tools)\n" +
                " • Tutte le impostazioni personali e l'indice di ricerca\n" +
                " • Tutti i modelli AI scaricati e file temporanei (%LOCALAPPDATA%\\AeroSpot)\n" +
                " • L'avvio automatico con Windows e le azioni nel menu contestuale\n" +
                " • Tutti i file e la cartella stessa del programma.\n\n" +
                "Procedere con la rimozione totale?",
                "AeroSpot — Disinstallazione Completa",
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
            string[] procNames = new string[] { "AeroSpot", "AeroSpot.Host", "AeroSpot.Worker", "AeroSpot.Tools", "AeroSpot.PreviewHost" };
            int currentPid = Process.GetCurrentProcess().Id;
            foreach (string name in procNames)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(name);
                    foreach (Process p in procs)
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
                catch { }
            }

            // 2. Pulizia Registro di Windows (Avvio automatico e Menu Contestuale)
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
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\image\shell\AeroSpot", false);
            }
            catch { }

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\AeroSpot", false);
            }
            catch { }

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
                    Process pDel = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c rd /s /q \"" + aeroSpotLocal + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    if (pDel != null)
                    {
                        pDel.WaitForExit(3000);
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
                    MessageBox.Show(
                        "Protezione Sviluppo Attiva:\n" +
                        "Tutte le impostazioni in %LOCALAPPDATA%\\AeroSpot, i modelli AI, la cache e le chiavi di registro sono state rimosse con successo.\n\n" +
                        "La cartella corrente contiene i sorgenti del progetto (AeroSpot.sln) e non è stata eliminata per tutelare i file di sviluppo.",
                        "AeroSpot — Pulizia Dati Eseguita",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return 0;
            }

            // Creazione dello script batch temporaneo per la cancellazione asincrona della cartella
            string tempBat = Path.Combine(Path.GetTempPath(), "aerospot_uninstall_" + Guid.NewGuid().ToString("N") + ".bat");
            string completionMsg = "powershell -NoProfile -WindowStyle Hidden -Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('AeroSpot e tutti i file correlati sono stati rimossi con successo da questo computer.', 'AeroSpot — Disinstallazione Completata', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)\"";

            string batContent =
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                ":wait_exit\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "taskkill /f /pid " + currentPid + " >nul 2>&1\r\n" +
                "rd /s /q \"" + baseDir + "\" >nul 2>&1\r\n" +
                "if exist \"" + baseDir + "\" goto wait_exit\r\n" +
                (silent ? "" : completionMsg + "\r\n") +
                "(goto) 2>nul & del \"%~f0\"\r\n";

            File.WriteAllText(tempBat, batContent, Encoding.Default);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + tempBat + "\"",
                WorkingDirectory = Path.GetTempPath(),
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);

            // Esci immediatamente per liberare qualsiasi lock su Uninstall.exe e sulla cartella baseDir
            Environment.Exit(0);
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(
                    "Si è verificato un errore durante la disinstallazione:\n" + ex.Message,
                    "AeroSpot — Errore Disinstallazione",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }
    }
}
