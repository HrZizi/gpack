using System.Diagnostics;
using System.IO.Compression;
using DiscUtils.Iso9660;
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
        if (Directory.Exists(manifestOrPackageDirectory))
        {
            var gpackFiles = Directory.GetFiles(manifestOrPackageDirectory, "*.gpack");
            if (gpackFiles.Length == 0)
            {
                throw new FileNotFoundException($"No .gpack manifest file found in directory '{manifestOrPackageDirectory}'.");
            }
            return await ManifestSerializer.LoadFromFileAsync(gpackFiles[0], cancellationToken);
        }

        if (File.Exists(manifestOrPackageDirectory))
        {
            string ext = Path.GetExtension(manifestOrPackageDirectory);

            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zipArchive = ZipFile.OpenRead(manifestOrPackageDirectory);
                var gpackEntry = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".gpack", StringComparison.OrdinalIgnoreCase));
                if (gpackEntry == null)
                {
                    throw new FileNotFoundException($"No .gpack manifest file found inside ZIP archive '{manifestOrPackageDirectory}'.");
                }

                using var entryStream = gpackEntry.Open();
                using var reader = new StreamReader(entryStream);
                string json = await reader.ReadToEndAsync(cancellationToken);
                return ManifestSerializer.Deserialize(json);
            }

            if (ext.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                using var isoFile = File.OpenRead(manifestOrPackageDirectory);
                using var cdReader = new CDReader(isoFile, joliet: true);
                var gpackFile = cdReader.GetFiles(string.Empty, "*.gpack", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    ?? cdReader.GetFiles(string.Empty, "*.*", SearchOption.AllDirectories).FirstOrDefault(f => f.EndsWith(".gpack", StringComparison.OrdinalIgnoreCase));

                if (gpackFile == null)
                {
                    throw new FileNotFoundException($"No .gpack manifest file found inside ISO image '{manifestOrPackageDirectory}'.");
                }

                using var entryStream = cdReader.OpenFile(gpackFile, FileMode.Open, FileAccess.Read);
                using var reader = new StreamReader(entryStream);
                string json = await reader.ReadToEndAsync(cancellationToken);
                return ManifestSerializer.Deserialize(json);
            }

            return await ManifestSerializer.LoadFromFileAsync(manifestOrPackageDirectory, cancellationToken);
        }

        throw new FileNotFoundException($"Package or manifest path not found: '{manifestOrPackageDirectory}'");
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
