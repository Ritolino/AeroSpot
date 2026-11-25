using System;
using System.IO;
using Microsoft.Win32;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Native
{
    public static class ShellContextMenuIntegration
    {
        private const string DefaultBaseClassesKey = @"Software\Classes\SystemFileAssociations";

        private static string NormalizeBaseKey(string? baseClassesKeyOverride)
        {
            return string.IsNullOrWhiteSpace(baseClassesKeyOverride)
                ? DefaultBaseClassesKey
                : baseClassesKeyOverride.Trim().TrimEnd('\\');
        }

        private static string[] GetAllKeyPaths(string? baseClassesKeyOverride)
        {
            string baseKey = NormalizeBaseKey(baseClassesKeyOverride);
            return new[]
            {
                $@"{baseKey}\image\shell\AeroSpot",
                $@"{baseKey}\video\shell\AeroSpot",
                $@"{baseKey}\audio\shell\AeroSpot",
                $@"{baseKey}\.pdf\shell\AeroSpot",
                $@"{baseKey}\.docx\shell\AeroSpot",
                $@"{baseKey}\.txt\shell\AeroSpot",
                $@"{baseKey}\.md\shell\AeroSpot",
                $@"{baseKey}\.rtf\shell\AeroSpot",
                $@"{baseKey}\.odt\shell\AeroSpot"
            };
        }

        private static string GetLocalized(string key, string lang)
        {
            var dict = string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase)
                ? LocalizationService.Italian
                : LocalizationService.English;
            return dict.TryGetValue(key, out string? value) ? value : LocalizationService.Get(key);
        }

        public static bool IsRegistered(string? baseClassesKeyOverride = null)
        {
            try
            {
                string baseKey = NormalizeBaseKey(baseClassesKeyOverride);
                using var key = Registry.CurrentUser.OpenSubKey($@"{baseKey}\image\shell\AeroSpot");
                using var pdfKey = Registry.CurrentUser.OpenSubKey($@"{baseKey}\.pdf\shell\AeroSpot");
                return key != null && pdfKey != null;
            }
            catch
            {
                return false;
            }
        }

        public static void Register(string appBaseDir, string language, string? baseClassesKeyOverride = null)
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
                string baseKey = NormalizeBaseKey(baseClassesKeyOverride);
                string rootLabel = GetLocalized("ShellMenu_Root", lang);

                // Pulisce prima eventuali registrazioni precedenti
                Unregister(baseClassesKeyOverride);

                // 1. Menu a cascata per IMMAGINI (JPG, PNG, WEBP, BMP, GIF, ecc.)
                RegisterCascadeMenu(
                    $@"{baseKey}\image\shell\AeroSpot",
                    rootLabel,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_ConvertJpg", GetLocalized("ShellMenu_ConvertJpg", lang), $"\"{toolsExe}\" --convert-jpg \"%1\"", "NOT System.FileExtension:=.jpg AND NOT System.FileExtension:=.jpeg"),
                        ("02_ConvertPng", GetLocalized("ShellMenu_ConvertPng", lang), $"\"{toolsExe}\" --convert-png \"%1\"", "NOT System.FileExtension:=.png"),
                        ("03_ConvertPdf", GetLocalized("ShellMenu_ConvertPdf", lang), $"\"{toolsExe}\" --convert-pdf \"%1\"", null),
                        ("04_ImagesToPdf", isIt ? "Unisci foto in unico PDF" : "Combine photos into single PDF", $"\"{toolsExe}\" --images-to-pdf \"%1\"", null),
                        ("05_RemoveBg", GetLocalized("ShellMenu_RemoveBg", lang), $"\"{toolsExe}\" --remove-bg \"%1\"", null)
                    }
                );

                // 2. Menu a cascata per DOCUMENTI PDF (.pdf) - Supporto unione aggregata multi-file
                RegisterCascadeMenu(
                    $@"{baseKey}\.pdf\shell\AeroSpot",
                    rootLabel,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_MergePdf", GetLocalized("ShellMenu_MergePdf", lang), $"\"{toolsExe}\" --merge-pdf \"%1\"", null)
                    }
                );

                // 3. Menu a cascata per VIDEO (MP4, MKV, AVI, MOV, WMV, WEBM)
                RegisterCascadeMenu(
                    $@"{baseKey}\video\shell\AeroSpot",
                    rootLabel,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_ConvertMp4", GetLocalized("ShellMenu_ConvertMp4", lang), $"\"{toolsExe}\" --convert-mp4 \"%1\"", "NOT System.FileExtension:=.mp4"),
                        ("02_ExtractMp3", GetLocalized("ShellMenu_ExtractMp3", lang), $"\"{toolsExe}\" --extract-mp3 \"%1\"", null),
                        ("03_ExtractWav", GetLocalized("ShellMenu_ExtractWav", lang), $"\"{toolsExe}\" --extract-wav \"%1\"", null)
                    }
                );

                // 4. Menu a cascata per AUDIO (MP3, WAV, FLAC, AAC, OGG)
                RegisterCascadeMenu(
                    $@"{baseKey}\audio\shell\AeroSpot",
                    rootLabel,
                    launcherExe,
                    new (string id, string text, string cmd, string? appliesTo)[]
                    {
                        ("01_ConvertMp3", GetLocalized("Spotlight_Menu_ConvertMp3", lang), $"\"{toolsExe}\" --extract-mp3 \"%1\"", "NOT System.FileExtension:=.mp3"),
                        ("02_ConvertWav", GetLocalized("Spotlight_Menu_ConvertWav", lang), $"\"{toolsExe}\" --extract-wav \"%1\"", "NOT System.FileExtension:=.wav")
                    }
                );

                // 5. Menu per DOCUMENTI di testo (.docx, .txt, .md, .rtf, .odt)
                foreach (string ext in new[] { ".docx", ".txt", ".md", ".rtf", ".odt" })
                {
                    RegisterCascadeMenu(
                        $@"{baseKey}\{ext}\shell\AeroSpot",
                        rootLabel,
                        launcherExe,
                        new (string id, string text, string cmd, string? appliesTo)[]
                        {
                            ("01_ConvertPdf", GetLocalized("ShellMenu_ConvertPdf", lang), $"\"{toolsExe}\" --convert-pdf \"%1\"", null)
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
            string rootLabel,
            string iconExe,
            (string id, string text, string cmd, string? appliesTo)[] subCommands)
        {
            using var parentKey = Registry.CurrentUser.CreateSubKey(parentKeyPath);
            if (parentKey == null) return;

            parentKey.SetValue("MUIVerb", rootLabel);
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

        public static void Unregister(string? baseClassesKeyOverride = null)
        {
            try
            {
                foreach (string path in GetAllKeyPaths(baseClassesKeyOverride))
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
