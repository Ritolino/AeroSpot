using System;
using System.Collections.Generic;
using System.Globalization;

namespace AeroSpot.Core.Localization
{
    /// <summary>
    /// Ultra-lightweight localization service for AeroSpot Core, Host (Win32) and Worker (WPF).
    /// Does not depend on WPF, WinForms, or heavy satellite assemblies.
    /// </summary>
    public static class LocalizationService
    {
        private static readonly object _syncLock = new object();
        private static string _currentLanguage = "en";

        public static event Action? LanguageChanged;

        public static string CurrentLanguage => _currentLanguage;

        public static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Tray Menu
            { "Tray_OpenAeroSpot", "Open AeroSpot ({0})" },
            { "Tray_Settings", "Settings..." },
            { "Tray_Exit", "Exit AeroSpot" },
            { "Tray_WorkerNotReady", "Unable to open: Worker process is not ready." },
            { "Tray_HotkeyActive", "AeroSpot - Shortcut Active" },
            { "Tray_EmergencyHotkey", "AeroSpot - Emergency Shortcut" },
            { "Tray_Tooltip", "AeroSpot (Resident Host)" },

            // Spotlight / Launcher
            { "Spotlight_SearchPlaceholder", "Search apps, files and folders..." },
            { "Spotlight_IndexStatus_Ready", "Index ready: {0:N0} items" },
            { "Spotlight_IndexStatus_Updating", "Updating index..." },
            { "Spotlight_IndexStatus_Updated", "Index updated: {0:N0} items" },
            { "Spotlight_Btn_Settings", "Settings" },
            { "Spotlight_Btn_Reindex", "Update Index" },
            { "Spotlight_Menu_OpenFilePath", "Open file location" },
            { "Spotlight_Category_App", "Application" },
            { "Spotlight_Category_Folder", "Folder" },
            { "Spotlight_Category_Document", "Document" },
            { "Spotlight_Category_Image", "Image" },
            { "Spotlight_Category_Video", "Video" },
            { "Spotlight_Category_Audio", "Audio" },
            { "Spotlight_Category_3D", "3D Model" },
            { "Spotlight_Category_File", "File" },
            { "Spotlight_Hint_Open", "↵ Open" },
            { "Spotlight_Hint_Navigate", "↑↓ Navigate" },
            { "Spotlight_Hint_Dismiss", "Esc Dismiss" },

            // QuickLook / Preview
            { "Preview_Hint_Open", "↵ Open" },
            { "Preview_Hint_Dismiss", "Space / Esc Dismiss" },
            { "Preview_Btn_OpenFolder", "Open folder (↵)" },
            { "Preview_Btn_OpenDefaultAction", "Open in default app (↵)" },
            { "Preview_Btn_OpenWithDefault", "Open with default application" },
            { "Preview_Btn_Close", "Close" },
            { "Preview_Btn_Fullscreen", "Full screen" },
            { "Preview_Btn_Play", "Play" },
            { "Preview_Btn_Pause", "Pause" },
            { "Preview_Btn_Mute", "Mute" },
            { "Preview_Btn_Unmute", "Unmute" },
            { "Preview_ResetZoom_Tooltip", "Reset zoom (100%)" },
            { "Preview_Btn_RememberSize_Tooltip", "Remember window size for this file type (persistent across restarts)" },
            { "Preview_Folder_Contains", "{0:N0} items" },
            { "Preview_Folder_Calculating", "Calculating size..." },
            { "Preview_Folder_Subtitle", "File folder" },
            { "Preview_FileNotFound", "The selected file does not exist or has been removed." },
            { "Preview_NoViewer_Title", "No preview handler registered for {0} files" },
            { "Preview_NoViewer_Desc", "You can open this file using its default application." },
            { "Preview_Size_Label", "Size" },
            { "Preview_Modified_Label", "Modified" },
            { "Preview_Path_Label", "Path" },
            { "Preview_Pdf_Page", "Page {0} of {1}" },
            { "Preview_Pdf_Document", "PDF Document" },
            { "Preview_Audio_Track", "Audio Track" },
            { "Preview_Stl_Triangles", "{0:N0} triangles" },
            { "Preview_PartialContent", "Preview limited to the first {0} KB" },

            // Settings
            { "Settings_Title", "AeroSpot Settings" },
            { "Settings_Subtitle", "Customize shortcuts, language, drives, exclusions and launcher performance" },
            { "Settings_System_Title", "System & Performance" },
            { "Settings_System_Desc", "Configure Windows autostart and background memory management. Keeping the worker warm ensures instant searches and previews (70-90 MB); disabling it hibernates the worker down to ~10 MB after the specified idle timeout." },
            { "Settings_System_Info_Tooltip", "Information on startup and memory management (Click to show/hide)" },
            { "Settings_Language_Title", "Language" },
            { "Settings_Language_Desc", "Choose application interface language:" },
            { "Settings_Language_Auto", "Automatic (Windows language)" },
            { "Settings_Language_En", "English" },
            { "Settings_Language_It", "Italiano" },
            { "Settings_Shortcuts_Title", "Keyboard Shortcuts" },
            { "Settings_Spotlight_Shortcut", "Quick Search (Spotlight):" },
            { "Settings_Spotlight_Shortcut_Hint", "Click to record shortcut (any key except Esc)" },
            { "Settings_QuickLook_Shortcut", "File Preview (QuickLook):" },
            { "Settings_QuickLook_Shortcut_Hint", "Click to record shortcut for Explorer and Desktop" },
            { "Settings_Shortcuts_Info", "ℹ Click on the box and press your desired shortcut combination. Press Alt + Space or Esc to dismiss search at any time." },
            { "Settings_Indexing_Info_Title", "ℹ What does file indexing involve?" },
            { "Settings_Indexing_Info_Ram", "• RAM usage: Only ~2-4 MB used by local SQLite cache (zero wasted memory)." },
            { "Settings_Indexing_Info_Disk", "• Disk space: ~10 - 50 MB total for local SQLite database." },
            { "Settings_Indexing_Info_Cpu", "• CPU impact: Initial background scan runs at lowest I/O priority without slowing system or games." },
            { "Settings_Indexing_Info_Speed", "• Search speed: Instant results in 2 - 4 milliseconds with smart application ranking." },
            { "Settings_Drives_Title", "Drives Included in Indexing" },
            { "Settings_Drives_Desc", "Select the drives to index. System drive and primary local data drives are enabled by default:" },
            { "Settings_Drives_Item_Format", "Drive {0}: ({1}) • {2}" },
            { "Settings_Drives_Default_Label", "Local Disk" },
            { "Settings_Exclusions_Title", "Excluded Folders and Paths" },
            { "Settings_Exclusions_Desc", "Avoid indexing large game libraries, temporary files and build folders:" },
            { "Settings_Exclusions_Placeholder", "Paste or type folder path to exclude..." },
            { "Settings_Exclusions_Btn_Add", "Add" },
            { "Settings_Exclusions_Btn_Delete_Tooltip", "Remove custom exclusion" },
            { "Settings_Shortcuts_Info_Tooltip", "How to customize shortcuts (Click to show/hide)" },
            { "Settings_Indexing_Info_Tooltip", "What file indexing involves (Click to show/hide)" },
            { "Settings_Performance_Info_Tooltip", "Engine memory and background performance information (Click to show/hide)" },
            { "Settings_Startup_Info_Tooltip", "Windows startup details (Click to show/hide)" },
            { "Settings_RememberSize_Title", "QuickLook Window Sizing" },
            { "Settings_RememberSize_Desc", "By default, QuickLook opens each file type with its ideal adaptive size and resets on close. Enable to remember custom sizes across PC reboots:" },
            { "Settings_RememberSize_Images", "Remember custom size for Images" },
            { "Settings_RememberSize_Videos", "Remember custom size for Videos" },
            { "Settings_RememberSize_Audio", "Remember custom size for Audio" },
            { "Settings_RememberSize_Pdf", "Remember custom size for PDF Documents" },
            { "Settings_RememberSize_Documents", "Remember custom size for Text & Office Documents" },
            { "Settings_RememberSize_Generic", "Remember custom size for Other Files & Apps" },
            { "Settings_Performance_Title", "Performance and Memory (RAM) Optimization" },
            { "Settings_Performance_KeepWarm", "Keep UI engine always active in RAM (recommended for instant response)" },
            { "Settings_Performance_KeepWarm_Desc", "Recommended: keeps the engine resident in memory for instant 0 ms response. During inactivity it takes ~70-90 MB and automatically hibernates down to ~8-15 MB. Disable if you want to completely unload the worker after the idle timeout to save memory." },
            { "Settings_Performance_Timeout", "Seconds of inactivity before engine automatic shutdown:" },
            { "Settings_IndexingSync_Title", "File Indexing & Synchronization" },
            { "Settings_IndexingSync_Desc", "AeroSpot indexes configured drives in the background. To immediately sync new files or folders, use the 🔄 Update Index button in the Spotlight search bar (bottom left)." },
            { "Settings_DataDir_Title", "Data Directory and Storage" },
            { "Settings_DataDir_Desc", "Location where settings, search index database and system logs are stored:" },
            { "Settings_DataDir_Btn_Open", "Open data folder" },
            { "Settings_DataDir_Mode_Portable", "Mode: Portable (Data\\ subfolder next to application)" },
            { "Settings_DataDir_Mode_Standard", "Mode: Standard (%LocalAppData%\\AeroSpot)" },
            { "Settings_Startup_Title", "Windows Startup" },
            { "Settings_Startup_Enable", "Start AeroSpot automatically with Windows" },
            { "Settings_Startup_Desc", "Launches the lightweight background Host silently on system boot without administrative rights." },
            { "Settings_Btn_Reset", "↺" },
            { "Settings_Btn_Reset_Tooltip", "Reset settings" },
            { "Settings_Btn_Cancel", "Cancel" },
            { "Settings_Btn_Save", "Save Settings" },
            { "Settings_Status_Saved", "Settings saved successfully!" },
            { "Settings_Reset_Confirm", "Do you want to reset all settings to defaults?" },
            { "Settings_Reset_Title", "Reset AeroSpot Settings" },

            // Instructions (M3)
            { "Instructions_Tab_Title", "Instructions" },
            { "Instructions_Title", "User Instructions & Documentation" },
            { "Instructions_Desc", "Consult the offline user guide to learn all features and shortcuts." },
            { "Instructions_Btn_Open", "Open Instructions" },
            { "Instructions_Version", "Version {0} ({1})" },
            { "Instructions_GitHub_Link", "View project on GitHub" }
        };

        public static readonly Dictionary<string, string> Italian = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Tray Menu
            { "Tray_OpenAeroSpot", "Apri AeroSpot ({0})" },
            { "Tray_Settings", "Impostazioni..." },
            { "Tray_Exit", "Esci da AeroSpot" },
            { "Tray_WorkerNotReady", "Impossibile aprire: il processo Worker non è pronto." },
            { "Tray_HotkeyActive", "AeroSpot - Scorciatoia Attiva" },
            { "Tray_EmergencyHotkey", "AeroSpot - Scorciatoia di Emergenza" },
            { "Tray_Tooltip", "AeroSpot (Host Residente)" },

            // Spotlight / Launcher
            { "Spotlight_SearchPlaceholder", "Cerca app, file e cartelle..." },
            { "Spotlight_IndexStatus_Ready", "Indice pronto: {0:N0} elementi" },
            { "Spotlight_IndexStatus_Updating", "Aggiornamento indice in corso..." },
            { "Spotlight_IndexStatus_Updated", "Indice aggiornato: {0:N0} elementi" },
            { "Spotlight_Btn_Settings", "Impostazioni" },
            { "Spotlight_Btn_Reindex", "Aggiorna Indice" },
            { "Spotlight_Menu_OpenFilePath", "Vai al percorso file" },
            { "Spotlight_Category_App", "Applicazione" },
            { "Spotlight_Category_Folder", "Cartella" },
            { "Spotlight_Category_Document", "Documento" },
            { "Spotlight_Category_Image", "Immagine" },
            { "Spotlight_Category_Video", "Video" },
            { "Spotlight_Category_Audio", "Audio" },
            { "Spotlight_Category_3D", "Modello 3D" },
            { "Spotlight_Category_File", "File" },
            { "Spotlight_Hint_Open", "↵ Apri" },
            { "Spotlight_Hint_Navigate", "↑↓ Naviga" },
            { "Spotlight_Hint_Dismiss", "Esc Chiudi" },

            // QuickLook / Preview
            { "Preview_Hint_Open", "↵ Apri" },
            { "Preview_Hint_Dismiss", "Spazio / Esc Chiudi" },
            { "Preview_Btn_OpenFolder", "Apri cartella (↵)" },
            { "Preview_Btn_OpenDefaultAction", "Apri nel programma predefinito (↵)" },
            { "Preview_Btn_OpenWithDefault", "Apri con applicazione predefinita" },
            { "Preview_Btn_Close", "Chiudi" },
            { "Preview_Btn_Fullscreen", "Schermo intero" },
            { "Preview_Btn_Play", "Riproduci" },
            { "Preview_Btn_Pause", "Pausa" },
            { "Preview_Btn_Mute", "Disattiva audio" },
            { "Preview_Btn_Unmute", "Attiva audio" },
            { "Preview_ResetZoom_Tooltip", "Reimposta zoom (100%)" },
            { "Preview_Btn_RememberSize_Tooltip", "Memorizza dimensione per questo tipo di file (persistente anche dopo il riavvio)" },
            { "Preview_Folder_Contains", "{0:N0} elementi" },
            { "Preview_Folder_Calculating", "Calcolo dimensioni in corso..." },
            { "Preview_Folder_Subtitle", "Cartella di file" },
            { "Preview_FileNotFound", "Il file selezionato non esiste o è stato rimosso." },
            { "Preview_NoViewer_Title", "Nessun visualizzatore di anteprima registrato per i file {0}" },
            { "Preview_NoViewer_Desc", "Puoi aprire questo file utilizzando la relativa applicazione predefinita." },
            { "Preview_Size_Label", "Dimensione" },
            { "Preview_Modified_Label", "Ultima modifica" },
            { "Preview_Path_Label", "Percorso" },
            { "Preview_Pdf_Page", "Pagina {0} di {1}" },
            { "Preview_Pdf_Document", "Documento PDF" },
            { "Preview_Audio_Track", "Traccia Audio" },
            { "Preview_Stl_Triangles", "{0:N0} triangoli" },
            { "Preview_PartialContent", "Anteprima limitata ai primi {0} KB" },

            // Settings
            { "Settings_Title", "Impostazioni AeroSpot" },
            { "Settings_Subtitle", "Personalizza scorciatoie, lingua, dischi, esclusioni e prestazioni del launcher" },
            { "Settings_System_Title", "Sistema e Prestazioni" },
            { "Settings_System_Desc", "Configura l'avvio automatico con Windows e l'ottimizzazione del processo in memoria RAM (modalità a caldo vs ibernazione dopo inattività)." },
            { "Settings_System_Info_Tooltip", "Informazioni su sistema e prestazioni (Clicca per mostrare/nascondere)" },
            { "Settings_Language_Title", "Lingua dell'interfaccia" },
            { "Settings_Language_Desc", "Scegli la lingua per i menu e le finestre dell'applicazione:" },
            { "Settings_Language_Auto", "Automatico (lingua di Windows)" },
            { "Settings_Language_En", "English" },
            { "Settings_Language_It", "Italiano" },
            { "Settings_Shortcuts_Title", "Scelte rapide da tastiera" },
            { "Settings_Spotlight_Shortcut", "Ricerca rapida (Spotlight):" },
            { "Settings_Spotlight_Shortcut_Hint", "Clicca per registrare la scorciatoia (qualsiasi tasto tranne Esc)" },
            { "Settings_QuickLook_Shortcut", "Anteprima file (QuickLook):" },
            { "Settings_QuickLook_Shortcut_Hint", "Clicca per registrare la scorciatoia per Explorer e Desktop" },
            { "Settings_Shortcuts_Info", "ℹ Clicca sul riquadro blu e premi direttamente la combinazione di tasti desiderata. Premi Alt + Spazio o Esc per chiudere la barra di ricerca in qualsiasi momento." },
            { "Settings_Indexing_Info_Title", "ℹ Cosa comporta l'indicizzazione dei file?" },
            { "Settings_Indexing_Info_Ram", "• Consumo RAM: Solo ~2-4 MB utilizzati dalla cache SQLite locale (nessun spreco di memoria)." },
            { "Settings_Indexing_Info_Disk", "• Spazio su disco: Circa 10 - 50 MB totali per il database SQLite memorizzato nella cartella locale di AeroSpot." },
            { "Settings_Indexing_Info_Cpu", "• Prestazioni CPU: La scansione iniziale avviene in background con priorità I/O minima, senza mai rallentare il sistema né i giochi." },
            { "Settings_Indexing_Info_Speed", "• Velocità di ricerca: Risultati istantanei in 2 - 4 millisecondi con ordinamento intelligente dei programmi." },
            { "Settings_Drives_Title", "Dischi inclusi nell'indicizzazione" },
            { "Settings_Drives_Desc", "Seleziona le unità da monitorare con le ricerche. Di base è attivo solo C: (e l'unità principale dati):" },
            { "Settings_Drives_Item_Format", "Unità {0}: ({1}) • {2}" },
            { "Settings_Drives_Default_Label", "Disco Locale" },
            { "Settings_Exclusions_Title", "Cartelle e percorsi esclusi" },
            { "Settings_Exclusions_Desc", "Evita l'indicizzazione inutile di cartelle pesanti di gioco, cache e file temporanei:" },
            { "Settings_Exclusions_Placeholder", "Incolla o digita il percorso della cartella da escludere..." },
            { "Settings_Exclusions_Btn_Add", "Aggiungi" },
            { "Settings_Exclusions_Btn_Delete_Tooltip", "Rimuovi esclusione personalizzata" },
            { "Settings_Shortcuts_Info_Tooltip", "Come personalizzare le scelte rapide (Clicca per mostrare/nascondere)" },
            { "Settings_Indexing_Info_Tooltip", "Cosa comporta l'indicizzazione dei file? (Clicca per mostrare/nascondere)" },
            { "Settings_Performance_Info_Tooltip", "Informazioni su memoria e prestazioni del motore (Clicca per mostrare/nascondere)" },
            { "Settings_Startup_Info_Tooltip", "Dettagli sull'avvio automatico con Windows (Clicca per mostrare/nascondere)" },
            { "Settings_RememberSize_Title", "Dimensioni finestre QuickLook" },
            { "Settings_RememberSize_Desc", "Di default ogni tipo di file si apre con la sua dimensione ottimale e si ripristina alla chiusura. Attiva per memorizzare le dimensioni personalizzate anche a PC riavviato:" },
            { "Settings_RememberSize_Images", "Memorizza dimensione personalizzata per Immagini" },
            { "Settings_RememberSize_Videos", "Memorizza dimensione personalizzata per Video" },
            { "Settings_RememberSize_Audio", "Memorizza dimensione personalizzata per File Audio" },
            { "Settings_RememberSize_Pdf", "Memorizza dimensione personalizzata per Documenti PDF" },
            { "Settings_RememberSize_Documents", "Memorizza dimensione personalizzata per Testo e Documenti" },
            { "Settings_RememberSize_Generic", "Memorizza dimensione personalizzata per Altri File e App" },
            { "Settings_Performance_Title", "Prestazioni e Risparmio Memoria (RAM)" },
            { "Settings_Performance_KeepWarm", "Mantieni motore sempre attivo in RAM (consigliato per risposta immediata)" },
            { "Settings_Performance_KeepWarm_Desc", "Consigliato: il motore rimane attivo in memoria garantendo apertura a 0 ms. In inattività consuma ~70-90 MB e va automaticamente in ibernazione riducendosi a soli ~8-15 MB. Se vuoi il massimo risparmio puoi disattivarlo per chiudere completamente il worker dopo il tempo di inattività." },
            { "Settings_Performance_Timeout", "Secondi di inattività prima dello spegnimento automatico del motore:" },
            { "Settings_IndexingSync_Title", "Indicizzazione & Sincronizzazione File" },
            { "Settings_IndexingSync_Desc", "AeroSpot indicizza in background i dischi e cartelle selezionati. Per aggiornare immediatamente l'indice con nuovi file o cartelle, usa il pulsante 🔄 Aggiorna Indice presente direttamente nella barra di ricerca Spotlight (in basso a sinistra)." },
            { "Settings_DataDir_Title", "Cartella Dati e Archiviazione" },
            { "Settings_DataDir_Desc", "Percorso in cui sono memorizzati le impostazioni, il database dell'indice e i log di sistema:" },
            { "Settings_DataDir_Btn_Open", "Apri cartella dati" },
            { "Settings_DataDir_Mode_Portable", "Modalità: Portable (sottocartella Data\\ accanto all'applicazione)" },
            { "Settings_DataDir_Mode_Standard", "Modalità: Standard (%LocalAppData%\\AeroSpot)" },
            { "Settings_Startup_Title", "Avvio automatico con Windows" },
            { "Settings_Startup_Enable", "Avvia AeroSpot automaticamente all'avvio di Windows" },
            { "Settings_Startup_Desc", "Avvia l'Host leggero in background senza richiedere privilegi di amministratore." },
            { "Settings_Btn_Reset", "↺" },
            { "Settings_Btn_Reset_Tooltip", "Ripristino impostazioni" },
            { "Settings_Btn_Cancel", "Annulla" },
            { "Settings_Btn_Save", "Salva Impostazioni" },
            { "Settings_Status_Saved", "Impostazioni salvate con successo!" },
            { "Settings_Reset_Confirm", "Vuoi davvero ripristinare tutte le impostazioni predefinite?" },
            { "Settings_Reset_Title", "Ripristina Impostazioni AeroSpot" },

            // Instructions (M3)
            { "Instructions_Tab_Title", "Istruzioni" },
            { "Instructions_Title", "Istruzioni d'uso & Documentazione" },
            { "Instructions_Desc", "Consulta la guida utente offline per scoprire tutte le funzionalità e le scorciatoie di AeroSpot." },
            { "Instructions_Btn_Open", "Apri le istruzioni" },
            { "Instructions_Version", "Versione {0} ({1})" },
            { "Instructions_GitHub_Link", "Visualizza il progetto su GitHub" }
        };

        static LocalizationService()
        {
            Initialize("auto");
        }

        public static void Initialize(string configuredLanguage)
        {
            lock (_syncLock)
            {
                // QA / Testing override via environment variable
                string? envCulture = Environment.GetEnvironmentVariable("AEROSPOT_UI_CULTURE");
                if (!string.IsNullOrWhiteSpace(envCulture))
                {
                    _currentLanguage = ResolveLanguageCode(envCulture);
                    LanguageChanged?.Invoke();
                    return;
                }

                _currentLanguage = ResolveLanguageCode(configuredLanguage);
                LanguageChanged?.Invoke();
            }
        }

        public static string ResolveLanguageCode(string preference)
        {
            if (string.Equals(preference, "it", StringComparison.OrdinalIgnoreCase))
            {
                return "it";
            }
            if (string.Equals(preference, "en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            // Automatic detection from Windows display culture
            var winCulture = CultureInfo.CurrentUICulture;
            if (winCulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase))
            {
                return "it";
            }

            return "en";
        }

        public static string Get(string key)
        {
            var dict = _currentLanguage == "it" ? Italian : English;
            if (dict.TryGetValue(key, out string? value))
            {
                return value;
            }

            // Fallback to English
            if (English.TryGetValue(key, out string? fallbackValue))
            {
                return fallbackValue;
            }

            return key;
        }

        public static string Get(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                var culture = _currentLanguage == "it" ? new CultureInfo("it-IT") : CultureInfo.InvariantCulture;
                return string.Format(culture, template, args);
            }
            catch
            {
                return template;
            }
        }
    }
}
