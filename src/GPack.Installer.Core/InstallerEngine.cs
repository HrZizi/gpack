using System.Diagnostics;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Installer.Core.Actions;

namespace GPack.Installer.Core;

public class InstallOptions
{
    public string PackagePathOrDirectory { get; set; } = string.Empty;
    public string DestinationDirectory { get; set; } = string.Empty;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool SkipIntegrityCheck { get; set; } = false;
    public bool LaunchAfterInstall { get; set; } = false;
}

public class InstallResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; } = ExitCodes.Success;
    public string DestinationDirectory { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string? ErrorMessage { get; set; }
}

public class InstallerEngine
{
    private readonly ILogger _logger;
    private readonly PackageReader _packageReader;
    private readonly FileVerifier _fileVerifier;
    private readonly ShortcutManager _shortcutManager;

    public InstallerEngine(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
        _packageReader = new PackageReader(_logger);
        _fileVerifier = new FileVerifier();
        _shortcutManager = new ShortcutManager(_logger);
    }

    public async Task<PackageManifest> AnalyzeAsync(
        string packagePathOrDirectory,
        CancellationToken cancellationToken = default)
    {
        return await PackageReader.LoadManifestAsync(packagePathOrDirectory, cancellationToken);
    }

    public async Task<InstallResult> InstallAsync(
        InstallOptions options,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string? tempStagingDir = null;

        try
        {
            _logger.LogInfo("=== Starting NagelRepacks Installation ===");

            // 1. Preparing & Loading Manifest
            progress?.Report(new InstallProgress(InstallStage.Preparing, 0, 0, null, 0));
            var manifest = await AnalyzeAsync(options.PackagePathOrDirectory, cancellationToken);

            string packageDirectory = File.Exists(options.PackagePathOrDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(options.PackagePathOrDirectory))!
                : Path.GetFullPath(options.PackagePathOrDirectory);

            string finalDestination = Path.GetFullPath(options.DestinationDirectory);
            _logger.LogInfo($"Installing '{manifest.Game.Name} v{manifest.Game.Version}' to '{finalDestination}'");

            // 2. Checking Package Chunks
            progress?.Report(new InstallProgress(InstallStage.CheckingPackage, 0, 0, null, 0));
            _logger.LogInfo("Checking package payload files...");
            foreach (var chunk in manifest.Payload.Chunks)
            {
                var chunkPath = Path.Combine(packageDirectory, chunk.File.Replace('/', '\\'));
                if (!File.Exists(chunkPath))
                {
                    throw new FileNotFoundException($"Missing package chunk file: {chunk.File}", chunkPath);
                }
            }

            // 3. Checking Disk Space
            progress?.Report(new InstallProgress(InstallStage.CheckingDiskSpace, 0, 0, null, 0));
            var spaceInfo = DiskSpaceChecker.CheckSpace(finalDestination, manifest.Payload.UncompressedSize);
            _logger.LogInfo($"Disk free space: {SizeFormatter.FormatBytes(spaceInfo.AvailableFreeSpaceBytes)} on {spaceInfo.DriveLetter} (Required: {SizeFormatter.FormatBytes(spaceInfo.RequiredSpaceBytes)})");

            if (!spaceInfo.HasSufficientSpace)
            {
                throw new InvalidOperationException($"Insufficient disk space on {spaceInfo.DriveLetter}. Available: {SizeFormatter.FormatBytes(spaceInfo.AvailableFreeSpaceBytes)}, Required: {SizeFormatter.FormatBytes(spaceInfo.RequiredSpaceBytes)}.");
            }

            // 4. Create Temporary Staging Directory
            progress?.Report(new InstallProgress(InstallStage.CreatingTemporaryEnvironment, 0, 0, null, 0));
            string parentDir = Path.GetDirectoryName(finalDestination) ?? Path.GetPathRoot(finalDestination) ?? "C:\\";
            Directory.CreateDirectory(parentDir);

            tempStagingDir = Path.Combine(parentDir, $".gpack-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempStagingDir);
            _logger.LogInfo($"Temporary staging directory: {tempStagingDir}");

            // 5. Extracting Payload to Staging Directory
            var extractResult = await _packageReader.ExtractPackageAsync(
                manifest,
                packageDirectory,
                tempStagingDir,
                progress,
                cancellationToken);

            if (!extractResult.Success)
            {
                throw new InvalidOperationException(extractResult.ErrorMessage ?? "Payload extraction failed.");
            }

            // 6. Verifying Extracted Files
            if (!options.SkipIntegrityCheck)
            {
                _logger.LogInfo("Verifying extracted files integrity...");
                var verifyResult = await _fileVerifier.VerifyInstalledFilesAsync(tempStagingDir, manifest.Files, progress, cancellationToken);
                if (!verifyResult.Success)
                {
                    throw new InvalidOperationException(verifyResult.ErrorMessage ?? "File integrity verification failed.");
                }
                _logger.LogInfo("All extracted files successfully verified.");
            }

            // 7. Execute Post-Install Actions
            if (manifest.Actions != null && manifest.Actions.Count > 0)
            {
                _logger.LogInfo($"Executing {manifest.Actions.Count} post-install actions...");
                progress?.Report(new InstallProgress(InstallStage.ExecutingActions, 0, 0, null, 0));

                var context = new InstallContext(tempStagingDir, packageDirectory, Path.GetTempPath(), msg => _logger.LogInfo(msg));
                await ActionExecutor.ExecuteActionsAsync(manifest.Actions, context, cancellationToken);
            }

            // 8. Finalizing: Move from Staging to Final Destination
            progress?.Report(new InstallProgress(InstallStage.Finalizing, 0, 0, null, 0));
            _logger.LogInfo($"Committing installation to target destination: {finalDestination}");

            if (!string.IsNullOrEmpty(tempStagingDir) && Directory.Exists(tempStagingDir))
            {
                string stagingPath = tempStagingDir;
                if (Directory.Exists(finalDestination))
                {
                    try
                    {
                        // Merge/copy staging files to final destination
                        CopyDirectoryRecursive(stagingPath, finalDestination);
                        Directory.Delete(stagingPath, recursive: true);
                        tempStagingDir = null;
                    }
                    catch
                    {
                        if (Directory.Exists(stagingPath))
                        {
                            Directory.Move(stagingPath, finalDestination);
                            tempStagingDir = null;
                        }
                    }
                }
                else
                {
                    Directory.Move(stagingPath, finalDestination);
                    tempStagingDir = null;
                }
            }

            // 9. Create Shortcuts
            string mainExePath = Path.Combine(finalDestination, manifest.Launch.Executable);
            if (File.Exists(mainExePath))
            {
                if (options.CreateDesktopShortcut)
                {
                    progress?.Report(new InstallProgress(InstallStage.CreatingShortcuts, 0, 0, "Desktop Shortcut", 0));
                    _shortcutManager.CreateDesktopShortcut(manifest.Game.Name, mainExePath);
                }

                if (options.CreateStartMenuShortcut)
                {
                    progress?.Report(new InstallProgress(InstallStage.CreatingShortcuts, 0, 0, "Start Menu Shortcut", 0));
                    _shortcutManager.CreateStartMenuShortcut(manifest.Game.Name, mainExePath, subFolder: manifest.Game.Name);
                }
            }

            stopwatch.Stop();
            _logger.LogInfo($"=== Installation completed successfully in {stopwatch.Elapsed.TotalSeconds:F1}s ===");
            progress?.Report(new InstallProgress(InstallStage.Finished, manifest.Payload.UncompressedSize, manifest.Payload.UncompressedSize, null, 1.0));

            return new InstallResult
            {
                Success = true,
                ExitCode = ExitCodes.Success,
                DestinationDirectory = finalDestination,
                ExecutablePath = mainExePath,
                Elapsed = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Installation was cancelled by user. Rolling back...");
            Rollback(tempStagingDir);
            progress?.Report(new InstallProgress(InstallStage.Cancelled, 0, 0, null, 0));

            return new InstallResult
            {
                Success = false,
                ExitCode = ExitCodes.OperationCancelled,
                ErrorMessage = "Installation was cancelled."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Installation failed: {ex.Message}", ex);
            Rollback(tempStagingDir);
            progress?.Report(new InstallProgress(InstallStage.Failed, 0, 0, null, 0));

            return new InstallResult
            {
                Success = false,
                ExitCode = ExitCodes.InstallationFailed,
                ErrorMessage = ex.Message
            };
        }
    }

    private void Rollback(string? stagingDir)
    {
        if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
        {
            try
            {
                _logger.LogInfo($"Deleting temporary staging directory: {stagingDir}");
                Directory.Delete(stagingDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to cleanly delete temporary staging directory: {ex.Message}");
            }
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(targetDir, rel);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
