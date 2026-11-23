import SwiftUI

public struct TrackRowView: View {
    public let track: TrackDto
    public let isCurrentlyPlaying: Bool
    public var onPlay: () -> Void
    public var onFavoriteToggle: ((Bool) -> Void)? = nil

    @State private var isFav: Bool

    public init(
        track: TrackDto,
        isCurrentlyPlaying: Bool,
        onPlay: @escaping () -> Void,
        onFavoriteToggle: ((Bool) -> Void)? = nil
    ) {
        self.track = track
        self.isCurrentlyPlaying = isCurrentlyPlaying
        self.onPlay = onPlay
        self.onFavoriteToggle = onFavoriteToggle
        self._isFav = State(initialValue: track.isFavorite)
    }

    public var body: some View {
        HStack(spacing: 12) {
            ArtworkAsyncImage(artworkPath: track.artworkUrl, size: 48, cornerRadius: 6)

            VStack(alignment: .leading, spacing: 4) {
                Text(track.title)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(isCurrentlyPlaying ? AppTheme.primary : AppTheme.textPrimary)
                    .lineLimit(1)

                Text(track.artist + (track.album.isEmpty ? "" : " • \(track.album)"))
                    .font(.system(size: 13))
                    .foregroundColor(AppTheme.textSecondary)
                    .lineLimit(1)
            }

            Spacer()

            Button(action: {
                isFav.toggle()
                onFavoriteToggle?(isFav)
                Task {
                    try? await NetworkService.shared.toggleFavorite(trackId: track.id, isFavorite: isFav)
                }
            }) {
                Image(systemName: isFav ? "heart.fill" : "heart")
                    .font(.system(size: 18))
                    .foregroundColor(isFav ? AppTheme.primary : AppTheme.textSecondary.opacity(0.6))
                    .padding(8)
            }
            .buttonStyle(PlainButtonStyle())

            Text(track.formattedDuration)
                .font(.system(size: 12))
                .foregroundColor(AppTheme.textSecondary)
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
        .onTapGesture {
            onPlay()
        }
    }
}
