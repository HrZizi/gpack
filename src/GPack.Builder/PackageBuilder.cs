using System.Diagnostics;
using GPack.Core.Compression;
using GPack.Core.Configuration;
using GPack.Core.Files;
using GPack.Core.Hashing;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Packaging;

namespace GPack.Builder;

public class PackageBuildResult
{
    public bool Success { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public string ManifestFilePath { get; set; } = string.Empty;
    public PackageManifest Manifest { get; set; } = new();
    public TimeSpan Elapsed { get; set; }
    public long TotalUncompressedBytes { get; set; }
    public long TotalCompressedBytes { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PackageBuilder
{
    private readonly ILogger _logger;
    private readonly IHashProvider _hashProvider;

    public PackageBuilder(ILogger? logger = null, IHashProvider? hashProvider = null)
    {
        _logger = logger ?? new ConsoleLogger();
        _hashProvider = hashProvider ?? new Sha256HashProvider();
    }

    public async Task<PackageBuildResult> BuildPackageAsync(
        BuildConfiguration config,
        string outputDirectory,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(outputDirectory);
        string dataDirectory = Path.Combine(outputDirectory, "data");
        Directory.CreateDirectory(dataDirectory);

        _logger.LogInfo($"Scanning source directory: {config.Source.Directory}");
        progress?.Report(new InstallProgress(InstallStage.Scanning, 0, 0, null, 0));

        // 1. Scan files
        var scannedFiles = await FileScanner.ScanAsync(
            config.Source.Directory,
            config.Source.Include,
            config.Source.Exclude,
            progress,
            cancellationToken);

        if (scannedFiles.Count == 0)
        {
            throw new InvalidOperationException($"No files found to package in source directory '{config.Source.Directory}'.");
        }

        long totalUncompressedBytes = scannedFiles.Sum(f => f.Size);
        _logger.LogInfo($"Found {scannedFiles.Count} files ({SizeFormatter.FormatBytes(totalUncompressedBytes)}).");

        // 2. Hash files in parallel with bounded concurrency
        _logger.LogInfo("Computing file SHA-256 hashes...");
        var fileEntries = new List<FileEntry>(scannedFiles.Count);
        long hashedBytes = 0;
        var hashLock = new object();
        int maxConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

        using (var throttler = new SemaphoreSlim(maxConcurrency))
        {
            var hashTasks = scannedFiles.Select(async scanned =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hash = await _hashProvider.ComputeFileHashAsync(scanned.FullPath, cancellationToken);
                    var entry = new FileEntry
                    {
                        Path = scanned.RelativePath,
                        Size = scanned.Size,
                        Sha256 = hash,
                        Attributes = scanned.Attributes,
                        LastModifiedUtc = scanned.LastModifiedUtc
                    };

                    lock (hashLock)
                    {
                        fileEntries.Add(entry);
                        hashedBytes += scanned.Size;
                        double ratio = totalUncompressedBytes > 0 ? (double)hashedBytes / totalUncompressedBytes : 1.0;

                        progress?.Report(new InstallProgress(
                            Stage: InstallStage.Hashing,
                            ProcessedBytes: hashedBytes,
                            TotalBytes: totalUncompressedBytes,
                            CurrentFile: scanned.RelativePath,
                            ProgressRatio: ratio
                        ));
                    }
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(hashTasks);
        }

        // Sort entries deterministically by relative path
        fileEntries.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        // 3. Setup Streaming Compression and Chunking
        int compressionLevel = CompressionPreset.ResolveLevel(config.Package.Compression, config.Package.CompressionLevel);
        _logger.LogInfo($"Compressing payload using Zstandard (level {compressionLevel}, split size {SizeFormatter.FormatBytes(config.Package.SplitSizeBytes)})...");

        var zstd = new ZstdCompressionProvider();
        using var chunkedOutputStream = new ChunkedOutputStream(dataDirectory, config.Package.SplitSizeBytes);

        long processedPayloadBytes = 0;
        var compressionStopwatch = Stopwatch.StartNew();

        using (var compressionStream = zstd.CreateCompressionStream(chunkedOutputStream, compressionLevel, leaveOpen: true))
        {
            var payloadWriter = new PayloadStreamWriter(compressionStream);

            foreach (var entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullSourcePath = Path.Combine(config.Source.Directory, entry.Path);
                await payloadWriter.WriteFileEntryAsync(
                    entry.Path,
                    fullSourcePath,
                    entry.Size,
                    entry.LastModifiedUtc,
                    entry.Attributes,
                    cancellationToken);

                processedPayloadBytes += entry.Size;
                double speed = compressionStopwatch.Elapsed.TotalSeconds > 0
                    ? processedPayloadBytes / compressionStopwatch.Elapsed.TotalSeconds
                    : 0;
                double ratio = totalUncompressedBytes > 0 ? (double)processedPayloadBytes / totalUncompressedBytes : 1.0;

                TimeSpan? remaining = speed > 0 && totalUncompressedBytes > processedPayloadBytes
                    ? TimeSpan.FromSeconds((totalUncompressedBytes - processedPayloadBytes) / speed)
                    : null;

                progress?.Report(new InstallProgress(
                    Stage: InstallStage.Compressing,
                    ProcessedBytes: processedPayloadBytes,
                    TotalBytes: totalUncompressedBytes,
                    CurrentFile: entry.Path,
                    ProgressRatio: ratio,
                    SpeedBytesPerSecond: speed,
                    EstimatedRemaining: remaining
                ));
            }

            await payloadWriter.WriteEndAsync(cancellationToken);
            await compressionStream.FlushAsync(cancellationToken);
        }

        chunkedOutputStream.Flush();
        var chunks = chunkedOutputStream.Chunks.ToList();
        long totalCompressedBytes = chunkedOutputStream.TotalBytesWritten;

        _logger.LogInfo($"Compression complete. Generated {chunks.Count} chunks ({SizeFormatter.FormatBytes(totalCompressedBytes)}).");

        // Handle artwork if provided or present in source
        string? artworkRef = config.Game.Artwork ?? config.Installer.Artwork ?? config.Branding.Artwork;
        if (!string.IsNullOrWhiteSpace(artworkRef))
        {
            if (artworkRef.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                artworkRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // URL reference
            }
            else if (File.Exists(artworkRef))
            {
                // Local absolute or relative file path: copy into repack output folder
                try
                {
                    var ext = Path.GetExtension(artworkRef);
                    var destArtwork = Path.Combine(outputDirectory, $"artwork{ext}");
                    File.Copy(artworkRef, destArtwork, overwrite: true);
                    artworkRef = Path.GetFileName(destArtwork);
                }
                catch { }
            }
        }
        else
        {
            // Auto-detect cover/artwork in source directory if present
            string[] possibleArtworks = { "artwork.jpg", "artwork.png", "cover.jpg", "cover.png", "banner.jpg", "banner.png" };
            foreach (var art in possibleArtworks)
            {
                var candidate = Path.Combine(config.Source.Directory, art);
                if (File.Exists(candidate))
                {
                    try
                    {
                        var destArtwork = Path.Combine(outputDirectory, art);
                        File.Copy(candidate, destArtwork, overwrite: true);
                        artworkRef = art;
                        break;
                    }
                    catch { }
                }
            }
        }

        // 4. Create and Save Manifest (.gpack)
        var manifest = new PackageManifest
        {
            FormatVersion = 1,
            Package = new PackageMetadata
            {
                Generator = "gpack",
                GeneratorVersion = "0.1.0",
                Brand = config.Installer.Brand,
                CreatedUtc = DateTime.UtcNow
            },
            Game = new GameMetadata
            {
                Name = config.Game.Name,
                Version = config.Game.Version,
                Edition = config.Game.Edition,
                Artwork = artworkRef,
                Publisher = config.Game.Publisher,
                Developer = config.Game.Developer
            },
            Launch = new LaunchConfiguration
            {
                Executable = config.Launch.Executable,
                Arguments = config.Launch.Arguments,
                WorkingDirectory = config.Launch.WorkingDirectory
            },
            Payload = new PayloadMetadata
            {
                Compression = "zstd",
                CompressionLevel = compressionLevel,
                UncompressedSize = totalUncompressedBytes,
                TotalFiles = fileEntries.Count,
                Chunks = chunks
            },
            Files = fileEntries,
            Actions = config.Actions.Count > 0 ? config.Actions : null
        };

        string safeGameName = string.Join("_", config.Game.Name.Split(Path.GetInvalidFileNameChars()));
        string manifestFileName = $"{safeGameName}.gpack";
        string manifestFilePath = Path.Combine(outputDirectory, manifestFileName);

        await ManifestSerializer.SaveToFileAsync(manifest, manifestFilePath, cancellationToken);
        _logger.LogInfo($"Manifest written to: {manifestFilePath}");

        stopwatch.Stop();
        return new PackageBuildResult
        {
            Success = true,
            OutputDirectory = outputDirectory,
            ManifestFilePath = manifestFilePath,
            Manifest = manifest,
            Elapsed = stopwatch.Elapsed,
            TotalUncompressedBytes = totalUncompressedBytes,
            TotalCompressedBytes = totalCompressedBytes
        };
    }
}
