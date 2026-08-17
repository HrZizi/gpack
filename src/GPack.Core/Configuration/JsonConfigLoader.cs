using System.Text.Json;

namespace GPack.Core.Configuration;

public static class JsonConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static BuildConfiguration LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return LoadFromString(json);
    }

    public static BuildConfiguration LoadFromString(string json)
    {
        var config = JsonSerializer.Deserialize<BuildConfiguration>(json, JsonOptions) ?? new BuildConfiguration();
        if (!string.IsNullOrWhiteSpace(config.Package.SplitSize))
        {
            config.Package.SplitSizeBytes = Files.SizeFormatter.ParseSize(config.Package.SplitSize);
        }
        return config;
    }

    public static string Serialize(BuildConfiguration config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }
}
