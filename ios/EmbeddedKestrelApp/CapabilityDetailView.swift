// CapabilityDetailView.swift — drill-down page for a single capability probe.
// Shows the descriptor (expected verdict + mechanism), runs the probe on demand,
// and renders the live result including the server-defined `output` object as
// key/value rows — which is how the SQLCipher encryption-at-rest proof surfaces.

import SwiftUI

struct CapabilityDetailView: View {
    @EnvironmentObject private var server: ServerController
    let descriptor: CapabilityDescriptor
    @ObservedObject var viewModel: CapabilitiesViewModel

    private var result: CapabilityResult? { viewModel.results[descriptor.id] }
    private var isRunning: Bool { viewModel.isRunning(descriptor.id) }

    var body: some View {
        List {
            overviewSection
            runSection
            if let result { resultSection(result) }
            if let pairs = result?.output?.objectPairs, !pairs.isEmpty {
                outputSection(pairs)
            }
            if let problem = result?.error { errorSection(problem) }
        }
        .navigationTitle(descriptor.title)
        .navigationBarTitleDisplayMode(.inline)
    }

    // MARK: - Overview

    private var overviewSection: some View {
        Section("Overview") {
            LabeledContent("Capability") {
                Text(descriptor.id).font(.callout.monospaced()).foregroundStyle(.secondary)
            }
            LabeledContent("Category") { Text(descriptor.category) }
            LabeledContent("Expected") { VerdictBadge(verdict: descriptor.verdict) }
            VStack(alignment: .leading, spacing: 4) {
                Text("Summary").font(.caption).foregroundStyle(.secondary)
                Text(descriptor.detail).font(.callout)
            }
            VStack(alignment: .leading, spacing: 4) {
                Text("Mechanism").font(.caption).foregroundStyle(.secondary)
                Text(descriptor.mechanism).font(.callout)
            }
        }
    }

    // MARK: - Run

    private var runSection: some View {
        Section {
            Button {
                Task { await viewModel.runProbe(descriptor.id, using: server) }
            } label: {
                if isRunning {
                    HStack(spacing: 8) { ProgressView(); Text("Running…") }
                } else {
                    Label(result == nil ? "Run probe" : "Run again", systemImage: "play.fill")
                }
            }
            .disabled(isRunning || !server.isRunning)
            if !server.isRunning {
                Text("Server is not running — start it from the Dashboard.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    // MARK: - Result

    private func resultSection(_ result: CapabilityResult) -> some View {
        Section("Result") {
            LabeledContent("Verdict") { VerdictBadge(verdict: result.verdict) }
            if result.elapsedMs > 0 {
                LabeledContent("Elapsed") {
                    Text(String(format: "%.1f ms", result.elapsedMs)).monospacedDigit()
                }
            }
            if let detail = result.detail, !detail.isEmpty {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Detail").font(.caption).foregroundStyle(.secondary)
                    Text(detail).font(.callout).textSelection(.enabled)
                }
            }
            if let cid = result.correlationId {
                LabeledContent("Correlation") {
                    Text(cid).font(.caption.monospaced()).foregroundStyle(.secondary)
                }
            }
        }
    }

    // MARK: - Output (server-defined object, e.g. SQLCipher proof)

    private func outputSection(_ pairs: [(key: String, value: JSONValue)]) -> some View {
        Section("Output") {
            ForEach(pairs, id: \.key) { pair in
                OutputRow(key: pair.key, value: pair.value)
            }
        }
    }

    // MARK: - Error

    private func errorSection(_ problem: ProblemDetails) -> some View {
        Section("Error") {
            LabeledContent("Title") { Text(problem.title).foregroundStyle(.red) }
            LabeledContent("Status") { Text("\(problem.status)").monospacedDigit() }
            if let detail = problem.detail {
                Text(detail).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
            }
            LabeledContent("Correlation") {
                Text(problem.correlationId).font(.caption.monospaced()).foregroundStyle(.secondary)
            }
        }
    }
}

// MARK: - Reusable pieces

struct VerdictBadge: View {
    let verdict: Verdict
    var body: some View {
        Label(verdict.label, systemImage: verdict.symbol)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(verdict.color)
    }
}

/// One key/value row of a probe's `output` object. JSON booleans render as a
/// green check / red x so proof flags (e.g. wrongKeyRejected) read at a glance.
struct OutputRow: View {
    let key: String
    let value: JSONValue

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(key).font(.subheadline.weight(.medium))
            Spacer(minLength: 12)
            if let flag = value.boolValue {
                Image(systemName: flag ? "checkmark.circle.fill" : "xmark.circle.fill")
                    .foregroundStyle(flag ? .green : .red)
            } else {
                Text(value.displayString)
                    .font(.subheadline.monospaced())
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.trailing)
                    .textSelection(.enabled)
            }
        }
    }
}

#Preview {
    NavigationStack {
        CapabilityDetailView(
            descriptor: CapabilityDescriptor(
                id: "persist.sqlcipher",
                category: "Persistence",
                title: "SQLCipher encryption-at-rest",
                detail: "Keyed on-disk DB: cipher_version, ciphertext header, wrong-key rejected, round-trip",
                verdict: .works,
                mechanism: "Password=key → PRAGMA key (SQLCipher)"
            ),
            viewModel: CapabilitiesViewModel()
        )
        .environmentObject(ServerController.shared)
    }
}
