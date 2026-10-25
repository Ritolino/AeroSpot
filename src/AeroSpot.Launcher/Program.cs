using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("AeroSpot")]
[assembly: AssemblyProduct("AeroSpot")]
[assembly: AssemblyDescription("AeroSpot Launcher")]
[assembly: AssemblyCompany("Gabriele Clemente")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Gabriele Clemente")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string hostExe = Path.Combine(baseDir, "app", "AeroSpot.Host.exe");
            if (!File.Exists(hostExe))
            {
                hostExe = Path.Combine(baseDir, "bin", "AeroSpot.Host.exe");
            }
            if (!File.Exists(hostExe))
            {
                hostExe = Path.Combine(baseDir, "AeroSpot.Host.exe");
            }

            if (!File.Exists(hostExe))
            {
                MessageBox.Show(
                    "Impossibile trovare il motore di AeroSpot in 'app\\AeroSpot.Host.exe'.\nAssicurati che la cartella 'app' sia presente e intatta.",
                    "AeroSpot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            var psi = new ProcessStartInfo
            {
                FileName = hostExe,
                WorkingDirectory = Path.GetDirectoryName(hostExe),
                UseShellExecute = true
            };

            if (args != null && args.Length > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(" ");
                    string arg = args[i];
                    if (arg.Contains(" ") || arg.Contains("\""))
                    {
                        sb.Append("\"").Append(arg.Replace("\"", "\\\"")).Append("\"");
                    }
                    else
                    {
                        sb.Append(arg);
                    }
                }
                psi.Arguments = sb.ToString();
            }

            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Errore durante l'avvio di AeroSpot:\n" + ex.Message,
                "AeroSpot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
    }
}
