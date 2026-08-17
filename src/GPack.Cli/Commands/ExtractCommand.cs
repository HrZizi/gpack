using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Installer.Core;

namespace GPack.Cli.Commands;

public static class ExtractCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack extract <package.gpack | directory> --output <target-dir>");
            return ExitCodes.InvalidArguments;
        }

        string target = Path.GetFullPath(args[0]);
        string? outputDir = null;
        bool jsonMode = false;
        bool quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (args[i].Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                jsonMode = true;
            }
            else if (args[i].Equals("--quiet", StringComparison.OrdinalIgnoreCase) || args[i].Equals("-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
            }
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            logger.LogError("Output directory must be specified (--output <path>).");
            return ExitCodes.InvalidArguments;
        }

        PackageManifest manifest;
        try
        {
            manifest = await PackageReader.LoadManifestAsync(target, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to load manifest: {ex.Message}");
            return ExitCodes.InvalidConfiguration;
        }

        string packageDir = File.Exists(target) ? Path.GetDirectoryName(target)! : target;
        string fullOutputDir = Path.GetFullPath(outputDir);

        logger.LogInfo($"Extracting '{manifest.Game.Name}' to '{fullOutputDir}'...");

        using var progress = new ConsoleProgressReporter(quiet: quiet, jsonMode: jsonMode);

        var reader = new PackageReader(logger);
        var result = await reader.ExtractPackageAsync(manifest, packageDir, fullOutputDir, progress, cancellationToken);

        if (!quiet && !jsonMode) Console.WriteLine();
        if (result.Success)
        {
            logger.LogInfo($"Extraction complete: {result.FilesExtracted} files extracted in {result.Elapsed.TotalSeconds:F1}s.");
            return ExitCodes.Success;
        }
        else
        {
            logger.LogError($"Extraction failed: {result.ErrorMessage}");
            return ExitCodes.InstallationFailed;
        }
    }
}
