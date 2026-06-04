using LegacyLib;

namespace KestrelBackend;

/// <summary>
/// Demonstrates the core thesis: netstandard2.0 code reused unchanged inside a NativeAOT
/// iOS shared library. The DataProcessor was written before NativeAOT existed; it works
/// without modification because it uses only BCL primitives available since .NET Standard 2.0.
/// </summary>
internal sealed class LegacyModule : ICapabilityModule
{
    private readonly DataProcessor _processor = new();

    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("legacy.process", "Legacy", "netstandard2.0 reuse",
            "LegacyLib.DataProcessor — existing business logic, zero modification required",
            Verdict.Works,
            "netstandard2.0 ref assembly; SHA-256 via System.Security.Cryptography; AOT-safe")
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct)
    {
        if (id != "legacy.process")
            return Task.FromResult(Unknown(id));

        var result = _processor.Process("capability-explorer-probe");
        return Task.FromResult(new CapabilityResult
        {
            Id = id, Category = "Legacy", Title = "netstandard2.0 reuse",
            Verdict = Verdict.Works,
            Detail = $"DataProcessor.Process() → Hash={result.Hash[..16]}…; Length={result.Length}; " +
                     $"ProcessedAt={result.ProcessedAtUtc}. " +
                     "Compiled for netstandard2.0, referenced unmodified from net9.0 NativeAOT lib.",
            CorrelationId = CorrelationContext.Current
        });
    }

    public void MapRoutes(Router router) { }

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}
