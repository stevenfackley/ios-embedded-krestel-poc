using System.Runtime.InteropServices;

namespace KestrelBackend;

/// <summary>
/// Native entry points exported from the NativeAOT static library.
/// </summary>
/// <remarks>
/// Rules of the native boundary (enforced below):
///   • methods are <c>static</c> with only blittable args (<c>int</c>, <c>byte*</c>);
///   • no managed exception may unwind into native code — everything is caught and
///     surfaced as an <c>int</c> return code;
///   • unsafe spans are created with checked lengths to prevent buffer overruns.
/// </remarks>
public static unsafe class NativeBootstrap
{
    private static IDisposable? _host;
    private static readonly object Gate = new();

    /// <summary>Starts the embedded server. Idempotent and non-blocking.</summary>
    /// <param name="port">TCP port (0 = OS-assigned ephemeral port).</param>
    /// <returns>0 on success; -1 on failure (call kestrel_last_error for details).</returns>
    [UnmanagedCallersOnly(EntryPoint = "kestrel_start")]
    public static int Start(int port)
    {
        try
        {
            lock (Gate)
            {
                if (_host is not null) return 0;
                var (host, boundPort) = ServerComposition.CreateHost(port);
                _host = host;
                DiagInfo.PortForInfo = boundPort;
            }
            return 0;
        }
        catch (Exception ex)
        {
            NativeErrorBuffer.Capture("StartFailed", DescribeException(ex), null);
            return -1;
        }
    }

    /// <summary>
    /// Flattens an exception and its InnerException chain into one line. NativeAOT
    /// builds here set UseSystemResourceKeys=true and StackTraceSupport=false, which
    /// reduce a TypeInitializationException's own message to a bare resource key
    /// (e.g. "TypeInitialization_Type_NoTypeAvailable"). The real culprit lives in
    /// the inner exception, so we walk the whole chain.
    /// </summary>
    private static string DescribeException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" <- ");
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return sb.ToString();
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
                DiagInfo.PortForInfo = 0;
            }
        }
        catch
        {
            // Never throw across the native boundary.
        }
    }

    /// <summary>
    /// Copies the last error captured by the pipeline as UTF-8 into <paramref name="buf"/>.
    /// Format: <c>type|message|correlationId</c>.
    /// </summary>
    /// <param name="buf">Caller-allocated buffer to receive the UTF-8 string.</param>
    /// <param name="bufLen">Size of <paramref name="buf"/> in bytes.</param>
    /// <returns>
    /// Bytes written (&gt;=1) on success; 0 if no error is buffered;
    /// -(bytes needed) if the buffer is too small.
    /// </returns>
    [UnmanagedCallersOnly(EntryPoint = "kestrel_last_error")]
    public static int LastError(byte* buf, int bufLen)
    {
        try
        {
            if (buf is null || bufLen <= 0) return -1;
            return NativeErrorBuffer.CopyInto(new Span<byte>(buf, bufLen));
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Copies a JSON diagnostic snapshot as UTF-8 into <paramref name="buf"/>.
    /// Includes port, uptime, request count, runtime version, and OS.
    /// </summary>
    /// <param name="buf">Caller-allocated buffer to receive the UTF-8 JSON.</param>
    /// <param name="bufLen">Size of <paramref name="buf"/> in bytes.</param>
    /// <returns>
    /// Bytes written on success; -(bytes needed) if the buffer is too small; -1 on error.
    /// </returns>
    [UnmanagedCallersOnly(EntryPoint = "kestrel_info")]
    public static int Info(byte* buf, int bufLen)
    {
        try
        {
            if (buf is null || bufLen <= 0) return -1;
            return DiagInfo.CopySnapshotInto(new Span<byte>(buf, bufLen));
        }
        catch
        {
            return -1;
        }
    }
}
