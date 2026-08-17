# NagelRepacks / gpack

[![Build and Test](https://img.shields.io/badge/build-passing-brightgreen.svg)](#)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

**NagelRepacks (`gpack`)** is a high-performance, streaming game packaging and installer suite built in .NET 10. It packages locally installed games into portable, verified repack installer bundles (`.gpack` manifests accompanied by chunked `.gpk` payload files and a standalone `Setup.exe` graphical installer).

---

## Architecture Overview

```text
                     .gpack manifest + payload chunks (.gpk)
                                   │
           ┌───────────────────────┼───────────────────────┐
           │                       │                       │
           ▼                       ▼                       ▼
      Setup.exe                gpack.exe                Playnite
    (WPF Installer)          (CLI / Headless)       (Plugin Integration)
```

- **`gpack.exe`**: The command-line packaging tool used to inspect directories, generate templates, build packages, verify integrity, extract payload data, or install packages silently.
- **`Setup.exe`**: The standalone, user-facing graphical installer frontend branded as **NagelRepacks**.
- **`.gpack` / `.gpk`**: Streaming chunked package format using Zstandard (`zstd`) compression with SHA-256 integrity verification.

---

## Features

- **Chunked Payload Architecture**: Splits packages into manageable `.gpk` chunks (e.g. 4 GB default or customizable split sizes) for smooth network transfers, external drive copying, and corruption isolation.
- **Streaming Zstandard Compression**: Supports multiple compression presets (`none`, `fast`, `balanced`, `maximum`) with bounded memory utilization.
- **SHA-256 Checksums**: Integrity verification on individual chunks and unpacked game files.
- **Atomic & Safe Installation**: Extracts files to a staging sandbox (`.gpack-install-{GUID}`) before committing to target destination, with rollback on cancellation or errors.
- **Rich Post-Install Actions**: Supports shortcuts (Desktop and Start Menu), copy/move/delete operations, custom commands, registry adjustments, and environment variable expansion (`%INSTALL_DIR%`, `%APPDATA%`, `%PROGRAMDATA%`, etc.).
- **Playnite Automation Ready**: Line-delimited JSON event streaming (`--json`) with standardized exit codes.

---

## Installation & Building

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher.
- Windows 10/11 (for WPF Setup.exe and Windows shortcut integration).

### Build Solution

```powershell
# Restore dependencies and build entire solution
dotnet build NagelRepacks.sln -c Release

# Run automated test suite
dotnet test NagelRepacks.sln -c Release
```

### Publish Release Artifacts

#### Option 1: Standalone Self-Contained Single-File (Portable / No .NET Required)
Builds 100% self-contained single `.exe` files for `gpack.exe` and `Setup.exe` that run on any 64-bit Windows PC without requiring .NET installed:
```powershell
# Using the automated build script:
.\build.ps1 -Standalone

# Or via dotnet CLI:
dotnet publish src/GPack.Cli/GPack.Cli.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --self-contained true -o artifacts/gpack
dotnet publish src/GPack.Installer.UI/GPack.Installer.UI.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --self-contained true -o artifacts/installer
```

#### Option 2: Framework-Dependent Binaries (Lightweight)
```powershell
.\build.ps1 -Standalone:$false
```

---

## Guided Interactive Wizard

The quickest and easiest way to package a game into a NagelRepacks installer is using the interactive wizard:

### Via PowerShell Script:
```powershell
.\New-GameRepack.ps1
```
*(Supports browsing for folders with GUI folder picker, auto-inspecting executables, and setting compression/split sizes step-by-step)*

### Via CLI Command:
```powershell
gpack wizard
# or with pre-selected directory:
gpack wizard "D:\Games\Cyberpunk 2077"
```

---

## CLI Usage

### 1. `gpack wizard`
Interactive step-by-step repack generator.

### 2. `gpack init`
Create a starter YAML package definition from an installed game directory:
```powershell
gpack init "D:\Games\Cyberpunk 2077"
```

### 2. `gpack inspect`
Analyze an installation directory, identify main executables, and find largest files or logs:
```powershell
gpack inspect "D:\Games\Cyberpunk 2077" --largest
```

### 3. `gpack build`
Build a `.gpack` package and installer from CLI flags or configuration files:
```powershell
# Via CLI arguments:
gpack build --source "D:\Games\Cyberpunk 2077" --name "Cyberpunk 2077" --version "2.3" --exe "bin\x64\Cyberpunk2077.exe" --output "D:\Repacks" --compression balanced

# Via YAML configuration:
gpack build cyberpunk.gpack.yaml

# With JSON streaming progress:
gpack build cyberpunk.gpack.yaml --json --non-interactive
```

### 4. `gpack info`
Inspect package manifest and payload statistics:
```powershell
gpack info "D:\Repacks\Cyberpunk-2077-NagelRepacks\Cyberpunk-2077.gpack"
```

### 5. `gpack verify`
Verify chunk hashes and package manifest integrity:
```powershell
gpack verify "D:\Repacks\Cyberpunk-2077-NagelRepacks\Cyberpunk-2077.gpack"
```

### 6. `gpack extract`
Directly decompress and extract package files to a target directory:
```powershell
gpack extract "D:\Repacks\Cyberpunk-2077-NagelRepacks\Cyberpunk-2077.gpack" --output "D:\TestOutput"
```

### 7. `gpack install`
Perform a full headless installation with shortcut creation and post-install actions:
```powershell
gpack install "D:\Repacks\Cyberpunk-2077-NagelRepacks\Cyberpunk-2077.gpack" --destination "D:\Games\Cyberpunk 2077"
```

---

## Configuration Schema (YAML / JSON)

```yaml
game:
  name: "Cyberpunk 2077"
  version: "2.3"

source:
  directory: "D:\\Games\\Cyberpunk 2077"
  include: []
  exclude:
    - "*.log"
    - "*.tmp"
    - "CrashDumps/**"

launch:
  executable: "bin\\x64\\Cyberpunk2077.exe"
  arguments: ""

package:
  compression: balanced # none | fast | balanced | maximum
  splitSize: 4GB        # 500MB | 2GB | 4GB | custom bytes

installer:
  brand: "NagelRepacks"
  defaultDirectory: "%GAMES%\\Cyberpunk 2077"
  desktopShortcut: true
  startMenuShortcut: true

actions:
  - type: shortcut
    target: "%INSTALL_DIR%\\bin\\x64\\Cyberpunk2077.exe"
    location: desktop
```

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `10` | Invalid CLI arguments |
| `11` | Invalid configuration |
| `12` | Source directory not found |
| `20` | Scan failed |
| `21` | File read failed |
| `22` | Hashing failed |
| `30` | Compression failed |
| `31` | Package creation failed |
| `40` | Installer generation failed |
| `50` | Verification failed |
| `60` | Installation failed |
| `90` | Operation cancelled |
| `99` | Unexpected error |

---

## License

This project is licensed under the [MIT License](LICENSE).
