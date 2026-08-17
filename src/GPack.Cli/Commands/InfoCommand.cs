using GPack.Core.Files;
using GPack.Core.Logging;
using GPack.Core.Manifest;
using GPack.Core.Models;
using GPack.Installer.Core;

namespace GPack.Cli.Commands;

public static class InfoCommand
{
    public static async Task<int> ExecuteAsync(string[] args, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("-"))
        {
            Console.WriteLine("Usage: gpack info <package.gpack | directory>");
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

        long compressedTotalSize = manifest.Payload.Chunks.Sum(c => c.Size);

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($"Game:             {manifest.Game.Name}");
        Console.WriteLine($"Version:          {manifest.Game.Version}");
        if (!string.IsNullOrEmpty(manifest.Game.Publisher))
            Console.WriteLine($"Publisher:        {manifest.Game.Publisher}");
        if (!string.IsNullOrEmpty(manifest.Game.Developer))
            Console.WriteLine($"Developer:        {manifest.Game.Developer}");
        Console.WriteLine($"Brand:            {manifest.Package.Brand}");
        Console.WriteLine($"Created (UTC):    {manifest.Package.CreatedUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Generator:        {manifest.Package.Generator} v{manifest.Package.GeneratorVersion}");
        Console.WriteLine($"Launch Exe:       {manifest.Launch.Executable} {manifest.Launch.Arguments}");
        Console.WriteLine($"Total Files:      {manifest.Files.Count:N0}");
        Console.WriteLine($"Install Size:     {SizeFormatter.FormatBytes(manifest.Payload.UncompressedSize)}");
        Console.WriteLine($"Package Size:     {SizeFormatter.FormatBytes(compressedTotalSize)}");
        Console.WriteLine($"Chunks Count:     {manifest.Payload.Chunks.Count}");
        Console.WriteLine($"Compression:      {manifest.Payload.Compression} (level {manifest.Payload.CompressionLevel})");

        if (manifest.Actions != null && manifest.Actions.Count > 0)
        {
            Console.WriteLine($"Post Actions:     {manifest.Actions.Count}");
        }
        Console.WriteLine("==================================================");
        Console.WriteLine();

        return ExitCodes.Success;
    }
}
