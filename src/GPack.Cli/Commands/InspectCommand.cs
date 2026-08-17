using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Models;

namespace GPack.Cli.Commands;

public static class InspectCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack inspect <game-directory> [--largest]");
            return ExitCodes.InvalidArguments;
        }

        string sourceDir = Path.GetFullPath(args[0]);
        if (!Directory.Exists(sourceDir))
        {
            logger.LogError($"Directory not found: {sourceDir}");
            return ExitCodes.SourceNotFound;
        }

        bool showLargest = args.Contains("--largest") || args.Contains("-l");
        bool quiet = args.Contains("--quiet") || args.Contains("-q");

        InspectionResult inspection;
        if (!quiet)
        {
            using var spinner = ConsoleSpinner.Start($"Analyzing directory: {sourceDir}...");
            inspection = await FileScanner.InspectDirectoryAsync(sourceDir, cancellationToken);
            spinner.Stop(success: true, $"Analyzed {inspection.TotalFiles:N0} files ({SizeFormatter.FormatBytes(inspection.TotalSizeBytes)})");
        }
        else
        {
            inspection = await FileScanner.InspectDirectoryAsync(sourceDir, cancellationToken);
        }

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($"Directory:     {inspection.SourceDirectory}");
        Console.WriteLine($"Total Files:   {inspection.TotalFiles:N0}");
        Console.WriteLine($"Total Size:    {SizeFormatter.FormatBytes(inspection.TotalSizeBytes)}");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        Console.WriteLine("Potential Executables:");
        if (inspection.Executables.Count == 0)
        {
            Console.WriteLine("  (No .exe files found)");
        }
        else
        {
            for (int i = 0; i < inspection.Executables.Count; i++)
            {
                var exe = inspection.Executables[i];
                string marker = exe.IsLikelyMain ? " [MAIN CANDIDATE]" : "";
                Console.WriteLine($"  [{i + 1}] {exe.RelativePath}{marker}");
                if (!string.IsNullOrEmpty(exe.ProductName))
                    Console.WriteLine($"      Product:     {exe.ProductName}");
                if (!string.IsNullOrEmpty(exe.FileVersion))
                    Console.WriteLine($"      Version:     {exe.FileVersion}");
                if (!string.IsNullOrEmpty(exe.FileDescription))
                    Console.WriteLine($"      Description: {exe.FileDescription}");
                Console.WriteLine($"      Size:        {SizeFormatter.FormatBytes(exe.Size)}");
            }
        }

        if (!string.IsNullOrEmpty(inspection.LikelyMainExecutable))
        {
            Console.WriteLine();
            Console.WriteLine($"Likely Launch Executable: {inspection.LikelyMainExecutable}");
        }

        if (inspection.PotentialJunkFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Potential Junk / Log Files ({inspection.PotentialJunkFiles.Count} files, {SizeFormatter.FormatBytes(inspection.PotentialJunkFiles.Sum(f => f.Size))}):");
            foreach (var junk in inspection.PotentialJunkFiles.Take(5))
            {
                Console.WriteLine($"  - {junk.RelativePath} ({SizeFormatter.FormatBytes(junk.Size)})");
            }
            if (inspection.PotentialJunkFiles.Count > 5)
            {
                Console.WriteLine($"  ... and {inspection.PotentialJunkFiles.Count - 5} more");
            }
        }

        if (showLargest && inspection.LargestFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Largest Files:");
            foreach (var file in inspection.LargestFiles)
            {
                Console.WriteLine($"  - {file.RelativePath} ({SizeFormatter.FormatBytes(file.Size)})");
            }
        }

        Console.WriteLine();
        return ExitCodes.Success;
    }
}
