# AeroSpot User Guide

Welcome to **AeroSpot**, the modern Windows 11 (and Windows 10 x64) application designed to combine the fast search experience of a *Spotlight*-style launcher with the instant preview capabilities of *QuickLook*, keeping resident memory consumption of the Host at rest strictly under 10 MB of Private Bytes.

---

## 1. Quick Start & Editions

AeroSpot is distributed in two editions built from the exact same compiled binaries:

| Edition | Target Audience | Program Folder | Data & Database Folder |
|---|---|---|---|
| **Installer (`Setup.exe`)** | Everyday users, permanent installation | `{localappdata}\Programs\AeroSpot\` | `%LOCALAPPDATA%\AeroSpot\` |
| **Portable (`.zip`)** | Developers, USB drives, portable usage | User-selected extracted folder | Local `Data\` subfolder beside program files |

### Launching the Application
- **Installed Edition:** Launch AeroSpot from the Start menu or the Desktop shortcut.
- **Portable Edition:** Extract the ZIP archive and run the native launcher `AeroSpot.exe`.
- The application launches silently in the background and places its dark Fluent icon in the System Tray next to the Windows clock.

### Clean Exit and Uninstallation
- **Clean exit:** Right-click the AeroSpot icon in the system tray -> **Exit AeroSpot**. All processes (Host, Worker, Tools) terminate cleanly and immediately.
- **Uninstalling Installed Edition:** Open Windows *Settings* -> *Apps* -> *Installed apps* -> search for **AeroSpot** -> *Uninstall*. The uninstaller will prompt whether you wish to keep or delete your search index and user preferences.
- **Uninstalling Portable Edition:** Run `Uninstall.exe` located in the root of the extracted folder. It terminates active processes, cleans autostart and context entries from the Windows Registry, purges cache, and deletes the application folder. Alternatively, execute from terminal:
```cmd
AeroSpot.Host.exe --cleanup
```

---

## 2. Using the Launcher (Quick Search)

### Keyboard Shortcut
- **Default shortcut:** `Alt + Space` (customizable in Settings to `Ctrl + Space`, `Win + Space`, or a custom combination).
- Summon AeroSpot instantly from any application or screen.

### How to Search
1. Press the keyboard shortcut: the fluid dark acrylic search bar appears centered at the top of your active screen.
2. Type what you are looking for:
   - **Installed and system applications** (e.g. *Chrome*, *Notepad*, *Calculator*, *Terminal*, *VS Code*, *Steam*, Store/UWP apps like *WhatsApp* indexed via `shell:AppsFolder` and `Program Files`; `.lnk`/`.url` shortcut files are excluded from search results to prevent duplicates)
   - **Documents and files** (e.g. *report.docx*, *sheet.xlsx*, *project.py*, *image.png*, *video.mp4*)
   - **Folders** (e.g. *Downloads*, *Documents*, *Pictures*)
3. **Built-in Calculator with Percentages:** Directly enter a mathematical expression (e.g. `2+2`, `15 * 4`, `sqrt(144)`, `sin(pi/2)`) or percentage formulas in Windows/Excel style:
   - `1190-22%` = `928.2`
   - `1190+22%` = `1451.8`
   - `1190*22%` = `261.8`
   - `1190/22%` = `5409.09...`
   - `22%` = `0.22`
   - Chained percentages: `200+10%+10%` = `242`
   - With parentheses: `(100+20)-10%` = `108`
4. **Unit Converter:** Convert units on the fly for length, weight/mass (grams, hectograms, kilograms, pounds, ounces), volume (liters, milliliters, centiliters, gallons), and temperatures (°C, °F, K).
5. **Home Screen:** When the search box is empty, Spotlight displays **Most frequent searches** by default (customizable across 5 modes in Settings, including *Recently opened items* with a warm amber `"Recent"` badge).
6. **Windows Settings & AeroSpot Tabs Search:** Search Windows system settings directly (e.g. *"keyboard"*, *"tastiera"*, *"wifi"*, *"display"*, *"schermo"*, *"sound"*, *"suono"*, *"updates"*, *"bluetooth"*, *"printers"*, *"battery"*, *"mouse"*, *"privacy"*, *"notifications"*, *"background"*, *"storage"*, *"accessibility"*) and AeroSpot configuration tabs (*"shortcuts"*, *"indexing"*, *"updates"*, *"instructions"*). A dedicated *"Settings"* filter button lets you isolate configuration items.
7. **Zero-Lag Smooth Typing:** A 50ms asynchronous Dispatcher debounce timer decouples typing from SQLite database queries, maintaining a fluid 60/144 fps typing experience with zero keypress lag.
8. **Space works naturally:** When searching multi-word queries (e.g. *"Google Chrome"* or *"Shopping notes"*), pressing the spacebar types a normal space character without triggering file preview.

### Shortcuts within the Search Window
| Key | Action |
|---|---|
| **Alt + Space** | Toggles Spotlight open or closed. If QuickLook is active, closes only Spotlight. |
| **Typing** | Search starts with no item forcefully pre-selected, allowing continuous typing. |
| **Tab / Shift + Tab** | Cycles smoothly through category filter pills without losing focus from the search box. |
| **Down Arrow (↓)** | Selects the first result in the list, ready for preview or execution. |
| **Up (↑) and Down (↓) Arrows** | Navigate through the entire list of results. **If the QuickLook preview is open, it immediately updates the displayed file without closing!** |
| **Enter (↵)** | Immediately executes the selected item (or first result if none is explicitly focused). |
| **Space** (with item selected) | Opens the instant preview (*QuickLook*). **The Spotlight search window remains open underneath!** |
| **Space** (inside preview) | Closes *only* the preview and returns focus to Spotlight to continue browsing results. |
| **Del** (with item selected) | Sends the selected file or folder to the Windows Recycle Bin with system confirmation (customizable shortcut). |
| **Esc** (inside preview or in Spotlight) | Universal, unchangeable key: instantly closes both QuickLook preview and Spotlight search window simultaneously. |
| **Right-Click on result** | Opens dynamic context menu: **📁 Open file location**, **🚀 Open**, **👁 Preview**, **📋 Copy path**, **🗑 Move to Recycle Bin**, and quick conversion actions. |
| **Drag & Drop** | Drag a result onto Desktop, File Explorer, or external apps with a translucent ghost thumbnail (behavior configurable in Settings → Spot). |
| **Double Click** | Launches the file or application directly with the default application. |
| **Buttons 🔄 and ⚙** | Bottom bar: refresh icon 🔄 triggers background incremental reindexing, and gear icon ⚙ opens Settings. |
| **Clicking outside** | Automatically dismisses the launcher. |

### Quick Actions, Windows Context Menu & Aggregate Conversions
Right-clicking any search result in Spotlight, inside QuickLook, or via the cascading **"AeroSpot" submenu in Windows File Explorer** (`Shift + Right-click` / classic menu) gives instant access to single-file and **multi-selection batch conversions**:
- **📁 Open file location:** Opens File Explorer and directly selects and highlights the item.
- **🖼 Convert to JPG / PNG:** Converts images (individually or in batch when selecting multiple files of the same format) into high-quality JPEG (using DPI-independent pixel-level white compositing for transparency) or lossless transparent PNG.
- **📄 Convert to PDF / Combine photos into single PDF:** Converts images or text/Word documents (`.md`, `.txt`, `.docx`, `.rtf`, `.odt`) into native PDFs; selecting multiple photos combines them seamlessly into one ordered **multi-page PDF document**.
- **📑 Merge selected PDFs:** Selecting two or more `.pdf` files in File Explorer merges them into a single unified document preserving all vector graphics and selectable text.
- **🎬 Convert to MP4 / 🎵 Extract MP3 & WAV:** Converts non-MP4 videos (MKV, AVI, MOV, WMV, WEBM) to compatible MP4 via FFmpeg or extracts high-quality MP3/WAV audio.
- **✨ Remove Background (AI):** Cuts out photo backgrounds (single or multi-file batch) using a local Onnx/DirectML neural model with DirectX 12 GPU acceleration and CPU fallback. The primary `RMBG-1.4` model (~176 MB, with ~4.7 MB `u2netp` fallback) is downloaded on-demand upon first use.
- **Smart Multi-Monitor Progress Pill:** Every task displays a sleek Dark Acrylic notification anchored flush inside the bottom-right corner of the **currently active monitor** (per-monitor DPI aware) with accurate live progress and a "Show in folder" button upon completion.

---

## 3. Instant File Preview (QuickLook)

When in **File Explorer**, on the **Desktop**, or inside **Open / Save / Attach file dialogs** of any application (e.g. WhatsApp, Chrome, Edge, Notepad, Office):
1. Click on a file or select it using keyboard arrow keys.
2. Press the **Spacebar**: a window with the file's content preview opens instantly.
3. Use **Left (`←`) / Right (`→`) / Up (`↑`) / Down (`↓`)** arrow keys to step directly to the previous or next item in the folder (matching File Explorer's exact sort order) without losing focus or closing QuickLook.
4. Press **Space** (or **Esc**) again to close the preview.

### Interactive Features in Preview:
- **Right-Click Context Menu:** Right-clicking anywhere within the QuickLook preview window brings up a Dark Fluent context menu granting direct access to all conversion tools, file management, and window controls.
- **Manual Rotation (Photos & Videos):** Dedicated **⟳** button in the QuickLook header to rotate preview by 90° clockwise per click (0° → 90° → 180° → 270°). The window geometry and aspect ratio automatically adapt to the rotated view, eliminating black bars or letterboxing. Rotation is view-only in real time: the underlying file is never modified or rewritten on disk.
- **Universal View/Zoom Reset (`R`):** Pressing **`R`** (or clicking the `1:1` / `"↺ Reset view"` button) immediately resets camera view or 1:1 zoom across 3D models (`.stl`/`.3mf`), images, PDFs, and PreviewHandlers.
- **Mouse Wheel Zoom (Images & PDFs):** Zoom in or out smoothly using `Ctrl + Mouse Wheel` (or mouse wheel on images/3D) anchored directly at the mouse cursor.
- **Pan and Drag:** When an image or PDF is zoomed in, freely drag it by holding down the left mouse button. Click **1:1** to reset to original scale.
- **PDF Document Navigation:** High-speed left sidebar thumbnail gallery (rendered on-demand in 1–2 ms per page); click any thumbnail to jump directly to that page with synchronized blue border highlighting. Scroll vertically through continuous stacked pages, pan with the hand tool (`Cursors.SizeAll`), use keyboard keys (`PageUp`/`PageDown`/`Home`/`End`), or click the vertical Up (`▲`) and Down (`▼`) chevrons on the bottom-left page capsule.
- **Full Screen:** Press **F11**, **double-click** the photo/video, or click the **⛶** button at the top right to expand preview to full screen.
- **Window Memory:** The preview window remembers position and dimensions during the active session, with optional persistent per-category memory via the pin 📌 icon in the header.

### Canonical Supported Formats Table:

| Category | Supported Formats and Extensions | Viewer Features / Architectural Notes |
|---|---|---|
| **PDF Documents** | `.pdf` | Ultra-fast native preview via Google PDFium x64 (`Docnet.Core`, QuickLook Store standard), sub-50ms opening (~12–15 ms/page) on pure white paper with elegant drop shadow, **continuous vertical scrolling** with stacked pages, **left sidebar page thumbnail gallery** (`PdfSidebar`, 144px) for rapid browsing and click-to-page jump with synchronized highlight, **page counter capsule repositioned at bottom-left** under thumbnails with vertical chevrons (▲ / ▼), async lazy-loading with LRU cache (max 16 pages in RAM), left-click hand tool panning (`Cursors.SizeAll`), cursor-anchored `Ctrl + MouseWheel` zoom at **60 FPS with 1:1 / `R` reset**, keyboard shortcuts (`PageUp`/`PageDown`/`Home`/`End`), and 100% in-process execution with zero external browser subprocesses. |
| **Images** | `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico` | High-resolution rendering with DWM Cloaking anti-flicker architecture, DCT downsampling to 2560px for instant loading (< 150 ms), zoom, pan, `⟳` rotation and `R` reset, animated GIF support. |
| **Camera RAW** | `.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf` | Ultra-fast strategy: 1) Instant (~20–30 ms) embedded full-size preview JPEG extraction without full raw demosaicing; 2) Fallback to system WIC decoder with DCT downsampling and EXIF orientation; 3) Clean camera metadata card. Zero prior conversion needed. |
| **Vector Graphics** | `.svg` | High-fidelity native vector rendering powered by SharpVectors (WPF DrawingImage), infinite smooth zoom without pixelation, safe script isolation (`IncludeRuntime = false`), and XML source code fallback. |
| **Vector Graphics AI** | `.ai` (Adobe Illustrator) | Clean informational card with technical metadata and quick launch in default application, avoiding third-party COM server crashes and black screens. |
| **Spreadsheets** | `.xlsx`, `.xlsm`, `.xltx` | Native C# OpenXML reader with zero external dependencies (opens without Office installed), multi-sheet tab navigation, Dark Fluent grid with column letters and row numbers. |
| **Audio** | `.mp3`, `.wav`, `.m4a`, `.aac`, `.flac`, `.wma`, `.ogg`, `.mid`, `.midi`, `.aiff`, `.opus` | Vintage 33 RPM spinning vinyl turntable with center spindle hole, animated continuous rotation, cinematic tonearm tracking grooves and retracting on pause/stop while freezing rotation angle. |
| **Videos** | `.mp4`, `.mov`, `.mkv`, `.avi`, `.wmv`, `.webm`, `.h264`, `.h265`, `.m4v`, `.flv`, `.3gp`, `.ts`, `.mpg`, `.mpeg` | Hardware-accelerated smooth playback, automatic orientation for vertical smartphone recordings (plus manual `⟳` rotation), on-demand 60x FFmpeg remuxing pipeline for incompatible multichannel bitstream audio (AC-3, DTS, TrueHD) at VLC parity, interactive click-to-seek timeline scrubber. |
| **3D Models** | `.stl`, `.3mf` | Native 60 FPS 3D rendering engine for ASCII/binary STL and `.3mf` files. Full-container orbit rotation via mouse dragging, anti-disappear wheel zoom, `"↺ Reset view"` button and `R` key, triangle count, and bounding box dimensions. |
| **Web Pages** | `.html`, `.htm`, `.xhtml` | Safe GPU-accelerated passive dark-mode viewer (`HtmlToFlowDocumentConverter`), isolated from script execution and compliant with WPF airspace constraints, with **Preview / Source Code** toggle and **↗ Open in Browser** button. |
| **Text & Code** | `.txt`, `.md`, `.py`, `.json`, `.log`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.toml`, `.sql`, `.bat`, `.cmd`, `.cpp`, `.c`, `.h`, `.rs`, `.go`, `.ts`, `.js` | Monospace typography (*Cascadia Mono* / *Consolas*), automatic encoding detection (UTF-8, UTF-16, ANSI), line count, and safe 100 KB truncation limit to protect RAM. |
| **Office & RTF/ODT Documents** | `.docx`, `.docm`, `.dotx`, `.rtf`, `.odt` | Native zero-Office in-process readers (~5–15 ms) for `.docx`/`.docm`/`.dotx`, `.rtf`, and `.odt` with Dark Fluent typography and 1-click PDF export (zero `WINWORD.EXE` processes; legacy `.doc`, `.dot`, `.xls`, `.ppt`, `.pptx` display a safe metadata card). |
| **Folders** | File system directories and folders | Instant 0 ms display with asynchronous calculation of total disk size, file count, and subfolder count. |
| **Shortcuts & Apps** | `.lnk`, `.exe`, UWP / Store apps | Clean informational card with high-resolution icon, file size, path, and modification date. |
| **Generic Files** | All other file extensions | Clean informational card with native Shell icon, file size, modification date, and full path. |

> **Typing Protection:** When renaming a file or typing in Windows Explorer's address bar or search box, pressing Space inputs a normal space character without opening preview.

---

## 4. Settings Panel

You can access Settings in two ways:
1. **From system tray:** Right-click the AeroSpot icon next to the clock -> **Settings...**
2. **From search bar:** Click the **⚙ Settings** button at the bottom-right corner of Spotlight.

### Configurable Tabs and Sections:

The Settings window is organized into three clean, focused sections (plus the bottom-left `↺` restore defaults button):

1. **General:**
   - **Interface Language:** Toggle between `Automatic`, `Italiano`, and `English` with instantaneous in-memory language switching.
   - **Keyboard Shortcuts:** Configure all 3 global hotkeys: **Spotlight** (default `Alt + Space`), **QuickLook** (default `Space`), and **Move to Recycle Bin (Spotlight)** (default `Canc` / `Del`).
   - **System & Performance:** Options for *Start with Windows*, *Keep-Warm* active engine, worker idle timeout (default `30` s), auto-opening destination folder after conversions, and Windows Explorer context menu integration.
   - **Data Directory and Storage:** Current data folder path, *Open data folder* button, Portable (`Data\`) or Standard (`%LocalAppData%\AeroSpot`) mode indicator, and index disk usage.
   - **Application Updates:** Automatic GitHub update check frequency (*Startup*, *Daily*, *Weekly*, *Monthly*, *Manual*), *Check for Updates* button, version info, release notes button, download progress bar, and *Install Update & Restart* button.
   - **User Guide & Documentation:** Buttons to open this offline guide and the project's GitHub page.

2. **QuickLook:**
   - **Quick Guide & Keyboard/Mouse Controls:** Overview of controls (`Space` to open/close, arrow keys to navigate items, `Ctrl + Mouse Wheel` to zoom, `⟳` button to rotate photos/videos 90° clockwise, `Esc` to close).
   - **QuickLook Window Sizing:** Per-category checkboxes to persistently remember custom window dimensions across PC reboots (*Images*, *Videos*, *Audio*, *PDF Documents*, *Text & Office Documents*, *Other Files & Apps*).
   - **Window Behavior & Focus:** Option *"Keep preview window always on top"* to choose whether QuickLook remains permanently pinned in the foreground even when clicking other applications, or uses the default contextual layering where the active window smoothly layers over QuickLook upon deactivation.

3. **Spot (Search & Indexing):**
   - **Home Screen Mode:** Initial view dropdown with 5 options: **Most frequent searches (default)**, *Most frequently opened items*, *Recently opened items*, *Alphabetical order*, and *None (blank screen)*.
   - **Spotlight Drag & Drop Behavior:** Choose between *Follow Windows settings (default)*, *Always copy file*, and *Always move file* (Applications are always copied/linked for safety).
   - **Indexed Drives:** Toggle disk drives to index with interactive blue `i` technical info card on RAM, Disk, CPU, and scan speed.
   - **Automatic Index Update Frequency:** Configure periodic disk re-scan interval + *Update index now* button.
   - **Exclusions:** Exclude standard or custom folders from indexing with immediate add/remove support.
   - **History & Privacy:** *Clear All History* button to irreversibly clear recent search queries and opened items from the local SQLite database.
   - **Category Hierarchy:** Fluid drag-and-drop reordering of search result categories with ghost card, option to prioritize 100% exact name matches, and reset order button.

---

## 5. System Tray Icon and Menu

In the bottom-right corner, next to the Windows clock:
- **Left-Click (or double click):** Summons Spotlight quick search.
- **Right-Click:** Native Win32 context menu featuring:
  - *Open AeroSpot (`Alt + Space`)*
  - *Settings...*
  - *Status: Worker: Idle / Active* (read-only indicator showing whether the UI worker is idle or active in memory)
  - *Exit AeroSpot* (clean and complete shutdown of all processes).

---

## 6. Installation, Update, and Uninstallation Guide

### Installed Edition (Recommended)
1. **Installation:** Download and run `AeroSpot-Setup-v<ver>.exe`. Installs per-user into `{localappdata}\Programs\AeroSpot` without requiring UAC administrator privileges. Creates Start Menu and Desktop shortcuts.
2. **Updating:** When an update is detected, click **"🚀 Update now"** in the floating notification pill (or go to *Settings → General → Application Updates → Check for Updates → Install Update & Restart*). The updater verifies the SHA-256 checksum, terminates active processes, and replaces program binaries while preserving your search database and preferences intact in `%LOCALAPPDATA%\AeroSpot\`.
3. **Uninstallation:** From Windows *Installed Apps*, select AeroSpot and click *Uninstall*. The uninstaller cleans program files, shortcuts, autostart entries, and prompts whether to keep or remove the index database.

### Portable Edition
1. **Installation:** Download and extract `AeroSpot-v<ver>-win-x64-portable.zip` into any folder or USB drive. Run `AeroSpot.exe`.
2. **1-Click Auto-Update (or Manual):** When a new release is detected on GitHub, click **"🚀 Update now"** in the floating notification pill (or open *Settings → General → Application Updates → Check for Updates → Install Update & Restart*): AeroSpot downloads the `.zip`, verifies its SHA-256 checksum against `SHA256SUMS.txt`, cleanly closes all processes, replaces binaries while strictly preserving `Data\` and `portable.txt`, and relaunches automatically. You can also extract a new ZIP manually over existing files (`Data\` remains untouched).
3. **Uninstallation:** Run `Uninstall.exe` beside `AeroSpot.exe` for an automated, zero-residue cleanup.

---

## 7. Troubleshooting & FAQ

- **The shortcut `Alt + Space` does not respond:**  
  Verify if another utility (e.g. PowerToys Run or Listary) already occupies `Alt + Space`. AeroSpot detects the conflict, notifies you, and automatically switches to safe fallbacks `Ctrl + Space` or `Alt + F12`. You can customize shortcuts at any time in Settings.
- **Viewing Diagnostic Logs:**  
  Log files retaining the last 24 hours of diagnostics for Host, Worker, and Tools are located in the `logs\` subfolder inside your active data directory (`%LOCALAPPDATA%\AeroSpot\logs\` in Standard mode or `Data\logs\` in Portable mode).
