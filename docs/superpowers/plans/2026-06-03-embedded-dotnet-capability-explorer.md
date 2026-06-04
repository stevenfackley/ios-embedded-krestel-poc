# Embedded .NET Capability Explorer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline, batched with checkpoints) to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking. The plan author is also the executor (inline), so repetitive probe tasks use a fully-worked exemplar + an instance table rather than repeating identical code.

**Goal:** Extend the embedded-.NET-on-iOS PoC into a multi-screen capability explorer that runs ~25 "works" probes + ~12 "limited/fails" probes through an in-process HTTP server, with enterprise logging and error handling, all proven on Windows via analyzers + an xUnit harness.

**Architecture:** A NativeAOT shared library (`KestrelBackend`, net9.0) exposes a hand-rolled HTTP/1.1 server (`RawHttpHost`) fronted by a middleware pipeline (correlation-id → logging → exception→ProblemDetails) and a `Router`. A `CapabilityCatalog` registry drives a data-driven Swift matrix. Legacy `netstandard2.0` logic is reused untouched. The C ABI grows from 2→4 read-only blittable functions.

**Tech Stack:** net9.0 + PublishAot (`IsAotCompatible`), `Microsoft.Extensions.{Logging,DependencyInjection,Configuration,Options}`, `Microsoft.Data.Sqlite`, `System.Text.Json` source-gen, xUnit, SwiftUI.

---

## Execution model & conventions

- **Branch:** `feat/dotnet-capability-explorer` (already created).
- **Commits:** Conventional Commits, **no** `Co-Authored-By`/AI attribution (workspace rule). Commit at each task's final step.
- **TFM:** `KestrelBackend` and tests stay **net9.0**. Do not bump.
- **Windows proof per task:** `dotnet build` (analyzers clean) + `dotnet test` (harness green). Swift tasks (Phase 5) are **build-on-Mac**; verified by review here, explicitly not executed on Windows.
- **AOT discipline:** every "works" probe compiles warning-free under `IsAotCompatible`. Every "limit.*" probe isolates the unsafe call behind `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` + scoped `#pragma warning disable`, catches the failure, and returns the verdict.

---

## File map

```
src/KestrelBackend/
  KestrelBackend.csproj                 # MODIFY: add deps, IsAotCompatible, InternalsVisibleTo, config-binding gen
  ApiJsonContext.cs                     # MODIFY: add every DTO
  ServerComposition.cs                  # CREATE: DI container, module registration, host factory
  Native/
    NativeBootstrap.cs                  # MODIFY: add kestrel_last_error/kestrel_info
    NativeErrorBuffer.cs                # CREATE: last-error capture
  Hosting/
    IBackendHost.cs                     # MODIFY: add BoundPort
    RawHttpHost.cs                      # MODIFY: full parse + pipeline + router; ephemeral port
    KestrelHost.cs                      # MODIFY (#if USE_KESTREL): mirror pipeline/routes
    HostFactory.cs                      # unchanged
    Http/
      HttpRequest.cs HttpResponse.cs HttpStatus.cs   # CREATE
      Router.cs RouteTable.cs                        # CREATE
      RequestPipeline.cs                             # CREATE
      Middleware/CorrelationIdMiddleware.cs RequestLoggingMiddleware.cs ExceptionMiddleware.cs  # CREATE
  Diagnostics/
    RingBufferLoggerProvider.cs RingBufferSink.cs    # CREATE
    LogMessages.cs                                    # CREATE ([LoggerMessage])
    CorrelationContext.cs ProblemDetails.cs DiagInfo.cs LogEntry.cs  # CREATE
  Capabilities/
    Verdict.cs CapabilityDescriptor.cs CapabilityResult.cs CapabilityCatalog.cs ICapabilityModule.cs  # CREATE
  Features/
    Crypto/CryptoModule.cs Persistence/PersistenceModule.cs Networking/NetworkingModule.cs
    Serialization/SerializationModule.cs Concurrency/ConcurrencyModule.cs Numerics/NumericsModule.cs
    Text/TextModule.cs Compression/CompressionModule.cs Composition/CompositionModule.cs
    Runtime/RuntimeModule.cs Legacy/LegacyModule.cs
    Limits/LimitsModule.cs               # all limit.* probes
tests/KestrelBackend.Tests/
  KestrelBackend.Tests.csproj           # CREATE (net9.0, xUnit)
  HostFixture.cs                        # CREATE: boots host on ephemeral port + HttpClient
  *Tests.cs                             # CREATE: per phase
ios/EmbeddedKestrelApp/                 # Phase 5 (Mac build)
docs/                                   # Phase 6
```

---

# Phase 0 — Foundation

### Task 0.1: Test harness scaffold

**Files:**
- Create: `tests/KestrelBackend.Tests/KestrelBackend.Tests.csproj`
- Create: `tests/KestrelBackend.Tests/HostFixture.cs`
- Modify: `src/KestrelBackend/KestrelBackend.csproj` (add `InternalsVisibleTo`)
- Modify: `EmbeddedKestrel.slnx` (add test project)

- [ ] **Step 1:** Create the test csproj.
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\KestrelBackend\KestrelBackend.csproj" />
  </ItemGroup>
</Project>
```
- [ ] **Step 2:** Add to `KestrelBackend.csproj` an item group so tests see internals:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="KestrelBackend.Tests" />
</ItemGroup>
```
- [ ] **Step 3:** Write `HostFixture.cs` — boots the host on an ephemeral port via the internal composition root (defined in 0.6; for now stub against `RawHttpHost` once it exposes `BoundPort`). It exposes a configured `HttpClient`.
```csharp
using System.Net.Http;
using KestrelBackend; // internal via InternalsVisibleTo

namespace KestrelBackend.Tests;

public sealed class HostFixture : IDisposable
{
    public HttpClient Client { get; }
    private readonly IDisposable _host;

    public HostFixture()
    {
        (_host, int port) = TestHost.Start(); // ServerComposition.CreateHost on port 0 (Task 0.6)
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public void Dispose() { Client.Dispose(); _host.Dispose(); }
}
```
- [ ] **Step 4:** `dotnet build tests/KestrelBackend.Tests` — Expected: FAIL (`TestHost` undefined). This proves wiring; `TestHost` lands in 0.6. (Temporary: comment the body until 0.6, or implement `TestHost` minimally now against current `RawHttpHost`.) Implement a minimal `TestHost` now:
```csharp
// src/KestrelBackend/TestHost.cs  (internal helper, no AOT impact)
namespace KestrelBackend;
internal static class TestHost
{
    public static (IDisposable host, int port) Start()
    {
        var host = new Hosting.RawHttpHost();
        host.Start(0);                 // ephemeral (Task 0.2)
        return (host, host.BoundPort); // BoundPort (Task 0.2)
    }
}
```
- [ ] **Step 5:** Commit.
```bash
git add tests/KestrelBackend.Tests src/KestrelBackend/KestrelBackend.csproj src/KestrelBackend/TestHost.cs EmbeddedKestrel.slnx
git commit -m "test: add xUnit harness scaffold for KestrelBackend"
```

### Task 0.2: HTTP primitives + request parser + ephemeral port

**Files:** Create `Hosting/Http/HttpRequest.cs`, `HttpResponse.cs`, `HttpStatus.cs`; Modify `Hosting/IBackendHost.cs` (+`BoundPort`), `Hosting/RawHttpHost.cs`; Test `tests/KestrelBackend.Tests/HttpParseTests.cs`.

- [ ] **Step 1 (test first):**
```csharp
[Fact]
public void ParsesRequestLineHeadersAndBody()
{
    var raw = "POST /api/x?q=1 HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\n\r\nabc";
    var req = HttpRequest.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
    Assert.Equal("POST", req.Method);
    Assert.Equal("/api/x", req.Path);
    Assert.Equal("1", req.Query["q"]);
    Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(req.Body));
}
```
- [ ] **Step 2:** Run `dotnet test --filter HttpParseTests` → FAIL.
- [ ] **Step 3:** Implement `HttpStatus` (consts), `HttpResponse { int Status; Dictionary<string,string> Headers; byte[] Body; }` with `Json`/`Text`/`Problem` factories, and `HttpRequest { Method, Path, IReadOnlyDictionary<string,string> Query, Headers, byte[] Body }` with `static HttpRequest Parse(ReadOnlySpan<byte>)` (split head on `\r\n\r\n`, parse request line, headers, read `Content-Length` bytes). All reflection-free.
- [ ] **Step 4:** Modify `IBackendHost`: add `int BoundPort { get; }`. Modify `RawHttpHost`: support `Start(0)` → after `_listener.Start()` set `BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port`. (Routing stays as-is until 0.6.)
- [ ] **Step 5:** Run `dotnet test --filter HttpParseTests` → PASS. Commit `feat: add HTTP request/response primitives and parser`.

### Task 0.3: Router

**Files:** Create `Hosting/Http/Router.cs`, `RouteTable.cs`; Test `RouterTests.cs`.

- [ ] **Step 1 (test):** map `GET /api/notes/{id}` → handler; assert `/api/notes/7` matches with `id=7`, `/api/notes` does not.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement `Router` holding `(method, segments[], Func<HttpRequest,RouteValues,CancellationToken,Task<HttpResponse>>)`. Segment match with `{param}` capture; `RouteValues` is a small dict. `Map(method, template, handler)` + `TryMatch`.
- [ ] **Step 4:** PASS. Commit `feat: add path-template router`.

### Task 0.4: ProblemDetails + correlation + middleware pipeline

**Files:** Create `Diagnostics/ProblemDetails.cs`, `CorrelationContext.cs`, `Hosting/Http/RequestPipeline.cs`, `Middleware/{CorrelationId,RequestLogging,Exception}Middleware.cs`; Modify `ApiJsonContext.cs`; Test `PipelineTests.cs`.

- [ ] **Step 1 (test):** a handler that throws → pipeline returns 500 with `application/problem+json`, body has `correlationId` non-empty, and response carries `X-Correlation-Id`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement:
  - `ProblemDetails { string Type="about:blank"; string Title; int Status; string? Detail; string CorrelationId; string? Instance; }`.
  - `CorrelationContext` — `AsyncLocal<string?> Current`.
  - Middleware delegate shape: `delegate Task<HttpResponse> RequestDelegate(HttpRequest, CancellationToken);` and `interface IMiddleware { Task<HttpResponse> Invoke(HttpRequest req, RequestDelegate next, CancellationToken ct); }`.
  - `RequestPipeline` composes `[CorrelationId, RequestLogging, Exception]` around the router's terminal handler.
  - `ExceptionMiddleware` catches all, captures into `NativeErrorBuffer` (Task 4), logs, returns ProblemDetails.
- [ ] **Step 4:** Register `ProblemDetails` in `ApiJsonContext`. PASS. Commit `feat: add request pipeline, correlation IDs, RFC7807 problem details`.

### Task 0.5: Structured logging ring buffer

**Files:** Create `Diagnostics/RingBufferSink.cs`, `RingBufferLoggerProvider.cs`, `LogMessages.cs`, `LogEntry.cs`; Modify `ApiJsonContext.cs`; Test `LoggingTests.cs`.

- [ ] **Step 1 (test):** emit 3 logs via the provider; `RingBufferSink.Snapshot()` returns 3 `LogEntry` with level/category/message/correlationId; capacity overflow drops oldest.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement `RingBufferSink` (lock + `LogEntry[]` capacity 500, monotonic `seq`), `RingBufferLoggerProvider : ILoggerProvider` + `ILogger` (reads `CorrelationContext.Current`, formats via the passed formatter). `LogEntry { long Seq; string TimestampUtc; string Level; string Category; int EventId; string Message; string? CorrelationId; }`. `LogMessages` = `static partial class` with `[LoggerMessage(Level=..., Message="...")]` partial methods (request start/stop, probe run, errors).
- [ ] **Step 4:** Register `LogEntry`. PASS. Commit `feat: add in-memory ring-buffer logging with source-gen messages`.

### Task 0.6: DI composition root + rewire RawHttpHost + TestHost

**Files:** Create `ServerComposition.cs`; Modify `Hosting/RawHttpHost.cs`, `src/KestrelBackend/TestHost.cs`, `KestrelBackend.csproj` (add `Microsoft.Extensions.{Logging,DependencyInjection,Configuration,Options}` + `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`); Test `CompositionTests.cs`.

- [ ] **Step 1 (test):** `ServerComposition.CreateHost(port:0)` returns a running host whose `BoundPort>0`; `GET /health` via `HttpClient` returns `ok`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** `ServerComposition`:
  - Build `ServiceCollection`; add logging (`AddLogging(b => b.AddProvider(ringProvider))`), the `RingBufferSink` singleton, `Router`, `RequestPipeline`, `CapabilityCatalog` (Phase 1), and each `ICapabilityModule`.
  - `CreateHost(int port)` → builds provider, constructs `RawHttpHost(provider, pipeline)`, `Start(port)`, returns `(IDisposable, port)`.
  - `RawHttpHost` now delegates each accepted connection to `RequestPipeline.Process(request)`.
  - Update `TestHost.Start()` → `ServerComposition.CreateHost(0)`.
- [ ] **Step 4:** PASS (`/health`). Commit `feat: add DI composition root and rewire host onto pipeline`.

### Task 0.7: Diagnostics endpoints + legacy endpoint on the new pipeline

**Files:** Create `Diagnostics/DiagInfo.cs`; Modify `ServerComposition.cs` (route registration), `ApiJsonContext.cs`; Test `DiagTests.cs`.

- [ ] **Step 1 (test):** `GET /api/diag/info` → 200 JSON with `dotnetVersion`, `hostType="RawHttpHost"`, `uptimeSeconds>=0`, `requestsServed>=1`; `GET /api/diag/logs` → array incl. the request-logging entries; `GET /api/legacy/process?input=hello` → SHA-256 of hello (back-compat with the original `/api/process`; keep both paths).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement `DiagInfo` + handlers; register routes; keep `/api/process` aliased to `/api/legacy/process`. Register `DiagInfo`.
- [ ] **Step 4:** PASS. Commit `feat: add diagnostics endpoints; port legacy processor onto pipeline`.

### Task 0.8: Turn on AOT analyzers

**Files:** Modify `KestrelBackend.csproj`.

- [ ] **Step 1:** Add `<IsAotCompatible>true</IsAotCompatible>`.
- [ ] **Step 2:** `dotnet build src/KestrelBackend` — Expected: clean (0 IL2xxx/IL3xxx). Fix any warning by annotation or source-gen.
- [ ] **Step 3:** `dotnet test` full suite green. Commit `build: enable IsAotCompatible analyzers on KestrelBackend`.

---

# Phase 1 — Capability framework

### Task 1.1: Catalog model + registry

**Files:** Create `Capabilities/Verdict.cs`, `CapabilityDescriptor.cs`, `CapabilityResult.cs`, `ICapabilityModule.cs`, `CapabilityCatalog.cs`; Modify `ApiJsonContext.cs`; Test `CatalogTests.cs`.

- [ ] **Step 1 (test):** register a fake module exposing one descriptor `test.ok` + probe returning `Works`; `catalog.Descriptors` contains it; `await catalog.RunAsync("test.ok")` → `CapabilityResult{ Verdict=Works, ElapsedMs>=0 }`; unknown id → result with `Fails` + ProblemDetails.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement:
```csharp
public enum Verdict { Works, Limited, Fails }
public sealed record CapabilityDescriptor(string Id, string Category, string Title, string Summary, Verdict Expected, string Mechanism);
public sealed class CapabilityResult { /* Id, Category, Title, Verdict, Detail, JsonElement? Output, double ElapsedMs, string? CorrelationId, ProblemDetails? Error */ }
public interface ICapabilityModule {
    IEnumerable<CapabilityDescriptor> Describe();
    Task<CapabilityResult> RunAsync(string id, CancellationToken ct);
    void MapRoutes(Router router); // interactive/playground endpoints
}
public sealed class CapabilityCatalog { /* aggregates modules; Descriptors; RunAsync(id); RunAllAsync() */ }
```
Catalog times each probe with `Stamp`/`Stopwatch`, attaches `CorrelationContext.Current`, converts thrown exceptions into `Fails` + ProblemDetails.
- [ ] **Step 4:** Register `CapabilityDescriptor`, `CapabilityResult`, `Verdict`. PASS. Commit `feat: add capability catalog framework`.

### Task 1.2: Capability endpoints

**Files:** Modify `ServerComposition.cs`; Test `CapabilityEndpointTests.cs`.

- [ ] **Step 1 (test):** `GET /api/capabilities` → array of descriptors; `POST /api/capabilities/test.ok/run` → `Works`; `POST /api/capabilities/run-all` → array.
- [ ] **Step 2:** FAIL. **Step 3:** wire routes to catalog. **Step 4:** PASS. Commit `feat: expose capability catalog over HTTP`.

---

# Phase 2 — Advantage modules

Each module implements `ICapabilityModule`. **Worked exemplar = `CryptoModule`** (Task 2.1) shows the full pattern; subsequent modules follow it with the inputs/APIs in the instance table. Each module task: create module + register in `ServerComposition` + add DTOs to `ApiJsonContext` + a test asserting each probe's verdict and (where interactive) its endpoint.

### Task 2.1: CryptoModule (exemplar — full detail)

**Files:** Create `Features/Crypto/CryptoModule.cs`, `CryptoResult.cs`; Modify `ServerComposition.cs`, `ApiJsonContext.cs`; Test `CryptoTests.cs`.

- [ ] **Step 1 (test):**
```csharp
[Fact] public async Task Sha256_Works() {
    var r = await _fx.Client.PostAsync("/api/capabilities/crypto.sha/run", null);
    var cap = await r.Content.ReadFromJsonAsync<CapabilityResult>(...);
    Assert.Equal(Verdict.Works, cap!.Verdict);
}
[Fact] public async Task Crypto_Endpoint_Hashes() {
    var r = await _fx.Client.PostAsJsonAsync("/api/crypto/sha256", new { input = "hello" });
    var c = await r.Content.ReadFromJsonAsync<CryptoResult>(...);
    Assert.Equal("2cf24dba...9824", c!.Output); // canonical sha256("hello")
}
```
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement descriptors `crypto.sha|hmac|aesgcm|rsa|pbkdf2` (all `Expected=Works`), probe bodies using `SHA256/512`, `HMACSHA256`, `AesGcm`, `RSA`, `Rfc2898DeriveBytes`; each returns `CapabilityResult` with a small `Output` (hex/base64) and a `Detail` ("CryptoKit/Security-backed; reflection-free; AOT-safe"). `MapRoutes` adds `POST /api/crypto/{algo}` → `CryptoResult{Algo,Input,Output,Detail}`.
- [ ] **Step 4:** Register `CryptoResult`; add module to composition. `dotnet test --filter Crypto` PASS; `dotnet build` analyzer-clean. Commit `feat: add crypto capability module`.

### Tasks 2.2–2.11: remaining advantage modules (follow the 2.1 pattern)

For each: create `<Name>Module.cs` (+ result DTO), register, add DTOs to `ApiJsonContext`, test each probe's verdict (+ endpoint where interactive), keep build analyzer-clean, commit `feat: add <name> capability module`.

| Task | Module | Probe ids (Expected=Works) | Key APIs / notes | Interactive endpoint |
|---|---|---|---|---|
| 2.2 | Serialization | `json.sourcegen`, `json.polymorphic`, `text.base64` | STJ source-gen ctx, `[JsonDerivedType]`, `Convert.ToBase64String` | `POST /api/serialize` |
| 2.3 | Persistence | `persist.sqlite`, `persist.jsonfile`, `persist.fileio` | `Microsoft.Data.Sqlite` (open `:memory:`/sandbox file, CREATE/INSERT/SELECT), STJ file store, `File.*` in temp | `GET/POST/DELETE /api/notes` (SQLite) |
| 2.4 | Networking | `net.httpclient`, `net.dns`, `net.router` | `HttpClient` to a deterministic target (configurable; skip offline), `Dns.GetHostEntryAsync`, self-describe router | `POST /api/net/fetch` |
| 2.5 | Concurrency | `concurrency.channels`, `concurrency.parallel`, `concurrency.tasks` | `Channel<T>`, `Parallel.ForEachAsync`, `Task.WhenAll`+`Interlocked` | — |
| 2.6 | Numerics | `numerics.bigint`, `numerics.genericmath`, `numerics.simd` | `BigInteger`, `INumber<T>` generic sum, `Vector<T>` | — |
| 2.7 | Text | `text.regex` | `[GeneratedRegex]` partial method | `POST /api/regex` |
| 2.8 | Compression | `compress.gzip`, `compress.brotli` | `GZipStream`/`DeflateStream`, `BrotliStream`; report ratio | `POST /api/compress` |
| 2.9 | Composition | `compose.di`, `compose.config`, `compose.logging` | resolve a graph from the provider; bind options via **source-gen** binder; emit+read ring log | — |
| 2.10 | Runtime | `runtime.info`, `runtime.time` | `RuntimeInformation`, `GC.GetGCMemoryInfo`, `DateTimeOffset`, `Stopwatch` | — |
| 2.11 | Legacy | `legacy.process` | `LegacyLib.DataProcessor` (the thesis) | already `/api/legacy/process` |

> **Persistence contingency:** if `Microsoft.Data.Sqlite` raises trim/AOT warnings under `IsAotCompatible`, keep `persist.sqlite` `Expected=Limited` with the warning text in `Detail`, and make `/api/notes` use the JSON-file store. Record the outcome in the README (Phase 6).

---

# Phase 3 — Limitation modules

### Task 3.1: LimitsModule (all `limit.*` probes)

**Files:** Create `Features/Limits/LimitsModule.cs`; Modify `ServerComposition.cs`; Test `LimitsTests.cs`.

- [ ] **Step 1 (test):** each `limit.*` probe returns its **expected** verdict (`Fails`/`Limited`) AND a non-empty `Detail` explaining why; none throws uncaught.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement probes; isolate unsafe calls behind annotations + scoped `#pragma warning disable`, catch and report:
  - `limit.expressioncompile` — `[RequiresDynamicCode]`; call `Expression.Lambda(...).Compile()`, catch/observe `PlatformNotSupported`/interpreter fallback → `Fails`.
  - `limit.reflectionemit` — `[RequiresDynamicCode]`; `new DynamicMethod(...)` → `Fails`.
  - `limit.assemblyload` — `Assembly.LoadFile` of a bogus path → `Fails` (no JIT for new managed code).
  - `limit.process` — `Process.Start` → `Fails` (sandbox/unsupported).
  - `limit.reflectiontrim` — `[RequiresUnreferencedCode]`; reflect a member known to be trimmed → `Limited`.
  - `limit.newtonsoft` — **no package**; describe + (optionally) STJ reflection path with `[RequiresUnreferencedCode]` to show the warning → `Limited`.
  - `limit.globalization` — compare `"i".ToUpper(tr-TR)` expectation vs invariant → `Limited`.
  - `limit.stacktrace` — throw/catch, show `Exception.StackTrace` is null/empty → `Limited` (ties to the §8 design).
  - `limit.eventsource` — describe `EventSourceSupport=false` → `Limited`.
  - `limit.efcore` — **no package**; descriptor-only explainer → `Limited`.
  - `limit.kestrel`, `limit.grpc` — descriptor-only explainers (NETSDK1082) → `Fails`.
- [ ] **Step 4:** `dotnet build` analyzer-clean (annotations absorb the expected warnings); `dotnet test --filter Limits` PASS. Commit `feat: add limitation probes with graceful failure demos`.

---

# Phase 4 — Native ABI expansion

### Task 4.1: NativeErrorBuffer + new exports + header

**Files:** Create `Native/NativeErrorBuffer.cs`; Modify `Native/NativeBootstrap.cs`, `Diagnostics/DiagInfo.cs` (snapshot source), `ios/EmbeddedKestrelApp/KestrelBackend.h`; Test `NativeErrorTests.cs`.

- [ ] **Step 1 (test):** `NativeErrorBuffer.Capture("T","msg","cid")` then `CopyInto(span)` writes `"T|msg|cid"` UTF-8 and returns its byte length; oversize span → returns negative needed length.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implement `NativeErrorBuffer` (volatile last record, `CopyInto(Span<byte>)`). Add to `NativeBootstrap`:
```csharp
[UnmanagedCallersOnly(EntryPoint = "kestrel_last_error")]
public static unsafe int LastError(byte* buf, int len) { try { return NativeErrorBuffer.CopyInto(new Span<byte>(buf, len)); } catch { return -1; } }

[UnmanagedCallersOnly(EntryPoint = "kestrel_info")]
public static unsafe int Info(byte* buf, int len) { try { return DiagInfo.CopySnapshotInto(new Span<byte>(buf, len)); } catch { return -1; } }
```
  `ExceptionMiddleware` calls `NativeErrorBuffer.Capture(...)`. Update `KestrelBackend.h` with the two decls.
- [ ] **Step 4:** PASS. Commit `feat: add kestrel_last_error/kestrel_info native exports`.

---

# Phase 5 — Swift app (Mac build; verified by review here)

> No Windows execution. Each task = create/modify Swift files; "verify" = compiles on Mac via `xcodegen` + `xcodebuild` (Phase 6 scripts). Keep `ServerController`'s existing contract; extend it.

| Task | Files | Responsibility |
|---|---|---|
| 5.1 | `Models/*.swift` | Codable mirrors: `CapabilityDescriptor`, `CapabilityResult`, `Verdict`, `ProblemDetails`, `DiagInfo`, `LogEntry`, per-feature DTOs |
| 5.2 | `Services/ServerController.swift` (modify), `ApiClient.swift` (create) | lifecycle + typed per-endpoint client, 1× connection-refused retry, ProblemDetails error mapping, `lastNativeError()` via `kestrel_last_error`, `nativeInfo()` via `kestrel_info` |
| 5.3 | `Views/Dashboard/*` + `Components/StatusCard.swift` | status card from `/api/diag/info`+`kestrel_info`; health dot |
| 5.4 | `Views/Capabilities/*` + `Components/VerdictBadge.swift`, `JSONView.swift` | data-driven matrix + detail (run probe, raw JSON, verdict, timing, prose); "Run all" |
| 5.5 | `Views/Playgrounds/*` | Data Processor, Crypto, Persistence (Notes CRUD), HTTP Explorer, Outbound Fetch, Compression, Regex |
| 5.6 | `Views/Limitations/*` | list + graceful failure demos |
| 5.7 | `Views/Diagnostics/*` + `Components/LogRow.swift`, `ErrorBanner.swift` | live log viewer (poll `/api/diag/logs`), level filter, native error view |
| 5.8 | `Views/About/*` | architecture, thesis, AOT switches, findings |
| 5.9 | `EmbeddedKestrelApp.swift`, `ContentView.swift` (modify) | `TabView` host; keep launch boot |

Each Swift task commits `feat(ios): <screen>`.

---

# Phase 6 — Build wiring & docs

| Task | Files | Action |
|---|---|---|
| 6.1 | `ios/project.yml` | confirm `sources` globs new folders (already path-based — verify); bump nothing else |
| 6.2 | `build/publish-ios.sh` | header note that exports are now 4; no functional change (publish still default dylib) |
| 6.3 | `bridging header`, `KestrelBackend.h` | ensure 4 decls in lockstep (done in 4.1; re-verify) |
| 6.4 | `README.md` | new "Capability Explorer" section: matrix summary, the SQLite outcome, the ABI 2→4 note |
| 6.5 | `Obsidian Vault/claude-dev-projects/...` | per workspace CLAUDE.md: note PR#, SHA, date, reusable lessons (NOT duplicating README) |
| 6.6 | — | open PR (squash) per workspace rule; do not merge to main directly |

---

## Self-Review

**Spec coverage:** every spec §7 probe → a Phase 2/3 task; §8 logging/errors → Tasks 0.4/0.5/4.1; §9 ABI → 4.1; §11 screens → Phase 5 (all six tabs); §12 endpoints → 0.6/0.7/1.2/2.x; §13 verification → harness (0.1) + analyzers (0.8); §6 HTTP layer → 0.2/0.3/0.4. No gaps.

**Placeholder scan:** repetitive probe modules use the 2.1 exemplar + an instance table with concrete ids/APIs/endpoints (not "similar to") — acceptable because the executor is inline and reads in order. No "TBD/handle edge cases". Persistence contingency is an explicit branch, not a placeholder.

**Type consistency:** `Verdict`, `CapabilityDescriptor`, `CapabilityResult`, `ICapabilityModule`, `RequestDelegate`, `HttpRequest/Response`, `BoundPort`, `NativeErrorBuffer.CopyInto`, `kestrel_last_error/kestrel_info` are named identically across tasks and match the spec. `TestHost.Start()` signature is consistent between 0.1 and 0.6.
