using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LegacyLib;

namespace KestrelBackend;

/// <summary>
/// Dependency-free HTTP/1.1 server used when the ASP.NET Core framework pack is not
/// available for the iOS target — the always-buildable, trivially NativeAOT-safe
/// fallback. It implements just enough of HTTP to serve the PoC contract over
/// loopback and is selected automatically unless <c>-p:UseKestrel=true</c> is passed.
/// </summary>
internal sealed class RawHttpHost : IBackendHost
{
    private readonly DataProcessor _processor = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int BoundPort { get; private set; }

    public void Start(int port)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        BoundPort = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;

        // Fire-and-forget accept loop on a background thread; Start returns at once.
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
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break; // listener stopped
            }

            // Handle each connection independently so one slow client cannot stall the loop.
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using NetworkStream stream = client.GetStream();

                string requestLine = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                string target = ParseRequestTarget(requestLine);

                (string status, string contentType, byte[] body) = Route(target);

                await WriteResponseAsync(stream, status, contentType, body, ct).ConfigureAwait(false);

                // Graceful TCP close: signal end of our sends so the client can read the
                // response, then drain unread request bytes. Without this, closing a socket
                // with unread data in the receive buffer sends RST on Windows, which aborts
                // the response before the client finishes reading it.
                client.Client.Shutdown(SocketShutdown.Send);
                var drain = new byte[4096];
                while (await stream.ReadAsync(drain, ct).ConfigureAwait(false) > 0) { }
            }
            catch
            {
                // Best-effort server: silently drop malformed or aborted connections.
            }
        }
    }

    private (string status, string contentType, byte[] body) Route(string target)
    {
        if (target.StartsWith("/api/process", StringComparison.Ordinal))
        {
            string input = ParseQueryValue(target, "input") ?? "ping";
            ProcessResult result = _processor.Process(input);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(result, ApiJsonContext.Default.ProcessResult);
            return ("200 OK", "application/json; charset=utf-8", json);
        }

        if (target.StartsWith("/health", StringComparison.Ordinal))
        {
            return ("200 OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("ok"));
        }

        return ("404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"));
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        // Read the first line ("GET /path?q=v HTTP/1.1"); headers/body are ignored
        // because the PoC only serves bodyless GETs.
        var builder = new StringBuilder(128);
        var one = new byte[1];

        while (builder.Length < 8192)
        {
            int read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            char c = (char)one[0];
            if (c == '\n')
            {
                break;
            }

            if (c != '\r')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string ParseRequestTarget(string requestLine)
    {
        string[] parts = requestLine.Split(' ');
        return parts.Length >= 2 ? parts[1] : "/";
    }

    private static string? ParseQueryValue(string requestTarget, string key)
    {
        int q = requestTarget.IndexOf('?');
        if (q < 0 || q == requestTarget.Length - 1)
        {
            return null;
        }

        string query = requestTarget.Substring(q + 1);
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (string.Equals(pair.Substring(0, eq), key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
        }

        return null;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        byte[] body,
        CancellationToken ct)
    {
        string header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header).AsMemory(), ct).ConfigureAwait(false);
        await stream.WriteAsync(body.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
