package com.musichub.mobile.data.api.models

import kotlinx.serialization.Serializable

@Serializable
data class LoginRequest(
    val email: String,
    val password: String,
    val deviceName: String? = null,
    val clientType: String? = null,
)

@Serializable
data class PairingClaimRequest(
    val code: String,
    val deviceName: String? = null,
)

@Serializable
data class RefreshRequest(val refreshToken: String)

@Serializable
data class ChangePasswordRequest(
    val currentPassword: String? = null,
    val newPassword: String? = null,
    val deviceName: String? = null,
)

@Serializable
data class UserDto(
    val id: String,
    val email: String,
    val displayName: String,
    val role: String = "Listener",
    val roles: List<String> = emptyList(),
    val isActive: Boolean = true,
    val quotaBytes: Long = 0,
    val createdAt: String? = null,
    val lastLoginAt: String? = null,
)

@Serializable
data class TokenResponse(
    val accessToken: String,
    val accessTokenExpiresAt: String,
    val refreshToken: String,
    val refreshTokenExpiresAt: String? = null,
    val user: UserDto,
)

@Serializable
data class TrackDto(
    val id: String,
    val title: String,
    val artist: String = "",
    val album: String = "",
    val genre: String? = null,
    val durationSeconds: Double = 0.0,
    val year: Int? = null,
    val trackNumber: Int? = null,
    val discNumber: Int? = null,
    val codec: String? = null,
    val sizeBytes: Long = 0,
    val sha256: String = "",
    val createdAt: String? = null,
    val isFavorite: Boolean = false,
    val artworkUrl: String? = null,
)

@Serializable
data class TrackFileDto(
    val sizeBytes: Long? = null,
    val mimeType: String? = null,
    val extension: String? = null,
    val sha256: String? = null,
)

@Serializable
data class TrackDetailResponse(
    val track: TrackDto,
    val streamUrl: String? = null,
    val file: TrackFileDto? = null,
)

@Serializable
data class PagedResult<T>(
    val items: List<T> = emptyList(),
    val page: Int = 1,
    val pageSize: Int = 50,
    val total: Int = 0,
)

@Serializable
data class AlbumDto(
    val id: String,
    val title: String,
    val albumArtist: String? = null,
    val artistId: String? = null,
    val artistName: String? = null,
    val year: Int? = null,
    val genre: String? = null,
    val description: String? = null,
    val createdAt: String? = null,
    val trackCount: Int = 0,
    val artworkUrl: String? = null,
)

@Serializable
data class AlbumDetailResponse(
    val album: AlbumDto,
    val tracks: List<TrackDto> = emptyList(),
)

@Serializable
data class ArtistDto(
    val id: String,
    val name: String,
    val biography: String? = null,
    val trackCount: Int = 0,
    val albumCount: Int = 0,
    val artworkUrl: String? = null,
)

@Serializable
data class ArtistAlbumDto(
    val id: String,
    val title: String,
    val year: Int? = null,
    val trackCount: Int = 0,
)

@Serializable
data class ArtistDetailResponse(
    val artist: ArtistDto,
    val tracks: List<TrackDto> = emptyList(),
    val albums: List<ArtistAlbumDto> = emptyList(),
)

@Serializable
data class FolderNodeDto(
    val id: String,
    val name: String,
    val parentId: String? = null,
    val description: String? = null,
    val trackCount: Int = 0,
    val children: List<FolderNodeDto> = emptyList(),
)

@Serializable
data class GenreDto(
    val id: String,
    val name: String,
    val trackCount: Int = 0,
)

@Serializable
data class PlaylistDto(
    val id: String,
    val name: String,
    val description: String? = null,
    val ownerUserId: String? = null,
    val isPublic: Boolean = false,
    val isCollaborative: Boolean = false,
    val trackCount: Int = 0,
    val createdAt: String? = null,
    val updatedAt: String? = null,
    val artworkUrl: String? = null,
)

@Serializable
data class PlaylistDetailResponse(
    val playlist: PlaylistDto,
    val tracks: List<TrackDto> = emptyList(),
)

@Serializable
data class FavoritesResponse(
    val items: List<TrackDto> = emptyList(),
)

@Serializable
data class PlaybackRequest(
    val trackId: String,
    val positionSeconds: Double,
    val completed: Boolean,
    val deviceId: String? = null,
)

@Serializable
data class HistoryEntryDto(
    val lastPlayed: String,
    val plays: Int,
    val track: TrackDto,
)

@Serializable
data class HomeResponse(
    val recentlyAdded: List<TrackDto> = emptyList(),
    val recentlyPlayed: List<TrackDto> = emptyList(),
    val mostPlayed: List<TrackDto> = emptyList(),
    val favorites: List<TrackDto> = emptyList(),
    val albums: List<AlbumDto> = emptyList(),
)

@Serializable
data class SearchAlbumDto(
    val id: String,
    val title: String,
    val albumArtist: String? = null,
    val year: Int? = null,
)

@Serializable
data class SearchArtistDto(
    val id: String,
    val name: String,
)

@Serializable
data class SearchFolderDto(
    val id: String,
    val name: String,
)

@Serializable
data class SearchResponse(
    val tracks: List<TrackDto> = emptyList(),
    val albums: List<SearchAlbumDto> = emptyList(),
    val artists: List<SearchArtistDto> = emptyList(),
    val folders: List<SearchFolderDto> = emptyList(),
)

@Serializable
data class StreamTokenResponse(
    val token: String,
    val expiresAt: String? = null,
    val streamUrl: String? = null,
    val absoluteUrl: String? = null,
)

@Serializable
data class PairingCreateResponse(
    val code: String,
    val expiresAt: String? = null,
    val serverUrl: String? = null,
    val qrPayload: String? = null,
)

@Serializable
data class ApiErrorBody(
    val error: String? = null,
    val title: String? = null,
    val detail: String? = null,
)

@Serializable
data class SessionDto(
    val id: String,
    val deviceName: String? = null,
    val ipAddress: String? = null,
    val createdAt: String? = null,
    val lastSeenAt: String? = null,
    val revokedAt: String? = null,
)
