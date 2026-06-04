namespace KestrelBackend;

using RouteValues = IReadOnlyDictionary<string, string>;

internal delegate Task<HttpResponse> RouteHandler(
    HttpRequest request,
    IReadOnlyDictionary<string, string> routeValues,
    CancellationToken ct);

internal sealed class Router
{
    private readonly List<RouteEntry> _routes = [];

    public void Map(string method, string template, RouteHandler handler)
        => _routes.Add(new RouteEntry(method.ToUpperInvariant(), template, handler));

    public async Task<HttpResponse> DispatchAsync(HttpRequest request, CancellationToken ct)
    {
        bool pathMatched = false;

        foreach (var entry in _routes)
        {
            if (!TryMatch(entry.Template, request.Path, out var routeValues))
                continue;

            pathMatched = true;

            if (!entry.Method.Equals(request.Method, StringComparison.OrdinalIgnoreCase))
                continue;

            return await entry.Handler(request, routeValues, ct).ConfigureAwait(false);
        }

        return pathMatched
            ? HttpResponse.Problem(HttpStatus.MethodNotAllowed, $"Method {request.Method} not allowed")
            : HttpResponse.NotFound(request.Path);
    }

    private static bool TryMatch(string template, string path,
        out IReadOnlyDictionary<string, string> values)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        values = captured;

        var tSegs = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pSegs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (tSegs.Length != pSegs.Length) return false;

        for (int i = 0; i < tSegs.Length; i++)
        {
            if (tSegs[i].StartsWith('{') && tSegs[i].EndsWith('}'))
            {
                string paramName = tSegs[i][1..^1];
                captured[paramName] = pSegs[i];
            }
            else if (!tSegs[i].Equals(pSegs[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record RouteEntry(string Method, string Template, RouteHandler Handler);
}
