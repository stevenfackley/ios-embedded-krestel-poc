// AboutView.swift — Explains the PoC architecture, advantages, and limitations.

import SwiftUI

struct AboutView: View {
    var body: some View {
        NavigationStack {
            List {
                thesisSection
                architectureSection
                advantagesSection
                limitationsSection
                stackSection
            }
            .navigationTitle("About this PoC")
        }
    }

    // MARK: - Sections

    private var thesisSection: some View {
        Section("Core Thesis") {
            Text("""
                Embedded .NET on iOS: run a real .NET 9 NativeAOT shared library inside an \
                iOS app. No Xamarin. No MAUI. Swift is the host; .NET provides business logic \
                and an HTTP server over a loopback TCP socket.
                """)
            .font(.body)
        }
    }

    private var architectureSection: some View {
        Section("Architecture") {
            archRow("Native ABI",
                    detail: "kestrel_start / kestrel_stop / kestrel_info / kestrel_last_error — blittable C exports from NativeBootstrap.cs",
                    icon: "link")
            archRow("Transport",
                    detail: "RawHttpHost: a minimal TCP listener with a hand-written HTTP/1.1 parser — no ASP.NET runtime dependency",
                    icon: "antenna.radiowaves.left.and.right")
            archRow("Routing",
                    detail: "Router with path-template segments ({id}); 3-stage pipeline: CorrelationId → RequestLogging → Exception",
                    icon: "arrow.triangle.branch")
            archRow("DI / Logging",
                    detail: "Microsoft.Extensions.DependencyInjection + a RingBufferSink that captures structured log entries for /api/diag/logs",
                    icon: "cylinder.split.1x2")
            archRow("Capability Catalog",
                    detail: "37 probes across 12 modules: ICapabilityModule → CapabilityCatalog → /api/capabilities/run-all",
                    icon: "checklist")
        }
    }

    private var advantagesSection: some View {
        Section("What Works on NativeAOT") {
            advantageRow("System.Security.Cryptography",   detail: "SHA-256/512, HMAC, AES-GCM, RSA, PBKDF2")
            advantageRow("STJ Source Generation",          detail: "[JsonSerializable] — zero-reflection JSON")
            advantageRow("Microsoft.Data.Sqlite",          detail: "In-memory SQLite via :memory: — iOS sandbox-safe")
            advantageRow("System.Threading.Channels",      detail: "Lock-free bounded Channel<T>")
            advantageRow("Parallel.ForEachAsync",          detail: "CPU-bound parallel workload")
            advantageRow("System.Numerics.BigInteger",     detail: "Arbitrary-precision arithmetic")
            advantageRow("INumber<T> generic math",        detail: "Static abstractions; zero boxing")
            advantageRow("SIMD Vector<T>",                 detail: "Hardware arm64 NEON via Vector<float>")
            advantageRow("[GeneratedRegex]",               detail: "Source-gen compiled regex; zero reflection")
            advantageRow("System.IO.Compression",          detail: "GZipStream + BrotliStream roundtrip")
            advantageRow("M.E.DI / IOptions<T>",           detail: "Full DI container; source-gen config binder")
            advantageRow("[LoggerMessage] structured log", detail: "Zero-alloc source-gen log methods")
            advantageRow("netstandard2.0 reuse",           detail: "LegacyLib.DataProcessor — zero modification")
        }
    }

    private var limitationsSection: some View {
        Section("NativeAOT / iOS Limitations") {
            limitRow("Expression.Compile()",   detail: "PlatformNotSupportedException — no runtime codegen")
            limitRow("Reflection.Emit",        detail: "AssemblyBuilder removed from NativeAOT")
            limitRow("Process.Start()",        detail: "Blocked by iOS app sandbox")
            limitRow("Assembly.LoadFrom()",    detail: "Dynamic loading not supported")
            limitRow("Non-invariant cultures", detail: "InvariantGlobalization=true required")
            limitRow("StackTrace detail",      detail: "Names mangled without embedded PDB")
            limitRow("Newtonsoft.Json",        detail: "Reflection-based; IL2026/IL3050 warnings")
            limitRow("Kestrel on iOS",         detail: "No ios-arm64 Kestrel runtime pack → use RawHttpHost")
            limitRow("gRPC-dotnet",            detail: "DynamicProxy + HTTP/2 server both blocked")
        }
    }

    private var stackSection: some View {
        Section("Tech Stack") {
            LabeledContent(".NET",       value: "9.0 NativeAOT (net9.0)")
            LabeledContent("Swift",      value: "6.0 / SwiftUI")
            LabeledContent("Transport",  value: "Raw TCP socket (no Kestrel)")
            LabeledContent("JSON",       value: "System.Text.Json + source-gen")
            LabeledContent("DB",         value: "Microsoft.Data.Sqlite 9.0.0")
            LabeledContent("DI",         value: "Microsoft.Extensions.DependencyInjection 9.0.0")
            LabeledContent("Logging",    value: "Microsoft.Extensions.Logging 9.0.0")
        }
    }

    // MARK: - Row helpers

    private func archRow(_ title: String, detail: String, icon: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon).foregroundStyle(.tint).frame(width: 24)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 2)
    }

    private func advantageRow(_ title: String, detail: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.subheadline)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func limitRow(_ title: String, detail: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "xmark.circle.fill").foregroundStyle(.red)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.subheadline)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

#Preview {
    AboutView()
}
