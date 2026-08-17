using System.Diagnostics;
using DiscUtils.Iso9660;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;
using GPack.Core.Packaging;

namespace GPack.Builder.Packaging;

public class IsoPackager
{
    private readonly ILogger _logger;

    public IsoPackager(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
    }

    public async Task<string> CreateIsoAsync(
        string sourceDirectory,
        string targetIsoPath,
        string? volumeLabel = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInfo($"Creating ISO image at: {targetIsoPath}");

        var targetDir = Path.GetDirectoryName(targetIsoPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // 1. Ensure payload chunks are safe for ISO 9660 (<= 2 GB per chunk)
        if (Directory.GetFiles(sourceDirectory, "*.gpack").Length > 0)
        {
            await PackageRechunker.RechunkIfNeededAsync(
                sourceDirectory,
                PackageRechunker.IsoSafeMaxChunkSize,
                _logger,
                progress,
                cancellationToken);
        }

        var allFiles = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);

        // 2. Validate that no individual file exceeds the ISO 9660 limit (4 GB)
        foreach (var file in allFiles)
        {
            var fi = new FileInfo(file);
            if (fi.Length >= PackageRechunker.IsoMaxFileSizeLimit)
            {
                string rel = Path.GetRelativePath(sourceDirectory, file);
                throw new InvalidOperationException(
                    $"File '{rel}' ({SizeFormatter.FormatBytes(fi.Length)}) exceeds the ISO 9660 file size limit (4 GB). " +
                    $"ISO 9660 filesystem cannot store single files >= 4 GB. Use ZIP, RAR, or Directory format instead.");
            }
        }

        var builder = new CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = SanitizeVolumeLabel(volumeLabel ?? "GPACK_REPACK")
        };

        long totalBytes = allFiles.Sum(f => new FileInfo(f).Length);
        long processedBytes = 0;

        _logger.LogInfo($"Adding {allFiles.Length} files ({SizeFormatter.FormatBytes(totalBytes)}) to ISO...");

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            builder.AddFile(relativePath, file);

            var fi = new FileInfo(file);
            processedBytes += fi.Length;

            double ratio = totalBytes > 0 ? (double)processedBytes / totalBytes : 1.0;
            progress?.Report(new InstallProgress(
                Stage: InstallStage.Finalizing,
                ProcessedBytes: processedBytes,
                TotalBytes: totalBytes,
                CurrentFile: relativePath,
                ProgressRatio: ratio
            ));
        }

        _logger.LogInfo("Building ISO disc image file...");

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Build(targetIsoPath);
        }, cancellationToken);

        stopwatch.Stop();
        var generatedSize = new FileInfo(targetIsoPath).Length;
        _logger.LogInfo($"ISO image created successfully: {SizeFormatter.FormatBytes(generatedSize)} in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return targetIsoPath;
    }

    private static string SanitizeVolumeLabel(string label)
    {
        var chars = label.ToUpperInvariant()
            .Select(c => (char.IsAsciiLetterOrDigit(c) || c == '_') ? c : '_')
            .ToArray();
        string sanitized = new string(chars).Trim('_');
        if (string.IsNullOrEmpty(sanitized)) sanitized = "GPACK_REPACK";
        return sanitized.Length > 32 ? sanitized[..32] : sanitized;
    }
}
