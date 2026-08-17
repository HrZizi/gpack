using GPack.Abstractions;
using ZstdSharp;

namespace GPack.Core.Compression;

public static class CompressionPreset
{
    public const string None = "none";
    public const string Fast = "fast";
    public const string Balanced = "balanced";
    public const string Maximum = "maximum";

    public static int ResolveLevel(string presetName, int? customLevel = null)
    {
        if (customLevel.HasValue) return customLevel.Value;

        return presetName.ToLowerInvariant() switch
        {
            None => 0,
            Fast => 3,
            Balanced => 10,
            Maximum => 18,
            _ => 10
        };
    }
}

public class ZstdCompressionProvider : ICompressionProvider
{
    public string AlgorithmName => "zstd";

    public Stream CreateCompressionStream(Stream outputStream, int level = 0, bool leaveOpen = false)
    {
        if (level <= 0)
        {
            // Level 0 in our mapping means pass-through or level 1 in zstd
            // But if preset was explicitly 'none', we could use level 1 or pass-through
            return new CompressionStream(outputStream, 1, bufferSize: 0, leaveOpen: leaveOpen);
        }
        return new CompressionStream(outputStream, level, bufferSize: 0, leaveOpen: leaveOpen);
    }

    public Stream CreateDecompressionStream(Stream inputStream, bool leaveOpen = false)
    {
        return new DecompressionStream(inputStream, bufferSize: 0, leaveOpen: leaveOpen);
    }
}
