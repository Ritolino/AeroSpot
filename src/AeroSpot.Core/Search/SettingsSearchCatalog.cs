using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroSpot.Core.Search
{
    public sealed class SettingsEntry
    {
        public string Id { get; set; } = string.Empty;
        public string TitleIt { get; set; } = string.Empty;
        public string TitleEn { get; set; } = string.Empty;
        public string DescriptionIt { get; set; } = string.Empty;
        public string DescriptionEn { get; set; } = string.Empty;
        public string Uri { get; set; } = string.Empty;
        public string[] Keywords { get; set; } = Array.Empty<string>();
        public bool IsAeroSpot { get; set; }

        public string GetTitle(string lang) =>
            string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase) ? TitleIt : TitleEn;

        public string GetDescription(string lang) =>
            string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase) ? DescriptionIt : DescriptionEn;
    }

    /// <summary>
    /// Catalogo curato delle pagine di Impostazioni di Windows (ms-settings:) e delle schede di AeroSpot.
    /// Ricerca istantanea bilingue (it/en) a zero allocazioni I/O.
    /// </summary>
    public static class SettingsSearchCatalog
    {
        private static readonly List<SettingsEntry> _catalog = new List<SettingsEntry>
        {
            // --- Windows Settings ---
            new SettingsEntry
            {
                Id = "win_keyboard",
                TitleIt = "Tastiera (Impostazioni di sistema)",
                TitleEn = "Keyboard (System Settings)",
                DescriptionIt = "Configura lingua di digitazione, layout e opzioni tastiera",
                DescriptionEn = "Configure typing language, layout and keyboard options",
                Uri = "ms-settings:keyboard",
                Keywords = new[] { "tastiera", "keyboard", "typing", "digitazione", "layout", "lingua tastiera", "input" }
            },
            new SettingsEntry
            {
                Id = "win_display",
                TitleIt = "Schermo e luminosità (Impostazioni di sistema)",
                TitleEn = "Display & Brightness (System Settings)",
                DescriptionIt = "Risoluzione monitor, luminosità, scaling DPI e HDR",
                DescriptionEn = "Monitor resolution, brightness, DPI scaling and HDR",
                Uri = "ms-settings:display",
                Keywords = new[] { "schermo", "display", "monitor", "luminosita", "brightness", "risoluzione", "resolution", "scaling", "dpi", "hdr" }
            },
            new SettingsEntry
            {
                Id = "win_sound",
                TitleIt = "Audio e volume (Impostazioni di sistema)",
                TitleEn = "Sound & Volume (System Settings)",
                DescriptionIt = "Altoparlanti, cuffie, microfono e mixer del volume",
                DescriptionEn = "Speakers, headphones, microphone and volume mixer",
                Uri = "ms-settings:sound",
                Keywords = new[] { "suono", "sound", "audio", "volume", "altoparlanti", "speakers", "microfono", "microphone", "cuffie", "headphones" }
            },
            new SettingsEntry
            {
                Id = "win_wifi",
                TitleIt = "Wi-Fi e connessioni di rete (Impostazioni di sistema)",
                TitleEn = "Wi-Fi & Network (System Settings)",
                DescriptionIt = "Reti wireless, router, connessione internet ed Ethernet",
                DescriptionEn = "Wireless networks, router, internet connection and Ethernet",
                Uri = "ms-settings:network-wifi",
                Keywords = new[] { "wifi", "wi-fi", "wireless", "rete", "network", "internet", "connessione", "wlan", "ethernet" }
            },
            new SettingsEntry
            {
                Id = "win_bluetooth",
                TitleIt = "Bluetooth e dispositivi (Impostazioni di sistema)",
                TitleEn = "Bluetooth & Devices (System Settings)",
                DescriptionIt = "Associa dispositivi wireless, mouse, tastiere e auricolari",
                DescriptionEn = "Pair wireless devices, mice, keyboards and headsets",
                Uri = "ms-settings:bluetooth",
                Keywords = new[] { "bluetooth", "bt", "dispositivi", "devices", "auricolari", "airpods", "controller", "wireless" }
            },
            new SettingsEntry
            {
                Id = "win_updates",
                TitleIt = "Aggiornamenti di Windows (Windows Update)",
                TitleEn = "Windows Update (System Updates)",
                DescriptionIt = "Verifica la disponibilità di patch e aggiornamenti per Windows",
                DescriptionEn = "Check for Windows patches, security and feature updates",
                Uri = "ms-settings:windowsupdate",
                Keywords = new[] { "aggiornamenti", "aggiornamento", "update", "updates", "windows update", "patch", "sicurezza", "download update" }
            },
            new SettingsEntry
            {
                Id = "win_apps",
                TitleIt = "App installate (Impostazioni di sistema)",
                TitleEn = "Installed Apps (System Settings)",
                DescriptionIt = "Visualizza programmi installati, opzioni di disinstallazione e app predefinite",
                DescriptionEn = "View installed programs, uninstall apps and set default applications",
                Uri = "ms-settings:appsfeatures",
                Keywords = new[] { "app", "applicazioni", "programmi", "programs", "disinstalla", "uninstall", "disinstallare", "applicazioni installate" }
            },
            new SettingsEntry
            {
                Id = "win_printers",
                TitleIt = "Stampanti e scanner (Impostazioni di sistema)",
                TitleEn = "Printers & Scanners (System Settings)",
                DescriptionIt = "Aggiungi o rimuovi stampanti, code di stampa e scanner",
                DescriptionEn = "Add or remove printers, print queues and scanners",
                Uri = "ms-settings:printers",
                Keywords = new[] { "stampante", "stampanti", "printer", "printers", "scanner", "stampa", "print" }
            },
            new SettingsEntry
            {
                Id = "win_power",
                TitleIt = "Alimentazione e batteria (Impostazioni di sistema)",
                TitleEn = "Power & Battery (System Settings)",
                DescriptionIt = "Modalità sospensione, spegnimento schermo e risparmio energetico",
                DescriptionEn = "Sleep mode, screen timeout and battery saver options",
                Uri = "ms-settings:powersleep",
                Keywords = new[] { "batteria", "battery", "alimentazione", "power", "sospensione", "sleep", "standby", "risparmio energetico" }
            },
            new SettingsEntry
            {
                Id = "win_datetime",
                TitleIt = "Data e ora (Impostazioni di sistema)",
                TitleEn = "Date & Time (System Settings)",
                DescriptionIt = "Orologio, fuso orario, sincronizzazione dell'ora e calendario",
                DescriptionEn = "Clock, time zone, time synchronization and calendar",
                Uri = "ms-settings:dateandtime",
                Keywords = new[] { "data", "date", "ora", "time", "orologio", "clock", "fuso orario", "timezone", "calendario" }
            },
            new SettingsEntry
            {
                Id = "win_mouse",
                TitleIt = "Mouse e touchpad (Impostazioni di sistema)",
                TitleEn = "Mouse & Touchpad (System Settings)",
                DescriptionIt = "Velocità puntatore, scorrimento rotella e gesture touchpad",
                DescriptionEn = "Pointer speed, scroll wheel and touchpad gestures",
                Uri = "ms-settings:mousetouchpad",
                Keywords = new[] { "mouse", "puntatore", "pointer", "cursore", "cursor", "touchpad", "trackpad", "rotella", "scroll" }
            },
            new SettingsEntry
            {
                Id = "win_privacy",
                TitleIt = "Privacy e sicurezza (Impostazioni di sistema)",
                TitleEn = "Privacy & Security (System Settings)",
                DescriptionIt = "Autorizzazioni per fotocamera, microfono, posizione e telemetria",
                DescriptionEn = "Permissions for camera, microphone, location and telemetry",
                Uri = "ms-settings:privacy",
                Keywords = new[] { "privacy", "sicurezza", "security", "permessi", "permissions", "fotocamera", "posizione", "location" }
            },
            new SettingsEntry
            {
                Id = "win_notifications",
                TitleIt = "Notifiche e focus (Impostazioni di sistema)",
                TitleEn = "Notifications & Focus (System Settings)",
                DescriptionIt = "Banner di notifica, suoni di avviso e modalità non disturbare",
                DescriptionEn = "Notification banners, alert sounds and do not disturb focus",
                Uri = "ms-settings:notifications",
                Keywords = new[] { "notifiche", "notifications", "avvisi", "alerts", "non disturbare", "focus", "dnd" }
            },
            new SettingsEntry
            {
                Id = "win_personalization",
                TitleIt = "Sfondo e personalizzazione (Impostazioni di sistema)",
                TitleEn = "Background & Personalization (System Settings)",
                DescriptionIt = "Sfondo del desktop, colori dei temi, tema scuro/chiaro e caratteri",
                DescriptionEn = "Desktop background, theme colors, dark/light mode and fonts",
                Uri = "ms-settings:personalization-background",
                Keywords = new[] { "sfondo", "background", "wallpaper", "tema", "theme", "colori", "personalizzazione", "dark mode" }
            },
            new SettingsEntry
            {
                Id = "win_storage",
                TitleIt = "Spazio di archiviazione (Impostazioni di sistema)",
                TitleEn = "Storage (System Settings)",
                DescriptionIt = "Analisi spazio su disco, file temporanei e pulizia memoria",
                DescriptionEn = "Disk space breakdown, temporary files and storage cleanup",
                Uri = "ms-settings:storagesense",
                Keywords = new[] { "spazio", "storage", "disco", "hard disk", "ssd", "memoria", "archiviazione", "pulizia disco" }
            },
            new SettingsEntry
            {
                Id = "win_accessibility",
                TitleIt = "Accessibilità (Impostazioni di sistema)",
                TitleEn = "Accessibility (System Settings)",
                DescriptionIt = "Dimensioni testo, contrasto elevato, lente di ingrandimento e sottotitoli",
                DescriptionEn = "Text size, high contrast, magnifier and caption options",
                Uri = "ms-settings:easeofaccess-display",
                Keywords = new[] { "accessibilita", "accessibility", "contrasto", "ingranditore", "magnifier", "testo grande", "ipoacusici", "ipovedenti" }
            },
            new SettingsEntry
            {
                Id = "win_account",
                TitleIt = "Account utente (Impostazioni di sistema)",
                TitleEn = "User Accounts (System Settings)",
                DescriptionIt = "Profilo, password, opzioni di accesso Windows Hello e credenziali",
                DescriptionEn = "Profile, password, Windows Hello sign-in options and credentials",
                Uri = "ms-settings:yourinfo",
                Keywords = new[] { "account", "utente", "profilo", "profile", "password", "pin", "windows hello", "login" }
            },
            new SettingsEntry
            {
                Id = "win_gaming",
                TitleIt = "Giochi e Xbox Game Bar (Impostazioni di sistema)",
                TitleEn = "Gaming & Xbox Game Bar (System Settings)",
                DescriptionIt = "Modalità gioco, registrazione clip e impostazioni Xbox",
                DescriptionEn = "Game mode, clip recording and Xbox overlay settings",
                Uri = "ms-settings:gaming-gamebar",
                Keywords = new[] { "giochi", "game", "gaming", "xbox", "game bar", "registrazione giochi" }
            },

            // --- AeroSpot Settings ---
            new SettingsEntry
            {
                Id = "aerospot_settings",
                TitleIt = "Impostazioni AeroSpot (Generali)",
                TitleEn = "AeroSpot Settings (General)",
                DescriptionIt = "Finestra di configurazione generale, lingua e avvio di AeroSpot",
                DescriptionEn = "General AeroSpot configuration, language and startup options",
                Uri = "aerospot:settings:general",
                Keywords = new[] { "aerospot", "impostazioni", "settings", "preferenze", "opzioni", "configurazione", "generale", "generali" },
                IsAeroSpot = true
            },
            new SettingsEntry
            {
                Id = "aerospot_quicklook",
                TitleIt = "Impostazioni QuickLook (Anteprime)",
                TitleEn = "QuickLook Settings (Previews)",
                DescriptionIt = "Configura le anteprime di file, dimensioni memorizzate e scorciatoie",
                DescriptionEn = "Configure file previews, remembered window sizes and shortcuts",
                Uri = "aerospot:settings:quicklook",
                Keywords = new[] { "quicklook", "anteprima", "anteprime", "preview", "previews", "spazio", "rotazione", "zoom", "dimensioni finestre", "primo piano", "sempre in primo piano", "always on top", "topmost" },
                IsAeroSpot = true
            },
            new SettingsEntry
            {
                Id = "aerospot_spot",
                TitleIt = "Impostazioni Spot (Ricerca e Indicizzazione)",
                TitleEn = "Spotlight & Search Settings",
                DescriptionIt = "Dischi inclusi, esclusioni, frequenza indice e gerarchia categorie",
                DescriptionEn = "Indexed drives, exclusions, update frequency and result hierarchy",
                Uri = "aerospot:settings:spot",
                Keywords = new[] { "spot", "spotlight", "ricerca", "search", "indicizzazione", "dischi", "esclusioni", "gerarchia", "categorie" },
                IsAeroSpot = true
            },
            new SettingsEntry
            {
                Id = "aerospot_shortcuts",
                TitleIt = "Scorciatoie da tastiera AeroSpot",
                TitleEn = "AeroSpot Keyboard Shortcuts",
                DescriptionIt = "Configura la scorciatoia di ricerca (Spotlight) e anteprima (QuickLook)",
                DescriptionEn = "Configure quick search (Spotlight) and preview (QuickLook) hotkeys",
                Uri = "aerospot:settings:shortcuts",
                Keywords = new[] { "scorciatoie", "scorciatoia", "shortcuts", "shortcut", "hotkey", "hotkeys", "tasti scelta rapida", "combinazione tasti" },
                IsAeroSpot = true
            },
            new SettingsEntry
            {
                Id = "aerospot_indexing",
                TitleIt = "Indicizzazione e cartelle escluse AeroSpot",
                TitleEn = "AeroSpot Indexing & Excluded Folders",
                DescriptionIt = "Scegli i dischi inclusi e gestisci i percorsi esclusi dalla ricerca",
                DescriptionEn = "Choose indexed drives and manage folders excluded from search",
                Uri = "aerospot:settings:indexing",
                Keywords = new[] { "indicizzazione", "indice", "indexing", "index", "esclusioni", "escludi", "dischi", "cartelle escluse" },
                IsAeroSpot = true
            },
            new SettingsEntry
            {
                Id = "aerospot_updates",
                TitleIt = "Aggiornamenti AeroSpot",
                TitleEn = "AeroSpot Updates",
                DescriptionIt = "Verifica e installa nuove versioni di AeroSpot da GitHub",
                DescriptionEn = "Check and install new AeroSpot releases from GitHub",
                Uri = "aerospot:settings:updates",
                Keywords = new[] { "aggiorna aerospot", "update aerospot", "aggiornamenti aerospot", "nuova versione aerospot" },
                IsAeroSpot = true
            },
            new SettingsEntry
            {
                Id = "aerospot_instructions",
                TitleIt = "Istruzioni e guida utente AeroSpot",
                TitleEn = "AeroSpot User Guide & Instructions",
                DescriptionIt = "Consulta il manuale offline di AeroSpot con tutte le funzionalità",
                DescriptionEn = "View offline user guide with full feature list and shortcuts",
                Uri = "aerospot:settings:instructions",
                Keywords = new[] { "guida", "istruzioni", "instructions", "manuale", "help", "aiuto", "documentazione" },
                IsAeroSpot = true
            }
        };

        public static List<SettingsEntry> Search(string query, string currentLang, int maxResults = 5)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<SettingsEntry>();

            string cleanQuery = query.Trim();
            string[] queryTerms = cleanQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (queryTerms.Length == 0) return new List<SettingsEntry>();

            var scored = new List<(SettingsEntry Entry, int Score)>();
            string otherLang = string.Equals(currentLang, "it", StringComparison.OrdinalIgnoreCase) ? "en" : "it";

            foreach (var entry in _catalog)
            {
                string title = entry.GetTitle(currentLang);
                string otherTitle = entry.GetTitle(otherLang);
                string desc = entry.GetDescription(currentLang);

                // Verifica che tutti i termini corrispondano da qualche parte
                bool allTermsMatch = queryTerms.All(term =>
                    title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    otherTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    entry.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase))
                );

                if (!allTermsMatch) continue;

                int score = 0;

                // Match esatto del titolo
                if (title.StartsWith(cleanQuery, StringComparison.OrdinalIgnoreCase))
                {
                    score += 5000;
                }
                else if (otherTitle.StartsWith(cleanQuery, StringComparison.OrdinalIgnoreCase))
                {
                    score += 4500;
                }

                // Match esatto o parziale sulle keyword
                foreach (var kw in entry.Keywords)
                {
                    if (string.Equals(kw, cleanQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 6000;
                        break;
                    }
                    if (kw.StartsWith(cleanQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 3500;
                    }
                    else if (kw.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 1500;
                    }
                }

                // Bonus generale di presenza
                score += 1000;

                scored.Add((entry, score));
            }

            return scored
                .OrderByDescending(x => x.Score)
                .Take(maxResults)
                .Select(x => x.Entry)
                .ToList();
        }
    }
}
