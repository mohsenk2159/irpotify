import SwiftUI

public struct MiniPlayerView: View {
    @ObservedObject var player = AudioPlayerService.shared
    @Binding var isExpanded: Bool

    public init(isExpanded: Binding<Bool>) {
        self._isExpanded = isExpanded
    }

    public var body: some View {
        if let track = player.currentTrack {
            VStack(spacing: 0) {
                // Progress bar on top of the mini player
                GeometryReader { geo in
                    let progress = player.duration > 0 ? (player.currentTime / player.duration) : 0.0
                    ZStack(alignment: .leading) {
                        Rectangle()
                            .fill(AppTheme.cardElevated)
                        Rectangle()
                            .fill(AppTheme.primary)
                            .frame(width: geo.size.width * CGFloat(progress))
                    }
                }
                .frame(height: 2)

                HStack(spacing: 12) {
                    ArtworkAsyncImage(artworkPath: track.artworkUrl, size: 42, cornerRadius: 6)

                    VStack(alignment: .leading, spacing: 2) {
                        Text(track.title)
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(AppTheme.textPrimary)
                            .lineLimit(1)

                        Text(track.artist)
                            .font(.system(size: 12))
                            .foregroundColor(AppTheme.textSecondary)
                            .lineLimit(1)
                    }

                    Spacer()

                    // Play/Pause Button
                    Button(action: {
                        player.togglePlayPause()
                    }) {
                        Image(systemName: player.isPlaying ? "pause.fill" : "play.fill")
                            .font(.system(size: 20))
                            .foregroundColor(AppTheme.textPrimary)
                            .frame(width: 36, height: 36)
                    }
                    .buttonStyle(PlainButtonStyle())

                    // Next Button
                    Button(action: {
                        player.next()
                    }) {
                        Image(systemName: "forward.fill")
                            .font(.system(size: 18))
                            .foregroundColor(AppTheme.textSecondary)
                            .frame(width: 36, height: 36)
                    }
                    .buttonStyle(PlainButtonStyle())
                }
                .padding(.horizontal, 12)
                .frame(height: 56)
            }
            .background(AppTheme.card.opacity(0.98))
            .cornerRadius(8)
            .padding(.horizontal, 8)
            .shadow(color: Color.black.opacity(0.4), radius: 6, x: 0, y: 3)
            .contentShape(Rectangle())
            .onTapGesture {
                isExpanded = true
            }
        }
    }
}
