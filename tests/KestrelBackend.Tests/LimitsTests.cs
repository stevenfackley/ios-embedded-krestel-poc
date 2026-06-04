using System.Text.Json;

namespace KestrelBackend.Tests;

/// <summary>
/// Verifies limitation probes return the expected verdict on this platform (JIT/.NET 9).
/// On NativeAOT/iOS, dynamically-generated probes return Fails; architectural probes are
/// always Fails regardless of platform.
/// </summary>
public sealed class LimitsTests : IClassFixture<HostFixture>
{
    private readonly HttpClient _client;
    public LimitsTests(HostFixture fx) => _client = fx.Client;

    private async Task<string> VerdictAsync(string id)
    {
        var resp = await _client.PostAsync($"/api/capabilities/{Uri.EscapeDataString(id)}/run", null);
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("verdict").GetString()!;
    }

    // ── probes that return Limited on JIT (but Fails on NativeAOT) ───────────

    [Fact]
    public async Task ExpressionCompile_IsLimited_OnJit() =>
        Assert.Equal("Limited", await VerdictAsync("limit.expressioncompile"));

    [Fact]
    public async Task ReflectionEmit_IsLimited_OnJit() =>
        Assert.Equal("Limited", await VerdictAsync("limit.reflectionemit"));

    [Fact]
    public async Task Process_IsLimited_OnJit() =>
        Assert.Equal("Limited", await VerdictAsync("limit.process"));

    [Fact]
    public async Task DynamicLoad_IsLimited_OnJit() =>
        Assert.Equal("Limited", await VerdictAsync("limit.dynamicload"));

    [Fact]
    public async Task Globalization_IsLimited() =>
        Assert.Equal("Limited", await VerdictAsync("limit.globalization"));

    [Fact]
    public async Task StackTrace_IsLimited() =>
        Assert.Equal("Limited", await VerdictAsync("limit.stacktrace"));

    [Fact]
    public async Task ReflectionInvoke_IsLimited() =>
        Assert.Equal("Limited", await VerdictAsync("limit.reflectioninvoke"));

    [Fact]
    public async Task JsonReflection_IsLimited_OnJit() =>
        Assert.Equal("Limited", await VerdictAsync("limit.jsonreflection"));

    [Fact]
    public async Task EventSource_IsLimited() =>
        Assert.Equal("Limited", await VerdictAsync("limit.eventsource"));

    // ── architectural Fails — always Fails regardless of platform ────────────

    [Fact]
    public async Task Newtonsoft_IsFails() =>
        Assert.Equal("Fails", await VerdictAsync("limit.newtonsoft"));

    [Fact]
    public async Task Kestrel_IsFails() =>
        Assert.Equal("Fails", await VerdictAsync("limit.kestrel"));

    [Fact]
    public async Task Grpc_IsFails() =>
        Assert.Equal("Fails", await VerdictAsync("limit.grpc"));

    // ── descriptor smoke: all 12 limit.* probes are listed ───────────────────

    [Fact]
    public async Task AllLimitProbes_AreDescribed()
    {
        var resp = await _client.GetAsync("/api/capabilities");
        resp.EnsureSuccessStatusCode();
        var arr = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var ids = arr.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .Where(id => id.StartsWith("limit."))
            .ToHashSet();
        string[] expected =
        [
            "limit.expressioncompile", "limit.reflectionemit", "limit.process",
            "limit.dynamicload", "limit.globalization", "limit.stacktrace",
            "limit.reflectioninvoke", "limit.jsonreflection", "limit.newtonsoft",
            "limit.eventsource", "limit.kestrel", "limit.grpc",
        ];
        foreach (string id in expected)
            Assert.Contains(id, ids);
    }
}
