using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace KestrelBackend;

/// <summary>
/// Single entry point for wiring the entire server: DI container, logging, router,
/// middleware pipeline, and all capability modules. Both the native bootstrap and the
/// test harness call <see cref="CreateHost"/> — nothing else builds the graph.
/// </summary>
internal static class ServerComposition
{
    public static (IDisposable host, int port) CreateHost(int port, IEnumerable<ICapabilityModule>? extraModules = null)
    {
        var sink = new RingBufferSink(capacity: 500);

        var services = new ServiceCollection();
        services.AddSingleton(sink);
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(new RingBufferLoggerProvider(sink));
        });

        // Capability modules — populated in Phase 2+. Registered here so the DI
        // graph resolves them in order; each module is ICapabilityModule.
        RegisterModules(services);

        // Allow callers (test harness, integration tests) to inject additional modules
        if (extraModules is not null)
            foreach (var m in extraModules)
                services.AddSingleton<ICapabilityModule>(m);

        // Catalog aggregates all registered modules
        services.AddSingleton<CapabilityCatalog>(sp =>
        {
            var modules = sp.GetServices<ICapabilityModule>();
            var logger = sp.GetRequiredService<ILogger<CapabilityCatalog>>();
            return new CapabilityCatalog(logger, modules);
        });

        var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<CapabilityCatalog>();
        var logger = provider.GetRequiredService<ILogger<RawHttpHost>>();

        var router = BuildRoutes(sink, catalog, logger);
        var pipeline = new RequestPipeline(router, logger);
        var host = new RawHttpHost(pipeline);

        host.Start(port);
        return (host, host.BoundPort);
    }

    private static void RegisterModules(IServiceCollection services)
    {
        // Phase 2 modules registered here as each is added.
        // (Empty until Phase 2; the catalog starts with zero descriptors.)
    }

    private static Router BuildRoutes(RingBufferSink sink, CapabilityCatalog catalog, ILogger logger)
    {
        var router = new Router();
        var processor = new LegacyLib.DataProcessor();

        // Health
        router.Map("GET", "/health", (req, rv, ct) =>
            Task.FromResult(HttpResponse.Text("ok")));

        // Legacy / back-compat (kept on both paths)
        RouteHandler legacyHandler = (req, rv, ct) =>
        {
            string input = req.Query.TryGetValue("input", out string? v) ? v : "ping";
            var result = processor.Process(input);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(result, ApiJsonContext.Default.ProcessResult);
            return Task.FromResult(HttpResponse.Json(json));
        };
        router.Map("GET", "/api/process", legacyHandler);
        router.Map("GET", "/api/legacy/process", legacyHandler);

        // Diagnostics
        router.Map("GET", "/api/diag/info", DiagInfo.Handler);
        router.Map("GET", "/api/diag/logs", (req, rv, ct) =>
        {
            var entries = sink.Snapshot();
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(entries, ApiJsonContext.Default.IReadOnlyListLogEntry);
            return Task.FromResult(HttpResponse.Json(json));
        });

        // Capability endpoints
        router.Map("GET", "/api/capabilities", (req, rv, ct) =>
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                catalog.Descriptors, ApiJsonContext.Default.IReadOnlyListCapabilityDescriptor);
            return Task.FromResult(HttpResponse.Json(json));
        });

        router.Map("POST", "/api/capabilities/{id}/run", async (req, rv, ct) =>
        {
            var result = await catalog.RunAsync(rv["id"], ct).ConfigureAwait(false);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(result, ApiJsonContext.Default.CapabilityResult);
            return HttpResponse.Json(json);
        });

        router.Map("POST", "/api/capabilities/run-all", async (req, rv, ct) =>
        {
            var results = await catalog.RunAllAsync(ct).ConfigureAwait(false);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                results, ApiJsonContext.Default.IReadOnlyListCapabilityResult);
            return HttpResponse.Json(json);
        });

        // Module-specific interactive endpoints
        foreach (var module in catalog.Modules)
            module.MapRoutes(router);

        return router;
    }
}
