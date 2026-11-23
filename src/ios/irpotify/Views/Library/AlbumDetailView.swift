import SwiftUI

public struct AlbumDetailView: View {
    public let album: AlbumDto
    @State private var albumDetail: AlbumDetailResponse? = nil
    @State private var isLoading: Bool = false
    @State private var isAlphabetical: Bool = false
    @Environment(\.presentationMode) var presentationMode

    public init(album: AlbumDto) {
        self.album = album
    }

    public var displayedTracks: [TrackDto] {
        guard let detail = albumDetail else { return [] }
        if isAlphabetical {
            return detail.tracks.sorted { $0.title.localizedCaseInsensitiveCompare($1.title) == .orderedAscending }
        } else {
            return detail.tracks
        }
    }

    public var body: some View {
        ZStack {
            AppTheme.background.ignoresSafeArea()

            if isLoading && albumDetail == nil {
                ProgressView().progressViewStyle(CircularProgressViewStyle(tint: AppTheme.primary))
            } else {
                ScrollView {
                    VStack(spacing: 20) {
                        // Artwork & Album Meta
                        ArtworkAsyncImage(artworkPath: album.artworkUrl, size: 200, cornerRadius: 10)
                            .shadow(color: Color.black.opacity(0.5), radius: 12, x: 0, y: 6)
                            .padding(.top, 20)

                        VStack(spacing: 6) {
                            Text(album.title)
                                .font(.system(size: 22, weight: .bold))
                                .foregroundColor(AppTheme.textPrimary)
                                .multilineTextAlignment(.center)

                            Text(album.displayArtist)
                                .font(.system(size: 16, weight: .medium))
                                .foregroundColor(AppTheme.textSecondary)

                            Text("\(album.year != nil ? "\(album.year!) • " : "")\(album.trackCount) songs")
                                .font(.system(size: 13))
                                .foregroundColor(AppTheme.textSecondary)
                        }
                        .padding(.horizontal, 20)

                        // Action Bar: Sort Toggle & Big Play Button
                        HStack {
                            Button(action: {
                                isAlphabetical.toggle()
                            }) {
                                HStack(spacing: 6) {
                                    Image(systemName: "arrow.up.arrow.down")
                                    Text(isAlphabetical ? "Alphabetical (A-Z)" : "Default Order")
                                }
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(isAlphabetical ? AppTheme.primary : AppTheme.textSecondary)
                                .padding(.horizontal, 12)
                                .padding(.vertical, 6)
                                .background(AppTheme.card)
                                .cornerRadius(15)
                            }

                            Spacer()

                            // Play All Button
                            if let detail = albumDetail, !detail.tracks.isEmpty {
                                Button(action: {
                                    if let first = displayedTracks.first {
                                        AudioPlayerService.shared.playTrack(first, in: displayedTracks)
                                    }
                                }) {
                                    ZStack {
                                        Circle()
                                            .fill(AppTheme.primary)
                                            .frame(width: 52, height: 52)
                                        Image(systemName: "play.fill")
                                            .font(.system(size: 22))
                                            .foregroundColor(Color.black)
                                    }
                                }
                            }
                        }
                        .padding(.horizontal, 20)

                        // Track List
                        LazyVStack(spacing: 4) {
                            ForEach(displayedTracks) { track in
                                TrackRowView(
                                    track: track,
                                    isCurrentlyPlaying: AudioPlayerService.shared.currentTrack?.id == track.id,
                                    onPlay: {
                                        AudioPlayerService.shared.playTrack(track, in: displayedTracks)
                                    }
                                )
                                .padding(.horizontal, 16)
                            }
                        }

                        Spacer(minLength: 80)
                    }
                }
            }
        }
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            loadAlbumDetail()
        }
    }

    private func loadAlbumDetail() {
        isLoading = true
        Task {
            do {
                let res = try await NetworkService.shared.fetchAlbumDetail(albumId: album.id)
                await MainActor.run {
                    self.albumDetail = res
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
