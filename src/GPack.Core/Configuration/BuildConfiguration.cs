using GPack.Core.Manifest;

namespace GPack.Core.Configuration;

public class BuildConfiguration
{
    public GameConfig Game { get; set; } = new();
    public SourceConfig Source { get; set; } = new();
    public LaunchConfig Launch { get; set; } = new();
    public PackageConfig Package { get; set; } = new();
    public InstallerConfig Installer { get; set; } = new();
    public BrandingConfig Branding { get; set; } = new();
    public List<ActionDefinition> Actions { get; set; } = new();
    public AutomationConfig Automation { get; set; } = new();
}

public class GameConfig
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string? Edition { get; set; }
    public string? Artwork { get; set; }
    public string? Publisher { get; set; }
    public string? Developer { get; set; }
}

public class SourceConfig
{
    public string Directory { get; set; } = string.Empty;
    public List<string> Include { get; set; } = new();
    public List<string> Exclude { get; set; } = new();
}

public class LaunchConfig
{
    public string Executable { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
}

public class PackageConfig
{
    public string Compression { get; set; } = "balanced"; // none | fast | balanced | maximum
    public int? CompressionLevel { get; set; }
    public string SplitSize { get; set; } = "4GB";
    public long SplitSizeBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    public bool SingleFile { get; set; } = false;
    public string OutputDirectory { get; set; } = string.Empty;
}

public class InstallerConfig
{
    public string Brand { get; set; } = "NagelRepacks";
    public string DefaultDirectory { get; set; } = "%GAMES%\\{Name}";
    public bool DesktopShortcut { get; set; } = true;
    public bool StartMenuShortcut { get; set; } = true;
    public string? Icon { get; set; }
    public string? Artwork { get; set; }
    public string? TemplatePath { get; set; }
}

public class BrandingConfig
{
    public string? Icon { get; set; }
    public string? Artwork { get; set; }
    public string? Banner { get; set; }
}

public class AutomationConfig
{
    public bool NonInteractive { get; set; } = false;
    public bool Json { get; set; } = false;
    public bool Quiet { get; set; } = false;
    public bool Verbose { get; set; } = false;
}
