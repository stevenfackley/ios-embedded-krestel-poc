namespace KestrelBackend;

/// <summary>
/// Abstraction over the in-process HTTP host. Lets the Kestrel implementation and
/// the dependency-free raw-socket fallback be swapped behind a single seam, so the
/// native boundary and the Swift client never change regardless of which one ships.
/// </summary>
internal interface IBackendHost : IDisposable
{
    /// <summary>Starts listening on 127.0.0.1:<paramref name="port"/>. Must not block.</summary>
    void Start(int port);

    /// <summary>Stops the server and releases the listening socket.</summary>
    void Stop();

    /// <summary>The port actually bound. Available after <see cref="Start"/> returns.
    /// Pass 0 to <see cref="Start"/> to let the OS pick a free port.</summary>
    int BoundPort { get; }
}
