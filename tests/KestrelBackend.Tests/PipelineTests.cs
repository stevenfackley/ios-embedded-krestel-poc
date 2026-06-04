namespace KestrelBackend.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task ThrowingHandler_Returns500_WithProblemDetails()
    {
        var router = new Router();
        router.Map("GET", "/boom", (req, rv, ct) => throw new InvalidOperationException("test explosion"));

        var pipeline = new RequestPipeline(router);
        var req = MakeRequest("GET", "/boom");
        var resp = await pipeline.ProcessAsync(req, CancellationToken.None);

        Assert.Equal(500, resp.Status);
        Assert.Contains("problem+json", resp.Headers["Content-Type"]);
        string body = System.Text.Encoding.UTF8.GetString(resp.Body);
        Assert.Contains("correlationId", body);
        Assert.Contains("test explosion", body);
    }

    [Fact]
    public async Task SuccessfulHandler_CarriesCorrelationHeader()
    {
        var router = new Router();
        router.Map("GET", "/ok", (req, rv, ct) => Task.FromResult(HttpResponse.Text("fine")));

        var pipeline = new RequestPipeline(router);
        var resp = await pipeline.ProcessAsync(MakeRequest("GET", "/ok"), CancellationToken.None);

        Assert.Equal(200, resp.Status);
        Assert.True(resp.Headers.ContainsKey("X-Correlation-Id"));
        Assert.False(string.IsNullOrEmpty(resp.Headers["X-Correlation-Id"]));
    }

    [Fact]
    public async Task CorrelationId_PropagatesFromRequestHeader()
    {
        var router = new Router();
        string? seen = null;
        router.Map("GET", "/cid", (req, rv, ct) =>
        {
            seen = CorrelationContext.Current;
            return Task.FromResult(HttpResponse.Text("ok"));
        });

        var pipeline = new RequestPipeline(router);
        var req = MakeRequest("GET", "/cid");
        ((Dictionary<string, string>)req.Headers)["x-correlation-id"] = "my-corr-42";
        await pipeline.ProcessAsync(req, CancellationToken.None);

        Assert.Equal("my-corr-42", seen);
    }

    private static HttpRequest MakeRequest(string method, string path) => new()
    {
        Method = method,
        Path = path,
        Query = new Dictionary<string, string>(),
        Headers = new Dictionary<string, string>(),
        Body = default
    };
}
