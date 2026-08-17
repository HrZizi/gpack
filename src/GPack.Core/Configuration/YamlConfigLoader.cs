using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GPack.Core.Configuration;

public static class YamlConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static BuildConfiguration LoadFromFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return LoadFromString(yaml);
    }

    public static BuildConfiguration LoadFromString(string yaml)
    {
        var config = Deserializer.Deserialize<BuildConfiguration>(yaml) ?? new BuildConfiguration();
        if (!string.IsNullOrWhiteSpace(config.Package.SplitSize))
        {
            config.Package.SplitSizeBytes = Files.SizeFormatter.ParseSize(config.Package.SplitSize);
        }
        return config;
    }

    public static string Serialize(BuildConfiguration config)
    {
        return Serializer.Serialize(config);
    }
}
