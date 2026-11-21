package com.musichub.mobile.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Upsert
import com.musichub.mobile.core.DownloadStatus
import kotlinx.coroutines.flow.Flow

@Dao
interface TrackDao {
    @Query("SELECT * FROM tracks ORDER BY title COLLATE NOCASE")
    fun observeTracks(): Flow<List<TrackEntity>>

    @Query("SELECT * FROM tracks ORDER BY title COLLATE NOCASE LIMIT :limit")
    suspend fun getTracks(limit: Int = 500): List<TrackEntity>

    @Query("SELECT * FROM tracks WHERE isFavorite = 1 ORDER BY title COLLATE NOCASE")
    fun observeFavorites(): Flow<List<TrackEntity>>

    @Query("SELECT * FROM tracks WHERE isFavorite = 1 ORDER BY title COLLATE NOCASE LIMIT :limit")
    suspend fun getFavorites(limit: Int = 200): List<TrackEntity>

    @Query("SELECT * FROM tracks WHERE artist = :artist ORDER BY title COLLATE NOCASE")
    suspend fun getByArtist(artist: String): List<TrackEntity>

    @Query("SELECT * FROM tracks WHERE id = :id")
    suspend fun get(id: String): TrackEntity?

    @Query("SELECT * FROM tracks WHERE id = :id")
    fun observe(id: String): Flow<TrackEntity?>

    @Query("SELECT * FROM tracks WHERE id IN (:ids)")
    suspend fun getByIds(ids: List<String>): List<TrackEntity>

    @Query(
        "SELECT * FROM tracks WHERE title LIKE '%' || :term || '%' " +
            "OR artist LIKE '%' || :term || '%' OR album LIKE '%' || :term || '%' " +
            "ORDER BY title COLLATE NOCASE LIMIT 100",
    )
    suspend fun searchLocal(term: String): List<TrackEntity>

    @Upsert
    suspend fun upsertAll(tracks: List<TrackEntity>)

    @Query("UPDATE tracks SET isFavorite = :favorite WHERE id = :trackId")
    suspend fun setFavorite(trackId: String, favorite: Boolean)

    @Query("DELETE FROM tracks")
    suspend fun clear()

    @Query("SELECT COUNT(*) FROM tracks")
    suspend fun count(): Int
}

@Dao
interface AlbumDao {
    @Query("SELECT * FROM albums ORDER BY title COLLATE NOCASE")
    fun observeAlbums(): Flow<List<AlbumEntity>>

    @Query("SELECT * FROM albums ORDER BY title COLLATE NOCASE")
    suspend fun getAll(): List<AlbumEntity>

    @Query("SELECT * FROM albums WHERE id = :id")
    suspend fun get(id: String): AlbumEntity?

    @Query("SELECT * FROM albums WHERE artistId = :artistId ORDER BY title COLLATE NOCASE")
    suspend fun getByArtist(artistId: String): List<AlbumEntity>

    @Upsert
    suspend fun upsertAll(albums: List<AlbumEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAlbumTracks(items: List<AlbumTrackEntity>)

    @Query("DELETE FROM album_tracks WHERE albumId = :albumId")
    suspend fun clearAlbumTracks(albumId: String)

    @Query(
        "SELECT t.* FROM tracks t INNER JOIN album_tracks at ON t.id = at.trackId " +
            "WHERE at.albumId = :albumId ORDER BY at.position",
    )
    fun observeAlbumTracks(albumId: String): Flow<List<TrackEntity>>

    @Query(
        "SELECT t.* FROM tracks t INNER JOIN album_tracks at ON t.id = at.trackId " +
            "WHERE at.albumId = :albumId ORDER BY at.position",
    )
    suspend fun getAlbumTracks(albumId: String): List<TrackEntity>

    @Query("DELETE FROM albums")
    suspend fun clear()

    @Query("DELETE FROM album_tracks")
    suspend fun clearAllAlbumTracks()
}

@Dao
interface ArtistDao {
    @Query("SELECT * FROM artists ORDER BY name COLLATE NOCASE")
    fun observeArtists(): Flow<List<ArtistEntity>>

    @Query("SELECT * FROM artists WHERE id = :id")
    suspend fun get(id: String): ArtistEntity?

    @Upsert
    suspend fun upsertAll(artists: List<ArtistEntity>)

    @Query("DELETE FROM artists")
    suspend fun clear()
}

@Dao
interface PlaylistDao {
    @Query("SELECT * FROM playlists ORDER BY name COLLATE NOCASE")
    fun observePlaylists(): Flow<List<PlaylistEntity>>

    @Query("SELECT * FROM playlists WHERE id = :id")
    suspend fun get(id: String): PlaylistEntity?

    @Upsert
    suspend fun upsertAll(playlists: List<PlaylistEntity>)

    @Query("DELETE FROM playlist_tracks WHERE playlistId = :playlistId")
    suspend fun clearTracks(playlistId: String)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertPlaylistTracks(items: List<PlaylistTrackEntity>)

    @Query(
        "SELECT t.* FROM tracks t INNER JOIN playlist_tracks pt ON t.id = pt.trackId " +
            "WHERE pt.playlistId = :playlistId ORDER BY pt.position",
    )
    fun observePlaylistTracks(playlistId: String): Flow<List<TrackEntity>>

    @Query(
        "SELECT t.* FROM tracks t INNER JOIN playlist_tracks pt ON t.id = pt.trackId " +
            "WHERE pt.playlistId = :playlistId ORDER BY pt.position",
    )
    suspend fun getPlaylistTracks(playlistId: String): List<TrackEntity>

    @Query("DELETE FROM playlists")
    suspend fun clearPlaylists()

    @Query("DELETE FROM playlist_tracks")
    suspend fun clearAllPlaylistTracks()

    @Query("DELETE FROM album_tracks")
    suspend fun clearAllAlbumTracks()

    @Query("DELETE FROM folder_tracks")
    suspend fun clearAllFolderTracks()
}

@Dao
interface FolderDao {
    @Query("SELECT * FROM folders ORDER BY name COLLATE NOCASE")
    fun observeFolders(): Flow<List<FolderEntity>>

    @Upsert
    suspend fun upsertAll(folders: List<FolderEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertFolderTracks(items: List<FolderTrackEntity>)

    @Query("DELETE FROM folder_tracks WHERE folderId = :folderId")
    suspend fun clearFolderTracks(folderId: String)

    @Query(
        "SELECT t.* FROM tracks t INNER JOIN folder_tracks ft ON t.id = ft.trackId " +
            "WHERE ft.folderId = :folderId ORDER BY t.title COLLATE NOCASE",
    )
    fun observeFolderTracks(folderId: String): Flow<List<TrackEntity>>

    @Query(
        "SELECT t.* FROM tracks t INNER JOIN folder_tracks ft ON t.id = ft.trackId " +
            "WHERE ft.folderId = :folderId ORDER BY t.title COLLATE NOCASE",
    )
    suspend fun getFolderTracks(folderId: String): List<TrackEntity>

    @Query("DELETE FROM folders")
    suspend fun clear()

    @Query("DELETE FROM folder_tracks")
    suspend fun clearAllFolderTracks()
}

@Dao
interface GenreDao {
    @Query("SELECT * FROM genres ORDER BY name COLLATE NOCASE")
    fun observeGenres(): Flow<List<GenreEntity>>

    @Upsert
    suspend fun upsertAll(genres: List<GenreEntity>)

    @Query("DELETE FROM genres")
    suspend fun clear()
}

@Dao
interface DownloadDao {
    @Query("SELECT * FROM downloads WHERE profileId = :profileId ORDER BY updatedAt DESC")
    fun observeDownloads(profileId: String): Flow<List<DownloadEntity>>

    @Query("SELECT * FROM downloads WHERE profileId = :profileId AND trackId = :trackId")
    suspend fun get(profileId: String, trackId: String): DownloadEntity?

    @Query("SELECT * FROM downloads WHERE profileId = :profileId AND trackId = :trackId")
    fun observe(profileId: String, trackId: String): Flow<DownloadEntity?>

    @Query(
        "SELECT * FROM downloads WHERE profileId = :profileId " +
            "AND status IN ('QUEUED','RUNNING','PAUSED','FAILED')",
    )
    suspend fun pending(profileId: String): List<DownloadEntity>

    @Upsert
    suspend fun upsert(download: DownloadEntity)

    @Query(
        "UPDATE downloads SET status = :status, bytesDownloaded = :bytes, error = :error, " +
            "filePath = :filePath, updatedAt = :updatedAt WHERE trackId = :trackId AND profileId = :profileId",
    )
    suspend fun update(
        profileId: String,
        trackId: String,
        status: DownloadStatus,
        bytes: Long,
        error: String?,
        filePath: String?,
        updatedAt: Long,
    )

    @Query("DELETE FROM downloads WHERE profileId = :profileId AND trackId = :trackId")
    suspend fun delete(profileId: String, trackId: String)

    @Query("DELETE FROM downloads WHERE profileId = :profileId")
    suspend fun clear(profileId: String)
}

@Dao
interface SyncDao {
    @Query("SELECT * FROM pending_favorites ORDER BY updatedAt")
    suspend fun pendingFavorites(): List<PendingFavoriteEntity>

    @Upsert
    suspend fun upsertPendingFavorite(item: PendingFavoriteEntity)

    @Query("DELETE FROM pending_favorites WHERE trackId = :trackId")
    suspend fun deletePendingFavorite(trackId: String)

    @Query("SELECT * FROM pending_playback ORDER BY createdAt LIMIT 100")
    suspend fun pendingPlayback(): List<PendingPlaybackEntity>

    @Insert
    suspend fun insertPendingPlayback(item: PendingPlaybackEntity): Long

    @Query("UPDATE pending_playback SET attempts = attempts + 1, lastError = :error WHERE id = :id")
    suspend fun markPlaybackAttempt(id: Long, error: String?)

    @Query("DELETE FROM pending_playback WHERE id = :id")
    suspend fun deletePendingPlayback(id: Long)

    @Query("SELECT COUNT(*) FROM pending_favorites")
    suspend fun favoriteCount(): Int

    @Query("SELECT COUNT(*) FROM pending_playback")
    suspend fun playbackCount(): Int

    @Query("DELETE FROM pending_favorites")
    suspend fun clearPendingFavorites()

    @Query("DELETE FROM pending_playback")
    suspend fun clearPendingPlayback()
}

@Dao
interface HistoryDao {
    @Transaction
    @Query("SELECT * FROM history ORDER BY lastPlayed DESC LIMIT :limit")
    fun observeHistory(limit: Int = 100): Flow<List<HistoryWithTrack>>

    @Upsert
    suspend fun upsertAll(items: List<HistoryEntity>)

    @Query("DELETE FROM history")
    suspend fun clear()
}

@Dao
interface PlaybackStateDao {
    @Upsert
    suspend fun upsert(state: PlaybackStateEntity)

    @Query("SELECT * FROM playback_state WHERE trackId = :trackId")
    suspend fun get(trackId: String): PlaybackStateEntity?

    @Query("DELETE FROM playback_state")
    suspend fun clear()
}

@Dao
interface QueueDao {
    @Query("SELECT * FROM queue ORDER BY position")
    suspend fun getAll(): List<QueueItemEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(items: List<QueueItemEntity>)

    @Query("DELETE FROM queue")
    suspend fun clear()
}

@Dao
interface CacheAdminDao {
    @Query("DELETE FROM tracks")
    suspend fun clearTracks()

    @Query("DELETE FROM albums")
    suspend fun clearAlbums()

    @Query("DELETE FROM artists")
    suspend fun clearArtists()

    @Query("DELETE FROM playlists")
    suspend fun clearPlaylists()

    @Query("DELETE FROM playlist_tracks")
    suspend fun clearPlaylistTracks()

    @Query("DELETE FROM folders")
    suspend fun clearFolders()

    @Query("DELETE FROM genres")
    suspend fun clearGenres()

    @Query("DELETE FROM history")
    suspend fun clearHistory()

    @Query("DELETE FROM pending_favorites")
    suspend fun clearPendingFavorites()

    @Query("DELETE FROM pending_playback")
    suspend fun clearPendingPlayback()

    @Query("DELETE FROM playback_state")
    suspend fun clearPlaybackState()

    @Query("DELETE FROM queue")
    suspend fun clearQueue()
}
