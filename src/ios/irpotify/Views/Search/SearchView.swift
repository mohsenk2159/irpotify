import SwiftUI

public struct SearchView: View {
    @State private var query: String = ""
    @State private var searchResults: SearchResponse? = nil
    @State private var isLoading: Bool = false
    @State private var searchTask: Task<Void, Never>? = nil

    public var body: some View {
        NavigationView {
            ZStack {
                AppTheme.background.ignoresSafeArea()

                VStack(spacing: 16) {
                    // Header
                    HStack {
                        Text("Search")
                            .font(.system(size: 28, weight: .bold))
                            .foregroundColor(AppTheme.textPrimary)
                        Spacer()
                    }
                    .padding(.horizontal, 16)
                    .padding(.top, 16)

                    // Search Bar
                    HStack {
                        Image(systemName: "magnifyingglass")
                            .foregroundColor(AppTheme.textSecondary)
                        TextField("What do you want to listen to?", text: $query)
                            .foregroundColor(AppTheme.textPrimary)
                            .disableAutocorrection(true)
                            .onChange(of: query) { newQuery in
                                performSearch(query: newQuery)
                            }
                        if !query.isEmpty {
                            Button(action: { query = ""; searchResults = nil }) {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundColor(AppTheme.textSecondary)
                            }
                        }
                    }
                    .padding(12)
                    .background(AppTheme.card)
                    .cornerRadius(8)
                    .padding(.horizontal, 16)

                    if isLoading {
                        Spacer()
                        ProgressView().progressViewStyle(CircularProgressViewStyle(tint: AppTheme.primary))
                        Spacer()
                    } else if let results = searchResults {
                        ScrollView {
                            VStack(alignment: .leading, spacing: 24) {
                                // Tracks
                                if !results.tracks.isEmpty {
                                    VStack(alignment: .leading, spacing: 8) {
                                        Text("Songs")
                                            .font(.system(size: 18, weight: .bold))
                                            .foregroundColor(AppTheme.textPrimary)
                                            .padding(.horizontal, 16)

                                        ForEach(results.tracks) { track in
                                            TrackRowView(
                                                track: track,
                                                isCurrentlyPlaying: AudioPlayerService.shared.currentTrack?.id == track.id,
                                                onPlay: {
                                                    AudioPlayerService.shared.playTrack(track, in: results.tracks)
                                                }
                                            )
                                            .padding(.horizontal, 16)
                                        }
                                    }
                                }

                                // Albums
                                if !results.albums.isEmpty {
                                    VStack(alignment: .leading, spacing: 12) {
                                        Text("Albums")
                                            .font(.system(size: 18, weight: .bold))
                                            .foregroundColor(AppTheme.textPrimary)
                                            .padding(.horizontal, 16)

                                        ScrollView(.horizontal, showsIndicators: false) {
                                            HStack(spacing: 16) {
                                                ForEach(results.albums) { album in
                                                    NavigationLink(destination: AlbumDetailView(album: album)) {
                                                        VStack(alignment: .leading, spacing: 8) {
                                                            ArtworkAsyncImage(artworkPath: album.artworkUrl, size: 120, cornerRadius: 8)
                                                            Text(album.title)
                                                                .font(.system(size: 13, weight: .semibold))
                                                                .foregroundColor(AppTheme.textPrimary)
                                                                .lineLimit(1)
                                                            Text(album.displayArtist)
                                                                .font(.system(size: 11))
                                                                .foregroundColor(AppTheme.textSecondary)
                                                                .lineLimit(1)
                                                        }
                                                        .frame(width: 120)
                                                    }
                                                    .buttonStyle(PlainButtonStyle())
                                                }
                                            }
                                            .padding(.horizontal, 16)
                                        }
                                    }
                                }

                                // Artists
                                if !results.artists.isEmpty {
                                    VStack(alignment: .leading, spacing: 12) {
                                        Text("Artists")
                                            .font(.system(size: 18, weight: .bold))
                                            .foregroundColor(AppTheme.textPrimary)
                                            .padding(.horizontal, 16)

                                        ScrollView(.horizontal, showsIndicators: false) {
                                            HStack(spacing: 16) {
                                                ForEach(results.artists) { artist in
                                                    NavigationLink(destination: ArtistDetailView(artist: artist)) {
                                                        VStack(spacing: 8) {
                                                            ArtworkAsyncImage(artworkPath: artist.artworkUrl, size: 100, cornerRadius: 50)
                                                            Text(artist.name)
                                                                .font(.system(size: 13, weight: .semibold))
                                                                .foregroundColor(AppTheme.textPrimary)
                                                                .lineLimit(1)
                                                        }
                                                        .frame(width: 100)
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
                    } else {
                        Spacer()
                        VStack(spacing: 12) {
                            Image(systemName: "music.note.magnifyingglass")
                                .font(.system(size: 48))
                                .foregroundColor(AppTheme.textSecondary.opacity(0.5))
                            Text("Find songs, artists and albums")
                                .font(.system(size: 16))
                                .foregroundColor(AppTheme.textSecondary)
                        }
                        Spacer()
                    }
                }
            }
            .navigationBarHidden(true)
        }
    }

    private func performSearch(query: String) {
        searchTask?.cancel()
        let trimmed = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            searchResults = nil
            isLoading = false
            return
        }

        isLoading = true
        searchTask = Task {
            try? await Task.sleep(nanoseconds: 300_000_000) // 300ms debounce
            guard !Task.isCancelled else { return }

            do {
                let res = try await NetworkService.shared.search(query: trimmed)
                await MainActor.run {
                    self.searchResults = res
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
