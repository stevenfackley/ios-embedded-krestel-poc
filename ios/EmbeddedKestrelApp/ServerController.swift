// ServerController.swift
//
// Bridges the Swift world to the embedded .NET server in two layers:
//   1. lifecycle  — calls the native kestrel_start / kestrel_stop entry points
//   2. transport  — a URLSession client that talks to the loopback endpoint
//
// kestrel_start is synchronous on the native side and returns only once the
// listener is bound (see KestrelHost.Start / RawHttpHost.Start), so once
// startIfNeeded() reports success the server is guaranteed to be accepting.

import Foundation

/// Decoded shape of the JSON the server emits. The keys are camelCase because
/// ApiJsonContext is configured with JsonKnownNamingPolicy.CamelCase on the
/// .NET side — keep these property names identical to the wire contract.
struct ProcessResponse: Decodable, Sendable {
    let input: String
    let hash: String
    let length: Int
    let processedAtUtc: String
}

/// Errors surfaced to the UI layer. LocalizedError gives us a clean message to
/// render without leaking transport details.
enum ServerError: LocalizedError {
    case notStarted
    case badURL
    case unexpectedStatus(Int)

    var errorDescription: String? {
        switch self {
        case .notStarted:
            return "The embedded server failed to start."
        case .badURL:
            return "Could not construct the request URL."
        case .unexpectedStatus(let code):
            return "Server returned HTTP \(code)."
        }
    }
}

/// Owns the embedded server's lifetime and provides the typed client.
/// `@unchecked Sendable` because the only mutable state (`started`) is guarded
/// by the `stateLock` below; everything else is immutable.
final class ServerController: @unchecked Sendable {
    static let shared = ServerController()

    /// Loopback port the native host binds. Int32 matches the C `int` argument.
    let port: Int32 = 5001

    private let session: URLSession
    private let stateLock = NSLock()
    private var started = false

    private init() {
        // Short timeouts: this is an in-process server on loopback, so a slow
        // response means something is wrong rather than a flaky network.
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 5
        config.waitsForConnectivity = false
        self.session = URLSession(configuration: config)
    }

    /// Boots the embedded server exactly once. Returns true if it is running.
    /// Call this off the main thread during launch — the native side does a
    /// brief synchronous bind before returning.
    @discardableResult
    func startIfNeeded() -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }

        if started {
            return true
        }

        // 0 == success per the NativeBootstrap contract; anything else is failure.
        let rc = kestrel_start(port)
        started = (rc == 0)
        return started
    }

    /// Stops the embedded server if it is running.
    func stop() {
        stateLock.lock()
        defer { stateLock.unlock() }

        guard started else { return }
        kestrel_stop()
        started = false
    }

    /// Round-trips `input` through the server → LegacyLib.DataProcessor → JSON.
    /// This is the line that proves the whole thesis: a netstandard2.0 type
    /// reached from Swift over in-process HTTP with no rewrite.
    func fetchProcessed(input: String) async throws -> ProcessResponse {
        guard startIfNeeded() else {
            throw ServerError.notStarted
        }

        var components = URLComponents()
        components.scheme = "http"
        components.host = "127.0.0.1"
        components.port = Int(port)
        components.path = "/api/process"
        components.queryItems = [URLQueryItem(name: "input", value: input)]

        guard let url = components.url else {
            throw ServerError.badURL
        }

        let (data, response) = try await session.data(from: url)

        if let http = response as? HTTPURLResponse,
           !(200...299).contains(http.statusCode) {
            throw ServerError.unexpectedStatus(http.statusCode)
        }

        return try JSONDecoder().decode(ProcessResponse.self, from: data)
    }
}
