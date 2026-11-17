# AeroSpot

[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue?logo=windows&logoColor=white)](https://github.com/Ritolino/AeroSpot)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20Self--Contained%20(ReadyToRun)-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF%20Fluent%20Dark-black)](https://github.com/Ritolino/AeroSpot)
[![Release](https://img.shields.io/github/v/release/Ritolino/AeroSpot?label=Release&color=orange)](https://github.com/Ritolino/AeroSpot/releases/latest)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

AeroSpot is an ultra-lightweight, privacy-first native desktop productivity suite for **Windows 11** (compatible with Windows 10 x64), combining the search speed of an instant **Spotlight-style launcher** with macOS-like **QuickLook file previews**, maintaining a resident background memory footprint strictly below **10 MB of Private Bytes**.

[**🇮🇹 Leggi il README in lingua italiana (README.it.md)**](README.it.md)

---

## ⬇️ Download

AeroSpot is distributed in two editions built from the exact same compiled binaries (.NET 10 ReadyToRun self-contained, requiring zero external runtime downloads):

| Edition | Recommended For | Key Characteristics | Download Link |
| :--- | :--- | :--- | :--- |
| **🪟 Setup Installer** *(Coming Soon)* | Everyday users | Per-user installation without UAC in `{localappdata}\Programs\AeroSpot`, Start Menu & Desktop shortcuts, automatic update support | *Coming Soon* |
| **📦 Portable Package (.zip)** | Developers, USB drives | No installation required: extract and run `AeroSpot.exe`. User data saved strictly in local `Data\` subfolder | [**Download AeroSpot v1.6.0 Portable (.zip)**](https://github.com/Ritolino/AeroSpot/releases/download/v1.6.0/AeroSpot-v1.6.0-win-x64-portable.zip) |

---

## ✨ Key Features

- **⚡ Ultra-Low Footprint Dual-Process Architecture**:
  - **AeroSpot Host**: Pure native Win32 background coordinator (< 10 MB Private Bytes at rest) managing global keyboard hooks and process orchestration.
  - **AeroSpot Worker**: High-performance WPF UI engine spawned on demand for search and preview tasks; automatically terminates after 30 seconds of inactivity to release all memory to Windows (or remains in *Keep-Warm* mode for sub-millisecond response).
  - **AeroSpot Tools**: Ephemeral satellite process for DirectML DirectX 12 GPU neural inference (AI background removal) with guaranteed 15-second autoshutdown.
- **🚀 Instant Spotlight Search (`Alt + Space`)**:
  - Global hotkey callable instantly from any application or full-screen window.
  - **Zero-Lag Smooth Typing**: Background task decoupling for UI icons and result mapping ensures buttery-smooth 60/144 fps typing with zero keystroke stalls.
  - **Category Quick Filters & Tab Cycling**: Centered filter pills underneath the search box (`All`, `Apps`, `Photos`, `Docs`, `Video`, `Audio`, `Folders`, `Settings`). Pressing `Tab` or `Shift+Tab` cycles immediately through filters without losing typing focus from the search input. Resets automatically to `All` upon closing Spotlight.
  - **Drag & Drop with Translucent Ghost Adorner**: Directly drag search result items onto Desktop, File Explorer folders, or chat apps (WhatsApp, Telegram, browser dropzones) with a sleek semi-transparent thumbnail card following the cursor.
  - **Quick Recycle Bin Deletion (`Del`)**: Press `Del` (or use context menu) to send selected items to Windows Recycle Bin with system confirmation dialog and instantaneous differential index removal.
  - **Built-in Calculator & Unit Converter**: Real-time evaluation of math formulas (`2+2`, `sqrt(144)`), percentage discount/markup breakdown with contextual subtitles (e.g. `600 - 60%` = `240`, *"Discount 60% (-360)"*), and instant conversion of volume, mass, length, and temperature.
  - **Context Menu on Every Result**: Right-click to reveal file location in File Explorer, copy path, preview, recycle, or trigger quick conversions (JPG, PNG, PDF for images and `.md`/`.txt`/`.docx` documents, MP4, MP3, WAV, AI background removal).
  - **3-Tab Settings & Customizable Result Hierarchy**: Clean *General*, *QuickLook*, and *Spot* settings tabs with Spotlight deep-linking, drag-and-drop category ordering, and an option to always prioritize exact name matches.
- **👁 Instant QuickLook Previews (`Space`)**:
  - Works natively inside **File Explorer**, on the **Desktop**, across Spotlight search results, and inside **Open / Save / Attach dialogs (`#32770`)** of external apps (WhatsApp, Chrome, Notepad, Discord) via UI Automation with zero DLL injection.
  - **DWM Cloaking Anti-Flicker & Contextual Layering**: Zero ghost frames, zero black flash, automatic foreground activation, manual **90° rotation (`⟳`)** with dynamic window aspect-ratio adaptation (no letterboxing), and an optional *Always on top* toggle.
  - **Zero-Office Native Word Viewer (`.docx`, `.docm`, `.dotx`)**: Blazing-fast (~5–15 ms) native OpenXML reader with Fluent typography, tables, and zero background `WINWORD.EXE` processes.
  - **Direct PDF Export**: Built-in **📄 PDF** toolbar button to instantly convert and export Markdown (`.md`), text (`.txt`), and Word documents (`.docx`) to PDF.
  - **Native Web & HTML Viewer**: Real browser rendering with full CSS, fonts, and images via isolated WebBrowser component with source code inspection toggle.
  - **High-Speed Native Google PDFium Engine**: Sub-50ms ultra-fast preview powered by the native Google PDFium C++ engine (`Docnet.Core`, QuickLook Store standard) on pure white paper with elegant drop shadow, **continuous vertical scrolling** with stacked pages, **left sidebar page thumbnail gallery** for rapid visual browsing and instant click-to-page jump with dynamic highlight, async lazy-loading with LRU cache (max 16 pages in RAM), left-click hand tool panning (`Cursors.SizeAll`), cursor-anchored `Ctrl + MouseWheel` zoom at **60 FPS with 1:1 reset button**, floating page capsule repositioned at bottom-left (`▲ 1 / 40 ▼`), keyboard shortcuts (`PageUp`/`PageDown`/`Home`/`End`), and zero external browser subprocesses spawned.
  - **Excel Spreadsheet Viewer**: Zero-dependency OpenXML reader (`.xlsx`, `.xlsm`, `.xltx`) supporting multi-sheet tabs and Dark Fluent data grid without requiring Office installed.
  - **Smooth Video Player with Transparent Remuxing**: Universal MP4, MKV, AVI, MOV, WMV, WEBM playback with timeline scrubber, instant seek without bouncing, automatic orientation rotation for vertical smartphone recordings, and an on-demand 60x FFmpeg remuxing pipeline (stereo AAC downmix, pristine video stream copy) for incompatible bitstream multichannel audio (AC-3, DTS, TrueHD) at VLC parity.
  - **Vintage 33 RPM Vinyl Audio Turntable**: Animated rotating record with center spindle hole, cinematic tonearm tracking grooves and retracting on pause/stop while freezing rotation angle.
  - **Interactive 3D STL & 3MF Viewer**: 60 FPS native 3D engine with orbit mouse rotation, wheel zoom, and triangle count.
  - **Web Pages & HTML**: Safe passive dark viewing for `.html` and `.htm` files.
  - **Text & Source Code**: Fast preview for 20+ extensions with monospace font, line count, and safe 100 KB truncation limit.
  - **QuickLook Context Menu**: Right-click anywhere in preview to access conversions, file actions, and window controls (Fullscreen F11, Lock size 📌).
- **🛠 Quick Conversions, Explorer Context Menu & Batch Tools**:
  - **Windows Explorer Multi-File Cascading Menu (`Shift + Right-click`)**: Merge multiple selected PDFs into one document (`PDFsharp`), combine multiple photos into a single multi-page PDF, or run smart format-aware batch conversions (JPG/PNG) with a floating multi-monitor progress toast.
  - **Convert to JPG / PNG**: Quality 95 JPEG with DPI-independent pixel-level white compositing for transparent PNG/BMP/WebP layers.
  - **Convert to PDF**: Single or multi-page ISO 1.4 PDF generated in native C#.
  - **Convert to MP4 / Extract MP3 & WAV**: Convert non-MP4 videos (MKV, AVI, MOV, WMV, WEBM) to compatible MP4 via FFmpeg or extract audio via Windows Media Foundation.
  - **Remove Background (AI)**: DirectX 12 neural segmentation via DirectML. Compact model (~44 MB) is downloaded on-demand only upon first use and stored locally.
  - **Automatic Folder Opening**: Automatically opens File Explorer and highlights the newly generated file upon conversion completion.
- **🔄 Auto-Updater & GitHub Release Checks**:
  - Automated or manual check for official releases via secure GitHub Releases HTTPS REST API.
  - Configurable frequencies: *Manual*, *On startup*, *Daily*, *Weekly*, *Monthly*.
  - Automated SHA-256 cryptographic verification against `SHA256SUMS.txt` prior to execution.
- **🌐 Dual-Language UI (Italiano & English)**:
  - Automatically matches Windows language on initial launch and supports live switching without restarting.

---

## ⌨️ Essential Keyboard Shortcuts

| Shortcut | Context | Action |
|---|---|---|
| **`Alt + Space`** | Anywhere | Toggles Spotlight search bar open or closed. |
| **`Space`** | File Explorer / Desktop | Opens QuickLook preview for the selected file or folder. |
| **`Space`** | In Search Results | Opens QuickLook preview while keeping Spotlight visible underneath. |
| **`Space`** | Inside QuickLook | Closes QuickLook preview and restores focus to Spotlight or Explorer. |
| **`Esc`** | Any AeroSpot window | Universal close key: closes both Spotlight and QuickLook simultaneously. |
| **`F11` / Double Click** | Inside QuickLook | Toggles full-screen preview. |
| **`Ctrl + Wheel`** | Inside PDF Preview | Zoom in / out anchored to mouse pointer (1:1 reset button). |
| **`Click + Drag`** | Inside PDF Preview | Move and pan the zoomed page freely with the hand tool. |
| **`Click on Thumbnail`** | Inside PDF Preview | Instantly jumps to the clicked page in the left sidebar. |
| **`F12`** | Emergency | Emergency unhook: immediately releases keyboard hooks and restores focus. |

*For complete shortcuts and detailed usage guidelines, refer to the [User Guide (Docs/INSTRUCTIONS.md)](Docs/INSTRUCTIONS.md).*

---

## 📁 Supported Preview Formats (Summary)

AeroSpot supports instant preview across dozens of formats categorized into:
- **Documents:** Vector multi-page PDFs (`.pdf`), OpenXML spreadsheets (`.xlsx`, `.xlsm`, `.xltx`), Office documents (`.docx`, `.pptx`, `.rtf`).
- **Media:** Video (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, etc.), Audio (`.mp3`, `.wav`, `.flac`, `.m4a`, `.aac`, `.ogg`, `.wma`), Raster and vector images (`.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.svg`), Camera RAW (`.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf` with instant embedded JPEG extraction), Vector graphics Adobe Illustrator (`.ai`, clean technical metadata card).
- **3D & Web:** Stereolithography models (`.stl`, `.3mf`), web pages (`.html`, `.htm`).
- **Code & Text:** Source files and configs (`.txt`, `.md`, `.py`, `.json`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.sql`, `.bat`, `.cpp`, `.ts`, `.js`, etc.).
- **System:** Directories (with asynchronous size and item count calculation), shortcuts (`.lnk`), and Store/UWP apps.

*For the canonical, comprehensive table of supported formats, refer to the [User Guide (Docs/INSTRUCTIONS.md)](Docs/INSTRUCTIONS.md).*

---

## 🔒 Privacy, Network, and "What AeroSpot Touches"

### Privacy and Network Activity
- **100% Local Operation:** All search, indexing, and preview rendering runs entirely on your local device. Zero open listening sockets, zero telemetry.
- **Protected History & Searches:** All opened item paths and search queries reside exclusively in the protected local SQLite database (`index.db`) on your computer. You can freely choose between 4 home display modes (including *Show nothing / blank* which disables all recording) or permanently purge all history at any time with the "Clear history" button. No search terms ever leave your machine.
- **Optional Network Access:**
  1. *Update Checker:* If enabled in Settings, queries the public HTTPS endpoint `https://api.github.com/repos/Ritolino/AeroSpot/releases/latest`. No user data is sent.
  2. *AI Model Download:* If you click "Remove background (AI)", downloads the compact model from a verified repository and saves it locally.

### File System and Windows Registry
- **Installed Edition:**
  - Binaries: `{localappdata}\Programs\AeroSpot\`
  - Index database and user preferences: `%LOCALAPPDATA%\AeroSpot\Data\`
  - Diagnostic logs (automated 24-hour retention): `%LOCALAPPDATA%\AeroSpot\logs\`
  - Windows Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (autostart) and optionally `HKCU\Software\Classes\SystemFileAssociations` (Shell context menu).
- **Portable Edition:**
  - Binaries and user data: Contained entirely inside the folder selected by the user (with a local `Data\` subfolder created via `portable.txt`).
  - Leaves no trace in `%LOCALAPPDATA%` except AI models or logs if `portable.txt` is read-only.
- **Complete Clean Removal:**
  The uninstaller of the installed edition or `Uninstall.exe` in the portable edition removes all files and registry keys cleanly.

---

## 🛡 SmartScreen and Antivirus Transparency Notice

AeroSpot is an independent open-source project. Because executables are not signed with an expensive commercial EV (Extended Validation) code signing certificate, Windows SmartScreen or third-party antivirus software may display a standard prompt on first run (e.g. *"Unknown Publisher"* or heuristic flags due to the low-level `WH_KEYBOARD_LL` keyboard hook).

- **Why is a keyboard hook needed?** To capture the `Spacebar` press in File Explorer or on the Desktop without interfering with typing or file renaming.
- **Integrity Check:** You can verify the cryptographic SHA-256 checksum of your downloaded package at any time against the official `SHA256SUMS.txt` file published with each release.
- **Open Code:** The entire source code is fully transparent, public, and auditable in this repository.

---

## 🛠 System Requirements & Verified Performance

| Requirement | Specification |
|---|---|
| **Operating System** | Windows 11 (64-bit) version 22H2 or higher (compatible with Windows 10 x64 build 19041+) |
| **Architecture** | x64 (Intel / AMD 64-bit) |
| **Required .NET Runtime** | **None** (.NET 10 ReadyToRun self-contained runtime bundled) |
| **Measured RAM Footprint** | Host at rest: **~7–23 MB Private Bytes**; Worker at rest (post-bonification): **3.8 – 9.5 MB Working Set**; Worker active/peak: **180–257 MB** (Native PDFium: Worker ~158 MB Priv, **0 MB** external subprocesses); Post-timeout: **0 MB** |
| **Measured CPU Utilization** | At rest: **0.00% – 0.05%**; Peak (60x real-time audio remux): **0.4% – 1.6%** |
| **Response Latency** | Worker warm start: **< 1 ms**; SQLite query latency: **2–4 ms**; Spotlight reset: **0 ms**; WinRT PDF geometry: **5–50 ms**; UI typing debounce: **50 ms** |

*For the complete technical measurement report and hardware test environment details, refer to [STATUS.md](STATUS.md).*

---

## 🚀 Building from Source

To build AeroSpot from source using the .NET 10 SDK:

```powershell
# Clone the repository
git clone https://github.com/Ritolino/AeroSpot.git
cd AeroSpot

# Restore packages and compile Release
dotnet restore AeroSpot.sln
dotnet build -c Release AeroSpot.sln

# Run the complete test suite
dotnet test -c Release AeroSpot.sln

# Generate official release packages (Portable zip and optional Setup)
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

For further details on build options and ReadyToRun benchmarks, refer to [Docs/BUILDING.md](Docs/BUILDING.md).

---

## 💡 About the Project

AeroSpot was conceived, designed, and tested by Gabriele Clemente ([@Ritolino](https://github.com/Ritolino)).

> **Development Note:** This application was developed in human-AI pair programming utilizing advanced AI models. Conceptualization, hands-on hardware testing, dual-process architecture, low-level Win32 hooks, UI styling, and functional validation are the result of direct development iterations on Windows hardware.
> Feedback, suggestions, and pull requests are always welcome!

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for complete details.

Copyright (c) 2026 Gabriele Clemente
