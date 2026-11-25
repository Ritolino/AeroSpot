<p align="center">
  <img src="assets/app_icon.png" width="128" height="128" alt="AeroSpot Icon">
</p>

# AeroSpot

[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue?logo=windows&logoColor=white)](https://github.com/Ritolino/AeroSpot)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20ReadyToRun-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![Memory](https://img.shields.io/badge/Private%20Bytes-%3C%2010%20MB-brightgreen)](#system-requirements--verified-performance)
[![CPU Idle](https://img.shields.io/badge/CPU%20Idle-0.0%25-brightgreen)](#system-requirements--verified-performance)
[![Release](https://img.shields.io/github/v/release/Ritolino/AeroSpot?label=Release&color=orange)](https://github.com/Ritolino/AeroSpot/releases/latest)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

An **ultra-lightweight, zero-overhead, privacy-first** desktop productivity suite for **Windows 11 / Windows 10 x64**, combining an instant **Spotlight-style launcher (`Alt + Space`)** with macOS-like **QuickLook previews (`Space`)**. Designed as a unified, native alternative to heavy background launchers and multi-process previewers, maintaining a resident background memory footprint strictly below **10 MB of Private Bytes**.

[**🇮🇹 Leggi il README in lingua italiana (README.it.md)**](README.it.md)

---

## ⬇️ Download

AeroSpot is built as a Self-Contained ReadyToRun `.NET 10` package requiring zero external runtime downloads:

| Edition | Recommended For | Key Characteristics | Download Link |
| :--- | :--- | :--- | :--- |
| **🪟 Unified Setup (.exe)** *(Coming Soon)* | Everyday & Portable users | Guided wizard with choice between Standard Install (`{localappdata}\Programs\AeroSpot`) and Portable Mode, free folder selection, zero UAC, silent 1-click updates | *Coming Soon* |
| **📦 Portable Package (.zip)** | Current v1.6.2 release | Extract and run `AeroSpot.exe`. User data saved strictly in local `Data\` subfolder | [**Download AeroSpot v1.6.2 Portable (.zip)**](https://github.com/Ritolino/AeroSpot/releases/download/v1.6.2/AeroSpot-v1.6.2-win-x64-portable.zip) |

---

## ✨ Key Features

- **🔒 100% Private, Zero Telemetry & Zero Admin (No UAC):** All search queries, SQLite indexing, and file previews run strictly offline on your machine with zero listening sockets and zero analytics. Writes stay strictly inside your user profile (`HKCU`, `%LOCALAPPDATA%\AeroSpot`, or local `Data\` in portable mode). Network calls occur only on explicit user demand (GitHub Releases update check or optional AI model download).
- **⚡ Ultra-Lightweight (< 10 MB Private Bytes) & Zero CPU Idle (0.0%):**
  - **AeroSpot Host:** Pure native Win32 background coordinator (~7–8 MB Private Bytes at rest, 0.00%–0.05% CPU, zero WPF/WinForms dependencies) managing global keyboard hooks and tray coordination.
  - **AeroSpot Worker:** High-performance WPF UI engine spawned on demand for search and previews; automatically exits after 30s idle (or shrinks to ~3.8–9.5 MB Working Set via `EmptyWorkingSet` in *Keep-Warm* mode).
  - **AeroSpot Tools:** Ephemeral satellite process for DirectML DirectX 12 GPU AI background removal and Explorer multi-file batch tools (0 MB at rest).
- **🚀 Instant Spotlight Search (`Alt + Space`):**
  - Global hotkey callable instantly from any application or full-screen window.
  - **Zero-Lag Smooth Typing & Two-Phase SQLite Search:** Async UI icon loading and two-phase SQLite queries (`Title > FullPath` with customizable drag-and-drop category hierarchy) ensure buttery-smooth typing with zero keystroke stalls.
  - **Category Quick Filters & Tab Cycling:** Centered filter pills (`All`, `Apps`, `Photos`, `Docs`, `Video`, `Audio`, `Folders`, `Settings`) cycled via `Tab` / `Shift+Tab` without losing input focus.
  - **Drag & Drop with Translucent Ghost Adorner:** Drag search results onto Desktop, File Explorer, or chat apps (WhatsApp, Telegram, browser) with configurable default behavior (*Windows Default*, *Always Copy*, *Always Move*).
  - **Quick Recycle Bin (`Del`), Calculator & Unit Converter:** Instant deletion confirmation with differential index purge, commercial discount breakdown (`600 - 60% = 240`), and live unit conversion.
- **👁 Instant QuickLook Previews (`Space`):**
  - Works natively inside **File Explorer** (with Windows 11 multi-tab active tab resolution), on the **Desktop**, in Spotlight results, and inside **Open / Save / Attach dialogs (`#32770`)** of external apps via UI Automation with zero DLL injection.
  - **DWM Cloaking Anti-Flicker & Contextual Layering:** Zero ghost frames, arrow-key folder navigation matching Explorer sort order, manual **90° rotation (`⟳`)** with dynamic window aspect-ratio adaptation, universal **`R`** view/zoom reset, and optional *Always on top*.
  - **High-Speed Native Google PDFium Engine (`Docnet.Core`):** Sub-50ms PDF preview with continuous vertical scroll, left thumbnail sidebar, 60 FPS cursor-anchored zoom, hand panning when zoomed, and **0 MB** external browser subprocesses (no `msedgewebview2.exe`).
  - **Zero-Office Native Document & Spreadsheet Viewers:** Native ~5–15 ms readers for `.docx`/`.docm`/`.dotx`/`.odt`, `.rtf`, `.xlsx`/`.xlsm`/`.xltx`, and `.html`/`.htm` with 1-click PDF export and zero zombie `WINWORD.EXE` processes.
  - **Smooth Video Player, 33 RPM Vinyl Audio & Interactive 3D Viewer:** Universal video playback with 60x transparent FFmpeg stereo AAC remuxing for AC-3/DTS/TrueHD, animated vinyl turntable for audio, interactive 60 FPS 3D `.stl`/`.3mf` viewer, Camera RAW embedded JPEG extraction, and SVG vector rendering.
- **🛠 Explorer Batch Tools (`Shift + Right-click`) & AI Tools:** Merge multiple PDFs (`PDFsharp`), combine images into a multi-page PDF, batch convert JPG/PNG/MP4/MP3/WAV, and remove image backgrounds offline on GPU DirectX 12 (`RMBG-1.4` / `u2netp` fallback).
- **🔄 1-Click Auto-Updater & Dual-Language UI (IT / EN):** GitHub Releases HTTPS checks with SHA-256 verification against `SHA256SUMS.txt` and live IT/EN language switching without restarting.

---

## 📊 AeroSpot vs. Other Alternatives

| Feature / Metric | 🪟 **AeroSpot** *(Unified Suite)* | **Everything + QuickLook** | **Flow Launcher + QuickLook** | **PowerToys Run + QuickLook** |
| :--- | :--- | :--- | :--- | :--- |
| **Idle Resident RAM** | **~7–8 MB Private Bytes** | ~35 – 65 MB *(combined)* | ~80 – 170 MB *(combined)* | ~130 – 250+ MB *(combined)* |
| **Relative Memory Overhead** | **Baseline (0%)** | **+350% to +700%** *(4× to 8×)* | **+900% to +2,000%** *(10× to 21×)* | **+1,500% to +3,000%+** *(16× to 30×+)* |
| **Idle CPU Usage** | **0.00% – 0.05%** | ~0.0% – 0.2% | ~0.1% – 0.5% | ~0.2% – 1.0% |
| **PDF / Word / HTML Subprocesses** | **0 (Native In-Process)** | WebView / Office COM spawns | Separate QuickLook + COM | Separate QuickLook + COM |
| **Unified Launcher + Preview + Tools** | **Single lightweight app** | Requires 2+ separate tools | Requires 2+ separate tools | Requires 2+ separate tools |
| **Admin Privileges Required** | **No** *(Standard User)* | Often Yes (USN Service) | No | Optional |

---

## ⌨️ Essential Keyboard Shortcuts

| Shortcut | Context | Action |
|---|---|---|
| **`Alt + Space`** | Anywhere | Toggles Spotlight search bar open or closed. |
| **`Space`** | File Explorer / Desktop / Dialogs | Opens QuickLook preview for the selected file or folder. |
| **`Space`** | In Search Results | Opens QuickLook preview while keeping Spotlight visible underneath. |
| **`Space`** | Inside QuickLook | Closes QuickLook preview and restores focus to Spotlight or Explorer. |
| **`Esc`** | Any AeroSpot window | Universal close key: closes both Spotlight and QuickLook simultaneously. |
| **`Tab` / `Shift + Tab`** | Inside Spotlight | Cycles through category filter pills without losing search input focus. |
| **`Del`** | Inside Spotlight | Sends the selected item to Windows Recycle Bin with confirmation. |
| **`Left` / `Right` / `Up` / `Down`** | QuickLook / Spotlight | Navigates Explorer/Desktop selection (in exact sort order) or search results while updating QuickLook instantly. |
| **`R`** | Inside QuickLook | Universal reset: resets camera view or 1:1 zoom across 3D models, photos, PDFs, and PreviewHandlers. |
| **`F11` / Double Click** | Inside QuickLook | Toggles full-screen preview. |
| **`PageUp` / `PageDown` / `Home` / `End`** | Inside PDF Preview | Navigates pages or jumps to first/last page. |
| **`Ctrl + Wheel`** | Inside PDF / Image Preview | Zoom in / out anchored to mouse pointer (1:1 reset button). |
| **`Click + Drag`** | Inside PDF / Image Preview | Move and pan the zoomed view freely with the hand tool (only when zoomed). |
| **`Click on Thumbnail`** | Inside PDF Preview | Instantly jumps to the clicked page in the left sidebar. |

*For complete shortcuts and detailed usage guidelines, refer to the [User Guide (Docs/INSTRUCTIONS.md)](Docs/INSTRUCTIONS.md).*

---

## 📁 Supported Preview Formats (Summary)

- **Documents:** Vector multi-page PDFs (`.pdf`), OpenXML spreadsheets (`.xlsx`, `.xlsm`, `.xltx`), native zero-Office documents (`.docx`, `.docm`, `.dotx`, `.rtf`, `.odt`; legacy `.doc`, `.dot`, `.xls`, `.ppt`, `.pptx` display a safe metadata card without spawning Office COM).
- **Media:** Video (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, etc.), Audio (`.mp3`, `.wav`, `.flac`, `.m4a`, `.aac`, `.ogg`, `.wma`), Raster and vector images (`.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.svg`), Camera RAW (`.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf`), Adobe Illustrator (`.ai` safe metadata card).
- **3D & Web:** Stereolithography and 3D Manufacturing models (`.stl`, `.3mf`), web pages (`.html`, `.htm`, `.xhtml`).
- **Code & Text:** Source files and configs (`.txt`, `.md`, `.py`, `.json`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.sql`, `.bat`, `.cpp`, `.ts`, `.js`, etc.).
- **System:** Directories (async size and item count), executables (`.exe`), `.lnk` shortcuts (in QuickLook), and Store/UWP apps.

*For the canonical table of supported formats, refer to the [User Guide (Docs/INSTRUCTIONS.md)](Docs/INSTRUCTIONS.md).*

---

## ❓ Frequently Asked Questions (FAQ)

- **What makes AeroSpot different from PowerToys Run, Everything, or standalone QuickLook?**  
  AeroSpot unifies a Spotlight launcher, a multi-format QuickLook previewer, and Explorer batch conversion/AI tools into a single architecture built around a pure Win32 host (< 10 MB RAM) and on-demand WPF rendering, avoiding heavy background runtimes and external browser/Office COM processes.
- **How does AeroSpot keep idle RAM below 10 MB?**  
  Heavy UI frameworks (`WPF`), Google PDFium, and DirectML AI models never load inside the resident `AeroSpot.Host.exe` process. They live in `AeroSpot.Worker.exe` and `AeroSpot.Tools.exe`, which trim memory with `EmptyWorkingSet` or unload completely after inactivity.
- **Why does AeroSpot not spawn `msedgewebview2.exe` or zombie `WINWORD.EXE` processes?**  
  PDFs are rendered natively in-process with Google PDFium (`Docnet.Core`), `.html` uses GPU-accelerated `FlowDocument`, and `.docx`/`.rtf`/`.odt`/`.xlsx` use native in-memory C# parsers with Office COM Preview Handlers permanently blocked by CLSID.
- **Does AeroSpot require administrator privileges or modify system files?**  
  No. AeroSpot runs entirely under standard user permissions without UAC prompts. All optional integrations (autostart and Explorer context menu) stay strictly inside `HKCU`, and user data stays in `%LOCALAPPDATA%\AeroSpot` (or a local `Data\` folder in portable mode).

---

## 🔒 Privacy, SmartScreen & What AeroSpot Touches

- **Standard Edition:** Binaries in `{localappdata}\Programs\AeroSpot\` (or user-selected path); SQLite index, preferences, AI models, and 24-hour rotating logs in `%LOCALAPPDATA%\AeroSpot\`; optional `HKCU` Registry keys for autostart and Explorer context menu.
- **Portable Edition:** All binaries and `Data\` reside strictly inside your chosen folder (`portable.txt`).
- **SmartScreen & Antivirus Transparency:** Because AeroSpot is an independent open-source project without a paid commercial EV signing certificate, Windows SmartScreen or heuristic AV scanners may show a standard prompt on first launch (due to the `WH_KEYBOARD_LL` keyboard hook needed for `Space` previews). Every release publishes a verifiable `SHA256SUMS.txt` checksum and full auditable source code.

---

## 🛠 System Requirements & Verified Performance

| Requirement | Specification |
|---|---|
| **Operating System** | Windows 11 (64-bit) 22H2+ (compatible with Windows 10 x64 build 19041+) |
| **Architecture** | x64 (Intel / AMD 64-bit) |
| **Required .NET Runtime** | **None** (.NET 10 ReadyToRun self-contained runtime bundled) |
| **Measured RAM Footprint** | Host at rest: **~7–8 MB Private Bytes** (~28–35 MB WS); Worker at rest (post-trim): **3.8 – 9.5 MB Working Set**; Worker active: **150–255 MB**; Tools at rest: **0 MB** |
| **Measured CPU Utilization** | At rest: **0.00% – 0.05%**; Peak (60x real-time audio remux): **0.4% – 1.6%** |
| **Response Latency** | Worker warm start: **< 1 ms**; SQLite query: **2–4 ms**; Spotlight reset: **0 ms**; Native PDFium render: **~12–15 ms/page** (open **< 50 ms**) |

---

## 🚀 Building from Source

```powershell
git clone https://github.com/Ritolino/AeroSpot.git
cd AeroSpot
dotnet restore AeroSpot.sln
dotnet build -c Release AeroSpot.sln
dotnet test -c Release AeroSpot.sln
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

For full instructions and benchmarks, see [Docs/BUILDING.md](Docs/BUILDING.md).

---

## 💡 About this Project & Disclaimer

This project was conceived, designed, and tested by [@Ritolino](https://github.com/Ritolino).

> **Full Disclosure:** I am not a professional software engineer or UI/UX designer — I'm a passionate tech enthusiast! This application was architected, written, and refined through human-AI pair programming using Google Antigravity powered by Gemini.
>
> My primary contribution has been the initial concept, iterative testing, UI/UX design, feature scoping, and real-world verification on Windows hardware (including the tri-process Win32/WPF/Tools architecture, low-level hooks, native PDFium/OpenXML/RTF viewers, and complete bilingual localization).
>
> Because of this, suggestions, code reviews, and Pull Requests to improve the codebase, optimize dependencies, or enhance software architecture are warmly welcomed and appreciated!

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for complete details.

Copyright (c) 2026 Ritolino
