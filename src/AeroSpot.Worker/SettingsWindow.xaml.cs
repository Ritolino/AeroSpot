using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Config;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Native;
using AeroSpot.Core.Search;
using AeroSpot.Core.Logging;

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

    public sealed class CategoryHierarchyItem : System.ComponentModel.INotifyPropertyChanged
    {
        private int _rank;
        private bool _isBeingDragged;
        private bool _isDropTarget;

        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int Rank
        {
            get => _rank;
            set
            {
                if (_rank != value)
                {
                    _rank = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Rank)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(RankText)));
                }
            }
        }
        public string RankText => $"#{Rank}";

        public bool IsBeingDragged
        {
            get => _isBeingDragged;
            set
            {
                if (_isBeingDragged != value)
                {
                    _isBeingDragged = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsBeingDragged)));
                }
            }
        }

        public bool IsDropTarget
        {
            get => _isDropTarget;
            set
            {
                if (_isDropTarget != value)
                {
                    _isDropTarget = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDropTarget)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class SettingsWindow : Window
    {
        private AppConfig _config;
        private readonly ObservableCollection<DriveItem> _drives = new ObservableCollection<DriveItem>();
        private readonly ObservableCollection<ExclusionItem> _exclusions = new ObservableCollection<ExclusionItem>();
        private readonly ObservableCollection<CategoryHierarchyItem> _hierarchyItems = new ObservableCollection<CategoryHierarchyItem>();
        private Point _dragStartPoint;

        private uint _spotlightKey;
        private uint _spotlightModifiers;
        private uint _quickLookKey;
        private uint _quickLookModifiers;
        private uint _deleteKey;
        private uint _deleteModifiers;

        private string? _latestReleaseUrl;
        private string? _downloadedUpdatePath;
        private string? _latestSha256SumsUrl;
        private string? _latestAssetFileName;
        private CancellationTokenSource? _updateCts;
        private System.Windows.Threading.DispatcherTimer? _saveCloseTimer;
        private bool _isLoadingUi;

        private static readonly SolidColorBrush HotkeyFocusedBorder = CreateFrozenBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
        private static readonly SolidColorBrush HotkeyNormalBorder = CreateFrozenBrush(Color.FromRgb(0x47, 0x55, 0x69));
        private static readonly SolidColorBrush TabActiveBrush = CreateFrozenBrush(Color.FromRgb(0x02, 0x84, 0xC7));
        private static readonly SolidColorBrush TabInactiveTextBrush = CreateFrozenBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public event Action<AppConfig>? ConfigSaved;

        public SettingsWindow()
        {
            App.EnsureSharedScrollBarResources(this.Resources);
            InitializeComponent();
            _config = AppConfig.Load();

            ListDrives.ItemsSource = _drives;
            ListExclusions.ItemsSource = _exclusions;
            HierarchyListBox.ItemsSource = _hierarchyItems;

            LoadSettingsIntoUi();

            HierarchyListBox.LostMouseCapture += (s, e) =>
            {
                if (_isDragging) EndDrag();
            };

            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    ResetStatusMessage();
                }
            };
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public void ResetStatusMessage()
        {
            _saveCloseTimer?.Stop();
            _saveCloseTimer = null;
            if (TxtStatus != null)
            {
                TxtStatus.Text = string.Empty;
                TxtStatus.Visibility = Visibility.Collapsed;
            }
            if (TxtClearRecentHistoryStatus != null)
            {
                TxtClearRecentHistoryStatus.Text = string.Empty;
                TxtClearRecentHistoryStatus.Visibility = Visibility.Collapsed;
            }
            if (TxtUpdateStatus != null)
            {
                TxtUpdateStatus.Text = string.Empty;
                TxtUpdateStatus.Visibility = Visibility.Collapsed;
            }
        }

        public void ReloadSettings()
        {
            _config = AppConfig.Load();
            LoadSettingsIntoUi();
        }

        private void LoadSettingsIntoUi()
        {
            _isLoadingUi = true;
            try
            {
                ResetStatusMessage();

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
                string defaultSpotlightText = $"Alt + {LocalizationService.Get("Settings_Key_Space")}";
                TxtSpotlightHotkey.Text = !string.IsNullOrWhiteSpace(_config.SpotlightHotkeyText)
                    ? _config.SpotlightHotkeyText
                    : defaultSpotlightText;

                // 2. Hotkey QuickLook
                _quickLookKey = _config.QuickLookKey;
                _quickLookModifiers = _config.QuickLookModifiers;
                string defaultQuickLookText = LocalizationService.Get("Settings_Key_Space");
                TxtQuickLookHotkey.Text = !string.IsNullOrWhiteSpace(_config.QuickLookHotkeyText)
                    ? _config.QuickLookHotkeyText
                    : defaultQuickLookText;

                // 2b. Hotkey Delete (Cestino in Spotlight)
                _deleteKey = _config.DeleteKey;
                _deleteModifiers = _config.DeleteModifiers;
                string defaultDeleteText = LocalizationService.Get("Settings_Key_Delete");
                TxtDeleteHotkey.Text = !string.IsNullOrWhiteSpace(_config.DeleteHotkeyText)
                    ? _config.DeleteHotkeyText
                    : defaultDeleteText;

            // 3. Drives (caricamento asincrono per non bloccare mai l'apertura della finestra)
            LoadDrivesAsync();

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
            ChkStartWithWindows.IsChecked = StartupManager.IsStartupEnabled() || (_config.StartWithWindows && StartupManager.IsStartupApprovedByUser());

            // 6b. Conversion & Shell Integration
            ChkOpenFolderAfterConversion.IsChecked = _config.OpenFolderAfterConversion;
            ChkIntegrateWindowsContextMenu.IsChecked = _config.IntegrateWindowsContextMenu || AeroSpot.Core.Native.ShellContextMenuIntegration.IsRegistered();

            // 7. QuickLook Remember Size by Category
            ChkRememberImages.IsChecked = _config.RememberSizeImages;
            ChkRememberVideos.IsChecked = _config.RememberSizeVideos;
            ChkRememberAudio.IsChecked = _config.RememberSizeAudio;
            ChkRememberPdf.IsChecked = _config.RememberSizePdf;
            ChkRememberDocs.IsChecked = _config.RememberSizeDocuments;
            ChkRememberGeneric.IsChecked = _config.RememberSizeGeneric;
            ChkAlwaysOnTop.IsChecked = _config.QuickLookAlwaysOnTop;

            // 8. Data Directory & Index Size (P6)
            TxtDataDirectoryPath.Text = AppDataPath.GetDataDirectory();
            TxtDataModeInfo.Text = AppDataPath.IsPortable
                ? AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Mode_Portable")
                : AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Mode_Standard");
            UpdateIndexDiskUsageAsync();

            // 8. Version Info
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
            TxtAppVersionInfo.Text = AeroSpot.Core.Localization.LocalizationService.Get("Instructions_Version", verStr, "25.11.2026");

            // 8b. Aggiornamenti dell'applicazione (GitHub Release Checker)
            TxtCurrentAppVersion.Text = "v" + AeroSpot.Core.Services.UpdateService.CurrentVersion;
            string freq = _config.UpdateCheckFrequency ?? "Startup";
            foreach (ComboBoxItem item in CmbUpdateFrequency.Items)
            {
                if (string.Equals(item.Tag?.ToString(), freq, StringComparison.OrdinalIgnoreCase))
                {
                    CmbUpdateFrequency.SelectedItem = item;
                    break;
                }
            }
            if (_config.LastUpdateCheckUtc != DateTime.MinValue)
            {
                TxtLastCheckedTime.Text = string.Format(
                    AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_LastChecked"),
                    _config.LastUpdateCheckUtc.ToLocalTime().ToString("g"));
            }
            else
            {
                TxtLastCheckedTime.Text = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_NeverChecked");
            }

            if (TxtClearRecentHistoryStatus != null)
            {
                TxtClearRecentHistoryStatus.Text = string.Empty;
            }
            if (CmbHomeDisplayMode != null)
            {
                CmbHomeDisplayMode.SelectedItem = _config.HomeDisplayMode switch
                {
                    SpotlightHomeMode.MostFrequentSearches => CmbItemMostFrequentSearches,
                    SpotlightHomeMode.MostFrequentlyOpened => CmbItemMostFrequentItems,
                    SpotlightHomeMode.RecentOpened => CmbItemRecentOpened,
                    SpotlightHomeMode.Alphabetical => CmbItemAlphabetical,
                    SpotlightHomeMode.None => CmbItemNone,
                    _ => CmbItemMostFrequentSearches
                };
            }

            if (CmbDragDropMode != null)
            {
                CmbDragDropMode.SelectedItem = _config.DragDropMode switch
                {
                    SpotlightDragDropMode.AlwaysCopy => CmbItemDragDropAlwaysCopy,
                    SpotlightDragDropMode.AlwaysMove => CmbItemDragDropAlwaysMove,
                    _ => CmbItemDragDropWindowsDefault
                };
            }

            if (CmbIndexFrequency != null)
            {
                int targetFreqTag = (int)_config.IndexUpdateFrequency;
                foreach (ComboBoxItem item in CmbIndexFrequency.Items)
                {
                    if (item.Tag?.ToString() == targetFreqTag.ToString())
                    {
                        CmbIndexFrequency.SelectedItem = item;
                        break;
                    }
                }
                if (CmbIndexFrequency.SelectedItem == null && CmbIndexFrequency.Items.Count > 0)
                {
                    CmbIndexFrequency.SelectedIndex = 0;
                }
            }

            // 9. Spotlight Category Hierarchy
            PopulateHierarchyItems(_config.CategoryPriority);
            ChkPrioritizeExactMatches.IsChecked = _config.PrioritizeExactNameMatches;
            }
            finally
            {
                _isLoadingUi = false;
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnViewReleaseNotes.Visibility = Visibility.Collapsed;
            BtnApplyUpdate.Visibility = Visibility.Collapsed;
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Checking");
            PbUpdateDownload.Visibility = Visibility.Visible;
            PbUpdateDownload.IsIndeterminate = true;

            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();

            try
            {
                var result = await AeroSpot.Core.Services.UpdateService.CheckForUpdatesAsync(ct: _updateCts.Token);
                _config.LastUpdateCheckUtc = DateTime.UtcNow;
                _config.Save();

                TxtLastCheckedTime.Text = string.Format(
                    AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_LastChecked"),
                    _config.LastUpdateCheckUtc.ToLocalTime().ToString("g"));

                if (result.IsSuccess)
                {
                    if (result.HasUpdate)
                    {
                        _latestReleaseUrl = result.ReleaseUrl;
                        BtnViewReleaseNotes.Visibility = Visibility.Visible;

                        if (!string.IsNullOrEmpty(result.DownloadUrl))
                        {
                            TxtUpdateStatus.Text = string.Format(
                                AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Available"),
                                result.LatestVersion) + " " + string.Format(AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Downloading"), 0);

                            PbUpdateDownload.IsIndeterminate = false;
                            PbUpdateDownload.Value = 0;

                            var progress = new Progress<double>(pct =>
                            {
                                PbUpdateDownload.Value = pct;
                                TxtUpdateStatus.Text = string.Format(
                                    AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Available"),
                                    result.LatestVersion) + " " + string.Format(AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Downloading"), (int)pct);
                            });

                            string fileName = result.AssetFileName ?? $"AeroSpot-Setup-v{result.LatestVersion}.exe";
                            _latestAssetFileName = fileName;
                            _latestSha256SumsUrl = result.Sha256SumsUrl;
                            string downloadedPath = await AeroSpot.Core.Services.UpdateService.DownloadUpdateAsync(
                                result.DownloadUrl, fileName, progress, ct: _updateCts.Token);

                            _downloadedUpdatePath = downloadedPath;
                            TxtUpdateStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Ready");
                            PbUpdateDownload.Visibility = Visibility.Collapsed;
                            BtnApplyUpdate.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            TxtUpdateStatus.Text = string.Format(
                                AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Available"),
                                result.LatestVersion);
                            PbUpdateDownload.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        TxtUpdateStatus.Text = string.Format(
                            AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_UpToDate"),
                            AeroSpot.Core.Services.UpdateService.CurrentVersion);
                        PbUpdateDownload.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    TxtUpdateStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Error") +
                        (string.IsNullOrEmpty(result.ErrorMessage) ? "" : $" ({result.ErrorMessage})");
                    PbUpdateDownload.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                TxtUpdateStatus.Text = "";
                PbUpdateDownload.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Updates_Error") + $" ({ex.Message})";
                PbUpdateDownload.Visibility = Visibility.Collapsed;
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private async void BtnApplyUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_downloadedUpdatePath))
            {
                BtnApplyUpdate.IsEnabled = false;
                try
                {
                    TxtUpdateStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Installing_Detail");
                    await AeroSpot.Core.Services.UpdateService.PrepareAndApplyUpdateAsync(
                        _downloadedUpdatePath,
                        _latestSha256SumsUrl,
                        _latestAssetFileName,
                        onShutdownRequested: () =>
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                if (System.Windows.Application.Current is App app)
                                {
                                    _ = app.RequestFullSuiteShutdownForUpdateAsync();
                                }
                                else
                                {
                                    System.Windows.Application.Current.Shutdown(0);
                                }
                            });
                        });
                }
                catch (Exception ex)
                {
                    TxtUpdateStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Update_Pill_Error_Title") + ": " + ex.Message;
                    BtnApplyUpdate.IsEnabled = true;
                }
            }
        }

        private void BtnViewReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_latestReleaseUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _latestReleaseUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
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
                MessageBox.Show(this, LocalizationService.Get("Settings_Instructions_OpenError", ex.Message), "AeroSpot", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private async void LoadDrivesAsync()
        {
            var included = _config.IncludedDrives ?? new List<string>();
            string format = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Drives_Item_Format");
            string defaultLabel = AeroSpot.Core.Localization.LocalizationService.Get("Settings_Drives_Default_Label");

            var driveItems = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<DriveItem>();
                try
                {
                    var detectedDrives = DriveInfo.GetDrives();
                    foreach (var d in detectedDrives)
                    {
                        try
                        {
                            if (!d.IsReady) continue;
                            string letter = d.Name.TrimEnd('\\', ':').ToUpperInvariant();
                            string volumeLabel = string.Empty;
                            try { if (!string.IsNullOrWhiteSpace(d.VolumeLabel)) volumeLabel = d.VolumeLabel; } catch { }
                            string sizeGb = LocalizationService.Get("Settings_NotAvailable");
                            try { sizeGb = $"{d.TotalSize / (1024.0 * 1024 * 1024):F1} GB"; } catch { }
                            bool isIncluded = included.Any(x => string.Equals(x.TrimEnd('\\', ':'), letter, StringComparison.OrdinalIgnoreCase));

                            string displayLabel = string.IsNullOrWhiteSpace(volumeLabel) ? defaultLabel : volumeLabel;

                            list.Add(new DriveItem
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
                return list;
            });

            void UpdateUI()
            {
                _drives.Clear();
                foreach (var item in driveItems)
                {
                    _drives.Add(item);
                }
            }

            if (Dispatcher.CheckAccess())
            {
                UpdateUI();
            }
            else
            {
                try { Dispatcher.Invoke(UpdateUI); } catch { }
            }
        }

        private async void UpdateIndexDiskUsageAsync()
        {
            try
            {
                long totalBytes = await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        string dbPath = AeroSpot.Core.Config.AppDataPath.GetDatabasePath();
                        string? dir = Path.GetDirectoryName(dbPath);
                        long total = 0;
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            var di = new DirectoryInfo(dir);
                            foreach (var f in di.GetFiles("index.db*"))
                            {
                                total += f.Length;
                            }
                        }
                        return total;
                    }
                    catch
                    {
                        return 0L;
                    }
                });

                string sizeFormatted = Services.PreviewLoader.FormatFileSize(totalBytes);
                string label = LocalizationService.Get("Settings_Index_DiskSize", sizeFormatted);

                void ApplyLabel(string text)
                {
                    if (TxtIndexDiskUsage != null)
                    {
                        TxtIndexDiskUsage.Text = text;
                    }
                }

                if (Dispatcher.CheckAccess())
                {
                    ApplyLabel(label);
                }
                else
                {
                    try { Dispatcher.Invoke(() => ApplyLabel(label)); } catch { }
                }
            }
            catch
            {
                try
                {
                    if (Dispatcher.CheckAccess())
                    {
                        if (TxtIndexDiskUsage != null) TxtIndexDiskUsage.Text = string.Empty;
                    }
                    else
                    {
                        Dispatcher.Invoke(() => { if (TxtIndexDiskUsage != null) TxtIndexDiskUsage.Text = string.Empty; });
                    }
                }
                catch { }
            }
        }

        private void BtnToggleHistoryInfo_Click(object sender, RoutedEventArgs e)
        {
            if (BorderHistoryInfo != null)
            {
                BorderHistoryInfo.Visibility = BorderHistoryInfo.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi) return;

            if (CmbLanguage?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                LocalizationService.Initialize(tag);
                if (TxtDataModeInfo != null)
                {
                    TxtDataModeInfo.Text = AppDataPath.IsPortable
                        ? LocalizationService.Get("Settings_DataDir_Mode_Portable")
                        : LocalizationService.Get("Settings_DataDir_Mode_Standard");
                }
                UpdateIndexDiskUsageAsync();
                RefreshDriveItemDisplayNames();

                // Ricostruisci le voci di gerarchia mantenendo l'ordine corrente
                if (_hierarchyItems.Count > 0)
                {
                    PopulateHierarchyItems(_hierarchyItems.Select(x => x.Key).ToList());
                }

                // Aggiorna etichette hotkey predefinite se non personalizzate
                string spaceLabel = LocalizationService.Get("Settings_Key_Space");
                string delLabel = LocalizationService.Get("Settings_Key_Delete");
                if (TxtSpotlightHotkey != null && (TxtSpotlightHotkey.Text == "Alt + Spazio" || TxtSpotlightHotkey.Text == "Alt + Space"))
                {
                    TxtSpotlightHotkey.Text = $"Alt + {spaceLabel}";
                }
                if (TxtQuickLookHotkey != null && (TxtQuickLookHotkey.Text == "Spazio" || TxtQuickLookHotkey.Text == "Space"))
                {
                    TxtQuickLookHotkey.Text = spaceLabel;
                }
                if (TxtDeleteHotkey != null && (TxtDeleteHotkey.Text == "Canc" || TxtDeleteHotkey.Text == "Del"))
                {
                    TxtDeleteHotkey.Text = delLabel;
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

        private async void BtnReindexFromSettings_Click(object sender, RoutedEventArgs e)
        {
            if (BtnReindexFromSettings != null)
            {
                BtnReindexFromSettings.Content = AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Btn_Reindexing");
                BtnReindexFromSettings.IsEnabled = false;
            }

            try
            {
                if (App.MainLauncherWindow != null)
                {
                    await App.MainLauncherWindow.ViewModel.TriggerFullReindexAsync(force: true);
                }

                if (BtnReindexFromSettings != null)
                {
                    BtnReindexFromSettings.Content = AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Btn_Reindexed");
                    await Task.Delay(2500);
                    BtnReindexFromSettings.Content = AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Btn_ReindexNow");
                    BtnReindexFromSettings.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"BtnReindexFromSettings_Click: {ex.Message}");
                if (BtnReindexFromSettings != null)
                {
                    BtnReindexFromSettings.Content = AeroSpot.Core.Localization.LocalizationService.Get("Settings_DataDir_Btn_ReindexNow");
                    BtnReindexFromSettings.IsEnabled = true;
                }
            }
        }

        private void BtnClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var db = new SqliteIndexDatabase();
                db.ClearAllHistory();
                if (TxtClearRecentHistoryStatus != null)
                {
                    TxtClearRecentHistoryStatus.Text = AeroSpot.Core.Localization.LocalizationService.Get("Settings_RecentHistory_Cleared");
                    TxtClearRecentHistoryStatus.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Errore nella cancellazione della cronologia da Impostazioni: {ex.Message}");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _saveCloseTimer?.Stop();
            _saveCloseTimer = null;
            if (TxtStatus != null) TxtStatus.Text = string.Empty;
            LocalizationService.Initialize(_config.Language);
            Hide();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _saveCloseTimer?.Stop();
            _saveCloseTimer = null;
            if (TxtStatus != null) TxtStatus.Text = string.Empty;
            LocalizationService.Initialize(_config.Language);
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
                b.BorderBrush = HotkeyFocusedBorder;
            }
        }

        private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Parent is Border b)
            {
                b.BorderBrush = HotkeyNormalBorder;
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

        private void DeleteBorder_Click(object sender, MouseButtonEventArgs e)
        {
            TxtDeleteHotkey.Focus();
        }

        private void DeleteHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleHotkeyInput(e, out uint key, out uint mod, out string text);
            if (key != 0)
            {
                _deleteKey = key;
                _deleteModifiers = mod;
                TxtDeleteHotkey.Text = text;
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
                Key.Space => LocalizationService.Get("Settings_Key_Space"),
                Key.Return => LocalizationService.Get("Settings_Key_Enter"),
                Key.Delete => LocalizationService.Get("Settings_Key_Delete"),
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
                TxtStatus.Visibility = Visibility.Visible;
                ConfigSaved?.Invoke(_config);
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (BtnSave != null) BtnSave.IsEnabled = false;
            try
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

                _config.DeleteKey = _deleteKey;
                _config.DeleteModifiers = _deleteModifiers;
                _config.DeleteHotkeyText = TxtDeleteHotkey.Text;

                // Salva Drives
                var selectedDrives = _drives.Where(d => d.IsIncluded).Select(d => d.Letter).ToList();
                if (selectedDrives.Count == 0)
                {
                    selectedDrives.Add("C");
                }
                _config.IncludedDrives = selectedDrives;

                // Salva Esclusioni
                _config.ExcludedDirectoryNames = _exclusions.Where(x => x.IsExcluded).Select(x => x.Name).ToList();

                // Salva Prestazioni & Memoria (validazione timeout minimo 5s)
                _config.KeepWorkerWarm = ChkKeepWorkerWarm.IsChecked == true;
                if (int.TryParse(TxtTimeoutSeconds.Text.Trim(), out int timeout))
                {
                    _config.WorkerIdleTimeoutSeconds = Math.Clamp(timeout, 5, 86400);
                }
                TxtTimeoutSeconds.Text = _config.WorkerIdleTimeoutSeconds.ToString();

                // Salva Avvio Automatico con Windows (M4.2)
                bool enableStartup = ChkStartWithWindows.IsChecked == true;
                _config.StartWithWindows = enableStartup;
                StartupManager.SetStartupEnabled(enableStartup);

                // Salva opzioni conversione e integrazione shell
                _config.OpenFolderAfterConversion = ChkOpenFolderAfterConversion.IsChecked == true;
                bool integrateMenu = ChkIntegrateWindowsContextMenu.IsChecked == true;
                _config.IntegrateWindowsContextMenu = integrateMenu;
                if (integrateMenu)
                {
                    AeroSpot.Core.Native.ShellContextMenuIntegration.Register(AppDomain.CurrentDomain.BaseDirectory, _config.Language);
                }
                else
                {
                    AeroSpot.Core.Native.ShellContextMenuIntegration.Unregister();
                }

                // Salva opzioni persistenza dimensioni QuickLook
                _config.RememberSizeImages = ChkRememberImages.IsChecked == true;
                _config.RememberSizeVideos = ChkRememberVideos.IsChecked == true;
                _config.RememberSizeAudio = ChkRememberAudio.IsChecked == true;
                _config.RememberSizePdf = ChkRememberPdf.IsChecked == true;
                _config.RememberSizeDocuments = ChkRememberDocs.IsChecked == true;
                _config.RememberSizeGeneric = ChkRememberGeneric.IsChecked == true;
                _config.QuickLookAlwaysOnTop = ChkAlwaysOnTop.IsChecked == true;

                // Salva gerarchia Spotlight
                _config.CategoryPriority = _hierarchyItems.Select(x => x.Key).ToList();
                _config.PrioritizeExactNameMatches = ChkPrioritizeExactMatches.IsChecked == true;

                // Salva frequenza aggiornamenti automatici
                if (CmbUpdateFrequency.SelectedItem is ComboBoxItem selectedFreqItem)
                {
                    _config.UpdateCheckFrequency = selectedFreqItem.Tag?.ToString() ?? "Startup";
                }

                // Salva modalità home Spotlight e cronologia
                if (CmbHomeDisplayMode?.SelectedItem is ComboBoxItem selectedHomeItem)
                {
                    if (selectedHomeItem == CmbItemMostFrequentSearches) _config.HomeDisplayMode = SpotlightHomeMode.MostFrequentSearches;
                    else if (selectedHomeItem == CmbItemMostFrequentItems) _config.HomeDisplayMode = SpotlightHomeMode.MostFrequentlyOpened;
                    else if (selectedHomeItem == CmbItemRecentOpened) _config.HomeDisplayMode = SpotlightHomeMode.RecentOpened;
                    else if (selectedHomeItem == CmbItemAlphabetical) _config.HomeDisplayMode = SpotlightHomeMode.Alphabetical;
                    else if (selectedHomeItem == CmbItemNone) _config.HomeDisplayMode = SpotlightHomeMode.None;

                    _config.EnableRecentHistory = _config.HomeDisplayMode != SpotlightHomeMode.None;
                }

                // Salva comportamento Drag & Drop da Spotlight
                if (CmbDragDropMode?.SelectedItem is ComboBoxItem selectedDragDropItem &&
                    int.TryParse(selectedDragDropItem.Tag?.ToString(), out int ddTag))
                {
                    _config.DragDropMode = (SpotlightDragDropMode)ddTag;
                }

                // Salva frequenza aggiornamento automatico indice
                if (CmbIndexFrequency?.SelectedItem is ComboBoxItem selectedIndexFreqItem &&
                    int.TryParse(selectedIndexFreqItem.Tag?.ToString(), out int freqTag))
                {
                    _config.IndexUpdateFrequency = (IndexUpdateFrequency)freqTag;
                }

                // Scrivi su disco
                _config.Save();

                // Aggiorna PathFilter e attendi lo spurgo DB prima di ricaricare i risultati in ViewModel
                PathFilter.Configure(_config.IncludedDrives, _config.ExcludedDirectoryNames);
                var drivesCopy = _config.IncludedDrives.ToList();
                var exclusionsCopy = _config.ExcludedDirectoryNames.ToList();
                await Task.Run(() =>
                {
                    try
                    {
                        using var db = new SqliteIndexDatabase();
                        db.PurgeDisallowedDrives(drivesCopy);
                        db.PurgeExcludedSegments(exclusionsCopy);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Spurgo dischi da Impostazioni fallito: {ex.Message}");
                    }
                });

                TxtStatus.Text = "✓ " + LocalizationService.Get("Settings_Status_Saved");
                TxtStatus.Visibility = Visibility.Visible;
                ConfigSaved?.Invoke(_config);

                _saveCloseTimer?.Stop();
                _saveCloseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _saveCloseTimer.Tick += (s, ev) =>
                {
                    ResetStatusMessage();
                    UpdateLayout();
                    Hide();
                };
                _saveCloseTimer.Start();
            }
            finally
            {
                if (BtnSave != null) BtnSave.IsEnabled = true;
            }
        }

        #region Schede di Navigazione e Gerarchia Spotlight

        private void TabBtnGeneral_Click(object sender, RoutedEventArgs e)
        {
            SelectTab(0);
        }

        private void TabBtnQuickLook_Click(object sender, RoutedEventArgs e)
        {
            SelectTab(1);
        }

        private void TabBtnSpot_Click(object sender, RoutedEventArgs e)
        {
            SelectTab(2);
        }

        public void SelectTab(int tabIndex)
        {
            if (GeneralSettingsPanel != null) GeneralSettingsPanel.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (QuickLookSettingsPanel != null) QuickLookSettingsPanel.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (SpotSettingsPanel != null) SpotSettingsPanel.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

            var inactiveBrush = Brushes.Transparent;
            var activeText = Brushes.White;

            if (TabBtnGeneral != null)
            {
                TabBtnGeneral.Background = tabIndex == 0 ? TabActiveBrush : inactiveBrush;
                TabBtnGeneral.Foreground = tabIndex == 0 ? activeText : TabInactiveTextBrush;
            }

            if (TabBtnQuickLook != null)
            {
                TabBtnQuickLook.Background = tabIndex == 1 ? TabActiveBrush : inactiveBrush;
                TabBtnQuickLook.Foreground = tabIndex == 1 ? activeText : TabInactiveTextBrush;
            }

            if (TabBtnSpot != null)
            {
                TabBtnSpot.Background = tabIndex == 2 ? TabActiveBrush : inactiveBrush;
                TabBtnSpot.Foreground = tabIndex == 2 ? activeText : TabInactiveTextBrush;
            }
        }

        public void SelectTab(string? tabName)
        {
            switch (tabName?.ToLowerInvariant())
            {
                case "quicklook":
                case "preview":
                case "previews":
                    SelectTab(1);
                    break;
                case "spot":
                case "spotlight":
                case "search":
                case "indexing":
                case "hierarchy":
                    SelectTab(2);
                    break;
                default:
                    SelectTab(0);
                    break;
            }
        }

        private void PopulateHierarchyItems(List<string>? priorityList)
        {
            _hierarchyItems.Clear();
            var allKnown = AppConfig.GetDefaultCategoryPriority();
            var orderedKeys = new List<string>();

            if (priorityList != null)
            {
                foreach (var k in priorityList)
                {
                    if (allKnown.Contains(k, StringComparer.OrdinalIgnoreCase) && !orderedKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                    {
                        orderedKeys.Add(k);
                    }
                }
            }

            foreach (var k in allKnown)
            {
                if (!orderedKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                {
                    orderedKeys.Add(k);
                }
            }

            int rank = 1;
            foreach (var key in orderedKeys)
            {
                var item = CreateHierarchyItem(key, rank++);
                _hierarchyItems.Add(item);
            }
        }

        private static CategoryHierarchyItem CreateHierarchyItem(string key, int rank)
        {
            return key.ToLowerInvariant() switch
            {
                "impostazioni" or "settings" => new CategoryHierarchyItem
                {
                    Key = "Impostazioni",
                    Rank = rank,
                    Icon = "⚙️",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Settings"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Settings")
                },
                "applicazione" => new CategoryHierarchyItem
                {
                    Key = "Applicazione",
                    Rank = rank,
                    Icon = "🚀",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_App"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_App")
                },
                "documento" => new CategoryHierarchyItem
                {
                    Key = "Documento",
                    Rank = rank,
                    Icon = "📄",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Document"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Document")
                },
                "immagine" => new CategoryHierarchyItem
                {
                    Key = "Immagine",
                    Rank = rank,
                    Icon = "🖼️",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Image"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Image")
                },
                "video" => new CategoryHierarchyItem
                {
                    Key = "Video",
                    Rank = rank,
                    Icon = "🎬",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Video"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Video")
                },
                "audio" => new CategoryHierarchyItem
                {
                    Key = "Audio",
                    Rank = rank,
                    Icon = "🎵",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Audio"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Audio")
                },
                "cartella" => new CategoryHierarchyItem
                {
                    Key = "Cartella",
                    Rank = rank,
                    Icon = "📁",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Folder"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Folder")
                },
                "archivio" => new CategoryHierarchyItem
                {
                    Key = "Archivio",
                    Rank = rank,
                    Icon = "📦",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_Archive"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_Archive")
                },
                _ => new CategoryHierarchyItem
                {
                    Key = "File",
                    Rank = rank,
                    Icon = "📎",
                    Title = LocalizationService.Get("Settings_Hierarchy_Title_File"),
                    Subtitle = LocalizationService.Get("Settings_Hierarchy_Sub_File")
                }
            };
        }

        private bool _isDragging;
        private Point _dragOffset;
        private CategoryHierarchyItem? _currentDraggedItem;
        private CategoryHierarchyItem? _currentDropTarget;

        private void BtnResetHierarchy_Click(object sender, RoutedEventArgs e)
        {
            PopulateHierarchyItems(AppConfig.GetDefaultCategoryPriority());
            ChkPrioritizeExactMatches.IsChecked = true;
        }

        private void UpdateHierarchyRanks()
        {
            for (int i = 0; i < _hierarchyItems.Count; i++)
            {
                _hierarchyItems[i].Rank = i + 1;
            }
        }

        private void HierarchyListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                SpotSettingsPanel.ScrollToVerticalOffset(SpotSettingsPanel.VerticalOffset - (e.Delta * 0.6));
                e.Handled = true;
            }
        }

        private void HierarchyListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            var itemContainer = FindVisualParent<ListBoxItem>(dep);
            if (itemContainer != null && itemContainer.DataContext is CategoryHierarchyItem item)
            {
                _dragStartPoint = e.GetPosition(HierarchyListBox);
                _currentDraggedItem = item;
                _dragOffset = e.GetPosition(itemContainer);
            }
        }

        private void HierarchyListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _currentDraggedItem != null && !_isDragging)
            {
                Point currentPos = e.GetPosition(HierarchyListBox);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag();
                }
            }

            if (_isDragging && _currentDraggedItem != null)
            {
                // 1. Sposta l'elemento fantasma in tempo reale seguendo il cursore
                Point canvasPos = e.GetPosition(DragCanvas);
                Canvas.SetLeft(GhostVisual, canvasPos.X - _dragOffset.X);
                Canvas.SetTop(GhostVisual, canvasPos.Y - _dragOffset.Y);

                // 2. Identifica la destinazione corretta sotto il cursore
                Point listPos = e.GetPosition(HierarchyListBox);
                int targetIndex = GetItemIndexAtPosition(listPos);

                if (targetIndex >= 0 && targetIndex < _hierarchyItems.Count)
                {
                    var targetItem = _hierarchyItems[targetIndex];
                    if (targetItem != _currentDropTarget)
                    {
                        if (_currentDropTarget != null)
                        {
                            _currentDropTarget.IsDropTarget = false;
                        }
                        if (targetItem != _currentDraggedItem)
                        {
                            _currentDropTarget = targetItem;
                            _currentDropTarget.IsDropTarget = true;
                        }
                        else
                        {
                            _currentDropTarget = null;
                        }
                    }
                }
                else
                {
                    if (_currentDropTarget != null)
                    {
                        _currentDropTarget.IsDropTarget = false;
                        _currentDropTarget = null;
                    }
                }

                // 3. Auto-scroll fluido quando ci si avvicina ai bordi superiore/inferiore del pannello
                Point scrollPos = e.GetPosition(SpotSettingsPanel);
                if (scrollPos.Y < 40 && SpotSettingsPanel.VerticalOffset > 0)
                {
                    SpotSettingsPanel.ScrollToVerticalOffset(SpotSettingsPanel.VerticalOffset - 8);
                }
                else if (scrollPos.Y > SpotSettingsPanel.ActualHeight - 40)
                {
                    SpotSettingsPanel.ScrollToVerticalOffset(SpotSettingsPanel.VerticalOffset + 8);
                }
            }
        }

        private void StartDrag()
        {
            if (_currentDraggedItem == null) return;
            _isDragging = true;
            _currentDraggedItem.IsBeingDragged = true;

            // Configura il clone fantasma compatto (~230x36 px)
            GhostRankText.Text = _currentDraggedItem.RankText;
            GhostIconText.Text = _currentDraggedItem.Icon;
            GhostTitleText.Text = _currentDraggedItem.Title;

            GhostVisual.Width = 230;
            GhostVisual.Height = 36;

            // Limita l'offset in modo che il fantasma compatto rimanga vicino al cursore del mouse
            _dragOffset.X = Math.Min(_dragOffset.X, 160);
            _dragOffset.Y = Math.Min(_dragOffset.Y, 18);

            GhostVisual.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = Cursors.SizeAll;
            HierarchyListBox.CaptureMouse();
        }

        private int GetItemIndexAtPosition(Point listPos)
        {
            for (int i = 0; i < _hierarchyItems.Count; i++)
            {
                if (HierarchyListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
                {
                    Point itemPos = container.TranslatePoint(new Point(0, 0), HierarchyListBox);
                    if (listPos.Y >= itemPos.Y && listPos.Y <= itemPos.Y + container.ActualHeight)
                    {
                        return i;
                    }
                }
            }

            if (_hierarchyItems.Count > 0 && HierarchyListBox.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem firstContainer)
            {
                Point firstPos = firstContainer.TranslatePoint(new Point(0, 0), HierarchyListBox);
                if (listPos.Y < firstPos.Y) return 0;
            }

            int lastIdx = _hierarchyItems.Count - 1;
            if (lastIdx >= 0 && HierarchyListBox.ItemContainerGenerator.ContainerFromIndex(lastIdx) is ListBoxItem lastContainer)
            {
                Point lastPos = lastContainer.TranslatePoint(new Point(0, 0), HierarchyListBox);
                if (listPos.Y > lastPos.Y + lastContainer.ActualHeight) return lastIdx;
            }

            return -1;
        }

        private void HierarchyListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                if (_currentDropTarget != null && _currentDraggedItem != null)
                {
                    int oldIndex = _hierarchyItems.IndexOf(_currentDraggedItem);
                    int newIndex = _hierarchyItems.IndexOf(_currentDropTarget);
                    if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                    {
                        _hierarchyItems.Move(oldIndex, newIndex);
                        UpdateHierarchyRanks();
                    }
                }
                EndDrag();
            }
            else
            {
                _currentDraggedItem = null;
            }
        }

        private void EndDrag()
        {
            if (_currentDraggedItem != null)
            {
                _currentDraggedItem.IsBeingDragged = false;
                _currentDraggedItem = null;
            }
            if (_currentDropTarget != null)
            {
                _currentDropTarget.IsDropTarget = false;
                _currentDropTarget = null;
            }
            GhostVisual.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
            HierarchyListBox.ReleaseMouseCapture();
            _isDragging = false;
        }

        private void HierarchyListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isDragging)
            {
                EndDrag();
                e.Handled = true;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        #endregion

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ResetStatusMessage();
            UpdateLayout();
            LocalizationService.Initialize(_config.Language);

            if (!App.IsShuttingDown)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
