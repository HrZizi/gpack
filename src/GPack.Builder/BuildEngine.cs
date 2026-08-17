using GPack.Builder.Packaging;
using GPack.Core.Configuration;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Packaging;

namespace GPack.Builder;

public class BuildEngineResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; } = ExitCodes.Success;
    public PackageOutputFormat OutputFormat { get; set; } = PackageOutputFormat.Directory;
    public string OutputDirectory { get; set; } = string.Empty;
    public string ManifestFilePath { get; set; } = string.Empty;
    public string? InstallerFilePath { get; set; }
    public string? OutputArtifactPath { get; set; }
    public List<string> GeneratedFiles { get; set; } = new();
    public PackageManifest? Manifest { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan TotalTime { get; set; }
}

public class BuildEngine
{
    private readonly ILogger _logger;
    private readonly PackageBuilder _packageBuilder;
    private readonly InstallerBuilder _installerBuilder;
    private readonly IsoPackager _isoPackager;
    private readonly ZipPackager _zipPackager;
    private readonly RarPackager _rarPackager;

    public BuildEngine(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
        _packageBuilder = new PackageBuilder(_logger);
        _installerBuilder = new InstallerBuilder(_logger);
        _isoPackager = new IsoPackager(_logger);
        _zipPackager = new ZipPackager(_logger);
        _rarPackager = new RarPackager(_logger);
    }

    public async Task<BuildEngineResult> BuildAsync(
        BuildConfiguration configuration,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        string? stagingDir = null;

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

            var outputFormat = PackageOutputFormatExtensions.ParseFormat(configuration.Package.Format);
            _logger.LogInfo($"Selected output format: {outputFormat.ToFriendlyString()}");

            // If targeting ISO, automatically cap chunk split size to 2 GB for ISO 9660 compatibility
            if (outputFormat == PackageOutputFormat.Iso)
            {
                if (configuration.Package.SplitSizeBytes >= PackageRechunker.IsoMaxFileSizeLimit || configuration.Package.SplitSizeBytes > PackageRechunker.IsoSafeMaxChunkSize)
                {
                    _logger.LogInfo($"Adjusting payload chunk split size to {SizeFormatter.FormatBytes(PackageRechunker.IsoSafeMaxChunkSize)} for ISO 9660 filesystem compatibility.");
                    configuration.Package.SplitSizeBytes = PackageRechunker.IsoSafeMaxChunkSize;
                    configuration.Package.SplitSize = SizeFormatter.FormatBytes(PackageRechunker.IsoSafeMaxChunkSize);
                }
            }

            // 2. Resolve Output Paths
            string safeGameName = string.Join("-", configuration.Game.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string outputFolderName = $"{safeGameName}-{configuration.Installer.Brand}";
            string baseOutputDir = !string.IsNullOrWhiteSpace(configuration.Package.OutputDirectory)
                ? Path.GetFullPath(configuration.Package.OutputDirectory)
                : Path.GetDirectoryName(Path.GetFullPath(configuration.Source.Directory)) ?? Directory.GetCurrentDirectory();

            Directory.CreateDirectory(baseOutputDir);

            string packageWorkingDir;
            if (outputFormat == PackageOutputFormat.Directory)
            {
                packageWorkingDir = Path.Combine(baseOutputDir, outputFolderName);
                _logger.LogInfo($"Target output directory: {packageWorkingDir}");
            }
            else
            {
                stagingDir = Path.Combine(baseOutputDir, $".gpack-staging-{Guid.NewGuid():N}");
                packageWorkingDir = stagingDir;
                _logger.LogInfo($"Temporary staging directory: {packageWorkingDir}");
            }

            // 3. Build Package (Chunks + Manifest)
            var packageResult = await _packageBuilder.BuildPackageAsync(configuration, packageWorkingDir, progress, cancellationToken);
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
            string? installerPath = await _installerBuilder.GenerateInstallerAsync(configuration, packageWorkingDir, cancellationToken);

            // 5. Verify generated package
            _logger.LogInfo("Verifying generated package chunks...");
            progress?.Report(new InstallProgress(InstallStage.Verifying, 0, 0, null, 0));

            using (var testStream = new ChunkedInputStream(packageWorkingDir, packageResult.Manifest.Payload.Chunks, verifyHashesOnRead: true))
            {
                byte[] testBuffer = new byte[81920];
                while (await testStream.ReadAsync(testBuffer, 0, testBuffer.Length, cancellationToken) > 0) { }
            }

            _logger.LogInfo("Package chunk verification successful!");

            // 6. Output Format Packaging with Graceful Fallback
            string? primaryArtifactPath = null;
            var generatedFiles = new List<string>();

            try
            {
                switch (outputFormat)
                {
                    case PackageOutputFormat.Directory:
                        primaryArtifactPath = packageWorkingDir;
                        generatedFiles.Add(packageResult.ManifestFilePath);
                        if (!string.IsNullOrEmpty(installerPath)) generatedFiles.Add(installerPath);
                        foreach (var chunk in packageResult.Manifest.Payload.Chunks)
                        {
                            generatedFiles.Add(Path.Combine(packageWorkingDir, chunk.File.Replace('/', '\\')));
                        }
                        break;

                    case PackageOutputFormat.Iso:
                        string targetIsoPath = Path.Combine(baseOutputDir, $"{outputFolderName}.iso");
                        await _isoPackager.CreateIsoAsync(stagingDir!, targetIsoPath, safeGameName, progress, cancellationToken);
                        primaryArtifactPath = targetIsoPath;
                        generatedFiles.Add(targetIsoPath);
                        break;

                    case PackageOutputFormat.Zip:
                        string targetZipPath = Path.Combine(baseOutputDir, $"{outputFolderName}.zip");
                        await _zipPackager.CreateZipAsync(stagingDir!, targetZipPath, progress, cancellationToken);
                        primaryArtifactPath = targetZipPath;
                        generatedFiles.Add(targetZipPath);
                        break;

                    case PackageOutputFormat.Rar:
                        string targetRarPath = Path.Combine(baseOutputDir, $"{outputFolderName}.rar");
                        var rarFiles = await _rarPackager.CreateRarAsync(stagingDir!, targetRarPath, partSizeBytes: null, progress, cancellationToken);
                        primaryArtifactPath = targetRarPath;
                        generatedFiles.AddRange(rarFiles);
                        break;

                    case PackageOutputFormat.PartedRar:
                        long partSizeBytes = !string.IsNullOrWhiteSpace(configuration.Package.PartSize)
                            ? SizeFormatter.ParseSize(configuration.Package.PartSize)
                            : configuration.Package.SplitSizeBytes;

                        string targetPartedRarPath = Path.Combine(baseOutputDir, $"{outputFolderName}.rar");
                        var partedFiles = await _rarPackager.CreateRarAsync(stagingDir!, targetPartedRarPath, partSizeBytes: partSizeBytes, progress, cancellationToken);
                        primaryArtifactPath = partedFiles.FirstOrDefault() ?? targetPartedRarPath;
                        generatedFiles.AddRange(partedFiles);
                        break;
                }

                // 7. Cleanup staging directory if created
                if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
                {
                    try
                    {
                        _logger.LogInfo("Cleaning up temporary staging files...");
                        Directory.Delete(stagingDir, recursive: true);
                        stagingDir = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not cleanly remove staging folder: {ex.Message}");
                    }
                }
            }
            catch (Exception archiveEx) when (outputFormat != PackageOutputFormat.Directory && !string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
            {
                _logger.LogWarning($"Packaging to {outputFormat.ToFriendlyString()} failed ({archiveEx.Message}). Falling back to Directory output format.");
                string finalDir = Path.Combine(baseOutputDir, outputFolderName);
                if (Directory.Exists(finalDir))
                {
                    try { Directory.Delete(finalDir, recursive: true); } catch { }
                }
                Directory.Move(stagingDir, finalDir);
                stagingDir = null;

                outputFormat = PackageOutputFormat.Directory;
                primaryArtifactPath = finalDir;
                generatedFiles.Clear();
                generatedFiles.Add(Path.Combine(finalDir, Path.GetFileName(packageResult.ManifestFilePath)));
                if (!string.IsNullOrEmpty(installerPath)) generatedFiles.Add(Path.Combine(finalDir, Path.GetFileName(installerPath)));
                foreach (var chunk in packageResult.Manifest.Payload.Chunks)
                {
                    generatedFiles.Add(Path.Combine(finalDir, chunk.File.Replace('/', '\\')));
                }
            }

            progress?.Report(new InstallProgress(InstallStage.Finished, packageResult.TotalUncompressedBytes, packageResult.TotalUncompressedBytes, null, 1.0));

            var totalElapsed = DateTime.UtcNow - startTime;
            _logger.LogInfo($"=== Build finished successfully in {totalElapsed.TotalSeconds:F1}s ===");

            return new BuildEngineResult
            {
                Success = true,
                ExitCode = ExitCodes.Success,
                OutputFormat = outputFormat,
                OutputDirectory = outputFormat == PackageOutputFormat.Directory ? primaryArtifactPath! : baseOutputDir,
                ManifestFilePath = packageResult.ManifestFilePath,
                InstallerFilePath = installerPath,
                OutputArtifactPath = primaryArtifactPath,
                GeneratedFiles = generatedFiles,
                Manifest = packageResult.Manifest,
                TotalTime = totalElapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Build operation was cancelled.");
            CleanupStaging(stagingDir);
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
            CleanupStaging(stagingDir);
            return new BuildEngineResult
            {
                Success = false,
                ExitCode = ExitCodes.UnexpectedError,
                ErrorMessage = ex.Message
            };
        }
    }

    private void CleanupStaging(string? stagingDir)
    {
        if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
        {
            try
            {
                Directory.Delete(stagingDir, recursive: true);
            }
            catch { }
        }
    }
}
