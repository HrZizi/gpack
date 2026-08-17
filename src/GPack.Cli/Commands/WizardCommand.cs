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

        // 6. Output Folder
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

        // 7. Summary & Confirmation
        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($" Game:        {gameName} (v{gameVersion})");
        Console.WriteLine($" Source:      {sourceDir}");
        Console.WriteLine($" Executable:  {chosenExe}");
        Console.WriteLine($" Compression: {compression}");
        Console.WriteLine($" Split Size:  {splitSize}");
        Console.WriteLine($" Output:      {Path.Combine(outputDir, $"{gameName}-NagelRepacks")}");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        if (!autoConfirm && !nonInteractive)
        {
            Console.WriteLine("Action:");
            Console.WriteLine("  [1] Build Repack Installer Now (Setup.exe + .gpack + .gpk)");
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
            "--exclude", "*.log",
            "--exclude", "*.tmp",
            "--exclude", "CrashDumps/**"
        };

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
        Console.Write("Enter custom chunk size (e.g. 500MB, 1.5GB): ");
        var s = Console.ReadLine()?.Trim();
        return !string.IsNullOrWhiteSpace(s) ? s : "4GB";
    }
}
