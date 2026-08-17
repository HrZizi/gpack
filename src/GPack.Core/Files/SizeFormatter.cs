using System.Globalization;
using System.Text.RegularExpressions;

namespace GPack.Core.Files;

public static class SizeFormatter
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-" + FormatBytes(-bytes);
        if (bytes == 0) return "0 B";

        int mag = (int)Math.Log(bytes, 1024);
        if (mag >= SizeSuffixes.Length) mag = SizeSuffixes.Length - 1;

        decimal adjustedSize = (decimal)bytes / (1L << (mag * 10));
        if (Math.Round(adjustedSize, 1) >= 1000)
        {
            mag += 1;
            adjustedSize /= 1024;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", adjustedSize, SizeSuffixes[mag]);
    }

    public static long ParseSize(string sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr))
        {
            return 4L * 1024 * 1024 * 1024; // Default 4GB
        }

        sizeStr = sizeStr.Trim();
        if (long.TryParse(sizeStr, out var directBytes))
        {
            return directBytes;
        }

        var match = Regex.Match(sizeStr, @"^([0-9]+(?:\.[0-9]+)?)\s*([a-zA-Z]+)$");
        if (!match.Success)
        {
            throw new FormatException($"Invalid size format: '{sizeStr}'. Expected e.g. 500MB, 4GB, 2048KB.");
        }

        double val = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string unit = match.Groups[2].Value.ToUpperInvariant();

        double multiplier = unit switch
        {
            "B" => 1,
            "KB" or "K" => 1024L,
            "MB" or "M" => 1024L * 1024,
            "GB" or "G" => 1024L * 1024 * 1024,
            "TB" or "T" => 1024L * 1024 * 1024 * 1024,
            _ => throw new FormatException($"Unknown unit '{unit}' in size string '{sizeStr}'.")
        };

        return (long)(val * multiplier);
    }
}
