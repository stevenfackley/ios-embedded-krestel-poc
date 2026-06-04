namespace KestrelBackend;

internal static class TestHost
{
    public static (IDisposable host, int port) Start()
    {
        var host = new RawHttpHost();
        host.Start(0); // port 0 → OS-assigned ephemeral port
        return (host, host.BoundPort);
    }
}
