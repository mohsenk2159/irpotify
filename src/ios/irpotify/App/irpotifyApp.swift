import SwiftUI

@main
struct irpotifyApp: App {
    @StateObject private var session = SessionManager.shared

    var body: some Scene {
        WindowGroup {
            Group {
                if session.isAuthenticated {
                    MainTabView()
                } else {
                    LoginView()
                }
            }
            .preferredColorScheme(.dark)
        }
    }
}
