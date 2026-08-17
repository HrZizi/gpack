using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Packaging;
using GPack.Installer.Core;

namespace GPack.Cli.Commands;

public static class VerifyCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack verify <package.gpack | directory>");
            return ExitCodes.InvalidArguments;
        }

        string target = Path.GetFullPath(args[0]);
        PackageManifest manifest;

        try
        {
            manifest = await PackageReader.LoadManifestAsync(target, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to load manifest: {ex.Message}");
            return ExitCodes.InvalidConfiguration;
        }

        string packageDir = File.Exists(target) ? Path.GetDirectoryName(target)! : target;

        logger.LogInfo($"Verifying package '{manifest.Game.Name}' ({manifest.Payload.Chunks.Count} chunks)...");

        bool quiet = args.Contains("--quiet") || args.Contains("-q");

        try
        {
            using var progressBar = !quiet ? new ConsoleProgressBar() : null;
            using var chunkedStream = new ChunkedInputStream(packageDir, manifest.Payload.Chunks, verifyHashesOnRead: true);
            byte[] buffer = new byte[81920];
            long totalRead = 0;
            long totalSize = manifest.Payload.Chunks.Sum(c => c.Size);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                int read = await chunkedStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0) break;
                totalRead += read;

                double ratio = totalSize > 0 ? (double)totalRead / totalSize : 1.0;
                double speed = sw.Elapsed.TotalSeconds > 0 ? totalRead / sw.Elapsed.TotalSeconds : 0;
                TimeSpan? remaining = speed > 0 && totalSize > totalRead
                    ? TimeSpan.FromSeconds((totalSize - totalRead) / speed)
                    : null;

                progressBar?.Report(new InstallProgress(
                    Stage: InstallStage.Verifying,
                    ProcessedBytes: totalRead,
                    TotalBytes: totalSize,
                    CurrentFile: null,
                    ProgressRatio: ratio,
                    SpeedBytesPerSecond: speed,
                    EstimatedRemaining: remaining
                ));
            }

            progressBar?.Complete();
            if (!quiet) Console.WriteLine();
            logger.LogInfo("All payload chunk SHA-256 hashes successfully verified!");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            if (!quiet) Console.WriteLine();
            logger.LogError($"Verification failed: {ex.Message}");
            return ExitCodes.VerificationFailed;
        }
    }
}
