using System.Runtime.InteropServices;

namespace KestrelBackend;

/// <summary>
/// Native entry points exported from the NativeAOT static library. Swift calls
/// <c>kestrel_start</c> during app launch to boot the loopback server on a background
/// thread, and <c>kestrel_stop</c> on teardown.
/// </summary>
/// <remarks>
/// Rules of the native boundary (enforced below):
///   • methods are <c>static</c> and take only blittable arguments (<c>int</c>);
///   • no managed exception may unwind into native code — everything is caught and
///     surfaced as an <c>int</c> return code instead.
/// The .NET runtime is initialized lazily on the first managed call into the static
/// library, so no explicit runtime-init export is required.
/// </remarks>
public static class NativeBootstrap
{
    private static IDisposable? _host;
    private static readonly object Gate = new();

    /// <summary>Starts the embedded server. Idempotent and non-blocking.</summary>
    /// <param name="port">TCP port on the loopback interface (e.g. 5001).</param>
    /// <returns>0 on success; -1 on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "kestrel_start")]
    public static int Start(int port)
    {
        try
        {
            lock (Gate)
            {
                if (_host is not null) return 0;
                var (host, _) = ServerComposition.CreateHost(port);
                _host = host;
            }
            return 0;
        }
        catch (Exception ex)
        {
            NativeErrorBuffer.Capture("StartFailed", ex.Message, null);
            return -1;
        }
    }

    /// <summary>Stops the embedded server if it is running.</summary>
    [UnmanagedCallersOnly(EntryPoint = "kestrel_stop")]
    public static void Stop()
    {
        try
        {
            lock (Gate)
            {
                _host?.Dispose();
                _host = null;
            }
        }
        catch
        {
            // Never throw across the native boundary.
        }
    }
}
