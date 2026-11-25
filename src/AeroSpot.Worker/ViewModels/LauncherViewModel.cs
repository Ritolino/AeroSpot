using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Search;
using AeroSpot.Worker.Models;
using AeroSpot.Worker.Services;

namespace AeroSpot.Worker.ViewModels
{
    public sealed class LauncherViewModel : INotifyPropertyChanged, IDisposable
    {
        private string _searchText = string.Empty;
        private string _selectedCategoryFilter = "All";
        private int _selectedIndex = -1;
        private SearchResultItem? _selectedItem;
        private readonly SqliteIndexDatabase _db;
        private readonly bool _ownsDb;
        private readonly System.Timers.Timer _periodicCrawlTimer;
        private RealtimeFolderWatcher? _realtimeWatcher;
        private AeroSpot.Core.Config.AppConfig _config = AeroSpot.Core.Config.AppConfig.Load();
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
        private CancellationTokenSource? _searchCts;
        private List<SearchResultItem>? _cachedHomeItems;
        private readonly SemaphoreSlim _reindexSemaphore = new SemaphoreSlim(1, 1);

        public bool EnableAsyncDebounce { get; set; }

        public event Action? SettingsRequested;

        public ObservableCollection<SearchResultItem> FilteredResults { get; } = new ObservableCollection<SearchResultItem>();

        public string SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                if (_selectedCategoryFilter != value)
                {
                    _selectedCategoryFilter = value;
                    OnPropertyChanged();
                    FilterResults();
                }
            }
        }

        public void ReloadConfig()
        {
            _config = AeroSpot.Core.Config.AppConfig.Load();
            ApplyIndexUpdateFrequency(_config.IndexUpdateFrequency);
            if (_config.EnableRealtimeWatcher)
            {
                if (_realtimeWatcher == null) StartRealtimeWatcher();
            }
            else
            {
                StopRealtimeWatcher();
            }
            FilterResults(preserveSelection: true);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSearchText));

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        _searchDebounceTimer?.Stop();
                        _searchCts?.Cancel();
                        if (!EnableAsyncDebounce)
                        {
                            ExecuteSearchSync(string.Empty, _selectedCategoryFilter);
                        }
                        else
                        {
                            _searchCts = new CancellationTokenSource();
                            ExecuteSearchAsync(string.Empty, _selectedCategoryFilter, _searchCts.Token);
                        }
                    }
                    else
                    {
                        if (!EnableAsyncDebounce)
                        {
                            ExecuteSearchSync(_searchText, _selectedCategoryFilter);
                        }
                        else
                        {
                            // In produzione WPF: debounce 50ms per digitazione a 0ms di latenza UI
                            _searchDebounceTimer?.Stop();
                            _searchDebounceTimer?.Start();
                        }
                    }
                }
            }
        }

        public void ResetSearchImmediate()
        {
            _searchDebounceTimer?.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;

            _searchText = string.Empty;
            _selectedCategoryFilter = "All";
            _selectedIndex = -1;
            _selectedItem = null;

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(HasSearchText));
            OnPropertyChanged(nameof(SelectedCategoryFilter));
            OnPropertyChanged(nameof(SelectedIndex));
            OnPropertyChanged(nameof(SelectedItem));

            if (!EnableAsyncDebounce)
            {
                // Nei test unitari (sincroni) esegui la query direttamente
                ExecuteSearchSync(string.Empty, "All");
                return;
            }

            // In produzione WPF: aggiorna istantaneamente in-place da cache in memoria (0ms, zero Clear(), zero sfarfallio container)
            if (_cachedHomeItems != null && _cachedHomeItems.Count > 0)
            {
                ApplyResultsToUI(_cachedHomeItems);
            }
            else
            {
                FilteredResults.Clear();
                OnPropertyChanged(nameof(HasResults));
            }

            // Aggiorna la vista Home in background in modo asincrono senza mai bloccare il Dispatcher
            _searchCts = new CancellationTokenSource();
            ExecuteSearchAsync(string.Empty, "All", _searchCts.Token);
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);
        public bool HasResults => FilteredResults.Count > 0;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public SearchResultItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public LauncherViewModel(SqliteIndexDatabase? db = null, bool enableAsyncDebounce = false)
        {
            EnableAsyncDebounce = enableAsyncDebounce;
            _ownsDb = db == null;
            _db = db ?? new SqliteIndexDatabase();
            _db.Initialize();

            // Configura scansione periodica in background secondo la frequenza configurata
            _periodicCrawlTimer = new System.Timers.Timer();
            _periodicCrawlTimer.AutoReset = true;
            _periodicCrawlTimer.Elapsed += (s, e) =>
            {
                TriggerFullReindex(force: false);
            };
            ApplyIndexUpdateFrequency(_config.IndexUpdateFrequency);

            // Timer debounce a 50ms per digitazione fluida a 0ms di latenza UI
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                ExecuteSearchAsync(_searchText, _selectedCategoryFilter, _searchCts.Token);
            };

            // Popola il catalogo con applicazioni e utilità di base se vuoto
            if (_db.GetTotalCount() == 0)
            {
                InitializeBaseCatalog();
            }

            // All'istanziazione iniziale, popola sempre la lista Home e la cache in memoria (_cachedHomeItems)
            ExecuteSearchSync(string.Empty, _selectedCategoryFilter);

            // Avvia la scansione in background all'avvio
            EnsureBackgroundIndexing();

            // Avvia monitoraggio cartelle utente in tempo reale
            if (_config.EnableRealtimeWatcher)
            {
                StartRealtimeWatcher();
            }
        }

        private void InitializeBaseCatalog()
        {
            try
            {
                var baseItems = new List<IndexedItem>();

                // 1. Scansiona le app di sistema e Start Menu
                var apps = AppsFolderScanner.ScanApps();
                baseItems.AddRange(apps);

                // 2. Aggiunge i documenti guida dell'applicazione
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string docsDir = Path.Combine(baseDir, "Docs");
                AddIfFileExists(baseItems, "AeroSpot — Guida Utente", Path.Combine(docsDir, "ISTRUZIONI.md"), "Documento");
                AddIfFileExists(baseItems, "AeroSpot — User Guide", Path.Combine(docsDir, "INSTRUCTIONS.md"), "Documento");
                AddIfFileExists(baseItems, "AeroSpot — Roadmap", Path.Combine(docsDir, "ROADMAP.md"), "Documento");
                AddIfFileExists(baseItems, "AeroSpot — Roadmap", Path.Combine(baseDir, "ROADMAP.md"), "Documento");
                AddIfFileExists(baseItems, "AeroSpot — Roadmap", Path.Combine(baseDir, "..", "..", "..", "ROADMAP.md"), "Documento");

                _db.UpsertBatch(baseItems);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Errore durante l'inizializzazione del catalogo di base", ex);
            }
        }

        private void AddIfFileExists(List<IndexedItem> list, string title, string path, string category)
        {
            if (File.Exists(path) && PathFilter.IsPathAllowed(path))
            {
                list.Add(new IndexedItem(title, path, category, path, isApp: false));
            }
        }

        private void EnsureBackgroundIndexing()
        {
            int count = _db.GetTotalCount();
            if (count == 0)
            {
                AppLogger.Info("LauncherViewModel: Database vuoto. Avvio prima indicizzazione iniziale...");
                TriggerFullReindex(force: false);
            }
            else
            {
                AppLogger.Info($"LauncherViewModel: Database già popolato con {count} elementi indicizzati. Nessun aggiornamento all'avvio (l'aggiornamento avverrà tramite il timer periodico impostato su {_config.IndexUpdateFrequency}).");
            }
        }

        private void StartRealtimeWatcher()
        {
            try
            {
                _realtimeWatcher?.Dispose();
                _realtimeWatcher = new RealtimeFolderWatcher(_db);
                _realtimeWatcher.ItemsChanged += () =>
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => FilterResults(preserveSelection: true));
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"StartRealtimeWatcher error: {ex.Message}");
            }
        }

        private void StopRealtimeWatcher()
        {
            try
            {
                _realtimeWatcher?.Dispose();
                _realtimeWatcher = null;
            }
            catch { }
        }

        public void ApplyIndexUpdateFrequency(AeroSpot.Core.Config.IndexUpdateFrequency freq)
        {
            try
            {
                _periodicCrawlTimer.Stop();

                double? intervalMs = freq switch
                {
                    AeroSpot.Core.Config.IndexUpdateFrequency.Every15Minutes => 15.0 * 60 * 1000,
                    AeroSpot.Core.Config.IndexUpdateFrequency.Every30Minutes => 30.0 * 60 * 1000,
                    AeroSpot.Core.Config.IndexUpdateFrequency.EveryHour => 60.0 * 60 * 1000,
                    AeroSpot.Core.Config.IndexUpdateFrequency.Every2Hours => 2.0 * 60.0 * 60 * 1000,
                    _ => null
                };

                if (intervalMs.HasValue)
                {
                    _periodicCrawlTimer.Interval = intervalMs.Value;
                    _periodicCrawlTimer.Start();
                    AppLogger.Info($"LauncherViewModel: Timer periodico aggiornamento indice impostato su {_periodicCrawlTimer.Interval / 60000} min.");
                }
                else
                {
                    AppLogger.Info("LauncherViewModel: Timer periodico aggiornamento indice disattivato (modalità Ogni avvio o Solo manuale).");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"ApplyIndexUpdateFrequency error: {ex.Message}");
            }
        }

        public async Task TriggerFullReindexAsync(bool force = true)
        {
            if (force)
            {
                await _reindexSemaphore.WaitAsync().ConfigureAwait(false);
            }
            else if (!await _reindexSemaphore.WaitAsync(0).ConfigureAwait(false))
            {
                AppLogger.Info("LauncherViewModel: Scansione periodica saltata perché una scansione è già in corso.");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        AppLogger.Info($"LauncherViewModel: Avvio scansione completa indicizzazione (force={force})...");
                        var crawler = new FileCrawler(_db);
                        crawler.CrawlAll();
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => FilterResults(preserveSelection: true));
                        AppLogger.Info("LauncherViewModel: Scansione completa completata con successo.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"LauncherViewModel: Scansione background indicizzazione: {ex.Message}");
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _reindexSemaphore.Release();
            }
        }

        public void TriggerFullReindex(bool force = true)
        {
            _ = TriggerFullReindexAsync(force);
        }

        public void FilterResults()
        {
            FilterResults(preserveSelection: false);
        }

        public void FilterResults(bool preserveSelection)
        {
            _searchDebounceTimer?.Stop();
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            if (!EnableAsyncDebounce)
            {
                ExecuteSearchSync(_searchText, _selectedCategoryFilter, preserveSelection);
            }
            else
            {
                ExecuteSearchAsync(_searchText, _selectedCategoryFilter, _searchCts.Token, preserveSelection);
            }
        }

        private void ExecuteSearchSync(string query, string category, bool preserveSelection = false)
        {
            var items = new List<SearchResultItem>();

            bool isAllFilter = string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(category, "Tutti", StringComparison.OrdinalIgnoreCase);

            SearchResultItem? actionItem = null;
            if (isAllFilter && !string.IsNullOrWhiteSpace(query))
            {
                var exprResult = ExpressionEvaluator.TryEvaluate(query, AeroSpot.Core.Localization.LocalizationService.CurrentLanguage);
                if (exprResult != null)
                {
                    var (bColor, bFg) = GetCategoryColors(exprResult.Category);
                    var icon = exprResult.IsCalculator ? IconService.GetCalculatorIcon() : IconService.GetConversionIcon();
                    actionItem = new SearchResultItem(
                        title: exprResult.Title,
                        subtitle: exprResult.Subtitle,
                        category: exprResult.Category,
                        targetPath: string.Empty,
                        badgeColor: "#1E3A5F",
                        badgeForeground: "#38BDF8",
                        icon: icon,
                        copyText: exprResult.CopyValue,
                        isActionItem: true
                    );
                    items.Add(actionItem);
                }
            }

            var searchMatches = _db.Search(query, 10, category, _config.CategoryPriority, _config.PrioritizeExactNameMatches, _config.HomeDisplayMode);
            foreach (var item in searchMatches)
            {
                var (badgeColor, badgeFg) = GetCategoryColors(item.Category);
                var icon = IconService.GetIcon(item.FullPath, item.Category);

                items.Add(new SearchResultItem(
                    title: item.Title,
                    subtitle: item.FullPath,
                    category: item.Category,
                    targetPath: item.ExecutionTarget,
                    badgeColor: badgeColor,
                    badgeForeground: badgeFg,
                    icon: icon
                ));
            }

            AppendSettingsResults(items, query, category, actionItem, searchMatches);

            if (string.IsNullOrEmpty(query) && isAllFilter)
            {
                _cachedHomeItems = new List<SearchResultItem>(items);
            }

            ApplyResultsToUI(items, preserveSelection);
        }

        private async void ExecuteSearchAsync(string query, string category, CancellationToken token, bool preserveSelection = false)
        {
            try
            {
                // 1. Math / calculator evaluation (fast in-memory)
                SearchResultItem? actionItem = null;
                bool isAllFilter = string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(category, "Tutti", StringComparison.OrdinalIgnoreCase);

                if (isAllFilter && !string.IsNullOrWhiteSpace(query))
                {
                    var exprResult = ExpressionEvaluator.TryEvaluate(query, AeroSpot.Core.Localization.LocalizationService.CurrentLanguage);
                    if (exprResult != null)
                    {
                        var (bColor, bFg) = GetCategoryColors(exprResult.Category);
                        var icon = exprResult.IsCalculator ? IconService.GetCalculatorIcon() : IconService.GetConversionIcon();
                        actionItem = new SearchResultItem(
                            title: exprResult.Title,
                            subtitle: exprResult.Subtitle,
                            category: exprResult.Category,
                            targetPath: string.Empty,
                            badgeColor: "#1E3A5F",
                            badgeForeground: "#38BDF8",
                            icon: icon,
                            copyText: exprResult.CopyValue,
                            isActionItem: true
                        );
                    }
                }

                if (token.IsCancellationRequested) return;

                // 2. Query SQLite e risoluzione icone in background worker thread (zero blocchi sul thread UI)
                var configPriority = _config.CategoryPriority;
                var prioritizeExact = _config.PrioritizeExactNameMatches;
                var homeMode = _config.HomeDisplayMode;

                var items = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return new List<SearchResultItem>();

                    var searchMatches = _db.Search(query, 10, category, configPriority, prioritizeExact, homeMode);
                    if (token.IsCancellationRequested) return new List<SearchResultItem>();

                    var resultItems = new List<SearchResultItem>();
                    if (actionItem != null)
                    {
                        resultItems.Add(actionItem);
                    }

                    foreach (var item in searchMatches)
                    {
                        if (token.IsCancellationRequested) return resultItems;
                        var (badgeColor, badgeFg) = GetCategoryColors(item.Category);
                        var icon = IconService.GetIcon(item.FullPath, item.Category);

                        resultItems.Add(new SearchResultItem(
                            title: item.Title,
                            subtitle: item.FullPath,
                            category: item.Category,
                            targetPath: item.ExecutionTarget,
                            badgeColor: badgeColor,
                            badgeForeground: badgeFg,
                            icon: icon
                        ));
                    }

                    if (token.IsCancellationRequested) return resultItems;

                    AppendSettingsResults(resultItems, query, category, actionItem, searchMatches);
                    return resultItems;
                }, token);

                if (token.IsCancellationRequested) return;

                // 3. Aggiornamento collezione UI sul Dispatcher
                if (string.IsNullOrEmpty(query) && isAllFilter)
                {
                    _cachedHomeItems = new List<SearchResultItem>(items);
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        ApplyResultsToUI(items, preserveSelection);
                    });
                }
                else
                {
                    ApplyResultsToUI(items, preserveSelection);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Warn($"LauncherViewModel: Errore durante la ricerca: {ex.Message}");
            }
        }

        private static bool AreItemsEquivalent(SearchResultItem a, SearchResultItem b)
        {
            return string.Equals(a.Title, b.Title, StringComparison.Ordinal) &&
                   string.Equals(a.Subtitle, b.Subtitle, StringComparison.Ordinal) &&
                   string.Equals(a.Category, b.Category, StringComparison.Ordinal) &&
                   string.Equals(a.TargetPath, b.TargetPath, StringComparison.Ordinal) &&
                   string.Equals(a.BadgeColor, b.BadgeColor, StringComparison.Ordinal) &&
                   a.IsActionItem == b.IsActionItem;
        }

        private void ApplyResultsToUI(List<SearchResultItem> items, bool preserveSelection = false)
        {
            var previousSelected = preserveSelection ? SelectedItem : null;

            // Se la lista è già identica a quella visualizzata (es. refresh asincrono Home o FileSystemWatcher),
            // non toccare FilteredResults per evitare qualsiasi ricreazione dei container e sfarfallio grafico.
            if (FilteredResults.Count == items.Count)
            {
                bool allSame = true;
                for (int i = 0; i < items.Count; i++)
                {
                    if (!AreItemsEquivalent(FilteredResults[i], items[i]))
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    OnPropertyChanged(nameof(HasResults));
                    return;
                }
            }

            if (items.Count == 0)
            {
                FilteredResults.Clear();
                OnPropertyChanged(nameof(HasResults));
                SelectedIndex = -1;
                SelectedItem = null;
                return;
            }

            // Aggiornamento differenziale in-place senza Clear() intermedio (mantiene stabile l'altezza della finestra)
            int commonCount = Math.Min(FilteredResults.Count, items.Count);
            for (int i = 0; i < commonCount; i++)
            {
                if (!AreItemsEquivalent(FilteredResults[i], items[i]))
                {
                    FilteredResults[i] = items[i];
                }
            }

            for (int i = commonCount; i < items.Count; i++)
            {
                FilteredResults.Add(items[i]);
            }

            while (FilteredResults.Count > items.Count)
            {
                FilteredResults.RemoveAt(FilteredResults.Count - 1);
            }

            OnPropertyChanged(nameof(HasResults));

            if (previousSelected != null)
            {
                int restoredIndex = -1;
                for (int i = 0; i < FilteredResults.Count; i++)
                {
                    if (AreItemsEquivalent(FilteredResults[i], previousSelected))
                    {
                        restoredIndex = i;
                        break;
                    }
                }

                if (restoredIndex >= 0)
                {
                    SelectedIndex = restoredIndex;
                    SelectedItem = FilteredResults[restoredIndex];
                    return;
                }
            }

            SelectedIndex = -1;
            SelectedItem = null;
        }

        private static (string Bg, string Fg) GetCategoryColors(string category)
        {
            switch (category)
            {
                case "Applicazione":
                    return ("#264F78", "#9CDCFE");
                case "Recente":
                case "Recent":
                    return ("#3D2E14", "#F59E0B");
                case "Impostazioni":
                case "Settings":
                    return ("#2D3748", "#A78BFA");
                case "Calcolatrice":
                case "Calculator":
                    return ("#1E4A38", "#4EC9B0");
                case "Conversione":
                case "Conversion":
                    return ("#4B2E58", "#DCDCAA");
                case "Documento":
                    return ("#4E3E26", "#CE9178");
                case "Immagine":
                    return ("#3B4E26", "#B5CEA8");
                case "Cartella":
                    return ("#3E3E42", "#DCDCDC");
                case "Ricerca":
                case "Search":
                    return ("#1E293B", "#94A3B8");
                default:
                    return ("#333333", "#CCCCCC");
            }
        }

        private void AppendSettingsResults(List<SearchResultItem> items, string query, string category, SearchResultItem? actionItem, List<AeroSpot.Core.Search.IndexedItem>? searchMatches)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            bool isAllFilter = string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(category, "Tutti", StringComparison.OrdinalIgnoreCase);
            bool isSettingsFilter = string.Equals(category, "Impostazioni", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(category, "Settings", StringComparison.OrdinalIgnoreCase);

            if (!isAllFilter && !isSettingsFilter) return;

            var settingsMatches = SettingsSearchCatalog.Search(query, AeroSpot.Core.Localization.LocalizationService.CurrentLanguage, isSettingsFilter ? 10 : 3);
            if (settingsMatches.Count == 0) return;

            var aeroSpotIcon = IconService.GetAeroSpotSettingsIcon();
            var windowsIcon = IconService.GetSettingsIcon();

            if (isSettingsFilter)
            {
                foreach (var setting in settingsMatches)
                {
                    var (bColor, bFg) = setting.IsAeroSpot
                        ? ("#0C4A6E", "#38BDF8") // AeroSpot Blue
                        : ("#2D3748", "#A78BFA"); // Windows Purple

                    items.Add(new SearchResultItem(
                        title: setting.GetTitle(AeroSpot.Core.Localization.LocalizationService.CurrentLanguage),
                        subtitle: setting.GetDescription(AeroSpot.Core.Localization.LocalizationService.CurrentLanguage),
                        category: "Impostazioni",
                        targetPath: setting.Uri,
                        badgeColor: bColor,
                        badgeForeground: bFg,
                        icon: setting.IsAeroSpot ? aeroSpotIcon : windowsIcon
                    ));
                }
                return;
            }

            var priorityList = _config.CategoryPriority ?? AeroSpot.Core.Config.AppConfig.GetDefaultCategoryPriority();
            int settingsPriority = priorityList.FindIndex(c => string.Equals(c, "Impostazioni", StringComparison.OrdinalIgnoreCase) || string.Equals(c, "Settings", StringComparison.OrdinalIgnoreCase));
            if (settingsPriority < 0) settingsPriority = 0;

            int GetCategoryRank(string cat)
            {
                int idx = priorityList.FindIndex(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : 999;
            }

            int insertTopIndex = actionItem != null ? 1 : 0;
            if (_config.PrioritizeExactNameMatches)
            {
                string trimmedQ = query.Trim();
                string normQuery = string.Join(" ", trimmedQ.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries));
                while (insertTopIndex < items.Count)
                {
                    string itemTitle = items[insertTopIndex].Title ?? string.Empty;
                    string itemTitleNoExt = itemTitle.Contains('.') ? Path.GetFileNameWithoutExtension(itemTitle) : itemTitle;
                    string normItemTitle = string.Join(" ", itemTitleNoExt.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries));
                    if (string.Equals(itemTitle, trimmedQ, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(itemTitleNoExt, trimmedQ, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(normQuery) && string.Equals(normItemTitle, normQuery, StringComparison.OrdinalIgnoreCase)))
                    {
                        insertTopIndex++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            foreach (var setting in settingsMatches)
            {
                var (bColor, bFg) = setting.IsAeroSpot
                    ? ("#0C4A6E", "#38BDF8") // AeroSpot Blue
                    : ("#2D3748", "#A78BFA"); // Windows Purple

                var settingItem = new SearchResultItem(
                    title: setting.GetTitle(AeroSpot.Core.Localization.LocalizationService.CurrentLanguage),
                    subtitle: setting.GetDescription(AeroSpot.Core.Localization.LocalizationService.CurrentLanguage),
                    category: "Impostazioni",
                    targetPath: setting.Uri,
                    badgeColor: bColor,
                    badgeForeground: bFg,
                    icon: setting.IsAeroSpot ? aeroSpotIcon : windowsIcon
                );

                if (settingsPriority == 0)
                {
                    items.Insert(insertTopIndex, settingItem);
                    insertTopIndex++;
                }
                else
                {
                    int insertIdx = items.Count;
                    for (int i = (actionItem != null ? 1 : 0); i < items.Count; i++)
                    {
                        if (GetCategoryRank(items[i].Category) > settingsPriority)
                        {
                            insertIdx = i;
                            break;
                        }
                    }
                    items.Insert(insertIdx, settingItem);
                }
            }

            if (items.Count > 10)
            {
                items.RemoveRange(10, items.Count - 10);
            }
        }

        public void MoveSelectionDown()
        {
            if (FilteredResults.Count == 0) return;
            int next = SelectedIndex + 1;
            if (SelectedIndex == -1)
            {
                next = 0; // Prima pressione seleziona il primo risultato
            }
            else if (next >= FilteredResults.Count)
            {
                next = 0; // Wrap around
            }
            SelectedIndex = next;
            if (next >= 0 && next < FilteredResults.Count)
            {
                SelectedItem = FilteredResults[next];
            }
        }

        public void MoveSelectionUp()
        {
            if (FilteredResults.Count == 0) return;
            int prev = SelectedIndex - 1;
            if (prev < 0)
            {
                prev = FilteredResults.Count - 1; // Wrap around
            }
            SelectedIndex = prev;
            SelectedItem = FilteredResults[prev];
        }

        public bool ExecuteSelected()
        {
            var target = SelectedItem;
            // Se nessun elemento è selezionato manualmente con le frecce, esegui il 1° risultato top-match
            if (target == null && FilteredResults.Count > 0)
            {
                target = FilteredResults[0];
            }

            if (target == null)
            {
                return false;
            }

            if (target.Category == "Ricerca" || target.Category == "Search")
            {
                SearchText = target.Title;
                return false;
            }

            // Se è un elemento azione (es. Calcolatrice o Conversione), copia il risultato negli appunti
            if (target.IsActionItem && !string.IsNullOrEmpty(target.CopyText))
            {
                try
                {
                    System.Windows.Clipboard.SetText(target.CopyText);
                    AppLogger.Info($"Risultato calcolo/conversione copiato negli appunti: {target.CopyText}");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Impossibile copiare negli appunti: {ex.Message}");
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(target.TargetPath))
            {
                return false;
            }

            try
            {
                AppLogger.Info($"Avvio elemento selezionato: {target.Title} -> {target.TargetPath}");

                if (target.TargetPath.StartsWith("aerospot:settings", StringComparison.OrdinalIgnoreCase))
                {
                    SettingsRequested?.Invoke();
                    return true;
                }

                var psi = new ProcessStartInfo();
                if (target.TargetPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = target.TargetPath;
                }
                else
                {
                    psi.FileName = target.TargetPath;
                }
                psi.UseShellExecute = true;

                Process.Start(psi);

                if (_config.HomeDisplayMode != AeroSpot.Core.Config.SpotlightHomeMode.None &&
                    !string.IsNullOrWhiteSpace(_searchText) &&
                    _searchText.Trim().Length >= 2 &&
                    ExpressionEvaluator.TryEvaluate(_searchText, AeroSpot.Core.Localization.LocalizationService.CurrentLanguage) == null)
                {
                    _db.RecordSearch(_searchText);
                }

                if (_config.HomeDisplayMode != AeroSpot.Core.Config.SpotlightHomeMode.None && !string.IsNullOrWhiteSpace(target.Subtitle) && !target.IsActionItem && !target.TargetPath.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                {
                    _db.RecordRecentItem(target.Subtitle);
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Errore nell'avvio di {target.TargetPath}", ex);
                return false;
            }
        }

        public void RecordRecentItem(string fullPath)
        {
            if (_config.HomeDisplayMode != AeroSpot.Core.Config.SpotlightHomeMode.None && !string.IsNullOrWhiteSpace(fullPath))
            {
                _db.RecordRecentItem(fullPath);
            }
        }

        public void NotifyItemMovedOrDeleted(SearchResultItem? item)
        {
            if (item == null || item.IsActionItem || string.IsNullOrWhiteSpace(item.TargetPath))
            {
                return;
            }

            string path = item.TargetPath;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                try
                {
                    _db.DeleteBatch(new[] { path });
                }
                catch { }

                _cachedHomeItems?.Remove(item);
                FilteredResults.Remove(item);
                OnPropertyChanged(nameof(HasResults));
                if (FilteredResults.Count > 0)
                {
                    SelectedIndex = Math.Clamp(SelectedIndex, 0, FilteredResults.Count - 1);
                    SelectedItem = FilteredResults[SelectedIndex];
                }
                else
                {
                    SelectedIndex = -1;
                    SelectedItem = null;
                }
            }
        }

        public bool DeleteSelectedItem()
        {
            var item = SelectedItem;
            if (item == null || item.IsActionItem || string.IsNullOrWhiteSpace(item.TargetPath))
            {
                return false;
            }

            string path = item.TargetPath;
            bool isFile = File.Exists(path);
            bool isDir = !isFile && Directory.Exists(path);

            if (!isFile && !isDir)
            {
                return false;
            }

            try
            {
                if (isFile)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                // Se l'elemento è stato rimosso (l'utente non ha annullato la finestra di conferma):
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    NotifyItemMovedOrDeleted(item);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                // Utente ha cliccato 'Annulla' nella finestra di conferma standard di Windows
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Impossibile spostare nel cestino '{path}': {ex.Message}");
            }

            return false;
        }

        public void OnSpotlightHidden()
        {
            string queryToRecord = _searchText;
            if (_config.HomeDisplayMode != AeroSpot.Core.Config.SpotlightHomeMode.None &&
                !string.IsNullOrWhiteSpace(queryToRecord) &&
                queryToRecord.Trim().Length >= 2 &&
                ExpressionEvaluator.TryEvaluate(queryToRecord, AeroSpot.Core.Localization.LocalizationService.CurrentLanguage) == null)
            {
                if (EnableAsyncDebounce)
                {
                    _ = Task.Run(() =>
                    {
                        try { _db.RecordSearch(queryToRecord); } catch { }
                    });
                }
                else
                {
                    _db.RecordSearch(queryToRecord);
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public void Dispose()
        {
            StopRealtimeWatcher();
            _searchDebounceTimer?.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _periodicCrawlTimer?.Dispose();

            if (_ownsDb)
            {
                _db.Dispose();
            }
        }
    }
}
