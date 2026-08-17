using System.Reflection;
using GPack.Cli.Commands;
using GPack.Core.Logging;
using GPack.Core.Models;

namespace GPack.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return ExitCodes.Success;
        }

        if (args[0].Equals("--version", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-v", StringComparison.OrdinalIgnoreCase))
        {
            var ver = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
            Console.WriteLine($"gpack v{ver} (NagelRepacks)");
            return ExitCodes.Success;
        }

        string command = args[0].ToLowerInvariant();
        string[] commandArgs = args.Skip(1).ToArray();

        bool isJson = args.Contains("--json");
        bool isQuiet = args.Contains("--quiet") || args.Contains("-q");
        bool isVerbose = args.Contains("--verbose");

        var consoleLogger = new ConsoleLogger(quiet: isQuiet, verbose: isVerbose, jsonMode: isJson);
        var fileLogger = new FileLogger(appName: "NagelRepacks");
        ILogger multiLogger = new MultiLogger(consoleLogger, fileLogger);

        try
        {
            return command switch
            {
                "wizard" => await WizardCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "init" => await InitCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "inspect" => await InspectCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "build" => await BuildCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "info" => await InfoCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "verify" => await VerifyCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "extract" => await ExtractCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                "install" => await InstallCommand.ExecuteAsync(commandArgs, multiLogger, cts.Token),
                _ => HandleUnknownCommand(command, multiLogger)
            };
        }
        catch (OperationCanceledException)
        {
            multiLogger.LogWarning("Operation cancelled by user.");
            return ExitCodes.OperationCancelled;
        }
        catch (Exception ex)
        {
            multiLogger.LogError($"Fatal unhandled exception: {ex.Message}", ex);
            return ExitCodes.UnexpectedError;
        }
    }

    private static int HandleUnknownCommand(string command, ILogger logger)
    {
        logger.LogError($"Unknown command: '{command}'");
        PrintHelp();
        return ExitCodes.InvalidArguments;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("=======================================================================");
        Console.WriteLine(" gpack — NagelRepacks Game Packaging Tool");
        Console.WriteLine("=======================================================================");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  gpack <command> [arguments] [options]");
        Console.WriteLine();
        Console.WriteLine("COMMANDS:");
        Console.WriteLine("  wizard [dir]               Interactive guided wizard to create game repacks");
        Console.WriteLine("  init <dir>                 Generate starter package YAML definition from directory");
        Console.WriteLine("  inspect <dir> [--largest]  Analyze directory, detect main executables and stats");
        Console.WriteLine("  build [config | options]   Build .gpack package and standalone installer");
        Console.WriteLine("  info <package.gpack>       Display package manifest metadata and statistics");
        Console.WriteLine("  verify <package.gpack>     Verify chunk and manifest SHA-256 integrity");
        Console.WriteLine("  extract <pkg> -o <dir>     Directly decompress and extract package payload");
        Console.WriteLine("  install <pkg> -d <dir>     Headless installer engine execution");
        Console.WriteLine();
        Console.WriteLine("GLOBAL OPTIONS:");
        Console.WriteLine("  --json                     Output machine-readable line-delimited JSON events");
        Console.WriteLine("  --non-interactive          Disable interactive prompts (for automation/Playnite)");
        Console.WriteLine("  --quiet, -q                Suppress non-essential progress output");
        Console.WriteLine("  --verbose                  Enable debug output");
        Console.WriteLine("  --version, -v              Display version information");
        Console.WriteLine("  --help, -h                 Display help instructions");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  gpack inspect \"D:\\Games\\Game\"");
        Console.WriteLine("  gpack build \"D:\\Games\\Game\" --name \"My Game\" --compression balanced");
        Console.WriteLine("  gpack build game.gpack.yaml --output \"D:\\Repacks\"");
        Console.WriteLine("  gpack verify \"D:\\Repacks\\MyGame-NagelRepacks\\MyGame.gpack\"");
        Console.WriteLine("  gpack install \"D:\\Repacks\\MyGame-NagelRepacks\\MyGame.gpack\" -d \"C:\\Games\\MyGame\"");
        Console.WriteLine();
    }
}
