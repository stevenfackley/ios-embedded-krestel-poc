using System.Text.Json;

namespace KestrelBackend;

/// <summary>
/// Composes the three middleware stages (correlation-id, request-logging, exception)
/// around the router's terminal dispatch. Each stage can short-circuit or mutate the
/// response before returning to the caller.
/// </summary>
internal sealed class RequestPipeline
{
    private readonly Router _router;

    public RequestPipeline(Router router) => _router = router;

    public Task<HttpResponse> ProcessAsync(HttpRequest request, CancellationToken ct)
    {
        // Build pipeline: CorrelationId → (RequestLogging → (Exception → Router))
        return CorrelationIdStage(request, ct);
    }

    private Task<HttpResponse> CorrelationIdStage(HttpRequest request, CancellationToken ct)
    {
        // Use incoming header if present, otherwise mint a new short GUID segment
        string corrId = request.Headers.TryGetValue("x-correlation-id", out string? h) && !string.IsNullOrEmpty(h)
            ? h
            : Guid.NewGuid().ToString("N")[..12];

        CorrelationContext.Current = corrId;

        return RequestLoggingStage(request, corrId, ct);
    }

    private async Task<HttpResponse> RequestLoggingStage(HttpRequest request, string corrId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await ExceptionStage(request, corrId, ct).ConfigureAwait(false);
        sw.Stop();

        // Add correlation header to every response
        resp.Headers["X-Correlation-Id"] = corrId;

        return resp;
    }

    private async Task<HttpResponse> ExceptionStage(HttpRequest request, string corrId, CancellationToken ct)
    {
        try
        {
            return await _router.DispatchAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NativeErrorBuffer.Capture("Exception", ex.Message, corrId);

            var problem = ProblemDetails.From(ex, HttpStatus.InternalServerError, corrId, request.Path);
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(problem, ApiJsonContext.Default.ProblemDetails);
            return new HttpResponse
            {
                Status = HttpStatus.InternalServerError,
                Body = body,
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/problem+json; charset=utf-8"
                }
            };
        }
    }
}
