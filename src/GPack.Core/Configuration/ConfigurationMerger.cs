namespace GPack.Core.Configuration;

public static class ConfigurationMerger
{
    public static BuildConfiguration CreateDefault()
    {
        return new BuildConfiguration
        {
            Game = new GameConfig
            {
                Name = "Game",
                Version = "1.0.0"
            },
            Source = new SourceConfig(),
            Launch = new LaunchConfig(),
            Package = new PackageConfig
            {
                Compression = "balanced",
                SplitSize = "4GB",
                SplitSizeBytes = 4L * 1024 * 1024 * 1024,
                SingleFile = false
            },
            Installer = new InstallerConfig
            {
                Brand = "NagelRepacks",
                DefaultDirectory = "%GAMES%\\{Name}",
                DesktopShortcut = true,
                StartMenuShortcut = true
            },
            Branding = new BrandingConfig(),
            Automation = new AutomationConfig()
        };
    }

    public static BuildConfiguration Merge(BuildConfiguration? baseConfig, BuildConfiguration? overrideConfig)
    {
        var result = baseConfig ?? CreateDefault();
        if (overrideConfig == null) return result;

        // Merge Game
        if (!string.IsNullOrWhiteSpace(overrideConfig.Game.Name))
            result.Game.Name = overrideConfig.Game.Name;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Game.Version))
            result.Game.Version = overrideConfig.Game.Version;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Game.Edition))
            result.Game.Edition = overrideConfig.Game.Edition;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Game.Artwork))
            result.Game.Artwork = overrideConfig.Game.Artwork;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Game.Publisher))
            result.Game.Publisher = overrideConfig.Game.Publisher;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Game.Developer))
            result.Game.Developer = overrideConfig.Game.Developer;

        // Merge Source
        if (!string.IsNullOrWhiteSpace(overrideConfig.Source.Directory))
            result.Source.Directory = overrideConfig.Source.Directory;
        if (overrideConfig.Source.Include.Count > 0)
            result.Source.Include.AddRange(overrideConfig.Source.Include);
        if (overrideConfig.Source.Exclude.Count > 0)
            result.Source.Exclude.AddRange(overrideConfig.Source.Exclude);

        // Merge Launch
        if (!string.IsNullOrWhiteSpace(overrideConfig.Launch.Executable))
            result.Launch.Executable = overrideConfig.Launch.Executable;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Launch.Arguments))
            result.Launch.Arguments = overrideConfig.Launch.Arguments;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Launch.WorkingDirectory))
            result.Launch.WorkingDirectory = overrideConfig.Launch.WorkingDirectory;

        // Merge Package
        if (!string.IsNullOrWhiteSpace(overrideConfig.Package.Format))
            result.Package.Format = overrideConfig.Package.Format;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Package.PartSize))
            result.Package.PartSize = overrideConfig.Package.PartSize;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Package.Compression))
            result.Package.Compression = overrideConfig.Package.Compression;
        if (overrideConfig.Package.CompressionLevel.HasValue)
            result.Package.CompressionLevel = overrideConfig.Package.CompressionLevel;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Package.SplitSize))
            result.Package.SplitSize = overrideConfig.Package.SplitSize;
        if (overrideConfig.Package.SplitSizeBytes > 0)
            result.Package.SplitSizeBytes = overrideConfig.Package.SplitSizeBytes;
        if (overrideConfig.Package.SingleFile)
            result.Package.SingleFile = overrideConfig.Package.SingleFile;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Package.OutputDirectory))
            result.Package.OutputDirectory = overrideConfig.Package.OutputDirectory;

        // Merge Installer
        if (!string.IsNullOrWhiteSpace(overrideConfig.Installer.Brand))
            result.Installer.Brand = overrideConfig.Installer.Brand;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Installer.DefaultDirectory))
            result.Installer.DefaultDirectory = overrideConfig.Installer.DefaultDirectory;
        result.Installer.DesktopShortcut = overrideConfig.Installer.DesktopShortcut;
        result.Installer.StartMenuShortcut = overrideConfig.Installer.StartMenuShortcut;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Installer.Icon))
            result.Installer.Icon = overrideConfig.Installer.Icon;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Installer.Artwork))
            result.Installer.Artwork = overrideConfig.Installer.Artwork;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Installer.TemplatePath))
            result.Installer.TemplatePath = overrideConfig.Installer.TemplatePath;

        // Merge Branding
        if (!string.IsNullOrWhiteSpace(overrideConfig.Branding.Icon))
            result.Branding.Icon = overrideConfig.Branding.Icon;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Branding.Artwork))
            result.Branding.Artwork = overrideConfig.Branding.Artwork;
        if (!string.IsNullOrWhiteSpace(overrideConfig.Branding.Banner))
            result.Branding.Banner = overrideConfig.Branding.Banner;

        // Merge Actions
        if (overrideConfig.Actions.Count > 0)
        {
            result.Actions.AddRange(overrideConfig.Actions);
        }

        // Merge Automation
        if (overrideConfig.Automation.NonInteractive)
            result.Automation.NonInteractive = true;
        if (overrideConfig.Automation.Json)
            result.Automation.Json = true;
        if (overrideConfig.Automation.Quiet)
            result.Automation.Quiet = true;
        if (overrideConfig.Automation.Verbose)
            result.Automation.Verbose = true;

        return result;
    }
}
