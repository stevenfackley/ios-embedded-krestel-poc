// ServerController.swift — ObservableObject that owns the embedded server lifecycle
// and provides typed URLSession API calls used by all views.
//
// Thread model: @MainActor. All @Published mutations happen on the main actor.
// Network `await`s suspend without blocking — the main run loop stays live.

import Foundation

@MainActor
final class ServerController: ObservableObject {
    static let shared = ServerController()

    @Published private(set) var isRunning = false
    @Published private(set) var boundPort = 0
    @Published private(set) var statusMessage = "Not started"

    private let session: URLSession

    private init() {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest  = 10
        cfg.waitsForConnectivity       = false
        session = URLSession(configuration: cfg)
    }

    // MARK: - Lifecycle

    func startIfNeeded() async {
        guard !isRunning else { return }
        statusMessage = "Starting…"
        let rc = kestrel_start(0)   // 0 = OS-assigned ephemeral port
        guard rc == 0 else {
            statusMessage = "Start failed (rc=\(rc))"
            return
        }
        boundPort = discoverPort()
        isRunning = boundPort > 0
        statusMessage = isRunning ? "Running on port \(boundPort)" : "Started — port unknown"
    }

    func stop() {
        kestrel_stop()
        isRunning     = false
        boundPort     = 0
        statusMessage = "Stopped"
    }

    // MARK: - Port discovery

    private func discoverPort() -> Int {
        var buf = [UInt8](repeating: 0, count: 1024)
        let written = buf.withUnsafeMutableBufferPointer {
            kestrel_info($0.baseAddress, Int32($0.count))
        }
        guard written > 0 else { return 0 }
        guard let info = try? decoder.decode(
            DiagInfoResponse.self, from: Data(buf.prefix(Int(written))))
        else { return 0 }
        return info.port
    }

    // MARK: - Shared URL / decoder helpers

    private var decoder: JSONDecoder { JSONDecoder() }

    private func url(_ path: String) throws -> URL {
        guard isRunning else { throw APIError.serverNotRunning }
        guard let u = URL(string: "http://127.0.0.1:\(boundPort)\(path)") else {
            throw APIError.badURL
        }
        return u
    }

    private func get<T: Decodable>(_ path: String) async throws -> T {
        let data = try await session.data(from: try url(path)).0
        return try decoder.decode(T.self, from: data)
    }

    private func post<T: Decodable>(_ path: String, body: Data? = nil) async throws -> T {
        var req = URLRequest(url: try url(path))
        req.httpMethod = "POST"
        req.httpBody   = body
        let data = try await session.data(for: req).0
        return try decoder.decode(T.self, from: data)
    }

    private func postVoid(_ path: String) async throws {
        var req = URLRequest(url: try url(path))
        req.httpMethod = "POST"
        _ = try await session.data(for: req)
    }

    private func delete(_ path: String) async throws {
        var req = URLRequest(url: try url(path))
        req.httpMethod = "DELETE"
        _ = try await session.data(for: req)
    }

    // MARK: - Capabilities API

    func fetchDescriptors() async throws -> [CapabilityDescriptor] {
        try await get("/api/capabilities")
    }

    func runProbe(_ id: String) async throws -> CapabilityResult {
        let encoded = id.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? id
        return try await post("/api/capabilities/\(encoded)/run")
    }

    func runAll() async throws -> [CapabilityResult] {
        try await post("/api/capabilities/run-all")
    }

    // MARK: - Diagnostics API

    func fetchDiagInfo() async throws -> DiagInfoResponse {
        try await get("/api/diag/info")
    }

    func fetchLogs() async throws -> [LogEntry] {
        try await get("/api/diag/logs")
    }

    // MARK: - Playground APIs

    func hashText(_ input: String) async throws -> CryptoHashResult {
        let q = input.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? input
        return try await post("/api/crypto/hash?input=\(q)")
    }

    func compressText(_ input: String) async throws -> CompressResult {
        let q = input.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? input
        return try await post("/api/compress?input=\(q)")
    }

    func matchRegex(input: String) async throws -> RegexResult {
        let q = input.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? input
        return try await post("/api/regex?input=\(q)")
    }

    // Notes CRUD
    func fetchNotes() async throws -> [NoteRecord] {
        try await get("/api/notes")
    }

    func createNote(body: String) async throws -> NoteRecord {
        let q = body.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? body
        return try await post("/api/notes?body=\(q)")
    }

    func deleteNote(id: Int) async throws {
        try await delete("/api/notes/\(id)")
    }
}
