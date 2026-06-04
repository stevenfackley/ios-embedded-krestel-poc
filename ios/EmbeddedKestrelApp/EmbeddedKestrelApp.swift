// EmbeddedKestrelApp.swift — App entry point.
// Boots the embedded .NET server on the main actor immediately; the async
// suspension in startIfNeeded() keeps the main run loop live during the bind.

import SwiftUI

@main
struct EmbeddedKestrelApp: App {
    private let server = ServerController.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(server)
                .task {
                    // First .task fires before the first render on most devices;
                    // async suspension lets SwiftUI complete layout without blocking.
                    await server.startIfNeeded()
                }
        }
    }
}
