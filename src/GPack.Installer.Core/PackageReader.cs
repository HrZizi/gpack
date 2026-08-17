using System.Diagnostics;
using GPack.Core.Compression;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Packaging;

namespace GPack.Installer.Core;

public class PackageExtractionResult
{
    public bool Success { get; set; }
    public int FilesExtracted { get; set; }
    public long BytesExtracted { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PackageReader
{
    private readonly ILogger _logger;

    public PackageReader(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
    }

    public static async Task<PackageManifest> LoadManifestAsync(string manifestOrPackageDirectory, CancellationToken cancellationToken = default)
    {
        string manifestPath = manifestOrPackageDirectory;

        if (Directory.Exists(manifestOrPackageDirectory))
        {
            var gpackFiles = Directory.GetFiles(manifestOrPackageDirectory, "*.gpack");
            if (gpackFiles.Length == 0)
            {
                throw new FileNotFoundException($"No .gpack manifest file found in directory '{manifestOrPackageDirectory}'.");
            }
            manifestPath = gpackFiles[0];
        }

        return await ManifestSerializer.LoadFromFileAsync(manifestPath, cancellationToken);
    }

    public async Task<PackageExtractionResult> ExtractPackageAsync(
        PackageManifest manifest,
        string packageDirectory,
        string targetDirectory,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(targetDirectory);

        long totalUncompressedBytes = manifest.Payload.UncompressedSize;
        long processedBytes = 0;
        int filesExtracted = 0;

        _logger.LogInfo($"Starting package extraction to: {targetDirectory}");
        _logger.LogInfo($"Total payload: {manifest.Payload.TotalFiles} files ({SizeFormatter.FormatBytes(totalUncompressedBytes)}).");

        // 1. Open Chunked InputStream
        using var chunkedStream = new ChunkedInputStream(packageDirectory, manifest.Payload.Chunks, verifyHashesOnRead: true);

        // 2. Open Zstandard Decompression Stream
        var zstd = new ZstdCompressionProvider();
        using var decompressor = zstd.CreateDecompressionStream(chunkedStream, leaveOpen: true);
        var payloadReader = new PayloadStreamReader(decompressor);

        var speedStopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var header = await payloadReader.ReadNextEntryHeaderAsync(cancellationToken);
            if (header == null)
            {
                break; // End of payload reached
            }

            string targetFilePath = PathSecurity.EnsureSafePath(targetDirectory, header.RelativePath);
            var fileDir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            using (var destFile = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var fileBytesProgress = new Progress<long>(readBytes =>
                {
                    processedBytes += readBytes;
                    double speed = speedStopwatch.Elapsed.TotalSeconds > 0
                        ? processedBytes / speedStopwatch.Elapsed.TotalSeconds
                        : 0;
                    double ratio = totalUncompressedBytes > 0 ? (double)processedBytes / totalUncompressedBytes : 1.0;
                    TimeSpan? remaining = speed > 0 && totalUncompressedBytes > processedBytes
                        ? TimeSpan.FromSeconds((totalUncompressedBytes - processedBytes) / speed)
                        : null;

                    progress?.Report(new InstallProgress(
                        Stage: InstallStage.Extracting,
                        ProcessedBytes: processedBytes,
                        TotalBytes: totalUncompressedBytes,
                        CurrentFile: header.RelativePath,
                        ProgressRatio: ratio,
                        SpeedBytesPerSecond: speed,
                        EstimatedRemaining: remaining
                    ));
                });

                await payloadReader.ExtractFileContentAsync(destFile, header.FileSize, fileBytesProgress, cancellationToken);
            }

            // Restore timestamps and attributes
            try
            {
                if (header.LastModifiedUtc > DateTime.MinValue)
                {
                    File.SetLastWriteTimeUtc(targetFilePath, header.LastModifiedUtc);
                }
                if (header.Attributes != FileAttributes.Normal && (int)header.Attributes != 0)
                {
                    File.SetAttributes(targetFilePath, header.Attributes);
                }
            }
            catch { }

            filesExtracted++;
        }

        stopwatch.Stop();
        _logger.LogInfo($"Extracted {filesExtracted} files in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return new PackageExtractionResult
        {
            Success = true,
            FilesExtracted = filesExtracted,
            BytesExtracted = processedBytes,
            Elapsed = stopwatch.Elapsed
        };
    }
}
