namespace GPack.Core.Models;

public enum PackageOutputFormat
{
    Directory,
    Iso,
    Zip,
    Rar,
    PartedRar
}

public static class PackageOutputFormatExtensions
{
    public static PackageOutputFormat ParseFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackageOutputFormat.Directory;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "dir" or "directory" or "folder" => PackageOutputFormat.Directory,
            "iso" or ".iso" or "image" => PackageOutputFormat.Iso,
            "zip" or ".zip" or "archive" => PackageOutputFormat.Zip,
            "rar" or ".rar" => PackageOutputFormat.Rar,
            "parted-rar" or "parted_rar" or "partedrar" or "rar-parts" or "rar-parted" or "split-rar" or "rar-split" => PackageOutputFormat.PartedRar,
            _ => throw new ArgumentException($"Unsupported output format: '{value}'. Supported formats: directory, iso, zip, rar, parted-rar")
        };
    }

    public static bool TryParseFormat(string? value, out PackageOutputFormat format)
    {
        try
        {
            format = ParseFormat(value);
            return true;
        }
        catch
        {
            format = PackageOutputFormat.Directory;
            return false;
        }
    }

    public static string ToFriendlyString(this PackageOutputFormat format) => format switch
    {
        PackageOutputFormat.Directory => "Directory / Folder",
        PackageOutputFormat.Iso => "ISO Disc Image (.iso)",
        PackageOutputFormat.Zip => "ZIP Archive (.zip)",
        PackageOutputFormat.Rar => "RAR Archive (.rar)",
        PackageOutputFormat.PartedRar => "Parted RAR Multi-Volume Archive (.part1.rar, ...)",
        _ => format.ToString()
    };

    public static string ToExtension(this PackageOutputFormat format) => format switch
    {
        PackageOutputFormat.Directory => string.Empty,
        PackageOutputFormat.Iso => ".iso",
        PackageOutputFormat.Zip => ".zip",
        PackageOutputFormat.Rar => ".rar",
        PackageOutputFormat.PartedRar => ".rar",
        _ => string.Empty
    };
}
