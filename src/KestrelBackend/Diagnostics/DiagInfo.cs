using System.Text.Json;
using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class DiagInfo
{
    private static readonly System.Diagnostics.Stopwatch _uptime = System.Diagnostics.Stopwatch.StartNew();
    private static long _requestsServed;

    // Set by NativeBootstrap.Start so kestrel_info and /api/diag/info report the bound port.
    internal static int PortForInfo;

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("dotnetVersion")]
    public string DotnetVersion { get; init; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    [JsonPropertyName("hostType")]
    public string HostType { get; init; } = "RawHttpHost";

    [JsonPropertyName("uptimeSeconds")]
    public double UptimeSeconds { get; init; }

    [JsonPropertyName("requestsServed")]
    public long RequestsServed { get; init; }

    [JsonPropertyName("os")]
    public string Os { get; init; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    [JsonPropertyName("processArch")]
    public string ProcessArch { get; init; } =
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

    public static void IncrementRequests() =>
        System.Threading.Interlocked.Increment(ref _requestsServed);

    public static RouteHandler Handler => (req, rv, ct) =>
    {
        IncrementRequests();
        var info = new DiagInfo
        {
            Port = PortForInfo,
            UptimeSeconds = _uptime.Elapsed.TotalSeconds,
            RequestsServed = System.Threading.Interlocked.Read(ref _requestsServed)
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(info, ApiJsonContext.Default.DiagInfo);
        return Task.FromResult(HttpResponse.Json(json));
    };

    public static int CopySnapshotInto(Span<byte> dest)
    {
        var info = new DiagInfo
        {
            Port = PortForInfo,
            UptimeSeconds = _uptime.Elapsed.TotalSeconds,
            RequestsServed = System.Threading.Interlocked.Read(ref _requestsServed)
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(info, ApiJsonContext.Default.DiagInfo);
        if (json.Length > dest.Length) return -json.Length;
        json.CopyTo(dest);
        return json.Length;
    }
}
