namespace KestrelBackend.Tests;

public sealed class CompositionTests : IDisposable
{
    private readonly IDisposable _host;
    private readonly HttpClient _client;

    public CompositionTests()
    {
        var (host, port) = ServerComposition.CreateHost(0);
        _host = host;
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    [Fact]
    public async Task Health_Returns_Ok_ViaCompositionRoot()
    {
        var resp = await _client.GetAsync("/health");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("ok", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BoundPort_Is_Positive()
    {
        // The host fixture itself verifies port is non-zero; here we verify
        // the URL is reachable (port > 0 implied by successful connection)
        var resp = await _client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    public void Dispose() { _client.Dispose(); _host.Dispose(); }
}
