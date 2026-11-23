import SwiftUI

public enum LibraryTab: String, CaseIterable {
    case albums = "Albums"
    case artists = "Artists"
    case folders = "Folders"
    case playlists = "Playlists"
}

public struct LibraryView: View {
    @State private var selectedTab: LibraryTab = .albums
    @State private var albums: [AlbumDto] = []
    @State private var artists: [ArtistDto] = []
    @State private var folders: [FolderNodeDto] = []
    @State private var playlists: [PlaylistDto] = []
    @State private var isLoading: Bool = false
    @State private var isAlphabetical: Bool = true

    public var body: some View {
        NavigationView {
            ZStack {
                AppTheme.background.ignoresSafeArea()

                VStack(spacing: 12) {
                    // Header & User Avatar
                    HStack {
                        Text("Your Library")
                            .font(.system(size: 28, weight: .bold))
                            .foregroundColor(AppTheme.textPrimary)

                        Spacer()

                        Button(action: {
                            isAlphabetical.toggle()
                        }) {
                            HStack(spacing: 4) {
                                Image(systemName: "arrow.up.arrow.down")
                                Text(isAlphabetical ? "A-Z" : "Recent")
                            }
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(isAlphabetical ? AppTheme.primary : AppTheme.textSecondary)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 6)
                            .background(AppTheme.card)
                            .cornerRadius(12)
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.top, 16)

                    // Filter Pills
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 8) {
                            ForEach(LibraryTab.allCases, id: \.self) { tab in
                                Button(action: {
                                    selectedTab = tab
                                    loadTabData()
                                }) {
                                    Text(tab.rawValue)
                                        .font(.system(size: 13, weight: .semibold))
                                        .foregroundColor(selectedTab == tab ? Color.black : AppTheme.textPrimary)
                                        .padding(.horizontal, 16)
                                        .padding(.vertical, 8)
                                        .background(selectedTab == tab ? AppTheme.primary : AppTheme.card)
                                        .cornerRadius(20)
                                }
                            }
                        }
                        .padding(.horizontal, 16)
                    }

                    // Content
                    if isLoading && albums.isEmpty && artists.isEmpty && folders.isEmpty {
                        Spacer()
                        ProgressView().progressViewStyle(CircularProgressViewStyle(tint: AppTheme.primary))
                        Spacer()
                    } else {
                        ScrollView {
                            LazyVStack(spacing: 12) {
                                switch selectedTab {
                                case .albums:
                                    albumsGrid
                                case .artists:
                                    artistsList
                                case .folders:
                                    foldersList
                                case .playlists:
                                    playlistsGrid
                                }

                                Spacer(minLength: 80)
                            }
                            .padding(.top, 8)
                        }
                        .refreshable {
                            await refreshDataAsync()
                        }
                    }
                }
            }
            .navigationBarHidden(true)
        }
        .onAppear {
            loadTabData()
        }
    }

    // MARK: - Albums Grid

    private var albumsGrid: some View {
        let columns = [
            GridItem(.flexible(), spacing: 16),
            GridItem(.flexible(), spacing: 16)
        ]
        let sortedAlbums = isAlphabetical
            ? albums.sorted { $0.title.localizedCaseInsensitiveCompare($1.title) == .orderedAscending }
            : albums

        return LazyVGrid(columns: columns, spacing: 16) {
            ForEach(sortedAlbums) { album in
                NavigationLink(destination: AlbumDetailView(album: album)) {
                    VStack(alignment: .leading, spacing: 8) {
                        ArtworkAsyncImage(artworkPath: album.artworkUrl, size: (UIScreen.main.bounds.width - 48) / 2, cornerRadius: 8)

                        Text(album.title)
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(AppTheme.textPrimary)
                            .lineLimit(1)

                        Text("\(album.displayArtist) • \(album.trackCount) songs")
                            .font(.system(size: 12))
                            .foregroundColor(AppTheme.textSecondary)
                            .lineLimit(1)
                    }
                }
                .buttonStyle(PlainButtonStyle())
            }
        }
        .padding(.horizontal, 16)
    }

    // MARK: - Artists List

    private var artistsList: some View {
        let sortedArtists = isAlphabetical
            ? artists.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            : artists

        return ForEach(sortedArtists) { artist in
            NavigationLink(destination: ArtistDetailView(artist: artist)) {
                HStack(spacing: 14) {
                    ArtworkAsyncImage(artworkPath: artist.artworkUrl, size: 56, cornerRadius: 28)

                    VStack(alignment: .leading, spacing: 4) {
                        Text(artist.name)
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundColor(AppTheme.textPrimary)

                        Text("Artist • \(artist.trackCount) songs")
                            .font(.system(size: 13))
                            .foregroundColor(AppTheme.textSecondary)
                    }

                    Spacer()

                    Image(systemName: "chevron.right")
                        .font(.system(size: 12))
                        .foregroundColor(AppTheme.textSecondary)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 4)
            }
            .buttonStyle(PlainButtonStyle())
        }
    }

    // MARK: - Folders List

    private var foldersList: some View {
        let sortedFolders = isAlphabetical
            ? folders.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            : folders

        return ForEach(sortedFolders) { folder in
            NavigationLink(destination: FolderDetailView(folder: folder)) {
                HStack(spacing: 14) {
                    ZStack {
                        AppTheme.card
                        Image(systemName: "folder.fill")
                            .font(.system(size: 24))
                            .foregroundColor(AppTheme.primary)
                    }
                    .frame(width: 52, height: 52)
                    .cornerRadius(8)

                    VStack(alignment: .leading, spacing: 4) {
                        Text(folder.name)
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundColor(AppTheme.textPrimary)

                        Text("\(folder.trackCount) tracks")
                            .font(.system(size: 12))
                            .foregroundColor(AppTheme.textSecondary)
                    }

                    Spacer()

                    Image(systemName: "chevron.right")
                        .font(.system(size: 12))
                        .foregroundColor(AppTheme.textSecondary)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 4)
            }
            .buttonStyle(PlainButtonStyle())
        }
    }

    // MARK: - Playlists Grid

    private var playlistsGrid: some View {
        ForEach(playlists) { playlist in
            HStack(spacing: 14) {
                ArtworkAsyncImage(artworkPath: playlist.artworkUrl, size: 52, cornerRadius: 8)

                VStack(alignment: .leading, spacing: 4) {
                    Text(playlist.name)
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundColor(AppTheme.textPrimary)

                    Text("Playlist • \(playlist.trackCount) tracks")
                        .font(.system(size: 12))
                        .foregroundColor(AppTheme.textSecondary)
                }

                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 4)
        }
    }

    // MARK: - Data Loaders

    private func loadTabData() {
        isLoading = true
        Task {
            await refreshDataAsync()
        }
    }

    private func refreshDataAsync() async {
        do {
            switch selectedTab {
            case .albums:
                let res = try await NetworkService.shared.fetchAlbums()
                await MainActor.run { self.albums = res.items; self.isLoading = false }
            case .artists:
                let res = try await NetworkService.shared.fetchArtists()
                await MainActor.run { self.artists = res.items; self.isLoading = false }
            case .folders:
                let res = try await NetworkService.shared.fetchFolders()
                await MainActor.run { self.folders = res; self.isLoading = false }
            case .playlists:
                let res = try await NetworkService.shared.fetchPlaylists()
                await MainActor.run { self.playlists = res; self.isLoading = false }
            }
        } catch {
            await MainActor.run { self.isLoading = false }
        }
    }
}
