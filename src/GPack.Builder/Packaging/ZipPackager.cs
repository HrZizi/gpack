using System.Diagnostics;
using System.IO.Compression;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;

namespace GPack.Builder.Packaging;

public class ZipPackager
{
    private readonly ILogger _logger;

    public ZipPackager(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
    }

    public async Task<string> CreateZipAsync(
        string sourceDirectory,
        string targetZipPath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInfo($"Creating ZIP archive at: {targetZipPath}");

        var targetDir = Path.GetDirectoryName(targetZipPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (File.Exists(targetZipPath))
        {
            File.Delete(targetZipPath);
        }

        var allFiles = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        long totalBytes = allFiles.Sum(f => new FileInfo(f).Length);
        long processedBytes = 0;

        _logger.LogInfo($"Archiving {allFiles.Length} files ({SizeFormatter.FormatBytes(totalBytes)}) to ZIP...");

        using (var zipStream = new FileStream(targetZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            byte[] buffer = new byte[81920];

            foreach (var file in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                var fi = new FileInfo(file);

                // Payload chunk files (.gpk) are already compressed with Zstd; store them without recompressing
                var compressionLevel = file.EndsWith(".gpk", StringComparison.OrdinalIgnoreCase)
                    ? CompressionLevel.NoCompression
                    : CompressionLevel.Fastest;

                var entry = archive.CreateEntry(relativePath, compressionLevel);
                entry.LastWriteTime = fi.LastWriteTimeUtc;

                using (var entryStream = entry.Open())
                using (var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                {
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await entryStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        processedBytes += bytesRead;

                        double ratio = totalBytes > 0 ? (double)processedBytes / totalBytes : 1.0;
                        double speed = stopwatch.Elapsed.TotalSeconds > 0 ? processedBytes / stopwatch.Elapsed.TotalSeconds : 0;
                        TimeSpan? remaining = speed > 0 && totalBytes > processedBytes
                            ? TimeSpan.FromSeconds((totalBytes - processedBytes) / speed)
                            : null;

                        progress?.Report(new InstallProgress(
                            Stage: InstallStage.Finalizing,
                            ProcessedBytes: processedBytes,
                            TotalBytes: totalBytes,
                            CurrentFile: relativePath,
                            ProgressRatio: ratio,
                            SpeedBytesPerSecond: speed,
                            EstimatedRemaining: remaining
                        ));
                    }
                }
            }
        }

        stopwatch.Stop();
        var generatedSize = new FileInfo(targetZipPath).Length;
        _logger.LogInfo($"ZIP archive created successfully: {SizeFormatter.FormatBytes(generatedSize)} in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return targetZipPath;
    }
}
