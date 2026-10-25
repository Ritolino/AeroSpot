using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AeroSpot.Core.Config;
using AeroSpot.Core.Native;

namespace AeroSpot.Worker
{
    public sealed class DriveItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _displayName = string.Empty;
        public string Letter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }
        }
        public bool IsIncluded { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class ExclusionItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsExcluded { get; set; } = true;
        public bool IsCustom { get; set; } = false;
    }

    public partial class SettingsWindow : Window
    {
        private AppConfig _config;
        private readonly ObservableCollection<DriveItem> _drives = new ObservableCollection<DriveItem>();
        private readonly ObservableCollection<ExclusionItem> _exclusions = new ObservableCollection<ExclusionItem>();

        private uint _spotlightKey;
        private uint _spotlightModifiers;
        private uint _quickLookKey;
        private uint _quickLookModifiers;

        public event Action<AppConfig>? ConfigSaved;

        public SettingsWindow()
        {
            InitializeComponent();
            _config = AppConfig.Load();

            ListDrives.ItemsSource = _drives;
            ListExclusions.ItemsSource = _exclusions;

            LoadSettingsIntoUi();

            // Support drag to move
            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) DragMove();
            };
        }

        public void ReloadSettings()
        {
            _config = AppConfig.Load();
            LoadSettingsIntoUi();
        }

        private void LoadSettingsIntoUi()
        {
            // 0. Language
            string lang = _config.Language?.ToLowerInvariant() ?? "auto";
            switch (lang)
            {
                case "en":
                    CmbLanguage.SelectedItem = CmbItemEn;
                    break;
                case "it":
                    CmbLanguage.SelectedItem = CmbItemIt;
                    break;
                default:
                    CmbLanguage.SelectedItem = CmbItemAuto;
                    break;
            }

            // 1. Hotkey Spotlight
            _spotlightKey = _config.HotkeyKey;
            _spotlightModifiers = _config.HotkeyModifiers;
            string defaultSpotlightText = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "Alt + Spazio" : "Alt + Space";
            TxtSpotlightHotkey.Text = !string.IsNullOrWhiteSpace(_config.SpotlightHotkeyText)
                ? _config.SpotlightHotkeyText
                : defaultSpotlightText;

            // 2. Hotkey QuickLook
            _quickLookKey = _config.QuickLookKey;
            _quickLookModifiers = _config.QuickLookModifiers;
            string defaultQuickLookText = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "Spazio" : "Space";
            TxtQuickLookHotkey.Text = !string.IsNullOrWhiteSpace(_config.QuickLookHotkeyText)
                ? _config.QuickLookHotkeyText
                : defaultQuickLookText;

            // 3. Drives
            _drives.Clear();
            try
            {
                string format = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Drives_Item_Format");
                string defaultLabel = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Drives_Default_Label");

                var detectedDrives = DriveInfo.GetDrives();
                foreach (var d in detectedDrives)
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        string letter = d.Name.TrimEnd('\\', ':').ToUpperInvariant();
                        string volumeLabel = string.Empty;
                        try { if (!string.IsNullOrWhiteSpace(d.VolumeLabel)) volumeLabel = d.VolumeLabel; } catch { }
                        string sizeGb = "N/D";
                        try { sizeGb = $"{d.TotalSize / (1024.0 * 1024 * 1024):F1} GB"; } catch { }
                        bool isIncluded = _config.IncludedDrives.Any(x => string.Equals(x.TrimEnd('\\', ':'), letter, StringComparison.OrdinalIgnoreCase));

                        string displayLabel = string.IsNullOrWhiteSpace(volumeLabel) ? defaultLabel : volumeLabel;

                        _drives.Add(new DriveItem
                        {
                            Letter = letter,
                            VolumeLabel = volumeLabel,
                            SizeText = sizeGb,
                            DisplayName = string.Format(format, letter, displayLabel, sizeGb),
                            IsIncluded = isIncluded
                        });
                    }
                    catch { }
                }
            }
            catch { }

            // 4. Exclusions
            _exclusions.Clear();
            var defaultExclusions = new[]
            {
                "SteamLibrary",
                "Epic Games",
                @"AppData\Local\Temp",
                "$Recycle.Bin",
                ".git",
                "node_modules",
                "obj",
                "bin"
            };

            var defaultSet = new HashSet<string>(defaultExclusions, StringComparer.OrdinalIgnoreCase);

            // Prima le standard
            foreach (var exc in defaultExclusions)
            {
                bool isExc = _config.ExcludedDirectoryNames.Any(x => string.Equals(x, exc, StringComparison.OrdinalIgnoreCase));
                _exclusions.Add(new ExclusionItem
                {
                    Name = exc,
                    IsExcluded = isExc,
                    IsCustom = false
                });
            }

            // Poi le custom aggiunte dall'utente
            foreach (var custom in _config.ExcludedDirectoryNames)
            {
                if (!defaultSet.Contains(custom) && !_exclusions.Any(x => string.Equals(x.Name, custom, StringComparison.OrdinalIgnoreCase)))
                {
                    _exclusions.Add(new ExclusionItem
                    {
                        Name = custom,
                        IsExcluded = true,
                        IsCustom = true
                    });
                }
            }

            // 5. Memory & Performance
            ChkKeepWorkerWarm.IsChecked = _config.KeepWorkerWarm;
            TxtTimeoutSeconds.Text = _config.WorkerIdleTimeoutSeconds.ToString();

            // 6. Windows Startup (M4.2)
            ChkStartWithWindows.IsChecked = StartupManager.IsStartupEnabled();

            // 7. QuickLook Remember Size by Category
            ChkRememberImages.IsChecked = _config.RememberSizeImages;
            ChkRememberVideos.IsChecked = _config.RememberSizeVideos;
            ChkRememberAudio.IsChecked = _config.RememberSizeAudio;
            ChkRememberPdf.IsChecked = _config.RememberSizePdf;
            ChkRememberDocs.IsChecked = _config.RememberSizeDocuments;
            ChkRememberGeneric.IsChecked = _config.RememberSizeGeneric;

            // 8. Data Directory & Index Size (P6)
            TxtDataDirectoryPath.Text = AppDataPath.GetDataDirectory();
            TxtDataModeInfo.Text = AppDataPath.IsPortable
                ? AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Mode_Portable")
                : AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Mode_Standard");
            UpdateIndexDiskUsage();

            // 8. Version Info
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
            TxtAppVersionInfo.Text = AeroSpot.Core.Localization.LocalizationService.Get("Instructions_Version", verStr, "2026.10.20");
        }

        private void BtnOpenInstructions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string lang = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                string htmlDoc = Path.Combine(baseDir, "Docs", $"instructions.{lang}.html");
                if (!File.Exists(htmlDoc))
                {
                    htmlDoc = Path.Combine(baseDir, "Docs", "instructions.it.html");
                }
                if (!File.Exists(htmlDoc))
                {
                    htmlDoc = Path.Combine(baseDir, "Docs", "instructions.en.html");
                }

                if (File.Exists(htmlDoc))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = htmlDoc,
                        UseShellExecute = true
                    });
                    return;
                }

                OpenGitHubUrl("https://github.com/Ritolino/AeroSpot#readme");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile aprire le istruzioni: {ex.Message}", "AeroSpot", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnOpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenGitHubUrl("https://github.com/Ritolino/AeroSpot");
        }

        private void OpenGitHubUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void UpdateIndexDiskUsage()
        {
            try
            {
                string dbPath = AeroSpot.Core.Config.AppDataPath.GetDatabasePath();
                string? dir = Path.GetDirectoryName(dbPath);
                long totalBytes = 0;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var f in di.GetFiles("index.db*"))
                    {
                        totalBytes += f.Length;
                    }
                }

                string sizeFormatted = Services.PreviewLoader.FormatFileSize(totalBytes);
                string label = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it"
                    ? $"Spazio indice: {sizeFormatted}"
                    : $"Index disk size: {sizeFormatted}";
                TxtIndexDiskUsage.Text = label;
            }
            catch
            {
                TxtIndexDiskUsage.Text = string.Empty;
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguage?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                AeroSpot.Core.Localization.LocalizationService.Initialize(tag);
                if (TxtDataModeInfo != null)
                {
                    TxtDataModeInfo.Text = AppDataPath.IsPortable
                        ? AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Mode_Portable")
                        : AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Mode_Standard");
                }
                UpdateIndexDiskUsage();
                RefreshDriveItemDisplayNames();

                // Aggiorna etichette hotkey predefinite se non personalizzate
                if (TxtSpotlightHotkey != null && (TxtSpotlightHotkey.Text == "Alt + Spazio" || TxtSpotlightHotkey.Text == "Alt + Space"))
                {
                    TxtSpotlightHotkey.Text = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "Alt + Spazio" : "Alt + Space";
                }
                if (TxtQuickLookHotkey != null && (TxtQuickLookHotkey.Text == "Spazio" || TxtQuickLookHotkey.Text == "Space"))
                {
                    TxtQuickLookHotkey.Text = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "Spazio" : "Space";
                }
            }
        }

        private void RefreshDriveItemDisplayNames()
        {
            string format = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Drives_Item_Format");
            string defaultLabel = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Drives_Default_Label");
            foreach (var drive in _drives)
            {
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? defaultLabel : drive.VolumeLabel;
                drive.DisplayName = string.Format(format, drive.Letter, label, drive.SizeText);
            }
        }

        private void BtnOpenDataDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = AppDataPath.GetDataDirectory();
                if (Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void BtnToggleIndexingInfo_Click(object sender, RoutedEventArgs e)
        {
            BorderIndexingInfo.Visibility = (BorderIndexingInfo.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnToggleShortcutsInfo_Click(object sender, RoutedEventArgs e)
        {
            BorderShortcutsInfo.Visibility = (BorderShortcutsInfo.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnToggleSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            BorderSystemInfo.Visibility = (BorderSystemInfo.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnToggleWorkerInfo_Click(object sender, RoutedEventArgs e)
        {
            BorderSystemInfo.Visibility = (BorderSystemInfo.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnToggleStartupInfo_Click(object sender, RoutedEventArgs e)
        {
            BorderSystemInfo.Visibility = (BorderSystemInfo.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnToggleRememberSizeInfo_Click(object sender, RoutedEventArgs e)
        {
            BorderRememberSizeInfo.Visibility = (BorderRememberSizeInfo.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnDeleteExclusion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ExclusionItem item)
            {
                _exclusions.Remove(item);
            }
        }

        private void BtnAddExclusion_Click(object sender, RoutedEventArgs e)
        {
            string custom = TxtCustomExclusion.Text.Trim();
            if (!string.IsNullOrWhiteSpace(custom))
            {
                if (!_exclusions.Any(x => string.Equals(x.Name, custom, StringComparison.OrdinalIgnoreCase)))
                {
                    _exclusions.Add(new ExclusionItem { Name = custom, IsExcluded = true, IsCustom = true });
                    TxtCustomExclusion.Text = string.Empty;
                }
            }
        }

        private void SpotlightBorder_Click(object sender, MouseButtonEventArgs e)
        {
            TxtSpotlightHotkey.Focus();
        }

        private void QuickLookBorder_Click(object sender, MouseButtonEventArgs e)
        {
            TxtQuickLookHotkey.Focus();
        }

        private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Parent is Border b)
            {
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            }
        }

        private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Parent is Border b)
            {
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            }
        }

        private void SpotlightHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleHotkeyInput(e, out uint key, out uint mod, out string text);
            if (key != 0)
            {
                _spotlightKey = key;
                _spotlightModifiers = mod | Win32.MOD_NOREPEAT;
                TxtSpotlightHotkey.Text = text;
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void QuickLookHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleHotkeyInput(e, out uint key, out uint mod, out string text);
            if (key != 0)
            {
                _quickLookKey = key;
                _quickLookModifiers = mod;
                TxtQuickLookHotkey.Text = text;
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private static void HandleHotkeyInput(KeyEventArgs e, out uint vk, out uint mod, out string displayText)
        {
            vk = 0;
            mod = 0;
            displayText = string.Empty;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // Rifiuta Escape come da specifica utente
            if (key == Key.Escape)
            {
                return;
            }

            // Ignora la pressione isolata di soli tasti modificatori
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || e.SystemKey != Key.None;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

            if (ctrl) mod |= Win32.MOD_CONTROL;
            if (alt) mod |= Win32.MOD_ALT;
            if (shift) mod |= Win32.MOD_SHIFT;
            if (win) mod |= Win32.MOD_WIN;

            int rawVk = KeyInterop.VirtualKeyFromKey(key);
            vk = (uint)rawVk;

            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");

            string keyName = key switch
            {
                Key.Space => "Spazio",
                Key.Return => "Invio",
                Key.Back => "Backspace",
                Key.Tab => "Tab",
                Key.OemPlus => "+",
                Key.OemMinus => "-",
                Key.OemPeriod => ".",
                Key.OemComma => ",",
                _ => key.ToString()
            };
            parts.Add(keyName);

            displayText = string.Join(" + ", parts);
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(this,
                AeroSpot.Core.Localization.LocalizationService.Get("Settings_Reset_Confirm"),
                AeroSpot.Core.Localization.LocalizationService.Get("Settings_Reset_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _config = new AppConfig();
                _config.Save();
                LoadSettingsIntoUi();
                TxtStatus.Text = "✓ " + AeroSpot.Core.Localization.LocalizationService.Get("Settings_Status_Saved");
                ConfigSaved?.Invoke(_config);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Salva lingua
            if (CmbLanguage.SelectedItem is ComboBoxItem langItem && langItem.Tag is string langTag)
            {
                _config.Language = langTag;
            }

            // Salva Hotkey personalizzate
            _config.HotkeyKey = _spotlightKey;
            _config.HotkeyModifiers = _spotlightModifiers;
            _config.SpotlightHotkeyText = TxtSpotlightHotkey.Text;

            _config.QuickLookKey = _quickLookKey;
            _config.QuickLookModifiers = _quickLookModifiers;
            _config.QuickLookHotkeyText = TxtQuickLookHotkey.Text;

            // Salva Drives
            var selectedDrives = _drives.Where(d => d.IsIncluded).Select(d => d.Letter).ToList();
            if (selectedDrives.Count == 0)
            {
                selectedDrives.Add("C");
            }
            _config.IncludedDrives = selectedDrives;

            // Salva Esclusioni
            _config.ExcludedDirectoryNames = _exclusions.Where(x => x.IsExcluded).Select(x => x.Name).ToList();

            // Salva Prestazioni & Memoria
            _config.KeepWorkerWarm = ChkKeepWorkerWarm.IsChecked == true;
            if (int.TryParse(TxtTimeoutSeconds.Text.Trim(), out int timeout) && timeout >= 5)
            {
                _config.WorkerIdleTimeoutSeconds = timeout;
            }

            // Salva Avvio Automatico con Windows (M4.2)
            bool enableStartup = ChkStartWithWindows.IsChecked == true;
            _config.StartWithWindows = enableStartup;
            StartupManager.SetStartupEnabled(enableStartup);

            // Salva opzioni persistenza dimensioni QuickLook
            _config.RememberSizeImages = ChkRememberImages.IsChecked == true;
            _config.RememberSizeVideos = ChkRememberVideos.IsChecked == true;
            _config.RememberSizeAudio = ChkRememberAudio.IsChecked == true;
            _config.RememberSizePdf = ChkRememberPdf.IsChecked == true;
            _config.RememberSizeDocuments = ChkRememberDocs.IsChecked == true;
            _config.RememberSizeGeneric = ChkRememberGeneric.IsChecked == true;

            // Scrivi su disco
            _config.Save();

            TxtStatus.Text = "✓ " + AeroSpot.Core.Localization.LocalizationService.Get("Settings_Status_Saved");
            ConfigSaved?.Invoke(_config);

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                Hide();
            };
            timer.Start();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsShuttingDown)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
