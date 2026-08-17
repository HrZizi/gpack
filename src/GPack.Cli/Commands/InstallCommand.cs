using System.Diagnostics;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;
using GPack.Installer.Core;

namespace GPack.Cli.Commands;

public static class InstallCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack install <package.gpack | directory> --destination <install-dir> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --destination, -d <path>    Installation destination path");
            Console.WriteLine("  --desktop-shortcut          Create desktop shortcut (default: true)");
            Console.WriteLine("  --no-desktop-shortcut       Do not create desktop shortcut");
            Console.WriteLine("  --start-menu-shortcut       Create Start Menu shortcut (default: true)");
            Console.WriteLine("  --no-start-menu-shortcut    Do not create Start Menu shortcut");
            Console.WriteLine("  --no-verify                 Skip post-install SHA-256 verification");
            Console.WriteLine("  --launch                    Launch game immediately after installation");
            return ExitCodes.InvalidArguments;
        }

        var options = new InstallOptions
        {
            PackagePathOrDirectory = Path.GetFullPath(args[0]),
            CreateDesktopShortcut = true,
            CreateStartMenuShortcut = true,
            SkipIntegrityCheck = false,
            LaunchAfterInstall = false
        };

        bool jsonMode = false;
        bool quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if ((arg.Equals("--destination", StringComparison.OrdinalIgnoreCase) || arg.Equals("-d", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                options.DestinationDirectory = args[++i];
            }
            else if (arg.Equals("--desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                options.CreateDesktopShortcut = true;
            }
            else if (arg.Equals("--no-desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                options.CreateDesktopShortcut = false;
            }
            else if (arg.Equals("--start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                options.CreateStartMenuShortcut = true;
            }
            else if (arg.Equals("--no-start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                options.CreateStartMenuShortcut = false;
            }
            else if (arg.Equals("--no-verify", StringComparison.OrdinalIgnoreCase))
            {
                options.SkipIntegrityCheck = true;
            }
            else if (arg.Equals("--launch", StringComparison.OrdinalIgnoreCase))
            {
                options.LaunchAfterInstall = true;
            }
            else if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                jsonMode = true;
            }
            else if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || arg.Equals("-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
            }
        }

        if (string.IsNullOrWhiteSpace(options.DestinationDirectory))
        {
            logger.LogError("Destination directory must be specified (--destination <path>).");
            return ExitCodes.InvalidArguments;
        }

        using var progress = new ConsoleProgressReporter(quiet: quiet, jsonMode: jsonMode);

        var engine = new InstallerEngine(logger);
        var result = await engine.InstallAsync(options, progress, cancellationToken);

        if (!quiet && !jsonMode) Console.WriteLine();
        if (result.Success)
        {
            logger.LogInfo($"Installation successful to: {result.DestinationDirectory}");

            if (options.LaunchAfterInstall && !string.IsNullOrEmpty(result.ExecutablePath) && File.Exists(result.ExecutablePath))
            {
                logger.LogInfo($"Launching: {result.ExecutablePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = result.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(result.ExecutablePath)
                });
            }

            return ExitCodes.Success;
        }
        else
        {
            logger.LogError($"Installation failed: {result.ErrorMessage}");
            return result.ExitCode;
        }
    }
}
