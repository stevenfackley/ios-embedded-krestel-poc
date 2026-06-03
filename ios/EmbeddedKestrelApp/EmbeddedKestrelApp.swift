// EmbeddedKestrelApp.swift
//
// App entry point. Boots the embedded .NET server during launch on a background
// task so the native bind never blocks the main thread / UI. The first call
// into the static library also triggers lazy .NET runtime initialization.

import SwiftUI

@main
struct EmbeddedKestrelApp: App {
    init() {
        // Detached + background priority: the runtime spin-up and socket bind run
        // off the main actor, so SwiftUI keeps rendering the first frame on time.
        Task.detached(priority: .background) {
            _ = ServerController.shared.startIfNeeded()
        }
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
