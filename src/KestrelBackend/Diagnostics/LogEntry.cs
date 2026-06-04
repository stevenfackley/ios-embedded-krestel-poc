using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class LogEntry
{
    [JsonPropertyName("seq")]
    public long Seq { get; init; }

    [JsonPropertyName("timestampUtc")]
    public string TimestampUtc { get; init; } = "";

    [JsonPropertyName("level")]
    public string Level { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("eventId")]
    public int EventId { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}
