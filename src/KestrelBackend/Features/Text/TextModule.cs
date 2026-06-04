using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KestrelBackend;

internal sealed partial class TextModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("text.regex", "Text", "Compiled Regex ([GeneratedRegex])", "AOT-safe source-gen regex via [GeneratedRegex]", Verdict.Works, "[GeneratedRegex] partial method; zero reflection"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "text.regex" => RunRegex(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) =>
        router.Map("POST", "/api/regex", (req, rv, ct) =>
        {
            string input = req.Query.TryGetValue("input", out string? v) ? v
                         : Encoding.UTF8.GetString(req.Body.Span);
            var matches = WordRegex().Matches(input);
            var words = matches.Select(m => m.Value).ToArray();
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                new RegexResult(input, words.Length, words),
                ApiJsonContext.Default.RegexResult);
            return Task.FromResult(HttpResponse.Json(json));
        });

    private static CapabilityResult RunRegex()
    {
        string text = "Hello, world! .NET 9 is fast.";
        var matches = WordRegex().Matches(text);
        string[] words = [.. matches.Select(m => m.Value)];
        return new CapabilityResult
        {
            Id = "text.regex", Category = "Text", Title = "Compiled Regex ([GeneratedRegex])",
            Verdict = Verdict.Works,
            Detail = $"[GeneratedRegex] matched {words.Length} words in \"{text}\": [{string.Join(", ", words.Take(4))}…]",
            CorrelationId = CorrelationContext.Current
        };
    }

    [GeneratedRegex(@"\b\w+\b")]
    private static partial Regex WordRegex();

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

internal sealed record RegexResult(
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("matchCount")] int MatchCount,
    [property: JsonPropertyName("words")] string[] Words);
