// CapabilitiesView.swift — Table-of-contents over all capability probes:
// collapsible category sections (DisclosureGroup) whose rows drill into a
// per-capability CapabilityDetailView. State lives in CapabilitiesViewModel so
// runs started on a detail page are reflected here after popping back.

import SwiftUI

struct CapabilitiesView: View {
    @EnvironmentObject private var server: ServerController
    @StateObject private var viewModel = CapabilitiesViewModel()

    // Categories are expanded unless the user collapses them (so the TOC opens
    // fully populated rather than as a wall of empty headers).
    @State private var collapsed: Set<String> = []

    var body: some View {
        NavigationStack {
            Group {
                if viewModel.isLoading {
                    ProgressView("Loading capabilities…")
                } else if viewModel.descriptors.isEmpty {
                    ContentUnavailableView(
                        "No capabilities",
                        systemImage: "exclamationmark.triangle",
                        description: Text("Start the server and refresh")
                    )
                } else {
                    tableOfContents
                }
            }
            .navigationTitle("Capabilities")
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) { filterMenu }
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Refresh") { Task { await viewModel.load(using: server) } }
                }
            }
            .task { await viewModel.load(using: server) }
        }
    }

    // MARK: - TOC

    private var tableOfContents: some View {
        List {
            ForEach(viewModel.grouped, id: \.0) { category, items in
                DisclosureGroup(isExpanded: expansion(for: category)) {
                    ForEach(items) { descriptor in
                        NavigationLink {
                            CapabilityDetailView(descriptor: descriptor, viewModel: viewModel)
                        } label: {
                            CapabilityTocRow(
                                descriptor: descriptor,
                                result: viewModel.results[descriptor.id],
                                isRunning: viewModel.isRunning(descriptor.id)
                            )
                        }
                    }
                } label: {
                    categoryHeader(category, count: items.count)
                }
            }
            if let errorMessage = viewModel.errorMessage {
                Section("Error") {
                    Text(errorMessage).foregroundStyle(.red)
                }
            }
        }
    }

    private func categoryHeader(_ category: String, count: Int) -> some View {
        HStack {
            Text(category).font(.headline)
            Spacer()
            Text("\(count)")
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
                .padding(.horizontal, 8)
                .padding(.vertical, 2)
                .background(.quaternary, in: Capsule())
        }
    }

    /// Expanded-by-default binding backed by the `collapsed` set.
    private func expansion(for category: String) -> Binding<Bool> {
        Binding(
            get: { !collapsed.contains(category) },
            set: { isExpanded in
                if isExpanded { collapsed.remove(category) } else { collapsed.insert(category) }
            }
        )
    }

    // MARK: - Filter menu

    private var filterMenu: some View {
        Menu {
            Button("All") { viewModel.filter = nil }
            Divider()
            ForEach(Verdict.allCases, id: \.self) { v in
                Button { viewModel.filter = v } label: {
                    Label(v.label, systemImage: v.symbol)
                }
            }
        } label: {
            Image(systemName: viewModel.filter == nil ? "line.3.horizontal.decrease.circle" :
                              "line.3.horizontal.decrease.circle.fill")
        }
    }
}

// MARK: - TOC row

struct CapabilityTocRow: View {
    let descriptor: CapabilityDescriptor
    let result: CapabilityResult?
    let isRunning: Bool

    // Show the actual verdict once a run exists, otherwise the descriptor's expected.
    private var verdict: Verdict { result?.verdict ?? descriptor.verdict }

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: verdict.symbol)
                .foregroundStyle(verdict.color)
            VStack(alignment: .leading, spacing: 2) {
                Text(descriptor.title).font(.body)
                Text(descriptor.id)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .monospaced()
            }
            Spacer()
            if isRunning {
                ProgressView().scaleEffect(0.7)
            } else if result != nil {
                Image(systemName: "checkmark.seal")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
        }
        .padding(.vertical, 2)
    }
}

#Preview {
    CapabilitiesView().environmentObject(ServerController.shared)
}
