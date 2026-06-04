using System.Text.Json;

namespace KestrelBackend.Tests;

public sealed class DiagTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _fx;

    public DiagTests(HostFixture fx) => _fx = fx;

    [Fact]
    public async Task DiagInfo_HasExpectedFields()
    {
        var resp = await _fx.Client.GetAsync("/api/diag/info");
        resp.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("dotnetVersion", out _));
        Assert.True(root.TryGetProperty("hostType", out _));
        Assert.True(root.TryGetProperty("uptimeSeconds", out var uptime));
        Assert.True(uptime.GetDouble() >= 0);
    }

    [Fact]
    public async Task DiagLogs_ReturnsArray()
    {
        // Make a request first so there's at least one log entry
        await _fx.Client.GetAsync("/health");

        var resp = await _fx.Client.GetAsync("/api/diag/logs");
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task LegacyProcess_BothPaths_ReturnHash()
    {
        foreach (var path in new[] { "/api/process?input=hello", "/api/legacy/process?input=hello" })
        {
            var resp = await _fx.Client.GetAsync(path);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("hash", body);
        }
    }

    [Fact]
    public async Task Capabilities_Endpoint_ReturnsArray()
    {
        var resp = await _fx.Client.GetAsync("/api/capabilities");
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }
}
