import SwiftUI

public struct ArtworkAsyncImage: View {
    public let artworkPath: String?
    public let size: CGFloat
    public var cornerRadius: CGFloat = 8

    public init(artworkPath: String?, size: CGFloat, cornerRadius: CGFloat = 8) {
        self.artworkPath = artworkPath
        self.size = size
        self.cornerRadius = cornerRadius
    }

    public var body: some View {
        Group {
            if let url = NetworkService.shared.getArtworkUrl(path: artworkPath) {
                AsyncImage(url: url) { phase in
                    switch phase {
                    case .success(let image):
                        image
                            .resizable()
                            .scaledToFill()
                    case .failure(_), .empty:
                        placeholder
                    @unknown default:
                        placeholder
                    }
                }
            } else {
                placeholder
            }
        }
        .frame(width: size, height: size)
        .clipShape(RoundedRectangle(cornerRadius: cornerRadius))
    }

    private var placeholder: some View {
        ZStack {
            AppTheme.card
            Image(systemName: "music.note")
                .font(.system(size: size * 0.4))
                .foregroundColor(AppTheme.textSecondary)
        }
    }
}
