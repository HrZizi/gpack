using System.Diagnostics;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;

namespace GPack.Builder.Packaging;

public class RarPackager
{
    private readonly ILogger _logger;

    public RarPackager(ILogger? logger = null)
    {
        _logger = logger ?? new ConsoleLogger();
    }

    public static bool IsRarAvailable() => FindRarExecutable() != null;

    public static string? FindRarExecutable()
    {
        // 1. GPACK_RAR_EXE environment variable
        var envPath = Environment.GetEnvironmentVariable("GPACK_RAR_EXE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // 2. Standard WinRAR installation paths
        string[] standardPaths =
        {
            @"C:\Program Files\WinRAR\rar.exe",
            @"C:\Program Files\WinRAR\WinRAR.exe",
            @"C:\Program Files (x86)\WinRAR\rar.exe",
            @"C:\Program Files (x86)\WinRAR\WinRAR.exe"
        };

        foreach (var path in standardPaths)
        {
            if (File.Exists(path)) return path;
        }

        // 3. Application directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var localRar = Path.Combine(baseDir, "rar.exe");
        if (File.Exists(localRar)) return localRar;

        // 4. PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(segment.Trim(), "rar.exe");
                if (File.Exists(candidate)) return candidate;

                var candidateWinRar = Path.Combine(segment.Trim(), "WinRAR.exe");
                if (File.Exists(candidateWinRar)) return candidateWinRar;
            }
        }

        return null;
    }

    public async Task<List<string>> CreateRarAsync(
        string sourceDirectory,
        string targetRarPath,
        long? partSizeBytes = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rarExe = FindRarExecutable();
        if (string.IsNullOrEmpty(rarExe))
        {
            throw new FileNotFoundException(
                "RAR archive creation requires WinRAR (rar.exe). WinRAR was not found on your system. " +
                "Please install WinRAR (https://www.win-rar.com) or set the GPACK_RAR_EXE environment variable to your rar.exe path.");
        }

        var stopwatch = Stopwatch.StartNew();
        var targetDir = Path.GetDirectoryName(Path.GetFullPath(targetRarPath)) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(targetDir);

        string isParted = partSizeBytes.HasValue ? $" (parted: {SizeFormatter.FormatBytes(partSizeBytes.Value)})" : "";
        _logger.LogInfo($"Creating RAR archive{isParted} using {rarExe}...");

        var fullTargetRarPath = Path.GetFullPath(targetRarPath);
        if (File.Exists(fullTargetRarPath))
        {
            File.Delete(fullTargetRarPath);
        }

        // Arguments: a (add), -m0 (store - chunks are already Zstd compressed), -ep1 (exclude base dir from names), -r (recursive)
        var argsList = new List<string> { "a", "-m0", "-ep1", "-r" };

        if (partSizeBytes.HasValue && partSizeBytes.Value > 0)
        {
            argsList.Add($"-v{partSizeBytes.Value}b");
        }

        argsList.Add($"\"{fullTargetRarPath}\"");
        argsList.Add("\"*\"");

        string arguments = string.Join(" ", argsList);
        _logger.LogDebug($"Executing: {rarExe} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = rarExe,
            Arguments = arguments,
            WorkingDirectory = Path.GetFullPath(sourceDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var outputLines = new List<string>();

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputLines.Add(e.Data);
                _logger.LogDebug($"[rar] {e.Data}");
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.LogWarning($"[rar err] {e.Data}");
            }
        };

        progress?.Report(new InstallProgress(
            Stage: InstallStage.Finalizing,
            ProcessedBytes: 0,
            TotalBytes: 0,
            CurrentFile: "Packaging RAR archive...",
            ProgressRatio: 0.5
        ));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string err = string.Join(Environment.NewLine, outputLines.TakeLast(5));
            throw new InvalidOperationException($"WinRAR exited with error code {process.ExitCode}: {err}");
        }

        stopwatch.Stop();

        // Discover generated files
        var generatedFiles = new List<string>();
        string baseNameWithoutExt = Path.GetFileNameWithoutExtension(targetRarPath);

        if (partSizeBytes.HasValue)
        {
            var partFiles = Directory.GetFiles(targetDir, $"{baseNameWithoutExt}.part*.rar")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (partFiles.Count > 0)
            {
                generatedFiles.AddRange(partFiles);
            }
            else if (File.Exists(fullTargetRarPath))
            {
                // If the total size didn't exceed a single part, WinRAR might output standard .rar
                generatedFiles.Add(fullTargetRarPath);
            }
        }
        else
        {
            if (File.Exists(fullTargetRarPath))
            {
                generatedFiles.Add(fullTargetRarPath);
            }
        }

        long totalGeneratedBytes = generatedFiles.Sum(f => new FileInfo(f).Length);
        _logger.LogInfo($"RAR packaging completed: {generatedFiles.Count} file(s) ({SizeFormatter.FormatBytes(totalGeneratedBytes)}) in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        progress?.Report(new InstallProgress(
            Stage: InstallStage.Finished,
            ProcessedBytes: totalGeneratedBytes,
            TotalBytes: totalGeneratedBytes,
            CurrentFile: null,
            ProgressRatio: 1.0
        ));

        return generatedFiles;
    }
}
