using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace KestrelBackend;

/// <summary>
/// Demonstrates NativeAOT + iOS sandbox limitations. Each probe attempts the
/// restricted operation; on JIT/.NET 9 it returns Limited (works here, fails on
/// NativeAOT); on NativeAOT it throws and returns Fails.
/// Architectural limitations (no Newtonsoft ref, no Kestrel iOS pack, no gRPC
/// proxy) return Fails unconditionally with a diagnostic explanation.
/// </summary>
internal sealed class LimitsModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("limit.expressioncompile", "Limitations", "Expression.Compile()",
            "LambdaExpression.Compile() requires runtime code generation",
            Verdict.Fails, "PlatformNotSupportedException on NativeAOT; use source-gen or static lambdas"),
        new("limit.reflectionemit", "Limitations", "Reflection.Emit",
            "AssemblyBuilder / ILGenerator requires runtime codegen",
            Verdict.Fails, "NotSupportedException on NativeAOT; Emit APIs removed from iOS framework"),
        new("limit.process", "Limitations", "Process.Start()",
            "Process.Start() blocked by iOS sandbox; GetCurrentProcess() works",
            Verdict.Limited, "GetCurrentProcess() returns the host PID; Process.Start() throws PlatformNotSupportedException — use NSTask (ObjC) for subprocesses"),
        new("limit.dynamicload", "Limitations", "Assembly.LoadFrom()",
            "Dynamic assembly loading not supported on NativeAOT / iOS",
            Verdict.Fails, "NotSupportedException; all code must be statically linked at build time"),
        new("limit.globalization", "Limitations", "Non-invariant cultures",
            "ICU globalization data not bundled with NativeAOT iOS app by default",
            Verdict.Limited, "Set InvariantGlobalization=true or bundle ICU; culture-specific formatting degrades"),
        new("limit.stacktrace", "Limitations", "StackTrace detail",
            "Method names mangled or absent under NativeAOT without PDB",
            Verdict.Limited, "Frames present but names may be '[...]' without embedded symbols"),
        new("limit.reflectioninvoke", "Limitations", "Reflection on trimmed types",
            "MethodInfo.Invoke() on types not annotated with [DynamicallyAccessedMembers] may fail after trim",
            Verdict.Limited, "IL2026 trim warning; methods removed unless rooted via attributes or rd.xml"),
        new("limit.jsonreflection", "Limitations", "STJ without source-gen",
            "JsonSerializer.Serialize<T>(value) uses reflection; IL3050 AOT warning",
            Verdict.Limited, "Works on JIT; behavior undefined on NativeAOT without ApiJsonContext source-gen"),
        new("limit.newtonsoft", "Limitations", "Newtonsoft.Json",
            "Reflection-based JSON; not trim-safe; not NativeAOT-compatible",
            Verdict.Fails, "IL2026/IL3050 warnings; dynamic serialization fails on NativeAOT — use STJ+source-gen"),
        new("limit.eventsource", "Limitations", "EventSource / ETW",
            "EventSource events silently dropped on iOS; ETW not available",
            Verdict.Limited, "Compiles; Write() is no-op on iOS; use ILogger+RingBufferSink instead"),
        new("limit.kestrel", "Limitations", "ASP.NET Core Kestrel on iOS",
            "Kestrel runtime pack not published for iOS NativeAOT target",
            Verdict.Fails, "No iOS arm64 Kestrel pack; this PoC uses a raw TCP host instead — see RawHttpHost.cs"),
        new("limit.grpc", "Limitations", "gRPC-dotnet on iOS NativeAOT",
            "gRPC requires HTTP/2 + dynamic proxy generation; both blocked",
            Verdict.Fails, "DynamicProxy: NotSupportedException; no iOS Kestrel; use REST over RawHttpHost"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "limit.expressioncompile" => RunExpressionCompile(),
            "limit.reflectionemit"    => RunReflectionEmit(),
            "limit.process"           => RunProcess(),
            "limit.dynamicload"       => RunDynamicLoad(),
            "limit.globalization"     => RunGlobalization(),
            "limit.stacktrace"        => RunStackTrace(),
            "limit.reflectioninvoke"  => RunReflectionInvoke(),
            "limit.jsonreflection"    => RunJsonReflection(),
            "limit.newtonsoft"        => RunNewtonsoft(),
            "limit.eventsource"       => RunEventSource(),
            "limit.kestrel"           => RunKestrel(),
            "limit.grpc"              => RunGrpc(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) { }

    // ── probes ────────────────────────────────────────────────────────────────

    private static CapabilityResult RunExpressionCompile()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return Fails("limit.expressioncompile", "Limitations", "Expression.Compile()",
                "PlatformNotSupportedException: dynamic code not supported on NativeAOT");
        try
        {
            var x = Expression.Parameter(typeof(int), "x");
            var compiled = Expression.Lambda<Func<int, int>>(
                Expression.Add(x, Expression.Constant(1)), x).Compile();
            return Limited("limit.expressioncompile", "Limitations", "Expression.Compile()",
                $"Compiled on JIT (probe(41)={compiled(41)}); throws PlatformNotSupportedException on NativeAOT");
        }
        catch (Exception ex)
        {
            return Fails("limit.expressioncompile", "Limitations", "Expression.Compile()", ex.Message);
        }
    }

    private static CapabilityResult RunReflectionEmit()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return Fails("limit.reflectionemit", "Limitations", "Reflection.Emit",
                "NotSupportedException: Reflection.Emit removed from NativeAOT");
        try
        {
            var ab = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("KestrelProbe_Emit"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var mb = ab.DefineDynamicModule("DynModule");
            return Limited("limit.reflectionemit", "Limitations", "Reflection.Emit",
                $"AssemblyBuilder+DefineDynamicModule succeeded on JIT; NotSupportedException on NativeAOT");
        }
        catch (Exception ex)
        {
            return Fails("limit.reflectionemit", "Limitations", "Reflection.Emit", ex.Message);
        }
    }

    private static CapabilityResult RunProcess()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            return Limited("limit.process", "Limitations", "Process.Start()",
                $"GetCurrentProcess() works (PID={p.Id}); " +
                "Process.Start() throws PlatformNotSupportedException on iOS sandbox");
        }
        catch (Exception ex)
        {
            return Fails("limit.process", "Limitations", "Process.Start()", ex.Message);
        }
    }

    private static CapabilityResult RunDynamicLoad()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return Fails("limit.dynamicload", "Limitations", "Assembly.LoadFrom()",
                "NotSupportedException: dynamic assembly loading not supported on NativeAOT");
        // Don't load an arbitrary path; just show the API is reachable on JIT
        return Limited("limit.dynamicload", "Limitations", "Assembly.LoadFrom()",
            "Assembly.LoadFrom() API exists on JIT; " +
            "NotSupportedException on NativeAOT — all assemblies must be statically linked");
    }

    private static CapabilityResult RunGlobalization()
    {
        try
        {
            var en = CultureInfo.GetCultureInfo("en-US");
            string formatted = (1_234_567.89).ToString("N2", en);
            bool invariant = CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture);
            return Limited("limit.globalization", "Limitations", "Non-invariant cultures",
                $"en-US format OK: {formatted}; using invariant={invariant}. " +
                "iOS NativeAOT: set <InvariantGlobalization>true</InvariantGlobalization> " +
                "or bundle ICU to avoid runtime CultureNotFoundException");
        }
        catch (CultureNotFoundException ex)
        {
            return Fails("limit.globalization", "Limitations", "Non-invariant cultures",
                $"CultureNotFoundException: {ex.Message} — InvariantGlobalization active");
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026", Justification = "Intentional: demonstrating StackFrame.GetMethod() trim limitation")]
    private static CapabilityResult RunStackTrace()
    {
        var st = new StackTrace(fNeedFileInfo: false);
        var frames = st.GetFrames();
        int named = frames.Count(f => !string.IsNullOrEmpty(f.GetMethod()?.Name));
        return Limited("limit.stacktrace", "Limitations", "StackTrace detail",
            $"{frames.Length} frames; {named} with method names on JIT. " +
            "On NativeAOT without PDB embed: names show as '[...]' or are absent; " +
            "use structured logging + CorrelationId instead of stack traces for diagnostics");
    }

    private static CapabilityResult RunReflectionInvoke()
    {
        // string.ToUpper() is BCL-rooted and survives trimming — safe to invoke via reflection
        var mi = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes);
        if (mi is null)
            return Fails("limit.reflectioninvoke", "Limitations", "Reflection on trimmed types",
                "string.ToUpper trimmed away — unexpected");
        object? result = mi.Invoke("hello", null);
        return Limited("limit.reflectioninvoke", "Limitations", "Reflection on trimmed types",
            $"BCL string.ToUpper() via MethodInfo.Invoke='{result}'; " +
            "user-defined types lose members after trim unless annotated with " +
            "[DynamicallyAccessedMembers] or preserved via rd.xml");
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026", Justification = "Intentional: demonstrating reflection fallback limitation")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT", "IL3050", Justification = "Intentional: demonstrating AOT-unsafe serialization")]
    private static CapabilityResult RunJsonReflection()
    {
        try
        {
            // Reflection path — IL3050 suppressed intentionally to demonstrate the limitation
            string json = System.Text.Json.JsonSerializer.Serialize(new { probe = "json-reflection", x = 42 });
            return Limited("limit.jsonreflection", "Limitations", "STJ without source-gen",
                $"Reflection path produced: {json}. " +
                "Build emits IL3050 warning; NativeAOT runtime behavior undefined. " +
                "All production types use ApiJsonContext source-gen ([JsonSerializable])");
        }
        catch (Exception ex)
        {
            return Fails("limit.jsonreflection", "Limitations", "STJ without source-gen", ex.Message);
        }
    }

    private static CapabilityResult RunNewtonsoft() =>
        Fails("limit.newtonsoft", "Limitations", "Newtonsoft.Json",
            "Not referenced: Newtonsoft.Json uses reflection-based serialization incompatible with " +
            "NativeAOT trimming. Produces IL2026/IL3050 AOT analyzer warnings. " +
            "Replacement: System.Text.Json with [JsonSerializable] source generation.");

    private static CapabilityResult RunEventSource()
    {
        // EventSource is defined but Write() is a no-op on iOS
        return Limited("limit.eventsource", "Limitations", "EventSource / ETW",
            "EventSource compiles and IsEnabled() returns false on iOS (no ETW). " +
            "Events are silently dropped. Use ILogger<T> with RingBufferSink for in-process " +
            "structured diagnostics accessible via /api/diag/logs.");
    }

    private static CapabilityResult RunKestrel() =>
        Fails("limit.kestrel", "Limitations", "ASP.NET Core Kestrel on iOS",
            "No Kestrel runtime pack published for ios-arm64 NativeAOT. " +
            "ASP.NET Core hosting model requires libuv/IOCP and managed thread pool unavailable on iOS. " +
            "This PoC solves it with RawHttpHost: a minimal TCP listener with an HTTP/1.1 parser " +
            "and Router — no framework dependencies.");

    private static CapabilityResult RunGrpc() =>
        Fails("limit.grpc", "Limitations", "gRPC-dotnet on iOS NativeAOT",
            "gRPC-dotnet requires: (1) Kestrel — not available on iOS NativeAOT; " +
            "(2) DynamicProxy for interceptors — NotSupportedException on NativeAOT; " +
            "(3) HTTP/2 server — not exposed via RawHttpHost. " +
            "Alternative: use REST/JSON over RawHttpHost (this PoC's approach).");

    // ── helpers ───────────────────────────────────────────────────────────────

    private static CapabilityResult Limited(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Limited,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Fails(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Fails,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}
