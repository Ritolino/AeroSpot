using System;
using System.IO;
using Microsoft.Win32;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Native
{
    public static class ShellContextMenuIntegration
    {
        private const string ImageKeyPath = @"Software\Classes\SystemFileAssociations\image\shell\AeroSpot";
        private const string VideoKeyPath = @"Software\Classes\SystemFileAssociations\video\shell\AeroSpot";
        private const string AudioKeyPath = @"Software\Classes\SystemFileAssociations\audio\shell\AeroSpot";
        private const string PdfKeyPath = @"Software\Classes\SystemFileAssociations\.pdf\shell\AeroSpot";
        private const string DocxKeyPath = @"Software\Classes\SystemFileAssociations\.docx\shell\AeroSpot";
        private const string TxtKeyPath = @"Software\Classes\SystemFileAssociations\.txt\shell\AeroSpot";
        private const string MdKeyPath = @"Software\Classes\SystemFileAssociations\.md\shell\AeroSpot";

        private static readonly string[] AllKeyPaths = new[]
        {
            ImageKeyPath,
            VideoKeyPath,
            AudioKeyPath,
            PdfKeyPath,
            DocxKeyPath,
            TxtKeyPath,
            MdKeyPath
        };

        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ImageKeyPath);
                using var pdfKey = Registry.CurrentUser.OpenSubKey(PdfKeyPath);
                return key != null && pdfKey != null;
            }
            catch
            {
                return false;
            }
        }

        public static void Register(string appBaseDir, string language)
        {
            try
            {
                string trimmedBase = appBaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string launcherExe = Path.Combine(trimmedBase, "AeroSpot.exe");
                if (!File.Exists(launcherExe))
                {
                    string parentLauncher = Path.Combine(Path.GetDirectoryName(trimmedBase) ?? "", "AeroSpot.exe");
                    if (File.Exists(parentLauncher)) launcherExe = parentLauncher;
                }
                if (!File.Exists(launcherExe))
                {
                    string hostExe = Path.Combine(trimmedBase, "AeroSpot.Host.exe");
                    if (File.Exists(hostExe)) launcherExe = hostExe;
                }

                string toolsExe = Path.Combine(trimmedBase, "AeroSpot.Tools.exe");
                if (!File.Exists(toolsExe))
                {
                    string subTools = Path.Combine(trimmedBase, "app", "AeroSpot.Tools.exe");
                    if (File.Exists(subTools)) toolsExe = subTools;
                }

                string lang = LocalizationService.ResolveLanguageCode(language);
                bool isIt = lang == "it";

                // Pulisce prima eventuali registrazioni precedenti
                Unregister();

                // 1. Menu a cascata per IMMAGINI (JPG, PNG, WEBP, BMP, GIF, ecc.)
                RegisterCascadeMenu(
                    ImageKeyPath,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_ConvertJpg", isIt ? "Converti in JPG" : "Convert to JPG", $"\"{toolsExe}\" --convert-jpg \"%1\"", "NOT System.FileExtension:=.jpg AND NOT System.FileExtension:=.jpeg"),
                        ("02_ConvertPng", isIt ? "Converti in PNG" : "Convert to PNG", $"\"{toolsExe}\" --convert-png \"%1\"", "NOT System.FileExtension:=.png"),
                        ("03_ConvertPdf", isIt ? "Converti in PDF" : "Convert to PDF", $"\"{toolsExe}\" --convert-pdf \"%1\"", null),
                        ("04_ImagesToPdf", isIt ? "Unisci foto in unico PDF" : "Combine photos into single PDF", $"\"{toolsExe}\" --images-to-pdf \"%1\"", null),
                        ("05_RemoveBg", isIt ? "Rimuovi sfondo (AI)" : "Remove Background (AI)", $"\"{toolsExe}\" --remove-bg \"%1\"", null)
                    }
                );

                // 2. Menu a cascata per DOCUMENTI PDF (.pdf) - Supporto unione aggregata multi-file
                RegisterCascadeMenu(
                    PdfKeyPath,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_MergePdf", isIt ? "Unisci PDF selezionati" : "Merge selected PDFs", $"\"{toolsExe}\" --merge-pdf \"%1\"", null)
                    }
                );

                // 3. Menu a cascata per VIDEO (MP4, MKV, AVI, MOV, WMV, WEBM)
                RegisterCascadeMenu(
                    VideoKeyPath,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_ConvertMp4", isIt ? "Converti in MP4" : "Convert to MP4", $"\"{toolsExe}\" --convert-mp4 \"%1\"", "NOT System.FileExtension:=.mp4"),
                        ("02_ExtractMp3", isIt ? "Estrai audio MP3" : "Extract MP3 Audio", $"\"{toolsExe}\" --extract-mp3 \"%1\"", null),
                        ("03_ExtractWav", isIt ? "Estrai audio WAV" : "Extract WAV Audio", $"\"{toolsExe}\" --extract-wav \"%1\"", null)
                    }
                );

                // 4. Menu a cascata per AUDIO (MP3, WAV, FLAC, AAC, OGG)
                RegisterCascadeMenu(
                    AudioKeyPath,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_ConvertMp3", isIt ? "Converti in MP3" : "Convert to MP3", $"\"{toolsExe}\" --extract-mp3 \"%1\"", "NOT System.FileExtension:=.mp3"),
                        ("02_ConvertWav", isIt ? "Converti in WAV" : "Convert to WAV", $"\"{toolsExe}\" --extract-wav \"%1\"", "NOT System.FileExtension:=.wav")
                    }
                );

                // 5. Menu per DOCUMENTI di testo (.docx, .txt, .md)
                foreach (string docKey in new[] { DocxKeyPath, TxtKeyPath, MdKeyPath })
                {
                    RegisterCascadeMenu(
                        docKey,
                        launcherExe,
                        new (string id, string text, string cmd, string? appliesTo)[]
                        {
                            ("01_ConvertPdf", isIt ? "Converti in PDF" : "Convert to PDF", $"\"{toolsExe}\" --convert-pdf \"%1\"", null)
                        }
                    );
                }

                AppLogger.Info("ShellContextMenuIntegration: Registrazione menu a cascata AeroSpot completata in HKCU con MultiSelectModel=Player.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("ShellContextMenuIntegration: Errore durante la registrazione nel Registry", ex);
            }
        }

        private static void RegisterCascadeMenu(
            string parentKeyPath,
            string iconExe,
            (string id, string text, string cmd, string? appliesTo)[] subCommands)
        {
            using var parentKey = Registry.CurrentUser.CreateSubKey(parentKeyPath);
            if (parentKey == null) return;

            parentKey.SetValue("MUIVerb", "AeroSpot");
            parentKey.SetValue("SubCommands", "");
            parentKey.SetValue("MultiSelectModel", "Player");
            if (File.Exists(iconExe)) parentKey.SetValue("Icon", $"\"{iconExe}\"");

            using var shellKey = parentKey.CreateSubKey("shell");
            if (shellKey == null) return;

            foreach (var (id, text, cmd, appliesTo) in subCommands)
            {
                using var itemKey = shellKey.CreateSubKey(id);
                if (itemKey == null) continue;

                itemKey.SetValue("", text);
                itemKey.SetValue("MultiSelectModel", "Player");
                if (File.Exists(iconExe)) itemKey.SetValue("Icon", $"\"{iconExe}\"");

                if (!string.IsNullOrWhiteSpace(appliesTo))
                {
                    itemKey.SetValue("AppliesTo", appliesTo);
                }

                using var cmdKey = itemKey.CreateSubKey("command");
                cmdKey?.SetValue("", cmd);
            }
        }

        public static void Unregister()
        {
            try
            {
                foreach (string path in AllKeyPaths)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                }
                AppLogger.Info("ShellContextMenuIntegration: Rimozione chiavi menu contestuale completata.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"ShellContextMenuIntegration: Errore durante la rimozione dal Registry: {ex.Message}");
            }
        }
    }
}
