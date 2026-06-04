// CapabilitiesViewModel.swift — shared state for the Capabilities TOC and the
// per-capability detail pages it drills into.
//
// Held as @StateObject by CapabilitiesView and passed by reference to each
// CapabilityDetailView, so a probe run started on the detail page updates the
// same `results` the list reads — and the result survives popping back.

import Foundation

@MainActor
final class CapabilitiesViewModel: ObservableObject {
    @Published var descriptors: [CapabilityDescriptor] = []
    @Published var results: [String: CapabilityResult] = [:]
    @Published var running: Set<String> = []
    @Published var filter: Verdict? = nil
    @Published var isLoading = false
    @Published var errorMessage: String?

    /// Descriptors grouped into ordered (category, items) sections, after the
    /// optional verdict filter. Category order follows first appearance.
    /// Unlabeled tuple + `id: \.0` mirrors the original list (tuple-element key
    /// paths are only reliable in that proven form).
    var grouped: [(String, [CapabilityDescriptor])] {
        let shown = filter == nil ? descriptors : descriptors.filter { $0.verdict == filter }
        return shown.map(\.category).uniqued().map { cat in
            (cat, shown.filter { $0.category == cat })
        }
    }

    func isRunning(_ id: String) -> Bool { running.contains(id) }

    func load(using server: ServerController) async {
        guard server.isRunning else { return }
        isLoading = true
        defer { isLoading = false }
        do {
            descriptors = try await server.fetchDescriptors()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func runProbe(_ id: String, using server: ServerController) async {
        running.insert(id)
        defer { running.remove(id) }
        do {
            results[id] = try await server.runProbe(id)
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

// MARK: - Uniqued helper

extension Array where Element: Hashable {
    /// First-occurrence-preserving de-dup, used to derive ordered category lists.
    func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}
