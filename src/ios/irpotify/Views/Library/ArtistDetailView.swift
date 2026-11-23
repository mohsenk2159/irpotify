import SwiftUI

public struct ArtistDetailView: View {
    public let artist: ArtistDto
    @State private var artistDetail: ArtistDetailResponse? = nil
    @State private var isLoading: Bool = false

    public init(artist: ArtistDto) {
        self.artist = artist
    }

    public var body: some View {
        ZStack {
            AppTheme.background.ignoresSafeArea()

            if isLoading && artistDetail == nil {
                ProgressView().progressViewStyle(CircularProgressViewStyle(tint: AppTheme.primary))
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 24) {
                        // Header
                        HStack(spacing: 16) {
                            ArtworkAsyncImage(artworkPath: artist.artworkUrl, size: 90, cornerRadius: 45)

                            VStack(alignment: .leading, spacing: 6) {
                                Text(artist.name)
                                    .font(.system(size: 24, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)

                                Text("\(artist.trackCount) tracks • \(artist.albumCount) albums")
                                    .font(.system(size: 13))
                                    .foregroundColor(AppTheme.textSecondary)
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.top, 16)

                        // Top Tracks
                        if let detail = artistDetail, !detail.tracks.isEmpty {
                            VStack(alignment: .leading, spacing: 12) {
                                Text("Popular")
                                    .font(.system(size: 18, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)
                                    .padding(.horizontal, 16)

                                ForEach(detail.tracks) { track in
                                    TrackRowView(
                                        track: track,
                                        isCurrentlyPlaying: AudioPlayerService.shared.currentTrack?.id == track.id,
                                        onPlay: {
                                            AudioPlayerService.shared.playTrack(track, in: detail.tracks)
                                        }
                                    )
                                    .padding(.horizontal, 16)
                                }
                            }
                        }

                        // Albums
                        if let detail = artistDetail, !detail.albums.isEmpty {
                            VStack(alignment: .leading, spacing: 12) {
                                Text("Albums")
                                    .font(.system(size: 18, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)
                                    .padding(.horizontal, 16)

                                ScrollView(.horizontal, showsIndicators: false) {
                                    HStack(spacing: 16) {
                                        ForEach(detail.albums) { alb in
                                            let fullAlbum = AlbumDto(
                                                id: alb.id,
                                                title: alb.title,
                                                albumArtist: artist.name,
                                                artistId: artist.id,
                                                artistName: artist.name,
                                                year: alb.year,
                                                genre: nil,
                                                trackCount: alb.trackCount,
                                                artworkUrl: nil
                                            )
                                            NavigationLink(destination: AlbumDetailView(album: fullAlbum)) {
                                                VStack(alignment: .leading, spacing: 6) {
                                                    ZStack {
                                                        AppTheme.card
                                                        Image(systemName: "opticaldisc")
                                                            .font(.system(size: 40))
                                                            .foregroundColor(AppTheme.textSecondary)
                                                    }
                                                    .frame(width: 130, height: 130)
                                                    .cornerRadius(8)

                                                    Text(alb.title)
                                                        .font(.system(size: 13, weight: .semibold))
                                                        .foregroundColor(AppTheme.textPrimary)
                                                        .lineLimit(1)

                                                    Text(alb.year != nil ? "\(alb.year!)" : "")
                                                        .font(.system(size: 11))
                                                        .foregroundColor(AppTheme.textSecondary)
                                                }
                                                .frame(width: 130)
                                            }
                                            .buttonStyle(PlainButtonStyle())
                                        }
                                    }
                                    .padding(.horizontal, 16)
                                }
                            }
                        }

                        Spacer(minLength: 80)
                    }
                }
            }
        }
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            loadArtistDetail()
        }
    }

    private func loadArtistDetail() {
        isLoading = true
        Task {
            do {
                let res = try await NetworkService.shared.fetchArtistDetail(artistId: artist.id)
                await MainActor.run {
                    self.artistDetail = res
                    self.isLoading = false
                }
            } catch {
                await MainActor.run {
                    self.isLoading = false
                }
            }
        }
    }
}
