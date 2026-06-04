using System.Text;

namespace KestrelBackend;

internal sealed class HttpResponse
{
    public int Status { get; init; } = HttpStatus.Ok;
    public Dictionary<string, string> Headers { get; init; } = [];
    public byte[] Body { get; init; } = [];

    public static HttpResponse Json(byte[] body, int status = HttpStatus.Ok) => new()
    {
        Status = status,
        Body = body,
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json; charset=utf-8"
        }
    };

    public static HttpResponse Text(string text, int status = HttpStatus.Ok) => new()
    {
        Status = status,
        Body = Encoding.UTF8.GetBytes(text),
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain; charset=utf-8"
        }
    };

    public static HttpResponse Problem(int status, string detail, string? correlationId = null)
    {
        // Hand-rolled to avoid reflection-based JsonSerializer with anonymous types (AOT-unsafe).
        // Task 0.4 introduces a typed ProblemDetails + source-gen serializer; this stub is used
        // only by routes that exist before the pipeline is wired (HttpStatus.NotFound route, etc.).
        string corr = EscapeJson(correlationId ?? "");
        string json = $"{{\"type\":\"about:blank\",\"title\":\"{EscapeJson(HttpStatus.Phrase(status))}\","
                    + $"\"status\":{status},\"detail\":\"{EscapeJson(detail)}\","
                    + $"\"correlationId\":\"{corr}\"}}";
        return new HttpResponse
        {
            Status = status,
            Body = Encoding.UTF8.GetBytes(json),
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/problem+json; charset=utf-8"
            }
        };
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public static HttpResponse NotFound(string path) =>
        Problem(HttpStatus.NotFound, $"No route matched: {path}");

    public byte[] ToBytes()
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {Status} {HttpStatus.Phrase(Status)}\r\n");
        sb.Append($"Content-Length: {Body.Length}\r\n");
        foreach (var (k, v) in Headers)
            sb.Append($"{k}: {v}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        byte[] header = Encoding.ASCII.GetBytes(sb.ToString());
        byte[] result = new byte[header.Length + Body.Length];
        header.CopyTo(result, 0);
        Body.CopyTo(result, header.Length);
        return result;
    }
}
