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
    let id: String
    let category: String
    let title: String
    // Swift property names kept stable for views; wire keys bridged via CodingKeys.
    let detail: String      // wire: "summary"
    let verdict: Verdict    // wire: "expected"
    let mechanism: String   // wire: "mechanism" (was the now-removed `aotNotes`)

    enum CodingKeys: String, CodingKey {
        case id
        case category
        case title
        case detail   = "summary"
        case verdict  = "expected"
        case mechanism
    }
}

struct CapabilityResult: Codable, Identifiable, Sendable {
    let id: String
    let category: String?
    let title: String?
    let verdict: Verdict
    let detail: String?
    let output: JSONValue?       // arbitrary JSON (object/array/null); round-trips, not consumed by views
    let elapsedMs: Double
    let correlationId: String?
    let error: ProblemDetails?   // wire sends a ProblemDetails object or null, not a string
}

// RFC 7807 problem details, as emitted by the .NET backend.
struct ProblemDetails: Codable, Sendable {
    let type: String
    let title: String
    let status: Int
    let detail: String?
    let correlationId: String
    let instance: String?
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
    let timestampUtc: String   // wire: "timestampUtc" (was mis-named `timestamp`)
    let level: String
    let category: String
    let eventId: Int
    let message: String
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

// MARK: - Arbitrary JSON

/// Minimal `Codable` representation of an arbitrary JSON value, used for fields
/// (e.g. CapabilityResult.output) whose shape is server-defined. Round-trips
/// faithfully; views don't currently read it.
enum JSONValue: Codable, Sendable {
    case string(String)
    case number(Double)
    case bool(Bool)
    case object([String: JSONValue])
    case array([JSONValue])
    case null

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let v = try? container.decode(Bool.self) {
            self = .bool(v)
        } else if let v = try? container.decode(Double.self) {
            self = .number(v)
        } else if let v = try? container.decode(String.self) {
            self = .string(v)
        } else if let v = try? container.decode([String: JSONValue].self) {
            self = .object(v)
        } else if let v = try? container.decode([JSONValue].self) {
            self = .array(v)
        } else {
            throw DecodingError.dataCorruptedError(
                in: container,
                debugDescription: "Unsupported JSON value")
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .string(let v): try container.encode(v)
        case .number(let v): try container.encode(v)
        case .bool(let v):   try container.encode(v)
        case .object(let v): try container.encode(v)
        case .array(let v):  try container.encode(v)
        case .null:          try container.encodeNil()
        }
    }
}

// MARK: - JSONValue display helpers (consumed by CapabilityDetailView)

extension JSONValue {
    /// Human-readable leaf rendering for key/value detail rows.
    var displayString: String {
        switch self {
        case .string(let s): return s
        case .bool(let b):   return b ? "true" : "false"
        case .number(let n): return n == n.rounded() ? String(Int(n)) : String(n)
        case .null:          return "null"
        case .object:        return "{…}"
        case .array(let a):  return "[\(a.count)]"
        }
    }

    /// Bool payload when this value is a JSON boolean, else nil — lets a view show
    /// a check/x icon for proof flags (e.g. SQLCipher wrongKeyRejected).
    var boolValue: Bool? {
        if case .bool(let b) = self { return b }
        return nil
    }

    /// Sorted key/value pairs when this is a JSON object, else nil.
    var objectPairs: [(key: String, value: JSONValue)]? {
        guard case .object(let dict) = self else { return nil }
        return dict.sorted { $0.key < $1.key }.map { (key: $0.key, value: $0.value) }
    }
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
