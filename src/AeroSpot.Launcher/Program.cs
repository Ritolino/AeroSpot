using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("AeroSpot")]
[assembly: AssemblyProduct("AeroSpot")]
[assembly: AssemblyDescription("AeroSpot Launcher")]
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

    /// <summary>
    /// Quotes a single command-line argument according to Windows CommandLineToArgvW rules.
    /// </summary>
    private static string QuoteWindowsArgument(string arg)
    {
        if (arg == null)
        {
            return "\"\"";
        }
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuotes = false;
        for (int i = 0; i < arg.Length; i++)
        {
            char c = arg[i];
            if (c == ' ' || c == '\t' || c == '"')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return arg;
        }

        var sb = new StringBuilder();
        sb.Append('"');
        int backslashes = 0;

        for (int i = 0; i < arg.Length; i++)
        {
            char c = arg[i];
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
        }

        if (backslashes > 0)
        {
            sb.Append('\\', backslashes * 2);
        }

        sb.Append('"');
        return sb.ToString();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        bool it = IsItalian();
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
                string missingMsg = it
                    ? "Impossibile trovare il motore di AeroSpot in 'app\\AeroSpot.Host.exe'.\nAssicurati che la cartella 'app' sia presente e intatta."
                    : "Could not find the AeroSpot engine at 'app\\AeroSpot.Host.exe'.\nMake sure the 'app' folder is present and intact.";
                MessageBox.Show(
                    missingMsg,
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
                    sb.Append(QuoteWindowsArgument(args[i]));
                }
                psi.Arguments = sb.ToString();
            }

            using (Process p = Process.Start(psi))
            {
            }
            return 0;
        }
        catch (Exception ex)
        {
            string errorPrefix = it
                ? "Errore durante l'avvio di AeroSpot:\n"
                : "Error starting AeroSpot:\n";
            MessageBox.Show(
                errorPrefix + ex.Message,
                "AeroSpot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
    }
}
