<#
.SYNOPSIS
    Guided interactive wizard for creating NagelRepacks game installer packages.
.DESCRIPTION
    Step-by-step interactive script that inspects game folders, suggests executables,
    configures compression and chunk sizes, and builds the .gpack repack installer.
.EXAMPLE
    .\New-GameRepack.ps1
.EXAMPLE
    .\New-GameRepack.ps1 -SourceDir "D:\Games\Cyberpunk 2077"
.EXAMPLE
    .\New-GameRepack.ps1 -SourceDir "D:\Games\Game" -GameName "MyGame" -Exe "bin\Game.exe" -Compression fast -SplitSize 2GB -Yes
#>

[CmdletBinding()]
param (
    [string]$SourceDir,
    [string]$GameName,
    [string]$Version,
    [string]$Edition,
    [string]$Artwork,
    [string]$Exe,
    [string]$Compression,
    [string]$SplitSize,
    [string]$Format,
    [string]$PartSize,
    [string]$OutputDir,
    [string]$SetupExePath,
    [switch]$Yes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Colors & Formatting
function Write-Header ($text) {
    Write-Host ""
    Write-Host "=======================================================================" -ForegroundColor Cyan
    Write-Host " $text" -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host "=======================================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Section ($text) {
    Write-Host "`n--- $text ---" -ForegroundColor Yellow
}

function Write-Success ($text) {
    Write-Host "[✓] $text" -ForegroundColor Green
}

function Write-Info ($text) {
    Write-Host "[i] $text" -ForegroundColor Cyan
}

function Write-WarningMsg ($text) {
    Write-Host "[!] $text" -ForegroundColor Yellow
}

# Locate gpack.exe
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = Get-Location }

$gpackCandidates = @(
    (Join-Path $root "artifacts\gpack\gpack.exe"),
    (Join-Path $root "artifacts\standalone\gpack\gpack.exe"),
    (Join-Path $root "src\GPack.Cli\bin\Release\net10.0\gpack.exe"),
    (Join-Path $root "src\GPack.Cli\bin\Debug\net10.0\gpack.exe"),
    (Join-Path $root "..\artifacts\gpack\gpack.exe")
)

$gpackExe = $null
foreach ($path in $gpackCandidates) {
    if (Test-Path $path) {
        $gpackExe = (Resolve-Path $path).Path
        break
    }
}

if (-not $gpackExe) {
    Write-WarningMsg "gpack.exe not found in release/debug folders. Building CLI..."
    dotnet build (Join-Path $root "NagelRepacks.sln") -c Release | Out-Null
    foreach ($path in $gpackCandidates) {
        if (Test-Path $path) {
            $gpackExe = (Resolve-Path $path).Path
            break
        }
    }
}

if (-not $gpackExe -or -not (Test-Path $gpackExe)) {
    Write-Error "Could not locate or build gpack.exe. Please run '.\build.ps1 -Standalone' first."
    return
}

Write-Header "NagelRepacks / gpack — Repack Packaging Wizard"

# STEP 1: Game Directory
Write-Section "Step 1: Select Game Installation Directory"

while ([string]::IsNullOrWhiteSpace($SourceDir) -or -not (Test-Path $SourceDir -PathType Container)) {
    if (-not [string]::IsNullOrWhiteSpace($SourceDir)) {
        Write-WarningMsg "Directory not found: $SourceDir"
    }

    Write-Host "Enter the path to the installed game directory (or press Enter to browse): " -NoNewline -ForegroundColor White
    $inputDir = Read-Host

    if ([string]::IsNullOrWhiteSpace($inputDir)) {
        # Open Windows Folder Browser dialog
        Add-Type -AssemblyName System.Windows.Forms
        $folderDialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $folderDialog.Description = "Select Installed Game Folder"
        $folderDialog.ShowNewFolderButton = $false

        if ($folderDialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $SourceDir = $folderDialog.SelectedPath
        }
    } else {
        $SourceDir = $inputDir.Trim('"').Trim("'")
    }
}

$SourceDir = (Resolve-Path $SourceDir).Path
Write-Success "Source Game Directory: $SourceDir"

# STEP 2: Automatic Analysis
Write-Section "Step 2: Analyzing Game Directory"
Write-Info "Scanning files and executables..."

$detectedName = Split-Path $SourceDir -Leaf
$detectedExe = if (-not [string]::IsNullOrWhiteSpace($Exe)) { $Exe } else { "" }
$allExes = @(Get-ChildItem -Path $SourceDir -Filter "*.exe" -Recurse -File -ErrorAction SilentlyContinue)

Write-Host "Found $($allExes.Count) executable(s) in directory." -ForegroundColor Gray

if ([string]::IsNullOrWhiteSpace($detectedExe)) {
    if ($allExes.Count -gt 0) {
        Write-Host "`nCandidate Executables:" -ForegroundColor White
        for ($i = 0; $i -lt $allExes.Count; $i++) {
            $relPath = $allExes[$i].FullName.Substring($SourceDir.Length).TrimStart('\', '/')
            $sizeMB = [math]::Round($allExes[$i].Length / 1MB, 2)
            $isLikely = ($allExes[$i].BaseName -like "*$detectedName*" -or $relPath -notlike "*\*")
            $marker = if ($isLikely) { " [Recommended]" } else { "" }
            Write-Host "  [$($i+1)] $relPath ($sizeMB MB)$marker" -ForegroundColor $(if ($isLikely) { "Green" } else { "Gray" })
            if ($isLikely -and [string]::IsNullOrEmpty($detectedExe)) {
                $detectedExe = $relPath
            }
        }
        if ([string]::IsNullOrEmpty($detectedExe)) {
            $detectedExe = $allExes[0].FullName.Substring($SourceDir.Length).TrimStart('\', '/')
        }
    }

    if (-not $Yes) {
        Write-Host "`nEnter main launch executable (default: '$detectedExe' or type number): " -NoNewline -ForegroundColor White
        $exeChoice = Read-Host
        if (-not [string]::IsNullOrWhiteSpace($exeChoice)) {
            if ($exeChoice -match '^\d+$' -and [int]$exeChoice -ge 1 -and [int]$exeChoice -le $allExes.Count) {
                $detectedExe = $allExes[[int]$exeChoice - 1].FullName.Substring($SourceDir.Length).TrimStart('\', '/')
            } else {
                $detectedExe = $exeChoice.Trim('"').Trim("'")
            }
        }
    }
}
Write-Success "Launch Executable: $detectedExe"

# STEP 3: Game Metadata
Write-Section "Step 3: Game Metadata"

if ([string]::IsNullOrWhiteSpace($GameName)) {
    if (-not $Yes) {
        Write-Host "Game Name [Default: '$detectedName']: " -NoNewline -ForegroundColor White
        $nameInput = Read-Host
        $GameName = if (-not [string]::IsNullOrWhiteSpace($nameInput)) { $nameInput.Trim() } else { $detectedName }
    } else {
        $GameName = $detectedName
    }
}
Write-Success "Game Name: $GameName"

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not $Yes) {
        Write-Host "Version [Default: '1.0.0']: " -NoNewline -ForegroundColor White
        $verInput = Read-Host
        $Version = if (-not [string]::IsNullOrWhiteSpace($verInput)) { $verInput.Trim() } else { "1.0.0" }
    } else {
        $Version = "1.0.0"
    }
}
Write-Success "Version: $Version"

if ([string]::IsNullOrWhiteSpace($Artwork)) {
    $possibleArtworks = @("artwork.jpg", "artwork.png", "cover.jpg", "cover.png", "banner.jpg", "banner.png")
    $detectedArt = $null
    foreach ($art in $possibleArtworks) {
        $cand = Join-Path $SourceDir $art
        if (Test-Path $cand) {
            $detectedArt = $cand
            break
        }
    }
    $artPromptDefault = if ($detectedArt) { Split-Path $detectedArt -Leaf } else { "None / Default" }

    if (-not $Yes) {
        Write-Host "Artwork / Cover image path or URL [Default: '$artPromptDefault', Enter to skip]: " -NoNewline -ForegroundColor White
        $artInput = Read-Host
        if (-not [string]::IsNullOrWhiteSpace($artInput)) {
            $Artwork = $artInput.Trim('"').Trim("'")
        } elseif ($detectedArt) {
            $Artwork = $detectedArt
        }
    } elseif ($detectedArt) {
        $Artwork = $detectedArt
    }
}
if (-not [string]::IsNullOrWhiteSpace($Artwork)) {
    Write-Success "Artwork: $Artwork"
}

$brandName = "NagelRepacks"

# STEP 4: Compression & Chunk Size
Write-Section "Step 4: Compression & Package Chunking"

if ([string]::IsNullOrWhiteSpace($Compression)) {
    if (-not $Yes) {
        Write-Host "Select Compression Preset:" -ForegroundColor White
        Write-Host "  [1] Balanced (Zstandard Level 10 — Recommended)" -ForegroundColor Green
        Write-Host "  [2] Fast (Zstandard Level 3 — Faster build time)"
        Write-Host "  [3] Maximum (Zstandard Level 18 — Smallest package size)"
        Write-Host "  [4] None (Store uncompressed)"
        Write-Host "Choice [1-4, Default: 1]: " -NoNewline -ForegroundColor White
        $compChoice = Read-Host
        $Compression = switch ($compChoice) {
            "2" { "fast" }
            "3" { "maximum" }
            "4" { "none" }
            Default { "balanced" }
        }
    } else {
        $Compression = "balanced"
    }
}
Write-Success "Compression Preset: $Compression"

if ([string]::IsNullOrWhiteSpace($SplitSize)) {
    if (-not $Yes) {
        Write-Host "`nSelect Payload Split Chunk Size:" -ForegroundColor White
        Write-Host "  [1] 4 GB (Standard / FAT32 & USB drive compatible — Recommended)" -ForegroundColor Green
        Write-Host "  [2] 2 GB (Smaller chunks)"
        Write-Host "  [3] 8 GB (Larger chunks)"
        Write-Host "  [4] Custom size"
        Write-Host "Choice [1-4, Default: 1]: " -NoNewline -ForegroundColor White
        $splitChoice = Read-Host
        $SplitSize = switch ($splitChoice) {
            "2" { "2GB" }
            "3" { "8GB" }
            "4" {
                Write-Host "Enter custom size (e.g. 500MB, 1.5GB): " -NoNewline -ForegroundColor White
                (Read-Host).Trim()
            }
            Default { "4GB" }
        }
    } else {
        $SplitSize = "4GB"
    }
}
Write-Success "Chunk Split Size: $SplitSize"

# STEP 5: Output Format
Write-Section "Step 5: Output Format"

if ([string]::IsNullOrWhiteSpace($Format)) {
    if (-not $Yes) {
        Write-Host "Select Output Format:" -ForegroundColor White
        Write-Host "  [1] Directory / Folder (Default — Setup.exe + .gpack + .gpk chunks)" -ForegroundColor Green
        Write-Host "  [2] ISO Disc Image (.iso)"
        Write-Host "  [3] ZIP Archive (.zip)"
        Write-Host "  [4] RAR Archive (.rar)"
        Write-Host "  [5] Parted RAR Multi-Volume Archive (.part1.rar, .part2.rar, ...)"
        Write-Host "Choice [1-5, Default: 1]: " -NoNewline -ForegroundColor White
        $formatChoice = Read-Host
        $Format = switch ($formatChoice) {
            "2" { "iso" }
            "3" { "zip" }
            "4" { "rar" }
            "5" { "parted-rar" }
            Default { "directory" }
        }
    } else {
        $Format = "directory"
    }
}
Write-Success "Output Format: $Format"

if ($Format -eq "parted-rar" -and [string]::IsNullOrWhiteSpace($PartSize)) {
    if (-not $Yes) {
        Write-Host "`nSelect RAR Part / Volume Size:" -ForegroundColor White
        Write-Host "  [1] 4 GB (Standard FAT32 & USB drive compatible — Recommended)" -ForegroundColor Green
        Write-Host "  [2] 2 GB"
        Write-Host "  [3] 1 GB"
        Write-Host "  [4] 500 MB"
        Write-Host "  [5] Custom size"
        Write-Host "Choice [1-5, Default: 1]: " -NoNewline -ForegroundColor White
        $partChoice = Read-Host
        $PartSize = switch ($partChoice) {
            "2" { "2GB" }
            "3" { "1GB" }
            "4" { "500MB" }
            "5" {
                Write-Host "Enter custom size (e.g. 500MB, 1.5GB): " -NoNewline -ForegroundColor White
                (Read-Host).Trim()
            }
            Default { "4GB" }
        }
    } else {
        $PartSize = "4GB"
    }
    Write-Success "RAR Part Size: $PartSize"
}

# STEP 6: Output Folder
Write-Section "Step 6: Output Location"
$defaultOutput = Split-Path $SourceDir -Parent
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    if (-not $Yes) {
        Write-Host "Output Base Folder [Default: '$defaultOutput']: " -NoNewline -ForegroundColor White
        $outInput = Read-Host
        $OutputDir = if (-not [string]::IsNullOrWhiteSpace($outInput)) { $outInput.Trim('"').Trim("'") } else { $defaultOutput }
    } else {
        $OutputDir = $defaultOutput
    }
}
Write-Success "Output Base Folder: $OutputDir"

# STEP 7: Summary & Confirmation
Write-Section "Step 7: Summary & Build"

$targetPreview = switch ($Format) {
    "iso" { "$OutputDir\$GameName-$brandName.iso" }
    "zip" { "$OutputDir\$GameName-$brandName.zip" }
    "rar" { "$OutputDir\$GameName-$brandName.rar" }
    "parted-rar" { "$OutputDir\$GameName-$brandName.part1.rar" }
    Default { "$OutputDir\$GameName-$brandName" }
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Game:         $GameName (v$Version)" -ForegroundColor White
Write-Host " Source:       $SourceDir" -ForegroundColor White
Write-Host " Main Exe:     $detectedExe" -ForegroundColor White
if (-not [string]::IsNullOrWhiteSpace($Artwork)) {
    Write-Host " Artwork:      $Artwork" -ForegroundColor White
}
Write-Host " Compression:  $Compression" -ForegroundColor White
Write-Host " Split Size:   $SplitSize" -ForegroundColor White
Write-Host " Format:       $Format" -ForegroundColor White
if ($Format -eq "parted-rar" -and -not [string]::IsNullOrWhiteSpace($PartSize)) {
    Write-Host " Part Size:    $PartSize" -ForegroundColor White
}
Write-Host " Brand:        $brandName" -ForegroundColor White
Write-Host " Target:       $targetPreview" -ForegroundColor White
Write-Host "==================================================" -ForegroundColor Cyan

if (-not $Yes) {
    Write-Host "`nWhat would you like to do?" -ForegroundColor White
    Write-Host "  [1] Build Repack Installer Now" -ForegroundColor Green
    Write-Host "  [2] Generate .gpack.yaml configuration file only" -ForegroundColor Yellow
    Write-Host "  [3] Cancel" -ForegroundColor Red
    Write-Host "Choice [1-3, Default: 1]: " -NoNewline -ForegroundColor White
    $action = Read-Host

    if ($action -eq "3") {
        Write-WarningMsg "Cancelled by user."
        return
    }

    if ($action -eq "2") {
        $yamlPath = Join-Path $OutputDir "$GameName.gpack.yaml"
        & $gpackExe init $SourceDir --output $yamlPath
        Write-Success "Generated configuration at: $yamlPath"
        return
    }
}

# BUILD
Write-Header "Building Repack Installer: $GameName"

$buildArgs = @(
    "build",
    "--source", $SourceDir,
    "--name", $GameName,
    "--version", $Version,
    "--exe", $detectedExe,
    "--output", $OutputDir,
    "--compression", $Compression,
    "--split-size", $SplitSize,
    "--format", $Format,
    "--desktop-shortcut",
    "--start-menu-shortcut",
    "--exclude", "*.log",
    "--exclude", "*.tmp",
    "--exclude", "CrashDumps/**"
)

if (-not [string]::IsNullOrWhiteSpace($PartSize)) {
    $buildArgs += @("--part-size", $PartSize)
}

if (-not [string]::IsNullOrWhiteSpace($Edition)) {
    $buildArgs += @("--edition", $Edition)
}

if (-not [string]::IsNullOrWhiteSpace($Artwork)) {
    $buildArgs += @("--artwork", $Artwork)
}

if (-not [string]::IsNullOrWhiteSpace($SetupExePath)) {
    $buildArgs += @("--setup-exe", $SetupExePath)
}

& $gpackExe @buildArgs
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    $finalRepackDir = Join-Path $OutputDir "$GameName-$brandName"
    Write-Header "Repack Successfully Created!"
    Write-Success "Repack folder: $finalRepackDir"
    Write-Host "Contains:" -ForegroundColor Gray
    Write-Host "  - Setup.exe (Standalone graphical installer)" -ForegroundColor White
    Write-Host "  - $GameName.gpack (Package manifest)" -ForegroundColor White
    Write-Host "  - data\*.gpk (Compressed chunked payload)" -ForegroundColor White

    if (-not $Yes) {
        Write-Host "`nOpen output folder in Windows Explorer? [Y/n]: " -NoNewline -ForegroundColor Yellow
        $openChoice = Read-Host
        if ($openChoice -ne "n" -and $openChoice -ne "N") {
            if (Test-Path $finalRepackDir) {
                Start-Process "explorer.exe" -ArgumentList $finalRepackDir
            }
        }
    }
} else {
    Write-Error "Build failed with exit code $exitCode."
}
