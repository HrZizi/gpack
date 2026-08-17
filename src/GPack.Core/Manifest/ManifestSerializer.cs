using System.Text.Json;

namespace GPack.Core.Manifest;

public static class ManifestSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(PackageManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static async Task SaveToFileAsync(PackageManifest manifest, string filePath, CancellationToken cancellationToken = default)
    {
        var json = Serialize(manifest);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public static PackageManifest Deserialize(string json)
    {
        return JsonSerializer.Deserialize<PackageManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize package manifest.");
    }

    public static async Task<PackageManifest> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Package manifest file not found: {filePath}", filePath);
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return Deserialize(json);
    }
}
