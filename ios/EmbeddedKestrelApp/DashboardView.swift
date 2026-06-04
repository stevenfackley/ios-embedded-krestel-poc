// DashboardView.swift — Server status + quick run-all summary.

import SwiftUI

struct DashboardView: View {
    @EnvironmentObject private var server: ServerController
    @State private var runAllResults: [CapabilityResult] = []
    @State private var isRunningAll = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            List {
                serverStatusSection
                if !runAllResults.isEmpty { summarySection }
                runAllSection
                if let errorMessage { errorSection(errorMessage) }
            }
            .navigationTitle("Embedded .NET on iOS")
            .task { await server.startIfNeeded() }
        }
    }

    // MARK: - Sections

    private var serverStatusSection: some View {
        Section("Server") {
            LabeledContent("Status") {
                HStack(spacing: 6) {
                    Circle()
                        .fill(server.isRunning ? .green : .red)
                        .frame(width: 10, height: 10)
                    Text(server.isRunning ? "Running" : "Stopped")
                        .foregroundStyle(server.isRunning ? .green : .red)
                }
            }
            if server.isRunning {
                LabeledContent("Port", value: "\(server.boundPort)")
                LabeledContent("Message", value: server.statusMessage)
            }
            if !server.isRunning {
                Button("Start Server") {
                    Task { await server.startIfNeeded() }
                }
            }
        }
    }

    private var runAllSection: some View {
        Section {
            Button(action: runAll) {
                HStack {
                    Label("Run All Probes", systemImage: "bolt.fill")
                    if isRunningAll {
                        Spacer()
                        ProgressView()
                    }
                }
            }
            .disabled(!server.isRunning || isRunningAll)
        } footer: {
            Text("Runs all \(runAllResults.count > 0 ? String(runAllResults.count) : "~37") capability probes and shows a summary.")
        }
    }

    private var summarySection: some View {
        Section("Last Run-All Summary") {
            let works   = runAllResults.filter { $0.verdict == .works }.count
            let limited = runAllResults.filter { $0.verdict == .limited }.count
            let fails   = runAllResults.filter { $0.verdict == .fails }.count

            ForEach([
                ("Works",   works,   Verdict.works),
                ("Limited", limited, Verdict.limited),
                ("Fails",   fails,   Verdict.fails),
            ], id: \.0) { label, count, verdict in
                HStack {
                    Image(systemName: verdict.symbol)
                        .foregroundStyle(verdict.color)
                    Text(label)
                    Spacer()
                    Text("\(count)")
                        .font(.headline)
                        .foregroundStyle(verdict.color)
                }
            }
        }
    }

    private func errorSection(_ msg: String) -> some View {
        Section("Error") {
            Text(msg).foregroundStyle(.red)
        }
    }

    // MARK: - Actions

    private func runAll() {
        isRunningAll = true
        errorMessage = nil
        Task {
            defer { isRunningAll = false }
            do {
                runAllResults = try await server.runAll()
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }
}

#Preview {
    DashboardView().environmentObject(ServerController.shared)
}
