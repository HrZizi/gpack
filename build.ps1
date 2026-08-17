<#
.SYNOPSIS
    Build and publish NagelRepacks / gpack binaries.
.DESCRIPTION
    Builds and publishes standalone, self-contained single-file executables (or framework-dependent binaries)
    for gpack.exe CLI and Setup.exe WPF installer.
.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Standalone
.EXAMPLE
    .\build.ps1 -SkipTests
#>

[CmdletBinding()]
param (
    [switch]$Standalone = $true,
    [switch]$SkipTests = $false,
    [switch]$Clean = $false,
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = Get-Location }

function Write-Step ($text) {
    Write-Host "`n=== $text ===" -ForegroundColor Cyan
}

if ($Clean) {
    Write-Step "Cleaning Build Outputs"
    if (Test-Path "$root\artifacts") { Remove-Item -Recurse -Force "$root\artifacts" }
    dotnet clean "$root\NagelRepacks.sln" -c $Configuration | Out-Null
}

if (-not $SkipTests) {
    Write-Step "Running Automated Test Suite"
    dotnet test "$root\NagelRepacks.sln" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed. Aborting publish."
        exit $LASTEXITCODE
    }
}

if ($Standalone) {
    Write-Step "Publishing Standalone Self-Contained Single-File Binaries (win-x64)"

    $cliOut = "$root\artifacts\gpack"
    $guiOut = "$root\artifacts\installer"

    Write-Host "Publishing gpack.exe CLI (Self-Contained Single-File)..." -ForegroundColor White
    dotnet publish "$root\src\GPack.Cli\GPack.Cli.csproj" `
        -c $Configuration `
        -r win-x64 `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        --self-contained true `
        -o $cliOut `
        --nologo

    Write-Host "Publishing Setup.exe WPF Installer (Self-Contained Single-File)..." -ForegroundColor White
    dotnet publish "$root\src\GPack.Installer.UI\GPack.Installer.UI.csproj" `
        -c $Configuration `
        -r win-x64 `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        --self-contained true `
        -o $guiOut `
        --nologo

    # Also keep in artifacts/standalone for convenience
    $standaloneCli = "$root\artifacts\standalone\gpack"
    $standaloneGui = "$root\artifacts\standalone\installer"
    New-Item -ItemType Directory -Force -Path $standaloneCli, $standaloneGui | Out-Null
    Copy-Item "$cliOut\gpack.exe" "$standaloneCli\gpack.exe" -Force
    Copy-Item "$guiOut\Setup.exe" "$standaloneGui\Setup.exe" -Force

    Write-Step "Build & Publish Complete!"
    Write-Host "[OK] Standalone CLI:       $cliOut\gpack.exe" -ForegroundColor Green
    Write-Host "[OK] Standalone Installer: $guiOut\Setup.exe" -ForegroundColor Green
    Write-Host "`nBoth executables are 100% self-contained and run on any 64-bit Windows PC without requiring .NET runtime installed." -ForegroundColor Yellow
} else {
    Write-Step "Publishing Framework-Dependent Binaries"

    $cliOut = "$root\artifacts\framework-dependent\gpack"
    $guiOut = "$root\artifacts\framework-dependent\installer"

    dotnet publish "$root\src\GPack.Cli\GPack.Cli.csproj" -c $Configuration -r win-x64 --self-contained false -o $cliOut --nologo
    dotnet publish "$root\src\GPack.Installer.UI\GPack.Installer.UI.csproj" -c $Configuration -r win-x64 --self-contained false -o $guiOut --nologo

    Write-Step "Build & Publish Complete!"
    Write-Host "[OK] CLI:       $cliOut\gpack.exe" -ForegroundColor Green
    Write-Host "[OK] Installer: $guiOut\Setup.exe" -ForegroundColor Green
}
