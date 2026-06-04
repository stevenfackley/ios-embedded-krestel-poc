using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KestrelBackend;

internal sealed class RuntimeModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("runtime.info", "Runtime", "Runtime info",   "RuntimeInformation, GC, process arch",    Verdict.Works, "RuntimeInformation; GC.GetGCMemoryInfo; AOT-safe"),
        new("runtime.time", "Runtime", "Time + Stopwatch", "DateTimeOffset.UtcNow + Stopwatch",    Verdict.Works, "DateTimeOffset; Stopwatch; no reflection"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "runtime.info" => RunInfo(),
            "runtime.time" => RunTime(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) { }

    private static CapabilityResult RunInfo()
    {
        var gc = GC.GetGCMemoryInfo();
        string detail =
            $"Framework={RuntimeInformation.FrameworkDescription}; " +
            $"OS={RuntimeInformation.OSDescription[..Math.Min(40, RuntimeInformation.OSDescription.Length)]}; " +
            $"Arch={RuntimeInformation.ProcessArchitecture}; " +
            $"GC HeapSizeBytes={gc.HeapSizeBytes:#,##0}";
        return Works("runtime.info", "Runtime", "Runtime info", detail);
    }

    private static CapabilityResult RunTime()
    {
        var sw = Stopwatch.StartNew();
        long ticks = DateTimeOffset.UtcNow.Ticks;
        sw.Stop();
        string detail = $"DateTimeOffset.UtcNow.Ticks={ticks}; " +
                        $"Stopwatch.ElapsedNanoseconds={sw.Elapsed.TotalNanoseconds:F0}ns; " +
                        $"Frequency={Stopwatch.Frequency}";
        return Works("runtime.time", "Runtime", "Time + Stopwatch", detail);
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}
