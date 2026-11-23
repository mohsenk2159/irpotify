import SwiftUI

public struct MainTabView: View {
    @State private var selectedTab: Int = 0
    @State private var isNowPlayingExpanded: Bool = false
    @ObservedObject var player = AudioPlayerService.shared

    public init() {
        // Dark translucent tab bar styling
        let appearance = UITabBarAppearance()
        appearance.configureWithOpaqueBackground()
        appearance.backgroundColor = UIColor(Color(hex: "121212").opacity(0.95))
        UITabBar.appearance().standardAppearance = appearance
        UITabBar.appearance().scrollEdgeAppearance = appearance
    }

    public var body: some View {
        ZStack(alignment: .bottom) {
            TabView(selection: $selectedTab) {
                HomeView()
                    .tabItem {
                        Label("Home", systemImage: "house.fill")
                    }
                    .tag(0)

                SearchView()
                    .tabItem {
                        Label("Search", systemImage: "magnifyingglass")
                    }
                    .tag(1)

                LibraryView()
                    .tabItem {
                        Label("Your Library", systemImage: "books.vertical.fill")
                    }
                    .tag(2)

                SettingsView()
                    .tabItem {
                        Label("Settings", systemImage: "gearshape.fill")
                    }
                    .tag(3)
            }
            .accentColor(AppTheme.primary)

            // Docked Mini Player right above TabBar
            if player.currentTrack != nil {
                MiniPlayerView(isExpanded: $isNowPlayingExpanded)
                    .padding(.bottom, 54) // right above iOS tab bar
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .animation(.easeInOut(duration: 0.25), value: player.currentTrack)
            }
        }
        .sheet(isPresented: $isNowPlayingExpanded) {
            NowPlayingView()
        }
    }
}
