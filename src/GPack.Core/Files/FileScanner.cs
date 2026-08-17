using System.Diagnostics;
using GPack.Core.Manifest;
using GPack.Core.Models;

namespace GPack.Core.Files;

public class ScannedFileInfo
{
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public FileAttributes Attributes { get; set; }
}

public class ExecutableInfo
{
    public string RelativePath { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? FileDescription { get; set; }
    public string? ProductVersion { get; set; }
    public string? FileVersion { get; set; }
    public long Size { get; set; }
    public bool IsLikelyMain { get; set; }
}

public class InspectionResult
{
    public string SourceDirectory { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<ExecutableInfo> Executables { get; set; } = new();
    public string? LikelyMainExecutable { get; set; }
    public List<ScannedFileInfo> LargestFiles { get; set; } = new();
    public List<ScannedFileInfo> PotentialJunkFiles { get; set; } = new();
}

public static class FileScanner
{
    private static readonly string[] DefaultJunkGlobs =
    {
        "*.log", "*.tmp", "*.dmp", "*.bak", "CrashDumps/**", "CrashReport*/**", "Screenshots/**", "logs/**"
    };

    public static async Task<List<ScannedFileInfo>> ScanAsync(
        string sourceDirectory,
        IEnumerable<string>? includes = null,
        IEnumerable<string>? excludes = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }

        var includeMatcher = includes != null && includes.Any() ? new GlobMatcher(includes) : null;
        var excludeMatcher = excludes != null && excludes.Any() ? new GlobMatcher(excludes) : null;

        var results = new List<ScannedFileInfo>();
        var rootDir = new DirectoryInfo(sourceDirectory);
        int rootPathLength = rootDir.FullName.TrimEnd('\\', '/').Length + 1;

        long processedBytes = 0;

        await Task.Run(() =>
        {
            var files = rootDir.EnumerateFiles("*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = file.FullName[rootPathLength..];
                relativePath = PathSecurity.NormalizeRelativePath(relativePath);

                if (excludeMatcher != null && excludeMatcher.IsMatch(relativePath))
                {
                    continue;
                }

                if (includeMatcher != null && !includeMatcher.IsMatch(relativePath))
                {
                    continue;
                }

                var scanned = new ScannedFileInfo
                {
                    FullPath = file.FullName,
                    RelativePath = relativePath,
                    Size = file.Length,
                    LastModifiedUtc = file.LastWriteTimeUtc,
                    Attributes = file.Attributes
                };

                results.Add(scanned);
                processedBytes += file.Length;

                progress?.Report(new InstallProgress(
                    Stage: InstallStage.Scanning,
                    ProcessedBytes: processedBytes,
                    TotalBytes: processedBytes,
                    CurrentFile: relativePath,
                    ProgressRatio: 0
                ));
            }
        }, cancellationToken);

        return results;
    }

    public static async Task<InspectionResult> InspectDirectoryAsync(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        var scannedFiles = await ScanAsync(sourceDirectory, cancellationToken: cancellationToken);
        var junkMatcher = new GlobMatcher(DefaultJunkGlobs);

        var result = new InspectionResult
        {
            SourceDirectory = sourceDirectory,
            TotalFiles = scannedFiles.Count,
            TotalSizeBytes = scannedFiles.Sum(f => f.Size),
            LargestFiles = scannedFiles.OrderByDescending(f => f.Size).Take(10).ToList(),
            PotentialJunkFiles = scannedFiles.Where(f => junkMatcher.IsMatch(f.RelativePath)).ToList()
        };

        // Inspect Executables
        var exeFiles = scannedFiles.Where(f => f.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var exe in exeFiles)
        {
            var exeInfo = new ExecutableInfo
            {
                RelativePath = exe.RelativePath,
                Size = exe.Size
            };

            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe.FullPath);
                exeInfo.ProductName = vi.ProductName;
                exeInfo.FileDescription = vi.FileDescription;
                exeInfo.ProductVersion = vi.ProductVersion;
                exeInfo.FileVersion = vi.FileVersion;
            }
            catch { }

            result.Executables.Add(exeInfo);
        }

        // Determine likely main executable
        var dirName = Path.GetFileName(sourceDirectory.TrimEnd('\\', '/'));
        var topLevelExes = result.Executables.Where(e => !e.RelativePath.Contains('\\')).ToList();

        // 1. Exact match with dir name
        var exactMatch = result.Executables.FirstOrDefault(e =>
            Path.GetFileNameWithoutExtension(e.RelativePath).Equals(dirName, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null)
        {
            exactMatch.IsLikelyMain = true;
            result.LikelyMainExecutable = exactMatch.RelativePath;
        }
        else if (topLevelExes.Count == 1)
        {
            topLevelExes[0].IsLikelyMain = true;
            result.LikelyMainExecutable = topLevelExes[0].RelativePath;
        }
        else if (result.Executables.Count > 0)
        {
            // Pick largest non-uninstaller / non-launcher executable
            var candidate = result.Executables
                .Where(e => !e.RelativePath.Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                            !e.RelativePath.Contains("crash", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Size)
                .FirstOrDefault();

            if (candidate != null)
            {
                candidate.IsLikelyMain = true;
                result.LikelyMainExecutable = candidate.RelativePath;
            }
        }

        return result;
    }
}
