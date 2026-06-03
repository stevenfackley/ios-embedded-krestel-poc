// ContentView.swift
//
// Minimal SwiftUI screen that drives the end-to-end round trip:
//   text field → button → URLSession GET 127.0.0.1:5001/api/process → render.
// State is intentionally local (@State) since this is a single-screen PoC.

import SwiftUI

struct ContentView: View {
    @State private var input: String = "hello, kestrel"
    @State private var result: ProcessResponse?
    @State private var errorMessage: String?
    @State private var isLoading = false

    var body: some View {
        NavigationStack {
            Form {
                Section("Input") {
                    TextField("Text to process", text: $input)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .onSubmit(run)

                    Button(action: run) {
                        HStack {
                            Text("Process via embedded Kestrel")
                            if isLoading {
                                Spacer()
                                ProgressView()
                            }
                        }
                    }
                    .disabled(isLoading || input.isEmpty)
                }

                if let result {
                    Section("Result from 127.0.0.1:5001") {
                        LabeledContent("Input", value: result.input)
                        LabeledContent("Length", value: String(result.length))
                        LabeledContent("Processed (UTC)", value: result.processedAtUtc)
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Hash").foregroundStyle(.secondary)
                            Text(result.hash)
                                .font(.system(.footnote, design: .monospaced))
                                .textSelection(.enabled)
                        }
                    }
                }

                if let errorMessage {
                    Section("Error") {
                        Text(errorMessage).foregroundStyle(.red)
                    }
                }
            }
            .navigationTitle("Embedded Kestrel")
        }
    }

    /// Fires the async request and funnels success/failure into view state.
    private func run() {
        guard !input.isEmpty else { return }
        isLoading = true
        errorMessage = nil

        Task {
            defer { isLoading = false }
            do {
                result = try await ServerController.shared.fetchProcessed(input: input)
            } catch {
                result = nil
                errorMessage = error.localizedDescription
            }
        }
    }
}

#Preview {
    ContentView()
}
