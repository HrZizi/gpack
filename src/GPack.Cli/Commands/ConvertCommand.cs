using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DiscUtils.Iso9660;
using GPack.Builder.Packaging;
using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Core.Models.Events;
using GPack.Core.Packaging;
using GPack.Installer.Core;

namespace GPack.Cli.Commands;

public static class ConvertCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack convert <source-package | dir | .zip | .iso> --format <target-format> [options]");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  --format, -f <fmt>       Target format: directory, iso, zip, rar, parted-rar");
            Console.WriteLine("  --output, -o <path>      Output directory for converted package (default: same parent directory)");
            Console.WriteLine("  --part-size <size>       Part size for parted-rar (e.g. 2GB, 4GB, 500MB)");
            Console.WriteLine("  --json                   Output machine-readable line-delimited JSON events");
            Console.WriteLine("  --quiet, -q              Suppress progress output");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("  gpack convert \"D:\\Repacks\\Game-NagelRepacks.zip\" --format iso");
            Console.WriteLine("  gpack convert \"D:\\Repacks\\Game-NagelRepacks\" --format zip");
            Console.WriteLine("  gpack convert \"D:\\Repacks\\Game-NagelRepacks.iso\" --format directory");
            Console.WriteLine("  gpack convert \"D:\\Repacks\\Game-NagelRepacks\" --format parted-rar --part-size 2GB");
            return ExitCodes.InvalidArguments;
        }

        string sourcePath = Path.GetFullPath(args[0]);
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            logger.LogError($"Source path not found: {sourcePath}");
            return ExitCodes.SourceNotFound;
        }

        string? targetFormatString = null;
        string? outputDir = null;
        string? partSizeString = null;
        bool jsonMode = false;
        bool quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if ((arg.Equals("--format", StringComparison.OrdinalIgnoreCase) || arg.Equals("-f", StringComparison.OrdinalIgnoreCase) || arg.Equals("--output-format", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                targetFormatString = args[++i];
            }
            else if ((arg.Equals("--output", StringComparison.OrdinalIgnoreCase) || arg.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (arg.Equals("--part-size", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                partSizeString = args[++i];
            }
            else if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                jsonMode = true;
            }
            else if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || arg.Equals("-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
            }
        }

        if (string.IsNullOrWhiteSpace(targetFormatString))
        {
            logger.LogError("Target format must be specified (--format <directory|iso|zip|rar|parted-rar>).");
            return ExitCodes.InvalidArguments;
        }

        PackageOutputFormat targetFormat;
        try
        {
            targetFormat = PackageOutputFormatExtensions.ParseFormat(targetFormatString);
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return ExitCodes.InvalidArguments;
        }

        // 1. Verify manifest can be loaded from source
        PackageManifest manifest;
        try
        {
            manifest = await PackageReader.LoadManifestAsync(sourcePath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to load manifest from source: {ex.Message}");
            return ExitCodes.InvalidConfiguration;
        }

        string baseOutputDir = !string.IsNullOrWhiteSpace(outputDir)
            ? Path.GetFullPath(outputDir)
            : (File.Exists(sourcePath) ? Path.GetDirectoryName(sourcePath)! : Path.GetDirectoryName(sourcePath.TrimEnd('\\', '/'))!);

        Directory.CreateDirectory(baseOutputDir);

        string safeGameName = string.Join("-", manifest.Game.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string brand = !string.IsNullOrWhiteSpace(manifest.Package.Brand) ? manifest.Package.Brand : "NagelRepacks";
        string outputFolderName = $"{safeGameName}-{brand}";

        logger.LogInfo($"Converting '{manifest.Game.Name}' to {targetFormat.ToFriendlyString()}...");

        string? stagingDir = null;
        string workingPackageDir;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 2. Prepare uncompressed package directory
            if (Directory.Exists(sourcePath))
            {
                workingPackageDir = sourcePath;
            }
            else
            {
                string ext = Path.GetExtension(sourcePath);
                stagingDir = Path.Combine(baseOutputDir, $".gpack-convert-{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingDir);

                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInfo($"Extracting source ZIP archive to temporary workspace...");
                    ZipFile.ExtractToDirectory(sourcePath, stagingDir, overwriteFiles: true);
                }
                else if (ext.Equals(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInfo($"Extracting source ISO disc image to temporary workspace...");
                    ExtractIsoToDirectory(sourcePath, stagingDir, cancellationToken);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported source file extension '{ext}'. Supported source types: directory, .zip, .iso");
                }

                workingPackageDir = stagingDir;
            }

            // 3. Perform conversion to target format
            string? primaryArtifactPath = null;
            var generatedFiles = new List<string>();

            using var progress = new ConsoleProgressReporter(quiet: quiet, jsonMode: jsonMode);

            switch (targetFormat)
            {
                case PackageOutputFormat.Directory:
                    string destFolder = Path.Combine(baseOutputDir, outputFolderName);
                    if (workingPackageDir.Equals(destFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        primaryArtifactPath = destFolder;
                    }
                    else
                    {
                        Directory.CreateDirectory(destFolder);
                        CopyDirectoryRecursive(workingPackageDir, destFolder);
                        primaryArtifactPath = destFolder;
                    }
                    generatedFiles.Add(destFolder);
                    break;

                case PackageOutputFormat.Iso:
                    string targetIsoPath = Path.Combine(baseOutputDir, $"{outputFolderName}.iso");
                    var isoPackager = new IsoPackager(logger);
                    await isoPackager.CreateIsoAsync(workingPackageDir, targetIsoPath, safeGameName, progress, cancellationToken);
                    primaryArtifactPath = targetIsoPath;
                    generatedFiles.Add(targetIsoPath);
                    break;

                case PackageOutputFormat.Zip:
                    string targetZipPath = Path.Combine(baseOutputDir, $"{outputFolderName}.zip");
                    var zipPackager = new ZipPackager(logger);
                    await zipPackager.CreateZipAsync(workingPackageDir, targetZipPath, progress, cancellationToken);
                    primaryArtifactPath = targetZipPath;
                    generatedFiles.Add(targetZipPath);
                    break;

                case PackageOutputFormat.Rar:
                    string targetRarPath = Path.Combine(baseOutputDir, $"{outputFolderName}.rar");
                    var rarPackager = new RarPackager(logger);
                    var rarFiles = await rarPackager.CreateRarAsync(workingPackageDir, targetRarPath, partSizeBytes: null, progress, cancellationToken);
                    primaryArtifactPath = targetRarPath;
                    generatedFiles.AddRange(rarFiles);
                    break;

                case PackageOutputFormat.PartedRar:
                    long partSizeBytes = !string.IsNullOrWhiteSpace(partSizeString)
                        ? SizeFormatter.ParseSize(partSizeString)
                        : (manifest.Payload.Chunks.Count > 0 ? manifest.Payload.Chunks[0].Size : 4L * 1024 * 1024 * 1024);

                    string targetPartedRarPath = Path.Combine(baseOutputDir, $"{outputFolderName}.rar");
                    var partedRarPackager = new RarPackager(logger);
                    var partedFiles = await partedRarPackager.CreateRarAsync(workingPackageDir, targetPartedRarPath, partSizeBytes: partSizeBytes, progress, cancellationToken);
                    primaryArtifactPath = partedFiles.FirstOrDefault() ?? targetPartedRarPath;
                    generatedFiles.AddRange(partedFiles);
                    break;
            }

            stopwatch.Stop();
            if (!quiet && !jsonMode) Console.WriteLine();

            if (jsonMode)
            {
                var completeEvt = new JsonCompleteEvent
                {
                    Format = targetFormat.ToString().ToLowerInvariant(),
                    OutputDirectory = baseOutputDir,
                    OutputArtifact = primaryArtifactPath,
                    Package = primaryArtifactPath
                };
                Console.WriteLine(JsonSerializer.Serialize(completeEvt));
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("==================================================");
                Console.WriteLine("CONVERSION COMPLETE!");
                Console.WriteLine($"Game:          {manifest.Game.Name} (v{manifest.Game.Version})");
                Console.WriteLine($"Format:        {targetFormat.ToFriendlyString()}");
                Console.WriteLine($"Target:        {primaryArtifactPath}");
                if (generatedFiles.Count > 1)
                {
                    Console.WriteLine($"Total Parts:   {generatedFiles.Count}");
                }
                Console.WriteLine($"Elapsed:       {stopwatch.Elapsed.TotalSeconds:F1}s");
                Console.WriteLine("==================================================");
                Console.WriteLine();
            }

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            if (!quiet && !jsonMode) Console.WriteLine();
            logger.LogError($"Conversion failed: {ex.Message}", ex);

            if (jsonMode)
            {
                var errEvt = new JsonErrorEvent
                {
                    Code = ExitCodes.PackageCreationFailed.ToString(),
                    Message = ex.Message
                };
                Console.WriteLine(JsonSerializer.Serialize(errEvt));
            }

            return ExitCodes.PackageCreationFailed;
        }
        finally
        {
            if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { }
            }
        }
    }

    private static void ExtractIsoToDirectory(string isoPath, string targetDirectory, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(isoPath);
        using var cdReader = new CDReader(stream, joliet: true);

        ExtractDirectoryRecursive(cdReader, string.Empty, targetDirectory, cancellationToken);
    }

    private static void ExtractDirectoryRecursive(CDReader reader, string pathInIso, string targetDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in reader.GetFiles(pathInIso))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(file);
            string targetFilePath = Path.Combine(targetDirectory, fileName);

            using var srcStream = reader.OpenFile(file, FileMode.Open, FileAccess.Read);
            using var destStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            srcStream.CopyTo(destStream);

            try
            {
                var lastWrite = reader.GetLastWriteTimeUtc(file);
                if (lastWrite > DateTime.MinValue) File.SetLastWriteTimeUtc(targetFilePath, lastWrite);
            }
            catch { }
        }

        foreach (var dir in reader.GetDirectories(pathInIso))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dirName = Path.GetFileName(dir);
            string targetSubDir = Path.Combine(targetDirectory, dirName);
            ExtractDirectoryRecursive(reader, dir, targetSubDir, cancellationToken);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(targetDir, rel);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
