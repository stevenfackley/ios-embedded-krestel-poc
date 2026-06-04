using System.Text;

namespace KestrelBackend;

internal sealed class HttpRequest
{
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public ReadOnlyMemory<byte> Body { get; init; }

    public static HttpRequest Parse(ReadOnlySpan<byte> raw)
    {
        // Split head from body on \r\n\r\n
        var separator = "\r\n\r\n"u8;
        int sepIdx = raw.IndexOf(separator);
        ReadOnlySpan<byte> head = sepIdx >= 0 ? raw[..sepIdx] : raw;
        ReadOnlySpan<byte> bodySpan = sepIdx >= 0 ? raw[(sepIdx + 4)..] : default;

        string headStr = Encoding.ASCII.GetString(head);
        string[] lines = headStr.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // Parse request line
        string method = "", rawPath = "/";
        if (lines.Length > 0)
        {
            var parts = lines[0].Split(' ');
            if (parts.Length >= 2) { method = parts[0]; rawPath = parts[1]; }
        }

        // Split path and query
        int qIdx = rawPath.IndexOf('?');
        string path = qIdx >= 0 ? rawPath[..qIdx] : rawPath;
        string queryStr = qIdx >= 0 ? rawPath[(qIdx + 1)..] : "";

        // Parse query string
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in queryStr.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0)
                query[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        // Parse headers (lowercase keys for case-insensitive lookup)
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int contentLength = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            string key = lines[i][..colon].Trim().ToLowerInvariant();
            string value = lines[i][(colon + 1)..].Trim();
            headers[key] = value;
            if (key == "content-length" && int.TryParse(value, out int cl))
                contentLength = cl;
        }

        // Slice body to Content-Length bytes (avoid consuming trailing pipeline data)
        int bodyLen = Math.Min(contentLength, bodySpan.Length);
        byte[] bodyBytes = bodyLen > 0 ? bodySpan[..bodyLen].ToArray() : [];

        return new HttpRequest
        {
            Method = method,
            Path = path,
            Query = query,
            Headers = headers,
            Body = bodyBytes
        };
    }
}
