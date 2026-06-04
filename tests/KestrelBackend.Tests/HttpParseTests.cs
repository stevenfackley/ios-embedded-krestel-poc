namespace KestrelBackend.Tests;

public sealed class HttpParseTests
{
    [Fact]
    public void ParsesGetWithQueryString()
    {
        var raw = "GET /api/x?q=1&z=hi HTTP/1.1\r\nHost: a\r\n\r\n";
        var req = HttpRequest.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
        Assert.Equal("GET", req.Method);
        Assert.Equal("/api/x", req.Path);
        Assert.Equal("1", req.Query["q"]);
        Assert.Equal("hi", req.Query["z"]);
        Assert.Equal(0, req.Body.Length);
    }

    [Fact]
    public void ParsesPostWithBody()
    {
        var raw = "POST /api/x?q=1 HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\n\r\nabc";
        var req = HttpRequest.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
        Assert.Equal("POST", req.Method);
        Assert.Equal("/api/x", req.Path);
        Assert.Equal("1", req.Query["q"]);
        Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(req.Body.Span));
    }

    [Fact]
    public void ParsesHeaderValues()
    {
        var raw = "GET / HTTP/1.1\r\nHost: localhost\r\nX-Custom: myval\r\n\r\n";
        var req = HttpRequest.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
        Assert.Equal("localhost", req.Headers["host"]);
        Assert.Equal("myval", req.Headers["x-custom"]);
    }

    [Fact]
    public void UnescapesQueryValues()
    {
        var raw = "GET /api?input=hello%20world HTTP/1.1\r\nHost: a\r\n\r\n";
        var req = HttpRequest.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
        Assert.Equal("hello world", req.Query["input"]);
    }

    [Fact]
    public void HttpResponse_Json_SetsContentType()
    {
        var r = HttpResponse.Json("{}"u8.ToArray());
        Assert.Equal(200, r.Status);
        Assert.Contains("application/json", r.Headers["Content-Type"]);
    }

    [Fact]
    public void HttpResponse_Problem_Returns500()
    {
        var r = HttpResponse.Problem(500, "test error", "corr-1");
        Assert.Equal(500, r.Status);
        Assert.Contains("problem+json", r.Headers["Content-Type"]);
    }
}
