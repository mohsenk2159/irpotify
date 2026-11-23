import SwiftUI

public struct NowPlayingView: View {
    @ObservedObject var player = AudioPlayerService.shared
    @Environment(\.presentationMode) var presentationMode
    @State private var isSeeking: Bool = false
    @State private var seekPosition: Double = 0.0

    public var body: some View {
        ZStack {
            AppTheme.background.ignoresSafeArea()

            // Subtle gradient background
            LinearGradient(
                colors: [AppTheme.card, AppTheme.background],
                startPoint: .top,
                endPoint: .bottom
            )
            .ignoresSafeArea()

            if let track = player.currentTrack {
                VStack(spacing: 24) {
                    // Top Bar
                    HStack {
                        Button(action: {
                            presentationMode.wrappedValue.dismiss()
                        }) {
                            Image(systemName: "chevron.down")
                                .font(.system(size: 20, weight: .bold))
                                .foregroundColor(AppTheme.textPrimary)
                                .padding()
                        }

                        Spacer()

                        VStack(spacing: 2) {
                            Text("PLAYING FROM LIBRARY")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(AppTheme.textSecondary)
                            Text(track.album.isEmpty ? "irpotify" : track.album)
                                .font(.system(size: 12, weight: .bold))
                                .foregroundColor(AppTheme.textPrimary)
                                .lineLimit(1)
                        }

                        Spacer()

                        Color.clear.frame(width: 44, height: 44)
                    }

                    Spacer()

                    // Large Album Artwork
                    ArtworkAsyncImage(
                        artworkPath: track.artworkUrl,
                        size: UIScreen.main.bounds.width - 64,
                        cornerRadius: 12
                    )
                    .shadow(color: Color.black.opacity(0.6), radius: 20, x: 0, y: 10)

                    Spacer()

                    // Track Title & Artist & Favorite
                    HStack(alignment: .center) {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(track.title)
                                .font(.system(size: 22, weight: .bold))
                                .foregroundColor(AppTheme.textPrimary)
                                .lineLimit(1)

                            Text(track.artist)
                                .font(.system(size: 16))
                                .foregroundColor(AppTheme.textSecondary)
                                .lineLimit(1)
                        }

                        Spacer()

                        Button(action: {
                            var updated = track
                            updated.isFavorite.toggle()
                            player.currentTrack = updated
                            Task {
                                try? await NetworkService.shared.toggleFavorite(trackId: track.id, isFavorite: updated.isFavorite)
                            }
                        }) {
                            Image(systemName: track.isFavorite ? "heart.fill" : "heart")
                                .font(.system(size: 24))
                                .foregroundColor(track.isFavorite ? AppTheme.primary : AppTheme.textSecondary)
                        }
                    }
                    .padding(.horizontal, 24)

                    // Seek Slider & Timers
                    VStack(spacing: 6) {
                        Slider(
                            value: Binding(
                                get: { isSeeking ? seekPosition : player.currentTime },
                                set: { newValue in
                                    isSeeking = true
                                    seekPosition = newValue
                                }
                            ),
                            in: 0...max(player.duration, 1.0),
                            onEditingChanged: { editing in
                                if !editing {
                                    player.seek(to: seekPosition)
                                    isSeeking = false
                                }
                            }
                        )
                        .accentColor(AppTheme.textPrimary)

                        HStack {
                            Text(formatTime(isSeeking ? seekPosition : player.currentTime))
                                .font(.system(size: 12))
                                .foregroundColor(AppTheme.textSecondary)

                            Spacer()

                            Text(formatTime(player.duration))
                                .font(.system(size: 12))
                                .foregroundColor(AppTheme.textSecondary)
                        }
                    }
                    .padding(.horizontal, 24)

                    // Playback Controls
                    HStack(spacing: 32) {
                        // Shuffle
                        Button(action: {
                            player.isShuffle.toggle()
                        }) {
                            Image(systemName: "shuffle")
                                .font(.system(size: 20))
                                .foregroundColor(player.isShuffle ? AppTheme.primary : AppTheme.textSecondary)
                        }

                        // Previous
                        Button(action: {
                            player.previous()
                        }) {
                            Image(systemName: "backward.fill")
                                .font(.system(size: 28))
                                .foregroundColor(AppTheme.textPrimary)
                        }

                        // Play/Pause Big Button
                        Button(action: {
                            player.togglePlayPause()
                        }) {
                            ZStack {
                                Circle()
                                    .fill(AppTheme.textPrimary)
                                    .frame(width: 68, height: 68)

                                Image(systemName: player.isPlaying ? "pause.fill" : "play.fill")
                                    .font(.system(size: 28))
                                    .foregroundColor(Color.black)
                            }
                        }

                        // Next
                        Button(action: {
                            player.next()
                        }) {
                            Image(systemName: "forward.fill")
                                .font(.system(size: 28))
                                .foregroundColor(AppTheme.textPrimary)
                        }

                        // Repeat
                        Button(action: {
                            player.isRepeat.toggle()
                        }) {
                            Image(systemName: "repeat")
                                .font(.system(size: 20))
                                .foregroundColor(player.isRepeat ? AppTheme.primary : AppTheme.textSecondary)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 32)
                }
            }
        }
    }

    private func formatTime(_ seconds: Double) -> String {
        guard seconds.isFinite && !seconds.isNaN else { return "0:00" }
        let total = Int(seconds)
        let min = total / 60
        let sec = total % 60
        return String(format: "%d:%02d", min, sec)
    }
}
