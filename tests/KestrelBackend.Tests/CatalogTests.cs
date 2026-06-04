using System.Text.Json;

namespace KestrelBackend.Tests;

public sealed class CatalogTests
{
    private static CapabilityCatalog MakeCatalog(params ICapabilityModule[] modules)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CapabilityCatalog>.Instance;
        return new CapabilityCatalog(logger, modules);
    }

    [Fact]
    public void Catalog_AggregatesDescriptors()
    {
        var catalog = MakeCatalog(new FakeModule());
        Assert.Single(catalog.Descriptors);
        Assert.Equal("test.ok", catalog.Descriptors[0].Id);
    }

    [Fact]
    public async Task Catalog_RunAsync_KnownId_ReturnsWorks()
    {
        var catalog = MakeCatalog(new FakeModule());
        var result = await catalog.RunAsync("test.ok", CancellationToken.None);
        Assert.Equal(Verdict.Works, result.Verdict);
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Catalog_RunAsync_UnknownId_ReturnsFails()
    {
        var catalog = MakeCatalog(new FakeModule());
        var result = await catalog.RunAsync("does.not.exist", CancellationToken.None);
        Assert.Equal(Verdict.Fails, result.Verdict);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Catalog_RunAllAsync_RunsAllModules()
    {
        var catalog = MakeCatalog(new FakeModule(), new FakeModule2());
        var results = await catalog.RunAllAsync(CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(Verdict.Works, r.Verdict));
    }

    [Fact]
    public async Task CapabilityEndpoint_RunsProbe()
    {
        using var fx = new HostFixtureWith(new FakeModule());
        var resp = await fx.Client.PostAsync("/api/capabilities/test.ok/run", null);
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Works", doc.RootElement.GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task CapabilityEndpoint_ListDescriptors()
    {
        using var fx = new HostFixtureWith(new FakeModule());
        var resp = await fx.Client.GetAsync("/api/capabilities");
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        // extra modules append after registered modules — search by id, not position
        bool found = doc.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("id").GetString() == "test.ok");
        Assert.True(found, "test.ok descriptor not found in capabilities list");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed class FakeModule : ICapabilityModule
    {
        public IEnumerable<CapabilityDescriptor> Describe() =>
        [
            new("test.ok", "Test", "OK probe", "Always returns Works", Verdict.Works, "FakeModule")
        ];

        public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
            Task.FromResult(new CapabilityResult { Id = id, Verdict = Verdict.Works, Detail = "fake ok" });

        public void MapRoutes(Router router) { }
    }

    private sealed class FakeModule2 : ICapabilityModule
    {
        public IEnumerable<CapabilityDescriptor> Describe() =>
        [
            new("test.ok2", "Test", "OK2 probe", "Always Works", Verdict.Works, "FakeModule2")
        ];

        public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
            Task.FromResult(new CapabilityResult { Id = id, Verdict = Verdict.Works, Detail = "fake2 ok" });

        public void MapRoutes(Router router) { }
    }
}

/// <summary>HostFixture variant that accepts pre-built modules for catalog integration tests.</summary>
internal sealed class HostFixtureWith : IDisposable
{
    public HttpClient Client { get; }
    private readonly IDisposable _host;

    public HostFixtureWith(params ICapabilityModule[] modules)
    {
        var (host, port) = ServerComposition.CreateHost(0, modules);
        _host = host;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public void Dispose() { Client.Dispose(); _host.Dispose(); }
}
