# AeroSpot

[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue?logo=windows&logoColor=white)](https://github.com/Ritolino/AeroSpot)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20Self--Contained%20(ReadyToRun)-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF%20Fluent%20Dark-black)](https://github.com/Ritolino/AeroSpot)
[![Release](https://img.shields.io/badge/Release-v1.0.0-orange)](https://github.com/Ritolino/AeroSpot/releases/tag/v1.0.0)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

A native, ultra-lightweight, privacy-first productivity suite for **Windows 11** combining an instant **Spotlight-style search launcher** with macOS-like **QuickLook file previews**, maintaining a resident background memory footprint under **10 MB of RAM**.

[**🇮🇹 Leggi il README in lingua italiana (README.it.md)**](README.it.md)

---

### ⬇️ Download Pre-built Portable App (No Installation Required)

| Platform | Package Format | Architecture | Download Link |
| :--- | :--- | :--- | :--- |
| **🪟 Windows 11** | Portable Standalone (.zip) | x64 (Intel / AMD) | [**Download AeroSpot v1.0.0 (.zip)**](https://github.com/Ritolino/AeroSpot/releases/download/v1.0.0/AeroSpot-v1.0.0-win-x64-portable.zip) |

*Simply download the `.zip`, extract it to any folder on your PC, and launch `AeroSpot.exe`.*

---

## ✨ Features

- **🔒 100% Offline & Zero Telemetry**: All searching, indexing, and preview rendering happens strictly on your local computer. Zero internet sockets, zero analytics, zero cloud dependencies.
- **⚡ Ultra-Lightweight Dual-Process Architecture**:
  - **AeroSpot Host**: Pure native Win32 background coordinator consuming strictly **< 10 MB Private Bytes** at rest.
  - **AeroSpot Worker**: High-performance WPF engine spawned only on demand, automatically returning all memory to Windows after 30 seconds of inactivity (with a 10-second grace period between consecutive previews).
- **🚀 Instant Spotlight Search (`Alt + Space`)**:
  - Global hotkey callable from any full-screen app, game, or desktop window.
  - Sub-millisecond SQLite search engine (`index.db`) indexing applications, system tools, user documents, and directories.
  - **In-Line Calculator**: Evaluate math formulas on the fly (`2+2`, `sqrt(144)`, `sin(pi/2)`) with clean results.
  - **Real-Time Unit Converter**: Instant conversions for volume (liters, milliliters, gallons), weight/mass (grams, kilograms, pounds, ounces), length, temperature, and currency.
- **👁 High-Performance QuickLook Previews (`Space`)**:
  - Works natively inside **File Explorer**, on the **Desktop**, or directly across **Search Results**.
  - **Vector Multi-Page PDF Viewer**: Native high-DPI rendering via `Windows.Data.Pdf` with dynamic aspect ratio adaptation, mouse wheel scrolling, `Ctrl + Wheel` smooth zoom (0.4x - 4.0x), reset 1:1, and mouse drag pan.
  - **Native Video Player**: Smooth playback for MP4, MKV, AVI, MOV, WMV, WEBM, FLV, and TS. Supports durations over 1 hour (`hh:mm:ss`), click-to-seek timeline scrubber, frameless vector transport controls, and default 50% volume.
  - **Vintage Vinyl Audio Player**: Interactive playback for MP3, WAV, M4A, FLAC, AAC, WMA, and OGG with a 33 RPM spinning vinyl record, center hole, tonearm tracking elapsed track time and retracting to side on stop/pause without resetting rotation angle.
  - **Interactive 3D STL Viewer**: 60 FPS native rendering for ASCII and binary STL models with orbital mouse dragging, mouse wheel zoom, and triangle count metadata.
  - **Native Excel Spreadsheet Viewer**: High-speed OpenXML reader for `.xlsx`, `.xlsm`, and `.xltx` with sheet tabs and dark fluid scrollbars.
  - **Text & Source Code Viewer**: Fast preview for 20+ extensions (.txt, .md, .py, .json, .csv, .cs, .xml, .yaml, .sql, etc.) with monospace typography, automatic encoding detection, and line count.
  - **Shell Preview Handlers**: COM integration for Word (.docx), PowerPoint (.pptx), and Rich Text (.rtf) files.
  - **Folder Metadata Card**: Instant path display, modification date, and asynchronous total disk size, file count, and folder count calculation.
- **🌐 Dual-Language UI (English & Italiano)**:
  - Automatically matches system language on initial launch.
  - Live runtime language switching from Settings without requiring app restarts.
- **🛡 True Portable Mode & Clean Autostart**:
  - Supports USB drive portability via `portable.txt` storing all state inside `<AppDir>\Data\`.
  - Windows autostart configuration with registry self-repair and complete uninstallation support via `AeroSpot.Host.exe --cleanup`.

---

## ⌨️ Essential Keyboard Shortcuts

| Shortcut | Context | Action |
|---|---|---|
| **`Alt + Space`** | Anywhere | Toggles the Spotlight search bar open or closed. |
| **`Space`** | File Explorer / Desktop | Opens QuickLook preview for the selected file or folder. |
| **`Space`** | In Search Results | Opens QuickLook preview while keeping Spotlight open underneath. |
| **`Space`** | Inside QuickLook | Closes QuickLook preview and restores focus to Spotlight or Explorer. |
| **`Esc`** | Any AeroSpot window | Universal close key: closes both Spotlight and QuickLook simultaneously. |
| **`F11` / Double Click** | Inside QuickLook | Toggles distraction-free full-screen preview. |
| **`Ctrl + Wheel`** | Inside PDF Preview | Zooms smoothly (0.4x to 4.0x) anchored at the mouse cursor. |
| **`F12`** | Emergency | Emergency unblock hotkey: immediately unhooks keyboard listeners. |

*All shortcuts can be customized in the Settings panel (`⚙`).*

---

## 🎧 Supported Preview Formats

| Category | Supported Extensions | Technology |
|---|---|---|
| **PDF Documents** | `.pdf` | Native Windows.Data.Pdf vector engine with multi-page scroll & zoom |
| **Spreadsheets** | `.xlsx`, `.xlsm`, `.xltx` | Native OpenXML engine with multi-sheet tab navigation |
| **Video** | `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, `.h264`, `.h265`, `.m4v`, `.flv`, `.3gp`, `.ts`, `.mpg` | Native Windows Media Engine with click-to-seek timeline & >1h format |
| **Audio** | `.mp3`, `.wav`, `.m4a`, `.aac`, `.flac`, `.wma`, `.ogg`, `.mid`, `.midi`, `.aiff`, `.opus` | Native Audio Engine with 33 RPM spinning vinyl visualizer & tonearm |
| **3D Models** | `.stl` (binary & ASCII) | Native 3D Viewport engine with 60 FPS orbit rotation & zoom |
| **Images** | `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.svg` | High-DPI raster and vector loader with mouse-anchored zoom & pan |
| **Code & Text** | `.txt`, `.md`, `.py`, `.json`, `.csv`, `.ini`, `.cs`, `.xml`, `.html`, `.yaml`, `.toml`, `.sql`, `.bat`, etc. | Monospace editor with auto-detected encoding & line counting |
| **Office Documents** | `.docx`, `.pptx`, `.rtf` | Windows COM Shell Preview Handlers (out-of-process isolation) |
| **Folders** | Directories | Asynchronous recursive size calculation (size, file count, folder count) |
| **Applications** | `.exe`, `.lnk`, `shell:AppsFolder` (UWP/Store) | High-definition Shell icon extraction & application launcher |

---

## 🛠 System Requirements

| Requirement | Specification |
|---|---|
| **Operating System** | Windows 11 (64-bit) version 22H2, 23H2, 24H2 or newer (Windows 10 19041+ supported) |
| **Architecture** | x64 (Intel 64-bit / AMD64) |
| **.NET Runtime** | **None required** — Pre-packaged self-contained runtime with ReadyToRun AOT |
| **Disk Space** | ~75 MB download / ~195 MB extracted |
| **RAM** | Host: ~8 MB at rest; Worker: ~150 MB active |

---

## 🛡 SmartScreen and Antivirus Transparency Notice

AeroSpot is an independent open-source project. Because standalone executable releases are not signed with an expensive commercial Extended Validation (EV) certificate, Windows Defender SmartScreen or third-party antivirus software may display a warning on your first run (e.g., *"Unknown Publisher"* or heuristic warning regarding global keyboard hooks).

- **Why does AeroSpot use a keyboard hook?** To detect when you press `Space` in File Explorer or on the Desktop without conflicting with normal typing or file renaming.
- **Verification:** You can verify the integrity of the downloaded zip file against the published `SHA256SUMS.txt` hash.
- **Transparency:** The complete source code is 100% open and auditable right here on GitHub.

---

## 🚀 Building from Source

To compile AeroSpot from source using the .NET 10 SDK:

```powershell
# Clone the repository
git clone https://github.com/Ritolino/AeroSpot.git
cd AeroSpot

# Restore dependencies & compile Release
dotnet restore AeroSpot.sln
dotnet build -c Release AeroSpot.sln

# Run the complete test suite (100 tests)
dotnet test -c Release AeroSpot.sln

# Build self-contained portable package
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

For complete compilation architecture and benchmarks, consult [docs/BUILDING.md](docs/BUILDING.md).

---

💡 About this Project & Disclaimer

This project was conceived, designed, and tested by Gabriele Clemente (@Ritolino).

>**Full Disclosure:** I am not a professional software engineer or UI/UX designer — I'm a passionate tech enthusiast! This application was architected, written, and refined through human-AI pair programming using Google Antigravity powered by Gemini 3.8.
>
>My primary contribution has been the initial concept, iterative testing, UI/UX design, feature scoping, and real-world verification on Windows hardware (including multi-process architecture, low-level Win32 hooks, DirectShow & MediaElement audio/video playback, SharpDX DirectX 11 >3D rendering, SQLite full-text search indexing, and complete bilingual localization).
>
>Because of this, suggestions, code reviews, and Pull Requests to improve the codebase, optimize dependencies, or enhance software architecture are warmly welcomed and appreciated!

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

Copyright (c) 2026 Gabriele Clemente
