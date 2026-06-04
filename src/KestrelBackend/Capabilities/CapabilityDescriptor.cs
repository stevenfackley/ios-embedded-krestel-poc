using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed record CapabilityDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("expected")] Verdict Expected,
    [property: JsonPropertyName("mechanism")] string Mechanism);
