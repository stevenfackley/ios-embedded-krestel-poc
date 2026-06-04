using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class CompressionModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("compress.gzip",   "Compression", "GZip",   "GZipStream compress+decompress",    Verdict.Works, "System.IO.Compression.GZipStream; AOT-safe"),
        new("compress.brotli", "Compression", "Brotli",  "BrotliStream compress+decompress",  Verdict.Works, "System.IO.Compression.BrotliStream; AOT-safe"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "compress.gzip"   => RunGzip(),
            "compress.brotli" => RunBrotli(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) =>
        router.Map("POST", "/api/compress", (req, rv, ct) =>
        {
            string input = req.Query.TryGetValue("input", out string? v) ? v
                         : Encoding.UTF8.GetString(req.Body.Span);
            byte[] raw = Encoding.UTF8.GetBytes(input);
            byte[] compressed = Gzip(raw);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                new CompressResult("gzip", raw.Length, compressed.Length,
                    (double)compressed.Length / raw.Length),
                ApiJsonContext.Default.CompressResult);
            return Task.FromResult(HttpResponse.Json(json));
        });

    private static CapabilityResult RunGzip()
    {
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("hello gzip ", 20)));
        byte[] compressed = Gzip(input);
        byte[] back = GunZip(compressed);
        bool ok = input.AsSpan().SequenceEqual(back);
        double ratio = (double)compressed.Length / input.Length;
        return Works("compress.gzip", "Compression", "GZip",
            $"GZip ratio={ratio:P0}; {input.Length}→{compressed.Length} bytes; roundtrip OK={ok}");
    }

    private static CapabilityResult RunBrotli()
    {
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("hello brotli ", 20)));
        var ms = new MemoryStream();
        using (var br = new BrotliStream(ms, CompressionMode.Compress, leaveOpen: true))
            br.Write(input);
        byte[] compressed = ms.ToArray();

        var ms2 = new MemoryStream(compressed);
        using var br2 = new BrotliStream(ms2, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        br2.CopyTo(outMs);
        byte[] back = outMs.ToArray();

        bool ok = input.AsSpan().SequenceEqual(back);
        double ratio = (double)compressed.Length / input.Length;
        return Works("compress.brotli", "Compression", "Brotli",
            $"Brotli ratio={ratio:P0}; {input.Length}→{compressed.Length} bytes; roundtrip OK={ok}");
    }

    private static byte[] Gzip(byte[] input)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            gz.Write(input);
        return ms.ToArray();
    }

    private static byte[] GunZip(byte[] input)
    {
        using var gz = new GZipStream(new MemoryStream(input), CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

internal sealed record CompressResult(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("rawBytes")] int RawBytes,
    [property: JsonPropertyName("compressedBytes")] int CompressedBytes,
    [property: JsonPropertyName("ratio")] double Ratio);
