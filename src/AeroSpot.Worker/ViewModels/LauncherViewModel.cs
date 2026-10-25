using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private int _selectedIndex = -1;
        private SearchResultItem? _selectedItem;
        private readonly SqliteIndexDatabase _db;
        private readonly bool _ownsDb;
        private readonly System.Timers.Timer _periodicCrawlTimer;

        public ObservableCollection<SearchResultItem> FilteredResults { get; } = new ObservableCollection<SearchResultItem>();

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
                    FilterResults();
                }
            }
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

        public LauncherViewModel(SqliteIndexDatabase? db = null)
        {
            _ownsDb = db == null;
            _db = db ?? new SqliteIndexDatabase();
            _db.Initialize();

            // Configura scansione incrementale periodica in background (ogni 15 minuti)
            _periodicCrawlTimer = new System.Timers.Timer(15 * 60 * 1000);
            _periodicCrawlTimer.AutoReset = true;
            _periodicCrawlTimer.Elapsed += (s, e) =>
            {
                TriggerFullReindex(force: false);
            };
            _periodicCrawlTimer.Start();

            // Popola il catalogo con applicazioni e utilità di base se vuoto
            if (_db.GetTotalCount() == 0)
            {
                InitializeBaseCatalog();
            }

            FilterResults();

            // Avvia la scansione in background all'avvio
            EnsureBackgroundIndexing();
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
            // Scansione iniziale solo se il database locale è vuoto (primo avvio)
            int count = _db.GetTotalCount();
            if (count == 0)
            {
                TriggerFullReindex(force: false);
            }
            else
            {
                AppLogger.Info($"LauncherViewModel: Database già popolato con {count} elementi indicizzati. Scansione disco non necessaria all'avvio.");
            }
        }

        public void TriggerFullReindex(bool force = true)
        {
            Task.Run(() =>
            {
                try
                {
                    AppLogger.Info($"LauncherViewModel: Avvio scansione completa indicizzazione (force={force})...");
                    var crawler = new FileCrawler(_db);
                    crawler.CrawlAll();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(FilterResults);
                    AppLogger.Info("LauncherViewModel: Scansione completa completata con successo.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"LauncherViewModel: Scansione background indicizzazione: {ex.Message}");
                }
            });
        }

        public void FilterResults()
        {
            FilteredResults.Clear();

            // 1. Controlla prima se l'input è un'espressione matematica o una conversione di unità
            var exprResult = ExpressionEvaluator.TryEvaluate(_searchText, AeroSpot.Core.Localization.LocalizationService.CurrentLanguage);
            if (exprResult != null)
            {
                var (bColor, bFg) = GetCategoryColors(exprResult.Category);
                var icon = exprResult.IsCalculator ? IconService.GetCalculatorIcon() : IconService.GetConversionIcon();
                FilteredResults.Add(new SearchResultItem(
                    title: exprResult.Title,
                    subtitle: exprResult.Subtitle,
                    category: exprResult.Category,
                    targetPath: string.Empty,
                    badgeColor: "#1E3A5F",
                    badgeForeground: "#38BDF8",
                    icon: icon,
                    copyText: exprResult.CopyValue,
                    isActionItem: true
                ));
            }

            var searchMatches = _db.Search(_searchText, 10);
            foreach (var item in searchMatches)
            {
                var (badgeColor, badgeFg) = GetCategoryColors(item.Category);
                var icon = IconService.GetIcon(item.FullPath, item.Category);

                FilteredResults.Add(new SearchResultItem(
                    title: item.Title,
                    subtitle: item.FullPath,
                    category: item.Category,
                    targetPath: item.ExecutionTarget,
                    badgeColor: badgeColor,
                    badgeForeground: badgeFg,
                    icon: icon
                ));
            }

            OnPropertyChanged(nameof(HasResults));

            // Nessun elemento selezionato inizialmente durante la digitazione
            SelectedIndex = -1;
            SelectedItem = null;
        }

        private static (string Bg, string Fg) GetCategoryColors(string category)
        {
            switch (category)
            {
                case "Applicazione":
                    return ("#264F78", "#9CDCFE");
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
                default:
                    return ("#333333", "#CCCCCC");
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
            SelectedItem = FilteredResults[next];
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
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Errore nell'avvio di {target.TargetPath}", ex);
                return false;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public void Dispose()
        {
            _periodicCrawlTimer?.Dispose();

            if (_ownsDb)
            {
                _db.Dispose();
            }
        }
    }
}
