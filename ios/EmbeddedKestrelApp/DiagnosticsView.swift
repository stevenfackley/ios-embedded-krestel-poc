// DiagnosticsView.swift — /api/diag/info + /api/diag/logs ring-buffer viewer.

import SwiftUI

struct DiagnosticsView: View {
    @EnvironmentObject private var server: ServerController
    @State private var diagInfo: DiagInfoResponse?
    @State private var logs: [LogEntry] = []
    @State private var isLoading = false
    @State private var error: String?

    var body: some View {
        NavigationStack {
            List {
                if let info = diagInfo { infoSection(info) }
                logsSection
                if let error { Section("Error") { Text(error).foregroundStyle(.red) } }
            }
            .navigationTitle("Diagnostics")
            .toolbar {
                Button("Refresh") { Task { await load() } }
            }
            .task { await load() }
            .overlay {
                if isLoading { ProgressView() }
            }
        }
    }

    // MARK: - Info section

    private func infoSection(_ info: DiagInfoResponse) -> some View {
        Section("Server Info") {
            LabeledContent("Port",     value: "\(info.port)")
            LabeledContent("Uptime",   value: String(format: "%.1f s", info.uptimeSeconds))
            LabeledContent("Requests", value: "\(info.requestsServed)")
            LabeledContent(".NET",     value: info.dotnetVersion)
            LabeledContent("OS",       value: String(info.os.prefix(50)))
            LabeledContent("Arch",     value: info.processArch)
        }
    }

    // MARK: - Logs section

    private var logsSection: some View {
        Section("Ring Buffer (\(logs.count) entries)") {
            if logs.isEmpty {
                Text("No log entries captured yet").foregroundStyle(.secondary)
            } else {
                ForEach(logs.suffix(50).reversed()) { entry in
                    LogEntryRow(entry: entry)
                }
            }
        }
    }

    // MARK: - Data loading

    private func load() async {
        guard server.isRunning else { return }
        isLoading = true
        defer { isLoading = false }
        do {
            async let info = server.fetchDiagInfo()
            async let logEntries = server.fetchLogs()
            diagInfo = try await info
            logs = try await logEntries
        } catch {
            self.error = error.localizedDescription
        }
    }
}

// MARK: - LogEntryRow

struct LogEntryRow: View {
    let entry: LogEntry

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(entry.level)
                    .font(.caption2.bold())
                    .foregroundStyle(levelColor)
                Text(entry.category)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                Spacer()
                Text("#\(entry.seq)")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
            Text(entry.message)
                .font(.caption)
                .lineLimit(3)
            if let corr = entry.correlationId, !corr.isEmpty {
                Text(corr)
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundStyle(.tertiary)
            }
        }
        .padding(.vertical, 2)
    }

    private var levelColor: Color {
        switch entry.level.lowercased() {
        case "error",    "critical": return .red
        case "warning":              return .orange
        case "information":          return .blue
        default:                     return .secondary
        }
    }
}

#Preview {
    DiagnosticsView().environmentObject(ServerController.shared)
}
