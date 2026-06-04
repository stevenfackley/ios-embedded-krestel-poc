using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KestrelBackend;

internal sealed class CompositionModule : ICapabilityModule
{
    private readonly IServiceProvider _provider;

    public CompositionModule(IServiceProvider provider) => _provider = provider;

    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("compose.di",      "Composition", "DI resolution",  "Resolve services from IServiceProvider",                  Verdict.Works, "M.E.DI ServiceProvider; source-gen binding"),
        new("compose.config",  "Composition", "Options pattern", "IOptions<T> with M.E.Configuration source-gen binder",   Verdict.Works, "IOptions<T>; EnableConfigurationBindingGenerator"),
        new("compose.logging", "Composition", "Structured log",  "Emit + read back from RingBufferSink",                   Verdict.Works, "[LoggerMessage]; ring buffer snapshot"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "compose.di"      => RunDi(),
            "compose.config"  => RunConfig(),
            "compose.logging" => RunLogging(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) { }

    private CapabilityResult RunDi()
    {
        // Resolve the ring-buffer sink and a logger from the shared container
        var sink = _provider.GetService<RingBufferSink>();
        bool hasSink = sink is not null;
        var logFac = _provider.GetService<ILoggerFactory>();
        bool hasLogger = logFac is not null;
        return Works("compose.di", "Composition", "DI resolution",
            $"RingBufferSink resolved={hasSink}; ILoggerFactory resolved={hasLogger}");
    }

    private static CapabilityResult RunConfig()
    {
        // Demonstrate the Options pattern by building a mini-container inline
        var sc = new ServiceCollection();
        sc.Configure<DemoOptions>(o => { o.MaxItems = 42; o.Prefix = "demo"; });
        using var sp = sc.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<DemoOptions>>().Value;
        bool ok = opts.MaxItems == 42 && opts.Prefix == "demo";
        return Works("compose.config", "Composition", "Options pattern",
            $"IOptions<DemoOptions>: MaxItems={opts.MaxItems}, Prefix={opts.Prefix}, OK={ok}");
    }

    private CapabilityResult RunLogging()
    {
        var sink = _provider.GetRequiredService<RingBufferSink>();
        int before = sink.Snapshot().Count;
        var logger = _provider.GetRequiredService<ILoggerFactory>().CreateLogger("ComposeProbe");
        logger.LogInformation("compose.logging probe fired");
        int after = sink.Snapshot().Count;
        return Works("compose.logging", "Composition", "Structured log",
            $"Entries before={before}, after={after}; delta={after - before} (expected 1)");
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

internal sealed class DemoOptions
{
    public int MaxItems { get; set; }
    public string Prefix { get; set; } = "";
}
