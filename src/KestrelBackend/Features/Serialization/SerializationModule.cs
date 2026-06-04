using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class SerializationModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("json.sourcegen",   "Serialization", "STJ Source Generation", "Zero-reflection JSON via [JsonSerializable]",   Verdict.Works, "ApiJsonContext source-gen; AOT-safe"),
        new("json.polymorphic", "Serialization", "Polymorphic JSON",       "[JsonDerivedType] discriminated union",         Verdict.Works, "[JsonPolymorphic]+[JsonDerivedType]; source-gen"),
        new("text.base64",      "Serialization", "Base64 encoding",        "Convert.ToBase64String / FromBase64String",     Verdict.Works, "BCL; no reflection; AOT-safe"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "json.sourcegen"   => RunSourceGen(),
            "json.polymorphic" => RunPolymorphic(),
            "text.base64"      => RunBase64(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) =>
        router.Map("POST", "/api/serialize", (req, rv, ct) =>
        {
            string input = req.Query.TryGetValue("input", out string? v) ? v
                         : Encoding.UTF8.GetString(req.Body.Span);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                new SerializeResult(input, b64, decoded),
                ApiJsonContext.Default.SerializeResult);
            return Task.FromResult(HttpResponse.Json(json));
        });

    private static CapabilityResult RunSourceGen()
    {
        var pr = new LegacyLib.ProcessResult("hello", "deadbeef", 5, DateTime.UtcNow.ToString("O"));
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(pr, ApiJsonContext.Default.ProcessResult);
        var back = JsonSerializer.Deserialize(json, ApiJsonContext.Default.ProcessResult);
        bool ok = back?.Input == "hello";
        return Works("json.sourcegen", "Serialization", "STJ Source Generation",
            $"Round-trip via ApiJsonContext OK={ok}; {json.Length} bytes");
    }

    private static CapabilityResult RunPolymorphic()
    {
        // Demonstrates [JsonDerivedType] via the Animal hierarchy registered in ApiJsonContext
        Animal dog = new Dog("Rex");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(dog, ApiJsonContext.Default.Animal);
        string text = Encoding.UTF8.GetString(json);
        var back = JsonSerializer.Deserialize(json, ApiJsonContext.Default.Animal);
        return Works("json.polymorphic", "Serialization", "Polymorphic JSON",
            $"Discriminator present={text.Contains("$type")}; roundtrip type={back?.GetType().Name}");
    }

    private static CapabilityResult RunBase64()
    {
        byte[] input = "hello base64 encoding"u8.ToArray();
        string b64 = Convert.ToBase64String(input);
        byte[] back = Convert.FromBase64String(b64);
        bool ok = input.AsSpan().SequenceEqual(back);
        return Works("text.base64", "Serialization", "Base64 encoding",
            $"base64={b64}; roundtrip OK={ok}");
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

// Polymorphic animal hierarchy for the json.polymorphic probe
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Dog), "dog")]
[JsonDerivedType(typeof(Cat), "cat")]
internal abstract class Animal
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

internal sealed class Dog : Animal
{
    public Dog() { }
    public Dog(string name) { Name = name; }
    [JsonPropertyName("breed")] public string Breed { get; init; } = "mixed";
}

internal sealed class Cat : Animal
{
    public Cat() { }
    public Cat(string name) { Name = name; }
    [JsonPropertyName("indoor")] public bool Indoor { get; init; } = true;
}

internal sealed record SerializeResult(
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("base64")] string Base64,
    [property: JsonPropertyName("decoded")] string Decoded);
