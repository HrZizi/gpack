using GPack.Core.Hashing;
using GPack.Core.Manifest;
using GPack.Core.Models;

namespace GPack.Installer.Core;

public class VerificationResult
{
    public bool Success { get; set; }
    public int TotalFilesChecked { get; set; }
    public List<string> MissingFiles { get; set; } = new();
    public List<string> MismatchedFiles { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class FileVerifier
{
    private readonly IHashProvider _hashProvider;

    public FileVerifier(IHashProvider? hashProvider = null)
    {
        _hashProvider = hashProvider ?? new Sha256HashProvider();
    }

    public async Task<VerificationResult> VerifyInstalledFilesAsync(
        string installDirectory,
        IReadOnlyList<FileEntry> expectedFiles,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new VerificationResult
        {
            TotalFilesChecked = expectedFiles.Count
        };

        long totalBytes = expectedFiles.Sum(f => f.Size);
        long processedBytes = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var entry in expectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(installDirectory, entry.Path);
            if (!File.Exists(fullPath))
            {
                result.MissingFiles.Add(entry.Path);
                continue;
            }

            var actualHash = await _hashProvider.ComputeFileHashAsync(fullPath, cancellationToken);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.MismatchedFiles.Add(entry.Path);
            }

            processedBytes += entry.Size;
            double ratio = totalBytes > 0 ? (double)processedBytes / totalBytes : 1.0;
            double speed = sw.Elapsed.TotalSeconds > 0 ? processedBytes / sw.Elapsed.TotalSeconds : 0;
            TimeSpan? remaining = speed > 0 && totalBytes > processedBytes
                ? TimeSpan.FromSeconds((totalBytes - processedBytes) / speed)
                : null;

            progress?.Report(new InstallProgress(
                Stage: InstallStage.Verifying,
                ProcessedBytes: processedBytes,
                TotalBytes: totalBytes,
                CurrentFile: entry.Path,
                ProgressRatio: ratio,
                SpeedBytesPerSecond: speed,
                EstimatedRemaining: remaining
            ));
        }

        result.Success = result.MissingFiles.Count == 0 && result.MismatchedFiles.Count == 0;
        if (!result.Success)
        {
            result.ErrorMessage = $"Verification failed: {result.MissingFiles.Count} missing files, {result.MismatchedFiles.Count} corrupted files.";
        }

        return result;
    }
}
