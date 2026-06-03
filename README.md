# Embedded ASP.NET Core Kestrel inside a native iOS Swift app (.NET 9)

A production-shaped Proof of Concept proving this flow end-to-end:

```
┌────────────┐   http://127.0.0.1:5001    ┌───────────────────────────┐
│  Swift UI  │ ─────────────────────────▶ │  Embedded server (.NET 9) │
│ (SwiftUI)  │      URLSession GET         │  Kestrel  ⟷  RawHttpHost  │
│            │ ◀───────────────────────── │           │               │
└────────────┘        JSON response        └───────────┼───────────────┘
                                                        ▼
                                          ┌───────────────────────────┐
                                          │ LegacyLib (netstandard2.0)│
                                          │   DataProcessor.Process    │
                                          └───────────────────────────┘
```

**Thesis:** a legacy `netstandard2.0` business library can be surfaced to a native iOS
app, *with no rewrite*, by hosting an in-process HTTP server compiled with NativeAOT and
calling it over the loopback interface.

> **Status: PROVEN on the iOS Simulator (2026-06-02).** A signed-free Simulator build
> does the full round trip — `curl http://127.0.0.1:5001/api/process?input=hello` returns
> the SHA-256 of `hello` computed by the netstandard2.0 `DataProcessor`. See
> [Section 7](#7-results--what-was-actually-proven). Physical-device install is wired and
> waiting only on an Apple ID sign-in (free account is sufficient).

The server sits behind a one-method interface (`IBackendHost`) with **two
implementations** selected at compile time by the `UseKestrel` MSBuild flag:

| Host | When | Dependencies | iOS publish |
|---|---|---|---|
| `KestrelHost` | `-p:UseKestrel=true` | `Microsoft.AspNetCore.App` | ❌ **impossible** — NETSDK1082, no ASP.NET runtime pack for any `ios-*` RID |
| `RawHttpHost` | default / `false` | none (`TcpListener`) | ✅ **the shipped host** — trivially AOT-safe |

The Swift client, the native entry points, `LegacyLib`, and the JSON wire contract are
**byte-for-byte identical** across both hosts. The `#if USE_KESTREL` seam is retained to
document the Kestrel attempt and to flip on the day Microsoft ships an iOS ASP.NET
runtime pack — but on .NET 9 today, **the raw host is the answer, not a fallback.**

---

## Headline findings (validated on a Mac mini, Apple Silicon, .NET 9 SDK 9.0.314)

1. **The supported NativeAOT-for-iOS recipe yields a self-contained `.dylib`, not a
   static `.a`.** Plain `net9.0` TFM + `PublishAot` + `PublishAotUsingRuntimePack` and
   **no `<NativeLib>` element** → `dotnet publish -r ios-arm64` emits
   `KestrelBackend.dylib` with the entire NativeAOT runtime + GC linked in (zero
   undefined `_Rh*` symbols). This is Microsoft's documented
   [*NativeAOT for iOS-like platforms*](https://learn.microsoft.com/dotnet/core/deploying/native-aot/ios-like-platforms)
   path. Wrap the dylib in a `.framework` and **Embed & Sign** it.

2. **Kestrel cannot be published for iOS.** `-p:UseKestrel=true` fails restore with
   **NETSDK1082**: there is no `Microsoft.AspNetCore.App` runtime pack for any `ios-*`
   RID. (A *managed-only* `dotnet build` on a desktop resolves the reference — that is
   not the same as a publishable iOS app, and the earlier draft of this README drew the
   wrong conclusion from it.) Ship `RawHttpHost`.

3. **Never set `<NativeLib>` and never pass `-p:NativeLib` on the CLI.** Three outcomes
   on an Apple RID: *unset* → the good self-contained dylib; `Static` → a `.a` whose
   consumption Microsoft's docs explicitly **don't** cover (you'd hand-link ~8
   order-sensitive runtime/PAL archives); `Shared` (or any CLI `-p:NativeLib`) → routes
   into the Mono `mobile-librarybuilder` toolchain (pulls Mono 8.0.27 packs, dies
   MSB4022 under a 9.0 SDK) and the CLI form also leaks into `LegacyLib` → NETSDK1147.

4. **No `OTHER_LDFLAGS` are required.** The self-contained dylib's only external
   dependencies are system libs/frameworks (`libSystem`, `libobjc`, `libswiftCore`,
   `libz`, `libicucore`, `CoreFoundation`, `CryptoKit`, `Foundation`, `Security`, `GSS`),
   all recorded as `LC_LOAD_DYLIB` in the framework binary; dyld resolves them at load.

---

## Prerequisites

**Authoring (any OS):** .NET SDK. Managed-only sanity builds of `LegacyLib` and the raw
`KestrelBackend` run on Windows/Linux.

**Build & run (Mac, mandatory):**
- .NET 9 SDK — `dotnet --version` → `9.x` (this PoC pinned `9.0.314` via `global.json`)
- iOS workload, 9.x band — `dotnet workload install ios`
- Xcode + command line tools — `xcode-select -p`
- [XcodeGen](https://github.com/yonaskolb/XcodeGen)

> NativeAOT-for-iOS cross-compiles via Apple clang + the iOS SDK. The dylib link,
> `install_name_tool`, `xcodebuild -create-xcframework`, codesign, and simulator/device
> run **cannot** happen on Windows.

> **Build-host gotcha (SIP-disabled macOS):** if `dotnet` is killed instantly
> (`SIGKILL`) or fails CoreCLR init, the cause is code-signing, not the SDK. On a
> SIP-disabled box the fix is to **remove** signatures from the dotnet Mach-O tree
> (`codesign --remove-signature ~/.dotnet/dotnet`), **not** to add a hardened-runtime
> ad-hoc signature — re-signing with `--options runtime` produces
> `Failed to create CoreCLR, HRESULT: 0x8007000C`. Re-strip after any new workload/pack
> install. (NativeAOT *output* is unaffected — it's ahead-of-time, no JIT/W^X at runtime.)

---

## 1. Directory structure

```
ios-embedded-krestel-poc/
├─ EmbeddedKestrel.slnx               # solution
├─ Directory.Build.props              # shared: LangVersion, Nullable, Deterministic
├─ README.md                          # this blueprint
├─ src/
│  ├─ LegacyLib/                      # ── Component 1 ──
│  │  ├─ LegacyLib.csproj             #    netstandard2.0, reflection-free
│  │  ├─ ProcessResult.cs             #    immutable wire-contract POCO
│  │  └─ DataProcessor.cs             #    business logic (SHA-256)
│  └─ KestrelBackend/                 # ── Component 2 ──
│     ├─ KestrelBackend.csproj        #    net9.0, PublishAot, NO NativeLib → dylib
│     ├─ NativeBootstrap.cs           #    [UnmanagedCallersOnly] kestrel_start/stop
│     ├─ IBackendHost.cs              #    Start(port)/Stop() seam
│     ├─ HostFactory.cs               #    #if USE_KESTREL → Kestrel : Raw
│     ├─ KestrelHost.cs               #    #if USE_KESTREL — CreateSlimBuilder + RDG
│     ├─ RawHttpHost.cs               #    always built — TcpListener, 0 deps (shipped)
│     └─ ApiJsonContext.cs            #    System.Text.Json source-gen context
├─ ios/                               # ── Component 3 ──
│  ├─ project.yml                     #    XcodeGen spec → EmbeddedKestrel.xcodeproj
│  └─ EmbeddedKestrelApp/
│     ├─ Info.plist                   #    NSAllowsLocalNetworking
│     ├─ KestrelBackend.h             #    C decls for the exported symbols
│     ├─ EmbeddedKestrelApp-Bridging-Header.h
│     ├─ EmbeddedKestrelApp.swift     #    @main App — boots server at launch
│     ├─ ServerController.swift       #    native wrapper + URLSession client
│     └─ ContentView.swift            #    SwiftUI: field → button → result
└─ build/
   ├─ publish-ios.sh                  # publish device+sim → framework → xcframework (Mac)
   └─ artifacts/KestrelBackend.xcframework   # produced by the script
```

---

## 2. Component 1 — `LegacyLib` (`netstandard2.0`)

The legacy library, untouched in its original form. `netstandard2.0` has **no implicit
usings** (disabled) and predates `IsAotCompatible` (net8+); AOT-safety is achieved
instead by keeping the code reflection-free.

```xml
<!-- src/LegacyLib/LegacyLib.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>LegacyLib</RootNamespace>
  </PropertyGroup>
</Project>
```

`ProcessResult` is the immutable wire contract (`Input`, `Hash`, `Length`,
`ProcessedAtUtc`). `DataProcessor.Process(string)` validates input, derives a value via
`Transform`, and stamps `DateTime.UtcNow.ToString("O")`. `Transform` hashes the UTF-8
bytes as **SHA-256 hex** (`System.Security.Cryptography.SHA256`, available on ns2.0). It
is deliberately allocation-light and reflection-free so it survives NativeAOT trimming.

> **Do not pass `-p:NativeLib` on the CLI**: it propagates here, to a `netstandard2.0`
> project, and trips NETSDK1147 (mobile-librarybuilder required). `LegacyLib` must never
> see an AOT/NativeLib property.

---

## 3. Component 2 — `KestrelBackend` (`net9.0`, NativeAOT → self-contained dylib)

### The decisive `.csproj`

```xml
<!-- src/KestrelBackend/KestrelBackend.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Plain net9.0, NOT net9.0-ios. The -ios TFM routes publish through the
         Microsoft.iOS workload SDK, which builds .app bundles and ignores ILC.
         Base net9.0 + the two PublishAot switches is the supported "NativeAOT for
         iOS-like platforms" route; cross-compile with -r ios-arm64. -->
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Library</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>KestrelBackend</RootNamespace>

    <PublishAot>true</PublishAot>
    <!-- REQUIRED for Apple RIDs; without it the SDK rejects ios-arm64 (NETSDK1203). -->
    <PublishAotUsingRuntimePack>true</PublishAotUsingRuntimePack>
    <!-- Deliberately NO <NativeLib>. The default publish output is a self-contained
         KestrelBackend.dylib. Static = unsupported consumption on iOS; Shared = Mono
         librarybuilder (breaks under a 9.0 SDK). See "Headline findings" #3. -->

    <!-- AOT size/trim switches: smaller binary, fewer trim surprises -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <EventSourceSupport>false</EventSourceSupport>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <AutoreleasePoolSupport>true</AutoreleasePoolSupport>

    <!-- Compiles Minimal-API lambdas into endpoints at build time (no reflection) -->
    <EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>
  </PropertyGroup>

  <!-- Kestrel is gated so the project still restores when the raw host is chosen.
       NOTE: with UseKestrel=true, `dotnet publish -r ios-arm64` fails NETSDK1082 —
       there is no Microsoft.AspNetCore.App runtime pack for iOS. Kept to document
       the attempt; not a shippable configuration on .NET 9. -->
  <PropertyGroup Condition="'$(UseKestrel)' == 'true'">
    <DefineConstants>$(DefineConstants);USE_KESTREL</DefineConstants>
  </PropertyGroup>
  <ItemGroup Condition="'$(UseKestrel)' == 'true'">
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LegacyLib\LegacyLib.csproj" />
  </ItemGroup>
</Project>
```

**Why each AOT switch matters**
- `PublishAot` + `PublishAotUsingRuntimePack` (and *no* `NativeLib`) → produces a
  self-contained `KestrelBackend.dylib` (managed code + the .NET runtime + GC, merged).
- `InvariantGlobalization=true` → drops the ICU data dependency; smaller lib.
- `EventSourceSupport`/`StackTraceSupport=false`, `UseSystemResourceKeys` → strip
  diagnostics machinery the embedded server doesn't need.
- `EnableRequestDelegateGenerator` (RDG) → source-generates endpoint plumbing so the
  (gated) Minimal-API routing doesn't rely on reflection the trimmer would break.

### The native boundary — `NativeBootstrap.cs`

```csharp
[UnmanagedCallersOnly(EntryPoint = "kestrel_start")]
public static int Start(int port)
{
    try
    {
        lock (Gate)
        {
            if (_host is not null) return 0;       // idempotent
            IBackendHost host = HostFactory.Create();
            host.Start(port);                      // binds before returning
            _host = host;
        }
        return 0;                                  // 0 = success
    }
    catch { return -1; }                           // never unwind into native code
}

[UnmanagedCallersOnly(EntryPoint = "kestrel_stop")]
public static void Stop() { try { lock (Gate) { _host?.Stop(); _host = null; } } catch { } }
```

Rules of the boundary, all enforced above: methods are `static`, take only **blittable**
args (`int`), and **no managed exception may escape** — failures become an `int` return
code. The .NET runtime initializes lazily on the first managed call, so no explicit
runtime-init export is needed. On Apple platforms NativeAOT prefixes the C symbols with
an underscore, so the exports are `_kestrel_start` / `_kestrel_stop`.

### Two hosts, one contract

- **`RawHttpHost`** (always built — **the shipped host**): a `TcpListener` on
  `IPAddress.Loopback`, a background accept loop, a minimal HTTP/1.1 request-line parse,
  routing for `GET /api/process` and `GET /health`, and
  `JsonSerializer.SerializeToUtf8Bytes(result, ApiJsonContext.Default.ProcessResult)`.
  Zero dependencies, binds synchronously, trivially AOT-safe.
- **`KestrelHost`** (`#if USE_KESTREL` — *cannot publish for iOS*):
  `WebApplication.CreateSlimBuilder()`, `ConfigureKestrel(o =>
  o.Listen(IPAddress.Loopback, port))`, `ApiJsonContext` via `ConfigureHttpJsonOptions`,
  the same two routes, `app.StartAsync().GetAwaiter().GetResult()` (bind-before-return).
  Retained for documentation and for a future iOS ASP.NET runtime pack.

Both call `new DataProcessor().Process(...)` — the reuse point that *is* the PoC.

### AOT-safe JSON — `ApiJsonContext.cs`

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProcessResult))]
internal partial class ApiJsonContext : JsonSerializerContext { }
```

Source generation emits the (de)serialization metadata at compile time, removing all
serializer reflection — the thing that otherwise breaks JSON under trimming. CamelCase
is why the Swift `Decodable` keys are `input`/`hash`/`length`/`processedAtUtc`.

---

## 4. Component 3a — Swift bridging header & interop

NativeAOT emits the symbols into the binary but **does not generate a C header** — we
hand-author one and keep it in lockstep with the `EntryPoint` names. (A local copy here,
resolved via `HEADER_SEARCH_PATHS`, keeps editing/indexing happy and is independent of
the framework packaging.)

```c
// ios/EmbeddedKestrelApp/KestrelBackend.h
#ifndef KESTREL_BACKEND_H
#define KESTREL_BACKEND_H
#ifdef __cplusplus
extern "C" {
#endif

int  kestrel_start(int port);   // 0 ok, -1 fail; non-blocking, idempotent
void kestrel_stop(void);

#ifdef __cplusplus
}
#endif
#endif /* KESTREL_BACKEND_H */
```

```c
// ios/EmbeddedKestrelApp/EmbeddedKestrelApp-Bridging-Header.h
#import "KestrelBackend.h"
```

The bridging header is wired via `SWIFT_OBJC_BRIDGING_HEADER` (see `ios/project.yml`).
Anything imported there is a global function in Swift — `kestrel_start(5001)` just works;
the symbol is satisfied at link time by the embedded `KestrelBackend.framework`.

`ServerController.swift` is the two-layer wrapper: **lifecycle** (calls the native entry
points, guarded by an `NSLock` so `startIfNeeded()` is one-shot) and **transport** (a
`URLSession` client). The decoded contract mirrors the camelCase JSON:

```swift
struct ProcessResponse: Decodable, Sendable {
    let input: String
    let hash: String
    let length: Int
    let processedAtUtc: String
}

func fetchProcessed(input: String) async throws -> ProcessResponse {
    guard startIfNeeded() else { throw ServerError.notStarted }   // self-starts the server

    var c = URLComponents()
    c.scheme = "http"; c.host = "127.0.0.1"; c.port = Int(port)
    c.path = "/api/process"
    c.queryItems = [URLQueryItem(name: "input", value: input)]
    guard let url = c.url else { throw ServerError.badURL }

    let (data, response) = try await session.data(from: url)
    if let http = response as? HTTPURLResponse, !(200...299).contains(http.statusCode) {
        throw ServerError.unexpectedStatus(http.statusCode)
    }
    return try JSONDecoder().decode(ProcessResponse.self, from: data)
}
```

---

## 5. Component 3b — Swift app

`@main` boots the server during launch on a detached background task so the native
runtime spin-up and socket bind never touch the main actor:

```swift
@main
struct EmbeddedKestrelApp: App {
    init() {
        Task.detached(priority: .background) {
            _ = ServerController.shared.startIfNeeded()
        }
    }
    var body: some Scene { WindowGroup { ContentView() } }
}
```

> **Launch-time QoS note.** The launch boot runs at `.background` (lowest) QoS, so the
> listener can take a second or two to bind after launch — an *external* client (e.g. a
> host `curl`) may briefly see connection-refused. The app's own button is immune:
> `fetchProcessed` calls `startIfNeeded()`, which invokes `kestrel_start` synchronously
> (`RawHttpHost.Start` binds before returning) and is idempotent under the `NSLock` +
> native `Gate`. Raise the QoS to `.utility` if you want faster external availability.

`ContentView` is a single SwiftUI `Form`: a `TextField`, a button that runs
`Task { try await ServerController.shared.fetchProcessed(input:) }`, and `@State` for the
result / error / loading spinner. On success it renders `input`, `length`,
`processedAtUtc`, and the monospaced, selectable `hash`.

### Info.plist — the one load-bearing key

```xml
<key>NSAppTransportSecurity</key>
<dict>
  <key>NSAllowsLocalNetworking</key>
  <true/>
</dict>
```

ATS blocks cleartext `http://` by default; this exception permits it for
local/loopback. Pure loopback (`127.0.0.1`) is also **exempt** from the iOS-14 Local
Network permission prompt, so `NSLocalNetworkUsageDescription` is intentionally omitted.

---

## 6. Compilation & packaging

### Sanity build (any OS, managed only)

```bash
dotnet build src/LegacyLib/LegacyLib.csproj
dotnet build src/KestrelBackend/KestrelBackend.csproj    # raw host (shipped)
```

### The gate — NativeAOT publish + framework + xcframework (Mac)

One script does both slices, the per-slice `.framework` wrap, the `xcframework`, and the
symbol/self-containment checks:

```bash
build/publish-ios.sh                       # ships RawHttpHost (UseKestrel=false default)
# USE_KESTREL=true build/publish-ios.sh    # ATTEMPTS Kestrel → NETSDK1082 (documented, not shippable)
```

Under the hood, per RID (note: **no** `-p:NativeLib`):

```bash
dotnet publish src/KestrelBackend/KestrelBackend.csproj -c Release -r ios-arm64
dotnet publish src/KestrelBackend/KestrelBackend.csproj -c Release -r iossimulator-arm64
# → bin/Release/net9.0/<rid>/publish/KestrelBackend.dylib  (self-contained)
```

Verify each dylib (`nm`), then wrap it in a flat iOS framework and combine:

```bash
nm -gU  KestrelBackend.dylib | grep _kestrel_     # expect _kestrel_start, _kestrel_stop
nm -u   KestrelBackend.dylib | grep -c _Rh        # expect 0  (runtime linked in)

# per slice: cp dylib → KestrelBackend.framework/KestrelBackend (+ Info.plist), then
install_name_tool -id @rpath/KestrelBackend.framework/KestrelBackend  .../KestrelBackend

xcodebuild -create-xcframework \
  -framework build/frameworks/ios-arm64/KestrelBackend.framework \
  -framework build/frameworks/iossimulator-arm64/KestrelBackend.framework \
  -output    build/artifacts/KestrelBackend.xcframework
```

> **pipefail trap:** verify exports by **counting** matches, not `grep -q`. Under
> `set -o pipefail`, `grep -q` closes the pipe on first match, `nm` dies with
> `SIGPIPE(141)`, and pipefail reports a spurious failure even though the symbols exist.

### Generate the Xcode project

```bash
cd ios && xcodegen generate          # → EmbeddedKestrel.xcodeproj
```

`ios/project.yml` encodes: the xcframework dependency with **`embed: true`** (Embed &
Sign — it's a self-contained dylib that must ship in the app's `Frameworks/`),
`SWIFT_OBJC_BRIDGING_HEADER`, `HEADER_SEARCH_PATHS`, the `Info.plist`, automatic signing
(`DEVELOPMENT_TEAM` + `CODE_SIGN_STYLE: Automatic`), and **no `OTHER_LDFLAGS`** (the
framework carries its own system-lib load commands).

### Run on the Simulator (no signing required)

```bash
xcodebuild -project EmbeddedKestrel.xcodeproj -target EmbeddedKestrelApp \
  -sdk iphonesimulator -configuration Debug \
  ARCHS=arm64 ONLY_ACTIVE_ARCH=NO \
  CONFIGURATION_BUILD_DIR="$PWD/out" \
  CODE_SIGNING_ALLOWED=NO build

UDID=$(xcrun simctl list devices available | grep -m1 'iPhone' | grep -oE '[0-9A-F-]{36}')
xcrun simctl boot "$UDID"; xcrun simctl bootstatus "$UDID"
xcrun simctl install "$UDID" out/EmbeddedKestrelApp.app
xcrun simctl launch  "$UDID" org.steveackley.EmbeddedKestrelApp
curl "http://127.0.0.1:5001/api/process?input=hello"      # the round trip
```

> **Arch gotcha:** on an Apple-Silicon Mac, omitting a `-destination` lets `xcodebuild`
> try to build `x86_64` too, which the arm64-only simulator slice can't satisfy
> (`missing architecture(s) required by this target (x86_64)`). Pin `ARCHS=arm64` (or
> pass `-destination 'platform=iOS Simulator,...'`).
> **Simulator networking:** iOS-Simulator apps are native macOS processes sharing the
> host network stack, so a listener on `127.0.0.1:5001` *in the app* is reachable as
> `127.0.0.1:5001` *on the Mac* — which is why the host `curl` above proves the path.

### Run on a physical iPad

Automatic signing needs an Apple ID signed into Xcode (free account is fine for
sideloading to your own device — the app uses no restricted entitlements). One-time GUI
steps on the Mac: unlock the iPad + **Trust This Computer**; enable **Developer Mode**
(Settings → Privacy & Security → Developer Mode → restart); **Xcode → Settings →
Accounts → add Apple ID**. Then, headless:

```bash
DEV_ID=$(xcrun devicectl list devices | awk '/connected/{print $(NF-1)}')   # the iPad UDID
xcodebuild -project EmbeddedKestrel.xcodeproj -target EmbeddedKestrelApp \
  -sdk iphoneos -configuration Debug -destination "id=$DEV_ID" \
  -allowProvisioningUpdates build
xcrun devicectl device install app   --device "$DEV_ID" out/EmbeddedKestrelApp.app
xcrun devicectl device process launch --device "$DEV_ID" org.steveackley.EmbeddedKestrelApp
```

---

## 7. Results — what was actually proven

Run on a Mac mini (Apple Silicon), Xcode 26.5 / iPhoneSimulator 26.5 SDK, .NET 9 SDK
9.0.314 + `ios` workload `26.5.9002/9.0.100`, on **2026-06-02**:

| Step | Result |
|---|---|
| `dotnet publish -r ios-arm64` (no `NativeLib`) | ✅ self-contained `KestrelBackend.dylib`, 2.5 MB, exports `_kestrel_start`/`_kestrel_stop`, **0** undefined `_Rh*` |
| `dotnet publish -r iossimulator-arm64` | ✅ second self-contained slice |
| `xcodebuild -create-xcframework` | ✅ `build/artifacts/KestrelBackend.xcframework` (device + sim) |
| `xcodegen generate` | ✅ `EmbeddedKestrel.xcodeproj` |
| Simulator build (`-sdk iphonesimulator`, `ARCHS=arm64`, signing off) | ✅ `EmbeddedKestrelApp.app` with `Frameworks/KestrelBackend.framework` embedded |
| `simctl install` + `launch` | ✅ app runs; SwiftUI renders; `PID → TCP 127.0.0.1:5001 (LISTEN)` confirmed via `lsof` |
| **Round trip** `curl …/api/process?input=hello` | ✅ `{"input":"hello","hash":"2cf24dba…938b9824","length":5,"processedAtUtc":"2026-06-02T21:28:25Z"}` — `2cf24dba…` is the canonical SHA-256 of `hello` |
| `curl …/api/process?input=hello,%20kestrel` | ✅ `length:14`, `hash:"a51ccdc7…44c65a93"` |
| `curl …/health` | ✅ `ok` |
| `-p:UseKestrel=true` publish | ❌ **NETSDK1082** (no ASP.NET runtime pack for iOS) — Kestrel-on-iOS confirmed dead on .NET 9 |
| Physical iPad (`iPad13,18`) | ⏳ detected via `devicectl`, `state=unavailable` until trust + Developer Mode + Xcode Apple ID |

**Bottom line:** the thesis is proven. A native iOS app reaches an untouched
`netstandard2.0` library through an in-process HTTP server compiled with NativeAOT — over
pure loopback, with no rewrite. Kestrel itself isn't available for iOS today, but the
PoC's value (the embed + interop + legacy-reuse pattern) holds with the dependency-free
host, and the contract is identical if/when an iOS ASP.NET pack ships.

---

## Risks & fallbacks — final status

| Risk | Status | Resolution |
|---|---|---|
| `Microsoft.AspNetCore.App` won't resolve for iOS | **Confirmed real** (NETSDK1082 at publish) | Ship `RawHttpHost`; identical contract |
| AOT trimming breaks routing/DI/JSON | **Moot for shipped host** | `RawHttpHost` is reflection-free; STJ source-gen for JSON |
| Consuming the native lib on iOS | **Solved** | Default self-contained `.dylib` → `.framework` → Embed & Sign; no `NativeLib`, no `OTHER_LDFLAGS` |
| Mono `librarybuilder` hijacks the AOT build | **Avoided** | Never set `<NativeLib>`; never pass `-p:NativeLib` on the CLI |
| First request races server bind | **Closed** | App button self-starts via synchronous `kestrel_start`; only external clients see the launch-QoS delay |
| SIP-disabled host kills `dotnet` | **Documented** | `codesign --remove-signature` the dotnet tree; do not hardened-runtime re-sign |
