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
            { "Spotlight_Btn_Reindexing", "⏳ Updating..." },
            { "Spotlight_Btn_Reindexed", "✓ Index updated" },
            { "Spotlight_Clear_Search", "Clear search" },
            { "Spotlight_Filter_All", "All" },
            { "Spotlight_Filter_Apps", "Apps" },
            { "Spotlight_Filter_Photos", "Photos" },
            { "Spotlight_Filter_Documents", "Documents" },
            { "Spotlight_Filter_Videos", "Videos" },
            { "Spotlight_Filter_Audio", "Audio" },
            { "Spotlight_Filter_Folders", "Folders" },
            { "Spotlight_Menu_OpenFilePath", "Open file location" },
            { "Spotlight_Menu_CopyPath", "Copy file path" },
            { "Spotlight_Menu_Convert", "Convert to..." },
            { "Spotlight_Menu_ConvertJpg", "Convert to JPG" },
            { "Spotlight_Menu_ConvertPng", "Convert to PNG" },
            { "Spotlight_Menu_ConvertPdf", "Convert to PDF" },
            { "Spotlight_Menu_ExtractMp3", "Extract to MP3" },
            { "Spotlight_Menu_ConvertMp3", "Convert to MP3" },
            { "Spotlight_Menu_ConvertWav", "Convert to WAV" },
            { "Spotlight_Menu_ConvertMp4", "Convert to MP4" },
            { "Spotlight_Menu_RemoveBg", "Remove Background (AI)" },
            { "Spotlight_Menu_RecycleFile", "Move to Recycle Bin (Del)" },
            { "Notification_Converting", "Converting file..." },
            { "Notification_ExtractingAudio", "Extracting MP3 audio..." },
            { "Notification_DownloadingModel", "Downloading AI model ({0}%)..." },
            { "Notification_RemovingBg", "Removing background with AI..." },
            { "Notification_FileRecycled", "Item moved to Recycle Bin" },
            { "Notification_FileRecycleError", "Unable to delete item" },
            { "Notification_PathCopied", "File path copied to clipboard" },
            { "Notification_Success", "Successfully completed: {0}" },
            { "Notification_Error", "Operation failed: {0}" },
            { "Notification_OpenFolder", "Show in folder" },
            { "Spotlight_Category_App", "Application" },
            { "Spotlight_Category_Folder", "Folder" },
            { "Spotlight_Category_Document", "Document" },
            { "Spotlight_Category_Image", "Image" },
            { "Spotlight_Category_Video", "Video" },
            { "Spotlight_Category_Audio", "Audio" },
            { "Spotlight_Category_3D", "3D Model" },
            { "Spotlight_Category_File", "File" },
            { "Spotlight_Category_Recent", "Recent" },
            { "Spotlight_Category_Search", "Search" },
            { "Spotlight_Category_Settings", "Settings" },
            { "Spotlight_Filter_Settings", "Settings" },
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
            { "Preview_Btn_Rotate_Tooltip", "Rotate 90° clockwise (view only)" },
            { "Preview_Btn_Play", "Play" },
            { "Preview_Btn_Pause", "Pause" },
            { "Preview_Btn_Mute", "Mute" },
            { "Preview_Btn_Unmute", "Unmute" },
            { "Preview_ResetZoom_Tooltip", "Reset zoom (100%)" },
            { "Preview_Btn_RememberSize_Tooltip", "Remember window size for this file type (persistent across restarts)" },
            { "Preview_Btn_ExportPdf_Tooltip", "Export document to PDF" },
            { "Preview_Folder_Contains", "{0:N0} items" },
            { "Preview_Folder_Calculating", "Calculating size..." },
            { "Preview_Folder_Subtitle", "File folder" },
            { "Preview_FileNotFound", "The selected file does not exist or has been removed." },
            { "Preview_NoViewer_Title", "No preview handler registered for {0} files" },
            { "Preview_NoViewer_Desc", "You can open this file using its default application." },
            { "Preview_Size_Label", "Size" },
            { "Preview_Modified_Label", "Modified" },
            { "Preview_Path_Label", "Path" },
            { "Preview_Audio_Track", "Audio Track" },
            { "Preview_Audio_CompatiblePreparing", "Preparing compatible audio..." },
            { "Preview_Audio_CompatibleReady", "Compatible audio active (Stereo AAC)" },
            { "Preview_Audio_MutedBitstream", "Audio AC-3/DTS: muted to prevent digital noise bursts" },
            { "Preview_Audio_ConvertingBadge", "Audio (Preparing...)" },
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
            { "Settings_Delete_Shortcut", "Move to Recycle Bin (Spotlight):" },
            { "Settings_Delete_Shortcut_Hint", "Keyboard shortcut to recycle the selected item in search results" },
            { "Settings_Delete_Shortcut_Key", "Del (Canc)" },
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
            { "Settings_DataDir_Btn_ReindexNow", "Update index now" },
            { "Settings_DataDir_Mode_Portable", "Mode: Portable (Data\\ subfolder next to application)" },
            { "Settings_DataDir_Mode_Standard", "Mode: Standard (%LocalAppData%\\AeroSpot)" },
            { "Settings_IndexFrequency_Label", "Automatic index update frequency:" },
            { "Settings_IndexFreq_Startup", "At every startup" },
            { "Settings_IndexFreq_1Hour", "Every hour (default)" },
            { "Settings_IndexFreq_2Hours", "Every 2 hours" },
            { "Settings_IndexFreq_30Min", "Every 30 minutes" },
            { "Settings_IndexFreq_15Min", "Every 15 minutes" },
            { "Settings_IndexFreq_Manual", "Manual only" },
            { "Settings_Startup_Title", "Windows Startup" },
            { "Settings_Startup_Enable", "Start AeroSpot automatically with Windows" },
            { "Settings_Startup_Desc", "Launches the lightweight background Host silently on system boot without administrative rights." },
            { "Settings_Conversion_OpenFolder", "Open containing folder automatically after file conversion" },
            { "Settings_Shell_ContextMenu", "Integrate AeroSpot quick actions into Windows File Explorer context menu" },
            { "Settings_Tab_General", "General" },
            { "Settings_Tab_QuickLook", "QuickLook" },
            { "Settings_Tab_Spot", "Spot" },
            { "Settings_Tab_Hierarchy", "Spotlight Hierarchy" },
            { "Settings_QuickLook_Intro_Title", "QuickLook Previews" },
            { "Settings_QuickLook_Intro_Desc", "Instant spacebar preview without opening default apps. Supports continuous scrolling, zoom, and lossless rotation." },
            { "Settings_QuickLook_Hints_Title", "Keyboard & Mouse Controls" },
            { "Settings_QuickLook_Hints_Desc", "• Space: Open or close preview\n• Arrow keys: Navigate between items\n• Ctrl + Mouse Wheel: Smooth zoom on images, PDFs, and documents\n• ⟳ Rotate Button: Rotate photos and videos by 90° clockwise\n• Escape: Close preview window" },
            { "Settings_QuickLook_WindowBehavior_Title", "Window Behavior & Focus" },
            { "Settings_QuickLook_WindowBehavior_Desc", "QuickLook opens instantly in the foreground for fast inspection and gracefully restores focus to the underlying window when closed." },
            { "Settings_QuickLook_AlwaysOnTop_Label", "Keep preview window always on top" },
            { "Settings_QuickLook_AlwaysOnTop_Tooltip", "If enabled, the preview window stays pinned in the foreground even when clicking other windows." },
            { "Settings_Spot_Intro_Title", "Spotlight & Search" },
            { "Settings_Spot_Intro_Desc", "Configure the initial home view, indexed drives, excluded folders, and category search hierarchy." },
            { "Settings_Hierarchy_Title", "Spotlight Result Priority" },
            { "Settings_Hierarchy_Desc", "Drag categories to set which file types appear first in Spotlight search results." },
            { "Settings_Hierarchy_Reset", "Reset Default Order" },
            { "Settings_Hierarchy_MoveUp", "Move Up" },
            { "Settings_Hierarchy_MoveDown", "Move Down" },
            { "Settings_Hierarchy_SmartMixed", "Prioritize exact name matches (Smart mixed hierarchy)" },
            { "Settings_Hierarchy_SmartMixed_Desc", "Always show files or folders whose name matches the search query first, then order remaining results by category hierarchy." },
            { "Settings_Hierarchy_DropTarget", "Move here" },
            { "Preview_Menu_CopyPath", "Copy path" },
            { "Preview_Menu_OpenDefault", "Open with default application" },
            { "Preview_Menu_Close", "Close preview" },
            { "Preview_Menu_ConvertJpg", "Convert to JPG" },
            { "Preview_Menu_ConvertPng", "Convert to PNG" },
            { "Preview_Menu_ConvertPdf", "Convert to PDF" },
            { "Preview_Menu_ConvertWav", "Convert to WAV" },
            { "Preview_Menu_ConvertMp3", "Convert to MP3" },
            { "Preview_Menu_ConvertMp4", "Convert to MP4" },
            { "Spotlight_Hint_CopyPath", "Copy path" },
            { "Settings_Btn_Reset", "↺" },
            { "Settings_Btn_Reset_Tooltip", "Reset settings" },
            { "Settings_Btn_Cancel", "Cancel" },
            { "Settings_Btn_Save", "Save Settings" },
            { "Settings_Status_Saved", "Settings saved successfully!" },
            { "Settings_Reset_Confirm", "Do you want to reset all settings to defaults?" },
            { "Settings_Reset_Title", "Reset AeroSpot Settings" },
            { "Settings_RecentHistory_Title", "Recent Items & History" },
            { "Settings_RecentHistory_Desc", "AeroSpot can save the history of items opened through the launcher to let you quickly access them from the home view." },
            { "Settings_RecentHistory_Enable", "Save history of items opened via AeroSpot" },
            { "Settings_RecentHistory_Info_Tooltip", "Information about home view and history" },
            { "Settings_RecentHistory_Info_Desc", "Choose what to display in the search bar when Spotlight is empty:\n• Most frequent searches: displays your most frequent search queries; pressing Enter or clicking one executes the search without opening anything.\n• Most frequently opened: lists files and applications you launch most frequently with AeroSpot.\n• Recently opened items: shows recently opened items in chronological order.\n• Alphabetical order: lists installed applications alphabetically (classic behavior).\n• None: shows nothing when Spotlight is empty and pauses history recording.\n\nPrivacy notice: all searches and history are stored strictly locally on your PC and can be cleared at any time with 'Clear History'." },
            { "Settings_HomeDisplayMode_Label", "Home view content" },
            { "Settings_HomeMode_FrequentSearches", "Most frequent searches (default)" },
            { "Settings_HomeMode_FrequentItems", "Most frequently opened items" },
            { "Settings_HomeMode_RecentOpened", "Recently opened items" },
            { "Settings_HomeMode_Alphabetical", "Alphabetical order" },
            { "Settings_HomeMode_None", "None (blank screen)" },
            { "Settings_RecentHistory_ClearBtn", "Clear History" },
            { "Settings_RecentHistory_Cleared", "✓ History cleared" },

            // Instructions (M3)
            { "Instructions_Tab_Title", "Instructions" },
            { "Instructions_Title", "User Instructions & Documentation" },
            { "Instructions_Desc", "Consult the offline user guide to learn all features and shortcuts." },
            { "Instructions_Btn_Open", "Open Instructions" },
            { "Instructions_Version", "Version {0} ({1})" },
            { "Instructions_GitHub_Link", "View project on GitHub" },

            // Application Updates (GitHub Release Checker)
            { "Settings_Updates_Title", "Application Updates" },
            { "Settings_Updates_Desc", "Check for new AeroSpot releases on GitHub and automatically download updates." },
            { "Settings_Updates_Freq_Label", "Automatic check frequency:" },
            { "Settings_Updates_Freq_Startup", "At every startup (default)" },
            { "Settings_Updates_Freq_Daily", "Daily" },
            { "Settings_Updates_Freq_Weekly", "Weekly" },
            { "Settings_Updates_Freq_Monthly", "Monthly" },
            { "Settings_Updates_Freq_Manual", "Never (manual only)" },
            { "Settings_Updates_Btn_Check", "Check for Updates" },
            { "Settings_Updates_Checking", "Checking GitHub for updates..." },
            { "Settings_Updates_UpToDate", "✓ AeroSpot is up to date (v{0} is the latest version)" },
            { "Settings_Updates_Available", "★ New version available: v{0}!" },
            { "Settings_Updates_Downloading", "Downloading update... {0}%" },
            { "Settings_Updates_Ready", "✓ Update downloaded and ready to install!" },
            { "Settings_Updates_Btn_Install", "🚀 Install Update & Restart" },
            { "Settings_Updates_Btn_Notes", "Release Notes" },
            { "Settings_Updates_Error", "Unable to check for updates. Check your internet connection or try again later." },
            { "Settings_Updates_LastChecked", "Last check: {0}" },
            { "Settings_Updates_NeverChecked", "Never checked" }
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
            { "Spotlight_Btn_Reindexing", "⏳ Scansione in corso..." },
            { "Spotlight_Btn_Reindexed", "✓ Indice aggiornato" },
            { "Spotlight_Filter_All", "Tutti" },
            { "Spotlight_Filter_Apps", "App" },
            { "Spotlight_Filter_Photos", "Foto" },
            { "Spotlight_Filter_Documents", "Documenti" },
            { "Spotlight_Filter_Videos", "Video" },
            { "Spotlight_Filter_Audio", "Audio" },
            { "Spotlight_Filter_Folders", "Cartelle" },
            { "Spotlight_Menu_OpenFilePath", "Vai al percorso file" },
            { "Spotlight_Menu_CopyPath", "Copia percorso file" },
            { "Spotlight_Menu_Convert", "Converti in..." },
            { "Spotlight_Menu_ConvertJpg", "Converti in JPG" },
            { "Spotlight_Menu_ConvertPng", "Converti in PNG" },
            { "Spotlight_Menu_ConvertPdf", "Converti in PDF" },
            { "Spotlight_Menu_ExtractMp3", "Estrai in MP3" },
            { "Spotlight_Menu_ConvertMp3", "Converti in MP3" },
            { "Spotlight_Menu_ConvertWav", "Converti in WAV" },
            { "Spotlight_Menu_ConvertMp4", "Converti in MP4" },
            { "Spotlight_Menu_RemoveBg", "Rimuovi sfondo (AI)" },
            { "Spotlight_Menu_RecycleFile", "Sposta nel Cestino (Canc)" },
            { "Notification_Converting", "Conversione in corso..." },
            { "Notification_ExtractingAudio", "Estrazione audio MP3 in corso..." },
            { "Notification_DownloadingModel", "Download modello AI ({0}%)..." },
            { "Notification_RemovingBg", "Rimozione sfondo AI in corso..." },
            { "Notification_FileRecycled", "Elemento spostato nel Cestino" },
            { "Notification_FileRecycleError", "Impossibile eliminare l'elemento" },
            { "Notification_PathCopied", "Percorso copiato negli appunti" },
            { "Notification_Success", "Completato con successo: {0}" },
            { "Notification_Error", "Operazione non riuscita: {0}" },
            { "Notification_OpenFolder", "Mostra nella cartella" },
            { "Spotlight_Category_App", "Applicazione" },
            { "Spotlight_Category_Folder", "Cartella" },
            { "Spotlight_Category_Document", "Documento" },
            { "Spotlight_Category_Image", "Immagine" },
            { "Spotlight_Category_Video", "Video" },
            { "Spotlight_Category_Audio", "Audio" },
            { "Spotlight_Category_3D", "Modello 3D" },
            { "Spotlight_Category_File", "File" },
            { "Spotlight_Category_Recent", "Recente" },
            { "Spotlight_Category_Search", "Ricerca" },
            { "Spotlight_Category_Settings", "Impostazioni" },
            { "Spotlight_Filter_Settings", "Impostazioni" },
            { "Spotlight_Hint_Open", "↵ Apri" },
            { "Spotlight_Hint_Navigate", "↑↓ Naviga" },
            { "Spotlight_Hint_Dismiss", "Esc Chiudi" },
            { "Spotlight_Clear_Search", "Cancella ricerca" },

            // QuickLook / Preview
            { "Preview_Hint_Open", "↵ Apri" },
            { "Preview_Hint_Dismiss", "Spazio / Esc Chiudi" },
            { "Preview_Btn_OpenFolder", "Apri cartella (↵)" },
            { "Preview_Btn_OpenDefaultAction", "Apri nel programma predefinito (↵)" },
            { "Preview_Btn_OpenWithDefault", "Apri con applicazione predefinita" },
            { "Preview_Btn_Close", "Chiudi" },
            { "Preview_Btn_Fullscreen", "Schermo intero" },
            { "Preview_Btn_Rotate_Tooltip", "Ruota di 90° in senso orario (solo visualizzazione)" },
            { "Preview_Btn_Play", "Riproduci" },
            { "Preview_Btn_Pause", "Pausa" },
            { "Preview_Btn_Mute", "Disattiva audio" },
            { "Preview_Btn_Unmute", "Attiva audio" },
            { "Preview_ResetZoom_Tooltip", "Reimposta zoom (100%)" },
            { "Preview_Btn_RememberSize_Tooltip", "Memorizza dimensione per questo tipo di file (persistente anche dopo il riavvio)" },
            { "Preview_Btn_ExportPdf_Tooltip", "Esporta documento in PDF" },
            { "Preview_Folder_Contains", "{0:N0} elementi" },
            { "Preview_Folder_Calculating", "Calcolo dimensioni in corso..." },
            { "Preview_Folder_Subtitle", "Cartella di file" },
            { "Preview_FileNotFound", "Il file selezionato non esiste o è stato rimosso." },
            { "Preview_NoViewer_Title", "Nessun visualizzatore di anteprima registrato per i file {0}" },
            { "Preview_NoViewer_Desc", "Puoi aprire questo file utilizzando la relativa applicazione predefinita." },
            { "Preview_Size_Label", "Dimensione" },
            { "Preview_Modified_Label", "Ultima modifica" },
            { "Preview_Path_Label", "Percorso" },
            { "Preview_Audio_Track", "Traccia Audio" },
            { "Preview_Audio_CompatiblePreparing", "Preparazione audio compatibile..." },
            { "Preview_Audio_CompatibleReady", "Audio compatibile attivo (Stereo AAC)" },
            { "Preview_Audio_MutedBitstream", "Audio AC-3/DTS: silenziato per proteggere l'udito da distorsioni" },
            { "Preview_Audio_ConvertingBadge", "Audio (Preparazione in corso...)" },
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
            { "Settings_Delete_Shortcut", "Sposta file nel Cestino (Spotlight):" },
            { "Settings_Delete_Shortcut_Hint", "Scorciatoia da tastiera per spostare nel Cestino il file selezionato nei risultati" },
            { "Settings_Delete_Shortcut_Key", "Canc (Del)" },
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
            { "Settings_DataDir_Btn_ReindexNow", "Aggiorna indice ora" },
            { "Settings_DataDir_Mode_Portable", "Modalità: Portable (sottocartella Data\\ accanto all'applicazione)" },
            { "Settings_DataDir_Mode_Standard", "Modalità: Standard (%LocalAppData%\\AeroSpot)" },
            { "Settings_IndexFrequency_Label", "Frequenza aggiornamento indice automatico:" },
            { "Settings_IndexFreq_Startup", "Ad ogni avvio" },
            { "Settings_IndexFreq_1Hour", "Ogni ora (predefinito)" },
            { "Settings_IndexFreq_2Hours", "Ogni 2 ore" },
            { "Settings_IndexFreq_30Min", "Ogni 30 minuti" },
            { "Settings_IndexFreq_15Min", "Ogni 15 minuti" },
            { "Settings_IndexFreq_Manual", "Solo manuale" },
            { "Settings_Startup_Title", "Avvio automatico con Windows" },
            { "Settings_Startup_Enable", "Avvia AeroSpot automaticamente all'avvio di Windows" },
            { "Settings_Startup_Desc", "Avvia l'Host leggero in background senza richiedere privilegi di amministratore." },
            { "Settings_Conversion_OpenFolder", "Apri la cartella di destinazione al termine della conversione" },
            { "Settings_Shell_ContextMenu", "Integra le azioni rapide di AeroSpot nel menu contestuale di Esplora risorse" },
            { "Settings_Tab_General", "Generali" },
            { "Settings_Tab_QuickLook", "QuickLook" },
            { "Settings_Tab_Spot", "Spot" },
            { "Settings_Tab_Hierarchy", "Gerarchia Spotlight" },
            { "Settings_QuickLook_Intro_Title", "Anteprime QuickLook" },
            { "Settings_QuickLook_Intro_Desc", "Anteprima istantanea con barra spaziatrice senza aprire le app predefinite. Supporta scorrimento continuo, zoom e rotazione senza perdita." },
            { "Settings_QuickLook_Hints_Title", "Comandi da Tastiera e Mouse" },
            { "Settings_QuickLook_Hints_Desc", "• Spazio: Apri o chiudi l'anteprima\n• Frecce direzionali: Naviga tra i file selezionati\n• Ctrl + Rotellina: Zoom continuo su immagini, PDF e documenti\n• Pulsante ⟳: Ruota foto e video di 90° in senso orario\n• Esc: Chiudi la finestra di anteprima" },
            { "Settings_QuickLook_WindowBehavior_Title", "Comportamento Finestra e Focus" },
            { "Settings_QuickLook_WindowBehavior_Desc", "QuickLook si apre immediatamente in primo piano per una consultazione rapida e restituisce con precisione il focus alla finestra sottostante alla chiusura." },
            { "Settings_QuickLook_AlwaysOnTop_Label", "Mantieni la finestra sempre in primo piano" },
            { "Settings_QuickLook_AlwaysOnTop_Tooltip", "Se attivo, la finestra di anteprima rimane bloccata in primo piano anche quando clicchi su altre finestre." },
            { "Settings_Spot_Intro_Title", "Spotlight & Ricerca" },
            { "Settings_Spot_Intro_Desc", "Configura la schermata iniziale di Spotlight, l'indicizzazione dei dischi, le cartelle escluse e la gerarchia delle categorie nei risultati." },
            { "Settings_Hierarchy_Title", "Priorità Risultati di Ricerca" },
            { "Settings_Hierarchy_Desc", "Trascina le categorie per definire quali tipi di file mostrare prima nei risultati di Spotlight." },
            { "Settings_Hierarchy_Reset", "Ripristina Ordine Predefinito" },
            { "Settings_Hierarchy_MoveUp", "Sposta Su" },
            { "Settings_Hierarchy_MoveDown", "Sposta Giù" },
            { "Settings_Hierarchy_SmartMixed", "Priorità corrispondenza esatta del nome (Gerarchia mista)" },
            { "Settings_Hierarchy_SmartMixed_Desc", "Mostra per primi i file o le cartelle il cui nome corrisponde esattamente alla ricerca, ordinando poi gli altri risultati secondo la gerarchia di categorie." },
            { "Settings_Hierarchy_DropTarget", "Sposta qui" },
            { "Preview_Menu_CopyPath", "Copia percorso" },
            { "Preview_Menu_OpenDefault", "Apri con applicazione predefinita" },
            { "Preview_Menu_Close", "Chiudi anteprima" },
            { "Preview_Menu_ConvertJpg", "Converti in JPG" },
            { "Preview_Menu_ConvertPng", "Converti in PNG" },
            { "Preview_Menu_ConvertPdf", "Converti in PDF" },
            { "Preview_Menu_ConvertWav", "Converti in WAV" },
            { "Preview_Menu_ConvertMp3", "Converti in MP3" },
            { "Preview_Menu_ConvertMp4", "Converti in MP4" },
            { "Spotlight_Hint_CopyPath", "Copia percorso" },
            { "Settings_Btn_Reset", "↺" },
            { "Settings_Btn_Reset_Tooltip", "Ripristino impostazioni" },
            { "Settings_Btn_Cancel", "Annulla" },
            { "Settings_Btn_Save", "Salva Impostazioni" },
            { "Settings_Status_Saved", "Impostazioni salvate con successo!" },
            { "Settings_Reset_Confirm", "Vuoi davvero ripristinare tutte le impostazioni predefinite?" },
            { "Settings_Reset_Title", "Ripristina Impostazioni AeroSpot" },
            { "Settings_RecentHistory_Title", "Cronologia e Privacy" },
            { "Settings_RecentHistory_Desc", "AeroSpot salva localmente la lista degli elementi aperti di recente per consentirti di riaprirli rapidamente dalla schermata principale dello Spotlight." },
            { "Settings_RecentHistory_Enable", "Salva la cronologia degli elementi aperti tramite AeroSpot" },
            { "Settings_RecentHistory_Info_Tooltip", "Informazioni sulla schermata iniziale e cronologia" },
            { "Settings_RecentHistory_Info_Desc", "Scegli cosa mostrare nella barra di ricerca quando lo Spotlight è vuoto:\n• Ricerche più frequenti: mostra le ricerche che effettui più spesso; premendo Invio o cliccandone una viene avviata la ricerca senza aprire nulla.\n• Elementi aperti più spesso: elenca i file e le applicazioni che apri più frequentemente con AeroSpot.\n• Elementi aperti di recente: mostra gli ultimi elementi aperti in ordine cronologico.\n• Ordine alfabetico: elenca le applicazioni installate in ordine alfabetico (comportamento classico).\n• Nessuno: non mostra alcun elemento a Spotlight vuoto e sospende la registrazione della cronologia.\n\nNota sulla privacy: tutte le ricerche e la cronologia sono conservate esclusivamente in locale sul tuo computer e possono essere rimosse completamente in ogni momento con 'Cancella cronologia'." },
            { "Settings_HomeDisplayMode_Label", "Contenuto schermata iniziale" },
            { "Settings_HomeMode_FrequentSearches", "Ricerche più frequenti (predefinito)" },
            { "Settings_HomeMode_FrequentItems", "Elementi aperti più spesso" },
            { "Settings_HomeMode_RecentOpened", "Elementi aperti di recente" },
            { "Settings_HomeMode_Alphabetical", "Ordine alfabetico" },
            { "Settings_HomeMode_None", "Nessuno (schermo vuoto)" },
            { "Settings_RecentHistory_ClearBtn", "Cancella cronologia" },
            { "Settings_RecentHistory_Cleared", "✓ Cronologia cancellata" },

            // Instructions (M3)
            { "Instructions_Tab_Title", "Istruzioni" },
            { "Instructions_Title", "Istruzioni d'uso & Documentazione" },
            { "Instructions_Desc", "Consulta la guida utente offline per scoprire tutte le funzionalità e le scorciatoie di AeroSpot." },
            { "Instructions_Btn_Open", "Apri le istruzioni" },
            { "Instructions_Version", "Versione {0} ({1})" },
            { "Instructions_GitHub_Link", "Visualizza il progetto su GitHub" },

            // Aggiornamenti dell'applicazione (GitHub Release Checker)
            { "Settings_Updates_Title", "Aggiornamenti dell'applicazione" },
            { "Settings_Updates_Desc", "Verifica la disponibilità di nuove versioni su GitHub e scarica automaticamente l'aggiornamento." },
            { "Settings_Updates_Freq_Label", "Frequenza controllo automatico:" },
            { "Settings_Updates_Freq_Startup", "Ad ogni avvio (predefinito)" },
            { "Settings_Updates_Freq_Daily", "Ogni giorno" },
            { "Settings_Updates_Freq_Weekly", "Ogni settimana" },
            { "Settings_Updates_Freq_Monthly", "Ogni mese" },
            { "Settings_Updates_Freq_Manual", "Mai (solo manuale)" },
            { "Settings_Updates_Btn_Check", "Verifica aggiornamenti" },
            { "Settings_Updates_Checking", "Verifica aggiornamenti su GitHub in corso..." },
            { "Settings_Updates_UpToDate", "✓ AeroSpot è aggiornato (v{0} è l'ultima versione disponibile)" },
            { "Settings_Updates_Available", "★ Nuova versione disponibile: v{0}!" },
            { "Settings_Updates_Downloading", "Download aggiornamento in corso... {0}%" },
            { "Settings_Updates_Ready", "✓ Aggiornamento scaricato e pronto all'installazione!" },
            { "Settings_Updates_Btn_Install", "🚀 Installa aggiornamento e riavvia" },
            { "Settings_Updates_Btn_Notes", "Note di rilascio" },
            { "Settings_Updates_Error", "Impossibile verificare gli aggiornamenti. Verifica la connessione a Internet o riprova più tardi." },
            { "Settings_Updates_LastChecked", "Ultima verifica: {0}" },
            { "Settings_Updates_NeverChecked", "Mai verificato" }
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
