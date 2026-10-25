# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-10-25

### Added
- **Spotlight Search Launcher (`Alt + Space`):** Fluid, dark acrylic search bar summoned instantly anywhere in Windows 11.
- **SQLite High-Speed Index:** Persistent multi-threaded search index (`index.db`) with sub-millisecond query execution, asynchronous crawler, and smart ranking.
- **QuickLook File Previewer (`Space`):** Instant preview on File Explorer, Desktop, or directly from search results.
- **Native Vector PDF Viewer:** Multi-page rendering powered by `Windows.Data.Pdf`, dynamic page aspect ratio adaptation, mouse wheel scrolling, `Ctrl + Wheel` smooth zoom (0.4x - 4.0x), reset 1:1, and left mouse button pan.
- **Native Video Player:** Smooth playback for MP4, MKV, AVI, MOV, WMV, WEBM, H.264, H.265, FLV, and TS. Supports video durations over 1 hour (`hh:mm:ss`), click-to-seek timeline scrubber, and frameless vector transport controls.
- **Native Audio Player:** Playback for MP3, WAV, M4A, AAC, FLAC, WMA, OGG, and MIDI with a vintage 33 RPM spinning vinyl record, center spindle hole, tonearm tracking elapsed track time and retracting to side on stop/pause without resetting rotation angle.
- **Interactive 3D STL Viewer:** 60 FPS native rendering engine for ASCII and binary STL models with orbital mouse rotation, wheel zoom, and triangle count metadata.
- **Native Excel Spreadsheet Viewer:** OpenXML reader for `.xlsx`, `.xlsm`, and `.xltx` files with sheet tab navigation, column/row headers, and dark fluid scrollbars.
- **Source Code & Text Viewer:** Fast preview for 20+ extensions (.txt, .md, .py, .json, .csv, .cs, .xml, .yaml, .sql, etc.) with monospace typography, automatic encoding detection, and line count.
- **Shell Preview Handler Integration:** Native COM hosting for Word (.docx), PowerPoint (.pptx), and Rich Text (.rtf) documents.
- **Folder Metadata Card:** Instant path display, modification date, and asynchronous total disk size, file count, and folder count calculation.
- **In-Line Calculator & Unit Converter:** Real-time evaluation of math expressions (`sqrt`, trigonometric, arithmetic) and instant conversion of volume (liters, milliliters, gallons), weight/mass (grams, kilograms, ounces, pounds), length, temperature, and currency.
- **Dual-Process Resident Architecture:** Ultra-lightweight Win32 Host (< 10 MB Private Bytes at rest) and on-demand WPF Worker with configurable idle auto-shutdown (30s default) and 10-second grace period.
- **Unified Escape Key (Esc):** Universal unchangeable key that closes both Spotlight and QuickLook simultaneously; `Alt + Space` toggles Spotlight; `Space` toggles QuickLook.
- **Pre-Spawn on Startup:** If both "Start with Windows" and "Keep worker warm" are enabled, Worker is automatically pre-spawned in background for immediate 0 ms response.
- **Bilingual Interface (English & Italian):** Instant runtime language switching with zero restart required.
- **Portable Mode & Autostart Management:** Fully self-contained portable mode via `portable.txt` in `<AppDir>\Data\` and clean Windows autostart with `--cleanup` CLI argument.
