import Foundation

// MARK: - Auth & User Models

public struct UserDto: Codable, Identifiable, Equatable {
    public let id: String
    public let email: String
    public let displayName: String
    public let role: String?
    public let roles: [String]?
    public let isActive: Bool?
    public let quotaBytes: Int64?
    public let createdAt: String?
    public let lastLoginAt: String?

    public init(id: String, email: String, displayName: String, role: String? = "Listener", roles: [String]? = [], isActive: Bool? = true, quotaBytes: Int64? = 0, createdAt: String? = nil, lastLoginAt: String? = nil) {
        self.id = id
        self.email = email
        self.displayName = displayName
        self.role = role
        self.roles = roles
        self.isActive = isActive
        self.quotaBytes = quotaBytes
        self.createdAt = createdAt
        self.lastLoginAt = lastLoginAt
    }
}

public struct TokenResponse: Codable {
    public let accessToken: String
    public let accessTokenExpiresAt: String
    public let refreshToken: String
    public let refreshTokenExpiresAt: String?
    public let user: UserDto
}

public struct LoginRequest: Codable {
    public let email: String
    public let password: String
    public let deviceName: String?
    public let clientType: String?

    public init(email: String, password: String, deviceName: String? = "iOS Device", clientType: String? = "iOS") {
        self.email = email
        self.password = password
        self.deviceName = deviceName
        self.clientType = clientType
    }
}

public struct PairingClaimRequest: Codable {
    public let code: String
    public let deviceName: String?

    public init(code: String, deviceName: String? = "iOS Device") {
        self.code = code
        self.deviceName = deviceName
    }
}

public struct RefreshRequest: Codable {
    public let refreshToken: String
}

// MARK: - Track & Media Models

public struct TrackDto: Codable, Identifiable, Equatable, Hashable {
    public let id: String
    public let title: String
    public let artist: String
    public let album: String
    public let genre: String?
    public let durationSeconds: Double
    public let year: Int?
    public let trackNumber: Int?
    public let discNumber: Int?
    public let codec: String?
    public let sizeBytes: Int64?
    public let sha256: String?
    public var isFavorite: Bool
    public let artworkUrl: String?

    public init(
        id: String,
        title: String,
        artist: String = "Unknown Artist",
        album: String = "Unknown Album",
        genre: String? = nil,
        durationSeconds: Double = 0.0,
        year: Int? = nil,
        trackNumber: Int? = nil,
        discNumber: Int? = nil,
        codec: String? = nil,
        sizeBytes: Int64? = 0,
        sha256: String? = nil,
        isFavorite: Bool = false,
        artworkUrl: String? = nil
    ) {
        self.id = id
        self.title = title
        self.artist = artist
        self.album = album
        self.genre = genre
        self.durationSeconds = durationSeconds
        self.year = year
        self.trackNumber = trackNumber
        self.discNumber = discNumber
        self.codec = codec
        self.sizeBytes = sizeBytes
        self.sha256 = sha256
        self.isFavorite = isFavorite
        self.artworkUrl = artworkUrl
    }

    public var formattedDuration: String {
        let totalSeconds = Int(durationSeconds)
        let minutes = totalSeconds / 60
        let seconds = totalSeconds % 60
        return String(format: "%d:%02d", minutes, seconds)
    }
}

public struct TrackDetailResponse: Codable {
    public let track: TrackDto
    public let streamUrl: String?
}

// MARK: - Paged Container

public struct PagedResult<T: Codable>: Codable {
    public let items: [T]
    public let page: Int
    public let pageSize: Int
    public let total: Int
}

// MARK: - Album Models

public struct AlbumDto: Codable, Identifiable, Equatable, Hashable {
    public let id: String
    public let title: String
    public let albumArtist: String?
    public let artistId: String?
    public let artistName: String?
    public let year: Int?
    public let genre: String?
    public let trackCount: Int
    public let artworkUrl: String?

    public var displayArtist: String {
        albumArtist ?? artistName ?? "Various Artists"
    }
}

public struct AlbumDetailResponse: Codable {
    public let album: AlbumDto
    public let tracks: [TrackDto]
}

// MARK: - Artist Models

public struct ArtistDto: Codable, Identifiable, Equatable, Hashable {
    public let id: String
    public let name: String
    public let biography: String?
    public let trackCount: Int
    public let albumCount: Int
    public let artworkUrl: String?
}

public struct ArtistAlbumDto: Codable, Identifiable, Equatable, Hashable {
    public let id: String
    public let title: String
    public let year: Int?
    public let trackCount: Int
}

public struct ArtistDetailResponse: Codable {
    public let artist: ArtistDto
    public let tracks: [TrackDto]
    public let albums: [ArtistAlbumDto]
}

// MARK: - Folder Models

public struct FolderNodeDto: Codable, Identifiable, Equatable, Hashable {
    public let id: String
    public let name: String
    public let parentId: String?
    public let description: String?
    public let trackCount: Int
    public let children: [FolderNodeDto]?
}

// MARK: - Playlist Models

public struct PlaylistDto: Codable, Identifiable, Equatable, Hashable {
    public let id: String
    public let name: String
    public let description: String?
    public let ownerUserId: String?
    public let isPublic: Bool
    public let isCollaborative: Bool
    public let trackCount: Int
    public let artworkUrl: String?
}

public struct PlaylistDetailResponse: Codable {
    public let playlist: PlaylistDto
    public let tracks: [TrackDto]
}

// MARK: - Home & Search Models

public struct HomeResponse: Codable {
    public let recentlyAdded: [TrackDto]
    public let recentlyPlayed: [TrackDto]
    public let mostPlayed: [TrackDto]
}

public struct SearchResponse: Codable {
    public let tracks: [TrackDto]
    public let albums: [AlbumDto]
    public let artists: [ArtistDto]
}

public struct PlaybackReportRequest: Codable {
    public let trackId: String
    public let positionSeconds: Double
    public let completed: Bool
    public let deviceId: String?
}

public struct StreamTokenResponse: Codable {
    public let token: String
    public let expiresAt: String
}
