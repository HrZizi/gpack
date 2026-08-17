using System.Text.Json.Serialization;

namespace GPack.Core.Models.Events;

public class JsonProgressEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "progress";

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("processedBytes")]
    public long ProcessedBytes { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonPropertyName("speedBytesPerSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double SpeedBytesPerSec { get; set; }
}

public class JsonCompleteEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "complete";

    [JsonPropertyName("outputDirectory")]
    public string? OutputDirectory { get; set; }

    [JsonPropertyName("package")]
    public string? Package { get; set; }

    [JsonPropertyName("installedDirectory")]
    public string? InstalledDirectory { get; set; }
}

public class JsonErrorEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "error";

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
