package com.musichub.mobile.data.library

import com.musichub.mobile.data.api.ApiCallException
import com.musichub.mobile.data.api.ApiFactory
import com.musichub.mobile.data.api.MusicHubApi
import com.musichub.mobile.data.api.models.AlbumDto
import com.musichub.mobile.data.api.models.PlaybackRequest
import com.musichub.mobile.data.api.models.PlaylistDto
import com.musichub.mobile.data.api.models.TrackDto
import com.musichub.mobile.data.api.runApi
import com.musichub.mobile.data.auth.SessionManager
import com.musichub.mobile.data.local.AlbumEntity
import com.musichub.mobile.data.local.AlbumTrackEntity
import com.musichub.mobile.data.local.ArtistEntity
import com.musichub.mobile.data.local.DownloadEntity
import com.musichub.mobile.data.local.FolderEntity
import com.musichub.mobile.data.local.FolderTrackEntity
import com.musichub.mobile.data.local.GenreEntity
import com.musichub.mobile.data.local.HistoryEntity
import com.musichub.mobile.data.local.HistoryWithTrack
import com.musichub.mobile.data.local.MusicHubDatabase
import com.musichub.mobile.data.local.PendingFavoriteEntity
import com.musichub.mobile.data.local.PendingPlaybackEntity
import com.musichub.mobile.data.local.PlaybackStateEntity
import com.musichub.mobile.data.local.PlaylistEntity
import com.musichub.mobile.data.local.PlaylistTrackEntity
import com.musichub.mobile.data.local.QueueItemEntity
import com.musichub.mobile.data.local.TrackEntity
import com.musichub.mobile.data.sync.SyncScheduler
import kotlinx.coroutines.flow.Flow
import java.util.UUID

data class TrackPage(val tracks: List<TrackEntity>, val page: Int, val pageSize: Int, val total: Int)

data class AlbumDetailData(val album: AlbumEntity, val tracks: List<TrackEntity>, val fromCache: Boolean)

data class PlaylistDetailData(val playlist: PlaylistEntity, val tracks: List<TrackEntity>, val fromCache: Boolean)

data class ArtistDetailData(
    val artist: ArtistEntity,
    val tracks: List<TrackEntity>,
    val albums: List<AlbumEntity>,
    val fromCache: Boolean,
)

data class HomeData(
    val recentlyAdded: List<TrackEntity>,
    val recentlyPlayed: List<TrackEntity>,
    val mostPlayed: List<TrackEntity>,
    val favorites: List<TrackEntity>,
    val albums: List<AlbumEntity>,
    val fromCache: Boolean,
)

data class SearchData(
    val tracks: List<TrackEntity>,
    val albums: List<AlbumEntity>,
    val artists: List<ArtistEntity>,
    val folders: List<FolderEntity>,
    val fromCache: Boolean,
)

class LibraryRepository(
    private val database: MusicHubDatabase,
    private val apiFactory: ApiFactory,
    private val sessionManager: SessionManager,
    private val syncScheduler: SyncScheduler,
) {
    private fun api(): MusicHubApi {
        val profile = sessionManager.currentProfile() ?: error("No active server profile")
        return apiFactory.authenticatedApi(profile.baseUrl)
    }

    fun observeTracks(): Flow<List<TrackEntity>> = database.tracks().observeTracks()

    fun observeFavorites(): Flow<List<TrackEntity>> = database.tracks().observeFavorites()

    fun observeAlbums(): Flow<List<AlbumEntity>> = database.albums().observeAlbums()

    fun observeArtists(): Flow<List<ArtistEntity>> = database.artists().observeArtists()

    fun observePlaylists(): Flow<List<PlaylistEntity>> = database.playlists().observePlaylists()

    fun observeFolders(): Flow<List<FolderEntity>> = database.folders().observeFolders()

    fun observeGenres(): Flow<List<GenreEntity>> = database.genres().observeGenres()

    fun observeHistory(): Flow<List<HistoryWithTrack>> = database.history().observeHistory()

    fun observeAlbumTracks(albumId: String): Flow<List<TrackEntity>> =
        database.albums().observeAlbumTracks(albumId)

    fun observePlaylistTracks(playlistId: String): Flow<List<TrackEntity>> =
        database.playlists().observePlaylistTracks(playlistId)

    suspend fun getLocalPlaylistTracks(playlistId: String): List<TrackEntity> =
        database.playlists().getPlaylistTracks(playlistId)

    suspend fun createLocalPlaylist(name: String): String {
        val id = "local_" + UUID.randomUUID().toString()
        val playlist = PlaylistEntity(
            id = id,
            name = name.trim(),
            description = null,
            ownerUserId = "local",
            isPublic = false,
            trackCount = 0,
            updatedAt = System.currentTimeMillis().toString(),
            cachedAt = now(),
        )
        database.playlists().upsert(playlist)
        return id
    }

    suspend fun deleteLocalPlaylist(playlistId: String) {
        database.playlists().clearTracks(playlistId)
        database.playlists().deletePlaylist(playlistId)
    }

    suspend fun addTrackToLocalPlaylist(playlistId: String, trackId: String) {
        val currentTracks = database.playlists().getPlaylistTracks(playlistId)
        if (currentTracks.any { it.id == trackId }) return
        val nextPosition = currentTracks.size
        database.playlists().insertPlaylistTrack(PlaylistTrackEntity(playlistId, trackId, nextPosition))
        val count = database.playlists().getTrackCount(playlistId)
        database.playlists().updateTrackCount(playlistId, count)
    }

    suspend fun removeTrackFromLocalPlaylist(playlistId: String, trackId: String) {
        database.playlists().removeTrack(playlistId, trackId)
        val count = database.playlists().getTrackCount(playlistId)
        database.playlists().updateTrackCount(playlistId, count)
    }

    fun observeFolderTracks(folderId: String): Flow<List<TrackEntity>> =
        database.folders().observeFolderTracks(folderId)

    fun observeDownloads(profileId: String): Flow<List<DownloadEntity>> =
        database.downloads().observeDownloads(profileId)

    fun observeDownload(profileId: String, trackId: String): Flow<DownloadEntity?> =
        database.downloads().observe(profileId, trackId)

    suspend fun refreshLibrary(): Result<Unit> {
        return try {
            val tracks = runApi { api().tracks(page = 1, pageSize = 200) }
            cacheTracks(tracks.items)
            runCatching { runApi { api().albums(pageSize = 200) } }.getOrNull()?.let { cacheAlbums(it.items) }
            runCatching { runApi { api().artists(pageSize = 200) } }.getOrNull()?.let { cacheArtists(it.items) }
            runCatching { runApi { api().playlists() } }.getOrNull()?.let { cachePlaylists(it) }
            runCatching { runApi { api().folders() } }.getOrNull()?.let { cacheFolders(it) }
            runCatching { runApi { api().genres() } }.getOrNull()?.let { cacheGenres(it) }
            runCatching { refreshHistoryInternal() }
            Result.success(Unit)
        } catch (exception: ApiCallException) {
            Result.failure(exception)
        }
    }

    suspend fun loadTracks(
        query: String? = null,
        favoritesOnly: Boolean = false,
        albumId: String? = null,
        artistId: String? = null,
        folderId: String? = null,
        genreId: String? = null,
        sort: String? = null,
        page: Int = 1,
        pageSize: Int = 200,
    ): Result<TrackPage> {
        return try {
            val result = runApi {
                api().tracks(
                    query = query?.takeIf { it.isNotBlank() },
                    page = page,
                    pageSize = pageSize,
                    sort = sort,
                    favorites = if (favoritesOnly) true else null,
                    albumId = albumId,
                    artistId = artistId,
                    folderId = folderId,
                    genreId = genreId,
                )
            }
            val tracks = cacheTracks(result.items)
            if (albumId != null) {
                database.albums().clearAlbumTracks(albumId)
                database.albums().insertAlbumTracks(tracks.mapIndexed { index, track -> AlbumTrackEntity(albumId, track.id, index) })
            }
            if (folderId != null) {
                database.folders().clearFolderTracks(folderId)
                database.folders().insertFolderTracks(tracks.map { FolderTrackEntity(folderId, it.id) })
            }
            Result.success(TrackPage(tracks, result.page, result.pageSize, result.total))
        } catch (exception: ApiCallException) {
            val cached = when {
                albumId != null -> database.albums().getAlbumTracks(albumId)
                folderId != null -> database.folders().getFolderTracks(folderId)
                favoritesOnly -> database.tracks().getFavorites()
                else -> database.tracks().getTracks()
            }
            Result.success(TrackPage(cached, 1, cached.size, cached.size))
        }
    }

    suspend fun loadAlbum(id: String): Result<AlbumDetailData> {
        return try {
            val response = runApi { api().album(id) }
            val album = cacheAlbum(response.album)
            val tracks = cacheTracks(response.tracks)
            database.albums().clearAlbumTracks(id)
            database.albums().insertAlbumTracks(tracks.mapIndexed { index, track -> AlbumTrackEntity(id, track.id, index) })
            Result.success(AlbumDetailData(album, tracks, fromCache = false))
        } catch (exception: ApiCallException) {
            val album = database.albums().get(id)
                ?: return Result.failure(exception)
            Result.success(AlbumDetailData(album, database.albums().getAlbumTracks(id), fromCache = true))
        }
    }

    suspend fun loadPlaylist(id: String): Result<PlaylistDetailData> {
        if (id.startsWith("local_")) {
            val playlist = database.playlists().get(id)
                ?: return Result.failure(NoSuchElementException("Playlist not found"))
            val tracks = database.playlists().getPlaylistTracks(id)
            return Result.success(PlaylistDetailData(playlist, tracks, fromCache = true))
        }
        return try {
            val response = runApi { api().playlist(id) }
            val playlist = cachePlaylist(response.playlist)
            val tracks = cacheTracks(response.tracks)
            database.playlists().clearTracks(id)
            database.playlists().insertPlaylistTracks(
                tracks.mapIndexed { index, track -> PlaylistTrackEntity(id, track.id, index) },
            )
            Result.success(PlaylistDetailData(playlist, tracks, fromCache = false))
        } catch (exception: ApiCallException) {
            val playlist = database.playlists().get(id) ?: return Result.failure(exception)
            Result.success(
                PlaylistDetailData(playlist, database.playlists().getPlaylistTracks(id), fromCache = true),
            )
        }
    }

    suspend fun loadArtist(id: String): Result<ArtistDetailData> {
        return try {
            val response = runApi { api().artist(id) }
            val artist = ArtistEntity(
                id = response.artist.id,
                name = response.artist.name,
                biography = response.artist.biography,
                trackCount = response.artist.trackCount,
                albumCount = response.artist.albumCount,
                cachedAt = now(),
            )
            database.artists().upsertAll(listOf(artist))
            val tracks = cacheTracks(response.tracks)
            val albums = cacheAlbums(response.albums.map { AlbumDto(id = it.id, title = it.title, year = it.year, trackCount = it.trackCount) })
            Result.success(ArtistDetailData(artist, tracks, albums, fromCache = false))
        } catch (exception: ApiCallException) {
            val artist = database.artists().get(id) ?: return Result.failure(exception)
            Result.success(
                ArtistDetailData(
                    artist = artist,
                    tracks = database.tracks().getByArtist(artist.name),
                    albums = database.albums().getByArtist(id),
                    fromCache = true,
                ),
            )
        }
    }

    suspend fun loadHome(): Result<HomeData> {
        return try {
            val response = runApi { api().home() }
            val recentlyAdded = cacheTracks(response.recentlyAdded)
            val recentlyPlayed = cacheTracks(response.recentlyPlayed)
            val mostPlayed = cacheTracks(response.mostPlayed)
            val favorites = cacheTracks(response.favorites)
            val albums = cacheAlbums(response.albums)
            Result.success(
                HomeData(recentlyAdded, recentlyPlayed, mostPlayed, favorites, albums, fromCache = false),
            )
        } catch (exception: ApiCallException) {
            val cached = database.tracks().getTracks(20)
            Result.success(
                HomeData(
                    recentlyAdded = cached,
                    recentlyPlayed = cached,
                    mostPlayed = database.tracks().getFavorites(20),
                    favorites = database.tracks().getFavorites(20),
                    albums = database.albums().getAll(),
                    fromCache = true,
                ),
            )
        }
    }

    suspend fun search(query: String): Result<SearchData> {
        return try {
            val response = runApi { api().search(query) }
            val tracks = cacheTracks(response.tracks)
            val albums = cacheAlbums(
                response.albums.map { AlbumDto(it.id, it.title, it.albumArtist, year = it.year) },
            )
            val artists = response.artists.map {
                ArtistEntity(it.id, it.name, null, 0, 0, now())
            }.also { database.artists().upsertAll(it) }
            val folders = response.folders.map {
                FolderEntity(it.id, it.name, null, null, 0, now())
            }.also { database.folders().upsertAll(it) }
            Result.success(SearchData(tracks, albums, artists, folders, fromCache = false))
        } catch (exception: ApiCallException) {
            val tracks = database.tracks().searchLocal(query)
            Result.success(SearchData(tracks, emptyList(), emptyList(), emptyList(), fromCache = true))
        }
    }

    suspend fun refreshHistory(): Result<Unit> = try {
        refreshHistoryInternal()
        Result.success(Unit)
    } catch (exception: ApiCallException) {
        Result.failure(exception)
    }

    suspend fun setFavorite(trackId: String, favorite: Boolean): Result<Unit> {
        database.tracks().setFavorite(trackId, favorite)
        database.sync().upsertPendingFavorite(
            PendingFavoriteEntity(trackId, favorite, System.currentTimeMillis()),
        )
        return try {
            runApi {
                if (favorite) api().addFavorite(trackId) else api().removeFavorite(trackId)
            }
            database.sync().deletePendingFavorite(trackId)
            Result.success(Unit)
        } catch (exception: ApiCallException) {
            if (exception.statusCode == 404) {
                database.tracks().setFavorite(trackId, !favorite)
                database.sync().deletePendingFavorite(trackId)
                Result.failure(exception)
            } else {
                syncScheduler.scheduleNow()
                Result.success(Unit)
            }
        }
    }

    suspend fun recordPlayback(
        trackId: String,
        positionSeconds: Double,
        completed: Boolean,
        deviceId: String,
    ) {
        val pending = PendingPlaybackEntity(
            trackId = trackId,
            positionSeconds = positionSeconds.coerceAtLeast(0.0),
            completed = completed,
            deviceId = deviceId,
            createdAt = System.currentTimeMillis(),
        )
        val id = database.sync().insertPendingPlayback(pending)
        try {
            runApi {
                api().reportPlayback(
                    PlaybackRequest(
                        trackId = trackId,
                        positionSeconds = positionSeconds.coerceAtLeast(0.0),
                        completed = completed,
                        deviceId = deviceId,
                    ),
                )
            }
            database.sync().deletePendingPlayback(id)
        } catch (exception: ApiCallException) {
            if (exception.statusCode == 404) {
                database.sync().deletePendingPlayback(id)
            } else {
                database.sync().markPlaybackAttempt(id, exception.serverMessage ?: "queued")
                syncScheduler.scheduleNow()
            }
        }
    }

    suspend fun savePlaybackPosition(trackId: String, positionMs: Long) {
        database.playbackState().upsert(
            PlaybackStateEntity(trackId, positionMs.coerceAtLeast(0), System.currentTimeMillis()),
        )
    }

    suspend fun playbackPosition(trackId: String): Long =
        database.playbackState().get(trackId)?.positionMs ?: 0L

    suspend fun saveQueue(tracks: List<TrackEntity>) {
        database.queue().clear()
        if (tracks.isNotEmpty()) {
            database.queue().insertAll(
                tracks.mapIndexed { index, track ->
                    QueueItemEntity(
                        position = index,
                        trackId = track.id,
                        title = track.title,
                        artist = track.artist,
                        album = track.album,
                        durationMs = track.durationMs,
                        artworkUrl = track.artworkUrl,
                    )
                },
            )
        }
    }

    suspend fun restoreQueue(): List<QueueItemEntity> = database.queue().getAll()

    suspend fun track(id: String): TrackEntity? = database.tracks().get(id)

    /** Live view of one cached track, used by Now Playing for artwork and the favourite heart. */
    fun observeTrack(id: String): Flow<TrackEntity?> = database.tracks().observe(id)

    suspend fun clearCache() {
        val admin = database.cacheAdmin()
        admin.clearTracks()
        admin.clearAlbums()
        admin.clearArtists()
        admin.clearPlaylists()
        admin.clearPlaylistTracks()
        admin.clearFolders()
        admin.clearGenres()
        admin.clearHistory()
        admin.clearPendingFavorites()
        admin.clearPendingPlayback()
        admin.clearPlaybackState()
        admin.clearQueue()
    }

    private suspend fun refreshHistoryInternal() {
        val history = runApi { api().history(limit = 100) }
        cacheTracks(history.map { it.track })
        database.history().upsertAll(
            history.map { HistoryEntity(it.track.id, it.lastPlayed, it.plays) },
        )
    }

    private suspend fun cacheTracks(tracks: List<TrackDto>): List<TrackEntity> {
        if (tracks.isEmpty()) return emptyList()
        val pending = database.sync().pendingFavorites().associate { it.trackId to it.desired }
        val entities = tracks.map { dto ->
            TrackEntity(
                id = dto.id,
                title = dto.title,
                artist = dto.artist,
                album = dto.album,
                genre = dto.genre,
                durationSeconds = dto.durationSeconds,
                year = dto.year,
                trackNumber = dto.trackNumber,
                discNumber = dto.discNumber,
                codec = dto.codec,
                sizeBytes = dto.sizeBytes,
                sha256 = dto.sha256,
                createdAt = dto.createdAt,
                isFavorite = pending[dto.id] ?: dto.isFavorite,
                artworkUrl = dto.artworkUrl,
                cachedAt = now(),
            )
        }
        database.tracks().upsertAll(entities)
        return entities
    }

    private suspend fun cacheAlbums(albums: List<AlbumDto>): List<AlbumEntity> {
        val entities = albums.map { cacheAlbumEntity(it) }
        database.albums().upsertAll(entities)
        return entities
    }

    private suspend fun cacheAlbum(album: AlbumDto): AlbumEntity {
        val entity = cacheAlbumEntity(album)
        database.albums().upsertAll(listOf(entity))
        return entity
    }

    private fun cacheAlbumEntity(album: AlbumDto) = AlbumEntity(
        id = album.id,
        title = album.title,
        albumArtist = album.albumArtist ?: album.artistName,
        artistId = album.artistId,
        year = album.year,
        trackCount = album.trackCount,
        artworkUrl = album.artworkUrl,
        cachedAt = now(),
    )

    private suspend fun cacheArtists(artists: List<com.musichub.mobile.data.api.models.ArtistDto>): List<ArtistEntity> {
        val entities = artists.map {
            ArtistEntity(it.id, it.name, it.biography, it.trackCount, it.albumCount, now())
        }
        database.artists().upsertAll(entities)
        return entities
    }

    private suspend fun cachePlaylists(playlists: List<PlaylistDto>): List<PlaylistEntity> {
        val entities = playlists.map { cachePlaylistEntity(it) }
        database.playlists().upsertAll(entities)
        return entities
    }

    private suspend fun cachePlaylist(playlist: PlaylistDto): PlaylistEntity {
        val entity = cachePlaylistEntity(playlist)
        database.playlists().upsertAll(listOf(entity))
        return entity
    }

    private fun cachePlaylistEntity(playlist: PlaylistDto) = PlaylistEntity(
        id = playlist.id,
        name = playlist.name,
        description = playlist.description,
        ownerUserId = playlist.ownerUserId,
        isPublic = playlist.isPublic,
        trackCount = playlist.trackCount,
        updatedAt = playlist.updatedAt,
        cachedAt = now(),
    )

    private suspend fun cacheFolders(nodes: List<com.musichub.mobile.data.api.models.FolderNodeDto>) {
        val flat = mutableListOf<FolderEntity>()
        fun visit(node: com.musichub.mobile.data.api.models.FolderNodeDto) {
            flat += FolderEntity(node.id, node.name, node.parentId, node.description, node.trackCount, now())
            node.children.forEach(::visit)
        }
        nodes.forEach(::visit)
        database.folders().upsertAll(flat)
    }

    private suspend fun cacheGenres(genres: List<com.musichub.mobile.data.api.models.GenreDto>) {
        database.genres().upsertAll(
            genres.map { GenreEntity(it.id, it.name, it.trackCount, now()) },
        )
    }

    private fun now() = System.currentTimeMillis()
}
