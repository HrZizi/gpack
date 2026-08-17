namespace GPack.Core.Configuration;

public static class ConfigurationValidator
{
    public static List<string> Validate(BuildConfiguration config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Source.Directory))
        {
            errors.Add("Source directory must be specified (--source or source.directory in config).");
        }
        else if (!Directory.Exists(config.Source.Directory))
        {
            errors.Add($"Source directory does not exist: '{config.Source.Directory}'");
        }

        if (string.IsNullOrWhiteSpace(config.Game.Name))
        {
            errors.Add("Game name must be specified (--name or game.name in config).");
        }

        if (string.IsNullOrWhiteSpace(config.Launch.Executable))
        {
            // If executable is not specified, check if there's an executable in the source dir
            if (Directory.Exists(config.Source.Directory))
            {
                var exes = Directory.GetFiles(config.Source.Directory, "*.exe", SearchOption.TopDirectoryOnly);
                if (exes.Length == 1)
                {
                    config.Launch.Executable = Path.GetFileName(exes[0]);
                }
                else
                {
                    // If multiple or none, require explicit executable specification
                    errors.Add("Main launch executable must be specified (--exe or launch.executable in config).");
                }
            }
        }
        else if (Directory.Exists(config.Source.Directory))
        {
            var fullExePath = Path.Combine(config.Source.Directory, config.Launch.Executable);
            if (!File.Exists(fullExePath))
            {
                errors.Add($"Main executable '{config.Launch.Executable}' was not found in source directory '{config.Source.Directory}'.");
            }
        }

        if (config.Package.SplitSizeBytes < 1024 * 1024)
        {
            errors.Add($"Split size must be at least 1MB (was {config.Package.SplitSizeBytes} bytes).");
        }

        return errors;
    }
}
