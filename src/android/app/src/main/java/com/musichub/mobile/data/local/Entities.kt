package com.musichub.mobile.data.local

import androidx.room.Embedded
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import androidx.room.Relation
import com.musichub.mobile.core.DownloadStatus

@Entity(tableName = "tracks", indices = [Index("title"), Index("artist"), Index("album")])
data class TrackEntity(
    @PrimaryKey val id: String,
    val title: String,
    val artist: String,
    val album: String,
    val genre: String?,
    val durationSeconds: Double,
    val year: Int?,
    val trackNumber: Int?,
    val discNumber: Int?,
    val codec: String?,
    val sizeBytes: Long,
    val sha256: String,
    val createdAt: String?,
    val isFavorite: Boolean,
    val artworkUrl: String?,
    val cachedAt: Long,
) {
    val durationMs: Long get() = (durationSeconds * 1000).toLong()
}

@Entity(tableName = "albums")
data class AlbumEntity(
    @PrimaryKey val id: String,
    val title: String,
    val albumArtist: String?,
    val artistId: String?,
    val year: Int?,
    val trackCount: Int,
    val artworkUrl: String?,
    val cachedAt: Long,
)

@Entity(tableName = "artists")
data class ArtistEntity(
    @PrimaryKey val id: String,
    val name: String,
    val biography: String?,
    val trackCount: Int,
    val albumCount: Int,
    val cachedAt: Long,
)

@Entity(tableName = "playlists")
data class PlaylistEntity(
    @PrimaryKey val id: String,
    val name: String,
    val description: String?,
    val ownerUserId: String?,
    val isPublic: Boolean,
    val trackCount: Int,
    val updatedAt: String?,
    val cachedAt: Long,
)

@Entity(tableName = "playlist_tracks", primaryKeys = ["playlistId", "trackId"])
data class PlaylistTrackEntity(
    val playlistId: String,
    val trackId: String,
    val position: Int,
)

@Entity(tableName = "album_tracks", primaryKeys = ["albumId", "trackId"])
data class AlbumTrackEntity(
    val albumId: String,
    val trackId: String,
    val position: Int,
)

@Entity(tableName = "folder_tracks", primaryKeys = ["folderId", "trackId"])
data class FolderTrackEntity(
    val folderId: String,
    val trackId: String,
)

@Entity(tableName = "folders")
data class FolderEntity(
    @PrimaryKey val id: String,
    val name: String,
    val parentId: String?,
    val description: String?,
    val trackCount: Int,
    val cachedAt: Long,
)

@Entity(tableName = "genres")
data class GenreEntity(
    @PrimaryKey val id: String,
    val name: String,
    val trackCount: Int,
    val cachedAt: Long,
)

@Entity(tableName = "downloads", primaryKeys = ["profileId", "trackId"])
data class DownloadEntity(
    val profileId: String,
    val trackId: String,
    val title: String,
    val artist: String,
    val album: String,
    val mimeType: String?,
    val extension: String,
    val expectedBytes: Long,
    val sha256: String,
    val filePath: String?,
    val status: DownloadStatus,
    val bytesDownloaded: Long,
    val error: String?,
    val createdAt: Long,
    val updatedAt: Long,
)

@Entity(tableName = "pending_favorites")
data class PendingFavoriteEntity(
    @PrimaryKey val trackId: String,
    val desired: Boolean,
    val updatedAt: Long,
)

@Entity(tableName = "pending_playback")
data class PendingPlaybackEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val trackId: String,
    val positionSeconds: Double,
    val completed: Boolean,
    val deviceId: String?,
    val createdAt: Long,
    val attempts: Int = 0,
    val lastError: String? = null,
)

@Entity(tableName = "history")
data class HistoryEntity(
    @PrimaryKey val trackId: String,
    val lastPlayed: String,
    val plays: Int,
)

data class HistoryWithTrack(
    @Embedded val history: HistoryEntity,
    @Relation(parentColumn = "trackId", entityColumn = "id")
    val track: TrackEntity?,
)

@Entity(tableName = "playback_state")
data class PlaybackStateEntity(
    @PrimaryKey val trackId: String,
    val positionMs: Long,
    val updatedAt: Long,
)

@Entity(tableName = "queue")
data class QueueItemEntity(
    @PrimaryKey val position: Int,
    val trackId: String,
    val title: String,
    val artist: String,
    val album: String,
    val durationMs: Long,
    val artworkUrl: String?,
)
