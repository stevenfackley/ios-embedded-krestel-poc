namespace KestrelBackend.Tests;

public sealed class HostFixture : IDisposable
{
    public HttpClient Client { get; }
    private readonly IDisposable _host;

    public HostFixture()
    {
        var (host, port) = TestHost.Start();
        _host = host;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public void Dispose()
    {
        Client.Dispose();
        _host.Dispose();
    }
}
