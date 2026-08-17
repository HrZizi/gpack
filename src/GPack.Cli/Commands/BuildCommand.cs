using System.Text.Json;
using GPack.Builder;
using GPack.Core.Configuration;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Models.Events;

namespace GPack.Cli.Commands;

public static class BuildCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        BuildConfiguration config = ConfigurationMerger.CreateDefault();
        bool jsonMode = false;
        bool nonInteractive = false;
        bool quiet = false;
        bool verbose = false;

        // Parse CLI arguments
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                jsonMode = true;
            }
            else if (arg.Equals("--non-interactive", StringComparison.OrdinalIgnoreCase))
            {
                nonInteractive = true;
            }
            else if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || arg.Equals("-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
            }
            else if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
            }
            else if (arg.Equals("--config-json-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var loaded = JsonConfigLoader.LoadFromFile(args[++i]);
                config = ConfigurationMerger.Merge(config, loaded);
            }
            else if (arg.Equals("--config-json", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var loaded = JsonConfigLoader.LoadFromString(args[++i]);
                config = ConfigurationMerger.Merge(config, loaded);
            }
            else if (arg.Equals("--source", StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Source.Directory = args[++i];
            }
            else if (arg.Equals("--name", StringComparison.OrdinalIgnoreCase) || arg.Equals("-n", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Game.Name = args[++i];
            }
            else if (arg.Equals("--version", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Game.Version = args[++i];
            }
            else if (arg.Equals("--exe", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Launch.Executable = args[++i];
            }
            else if (arg.Equals("--launch-args", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Launch.Arguments = args[++i];
            }
            else if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase) || arg.Equals("-o", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Package.OutputDirectory = args[++i];
            }
            else if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase) || arg.Equals("-f", StringComparison.OrdinalIgnoreCase) || arg.Equals("--output-format", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Package.Format = args[++i];
            }
            else if (arg.Equals("--part-size", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Package.PartSize = args[++i];
            }
            else if (arg.Equals("--compression", StringComparison.OrdinalIgnoreCase) || arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Package.Compression = args[++i];
            }
            else if (arg.Equals("--compression-level", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && int.TryParse(args[++i], out var lvl)) config.Package.CompressionLevel = lvl;
            }
            else if (arg.Equals("--split-size", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    config.Package.SplitSize = args[++i];
                    config.Package.SplitSizeBytes = SizeFormatter.ParseSize(config.Package.SplitSize);
                }
            }
            else if (arg.Equals("--single-file", StringComparison.OrdinalIgnoreCase))
            {
                config.Package.SingleFile = true;
            }
            else if (arg.Equals("--desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                config.Installer.DesktopShortcut = true;
            }
            else if (arg.Equals("--start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                config.Installer.StartMenuShortcut = true;
            }
            else if (arg.Equals("--icon", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Installer.Icon = args[++i];
            }
            else if (arg.Equals("--artwork", StringComparison.OrdinalIgnoreCase) || arg.Equals("--cover", StringComparison.OrdinalIgnoreCase) || arg.Equals("--banner", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Game.Artwork = args[++i];
            }
            else if (arg.Equals("--edition", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Game.Edition = args[++i];
            }
            else if (arg.Equals("--setup-exe", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Installer.TemplatePath = args[++i];
            }
            else if (arg.Equals("--exclude", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Source.Exclude.Add(args[++i]);
            }
            else if (arg.Equals("--include", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) config.Source.Include.Add(args[++i]);
            }
            else if (arg.Equals("--action", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    var actionDef = ParseSimpleAction(args[++i]);
                    if (actionDef != null) config.Actions.Add(actionDef);
                }
            }
            else if (!arg.StartsWith("-"))
            {
                // Positional argument: YAML or JSON configuration file, or source folder
                if (File.Exists(arg))
                {
                    if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var loaded = JsonConfigLoader.LoadFromFile(arg);
                        config = ConfigurationMerger.Merge(config, loaded);
                    }
                    else
                    {
                        var loaded = YamlConfigLoader.LoadFromFile(arg);
                        config = ConfigurationMerger.Merge(config, loaded);
                    }
                }
                else if (Directory.Exists(arg))
                {
                    config.Source.Directory = arg;
                }
            }
        }

        config.Automation.Json = jsonMode;
        config.Automation.NonInteractive = nonInteractive;
        config.Automation.Quiet = quiet;
        config.Automation.Verbose = verbose;

        // Progress handling
        using var progress = new ConsoleProgressReporter(quiet: quiet, jsonMode: jsonMode);

        var buildEngine = new BuildEngine(logger);
        var result = await buildEngine.BuildAsync(config, progress, cancellationToken);

        if (!jsonMode && !quiet)
        {
            Console.WriteLine();
        }

        if (jsonMode)
        {
            if (result.Success)
            {
                var completeEvt = new JsonCompleteEvent
                {
                    Format = result.OutputFormat.ToString().ToLowerInvariant(),
                    OutputDirectory = result.OutputDirectory,
                    OutputArtifact = result.OutputArtifactPath,
                    Package = result.ManifestFilePath
                };
                Console.WriteLine(JsonSerializer.Serialize(completeEvt));
            }
            else
            {
                var errorEvt = new JsonErrorEvent
                {
                    Code = result.ExitCode.ToString(),
                    Message = result.ErrorMessage ?? "Build failed."
                };
                Console.WriteLine(JsonSerializer.Serialize(errorEvt));
            }
        }
        else
        {
            if (result.Success)
            {
                Console.WriteLine();
                Console.WriteLine("==================================================");
                Console.WriteLine("PACKAGING COMPLETE!");
                Console.WriteLine($"Format:        {result.OutputFormat.ToFriendlyString()}");
                if (!string.IsNullOrEmpty(result.OutputArtifactPath) && result.OutputFormat != PackageOutputFormat.Directory)
                {
                    Console.WriteLine($"Artifact:      {result.OutputArtifactPath}");
                    if (result.GeneratedFiles.Count > 1)
                    {
                        Console.WriteLine($"Total Files:   {result.GeneratedFiles.Count} archive parts");
                    }
                }
                else
                {
                    Console.WriteLine($"Output Folder: {result.OutputDirectory}");
                }
                Console.WriteLine($"Manifest:      {Path.GetFileName(result.ManifestFilePath)}");
                if (!string.IsNullOrEmpty(result.InstallerFilePath))
                    Console.WriteLine($"Installer:     {Path.GetFileName(result.InstallerFilePath)}");
                Console.WriteLine("==================================================");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"BUILD FAILED: {result.ErrorMessage}");
            }
        }

        return result.ExitCode;
    }

    private static ActionDefinition? ParseSimpleAction(string actionString)
    {
        // format: "copy|source=foo|destination=bar"
        var parts = actionString.Split('|');
        if (parts.Length == 0) return null;

        var def = new ActionDefinition { Type = parts[0].Trim() };
        for (int i = 1; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=', 2);
            if (kv.Length == 2)
            {
                string k = kv[0].Trim().ToLowerInvariant();
                string v = kv[1].Trim();
                switch (k)
                {
                    case "source": def.Source = v; break;
                    case "destination": def.Destination = v; break;
                    case "target": def.Target = v; break;
                    case "location": def.Location = v; break;
                    case "executable": def.Executable = v; break;
                    case "arguments": def.Arguments = v; break;
                    case "wait": def.Wait = bool.Parse(v); break;
                }
            }
        }
        return def;
    }
}
