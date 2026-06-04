// Models.swift — Codable API models mirroring the .NET server's JSON contracts.
// Property names match the camelCase JsonKnownNamingPolicy applied server-side.

import SwiftUI

// MARK: - Verdict

enum Verdict: String, Codable, Hashable, Sendable, CaseIterable {
    case works   = "Works"
    case limited = "Limited"
    case fails   = "Fails"

    var color: Color {
        switch self {
        case .works:   return .green
        case .limited: return .orange
        case .fails:   return .red
        }
    }

    var symbol: String {
        switch self {
        case .works:   return "checkmark.circle.fill"
        case .limited: return "exclamationmark.triangle.fill"
        case .fails:   return "xmark.circle.fill"
        }
    }

    var label: String { rawValue }
}

// MARK: - Capability descriptors & results

struct CapabilityDescriptor: Codable, Identifiable, Sendable {
    // Remap `id` JSON key to avoid conflict with Identifiable.id
    let id: String
    let category: String
    let title: String
    let detail: String
    let verdict: Verdict
    let aotNotes: String
}

struct CapabilityResult: Codable, Identifiable, Sendable {
    let id: String
    let category: String?
    let title: String?
    let verdict: Verdict
    let detail: String?
    let elapsedMs: Double
    let correlationId: String?
    let error: String?
    // `output` (JsonElement?) omitted — ignored by JSONDecoder for unknown keys
}

// MARK: - Diagnostics

struct DiagInfoResponse: Codable, Sendable {
    let port: Int
    let dotnetVersion: String
    let hostType: String
    let uptimeSeconds: Double
    let requestsServed: Int
    let os: String
    let processArch: String
}

struct LogEntry: Codable, Identifiable, Sendable {
    let seq: Int
    let level: String
    let category: String
    let message: String
    let timestamp: String
    let correlationId: String?

    var id: Int { seq }
}

// MARK: - Notes (Persistence playground)

struct NoteRecord: Codable, Identifiable, Sendable {
    let id: Int
    let body: String
}

// MARK: - Playground responses

struct CryptoHashResult: Codable, Sendable {
    let input: String
    let algorithm: String
    let hash: String
}

struct CompressResult: Codable, Sendable {
    let algorithm: String
    let rawBytes: Int
    let compressedBytes: Int
    let ratio: Double
}

struct RegexResult: Codable, Sendable {
    let input: String
    let matchCount: Int
    let words: [String]
}

// MARK: - Errors

enum APIError: LocalizedError {
    case serverNotRunning
    case badURL
    case httpError(Int)
    case decodingError(Error)

    var errorDescription: String? {
        switch self {
        case .serverNotRunning:     return "Server is not running — tap Dashboard to start"
        case .badURL:               return "Could not construct request URL"
        case .httpError(let code):  return "Server returned HTTP \(code)"
        case .decodingError(let e): return "Decode error: \(e.localizedDescription)"
        }
    }
}
