using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class ProblemDetails
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "about:blank";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = "";

    [JsonPropertyName("instance")]
    public string? Instance { get; init; }

    public static ProblemDetails From(Exception ex, int status, string correlationId, string? instance = null) =>
        new()
        {
            Title = HttpStatus.Phrase(status),
            Status = status,
            Detail = ex.Message,
            CorrelationId = correlationId,
            Instance = instance
        };
}
