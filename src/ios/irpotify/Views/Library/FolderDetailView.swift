import SwiftUI

public struct FolderDetailView: View {
    public let folder: FolderNodeDto
    @State private var tracks: [TrackDto] = []
    @State private var isLoading: Bool = false

    public init(folder: FolderNodeDto) {
        self.folder = folder
    }

    public var body: some View {
        ZStack {
            AppTheme.background.ignoresSafeArea()

            if isLoading && tracks.isEmpty {
                ProgressView().progressViewStyle(CircularProgressViewStyle(tint: AppTheme.primary))
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 20) {
                        // Header
                        HStack(spacing: 16) {
                            ZStack {
                                AppTheme.card
                                Image(systemName: "folder.fill")
                                    .font(.system(size: 36))
                                    .foregroundColor(AppTheme.primary)
                            }
                            .frame(width: 64, height: 64)
                            .cornerRadius(10)

                            VStack(alignment: .leading, spacing: 4) {
                                Text(folder.name)
                                    .font(.system(size: 20, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)

                                Text("\(folder.trackCount) tracks")
                                    .font(.system(size: 13))
                                    .foregroundColor(AppTheme.textSecondary)
                            }

                            Spacer()

                            if !tracks.isEmpty {
                                Button(action: {
                                    if let first = tracks.first {
                                        AudioPlayerService.shared.playTrack(first, in: tracks)
                                    }
                                }) {
                                    Image(systemName: "play.circle.fill")
                                        .font(.system(size: 44))
                                        .foregroundColor(AppTheme.primary)
                                }
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.top, 16)

                        // Subfolders
                        if let children = folder.children, !children.isEmpty {
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Subfolders")
                                    .font(.system(size: 16, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)
                                    .padding(.horizontal, 16)

                                ForEach(children) { sub in
                                    NavigationLink(destination: FolderDetailView(folder: sub)) {
                                        HStack {
                                            Image(systemName: "folder")
                                                .foregroundColor(AppTheme.primary)
                                            Text(sub.name)
                                                .foregroundColor(AppTheme.textPrimary)
                                            Spacer()
                                            Text("\(sub.trackCount)")
                                                .font(.system(size: 12))
                                                .foregroundColor(AppTheme.textSecondary)
                                            Image(systemName: "chevron.right")
                                                .font(.system(size: 12))
                                                .foregroundColor(AppTheme.textSecondary)
                                        }
                                        .padding()
                                        .background(AppTheme.card)
                                        .cornerRadius(8)
                                        .padding(.horizontal, 16)
                                    }
                                }
                            }
                        }

                        // Tracks in Folder
                        if !tracks.isEmpty {
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Tracks")
                                    .font(.system(size: 16, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)
                                    .padding(.horizontal, 16)

                                ForEach(tracks) { track in
                                    TrackRowView(
                                        track: track,
                                        isCurrentlyPlaying: AudioPlayerService.shared.currentTrack?.id == track.id,
                                        onPlay: {
                                            AudioPlayerService.shared.playTrack(track, in: tracks)
                                        }
                                    )
                                    .padding(.horizontal, 16)
                                }
                            }
                        } else if (folder.children?.isEmpty ?? true) {
                            Text("No tracks in this folder.")
                                .foregroundColor(AppTheme.textSecondary)
                                .padding()
                        }

                        Spacer(minLength: 80)
                    }
                }
            }
        }
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            loadFolderTracks()
        }
    }

    private func loadFolderTracks() {
        isLoading = true
        Task {
            do {
                let res = try await NetworkService.shared.fetchTracks(folderId: folder.id, pageSize: 200)
                await MainActor.run {
                    self.tracks = res.items
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
