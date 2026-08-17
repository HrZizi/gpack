using GPack.Core.Configuration;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;

namespace GPack.Cli.Commands;

public static class WizardCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=======================================================================");
        Console.WriteLine(" NagelRepacks / gpack — Guided Packaging Wizard");
        Console.WriteLine("=======================================================================");
        Console.WriteLine();

        string? sourceDir = null;
        string? gameName = null;
        string? gameVersion = null;
        string? gameEdition = null;
        string? gameArtwork = null;
        string? chosenExe = null;
        string? compression = null;
        string? splitSize = null;
        string? outputFormat = null;
        string? partSize = null;
        string? outputDir = null;
        string? setupExePath = null;
        bool? desktopShortcut = null;
        bool? startMenuShortcut = null;
        bool autoConfirm = false;
        bool nonInteractive = false;

        // Parse CLI arguments to pre-fill wizard answers
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if ((a.Equals("--source", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                sourceDir = args[++i];
            }
            else if ((a.Equals("--name", StringComparison.OrdinalIgnoreCase) || a.Equals("-n", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                gameName = args[++i];
            }
            else if (a.Equals("--version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                gameVersion = args[++i];
            }
            else if (a.Equals("--exe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                chosenExe = args[++i];
            }
            else if ((a.Equals("--compression", StringComparison.OrdinalIgnoreCase) || a.Equals("-c", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                compression = args[++i];
            }
            else if (a.Equals("--split-size", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                splitSize = args[++i];
            }
            else if ((a.Equals("--format", StringComparison.OrdinalIgnoreCase) || a.Equals("-f", StringComparison.OrdinalIgnoreCase) || a.Equals("--output-format", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                outputFormat = args[++i];
            }
            else if (a.Equals("--part-size", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                partSize = args[++i];
            }
            else if ((a.Equals("--output", StringComparison.OrdinalIgnoreCase) || a.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (a.Equals("--edition", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                gameEdition = args[++i];
            }
            else if ((a.Equals("--artwork", StringComparison.OrdinalIgnoreCase) || a.Equals("--cover", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                gameArtwork = args[++i];
            }
            else if (a.Equals("--setup-exe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                setupExePath = args[++i];
            }
            else if (a.Equals("--desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                desktopShortcut = true;
            }
            else if (a.Equals("--no-desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                desktopShortcut = false;
            }
            else if (a.Equals("--start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                startMenuShortcut = true;
            }
            else if (a.Equals("--no-start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                startMenuShortcut = false;
            }
            else if (a.Equals("--yes", StringComparison.OrdinalIgnoreCase) || a.Equals("-y", StringComparison.OrdinalIgnoreCase) || a.Equals("--auto-confirm", StringComparison.OrdinalIgnoreCase))
            {
                autoConfirm = true;
            }
            else if (a.Equals("--non-interactive", StringComparison.OrdinalIgnoreCase))
            {
                nonInteractive = true;
                autoConfirm = true;
            }
            else if (!a.StartsWith("-") && string.IsNullOrEmpty(sourceDir))
            {
                sourceDir = a;
            }
        }

        // 1. Source Directory
        while (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            if (nonInteractive)
            {
                logger.LogError("Source directory was not provided in non-interactive mode.");
                return ExitCodes.InvalidArguments;
            }

            Console.Write("Enter path to installed game directory: ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                sourceDir = input.Trim('"', '\'');
                if (!Directory.Exists(sourceDir))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Directory not found: '{sourceDir}'. Please try again.\n");
                    Console.ResetColor();
                }
            }
        }

        sourceDir = Path.GetFullPath(sourceDir);
        Console.WriteLine($"[✓] Source Directory: {sourceDir}");

        // 2. Inspection & Executable Detection
        Console.WriteLine("\nAnalyzing directory and detecting executables...");
        var inspection = await FileScanner.InspectDirectoryAsync(sourceDir, cancellationToken);
        string detectedFolder = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));

        if (string.IsNullOrWhiteSpace(chosenExe))
        {
            string defaultCandidate = inspection.LikelyMainExecutable ?? "";

            if (!nonInteractive && inspection.Executables.Count > 0)
            {
                Console.WriteLine("\nDetected Executables:");
                for (int i = 0; i < inspection.Executables.Count; i++)
                {
                    var exe = inspection.Executables[i];
                    string marker = exe.IsLikelyMain ? " [Recommended]" : "";
                    Console.WriteLine($"  [{i + 1}] {exe.RelativePath} ({SizeFormatter.FormatBytes(exe.Size)}){marker}");
                }

                Console.Write($"\nSelect launch executable (default: '{defaultCandidate}' or enter number): ");
                var exeInput = Console.ReadLine()?.Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(exeInput))
                {
                    if (int.TryParse(exeInput, out var num) && num >= 1 && num <= inspection.Executables.Count)
                    {
                        chosenExe = inspection.Executables[num - 1].RelativePath;
                    }
                    else
                    {
                        chosenExe = exeInput;
                    }
                }
                else
                {
                    chosenExe = defaultCandidate;
                }
            }
            else
            {
                chosenExe = defaultCandidate;
                if (string.IsNullOrWhiteSpace(chosenExe) && !nonInteractive)
                {
                    Console.Write("\nEnter relative path to launch executable (e.g. Game.exe or bin/Game.exe): ");
                    chosenExe = (Console.ReadLine() ?? "").Trim('"', '\'');
                }
            }
        }
        Console.WriteLine($"[✓] Launch Executable: {chosenExe}");

        // 3. Metadata
        if (string.IsNullOrWhiteSpace(gameName))
        {
            if (!nonInteractive)
            {
                Console.Write($"\nGame Name [Default: '{detectedFolder}']: ");
                var nameInput = Console.ReadLine()?.Trim();
                gameName = !string.IsNullOrWhiteSpace(nameInput) ? nameInput : detectedFolder;
            }
            else
            {
                gameName = detectedFolder;
            }
        }
        Console.WriteLine($"[✓] Game Name: {gameName}");

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            if (!nonInteractive)
            {
                Console.Write($"Version [Default: '1.0.0']: ");
                var verInput = Console.ReadLine()?.Trim();
                gameVersion = !string.IsNullOrWhiteSpace(verInput) ? verInput : "1.0.0";
            }
            else
            {
                gameVersion = "1.0.0";
            }
        }
        Console.WriteLine($"[✓] Version: {gameVersion}");

        if (string.IsNullOrWhiteSpace(gameArtwork))
        {
            string[] possibleArtworks = { "artwork.jpg", "artwork.png", "cover.jpg", "cover.png", "banner.jpg", "banner.png" };
            string? detectedArt = possibleArtworks.Select(a => Path.Combine(sourceDir, a)).FirstOrDefault(File.Exists);
            string artDefaultText = detectedArt != null ? Path.GetFileName(detectedArt) : "Default (Enter to skip)";

            if (!nonInteractive)
            {
                Console.Write($"Artwork / Cover image path or URL [Default: {artDefaultText}]: ");
                var artInput = Console.ReadLine()?.Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(artInput))
                {
                    gameArtwork = artInput;
                }
                else if (detectedArt != null)
                {
                    gameArtwork = detectedArt;
                }
            }
            else if (detectedArt != null)
            {
                gameArtwork = detectedArt;
            }
        }
        if (!string.IsNullOrWhiteSpace(gameArtwork))
        {
            Console.WriteLine($"[✓] Artwork: {gameArtwork}");
        }

        // 4. Compression Preset
        if (string.IsNullOrWhiteSpace(compression))
        {
            if (!nonInteractive)
            {
                Console.WriteLine("\nSelect Compression Preset:");
                Console.WriteLine("  [1] Balanced (Zstandard Level 10 — Recommended)");
                Console.WriteLine("  [2] Fast (Zstandard Level 3 — Faster build)");
                Console.WriteLine("  [3] Maximum (Zstandard Level 18 — Smallest size)");
                Console.WriteLine("  [4] None (Store uncompressed)");
                Console.Write("Choice [1-4, Default: 1]: ");
                var compInput = Console.ReadLine()?.Trim();
                compression = compInput switch
                {
                    "2" => "fast",
                    "3" => "maximum",
                    "4" => "none",
                    _ => "balanced"
                };
            }
            else
            {
                compression = "balanced";
            }
        }
        Console.WriteLine($"[✓] Compression: {compression}");

        // 5. Split Size
        if (string.IsNullOrWhiteSpace(splitSize))
        {
            if (!nonInteractive)
            {
                Console.WriteLine("\nSelect Payload Chunk Split Size:");
                Console.WriteLine("  [1] 4 GB (Standard FAT32 & USB drive compatible — Recommended)");
                Console.WriteLine("  [2] 2 GB (Smaller chunks)");
                Console.WriteLine("  [3] 8 GB (Larger chunks)");
                Console.WriteLine("  [4] Custom size");
                Console.Write("Choice [1-4, Default: 1]: ");
                var splitInput = Console.ReadLine()?.Trim();
                splitSize = splitInput switch
                {
                    "2" => "2GB",
                    "3" => "8GB",
                    "4" => PromptCustomSplitSize(),
                    _ => "4GB"
                };
            }
            else
            {
                splitSize = "4GB";
            }
        }
        Console.WriteLine($"[✓] Split Size: {splitSize}");

        // 6. Output Format
        if (string.IsNullOrWhiteSpace(outputFormat))
        {
            if (!nonInteractive)
            {
                Console.WriteLine("\nSelect Output Format:");
                Console.WriteLine("  [1] Directory / Folder (Default — Setup.exe + .gpack + .gpk chunks)");
                Console.WriteLine("  [2] ISO Disc Image (.iso)");
                Console.WriteLine("  [3] ZIP Archive (.zip)");
                Console.WriteLine("  [4] RAR Archive (.rar)");
                Console.WriteLine("  [5] Parted RAR Multi-Volume Archive (.part1.rar, .part2.rar, ...)");
                Console.Write("Choice [1-5, Default: 1]: ");
                var formatInput = Console.ReadLine()?.Trim();
                outputFormat = formatInput switch
                {
                    "2" => "iso",
                    "3" => "zip",
                    "4" => "rar",
                    "5" => "parted-rar",
                    _ => "directory"
                };
            }
            else
            {
                outputFormat = "directory";
            }
        }

        var parsedFormat = PackageOutputFormatExtensions.ParseFormat(outputFormat);
        Console.WriteLine($"[✓] Output Format: {parsedFormat.ToFriendlyString()}");

        // 7. Parted RAR Part Size (if applicable)
        if (parsedFormat == PackageOutputFormat.PartedRar && string.IsNullOrWhiteSpace(partSize))
        {
            if (!nonInteractive)
            {
                Console.WriteLine("\nSelect RAR Part / Volume Size:");
                Console.WriteLine("  [1] 4 GB (Standard FAT32 & USB drive compatible — Recommended)");
                Console.WriteLine("  [2] 2 GB");
                Console.WriteLine("  [3] 1 GB");
                Console.WriteLine("  [4] 500 MB");
                Console.WriteLine("  [5] Custom size");
                Console.Write("Choice [1-5, Default: 1]: ");
                var partInput = Console.ReadLine()?.Trim();
                partSize = partInput switch
                {
                    "2" => "2GB",
                    "3" => "1GB",
                    "4" => "500MB",
                    "5" => PromptCustomSplitSize(),
                    _ => "4GB"
                };
            }
            else
            {
                partSize = "4GB";
            }
            Console.WriteLine($"[✓] RAR Part Size: {partSize}");
        }

        // 8. Output Folder
        string defaultOutput = Path.GetDirectoryName(sourceDir) ?? Directory.GetCurrentDirectory();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            if (!nonInteractive)
            {
                Console.Write($"\nOutput Folder [Default: '{defaultOutput}']: ");
                var outInput = Console.ReadLine()?.Trim('"', '\'');
                outputDir = !string.IsNullOrWhiteSpace(outInput) ? outInput : defaultOutput;
            }
            else
            {
                outputDir = defaultOutput;
            }
        }
        Console.WriteLine($"[✓] Output Folder: {outputDir}");

        // 9. Summary & Confirmation
        string targetPreview = parsedFormat switch
        {
            PackageOutputFormat.Iso => Path.Combine(outputDir, $"{gameName}-NagelRepacks.iso"),
            PackageOutputFormat.Zip => Path.Combine(outputDir, $"{gameName}-NagelRepacks.zip"),
            PackageOutputFormat.Rar => Path.Combine(outputDir, $"{gameName}-NagelRepacks.rar"),
            PackageOutputFormat.PartedRar => Path.Combine(outputDir, $"{gameName}-NagelRepacks.part1.rar"),
            _ => Path.Combine(outputDir, $"{gameName}-NagelRepacks")
        };

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($" Game:        {gameName} (v{gameVersion})");
        Console.WriteLine($" Source:      {sourceDir}");
        Console.WriteLine($" Executable:  {chosenExe}");
        if (!string.IsNullOrWhiteSpace(gameArtwork))
        {
            Console.WriteLine($" Artwork:     {gameArtwork}");
        }
        Console.WriteLine($" Compression: {compression}");
        Console.WriteLine($" Split Size:  {splitSize}");
        Console.WriteLine($" Format:      {parsedFormat.ToFriendlyString()}");
        if (parsedFormat == PackageOutputFormat.PartedRar && !string.IsNullOrWhiteSpace(partSize))
        {
            Console.WriteLine($" Part Size:   {partSize}");
        }
        Console.WriteLine($" Target:      {targetPreview}");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        if (!autoConfirm && !nonInteractive)
        {
            Console.WriteLine("Action:");
            Console.WriteLine("  [1] Build Repack Installer Now");
            Console.WriteLine("  [2] Generate .gpack.yaml configuration only");
            Console.WriteLine("  [3] Cancel");
            Console.Write("Choice [1-3, Default: 1]: ");
            var actionChoice = Console.ReadLine()?.Trim();

            if (actionChoice == "3")
            {
                Console.WriteLine("Cancelled.");
                return ExitCodes.OperationCancelled;
            }

            if (actionChoice == "2")
            {
                string yamlPath = Path.Combine(outputDir, $"{gameName}.gpack.yaml");
                return await InitCommand.ExecuteAsync(new[] { sourceDir, "--output", yamlPath }, logger, cancellationToken);
            }
        }

        // Build Repack
        var buildArgs = new List<string>
        {
            "--source", sourceDir,
            "--name", gameName,
            "--version", gameVersion,
            "--exe", chosenExe,
            "--output", outputDir,
            "--compression", compression,
            "--split-size", splitSize,
            "--format", outputFormat,
            "--exclude", "*.log",
            "--exclude", "*.tmp",
            "--exclude", "CrashDumps/**"
        };

        if (!string.IsNullOrWhiteSpace(partSize))
        {
            buildArgs.Add("--part-size");
            buildArgs.Add(partSize);
        }

        if (desktopShortcut ?? true) buildArgs.Add("--desktop-shortcut");
        if (startMenuShortcut ?? true) buildArgs.Add("--start-menu-shortcut");
        if (!string.IsNullOrWhiteSpace(gameEdition))
        {
            buildArgs.Add("--edition");
            buildArgs.Add(gameEdition);
        }
        if (!string.IsNullOrWhiteSpace(gameArtwork))
        {
            buildArgs.Add("--artwork");
            buildArgs.Add(gameArtwork);
        }
        if (!string.IsNullOrWhiteSpace(setupExePath))
        {
            buildArgs.Add("--setup-exe");
            buildArgs.Add(setupExePath);
        }

        return await BuildCommand.ExecuteAsync(buildArgs.ToArray(), logger, cancellationToken);
    }

    private static string PromptCustomSplitSize()
    {
        Console.Write("Enter custom size (e.g. 500MB, 1.5GB): ");
        var s = Console.ReadLine()?.Trim();
        return !string.IsNullOrWhiteSpace(s) ? s : "4GB";
    }
}
