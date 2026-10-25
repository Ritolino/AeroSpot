# AeroSpot User Guide

Welcome to **AeroSpot**, the modern Windows 11 application designed to combine the fast search experience of a *Spotlight*-style launcher with the instant preview capabilities of *QuickLook*, keeping resident memory consumption of the Host at rest strictly under 10 MB of Private Bytes.

---

## 1. Quick Start

AeroSpot is a native, portable Windows 11 (x64) application requiring zero installation and zero external dependencies:
- **Launching the app:** Double-click `AeroSpot.Host.exe` (or the `AeroSpot` shortcut). The app launches silently in the background and places its dark Fluent icon in the system tray at the bottom right corner (next to the Windows clock).
- **Clean exit:** Right-click the AeroSpot icon in the system tray -> **Exit AeroSpot**. All processes (Host and Worker) terminate cleanly and immediately.
- **Uninstallation / Registry cleanup:** If you enabled automatic startup with Windows and wish to remove the app, execute from Command Prompt or Terminal:
  ```cmd
  AeroSpot.Host.exe --cleanup
  ```
  This command immediately purges any autostart reference from the Windows Registry before you delete the application folder.

---

## 2. Using the Launcher (Quick Search)

### Keyboard Shortcut
- **Default shortcut:** `Alt + Space` (customizable in Settings to `Ctrl + Space`, `Win + Space`, or a custom combination).
- AeroSpot can be summoned instantly from any application or screen.

### How to Search
1. Press the shortcut: the fluid dark acrylic search bar appears centered at the top of the screen.
2. Type what you are looking for:
   - **Installed and system applications** (e.g. *Chrome*, *Notepad*, *Calculator*, *Terminal*, *VS Code*, *Steam*, UWP/Store apps like *WhatsApp*)
   - **Documents and files** (e.g. *report.docx*, *sheet.xlsx*, *project.py*, *image.png*, *video.mp4*)
   - **Folders** (e.g. *Downloads*, *Documents*, *Pictures*)
3. **Built-in Calculator:** Directly enter a mathematical expression (e.g. `2+2`, `15 * 4`, `sqrt(144)`, `sin(pi/2)`) to view the single evaluated result instantly.
4. **Unit Converter:** Convert units on the fly for length, weight/mass (grams, hectograms, kilograms, pounds, ounces), volume (liters, milliliters, centiliters, gallons), temperature (°C, °F, K), and currencies.
5. **Space works naturally:** When searching multi-word queries (e.g. *"Google Chrome"* or *"Shopping notes"*), pressing the spacebar types a normal space character without triggering file preview.

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
| **Right-Click on result** | Opens context menu: **📁 Open file location** (reveals file in File Explorer), **🚀 Open**, **👁 Preview**, **📋 Copy path**. |
| **Double Click** | Launches the file or application directly with the default application. |
| **Buttons 🔄 and ⚙** | Bottom bar: refresh icon 🔄 triggers background incremental reindexing, and gear icon ⚙ opens Settings. |
| **Clicking outside** | Automatically dismisses the launcher. |

---

## 3. Instant File Preview (QuickLook)

When in **File Explorer** (or on the **Desktop**):
1. Click on a file or select it using keyboard arrow keys.
2. Press the **Spacebar**: a window with the file's content preview opens instantly.
3. Press **Space** (or **Esc**) again to close the preview.

### Interactive Features in Preview:
- **Mouse Wheel Zoom (Images and PDFs):** Zoom in or out smoothly anchored directly at the mouse cursor.
- **Pan and Drag:** When an image or PDF document is zoomed in, freely drag it by holding down the left mouse button. Click **1:1** to reset to original scale.
- **Full Screen:** Press **F11**, **double-click** the photo/video, or click the **⛶** button at the top right to expand preview to full screen.
- **Window Memory:** The preview window remembers position and dimensions during the active session, with optional persistent per-category memory via the pin 📌 icon in the header.

### Supported Formats:
- **PDF Documents:** High-performance native vector multi-page viewer (`Windows.Data.Pdf`). Automatically adapts to actual page aspect ratio (landscape vs portrait), allows vertical scrolling across pages, fluid `Ctrl + Mouse Wheel` zoom (0.4x - 4.0x), pan with left mouse button, and `1:1` reset button.
- **Audio (.mp3, .wav, .m4a, .aac, .flac, .wma, .ogg, .mid, .midi, .aiff, .opus):** Native playback with animated 33 RPM spinning vinyl record with vintage center hole/label, tonearm tracking track progression and retracting to side on stop/pause without resetting rotation, interactive scrubber, and volume slider.
- **3D STL Models (.stl):** Native 60 FPS 3D rendering engine for ASCII and binary STL files. Orbit rotation via mouse dragging, mouse wheel zoom, and triangle/bounding box dimensions.
- **Videos (.mp4, .mov, .mkv, .avi, .wmv, .webm, .h264, .h265, .m4v, .flv, .3gp, .ts, .mpg, .mpeg):** Smooth playback with default volume at 50%, full duration formatting with hours (`hh:mm:ss` for > 1h), interactive click-to-seek timeline scrubber, and frameless vector controls.
- **Excel Spreadsheets (.xlsx, .xlsm, .xltx):** High-speed native OpenXML reader with Dark Fluent grid layout, sheet tabs for multi-sheet workbooks, and correct bidirectional horizontal/vertical scrollbars.
- **Text & Source Code:** .txt, .md, .py, .json, .log, .csv, .ini, .cs, .xml, .yaml, .toml, .sql, .bat, .cmd, .cpp, .c, .h, .rs, .go, .ts, .js with monospace font, automatic encoding detection, line count, and vertical mouse wheel scrolling.
- **Office Documents & RTF:** Word (.docx), PowerPoint (.pptx), Rich Text (.rtf) via registered system preview handlers (COM Preview Handlers).
- **Folders:** Immediate 0 ms display with full path at top, modification date in footer, and asynchronous calculation of total disk size, file count, and folder count (e.g. `1.45 GB (1,240 files, 48 folders)`).
- **Images:** PNG, JPG, JPEG, WebP, animated GIFs, BMP, high-resolution ICO with proportional zoom and pan.
- **Shortcuts (.lnk) & Applications:** Clean information card with path, size, and date without text clipping, and high-definition vector icons for Desktop and UWP/Store apps.
- **Other Files:** Descriptive card with metadata, format, modification date, and file size.

> **Typing Protection:** When renaming a file or typing in Windows Explorer's address bar or search box, pressing Space inputs a normal space character without opening preview.

---

## 4. Settings Panel

You can access Settings in two ways:
1. **From system tray:** Right-click the AeroSpot icon next to the clock -> **Settings...**
2. **From search bar:** Click the **⚙ Settings** button in the lower bar of Spotlight.

### Configurable Options:
1. **Interface Language:** Choose between `Automatic (system language)`, `Italiano`, or `English`. Language switches instantly across the entire interface without restart.
2. **Spotlight Shortcut:** Choose between `Alt + Space`, `Ctrl + Space`, `Win + Space`, or record a custom combination with real-time collision detection.
3. **QuickLook Shortcut:** Choose `Space`, `Shift + Space`, or record your preferred key.
4. **Indexed Drives:** Select drives to monitor via checkboxes (system drive and detected fixed drives enabled by default, e.g. `C:\`, `E:\`, `D:\`).
5. **Folder Exclusions:** Excludes heavy game directories (*SteamLibrary*, *Epic Games*), temp folders (*Temp*, *AppData\Local\Temp*), and developer build folders (*.git*, *node_modules*, *bin*, *obj*) by default. Easily add or remove custom paths.
6. **System & Performance:**
   - *Start with Windows:* Registers AeroSpot in Windows autostart with standard user privileges. Honors Task Manager status and auto-repairs path if the folder is moved.
   - *Worker Pre-spawn:* If both "Start with Windows" and "Keep worker warm" are enabled, AeroSpot automatically pre-spawns the Worker in the background at system boot for instant 0 ms response.
   - *Worker Auto-Shutdown:* Host consumes only **~8 MB RAM** at rest. When Worker spawns for a preview or search, it shuts down after 30 seconds of inactivity, returning all memory to Windows. A 10-second grace period is active between consecutive previews to eliminate micro-stutters.
7. **Data Directory & Storage:**
   - Displays real data directory path: `<AppDir>\Data\` if `portable.txt` is present, or `%LOCALAPPDATA%\AeroSpot\`.
   - Shows real-time disk space consumed by the search index database (`index.db`).
8. **Instructions & Documentation:**
   - Buttons to open the formatted offline HTML documentation in the default browser or visit the official GitHub repository.

---

## 5. System Tray Icon and Menu

Located at the bottom right, next to the Windows clock:
- **Left Click (or Double Click):** Opens or toggles the Spotlight search bar.
- **Right Click:** Context menu offering:
  - *Open AeroSpot (Alt + Space)*
  - *Settings...*
  - *Worker Status* (indicates whether UI engine is idle or active in RAM)
  - *Exit AeroSpot* (clean, complete shutdown of all processes).

---

## 6. Graphic Identity and Logo

Original design assets are located in `design/`:
- `design/logo.svg`: High-precision vector SVG with opposed triangles and diagonal cut.
- `design/app_icon.svg` and `app_icon.png`: Fluent dark squircle icon with cyan/white gradient.
- `design/app.ico`: Multi-resolution embedded application and tray icon.

---

## 7. Troubleshooting

- **Shortcut does not respond:**
  Check if another utility (e.g. Listary or PowerToys) claims `Alt + Space`. AeroSpot automatically falls back to `Ctrl + Space` or `Alt + F12` and displays a notification. You can also pick an alternative shortcut in Settings.
- **Emergency unblock hotkey:**
  Pressing `F12` immediately unhooks keyboard listeners and restores input focus in case of unexpected lockups.
- **Log files:**
  Detailed diagnostic logs are stored in the data directory (`<DataDir>\logs\host.log` and `worker.log`).
