using System.Text.Json.Serialization;

namespace GPack.Core.Manifest;

public class PackageManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("package")]
    public PackageMetadata Package { get; set; } = new();

    [JsonPropertyName("game")]
    public GameMetadata Game { get; set; } = new();

    [JsonPropertyName("launch")]
    public LaunchConfiguration Launch { get; set; } = new();

    [JsonPropertyName("payload")]
    public PayloadMetadata Payload { get; set; } = new();

    [JsonPropertyName("files")]
    public List<FileEntry> Files { get; set; } = new();

    [JsonPropertyName("actions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ActionDefinition>? Actions { get; set; }
}

public class PackageMetadata
{
    [JsonPropertyName("generator")]
    public string Generator { get; set; } = "gpack";

    [JsonPropertyName("generatorVersion")]
    public string GeneratorVersion { get; set; } = "0.1.0";

    [JsonPropertyName("brand")]
    public string Brand { get; set; } = "NagelRepacks";

    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("installerVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallerVersion { get; set; }
}

public class GameMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("edition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Edition { get; set; }

    [JsonPropertyName("artwork")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Artwork { get; set; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    [JsonPropertyName("developer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Developer { get; set; }
}

public class LaunchConfiguration
{
    [JsonPropertyName("executable")]
    public string Executable { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("workingDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }
}

public class PayloadMetadata
{
    [JsonPropertyName("compression")]
    public string Compression { get; set; } = "zstd";

    [JsonPropertyName("compressionLevel")]
    public int CompressionLevel { get; set; } = 3;

    [JsonPropertyName("uncompressedSize")]
    public long UncompressedSize { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("chunks")]
    public List<PackageChunk> Chunks { get; set; } = new();
}

public class PackageChunk
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public class FileEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public FileAttributes Attributes { get; set; } = FileAttributes.Normal;

    [JsonPropertyName("lastModifiedUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime LastModifiedUtc { get; set; }
}

public class ActionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonPropertyName("destination")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Destination { get; set; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    [JsonPropertyName("location")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Location { get; set; }

    [JsonPropertyName("executable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Executable { get; set; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonPropertyName("workingDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("wait")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Wait { get; set; } = true;

    [JsonPropertyName("keyPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyPath { get; set; }

    [JsonPropertyName("valueName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValueName { get; set; }

    [JsonPropertyName("valueData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValueData { get; set; }

    [JsonPropertyName("valueType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValueType { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }
}
