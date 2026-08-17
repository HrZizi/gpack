using GPack.Core.Configuration;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Packaging;

namespace GPack.Builder;

public class BuildEngineResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; } = ExitCodes.Success;
    public string OutputDirectory { get; set; } = string.Empty;
    public string ManifestFilePath { get; set; } = string.Empty;
    public string? InstallerFilePath { get; set; }
    public PackageManifest? Manifest { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan TotalTime { get; set; }
}

public class BuildEngine
{
    private readonly ILogger _logger;
    private readonly PackageBuilder _packageBuilder;
    private readonly InstallerBuilder _installerBuilder;

    public BuildEngine(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
        _packageBuilder = new PackageBuilder(_logger);
        _installerBuilder = new InstallerBuilder(_logger);
    }

    public async Task<BuildEngineResult> BuildAsync(
        BuildConfiguration configuration,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInfo($"=== Starting NagelRepacks build for '{configuration.Game.Name}' ===");

            // 1. Validation
            var validationErrors = ConfigurationValidator.Validate(configuration);
            if (validationErrors.Count > 0)
            {
                foreach (var err in validationErrors)
                {
                    _logger.LogError($"Config error: {err}");
                }
                return new BuildEngineResult
                {
                    Success = false,
                    ExitCode = ExitCodes.InvalidConfiguration,
                    ErrorMessage = string.Join("; ", validationErrors)
                };
            }

            // 2. Resolve Output Directory
            string safeGameName = string.Join("-", configuration.Game.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string outputFolderName = $"{safeGameName}-{configuration.Installer.Brand}";
            string baseOutputDir = !string.IsNullOrWhiteSpace(configuration.Package.OutputDirectory)
                ? configuration.Package.OutputDirectory
                : Path.GetDirectoryName(Path.GetFullPath(configuration.Source.Directory)) ?? Directory.GetCurrentDirectory();

            string finalOutputDir = Path.Combine(baseOutputDir, outputFolderName);
            _logger.LogInfo($"Target output directory: {finalOutputDir}");

            // 3. Build Package (Chunks + Manifest)
            var packageResult = await _packageBuilder.BuildPackageAsync(configuration, finalOutputDir, progress, cancellationToken);
            if (!packageResult.Success)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ExitCode = ExitCodes.PackageCreationFailed,
                    ErrorMessage = packageResult.ErrorMessage ?? "Package build failed."
                };
            }

            // 4. Generate Setup.exe frontend
            string? installerPath = await _installerBuilder.GenerateInstallerAsync(configuration, finalOutputDir, cancellationToken);

            // 5. Verify generated package
            _logger.LogInfo("Verifying generated package chunks...");
            progress?.Report(new InstallProgress(InstallStage.Verifying, 0, 0, null, 0));

            using (var testStream = new ChunkedInputStream(finalOutputDir, packageResult.Manifest.Payload.Chunks, verifyHashesOnRead: true))
            {
                byte[] testBuffer = new byte[81920];
                while (await testStream.ReadAsync(testBuffer, 0, testBuffer.Length, cancellationToken) > 0) { }
            }

            _logger.LogInfo("Package verification successful!");
            progress?.Report(new InstallProgress(InstallStage.Finished, packageResult.TotalUncompressedBytes, packageResult.TotalUncompressedBytes, null, 1.0));

            var totalElapsed = DateTime.UtcNow - startTime;
            _logger.LogInfo($"=== Build finished successfully in {totalElapsed.TotalSeconds:F1}s ===");

            return new BuildEngineResult
            {
                Success = true,
                ExitCode = ExitCodes.Success,
                OutputDirectory = finalOutputDir,
                ManifestFilePath = packageResult.ManifestFilePath,
                InstallerFilePath = installerPath,
                Manifest = packageResult.Manifest,
                TotalTime = totalElapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Build operation was cancelled.");
            return new BuildEngineResult
            {
                Success = false,
                ExitCode = ExitCodes.OperationCancelled,
                ErrorMessage = "Operation cancelled by user."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Build failed with exception: {ex.Message}", ex);
            return new BuildEngineResult
            {
                Success = false,
                ExitCode = ExitCodes.UnexpectedError,
                ErrorMessage = ex.Message
            };
        }
    }
}
