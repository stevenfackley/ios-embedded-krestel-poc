// PlaygroundView.swift — Interactive sub-playgrounds for crypto, compression,
// regex, and notes CRUD. Each playground exercises a custom endpoint directly.

import SwiftUI

struct PlaygroundView: View {
    var body: some View {
        NavigationStack {
            List {
                NavigationLink("Crypto — SHA-256 Hash", destination: CryptoPlayground())
                NavigationLink("Compression — GZip ratio", destination: CompressPlayground())
                NavigationLink("Regex — Word matcher", destination: RegexPlayground())
                NavigationLink("Notes — SQLite CRUD", destination: NotesPlayground())
            }
            .navigationTitle("Playgrounds")
        }
    }
}

// MARK: - Crypto playground

struct CryptoPlayground: View {
    @EnvironmentObject private var server: ServerController
    @State private var input = "hello, kestrel"
    @State private var result: CryptoHashResult?
    @State private var isLoading = false
    @State private var error: String?

    var body: some View {
        Form {
            Section("Input") {
                TextField("Text to hash", text: $input)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                Button(action: run) {
                    HStack { Text("Hash (SHA-256)"); if isLoading { Spacer(); ProgressView() } }
                }
                .disabled(isLoading || input.isEmpty || !server.isRunning)
            }
            if let r = result {
                Section("Result") {
                    LabeledContent("Algorithm", value: r.algorithm)
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Hash").foregroundStyle(.secondary)
                        Text(r.hash).font(.system(.footnote, design: .monospaced))
                            .textSelection(.enabled)
                    }
                }
            }
            if let error { Section("Error") { Text(error).foregroundStyle(.red) } }
        }
        .navigationTitle("Crypto Hash")
    }

    private func run() {
        isLoading = true; error = nil
        Task {
            defer { isLoading = false }
            do { result = try await server.hashText(input) }
            catch { self.error = error.localizedDescription }
        }
    }
}

// MARK: - Compression playground

struct CompressPlayground: View {
    @EnvironmentObject private var server: ServerController
    @State private var input = String(repeating: "hello gzip world! ", count: 5)
    @State private var result: CompressResult?
    @State private var isLoading = false
    @State private var error: String?

    var body: some View {
        Form {
            Section("Input") {
                TextEditor(text: $input).frame(height: 100)
                Button(action: run) {
                    HStack { Text("Compress (GZip)"); if isLoading { Spacer(); ProgressView() } }
                }
                .disabled(isLoading || input.isEmpty || !server.isRunning)
            }
            if let r = result {
                Section("Result") {
                    LabeledContent("Algorithm",   value: r.algorithm)
                    LabeledContent("Raw bytes",   value: "\(r.rawBytes)")
                    LabeledContent("Compressed",  value: "\(r.compressedBytes)")
                    LabeledContent("Ratio",       value: String(format: "%.1f%%", r.ratio * 100))
                    ProgressView(value: r.ratio)
                        .tint(r.ratio < 0.5 ? .green : r.ratio < 0.8 ? .orange : .red)
                }
            }
            if let error { Section("Error") { Text(error).foregroundStyle(.red) } }
        }
        .navigationTitle("Compression")
    }

    private func run() {
        isLoading = true; error = nil
        Task {
            defer { isLoading = false }
            do { result = try await server.compressText(input) }
            catch { self.error = error.localizedDescription }
        }
    }
}

// MARK: - Regex playground

struct RegexPlayground: View {
    @EnvironmentObject private var server: ServerController
    @State private var input = "Hello, .NET 9 is fast and AOT-safe!"
    @State private var result: RegexResult?
    @State private var isLoading = false
    @State private var error: String?

    var body: some View {
        Form {
            Section("Input") {
                TextField("Text to match", text: $input)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                Button(action: run) {
                    HStack { Text("Match words"); if isLoading { Spacer(); ProgressView() } }
                }
                .disabled(isLoading || input.isEmpty || !server.isRunning)
            }
            if let r = result {
                Section("Result — \(r.matchCount) words") {
                    ForEach(r.words, id: \.self) { word in
                        Text(word).font(.system(.body, design: .monospaced))
                    }
                }
            }
            if let error { Section("Error") { Text(error).foregroundStyle(.red) } }
        }
        .navigationTitle("Regex ([GeneratedRegex])")
    }

    private func run() {
        isLoading = true; error = nil
        Task {
            defer { isLoading = false }
            do { result = try await server.matchRegex(input: input) }
            catch { self.error = error.localizedDescription }
        }
    }
}

// MARK: - Notes CRUD playground

struct NotesPlayground: View {
    @EnvironmentObject private var server: ServerController
    @State private var notes: [NoteRecord] = []
    @State private var newBody = ""
    @State private var isLoading = false
    @State private var error: String?

    var body: some View {
        Form {
            Section("Add note (SQLite in-memory)") {
                TextField("Note body", text: $newBody)
                    .textInputAutocapitalization(.never)
                Button(action: addNote) {
                    HStack { Text("Save"); if isLoading { Spacer(); ProgressView() } }
                }
                .disabled(isLoading || newBody.isEmpty || !server.isRunning)
            }
            Section("Notes (\(notes.count))") {
                if notes.isEmpty {
                    Text("No notes yet").foregroundStyle(.secondary)
                }
                ForEach(notes) { note in
                    HStack {
                        Text("#\(note.id)").foregroundStyle(.secondary).font(.caption)
                        Text(note.body)
                    }
                }
                .onDelete(perform: deleteNotes)
            }
            if let error { Section("Error") { Text(error).foregroundStyle(.red) } }
        }
        .navigationTitle("Notes — SQLite CRUD")
        .task { await loadNotes() }
        .toolbar {
            EditButton()
        }
    }

    private func loadNotes() async {
        guard server.isRunning else { return }
        do { notes = try await server.fetchNotes() }
        catch { self.error = error.localizedDescription }
    }

    private func addNote() {
        isLoading = true
        Task {
            defer { isLoading = false }
            do {
                _ = try await server.createNote(body: newBody)
                newBody = ""
                notes = try await server.fetchNotes()
            } catch {
                self.error = error.localizedDescription
            }
        }
    }

    private func deleteNotes(at offsets: IndexSet) {
        Task {
            for index in offsets {
                do { try await server.deleteNote(id: notes[index].id) }
                catch { self.error = error.localizedDescription }
            }
            notes = (try? await server.fetchNotes()) ?? notes
        }
    }
}

#Preview {
    PlaygroundView().environmentObject(ServerController.shared)
}
