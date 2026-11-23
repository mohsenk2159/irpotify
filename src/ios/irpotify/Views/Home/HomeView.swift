import SwiftUI

public struct HomeView: View {
    @State private var homeData: HomeResponse? = nil
    @State private var isLoading: Bool = false
    @State private var errorMessage: String? = nil

    public var body: some View {
        NavigationView {
            ZStack {
                AppTheme.background.ignoresSafeArea()

                if isLoading && homeData == nil {
                    ProgressView()
                        .progressViewStyle(CircularProgressViewStyle(tint: AppTheme.primary))
                } else if let err = errorMessage {
                    VStack(spacing: 12) {
                        Text(err)
                            .foregroundColor(AppTheme.textSecondary)
                        Button("Retry") { loadHome() }
                            .foregroundColor(AppTheme.primary)
                    }
                } else {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 28) {
                            // Header Greeting
                            Text(greetingText)
                                .font(.system(size: 24, weight: .bold))
                                .foregroundColor(AppTheme.textPrimary)
                                .padding(.horizontal, 16)
                                .padding(.top, 16)

                            // Recently Added Section
                            if let added = homeData?.recentlyAdded, !added.isEmpty {
                                trackCarousel(title: "Recently Added", tracks: added)
                            }

                            // Recently Played Section
                            if let played = homeData?.recentlyPlayed, !played.isEmpty {
                                trackCarousel(title: "Recently Played", tracks: played)
                            }

                            // Most Played Section
                            if let most = homeData?.mostPlayed, !most.isEmpty {
                                VStack(alignment: .leading, spacing: 12) {
                                    Text("Most Played")
                                        .font(.system(size: 20, weight: .bold))
                                        .foregroundColor(AppTheme.textPrimary)
                                        .padding(.horizontal, 16)

                                    ForEach(most) { track in
                                        TrackRowView(
                                            track: track,
                                            isCurrentlyPlaying: AudioPlayerService.shared.currentTrack?.id == track.id,
                                            onPlay: {
                                                AudioPlayerService.shared.playTrack(track, in: most)
                                            }
                                        )
                                        .padding(.horizontal, 16)
                                    }
                                }
                            }

                            Spacer(minLength: 80) // bottom padding for mini player
                        }
                    }
                    .refreshable {
                        await loadHomeAsync()
                    }
                }
            }
            .navigationBarHidden(true)
        }
        .onAppear {
            loadHome()
        }
    }

    private func trackCarousel(title: String, tracks: [TrackDto]) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title)
                .font(.system(size: 20, weight: .bold))
                .foregroundColor(AppTheme.textPrimary)
                .padding(.horizontal, 16)

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 16) {
                    ForEach(tracks) { track in
                        VStack(alignment: .leading, spacing: 8) {
                            ArtworkAsyncImage(artworkPath: track.artworkUrl, size: 140, cornerRadius: 8)

                            Text(track.title)
                                .font(.system(size: 14, weight: .semibold))
                                .foregroundColor(AppTheme.textPrimary)
                                .lineLimit(1)

                            Text(track.artist)
                                .font(.system(size: 12))
                                .foregroundColor(AppTheme.textSecondary)
                                .lineLimit(1)
                        }
                        .frame(width: 140)
                        .contentShape(Rectangle())
                        .onTapGesture {
                            AudioPlayerService.shared.playTrack(track, in: tracks)
                        }
                    }
                }
                .padding(.horizontal, 16)
            }
        }
    }

    private var greetingText: String {
        let hour = Calendar.current.component(.hour, from: Date())
        if hour < 12 { return "Good morning" }
        if hour < 18 { return "Good afternoon" }
        return "Good evening"
    }

    private func loadHome() {
        isLoading = true
        errorMessage = nil
        Task {
            await loadHomeAsync()
        }
    }

    private func loadHomeAsync() async {
        do {
            let res = try await NetworkService.shared.fetchHome()
            await MainActor.run {
                self.homeData = res
                self.isLoading = false
            }
        } catch {
            await MainActor.run {
                self.errorMessage = error.localizedDescription
                self.isLoading = false
            }
        }
    }
}
