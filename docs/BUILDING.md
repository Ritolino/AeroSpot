# Building & Packaging AeroSpot

This document describes system requirements, build commands, packaging procedures, and runtime architecture for **AeroSpot**.

---

## 1. System Requirements for Development

- **Operating System:** Windows 10 (version 19041 or higher) or Windows 11 (23H2+ recommended).
- **Architecture:** `win-x64`.
- **Required SDK:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (verified with `10.0.402` or higher).
- **Inno Setup (optional):** Inno Setup 6.x (only required for compiling the `Setup.exe` installer).
- **Shell:** PowerShell 7 or Windows PowerShell 5.1.
- **Recommended IDE:** Visual Studio 2026 / Visual Studio Code with C# Dev Kit extension.

---

## 2. Quick Build and Testing

From the project root directory:

### Restore and Release Build
```powershell
# Restore NuGet packages
dotnet restore AeroSpot.sln

# Build solution in Release configuration
dotnet build -c Release AeroSpot.sln
```

### Running the Test Suite
```powershell
dotnet test -c Release AeroSpot.sln --no-restore
```
*All tests must pass with 0 errors and 0 warnings.*

---

## 3. Automated Packaging and Release

AeroSpot is distributed in **Self-Contained (ReadyToRun win-x64)** mode with a shared runtime across processes:
- Full .NET 10 runtime bundled within the application folder.
- **Zero prerequisites** required on the end-user machine.
- No administrator privileges required.
- **Shared Runtime:** `AeroSpot.Host.exe`, `AeroSpot.Worker.exe`, and `AeroSpot.Tools.exe` reside in the `app\` subfolder, sharing CoreCLR and WPF runtime binaries without duplicating disk space.

### Running the Official Build Script

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

### Script Parameters:
| Parameter | Default | Description |
|---|---|---|
| `-Configuration` | `Release` | Build configuration (`Release` or `Debug`). |
| `-Runtime` | `win-x64` | Target Windows 64-bit Runtime Identifier. |
| `-ReadyToRun` | `$true` | ReadyToRun AOT precompilation to eliminate initial JIT latency spikes. |
| `-OutDir` | `bin\Release\publish` | Final output directory for compiled binaries. |
| `-SkipTests` | `$false` | Skips unit test execution (not recommended). |
| `-CreateZip` | `$true` | Automatically compresses output into the portable release zip. |
| `-GenerateInstaller` | `$false` | Invokes Inno Setup `iscc.exe` to compile `Setup.exe`. |

### Local SSD Deployment (C:\Programmi_miei\AeroSpot)
To rapidly update the local standalone installation on the primary SSD while strictly preserving user database and preferences (`Data\`):
```powershell
powershell -ExecutionPolicy Bypass -File scripts\deploy-local.ps1
```
The script gracefully terminates running processes, compiles the latest release, synchronizes binaries with Robocopy excluding `Data\`, and restarts the application.

---

## 4. Benchmarking and ReadyToRun Rationale

Both publication approaches were measured on Windows 11 Build 26300 across 10 cold-start runs of the application (CoreCLR bootstrap + MainWindow/PreviewWindow XAML parsing):

| Metric | Without ReadyToRun (Pure JIT) | With ReadyToRun (AOT + JIT) | Difference |
|---|---|---|---|
| **Uncompressed Disk Space** | 166.64 MB | 197.20 MB | +30.56 MB |
| **ZIP Archive Size** | 67.66 MB | 76.33 MB | +8.67 MB |
| **Cold Start (Min)** | 1015 ms | 1013 ms | -2 ms |
| **Cold Start (Median)** | 1019 ms | 1015 ms | -4 ms |
| **Cold Start (Max / First Run)** | **4080 ms** | **1041 ms** | **-3039 ms (-74%)** |

**Conclusion:**
Without ReadyToRun, the initial run from disk incurs a JIT compile spike exceeding 4 seconds. With ReadyToRun enabled, launch latency drops to 1.04 seconds with near-zero standard deviation. The ~8.6 MB zip size increase is justified by the sub-second responsiveness expected of AeroSpot. The build script defaults to `ReadyToRun = $true`.

---

## 5. Portable Mode and Key Files

- **Portable Mode:** To run AeroSpot in fully isolated portable mode (e.g., USB drive or cloud-synced folder), place `portable.txt` in the root folder of the package. AeroSpot will store SQLite index, settings, and logs strictly in the local `Data\` subfolder.
- **Registry Cleanup:** Running `AeroSpot.Host.exe --cleanup` immediately removes startup and shell registry entries prior to directory deletion.
