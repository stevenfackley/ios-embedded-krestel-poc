#if USE_KESTREL
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using LegacyLib;

namespace KestrelBackend;

/// <summary>
/// ASP.NET Core Kestrel host built on the AOT-friendly slim builder. Compiled only
/// when the ASP.NET Core framework pack resolves for the iOS target
/// (<c>-p:UseKestrel=true</c>). Its external contract is byte-for-byte identical to
/// <see cref="RawHttpHost"/>, so the Swift client is unaffected by which one ships.
/// </summary>
internal sealed class KestrelHost : IBackendHost
{
    private WebApplication? _app;
    private int _port;

    public int BoundPort => _port;

    public void Start(int port)
    {
        _port = port;
        // CreateSlimBuilder omits the heavyweight hosting defaults (config providers,
        // logging back-ends, etc.) that fight the trimmer — the right base for AOT.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        // Wire the source-generated JSON context so serialization stays reflection-free.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        WebApplication app = builder.Build();

        // The Request Delegate Generator turns these lambdas into compiled endpoints.
        app.MapGet("/api/process", (string? input) =>
        {
            ProcessResult result = new DataProcessor().Process(input ?? "ping");
            return Results.Json(result, ApiJsonContext.Default.ProcessResult);
        });

        app.MapGet("/health", () => Results.Text("ok"));

        _app = app;

        // StartAsync (unlike Run) returns as soon as Kestrel has bound its listener instead
        // of blocking for the app lifetime. We deliberately wait on it so the socket is
        // guaranteed to be accepting before Start returns to the native caller — that closes
        // the first-request "connection refused" race the raw host doesn't have (it binds
        // synchronously). Swift calls kestrel_start off the main thread, so this brief wait
        // never stalls app launch. No SynchronizationContext is installed by the slim builder,
        // so blocking here cannot deadlock the awaited continuations.
        app.StartAsync().GetAwaiter().GetResult();
    }

    public void Stop()
    {
        _app?.StopAsync().GetAwaiter().GetResult();
        _app = null;
    }

    public void Dispose() => Stop();
}
#endif
