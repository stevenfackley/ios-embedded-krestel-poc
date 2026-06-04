# Embedded .NET Capability Explorer — Design

- **Date:** 2026-06-03
- **Status:** Approved (verbal), pre-implementation
- **Branch:** `feat/dotnet-capability-explorer`
- **Supersedes scope of:** the single-endpoint PoC documented in `README.md`

---

## 1. Context & thesis

The existing PoC proves one thing: a legacy `netstandard2.0` library can be reached from a
native iOS app, with no rewrite, through an in-process loopback HTTP server compiled with
NativeAOT to a self-contained `.dylib` (wrapped `.framework`, Embed & Sign). The shipped
host is the zero-dependency `RawHttpHost`; real Kestrel cannot publish for iOS
(NETSDK1082 — no `Microsoft.AspNetCore.App` runtime pack for any `ios-*` RID).

This project **extends that PoC into a capability survey**: a multi-screen app that
exercises the full surface of .NET features reachable through this pattern, labels each as
**Works ✅ / Limited ⚠️ / Fails ❌**, and demonstrates enterprise-grade error handling and
logging within the pattern's hard constraints.

The goal is decision-support: anyone evaluating "should we embed .NET in our iOS app this
way?" can see exactly what they get and what they give up.

## 2. Goals / non-goals

**Goals**
- Maximalist capability matrix: ~25 "works" probes + ~12 "limited/fails" probes, grouped
  by category.
- Each probe is *live* — it runs real .NET code and returns a structured verdict + the
  reason it works or fails under NativeAOT-on-iOS.
- Enterprise error handling & logging: structured logging (ring buffer, surfaced in-app),
  correlation IDs, RFC 7807 Problem Details, a native-boundary error channel.
- All Swift screens built out (Dashboard, Capabilities matrix, Playgrounds, Limitations,
  Diagnostics/Logs, About).
- Curated AOT-safe dependency stack: `Microsoft.Extensions.{Logging,DependencyInjection,
  Configuration,Options}` + `Microsoft.Data.Sqlite`. No EF Core.
- Verifiable on Windows: trim/AOT analyzers prove each claim at build; an xUnit harness
  runs the host and every endpoint end-to-end.

**Non-goals**
- Bumping the TFM. `KestrelBackend` stays **net9.0** — the README's iOS findings are pinned
  to net9.0 and the 9.x `ios` workload. Do not move to net10.
- Making real Kestrel publish for iOS (impossible today). The `#if USE_KESTREL` seam is
  retained and expanded only to mirror the contract and document the gap.
- Production hardening of the hand-rolled HTTP server beyond what the PoC needs (no TLS,
  HTTP/2, chunked transfer, or keep-alive — see §6).
- Running the iOS/simulator build on Windows (impossible — see §13).

## 3. Hard constraints (the "physics" of this pattern)

These are fixed by the proven `KestrelBackend.csproj` and the platform; the whole design
respects them and several become *limitation probes*:

| Constraint | Source | Consequence |
|---|---|---|
| No JIT (ahead-of-time only) | NativeAOT | No `Expression.Compile`, `Reflection.Emit`, `DynamicMethod`, runtime assembly load |
| Trimming | `PublishAot` | Reflection over un-rooted members fails; reflection-based serialization is unsafe |
| `InvariantGlobalization=true` | csproj | No culture-specific format/collation; ICU dropped |
| `StackTraceSupport=false` | csproj | Exceptions carry no stack trace → error design must be correlation-ID based |
| `EventSourceSupport=false` | csproj | No ETW/EventSource diagnostics |
| No ASP.NET runtime pack for iOS | platform | No Kestrel / gRPC server / SignalR |
| iOS sandbox | platform | No `Process.Start`, restricted file system (app sandbox only) |
| Native boundary | interop | Only blittable args; no managed exception may unwind into native code |
| Mac-only build | toolchain | NativeAOT-for-iOS link, xcframework, simulator/device run cannot happen on Windows |

## 4. Architecture

```
NativeBootstrap  ── C ABI: kestrel_start / kestrel_stop / kestrel_last_error / kestrel_info
   └─ ServerComposition (DI container + logging + configuration)
        └─ RawHttpHost  ── expanded HTTP/1.1 (verbs, headers, body)
             └─ RequestPipeline (middleware): correlation-id → request logging → exception → ProblemDetails
                  └─ Router → Endpoints
                       └─ Feature modules (Crypto, Persistence, Networking, Serialization,
                          Concurrency, Numerics, Text, Compression, Globalization, DynamicCode,
                          Reflection, Runtime, Hosting-limits, Legacy)
                            └─ CapabilityCatalog (registry: descriptor + probe delegate)
   └─ LegacyLib (ns2.0, untouched) ◀── reuse thesis
```

### Project layout

```
src/
  LegacyLib/                       # unchanged ns2.0 anchor (+ maybe 1-2 more reused types)
  KestrelBackend/                  # net9.0, PublishAot, IsAotCompatible=true
    Native/
      NativeBootstrap.cs           # C ABI (start/stop/last_error/info)
      NativeErrorBuffer.cs         # last-error capture for kestrel_last_error
    Hosting/
      IBackendHost.cs
      RawHttpHost.cs               # expanded: full request parse + pipeline + router
      KestrelHost.cs               # #if USE_KESTREL — mirrors the same pipeline/routes
      HostFactory.cs
      Http/
        HttpRequest.cs  HttpResponse.cs  HttpStatus.cs
        Router.cs                  # method + path-template matching
        RequestPipeline.cs         # ordered middleware
        Middleware/                # CorrelationIdMiddleware, RequestLoggingMiddleware, ExceptionMiddleware
    Diagnostics/
      RingBufferLoggerProvider.cs  # in-memory, thread-safe, last N entries
      LogMessages.cs               # source-gen [LoggerMessage] partial methods
      CorrelationContext.cs
      ProblemDetails.cs            # RFC 7807
    Capabilities/
      Verdict.cs                   # Works | Limited | Fails
      CapabilityDescriptor.cs      # id, category, title, summary, expectedVerdict, mechanism
      CapabilityResult.cs          # verdict, detail, output, elapsedMs, correlationId, error?
      CapabilityCatalog.cs         # registry; aggregates module registrations
    Features/
      Crypto/  Persistence/  Networking/  Serialization/  Concurrency/  Numerics/
      Text/  Compression/  Globalization/  DynamicCode/  Reflection/  Runtime/
      HostingLimits/  Legacy/
    ApiJsonContext.cs              # [JsonSerializable] for every DTO
    ServerComposition.cs           # builds DI container, registers modules, logging, config
tests/
  KestrelBackend.Tests/            # net9.0, xUnit — boots host on ephemeral port, hits every endpoint
ios/EmbeddedKestrelApp/            # expanded SwiftUI app (see §11)
```

## 5. Curated AOT-safe dependencies

| Package | Why | AOT note |
|---|---|---|
| `Microsoft.Extensions.Logging` (+ Abstractions) | structured logging core | AOT-safe with source-gen `[LoggerMessage]`; custom provider |
| `Microsoft.Extensions.DependencyInjection` | composition root | AOT-safe (constructor injection; no assembly scanning) |
| `Microsoft.Extensions.Configuration` (+ Binder source generator) | config probe | use **source-gen binder**; reflection binder warns (becomes a caveat) |
| `Microsoft.Extensions.Options` | options pattern | AOT-safe |
| `Microsoft.Data.Sqlite` (+ `SQLitePCLRaw.bundle_e_sqlite3`) | real persistence | AOT-compatible; ships its own native sqlite — validate no trim warnings |

If `Microsoft.Data.Sqlite` produces unexpected trim/AOT analyzer warnings, fall back to a
hand-rolled JSON-file store for the *shipped* persistence path and demote SQLite to a
"works with bundled native lib" caveat. (Expected to pass; this is the contingency.)

## 6. HTTP layer (expanded `RawHttpHost`)

The current host parses only the request line and serves bodyless GETs. It grows into a
small but real HTTP/1.1 server:

- **Parse:** request line + headers + body (`Content-Length` only; **no** chunked).
- **Model:** `HttpRequest { Method, Path, Query, Headers, Body }`,
  `HttpResponse { Status, Headers, Body }`.
- **Router:** method + path-template matching (`/api/{category}/{action}`, `/api/notes/{id}`).
- **Pipeline (ordered middleware):**
  1. `CorrelationIdMiddleware` — read `X-Correlation-Id` or generate; push logging scope.
  2. `RequestLoggingMiddleware` — structured start/stop with elapsed ms + status.
  3. `ExceptionMiddleware` — catch all → `ProblemDetails` + 4xx/5xx + log with correlation id.
- **Connections:** one request per connection, `Connection: close` (robust, simplest).
  Keep-alive/HTTP-2/TLS are explicitly **out of scope** and themselves become a documented
  limitation ("you hand-roll a router but you don't get Kestrel's transport features").

This expanded host is itself the "Networking → hand-rolled router" advantage probe.

## 7. Capability catalog (the maximalist matrix)

The catalog is a **registry**: each feature module registers `CapabilityDescriptor`s with a
probe delegate. `GET /api/capabilities` returns the descriptors (so the Swift matrix is
data-driven), and `POST /api/capabilities/{id}/run` executes one probe.

### Works ✅ (advantages)

| id | Category | Demonstrates | Mechanism |
|---|---|---|---|
| `crypto.sha` | Crypto | SHA-256/512 hashing | `System.Security.Cryptography.SHA*` |
| `crypto.hmac` | Crypto | HMAC-SHA256 | `HMACSHA256` |
| `crypto.aesgcm` | Crypto | authenticated encrypt/decrypt | `AesGcm` |
| `crypto.rsa` | Crypto | sign/verify | `RSA` |
| `crypto.pbkdf2` | Crypto | key derivation | `Rfc2898DeriveBytes` |
| `json.sourcegen` | Serialization | reflection-free (de)serialize | STJ source-gen context |
| `json.polymorphic` | Serialization | polymorphic + collections | STJ source-gen w/ `[JsonDerivedType]` |
| `text.base64` | Serialization | Base64 + UTF-8 transcode | `Convert`, `Encoding` |
| `persist.sqlite` | Persistence | CRUD over embedded SQLite | `Microsoft.Data.Sqlite` |
| `persist.jsonfile` | Persistence | file-backed store | sandbox file I/O + STJ |
| `persist.fileio` | Persistence | read/write app sandbox | `System.IO.File` |
| `net.httpclient` | Networking | outbound GET/POST | `HttpClient` |
| `net.dns` | Networking | hostname resolve | `Dns.GetHostEntryAsync` |
| `net.router` | Networking | hand-rolled multi-verb router | RawHttpHost pipeline |
| `concurrency.channels` | Concurrency | producer/consumer | `System.Threading.Channels` |
| `concurrency.parallel` | Concurrency | data parallelism | `Parallel.ForEachAsync` |
| `concurrency.tasks` | Concurrency | fan-out/in | `Task.WhenAll`, `Interlocked` |
| `numerics.bigint` | Numerics | arbitrary precision | `BigInteger` |
| `numerics.genericmath` | Numerics | `INumber<T>` generic algorithm | generic math |
| `numerics.simd` | Numerics | vectorized sum | `Vector<T>` |
| `text.regex` | Text | source-gen regex | `[GeneratedRegex]` |
| `compress.gzip` | Compression | Gzip/Deflate round-trip + ratio | `System.IO.Compression` (libz) |
| `compress.brotli` | Compression | Brotli round-trip | `BrotliStream` |
| `compose.di` | Composition | constructor injection graph | `Microsoft.Extensions.DependencyInjection` |
| `compose.config` | Composition | bind options (source-gen) | `Microsoft.Extensions.Configuration` binder gen |
| `compose.logging` | Composition | structured log emit + read back | `[LoggerMessage]` + ring buffer |
| `runtime.info` | Runtime | runtime/GC/memory snapshot | `RuntimeInformation`, `GC.GetGCMemoryInfo` |
| `runtime.time` | Runtime | ISO-8601 round-trip, Stopwatch | `DateTimeOffset`, `Stopwatch` |
| `legacy.process` | Legacy | **the thesis** — ns2.0 reuse | `LegacyLib.DataProcessor` |

### Limited ⚠️ / Fails ❌ (limitations)

Each runs and **gracefully demonstrates** the failure (catches it, returns the verdict +
the reason). Known-unsafe calls are isolated behind `[RequiresDynamicCode]` /
`[RequiresUnreferencedCode]` + scoped `#pragma warning disable` so the build stays clean —
the analyzer annotation *is* the documented finding.

| id | Verdict | Demonstrates | Why |
|---|---|---|---|
| `limit.kestrel` | ❌ | ASP.NET Kestrel hosting | NETSDK1082, no iOS runtime pack (in-app explainer) |
| `limit.grpc` | ❌ | gRPC server / SignalR | same ASP.NET root cause |
| `limit.expressioncompile` | ❌ | `Expression.Compile()` | no JIT → throws/interprets under AOT |
| `limit.reflectionemit` | ❌ | `Reflection.Emit` / `DynamicMethod` | no runtime codegen |
| `limit.assemblyload` | ❌ | `Assembly.LoadFile` / `AssemblyLoadContext` | can't load new managed code (no JIT) |
| `limit.process` | ❌ | `Process.Start` | iOS sandbox |
| `limit.reflectiontrim` | ⚠️ | reflect over un-rooted member | trimmed away → null/throws |
| `limit.newtonsoft` | ⚠️ | reflection-based JSON | works but trim-unsafe (warns); contrast with source-gen |
| `limit.globalization` | ⚠️ | `tr-TR` casing / culture format | InvariantGlobalization → culture absent |
| `limit.stacktrace` | ⚠️ | exception with no trace | `StackTraceSupport=false` (drives §8) |
| `limit.eventsource` | ⚠️ | EventSource emit | `EventSourceSupport=false` |
| `limit.efcore` | ⚠️ | EF Core model | reflection/dynamic-heavy; not on this path → use `Microsoft.Data.Sqlite` |

## 8. Enterprise error handling & logging

- **Logging:** source-gen `[LoggerMessage]` partial methods (allocation-free, AOT-safe).
  A custom `RingBufferLoggerProvider` retains the last ~500 structured entries in a
  thread-safe ring; exposed at `GET /api/diag/logs?level=&sinceSeq=`.
  Entry: `{ seq, timestampUtc, level, category, eventId, message, correlationId, state[] }`.
- **Correlation IDs:** `CorrelationIdMiddleware` reads `X-Correlation-Id` or generates a
  `Guid` (AOT-safe); added to a logging scope, echoed in the response header and in every
  Problem Details body.
- **Problem Details (RFC 7807):** `ProblemDetails { type, title, status, detail,
  correlationId, instance }`; every error response uses it, serialized via source-gen.
- **Native error channel:** because exceptions can't cross the C ABI and there are no stack
  traces, `NativeErrorBuffer` captures the most recent boundary error
  (`type | message | correlationId`); `kestrel_last_error` copies it out to Swift. This is
  the interop error-handling demonstration.
- **Swift:** typed `ProblemDetails` decode, a global `ErrorPresenter`, one connection-refused
  retry (rides out the launch-QoS bind window), and an error inspector backed by
  `kestrel_last_error`.

## 9. Native ABI (expanded: 2 → 4 functions)

All blittable; underscore-prefixed on Apple (`_kestrel_*`). No managed exception escapes.

```c
int  kestrel_start(int port);                       // 0 ok, -1 fail; non-blocking, idempotent (unchanged)
void kestrel_stop(void);                            // unchanged
int  kestrel_last_error(uint8_t* buf, int len);     // copies UTF-8 "type|message|correlationId";
                                                    //   returns bytes written; 0 if none; -need if buf too small
int  kestrel_info(uint8_t* buf, int len);           // copies UTF-8 JSON runtime/server snapshot; same return rules
```

`kestrel_last_error` / `kestrel_info` are **read-only** and additive. This is the single
deviation from the PoC's "byte-for-byte identical contract"; accepted for enterprise error
handling. The header `KestrelBackend.h` and the bridging header are updated in lockstep.

## 10. Wire DTOs (all registered in `ApiJsonContext`, mirrored as Swift `Codable`)

`ProcessResult` (existing), `CapabilityDescriptor`, `CapabilityResult`, `Verdict` (string
enum), `ProblemDetails`, `DiagInfo`, `LogEntry`, plus per-feature payloads (`CryptoResult`,
`NoteRecord`, `FetchResult`, `CompressResult`, `RegexResult`, `SerializeResult`, …).
camelCase naming (matches the existing convention and the Swift decoders).

## 11. Swift app — all screens

`TabView` of `NavigationStack`s.

1. **Dashboard** — status card (host=RawHttpHost, port, .NET version, uptime, requests
   served, memory/GC, ring-buffer count), health dot. Source: `/api/diag/info` + `kestrel_info`.
2. **Capabilities** — data-driven matrix grouped by category; each row a verdict badge
   (✅/⚠️/❌) + chevron. Tap → **CapabilityDetail** (Run probe → request, raw JSON, verdict,
   timing, prose "why under AOT-iOS"). "Run all" updates every badge live.
3. **Playgrounds** — interactive: Data Processor (original, enhanced), Crypto, Persistence
   (Notes CRUD), HTTP Explorer (arbitrary verb/path/body), Outbound Fetch, Compression, Regex.
4. **Limitations** — explicit list; each probe demonstrates the failure gracefully with the
   explanation.
5. **Diagnostics / Logs** — live log viewer (poll `/api/diag/logs`), level filter,
   correlation grouping, copy; + native error view (`kestrel_last_error`).
6. **About** — architecture diagram, thesis, AOT switches, build constraints, headline findings.

Swift structure: `Models/` (Codable DTO mirrors), `Services/` (`ServerController` expanded:
lifecycle + typed per-endpoint client + retry + error mapping), `Views/<tab>/`,
`Components/` (`VerdictBadge`, `StatusCard`, `JSONView`, `LogRow`, `ErrorBanner`).

## 12. Endpoint map

```
GET  /health                              liveness ("ok")
GET  /api/diag/info                       DiagInfo
GET  /api/diag/logs?level=&sinceSeq=      LogEntry[]
GET  /api/capabilities                    CapabilityDescriptor[]
POST /api/capabilities/{id}/run           CapabilityResult
POST /api/capabilities/run-all            CapabilityResult[]
GET  /api/legacy/process?input=           ProcessResult        (thesis; back-compat)
POST /api/crypto/{algo}                   CryptoResult
GET  /api/notes  POST /api/notes  DELETE /api/notes/{id}       NoteRecord(s)  (SQLite)
POST /api/net/fetch                       FetchResult          (outbound HttpClient)
POST /api/compress                        CompressResult
POST /api/regex                           RegexResult
POST /api/serialize                       SerializeResult
```

All errors → `ProblemDetails`. All responses carry `X-Correlation-Id`.

## 13. Verification (Windows) & boundaries

**Runs on Windows (proof):**
- `dotnet build` with `IsAotCompatible=true` → trim+AOT+single-file analyzers prove each
  "works" probe is clean and each "limitation" probe carries the documented annotation.
- `dotnet test tests/KestrelBackend.Tests` → boots `RawHttpHost` on an ephemeral port and
  exercises every endpoint end-to-end (logic is identical to iOS; only host/runtime differ).
- Best-effort `dotnet publish -r win-x64 -p:PublishAot=true` if the C++ linker is present —
  the ultimate ILC proof that the whole surface AOT-compiles. If unavailable, analyzers +
  tests stand in.

**Cannot run on Windows (Mac-only, delivered as reviewed source):**
- NativeAOT-for-iOS publish, `install_name_tool`, `xcodebuild -create-xcframework`,
  `xcodegen`, simulator/device run. The Swift app builds on the Mac via the existing
  `build/publish-ios.sh` + `ios/project.yml` flow (updated for the new files).

This boundary is stated explicitly in every status update; "green on Windows" never implies
"runs on device."

## 14. Build sequencing (vertical slices)

Each slice ends green (build + tests pass). Order:

1. **Foundation** — expanded HTTP layer (parse/router/pipeline), DI composition, logging
   ring buffer, ProblemDetails, correlation IDs, `IsAotCompatible`, harness skeleton.
   Port existing `/health` + `/api/legacy/process` onto it (no behavior change).
2. **Capability framework** — `CapabilityCatalog`, descriptors, `/api/capabilities*`.
3. **Advantage slices** — Crypto → Serialization → Persistence → Networking → Concurrency →
   Numerics → Text → Compression → Composition → Runtime → Legacy (each: module + probes +
   tests + ApiJsonContext entries).
4. **Limitation slices** — all `limit.*` probes with isolation annotations + graceful demos.
5. **Native ABI** — `kestrel_last_error`, `kestrel_info`, `NativeErrorBuffer`; header +
   bridging header updates.
6. **Swift** — Models → Services → Dashboard → Capabilities/Detail → Playgrounds →
   Limitations → Diagnostics → About; `project.yml`/`publish-ios.sh` updates.
7. **Docs** — README section update + Obsidian vault note (per workspace CLAUDE.md).

## 15. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Large surface / scope creep | Vertical slices; catalog keeps additions mechanical; tree always green |
| `Microsoft.Data.Sqlite` AOT warnings | Validate via analyzers; JSON-file fallback for shipped path if needed |
| Outbound `HttpClient` test hits real network | Point at a deterministic/local target; mark test skippable offline |
| ABI expansion breaks PoC's "identical contract" property | Additive, read-only; documented in §9; header kept in lockstep |
| Windows ≠ iOS runtime behavior | Analyzers prove AOT-safety platform-independently; tests prove logic; iOS-only build stays Mac-side |
| net9.0 vs installed SDK 10.x / 9.0.311 | net9.0 builds under both; no `global.json` pin added (Mac uses 9.0.314) |

## 16. Out of scope / future

- TLS, HTTP/2, keep-alive, chunked transfer in the hand-rolled host.
- A real ASP.NET path (revisit if/when Microsoft ships an iOS ASP.NET runtime pack).
- CI wiring for the new test project (note for a follow-up; workspace tracks CI separately).
```