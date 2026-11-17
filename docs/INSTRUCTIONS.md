# AeroSpot User Guide

Welcome to **AeroSpot**, the modern Windows 11 (and Windows 10 x64) application designed to combine the fast search experience of a *Spotlight*-style launcher with the instant preview capabilities of *QuickLook*, keeping resident memory consumption of the Host at rest strictly under 10 MB of Private Bytes.

---

## 1. Quick Start & Editions

AeroSpot is distributed in two editions built from the exact same compiled binaries:

| Edition | Target Audience | Program Folder | Data & Database Folder |
|---|---|---|---|
| **Installer (`Setup.exe`)** | Everyday users, permanent installation | `{localappdata}\Programs\AeroSpot\` | `%LOCALAPPDATA%\AeroSpot\Data\` |
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
   - **Installed and system applications** (e.g. *Chrome*, *Notepad*, *Calculator*, *Terminal*, *VS Code*, *Steam*, Store/UWP apps like *WhatsApp*)
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
5. **Recently Opened Items (Home):** When the search box is empty, Spotlight displays your recently opened items (apps, files, documents) ordered by recency with a warm amber "Recent" badge.
6. **Windows Settings & AeroSpot Tabs Search:** Search Windows system settings directly (e.g. *"keyboard"*, *"tastiera"*, *"wifi"*, *"display"*, *"schermo"*, *"sound"*, *"suono"*, *"updates"*, *"bluetooth"*, *"printers"*, *"battery"*, *"mouse"*, *"privacy"*, *"notifications"*, *"background"*, *"storage"*, *"accessibility"*) and AeroSpot configuration tabs (*"shortcuts"*, *"indexing"*, *"updates"*, *"instructions"*). A dedicated *"Settings"* filter button lets you isolate configuration items.
7. **Zero-Lag Smooth Typing:** A 50ms asynchronous Dispatcher debounce timer decouples typing from SQLite database queries, maintaining a fluid 60/144 fps typing experience with zero keypress lag.
8. **Space works naturally:** When searching multi-word queries (e.g. *"Google Chrome"* or *"Shopping notes"*), pressing the spacebar types a normal space character without triggering file preview.

### Shortcuts within the Search Window
| Key | Action |
|---|---|
| **Alt + Space** | Toggles Spotlight open or closed. If QuickLook is active, closes only Spotlight. |
| **Typing** | Search starts with no item forcefully pre-selected, allowing continuous typing. |
| **Down Arrow (↓)** | Selects the first result in the list, ready for preview or execution. |
| **Up (↑) and Down (↓) Arrows** | Navigate through the entire list of results. **If the QuickLook preview is open, it immediately updates the displayed file without closing!** |
| **Enter (↵)** | Immediately executes the selected item (or first result if none is explicitly focused). |
| **Space** (with item selected) | Opens the instant preview (*QuickLook*). **The Spotlight search window remains open underneath!** |
| **Space** (inside preview) | Closes *only* the preview and returns focus to Spotlight to continue browsing results. |
| **Esc** (inside preview or in Spotlight) | Universal, unchangeable key: instantly closes both QuickLook preview and Spotlight search window simultaneously. |
| **Right-Click on result** | Opens dynamic context menu: **📁 Open file location**, **🚀 Open**, **👁 Preview**, **📋 Copy path**, and quick conversion actions. |
| **Double Click** | Launches the file or application directly with the default application. |
| **Buttons 🔄 and ⚙** | Bottom bar: refresh icon 🔄 triggers background incremental reindexing, and gear icon ⚙ opens Settings. |
| **Clicking outside** | Automatically dismisses the launcher. |

### Quick Actions, Windows Context Menu & Aggregate Conversions
Right-clicking any search result in Spotlight, inside QuickLook, or via the cascading **"AeroSpot" submenu in Windows File Explorer** (`Shift + Right-click` / classic menu) gives instant access to single-file and **multi-selection batch conversions**:
- **📁 Open file location:** Opens File Explorer and directly selects and highlights the item.
- **🖼 Convert to JPG / PNG:** Converts images (individually or in batch when selecting multiple files of the same format) into high-quality JPEG (using DPI-independent pixel-level white compositing for transparency) or lossless transparent PNG.
- **📄 Convert to PDF / Combine photos into single PDF:** Converts images or text/Word documents (`.md`, `.txt`, `.docx`) into native PDFs; selecting multiple photos combines them seamlessly into one ordered **multi-page PDF document**.
- **📑 Merge selected PDFs:** Selecting two or more `.pdf` files in File Explorer merges them into a single unified document preserving all vector graphics and selectable text.
- **🎬 Convert to MP4 / 🎵 Extract MP3 & WAV:** Converts non-MP4 videos (MKV, AVI, MOV, WMV, WEBM) to compatible MP4 via FFmpeg or extracts high-quality MP3/WAV audio.
- **✨ Remove Background (AI):** Cuts out photo backgrounds (single or multi-file batch) using a local Onnx/DirectML neural model with DirectX 12 GPU acceleration and CPU fallback. Downloaded on-demand upon first use (~44 MB).
- **Smart Multi-Monitor Progress Pill:** Every task displays a sleek Dark Acrylic notification anchored flush inside the bottom-right corner of the **currently active monitor** (per-monitor DPI aware) with accurate live progress and a "Show in folder" button upon completion.

---

## 3. Instant File Preview (QuickLook)

When in **File Explorer**, on the **Desktop**, or inside **Open / Save / Attach file dialogs** of any application (e.g. WhatsApp, Chrome, Edge, Notepad, Office):
1. Click on a file or select it using keyboard arrow keys.
2. Press the **Spacebar**: a window with the file's content preview opens instantly.
3. Press **Space** (or **Esc**) again to close the preview.

### Interactive Features in Preview:
- **Right-Click Context Menu:** Right-clicking anywhere within the QuickLook preview window brings up a Dark Fluent context menu granting direct access to all conversion tools, file management, and window controls.
- **Manual Rotation (Photos & Videos):** Dedicated **⟳** button in the QuickLook header to rotate preview by 90° clockwise per click (0° → 90° → 180° → 270°). The window geometry and aspect ratio automatically adapt to the rotated view, eliminating black bars or letterboxing. Rotation is view-only in real time: the underlying file is never modified or rewritten on disk.
- **Mouse Wheel Zoom (Images):** Zoom in or out smoothly anchored directly at the mouse cursor.
- **Pan and Drag:** When an image is zoomed in, freely drag it by holding down the left mouse button. Click **1:1** to reset to original scale.
- **PDF Document Navigation:** High-speed left sidebar thumbnail gallery (rendered on-demand in 1–2 ms per page); click any thumbnail to jump directly to that page with synchronized blue border highlighting. Scroll vertically through continuous stacked pages, pan with the hand tool (`Cursors.SizeAll`), use keyboard keys (`PageUp`/`PageDown`/`Home`/`End`), or click the vertical Up (`▲`) and Down (`▼`) chevrons on the bottom-left page capsule.
- **Full Screen:** Press **F11**, **double-click** the photo/video, or click the **⛶** button at the top right to expand preview to full screen.
- **Window Memory:** The preview window remembers position and dimensions during the active session, with optional persistent per-category memory via the pin 📌 icon in the header.

### Canonical Supported Formats Table:

| Category | Supported Formats and Extensions | Viewer Features / Architectural Notes |
|---|---|---|
| **PDF Documents** | `.pdf` | Ultra-fast native preview via Google PDFium x64 (`Docnet.Core`, QuickLook Store standard) on pure white paper with elegant drop shadow, **continuous vertical scrolling** with stacked pages, **left sidebar page thumbnail gallery** (`PdfSidebar`, 144px) for rapid browsing and click-to-page jump with synchronized highlight, **page counter capsule repositioned at bottom-left** under thumbnails with vertical chevrons (▲ / ▼), async lazy-loading with LRU cache (max 16 pages in RAM), left-click hand tool panning (`Cursors.SizeAll`), cursor-anchored `Ctrl + MouseWheel` zoom at **60 FPS with 1:1 reset button**, keyboard shortcuts (`PageUp`/`PageDown`/`Home`/`End`), and 100% in-process execution with zero external browser subprocesses. |
| **Images** | `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico` | High-resolution rendering with DWM Cloaking anti-flicker architecture, DCT downsampling to 2560px for instant loading (< 150 ms), zoom and pan, animated GIF support. |
| **Camera RAW** | `.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf` | Ultra-fast strategy: 1) Instant (~20–30 ms) embedded full-size preview JPEG extraction without full raw demosaicing; 2) Fallback to system WIC decoder with DCT downsampling and EXIF orientation; 3) Clean camera metadata card. Zero prior conversion needed. |
| **Vector Graphics** | `.svg` | High-fidelity native vector rendering powered by SharpVectors (WPF DrawingImage), infinite smooth zoom without pixelation, safe script isolation (`IncludeRuntime = false`), and XML source code fallback. |
| **Vector Graphics AI** | `.ai` (Adobe Illustrator) | Clean informational card with technical metadata and quick launch in default application, avoiding third-party COM server crashes and black screens. |
| **Spreadsheets** | `.xlsx`, `.xlsm`, `.xltx` | Native C# OpenXML reader with zero external dependencies (opens without Office installed), multi-sheet tab navigation, Dark Fluent grid with column letters and row numbers. |
| **Audio** | `.mp3`, `.wav`, `.m4a`, `.aac`, `.flac`, `.wma`, `.ogg`, `.mid`, `.midi`, `.aiff`, `.opus` | Vintage 33 RPM spinning vinyl turntable with center spindle hole, animated continuous rotation, cinematic tonearm tracking grooves and retracting on pause/stop while freezing rotation angle. |
| **Videos** | `.mp4`, `.mov`, `.mkv`, `.avi`, `.wmv`, `.webm`, `.h264`, `.h265`, `.m4v`, `.flv`, `.3gp`, `.ts`, `.mpg`, `.mpeg` | Hardware-accelerated smooth playback, automatic orientation for vertical smartphone recordings, on-demand 60x FFmpeg remuxing pipeline for incompatible multichannel bitstream audio (AC-3, DTS, TrueHD) at VLC parity, interactive click-to-seek timeline scrubber. |
| **3D Models** | `.stl` | Native 60 FPS 3D rendering engine for ASCII and binary STL files. Orbit rotation via mouse dragging, mouse wheel zoom, triangle count, and bounding box dimensions. |
| **Web Pages** | `.html`, `.htm` | Safe passive dark-mode viewer, isolated from script execution and compliant with WPF airspace constraints. |
| **Text & Code** | `.txt`, `.md`, `.py`, `.json`, `.log`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.toml`, `.sql`, `.bat`, `.cmd`, `.cpp`, `.c`, `.h`, `.rs`, `.go`, `.ts`, `.js` | Monospace typography (*Cascadia Mono* / *Consolas*), automatic encoding detection (UTF-8, UTF-16, ANSI), line count, and safe 100 KB truncation limit to protect RAM. |
| **Office Documents** | `.docx`, `.docm`, `.dotx`, `.pptx`, `.rtf` | Native zero-Office OpenXML reader (~5–15 ms) for `.docx`/`.docm`/`.dotx` with Fluent typography and PDF export; out-of-process COM hosting (`prevhost.exe`) for `.pptx` and `.rtf`. |
| **Folders** | File system directories and folders | Instant 0 ms display with asynchronous calculation of total disk size, file count, and subfolder count. |
| **Shortcuts & Apps** | `.lnk`, UWP / Store apps | Metadata card with real target extraction, file size, and high-resolution vector application icons. |
| **Generic Files** | All other file extensions | Clean informational card with native Shell icon, file size, modification date, and full path. |

> **Typing Protection:** When renaming a file or typing in Windows Explorer's address bar or search box, pressing Space inputs a normal space character without opening preview.

---

## 4. Settings Panel

You can access Settings in two ways:
1. **From system tray:** Right-click the AeroSpot icon next to the clock -> **Settings...**
2. **From search bar:** Click the **⚙ Settings** button at the bottom-right corner of Spotlight.

### Configurable Tabs and Sections:

The Settings window is organized into three clean, focused sections:

1. **General:**
   - **Interface Language:** Toggle between `Automatic`, `Italiano`, and `English` with instantaneous in-memory language switching.
   - **Keyboard Shortcuts:** Configure global hotkeys for Spotlight (default `Alt + Space`) and QuickLook (default `Spacebar`).
   - **Startup & Performance:** Options for *Start with Windows*, *Keep-Warm* active engine, and *Pre-spawn on Startup*.
   - **Explorer Integration:** Windows Shell context menu actions and auto-opening destination folder after media conversions.
   - **Updates & About:** GitHub update check frequency, instant manual check, version info, `Data\` folder link, and icon cache reset.

2. **QuickLook:**
   - **Preview Guide & Shortcuts:** Overview of keyboard commands (`Space` / `Esc` to dismiss, `F11` or double click for fullscreen, wheel zoom, mouse pan, `PageUp`/`PageDown` for PDF, `1:1` reset button).
   - **Rotation & Proportions:** Dedicated **⟳** button for 90° clockwise real-time rotation for photos and videos with automatic window aspect-ratio adjustment (no black bars).
   - **Window Behavior:** Manage persistent per-category window size memory via pin 📌 icon, with a one-click button to reset remembered sizes to factory defaults.
   - **Always on Top Window Lock:** Option *"Keep preview window always on top"* to choose whether QuickLook remains permanently pinned in the foreground even when clicking other applications, or uses the default contextual layering where the active window smoothly layers over QuickLook upon deactivation.

3. **Spot (Search & Indexing):**
   - **Home Screen Mode:** Initial view dropdown (*Recently opened items*, *Most frequently opened items*, *Most frequent searches*, *Show nothing / Blank privacy*).
   - **Indexed Drives:** Toggle disk drives to index with detailed technical info card on RAM, CPU, and scan speed.
   - **Exclusions:** Exclude folders or paths from indexing with immediate SQLite record purge.
   - **Update Frequency:** Configure periodic disk re-scan interval.
   - **History & Privacy:** Irreversibly clear recent search queries and opened items from local SQLite database.
   - **Category Hierarchy:** Fluid drag-and-drop reordering of search result categories with ghost card and target slot highlighting. Mixed hierarchy toggle and reset order button.

---

## 5. System Tray Icon and Menu

In the bottom-right corner, next to the Windows clock:
- **Left-Click (or double click):** Summons Spotlight quick search.
- **Right-Click:** Native Win32 context menu featuring:
  - *Open AeroSpot (`Alt + Space`)*
  - *Settings...*
  - *Check for updates...*
  - *Worker Status* (indicates whether UI worker is idle or active in memory)
  - *Exit AeroSpot* (clean and complete shutdown of all processes).

---

## 6. Installation, Update, and Uninstallation Guide

### Installed Edition (Recommended)
1. **Installation:** Download and run `AeroSpot-Setup-v<ver>.exe`. Installs per-user into `{localappdata}\Programs\AeroSpot` without requiring UAC administrator privileges. Creates Start Menu and Desktop shortcuts.
2. **Updating:** When an update is detected, click "Update". The updater terminates active processes and replaces program binaries while preserving your search database and preferences intact in `%LOCALAPPDATA%\AeroSpot\Data\`.
3. **Uninstallation:** From Windows *Installed Apps*, select AeroSpot and click *Uninstall*. The uninstaller cleans program files, shortcuts, autostart entries, and prompts whether to keep or remove the index database.

### Portable Edition
1. **Installation:** Download and extract `AeroSpot-v<ver>-win-x64-portable.zip` into any folder or USB drive. Run `AeroSpot.exe`.
2. **Updating:** Download the new portable ZIP, exit AeroSpot from tray, and overwrite existing files. Your local `Data\` subfolder is completely untouched.
3. **Uninstallation:** Run `Uninstall.exe` beside `AeroSpot.exe` for an automated, zero-residue cleanup.

---

## 7. Troubleshooting & FAQ

- **The shortcut `Alt + Space` does not respond:**  
  Verify if another utility (e.g. PowerToys Run or Listary) already occupies `Alt + Space`. AeroSpot detects the conflict, notifies you, and switches to safe fallbacks `Ctrl + Space` or `Alt + F12`. You can customize the shortcut at any time in Settings.
- **Emergency Unhook Key:**  
  If keyboard focus becomes trapped, pressing `F12` immediately forces the Host to unhook low-level keyboard listeners and restore Windows focus.
- **Viewing Diagnostic Logs:**  
  Log files retaining the last 24 hours of diagnostics for Host, Worker, and Tools are located in the `logs\` subfolder inside your active data directory.
