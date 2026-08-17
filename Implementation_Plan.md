# NagelRepacks / gpack — Implementation Plan

## 1. Project Goal

Create a standalone executable CLI application named **`gpack`** that can package an already-installed local game into a portable installer package.

The generated installer should:

- Restore the packaged game files to a chosen destination.
- Verify package integrity.
- Support optional shortcuts and post-install actions.
- Be independent from `gpack.exe`.
- Use **NagelRepacks** as the installer-facing brand.
- Keep the installer UI implementation flexible so its final visual design can be decided later.
- Be suitable for future integration into a **Playnite plugin**.

The project is a deployment/packaging tool. It should not modify executables to bypass DRM, licensing, or launcher requirements.

---

# 2. Naming

## User-facing brand

**NagelRepacks**

Used for:

- Generated installer branding.
- Installer window title.
- Optional installer metadata.
- Generated folder / installer naming.

Example:

```text
Cyberpunk-2077-NagelRepacks/
├── Setup.exe
├── Cyberpunk-2077.gpack
└── data/
```

## CLI

```text
gpack.exe
```

Example:

```powershell
gpack build --source "D:\Games\Game" --name "My Game"
```

## Package format

Primary package:

```text
.gpack
```

Payload chunks:

```text
.gpk
```

Suggested distinction:

```text
.gpack = package manifest/container entry point
.gpk   = compressed payload chunk
```

---

# 3. Core Design Principle

The project should treat the **package format as the primary product** and the graphical installer as one frontend for it.

```text
                    .gpack
                       │
          ┌────────────┼────────────┐
          │            │            │
          ▼            ▼            ▼
      Setup.exe     gpack.exe     Playnite
       GUI            CLI          Plugin
     installer      installer     integration
```

This allows the same package to be:

- Installed through the generated `Setup.exe`.
- Inspected or extracted through the CLI.
- Installed through a future Playnite plugin.
- Verified independently from the graphical installer.

---

# 4. Recommended Technology Stack

## Language

**C# / .NET 10**

Reasons:

- Excellent Windows filesystem support.
- Single-file executable publishing.
- Strong async I/O support.
- Good process execution APIs.
- Windows shortcut / registry access.
- Shared code between CLI and installer runtime.
- Easy Playnite integration later.

## Installer UI

Recommended starting point:

**WPF**

Keep UI code separate from installation logic.

Possible future alternatives:

- WinUI 3
- Avalonia
- Custom native frontend

The installer engine must not depend on the chosen UI framework.

---

# 5. Proposed Solution Structure

```text
NagelRepacks/
│
├── src/
│   ├── GPack.Cli/
│   │   ├── Commands/
│   │   │   ├── BuildCommand.cs
│   │   │   ├── InitCommand.cs
│   │   │   ├── InspectCommand.cs
│   │   │   ├── VerifyCommand.cs
│   │   │   ├── ExtractCommand.cs
│   │   │   ├── InstallCommand.cs
│   │   │   └── InfoCommand.cs
│   │   └── Program.cs
│   │
│   ├── GPack.Core/
│   │   ├── Configuration/
│   │   ├── Manifest/
│   │   ├── Models/
│   │   ├── Files/
│   │   ├── Hashing/
│   │   ├── Compression/
│   │   ├── Packaging/
│   │   └── Validation/
│   │
│   ├── GPack.Builder/
│   │   ├── BuildEngine.cs
│   │   ├── PackageBuilder.cs
│   │   ├── InstallerBuilder.cs
│   │   └── ResourceBuilder.cs
│   │
│   ├── GPack.Installer.Core/
│   │   ├── PackageReader.cs
│   │   ├── InstallerEngine.cs
│   │   ├── DiskSpaceChecker.cs
│   │   ├── FileVerifier.cs
│   │   ├── ShortcutManager.cs
│   │   ├── InstallStateManager.cs
│   │   └── Actions/
│   │
│   ├── GPack.Installer.UI/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   └── Assets/
│   │
│   └── GPack.Abstractions/
│       ├── IInstallerAction.cs
│       ├── ICompressionProvider.cs
│       ├── IGameSourceProvider.cs
│       └── IMetadataProvider.cs
│
├── templates/
│   └── default/
│
├── tests/
│   ├── GPack.Core.Tests/
│   ├── GPack.Builder.Tests/
│   └── GPack.Installer.Tests/
│
└── docs/
```

---

# 6. Configuration Model

All configuration sources should resolve into a single internal model:

```text
CLI arguments ───────┐
                     │
YAML config ─────────┼──► ConfigurationMerger
                     │
JSON config ─────────┘
                              │
                              ▼
                     BuildConfiguration
                              │
                              ▼
                         BuildEngine
```

The build engine must never care whether values came from YAML, JSON, CLI arguments, or Playnite.

Suggested entry point:

```csharp
Task<BuildResult> BuildAsync(
    BuildConfiguration configuration,
    CancellationToken cancellationToken);
```

---

# 7. Configuration Inputs

The tool should support all three approaches.

## 7.1 CLI arguments

CLI-only builds must be fully supported.

Example:

```powershell
gpack build `
  --source "D:\Games\Cyberpunk 2077" `
  --name "Cyberpunk 2077" `
  --version "2.3" `
  --exe "bin\x64\Cyberpunk2077.exe" `
  --output "D:\Repacks" `
  --compression balanced `
  --desktop-shortcut `
  --start-menu-shortcut
```

Minimal example:

```powershell
gpack build `
  --source "D:\Games\Cyberpunk 2077" `
  --name "Cyberpunk 2077" `
  --exe "bin\x64\Cyberpunk2077.exe"
```

---

## 7.2 YAML

Recommended for manually maintained package definitions.

Example:

```yaml
game:
  name: Cyberpunk 2077
  version: "2.3"

source:
  directory: D:\Games\Cyberpunk 2077

launch:
  executable: bin\x64\Cyberpunk2077.exe

package:
  compression: balanced
  splitSize: 4GB

installer:
  brand: NagelRepacks
  defaultDirectory: "%GAMES%\\Cyberpunk 2077"
  desktopShortcut: true
  startMenuShortcut: true

branding:
  icon: assets/icon.ico
```

Build:

```powershell
gpack build cyberpunk.gpack.yaml
```

---

## 7.3 JSON

Recommended for programmatic integrations such as Playnite.

Example:

```json
{
  "game": {
    "name": "Cyberpunk 2077",
    "version": "2.3"
  },
  "source": {
    "directory": "D:\\Games\\Cyberpunk 2077"
  },
  "launch": {
    "executable": "bin\\x64\\Cyberpunk2077.exe"
  },
  "package": {
    "compression": "balanced",
    "splitSize": "4GB"
  }
}
```

Usage:

```powershell
gpack build --config-json-file generated.json
```

Optional inline support:

```powershell
gpack build --config-json "{...}"
```

---

# 8. Configuration Precedence

Use a clear override hierarchy:

```text
Built-in defaults
       ↓
YAML / JSON configuration
       ↓
CLI arguments
       ↓
Final BuildConfiguration
```

Example:

```powershell
gpack build game.yaml --compression fast --output "E:\Repacks"
```

This allows reusable configuration files while still supporting automation and temporary overrides.

---

# 9. CLI Commands

Recommended command set:

```text
gpack
│
├── init
├── inspect
├── build
├── info
├── verify
├── extract
└── install
```

## `gpack init`

Create a starter package definition from an installed game directory.

```powershell
gpack init "D:\Games\Game"
```

Possible output:

```text
game.gpack.yaml
```

---

## `gpack inspect`

Analyze an installation directory.

```powershell
gpack inspect "D:\Games\Game"
```

Should report:

- Total files.
- Total size.
- Potential main executables.
- Executable metadata.
- Largest files.
- Possible junk / logs.
- Potential launcher files.
- Existing save/config folders only when explicitly requested.

Useful option:

```powershell
gpack inspect "D:\Games\Game" --largest
```

---

## `gpack build`

Build a `.gpack` package and installer.

```powershell
gpack build game.gpack.yaml
```

or:

```powershell
gpack build --source "D:\Games\Game" --name "Game"
```

---

## `gpack info`

Display package metadata.

```powershell
gpack info Game.gpack
```

Example:

```text
Game:         Game
Version:      1.2.0
Files:        4,821
Install Size: 68.3 GB
Package Size: 54.7 GB
Compression:  zstd
Created By:   gpack 0.1.0
Brand:        NagelRepacks
```

---

## `gpack verify`

Verify package integrity.

```powershell
gpack verify Game.gpack
```

---

## `gpack extract`

Debugging/manual extraction command.

```powershell
gpack extract Game.gpack --output "D:\Test"
```

---

## `gpack install`

Install a package through the CLI without the graphical installer.

```powershell
gpack install Game.gpack --destination "D:\Games\Game"
```

---

# 10. CLI Argument Groups

Organize help output by category.

## Game

```text
--name
--version
--exe
--launch-args
```

## Source

```text
--source
--include
--exclude
```

## Output

```text
--output
--single-file
--split-size
```

## Compression

```text
--compression
--compression-level
```

## Installer

```text
--install-dir
--desktop-shortcut
--start-menu-shortcut
--icon
```

## Automation

```text
--non-interactive
--json
--quiet
--verbose
```

Repeated options should be supported:

```powershell
gpack build `
  --source "D:\Game" `
  --exclude "*.log" `
  --exclude "CrashDumps/**" `
  --exclude "Screenshots/**"
```

---

# 11. Machine-Readable CLI Output

This is important for future Playnite integration.

Command:

```powershell
gpack build ... --json --non-interactive
```

Use line-delimited JSON events.

Progress example:

```json
{
  "event": "progress",
  "stage": "compression",
  "progress": 0.73,
  "processedBytes": 53312984064,
  "totalBytes": 73031442432,
  "currentFile": "archive\\data.pak"
}
```

Completion:

```json
{
  "event": "complete",
  "outputDirectory": "D:\\Repacks\\MyGame-NagelRepacks",
  "package": "D:\\Repacks\\MyGame-NagelRepacks\\MyGame.gpack"
}
```

Error:

```json
{
  "event": "error",
  "code": "SOURCE_NOT_FOUND",
  "message": "The source directory could not be found."
}
```

Do not require Playnite to parse human-readable console output.

---

# 12. Exit Codes

Define stable exit codes from the beginning.

Suggested mapping:

```text
0   Success

10  Invalid arguments
11  Invalid configuration
12  Source not found

20  Scan failed
21  File read failed
22  Hashing failed

30  Compression failed
31  Package creation failed

40  Installer generation failed

50  Verification failed

60  Installation failed

90  Operation cancelled
99  Unexpected error
```

---

# 13. Package Build Pipeline

```text
gpack build
     │
     ▼
Load configuration
     │
     ▼
Merge defaults + config + CLI overrides
     │
     ▼
Validate configuration
     │
     ▼
Scan source files
     │
     ├── apply includes
     └── apply exclusions
     │
     ▼
Build manifest
     │
     ▼
Hash files
     │
     ▼
Compress payload
     │
     ▼
Split into chunks
     │
     ▼
Write .gpack metadata
     │
     ▼
Generate installer frontend
     │
     ▼
Verify output package
     │
     ▼
Done
```

---

# 14. File Scanner

The scanner should recursively inspect the game directory.

Example source:

```text
D:\Games\Game\
├── Game.exe
├── Data\
├── Engine\
└── Config\
```

Store only relative paths:

```text
Game.exe
Data\data.pak
Engine\...
```

Never store source-specific absolute paths such as:

```text
D:\Games\Game\Data\data.pak
```

Each file entry should contain at least:

```text
relative path
uncompressed size
hash
attributes
optional modification timestamp
```

---

# 15. Exclusion System

Support glob-based exclusions.

Example:

```yaml
exclude:
  - "*.log"
  - "*.tmp"
  - "CrashDumps/**"
  - "Screenshots/**"
  - ".git/**"
```

CLI equivalent:

```powershell
gpack build `
  --exclude "*.log" `
  --exclude "CrashDumps/**"
```

Recommended default behavior:

- No destructive automatic exclusions.
- Show suggestions through `inspect`.
- Require explicit exclusion configuration.

---

# 16. Hashing and Verification

Use **SHA-256** initially.

Build process:

```text
Source file
   │
   ▼
SHA-256
   │
   ▼
Manifest
```

Install process:

```text
Extract file
   │
   ▼
Calculate hash
   │
   ▼
Compare with manifest
   │
   ├── valid
   └── invalid → retry / fail
```

Future optimization:

- Chunk hashes.
- Parallel hashing.
- Optional full file verification.
- Fast package-only verification.

---

# 17. Compression

Recommended default:

**Zstandard**

Expose friendly presets:

```text
none
fast
balanced
maximum
```

Possible mapping:

```text
none      → no compression
fast      → zstd level 3
balanced  → zstd level 10
maximum   → zstd level 18
```

Avoid exposing dozens of compression parameters initially.

Many game assets are already compressed, so aggressive compression often provides poor time-to-size gains.

---

# 18. Package Chunking

Do not rely on one huge ZIP archive.

Recommended layout:

```text
MyGame-NagelRepacks/
├── Setup.exe
├── MyGame.gpack
└── data/
    ├── payload-000.gpk
    ├── payload-001.gpk
    ├── payload-002.gpk
    └── payload-003.gpk
```

Config:

```yaml
package:
  splitSize: 4GB
```

or:

```powershell
gpack build ... --split-size 4GB
```

Chunking improves:

- Transfer reliability.
- NAS/network copying.
- External-drive compatibility.
- Resume support.
- Corruption isolation.

---

# 19. Proposed Package Manifest

Example:

```json
{
  "formatVersion": 1,

  "package": {
    "generator": "gpack",
    "generatorVersion": "0.1.0",
    "brand": "NagelRepacks",
    "createdUtc": "2026-08-17T00:00:00Z"
  },

  "game": {
    "name": "My Game",
    "version": "1.0.0"
  },

  "launch": {
    "executable": "Game.exe",
    "arguments": ""
  },

  "payload": {
    "compression": "zstd",
    "uncompressedSize": 98011473920,
    "chunks": [
      {
        "file": "data/payload-000.gpk",
        "size": 4294967296,
        "sha256": "..."
      }
    ]
  },

  "files": [
    {
      "path": "Game.exe",
      "size": 183654400,
      "sha256": "..."
    }
  ]
}
```

`formatVersion` must be versioned independently from the CLI application version.

---

# 20. Installer Runtime

The installer runtime should be a standalone application/library combination.

Suggested core interface:

```csharp
InstallerEngine
{
    AnalyzeAsync()
    PrepareAsync()
    InstallAsync()
    VerifyAsync()
    ExecutePostInstallActionsAsync()
    FinalizeAsync()
}
```

Installation stages:

```text
Preparing
   ↓
Checking package
   ↓
Checking disk space
   ↓
Creating temporary installation
   ↓
Extracting
   ↓
Verifying
   ↓
Post-install actions
   ↓
Creating shortcuts
   ↓
Finalizing
   ↓
Finished
```

---

# 21. UI / Engine Separation

The graphical UI must contain no package extraction logic.

The installer engine should publish state/progress events.

Example:

```csharp
public record InstallProgress(
    InstallStage Stage,
    long ProcessedBytes,
    long TotalBytes,
    string? CurrentFile,
    double Progress);
```

UI:

```text
InstallerEngine
      │
      ├── ProgressChanged
      ├── StageChanged
      └── ErrorOccurred
            │
            ▼
      Installer UI
```

This makes it possible to replace the UI later without rewriting the installer.

---

# 22. Installer UI Scope

The **final visual design is intentionally left open**.

The implementation should initially provide only the technical UI contract.

Required screens/states:

## Ready / Setup

- NagelRepacks branding.
- Game/package name.
- Install destination.
- Required disk space.
- Available disk space.
- Version.
- Desktop shortcut toggle.
- Start Menu shortcut toggle.
- Install button.
- Cancel button.

## Installing

- Overall progress.
- Current file.
- Elapsed time.
- Estimated remaining time.
- Installed size.
- Transfer speed.
- Current installation stage.
- Pause / resume if supported.
- Cancel.

## Completed

- Installation completed state.
- Launch game option.
- Open installation directory option.
- Close button.

## Error

- Human-readable error.
- Technical error code.
- Retry where possible.
- Log location.

The first UI implementation should prioritize functionality and clean separation. Visual polish can be applied after the installer workflow is stable.

---

# 23. Single-File vs Split Installer

Support both eventually.

## Split package

Recommended first implementation:

```text
MyGame-NagelRepacks/
├── Setup.exe
├── MyGame.gpack
└── data/
    ├── payload-000.gpk
    └── payload-001.gpk
```

Advantages:

- Easier to implement.
- Easier to debug.
- Better for huge games.
- Better for chunk verification.

## Single executable

Future option:

```text
MyGame-NagelRepacks-Setup.exe
```

Potential structure:

```text
┌────────────────────────┐
│ Installer executable   │
├────────────────────────┤
│ Package metadata       │
├────────────────────────┤
│ Payload                │
└────────────────────────┘
```

CLI:

```powershell
gpack build game.yaml --single-file
```

Implement split packages first.

---

# 24. Installer Variables

Provide a variable resolver shared by all installer actions.

Initial variables:

```text
%INSTALL_DIR%
%PACKAGE_DIR%
%TEMP_DIR%
%USER_PROFILE%
%APPDATA%
%LOCALAPPDATA%
%PROGRAMDATA%
```

Example:

```yaml
destination: "%APPDATA%\\MyGame\\config.ini"
```

---

# 25. Additional Game Data

Some games store important data outside the install directory.

Allow explicit additional sources:

```yaml
include:
  - source: "%APPDATA%\\MyGame"
    destination: "_userdata/appdata"

  - source: "%USER_PROFILE%\\Documents\\My Games\\MyGame"
    destination: "_userdata/documents"
```

Do not automatically collect arbitrary AppData or user directories.

Reasons:

- Privacy.
- Save files may contain personal information.
- User-specific launcher/account configuration should not be included accidentally.

---

# 26. Post-Install Action System

Implement post-processing through generic installer actions.

Interface:

```csharp
public interface IInstallerAction
{
    Task ExecuteAsync(
        InstallContext context,
        CancellationToken cancellationToken);
}
```

Initial actions:

```text
CopyAction
MoveAction
DeleteAction
CommandAction
ShortcutAction
RegistryAction
EnvironmentVariableAction
```

Example YAML:

```yaml
actions:
  - type: copy
    source: extras/config.ini
    destination: "%INSTALL_DIR%\\config.ini"

  - type: command
    executable: "%INSTALL_DIR%\\redist\\vcredist.exe"
    arguments: "/quiet"
    wait: true

  - type: shortcut
    target: "%INSTALL_DIR%\\Game.exe"
    location: desktop
```

---

# 27. Actions Through CLI

Simple actions can be represented directly:

```powershell
gpack build `
  --action "copy|source=config.ini|destination=%INSTALL_DIR%\config.ini"
```

For complex cases, prefer:

```powershell
--action-file actions.json
```

or:

```powershell
--config-json-file build.json
```

For Playnite, structured JSON should be preferred over long escaped CLI strings.

---

# 28. Temporary Installation and Rollback

Never extract directly into the final destination.

Use a temporary directory:

```text
D:\Games\.gpack-install-{GUID}
```

Flow:

```text
Temporary directory
      │
      ├── extract
      ├── verify
      ├── post-process
      │
      └── success
              │
              ▼
         final directory
```

If the operation fails or is cancelled:

- Remove temporary files where safe.
- Leave the previous installation unchanged.
- Write an error log.

---

# 29. Resume Support

Recommended for Phase 2.

Store state:

```text
.gpack-install-state.json
```

Example:

```json
{
  "packageId": "...",
  "completedChunks": [0, 1, 2, 3],
  "currentChunk": 4
}
```

On restart:

```text
Incomplete installation found.

Resume
Start over
Cancel
```

Chunked payloads make resume support substantially easier.

---

# 30. Logging

Both `gpack.exe` and generated installers should create detailed logs.

Suggested location:

```text
%LOCALAPPDATA%\NagelRepacks\Logs\
```

Example:

```text
2026-08-17 00:14:03 [INFO] Installation started
2026-08-17 00:14:03 [INFO] Package: MyGame.gpack
2026-08-17 00:14:03 [INFO] Target: D:\Games\MyGame
2026-08-17 00:14:04 [INFO] Required space: 63.21 GB
2026-08-17 00:14:06 [INFO] Extracting payload-000.gpk
```

Support:

```powershell
--quiet
--verbose
```

---

# 31. Cancellation

Every long-running operation should accept a `CancellationToken`.

Cancellation should be supported during:

- File scanning.
- Hashing.
- Compression.
- Package writing.
- Extraction.
- Verification.

Cancellation should cleanly stop the operation and remove temporary/incomplete files where appropriate.

---

# 32. Playnite Integration

The Playnite plugin should not need to understand the internal package format.

Recommended integration:

```text
Playnite
   │
   ▼
NagelRepacks Playnite Plugin
   │
   ├── game metadata
   ├── installation path
   ├── executable
   ├── icon
   └── build options
   │
   ▼
gpack.exe
   │
   ▼
.gpack + Setup.exe
```

Recommended invocation:

```powershell
gpack build `
  --config-json-file "<temporary config>" `
  --json `
  --non-interactive
```

The plugin can:

- Read progress events.
- Show gpack progress inside Playnite.
- Cancel the process.
- Open the output folder.
- Save presets.
- Build packages for selected games.

---

# 33. Future Direct Library Integration

Initially, calling `gpack.exe` from Playnite is safer because it creates a clean process boundary.

Later, the Playnite plugin could reference:

```text
GPack.Core
GPack.Builder
```

directly.

Possible API:

```csharp
var result = await buildEngine.BuildAsync(config, cancellationToken);
```

This should only be considered after the public API stabilizes.

---

# 34. Game Detection

`gpack inspect` should eventually identify likely launch executables.

For each `.exe`, inspect:

- File name.
- Product name.
- File description.
- Product version.
- File version.
- Size.
- Digital signature where applicable.

Example:

```text
Potential executables:

[1] Game.exe
    Product: My Game
    Version: 1.4.2
    Size: 124 MB

[2] Launcher.exe
    Product: Game Launcher
    Size: 8 MB

Likely launch executable:
    Game.exe
```

---

# 35. Launcher Requirements

The package may still depend on launchers such as:

- Steam
- Epic Games Launcher
- EA App
- Ubisoft Connect
- GOG Galaxy

The packager should not attempt to bypass license verification.

Optional package metadata:

```yaml
requirements:
  - name: Steam
    required: true
```

The installer can warn:

```text
Steam is required to launch this game.
```

---

# 36. Plugin-Friendly Architecture

Prepare the core for plugins even if plugin loading is not implemented in v0.1.

Interfaces:

```csharp
IGameSourceProvider
ICompressionProvider
IInstallerAction
IMetadataProvider
```

Possible future modules:

```text
GPack.Plugin.Playnite
GPack.Plugin.Steam
GPack.Plugin.GOG
```

The generic package format should remain independent of any launcher.

---

# 37. Security and Safety Considerations

## Path traversal

Reject archive entries such as:

```text
..\..\Windows\System32\...
```

All extracted paths must remain underneath the installation root unless a specific installer action explicitly targets another supported location.

## Symlinks / junctions

Handle them explicitly.

Initial recommendation:

- Do not follow unknown reparse points by default.
- Require explicit configuration to package links.

## Commands

Post-install commands should:

- Be visible in the manifest.
- Have explicit arguments.
- Have explicit working directories.
- Return exit codes.
- Support timeout/cancellation.

## Package integrity

Verify:

- Manifest integrity.
- Chunk hashes.
- File hashes where enabled.

---

# 38. Testing Strategy

## Unit tests

Cover:

- Path normalization.
- Include/exclude glob rules.
- Configuration merging.
- Variable resolution.
- Manifest serialization.
- Hash calculation.
- Chunk boundary logic.
- Exit-code mapping.

## Integration tests

Create test fixture games:

```text
SmallGame/
MediumGame/
ManySmallFiles/
LongPaths/
UnicodePaths/
LockedFiles/
CorruptedPackage/
```

Test:

- Build.
- Verify.
- Extract.
- Install.
- Cancel.
- Rollback.
- Reinstall.
- Corrupted chunk handling.

## Large-game testing

Before v1.0, test packages in ranges such as:

```text
10 GB
50 GB
100 GB
150+ GB
```

Memory consumption should remain bounded.

The build and installer pipelines should be streaming rather than loading entire files or archives into RAM.

---

# 39. Performance Requirements

Design for large game installations from the beginning.

## Build

- Stream file contents.
- Parallelize hashing with a bounded worker count.
- Avoid excessive small allocations.
- Avoid copying data unnecessarily.
- Allow compression concurrency to be configured.

## Install

- Sequential or bounded-parallel extraction.
- Efficient disk writes.
- Periodic progress updates rather than one update per tiny file.
- Avoid blocking UI thread.

---

# 40. MVP Scope — v0.1

The first working release should deliberately stay small.

## CLI

Implement:

```text
gpack inspect
gpack build
gpack info
gpack verify
gpack extract
```

## Package system

Support:

- Recursive directory scan.
- Relative paths.
- Include/exclude rules.
- SHA-256.
- Zstandard compression.
- Chunked payloads.
- Versioned manifest.
- Package verification.

## Installer

Support:

- Read `.gpack`.
- Destination selection.
- Disk-space check.
- Extract files.
- Verify files.
- Cancellation.
- Rollback/temp folder.
- Desktop shortcut.
- Start Menu shortcut.
- Launch executable.
- Logging.

## UI

Functional placeholder implementation only.

Final visual design remains open.

---

# 41. Phase 2

After the basic system works reliably:

- Custom NagelRepacks installer theme.
- Installer asset injection.
- Single-file installer support.
- Resume interrupted installs.
- Additional source folders.
- Post-install commands.
- Copy/move/delete actions.
- Registry actions.
- Multiple launch executables.
- Improved executable detection.
- Package metadata viewer.
- Installer music support if desired.
- Configurable UI branding / assets.

---

# 42. Phase 3

Integration and ecosystem:

- Playnite plugin.
- Direct `.gpack` installation from Playnite.
- Game metadata import.
- Icon/cover import.
- Saved package profiles.
- Build queue.
- Package library.
- Optional source providers.
- Plugin system.
- Differential/update packages.

---

# 43. Optional Future Feature — Differential Packages

A future package version could support:

```text
Base game package
      +
Update package
```

Instead of rebuilding the complete game.

Potential model:

```text
MyGame-1.0.gpack
MyGame-1.1-update.gpack
```

The update manifest would contain:

- Added files.
- Changed files.
- Removed files.
- Base package/version requirement.

This should not be part of the initial implementation.

---

# 44. Recommended Development Milestones

## M1 — Solution and core models

Implement:

- Solution/project layout.
- `BuildConfiguration`.
- `PackageManifest`.
- `GameMetadata`.
- `FileEntry`.
- `PackageChunk`.
- Versioning.

Acceptance criteria:

- Models serialize/deserialize.
- Configuration validation works.
- Unit test project established.

---

## M2 — Scanner

Implement:

- Recursive scan.
- Relative paths.
- Include/exclude rules.
- File metadata.
- SHA-256.
- Progress events.

Acceptance criteria:

```powershell
gpack inspect "D:\Games\TestGame"
```

successfully reports the directory.

---

## M3 — Package writer

Implement:

- Streaming compression.
- Zstandard.
- Payload chunks.
- Manifest output.
- Chunk hashes.

Acceptance criteria:

```powershell
gpack build ...
```

produces a valid `.gpack` and payload chunks.

---

## M4 — Package reader

Implement:

- Manifest reader.
- Chunk reader.
- Decompression.
- Package verification.
- File extraction.

Acceptance criteria:

```powershell
gpack extract Test.gpack --output TestOutput
```

recreates the source directory byte-for-byte.

---

## M5 — Headless installer engine

Implement:

- Destination validation.
- Disk-space checks.
- Temporary installation directory.
- Extraction.
- Verification.
- Cancellation.
- Rollback.
- Final move.
- Shortcut creation.

Acceptance criteria:

A package can be installed through:

```powershell
gpack install Test.gpack --destination "D:\Installed\Test"
```

---

## M6 — Basic installer UI

Implement:

- Ready state.
- Destination selection.
- Progress.
- Error handling.
- Completion.
- Cancel.

Acceptance criteria:

Generated `Setup.exe` can install a package without `gpack.exe`.

Visual design is still provisional.

---

## M7 — Installer builder

Implement:

- Installer runtime template.
- Package metadata injection.
- Icon injection.
- NagelRepacks branding.
- Package discovery.
- Output folder creation.

Acceptance criteria:

```powershell
gpack build ...
```

creates:

```text
Game-NagelRepacks/
├── Setup.exe
├── Game.gpack
└── data/
```

---

## M8 — Configuration system

Implement:

- YAML loader.
- JSON loader.
- CLI parser.
- Config merging.
- CLI overrides.
- Validation errors.

Acceptance criteria:

These produce equivalent output:

```powershell
gpack build game.yaml
```

```powershell
gpack build --config-json-file game.json
```

```powershell
gpack build --source ... --name ...
```

---

## M9 — Automation API

Implement:

- `--json`.
- `--non-interactive`.
- Stable event schema.
- Stable exit codes.
- Cancellation handling.

Acceptance criteria:

A separate test process can fully control and monitor `gpack`.

---

## M10 — Advanced actions

Implement:

- Variables.
- Copy action.
- Move action.
- Delete action.
- Command action.
- Shortcut action.
- Registry action.

Acceptance criteria:

Actions can be declared through YAML/JSON and run during installation.

---

## M11 — Playnite integration

Implement:

- Playnite game → `BuildConfiguration`.
- JSON temp config.
- Launch `gpack`.
- Parse JSON events.
- Progress UI.
- Cancel.
- Output handling.

---

# 45. Recommended First End-to-End Target

The first major target should be this exact workflow:

```text
D:\Games\MyGame
      │
      ▼
gpack build
      │
      ▼
MyGame-NagelRepacks/
├── Setup.exe
├── MyGame.gpack
└── data/
    ├── payload-000.gpk
    └── payload-001.gpk
      │
      ▼
Copy folder to another PC
      │
      ▼
Run Setup.exe
      │
      ▼
Choose destination
      │
      ▼
Extract + verify
      │
      ▼
Create shortcuts
      │
      ▼
Launch game
```

The project should not expand into advanced UI, launcher integrations, update systems, or plugin loading until this workflow is reliable for very large game installations.

---

# 46. Suggested Initial Priorities

Priority order:

1. Reliable package format.
2. Streaming compression.
3. Integrity verification.
4. Reliable extraction.
5. Cancellation and rollback.
6. CLI configuration.
7. JSON automation interface.
8. Basic generated installer.
9. YAML convenience configuration.
10. Playnite integration.
11. Installer visual design.
12. Advanced actions and plugins.

This prioritization prevents the installer UI from becoming the central dependency before the underlying package format is stable.

---

# 47. Final Architecture Summary

```text
                         INPUTS
                           │
          ┌────────────────┼────────────────┐
          │                │                │
       CLI args           YAML             JSON
          │                │                │
          └────────────────┼────────────────┘
                           ▼
                  ConfigurationMerger
                           │
                           ▼
                   BuildConfiguration
                           │
                           ▼
                      BuildEngine
                           │
          ┌────────────────┼────────────────┐
          │                │                │
          ▼                ▼                ▼
       Scanner         Compressor      ManifestBuilder
          │                │                │
          └────────────────┼────────────────┘
                           ▼
                       .gpack
                           │
              ┌────────────┴────────────┐
              │                         │
              ▼                         ▼
          Setup.exe                 gpack install
              │                         │
              └────────────┬────────────┘
                           ▼
                    InstallerEngine
                           │
              ┌────────────┼─────────────┐
              ▼            ▼             ▼
          Extraction   Verification    Actions
              │            │             │
              └────────────┼─────────────┘
                           ▼
                    Installed Game
```

The result is a clean separation between:

- **NagelRepacks** — user-facing installer brand.
- **gpack** — CLI/package tooling.
- **`.gpack`** — portable package format.
- **InstallerEngine** — reusable installation runtime.
- **Installer UI** — replaceable presentation layer.
- **Playnite plugin** — future integration layer.

This keeps the project flexible while avoiding coupling the package format to one installer design or one launcher.
