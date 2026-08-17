# GEMINI.md — Project & Workspace Rules

## 1. Project Overview
NagelRepacks / gpack is a high-performance C# / .NET 10 solution for packaging local game installations into portable repack installers (`.gpack` + `.gpk` payload chunks + `Setup.exe` WPF graphical installer and `gpack.exe` CLI).

## 2. Core Architecture Rules
- **Package Format Primacy**: The `.gpack` manifest and `.gpk` chunk format is the universal payload contract. Frontends (CLI, WPF GUI, Playnite plugins) are interchangeable consumers.
- **Strict UI & Engine Separation**: `GPack.Installer.UI` must never contain decompression or installation logic; it communicates solely via `InstallerEngine` events.
- **Streaming & Memory Efficiency**: Never load entire files or archives into RAM. Use streaming hashing, streaming Zstandard compression, and chunk boundaries.
- **Security & Path Validation**: All paths extracted or referenced in packages must be normalized and verified against directory traversal vulnerabilities (`..` or rooted absolute paths).
- **Atomic Operations**: Always extract payloads to a temporary folder (`.gpack-install-{GUID}`) and verify file SHA-256 hashes prior to moving to the target install location. Provide automatic rollback on error or cancellation.
- **Exit Code Standards**: Adhere strictly to the defined exit code schema (0 = Success, 10-12 = Args/Config/Source errors, 20-22 = Scan/Hash errors, 30-31 = Compression/Package errors, 40 = Installer build error, 50 = Verification error, 60 = Install error, 90 = Cancelled, 99 = Unknown).

## 3. Artifact Build & Portable Standalone Rule
Whenever the codebase is modified, updated, or reaches a usable/testable state:
1. Run all automated unit and integration tests:
   ```powershell
   dotnet test NagelRepacks.sln -c Release
   ```
2. Build and publish the release binary artifacts (Standalone Self-Contained Single-File options):
   ```powershell
   # Standalone Self-Contained Single-File (Runs on any Windows PC without .NET runtime):
   dotnet publish src/GPack.Cli/GPack.Cli.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --self-contained true -o artifacts/gpack
   dotnet publish src/GPack.Installer.UI/GPack.Installer.UI.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --self-contained true -o artifacts/installer

   # Or simply execute:
   .\build.ps1 -Standalone
   ```
3. Verify that the generated executable files exist and run correctly.
