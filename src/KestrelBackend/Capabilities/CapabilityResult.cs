using System.Text.Json;
using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class CapabilityResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("category")] public string Category { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("verdict")] public Verdict Verdict { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("output")] public JsonElement? Output { get; init; }
    [JsonPropertyName("elapsedMs")] public double ElapsedMs { get; init; }
    [JsonPropertyName("correlationId")] public string? CorrelationId { get; init; }
    [JsonPropertyName("error")] public ProblemDetails? Error { get; init; }
}
