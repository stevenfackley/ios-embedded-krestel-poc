namespace KestrelBackend;

internal static class TestHost
{
    public static (IDisposable host, int port) Start() => ServerComposition.CreateHost(0);
}
