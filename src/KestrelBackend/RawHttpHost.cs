using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LegacyLib;

namespace KestrelBackend;

/// <summary>
/// Dependency-free HTTP/1.1 server. Selects an ephemeral port when port 0 is passed.
/// When constructed with a <see cref="RequestPipeline"/> (Task 0.6+), every accepted
/// connection dispatches through the full middleware stack; without one it routes
/// directly (used only by the legacy TestHost path before composition is wired).
/// </summary>
internal sealed class RawHttpHost : IBackendHost
{
    private readonly RequestPipeline? _pipeline;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public RawHttpHost(RequestPipeline? pipeline = null) => _pipeline = pipeline;

    public int BoundPort { get; private set; }

    public void Start(int port)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();

                // Read the full request (headers + body) into a buffer
                byte[] buf = new byte[65536];
                int total = 0;
                while (total < buf.Length)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), ct).ConfigureAwait(false);
                    if (n == 0) break;
                    total += n;
                    // Stop once we've seen the full request: end-of-headers + body up to Content-Length
                    if (IsComplete(buf.AsSpan(0, total))) break;
                }

                var request = HttpRequest.Parse(buf.AsSpan(0, total));
                var response = _pipeline is not null
                    ? await _pipeline.ProcessAsync(request, ct).ConfigureAwait(false)
                    : Route(request);
                await stream.WriteAsync(response.ToBytes().AsMemory(), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                // Graceful close: prevents RST on Windows when receive buffer is not fully drained
                client.Client.Shutdown(SocketShutdown.Send);
                var drain = new byte[4096];
                while (await stream.ReadAsync(drain, ct).ConfigureAwait(false) > 0) { }
            }
            catch
            {
                // Best-effort: drop malformed or aborted connections silently
            }
        }
    }

    private static bool IsComplete(ReadOnlySpan<byte> buf)
    {
        // Headers end at \r\n\r\n
        var sep = "\r\n\r\n"u8;
        int sepIdx = buf.IndexOf(sep);
        if (sepIdx < 0) return false;

        // Check Content-Length to know if body is fully received
        string head = System.Text.Encoding.ASCII.GetString(buf[..sepIdx]);
        int bodyReceived = buf.Length - sepIdx - 4;

        foreach (var line in head.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                if (int.TryParse(line[(colon + 1)..].Trim(), out int cl))
                    return bodyReceived >= cl;
            }
        }
        return true; // no body expected
    }

    // Temporary direct router until Task 0.6 wires the pipeline.
    // Named _processor stored in local; this field removed once pipeline owns it.
    private readonly DataProcessor _processor = new();

    internal HttpResponse Route(HttpRequest request)
    {
        if (request.Path.StartsWith("/api/process", StringComparison.Ordinal)
         || request.Path.StartsWith("/api/legacy/process", StringComparison.Ordinal))
        {
            string input = request.Query.TryGetValue("input", out string? v) ? v : "ping";
            var result = _processor.Process(input);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(result, ApiJsonContext.Default.ProcessResult);
            return HttpResponse.Json(json);
        }

        if (request.Path == "/health")
            return HttpResponse.Text("ok");

        return HttpResponse.NotFound(request.Path);
    }
}
