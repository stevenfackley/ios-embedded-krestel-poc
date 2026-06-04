namespace KestrelBackend.Tests;

public sealed class RouterTests
{
    private static HttpRequest Get(string path) => new HttpRequest
    {
        Method = "GET",
        Path = path,
        Query = new Dictionary<string, string>(),
        Headers = new Dictionary<string, string>(),
        Body = default
    };

    [Fact]
    public async Task ExactRoute_Matches()
    {
        var router = new Router();
        router.Map("GET", "/health", (req, rv, ct) => Task.FromResult(HttpResponse.Text("ok")));
        var resp = await router.DispatchAsync(Get("/health"), CancellationToken.None);
        Assert.Equal(200, resp.Status);
    }

    [Fact]
    public async Task TemplateRoute_CapturesParam()
    {
        var router = new Router();
        string? captured = null;
        router.Map("GET", "/api/notes/{id}", (req, rv, ct) =>
        {
            captured = rv["id"];
            return Task.FromResult(HttpResponse.Text(captured ?? ""));
        });

        var resp = await router.DispatchAsync(Get("/api/notes/7"), CancellationToken.None);
        Assert.Equal(200, resp.Status);
        Assert.Equal("7", captured);
    }

    [Fact]
    public async Task TemplateRoute_NoMatchOnPartial()
    {
        var router = new Router();
        router.Map("GET", "/api/notes/{id}", (req, rv, ct) => Task.FromResult(HttpResponse.Text("hit")));
        var resp = await router.DispatchAsync(Get("/api/notes"), CancellationToken.None);
        Assert.Equal(404, resp.Status);
    }

    [Fact]
    public async Task WrongMethod_Returns405()
    {
        var router = new Router();
        router.Map("POST", "/api/x", (req, rv, ct) => Task.FromResult(HttpResponse.Text("ok")));
        var req = new HttpRequest { Method = "GET", Path = "/api/x",
            Query = new Dictionary<string, string>(), Headers = new Dictionary<string, string>(), Body = default };
        var resp = await router.DispatchAsync(req, CancellationToken.None);
        Assert.Equal(405, resp.Status);
    }

    [Fact]
    public async Task MultiSegmentTemplate_Works()
    {
        var router = new Router();
        router.Map("GET", "/api/caps/{cat}/{id}", (req, rv, ct) =>
        {
            var body = $"{rv["cat"]}/{rv["id"]}";
            return Task.FromResult(HttpResponse.Text(body));
        });

        var resp = await router.DispatchAsync(Get("/api/caps/crypto/sha256"), CancellationToken.None);
        var text = System.Text.Encoding.UTF8.GetString(resp.Body);
        Assert.Equal("crypto/sha256", text);
    }
}
