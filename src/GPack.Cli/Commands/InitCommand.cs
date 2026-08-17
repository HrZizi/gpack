using GPack.Core.Configuration;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;

namespace GPack.Cli.Commands;

public static class InitCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack init <game-directory> [--output <file.yaml>]");
            return ExitCodes.InvalidArguments;
        }

        string sourceDir = Path.GetFullPath(args[0]);
        if (!Directory.Exists(sourceDir))
        {
            logger.LogError($"Directory not found: {sourceDir}");
            return ExitCodes.SourceNotFound;
        }

        string outputFile = "game.gpack.yaml";
        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputFile = args[i + 1];
                i++;
            }
        }

        logger.LogInfo($"Inspecting source directory: {sourceDir}");
        var inspection = await FileScanner.InspectDirectoryAsync(sourceDir, cancellationToken);

        string gameName = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));
        string mainExe = inspection.LikelyMainExecutable ?? "";

        var config = new BuildConfiguration
        {
            Game = new GameConfig
            {
                Name = gameName,
                Version = "1.0.0"
            },
            Source = new SourceConfig
            {
                Directory = sourceDir,
                Exclude = new List<string>
                {
                    "*.log",
                    "*.tmp",
                    "CrashDumps/**",
                    "Screenshots/**"
                }
            },
            Launch = new LaunchConfig
            {
                Executable = mainExe
            },
            Package = new PackageConfig
            {
                Compression = "balanced",
                SplitSize = "4GB"
            },
            Installer = new InstallerConfig
            {
                Brand = "NagelRepacks",
                DefaultDirectory = $"%GAMES%\\{gameName}",
                DesktopShortcut = true,
                StartMenuShortcut = true
            }
        };

        var yaml = YamlConfigLoader.Serialize(config);
        await File.WriteAllTextAsync(outputFile, yaml, cancellationToken);

        logger.LogInfo($"Generated package configuration file: {Path.GetFullPath(outputFile)}");
        Console.WriteLine();
        Console.WriteLine($"You can now build your package using:\n  gpack build \"{outputFile}\"");
        return ExitCodes.Success;
    }
}
