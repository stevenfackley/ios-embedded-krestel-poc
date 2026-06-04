namespace KestrelBackend.Tests;

public sealed class SmokeTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _fx;

    public SmokeTests(HostFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_Returns_Ok()
    {
        var resp = await _fx.Client.GetAsync("/health");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    [Fact]
    public async Task LegacyProcess_Returns_Hash()
    {
        var resp = await _fx.Client.GetAsync("/api/process?input=hello");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("hash", body);
        Assert.Contains("hello", body);
    }
}
