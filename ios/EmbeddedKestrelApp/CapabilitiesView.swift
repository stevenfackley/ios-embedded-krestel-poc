// CapabilitiesView.swift — Grouped list of all capability probes with run/filter.

import SwiftUI

struct CapabilitiesView: View {
    @EnvironmentObject private var server: ServerController

    @State private var descriptors: [CapabilityDescriptor] = []
    @State private var results: [String: CapabilityResult] = [:]
    @State private var running: Set<String> = []
    @State private var filter: Verdict? = nil
    @State private var isLoading = false
    @State private var errorMessage: String?

    private var grouped: [(String, [CapabilityDescriptor])] {
        let shown = filter == nil ? descriptors : descriptors.filter { $0.verdict == filter }
        let categories = shown.map(\.category).uniqued()
        return categories.map { cat in (cat, shown.filter { $0.category == cat }) }
    }

    var body: some View {
        NavigationStack {
            Group {
                if isLoading {
                    ProgressView("Loading capabilities…")
                } else if descriptors.isEmpty {
                    ContentUnavailableView(
                        "No capabilities",
                        systemImage: "exclamationmark.triangle",
                        description: Text("Start the server and refresh")
                    )
                } else {
                    capabilityList
                }
            }
            .navigationTitle("Capabilities")
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) { filterMenu }
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Refresh") { Task { await load() } }
                }
            }
            .task { await load() }
        }
    }

    // MARK: - List

    private var capabilityList: some View {
        List {
            ForEach(grouped, id: \.0) { category, items in
                Section(category) {
                    ForEach(items) { descriptor in
                        ProbeRow(
                            descriptor: descriptor,
                            result: results[descriptor.id],
                            isRunning: running.contains(descriptor.id)
                        ) {
                            Task { await runProbe(descriptor.id) }
                        }
                    }
                }
            }
            if let errorMessage {
                Section("Error") {
                    Text(errorMessage).foregroundStyle(.red)
                }
            }
        }
    }

    // MARK: - Filter menu

    private var filterMenu: some View {
        Menu {
            Button("All") { filter = nil }
            Divider()
            ForEach(Verdict.allCases, id: \.self) { v in
                Button { filter = v } label: {
                    Label(v.label, systemImage: v.symbol)
                }
            }
        } label: {
            Image(systemName: filter == nil ? "line.3.horizontal.decrease.circle" :
                              "line.3.horizontal.decrease.circle.fill")
        }
    }

    // MARK: - Data loading

    private func load() async {
        guard server.isRunning else { return }
        isLoading = true
        defer { isLoading = false }
        do {
            descriptors = try await server.fetchDescriptors()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func runProbe(_ id: String) async {
        running.insert(id)
        defer { running.remove(id) }
        do {
            results[id] = try await server.runProbe(id)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

// MARK: - ProbeRow

struct ProbeRow: View {
    let descriptor: CapabilityDescriptor
    let result: CapabilityResult?
    let isRunning: Bool
    let onRun: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Image(systemName: actualVerdict.symbol)
                    .foregroundStyle(actualVerdict.color)
                Text(descriptor.title)
                    .font(.headline)
                Spacer()
                if isRunning {
                    ProgressView().scaleEffect(0.7)
                } else {
                    Button("Run", action: onRun)
                        .buttonStyle(.borderless)
                        .font(.caption)
                }
            }
            Text(descriptor.id)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .monospaced()
            if let detail = result?.detail {
                Text(detail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
            } else {
                Text(descriptor.detail)
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                    .lineLimit(2)
            }
            if let ms = result?.elapsedMs, ms > 0 {
                Text(String(format: "%.1f ms", ms))
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
        }
        .padding(.vertical, 4)
    }

    private var actualVerdict: Verdict {
        result?.verdict ?? descriptor.verdict
    }
}

// MARK: - Uniqued helper

private extension Array where Element: Hashable {
    func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}

#Preview {
    CapabilitiesView().environmentObject(ServerController.shared)
}
