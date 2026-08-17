using GPack.Core.Configuration;
using GPack.Core.Logging;

namespace GPack.Builder;

public class InstallerBuilder
{
    private readonly ILogger _logger;

    public InstallerBuilder(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
    }

    public async Task<string?> GenerateInstallerAsync(
        BuildConfiguration config,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        string targetSetupPath = Path.Combine(outputDirectory, "Setup.exe");

        // Try to locate Setup.exe template:
        // 1. Explicit configuration (config.Installer.TemplatePath or --setup-exe)
        // 2. Auto-discovery relative to current executable / environment
        string? setupSourcePath = !string.IsNullOrWhiteSpace(config.Installer.TemplatePath) && File.Exists(config.Installer.TemplatePath)
            ? Path.GetFullPath(config.Installer.TemplatePath)
            : FindSetupExecutable();

        if (setupSourcePath != null && File.Exists(setupSourcePath))
        {
            _logger.LogInfo($"Copying installer runtime from: {setupSourcePath}");
            File.Copy(setupSourcePath, targetSetupPath, overwrite: true);

            // Copy accompanying dependencies if any (only when not standalone single-file)
            var sourceDir = Path.GetDirectoryName(setupSourcePath);
            if (!string.IsNullOrEmpty(sourceDir))
            {
                foreach (var file in Directory.GetFiles(sourceDir, "Setup.*"))
                {
                    var destFile = Path.Combine(outputDirectory, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                    {
                        try { File.Copy(file, destFile, overwrite: true); } catch { }
                    }
                }
                foreach (var file in Directory.GetFiles(sourceDir, "GPack.*.dll"))
                {
                    var destFile = Path.Combine(outputDirectory, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                    {
                        try { File.Copy(file, destFile, overwrite: true); } catch { }
                    }
                }
                foreach (var file in Directory.GetFiles(sourceDir, "ZstdSharp.dll"))
                {
                    var destFile = Path.Combine(outputDirectory, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                    {
                        try { File.Copy(file, destFile, overwrite: true); } catch { }
                    }
                }
            }

            return targetSetupPath;
        }

        _logger.LogWarning("Installer template Setup.exe not found. Output package will be usable via 'gpack install'.");
        return null;
    }

    private static string? FindSetupExecutable()
    {
        // 1. Check environment variable
        var envPath = Environment.GetEnvironmentVariable("GPACK_SETUP_EXE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // 2. Check candidate search paths relative to gpack.exe
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string[] candidatePaths =
        {
            Path.Combine(baseDir, "Setup.exe"),
            Path.Combine(baseDir, "templates", "Setup.exe"),
            Path.Combine(baseDir, "installer", "Setup.exe"),
            Path.Combine(baseDir, "..", "installer", "Setup.exe"),
            Path.Combine(baseDir, "..", "standalone", "installer", "Setup.exe"),
            Path.Combine(baseDir, "..", "..", "standalone", "installer", "Setup.exe"),
            Path.Combine(baseDir, "..", "GPack.Installer.UI", "bin", "Release", "net10.0-windows", "Setup.exe"),
            Path.Combine(baseDir, "..", "GPack.Installer.UI", "bin", "Debug", "net10.0-windows", "Setup.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "artifacts", "standalone", "installer", "Setup.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "artifacts", "installer", "Setup.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "GPack.Installer.UI", "bin", "Release", "net10.0-windows", "Setup.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "GPack.Installer.UI", "bin", "Debug", "net10.0-windows", "Setup.exe")
        };

        foreach (var path in candidatePaths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        return null;
    }
}
