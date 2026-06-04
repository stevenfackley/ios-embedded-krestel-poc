// ContentView.swift — TabView host for the capability explorer app.

import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var server: ServerController

    var body: some View {
        TabView {
            DashboardView()
                .tabItem { Label("Dashboard", systemImage: "square.grid.2x2") }

            CapabilitiesView()
                .tabItem { Label("Capabilities", systemImage: "checkmark.seal") }

            PlaygroundView()
                .tabItem { Label("Playground", systemImage: "play.circle") }

            DiagnosticsView()
                .tabItem { Label("Diagnostics", systemImage: "waveform.path.ecg") }

            AboutView()
                .tabItem { Label("About", systemImage: "info.circle") }
        }
    }
}

#Preview {
    ContentView()
        .environmentObject(ServerController.shared)
}
