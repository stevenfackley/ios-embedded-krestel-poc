using System.Text;
using System.Text.Json;

namespace KestrelBackend.Tests;

/// <summary>
/// Integration tests for each Phase 2 capability module. Each test POSTs to
/// /api/capabilities/{id}/run and verifies verdict=="Works". A subset also
/// exercises the module-specific interactive endpoints.
/// </summary>
public sealed class ModuleTests : IClassFixture<HostFixture>
{
    private readonly HttpClient _client;
    public ModuleTests(HostFixture fx) => _client = fx.Client;

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<JsonElement> RunProbeAsync(string id)
    {
        var resp = await _client.PostAsync($"/api/capabilities/{Uri.EscapeDataString(id)}/run", null);
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static void AssertWorks(JsonElement el) =>
        Assert.Equal("Works", el.GetProperty("verdict").GetString());

    // ── Runtime ─────────────────────────────────────────────────────────────

    [Fact] public async Task Runtime_Info_Works() => AssertWorks(await RunProbeAsync("runtime.info"));
    [Fact] public async Task Runtime_Time_Works() => AssertWorks(await RunProbeAsync("runtime.time"));

    // ── Crypto ──────────────────────────────────────────────────────────────

    [Fact] public async Task Crypto_Sha_Works()    => AssertWorks(await RunProbeAsync("crypto.sha"));
    [Fact] public async Task Crypto_Hmac_Works()   => AssertWorks(await RunProbeAsync("crypto.hmac"));
    [Fact] public async Task Crypto_AesGcm_Works() => AssertWorks(await RunProbeAsync("crypto.aesgcm"));
    [Fact] public async Task Crypto_Rsa_Works()    => AssertWorks(await RunProbeAsync("crypto.rsa"));
    [Fact] public async Task Crypto_Pbkdf2_Works() => AssertWorks(await RunProbeAsync("crypto.pbkdf2"));

    [Fact]
    public async Task Crypto_HashEndpoint_ReturnsHexHash()
    {
        var resp = await _client.PostAsync(
            "/api/crypto/hash?input=hello",
            new StringContent("", Encoding.UTF8, "text/plain"));
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        string hash = doc.RootElement.GetProperty("hash").GetString()!;
        Assert.Equal(64, hash.Length); // SHA-256 hex = 64 chars
        Assert.Equal("SHA256", doc.RootElement.GetProperty("algorithm").GetString());
    }

    // ── Serialization ────────────────────────────────────────────────────────

    [Fact] public async Task Serialization_SourceGen_Works()  => AssertWorks(await RunProbeAsync("json.sourcegen"));
    [Fact] public async Task Serialization_Polymorphic_Works() => AssertWorks(await RunProbeAsync("json.polymorphic"));
    [Fact] public async Task Serialization_Base64_Works()      => AssertWorks(await RunProbeAsync("text.base64"));

    [Fact]
    public async Task Serialize_Endpoint_RoundTrips()
    {
        var resp = await _client.PostAsync(
            "/api/serialize?input=helloworld",
            new StringContent("", Encoding.UTF8, "text/plain"));
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("helloworld", doc.RootElement.GetProperty("decoded").GetString());
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    [Fact] public async Task Persistence_Sqlite_Works()   => AssertWorks(await RunProbeAsync("persist.sqlite"));
    [Fact] public async Task Persistence_JsonFile_Works() => AssertWorks(await RunProbeAsync("persist.jsonfile"));
    [Fact] public async Task Persistence_FileIo_Works()   => AssertWorks(await RunProbeAsync("persist.fileio"));

    [Fact]
    public async Task Notes_CreateListDelete()
    {
        // Create
        var create = await _client.PostAsync(
            "/api/notes?body=test-note",
            new StringContent("", Encoding.UTF8, "text/plain"));
        Assert.Equal(System.Net.HttpStatusCode.Created, create.StatusCode);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        long id = created.RootElement.GetProperty("id").GetInt64();

        // List — note should be there
        var list = await _client.GetAsync("/api/notes");
        list.EnsureSuccessStatusCode();
        var arr = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(arr.EnumerateArray(), e => e.GetProperty("id").GetInt64() == id);

        // Delete
        var del = await _client.DeleteAsync($"/api/notes/{id}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, del.StatusCode);
    }

    // ── Networking ───────────────────────────────────────────────────────────

    [Fact] public async Task Networking_HttpClient_Works() => AssertWorks(await RunProbeAsync("net.httpclient"));
    [Fact] public async Task Networking_Dns_Works()        => AssertWorks(await RunProbeAsync("net.dns"));
    [Fact] public async Task Networking_Router_Works()     => AssertWorks(await RunProbeAsync("net.router"));

    // ── Concurrency ──────────────────────────────────────────────────────────

    [Fact] public async Task Concurrency_Channels_Works()  => AssertWorks(await RunProbeAsync("concurrency.channels"));
    [Fact] public async Task Concurrency_Parallel_Works()  => AssertWorks(await RunProbeAsync("concurrency.parallel"));
    [Fact] public async Task Concurrency_Tasks_Works()     => AssertWorks(await RunProbeAsync("concurrency.tasks"));

    // ── Numerics ─────────────────────────────────────────────────────────────

    [Fact] public async Task Numerics_BigInt_Works()      => AssertWorks(await RunProbeAsync("numerics.bigint"));
    [Fact] public async Task Numerics_GenericMath_Works() => AssertWorks(await RunProbeAsync("numerics.genericmath"));
    [Fact] public async Task Numerics_Simd_Works()        => AssertWorks(await RunProbeAsync("numerics.simd"));

    // ── Text ─────────────────────────────────────────────────────────────────

    [Fact] public async Task Text_Regex_Works() => AssertWorks(await RunProbeAsync("text.regex"));

    [Fact]
    public async Task Regex_Endpoint_ReturnsWords()
    {
        var resp = await _client.PostAsync(
            "/api/regex?input=Hello+World",
            new StringContent("", Encoding.UTF8, "text/plain"));
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("matchCount").GetInt32());
    }

    // ── Compression ──────────────────────────────────────────────────────────

    [Fact] public async Task Compression_Gzip_Works()   => AssertWorks(await RunProbeAsync("compress.gzip"));
    [Fact] public async Task Compression_Brotli_Works() => AssertWorks(await RunProbeAsync("compress.brotli"));

    [Fact]
    public async Task Compress_Endpoint_ReturnsRatio()
    {
        var resp = await _client.PostAsync(
            "/api/compress?input=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            new StringContent("", Encoding.UTF8, "text/plain"));
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ratio").GetDouble() < 1.0,
            "gzip should compress repetitive input below 1.0 ratio");
    }

    // ── Composition (DI / Options / Logging) ─────────────────────────────────

    [Fact] public async Task Composition_Di_Works()      => AssertWorks(await RunProbeAsync("compose.di"));
    [Fact] public async Task Composition_Config_Works()  => AssertWorks(await RunProbeAsync("compose.config"));
    [Fact] public async Task Composition_Logging_Works() => AssertWorks(await RunProbeAsync("compose.logging"));

    // ── Legacy ───────────────────────────────────────────────────────────────

    [Fact] public async Task Legacy_Process_Works() => AssertWorks(await RunProbeAsync("legacy.process"));

    // ── Smoke: run-all returns all probes as Works ────────────────────────────

    [Fact]
    public async Task RunAll_AdvantageProbesWork()
    {
        var resp = await _client.PostAsync("/api/capabilities/run-all", null);
        resp.EnsureSuccessStatusCode();
        var arr = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        int count = arr.GetArrayLength();
        Assert.True(count >= 35, $"Expected at least 35 probes (25 Works + 12 Limits), got {count}");

        // Advantage probes (not limit.*) must all return Works
        var nonLimits = arr.EnumerateArray()
            .Where(e => !e.GetProperty("id").GetString()!.StartsWith("limit."))
            .ToList();
        Assert.All(nonLimits, e =>
            Assert.Equal("Works", e.GetProperty("verdict").GetString()));

        // Limitation probes must not return Works
        var limits = arr.EnumerateArray()
            .Where(e => e.GetProperty("id").GetString()!.StartsWith("limit."))
            .ToList();
        Assert.Equal(12, limits.Count);
        Assert.All(limits, e =>
            Assert.NotEqual("Works", e.GetProperty("verdict").GetString()));
    }
}
