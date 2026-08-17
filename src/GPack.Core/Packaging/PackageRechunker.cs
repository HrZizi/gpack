using System.Diagnostics;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;

namespace GPack.Core.Packaging;

public static class PackageRechunker
{
    public const long IsoSafeMaxChunkSize = 2L * 1024 * 1024 * 1024; // 2 GB (safe for ISO 9660 & FAT32)
    public const long IsoMaxFileSizeLimit = 0xFFFFFFFFL; // 4,294,967,295 bytes (ISO 9660 single file limit)

    public static async Task<PackageManifest> RechunkIfNeededAsync(
        string packageDirectory,
        long maxChunkSizeBytes = IsoSafeMaxChunkSize,
        ILogger? logger = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var log = logger ?? new ConsoleLogger();

        var gpackFiles = Directory.GetFiles(packageDirectory, "*.gpack");
        if (gpackFiles.Length == 0)
        {
            throw new FileNotFoundException($"No .gpack manifest found in '{packageDirectory}'.");
        }

        string manifestPath = gpackFiles[0];
        var manifest = await ManifestSerializer.LoadFromFileAsync(manifestPath, cancellationToken);

        bool needsRechunking = manifest.Payload.Chunks.Any(c => c.Size > maxChunkSizeBytes);
        if (!needsRechunking)
        {
            return manifest;
        }

        log.LogInfo($"Re-chunking payload chunks to max {SizeFormatter.FormatBytes(maxChunkSizeBytes)} per chunk...");
        var stopwatch = Stopwatch.StartNew();

        string dataDir = Path.Combine(packageDirectory, "data");
        string tempRechunkDir = Path.Combine(packageDirectory, $"data_rechunk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRechunkDir);

        long totalPayloadBytes = manifest.Payload.Chunks.Sum(c => c.Size);
        long processedBytes = 0;

        try
        {
            using (var chunkedIn = new ChunkedInputStream(packageDirectory, manifest.Payload.Chunks, verifyHashesOnRead: false))
            using (var chunkedOut = new ChunkedOutputStream(tempRechunkDir, maxChunkSizeBytes))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await chunkedIn.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await chunkedOut.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    processedBytes += bytesRead;

                    double ratio = totalPayloadBytes > 0 ? (double)processedBytes / totalPayloadBytes : 1.0;
                    progress?.Report(new InstallProgress(
                        Stage: InstallStage.Finalizing,
                        ProcessedBytes: processedBytes,
                        TotalBytes: totalPayloadBytes,
                        CurrentFile: "Re-chunking payload for ISO compatibility...",
                        ProgressRatio: ratio
                    ));
                }

                chunkedOut.Flush();
                var newChunks = chunkedOut.Complete();

                // Delete old chunk files in data directory
                foreach (var oldChunk in manifest.Payload.Chunks)
                {
                    var oldChunkFile = Path.Combine(packageDirectory, oldChunk.File.Replace('/', '\\'));
                    if (File.Exists(oldChunkFile))
                    {
                        try { File.Delete(oldChunkFile); } catch { }
                    }
                }

                // Move new chunks from temp folder to data directory
                Directory.CreateDirectory(dataDir);
                foreach (var newChunkFile in Directory.GetFiles(tempRechunkDir, "*.gpk"))
                {
                    string fileName = Path.GetFileName(newChunkFile);
                    string destPath = Path.Combine(dataDir, fileName);
                    File.Move(newChunkFile, destPath, overwrite: true);
                }

                // Update manifest
                manifest.Payload.Chunks = newChunks.ToList();
                await ManifestSerializer.SaveToFileAsync(manifest, manifestPath, cancellationToken);

                stopwatch.Stop();
                log.LogInfo($"Re-chunking complete: generated {newChunks.Count} chunks in {stopwatch.Elapsed.TotalSeconds:F1}s.");

                return manifest;
            }
        }
        finally
        {
            if (Directory.Exists(tempRechunkDir))
            {
                try { Directory.Delete(tempRechunkDir, recursive: true); } catch { }
            }
        }
    }
}
