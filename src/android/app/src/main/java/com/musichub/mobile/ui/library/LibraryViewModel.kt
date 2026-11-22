package com.musichub.mobile.ui.library

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.library.PlaylistDetailData
import com.musichub.mobile.data.local.AlbumEntity
import com.musichub.mobile.data.local.ArtistEntity
import com.musichub.mobile.data.local.PlaylistEntity
import com.musichub.mobile.data.local.TrackEntity
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

enum class LibraryFilter { Playlists, Artists, Albums }

enum class LibrarySection { Tracks, Favorites, Albums, Artists, Playlists }

data class AlbumTracksState(
    val album: AlbumEntity,
    val tracks: List<TrackEntity>,
)

class LibraryViewModel(private val container: AppContainer) : ViewModel() {

    private val _selectedFilter = MutableStateFlow<LibraryFilter?>(null)
    val selectedFilter: StateFlow<LibraryFilter?> = _selectedFilter.asStateFlow()

    private val _refreshing = MutableStateFlow(false)
    val refreshing: StateFlow<Boolean> = _refreshing.asStateFlow()

    private val _error = MutableStateFlow<Throwable?>(null)
    val error: StateFlow<Throwable?> = _error.asStateFlow()

    private val _openAlbum = MutableStateFlow<AlbumTracksState?>(null)
    val openAlbum: StateFlow<AlbumTracksState?> = _openAlbum.asStateFlow()

    private val _openPlaylist = MutableStateFlow<PlaylistDetailData?>(null)
    val openPlaylist: StateFlow<PlaylistDetailData?> = _openPlaylist.asStateFlow()

    private val _showLikedSongs = MutableStateFlow(false)
    val showLikedSongs: StateFlow<Boolean> = _showLikedSongs.asStateFlow()

    val tracks: StateFlow<List<TrackEntity>> = container.libraryRepository.observeTracks()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val favorites: StateFlow<List<TrackEntity>> = container.libraryRepository.observeFavorites()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val albums: StateFlow<List<AlbumEntity>> = container.libraryRepository.observeAlbums()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val artists: StateFlow<List<ArtistEntity>> = container.libraryRepository.observeArtists()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val playlists: StateFlow<List<PlaylistEntity>> = container.libraryRepository.observePlaylists()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    init {
        refresh()
    }

    fun toggleFilter(filter: LibraryFilter) {
        _selectedFilter.value = if (_selectedFilter.value == filter) null else filter
    }

    fun refresh() {
        viewModelScope.launch {
            _refreshing.value = true
            _error.value = null
            container.libraryRepository.refreshLibrary()
                .onFailure { _error.value = it }
            _refreshing.value = false
        }
    }

    fun openAlbum(albumId: String) {
        viewModelScope.launch {
            val album = albums.value.firstOrNull { it.id == albumId } ?: return@launch
            container.libraryRepository.loadAlbum(albumId)
                .onSuccess { _openAlbum.value = AlbumTracksState(it.album, it.tracks) }
                .onFailure { _error.value = it }
        }
    }

    fun closeAlbum() {
        _openAlbum.value = null
    }

    fun openPlaylist(playlistId: String) {
        viewModelScope.launch {
            container.libraryRepository.loadPlaylist(playlistId)
                .onSuccess { _openPlaylist.value = it }
                .onFailure { _error.value = it }
        }
    }

    fun closePlaylist() {
        _openPlaylist.value = null
    }

    fun openLikedSongs() {
        _showLikedSongs.value = true
    }

    fun closeLikedSongs() {
        _showLikedSongs.value = false
    }

    fun createPlaylist(name: String, onCreated: (String) -> Unit = {}) {
        viewModelScope.launch {
            val trimmed = name.trim()
            if (trimmed.isEmpty()) return@launch
            val id = container.libraryRepository.createLocalPlaylist(trimmed)
            openPlaylist(id)
            onCreated(id)
        }
    }

    fun deletePlaylist(playlistId: String) {
        viewModelScope.launch {
            container.libraryRepository.deleteLocalPlaylist(playlistId)
            if (_openPlaylist.value?.playlist?.id == playlistId) {
                _openPlaylist.value = null
            }
        }
    }

    fun addTrackToPlaylist(playlistId: String, trackId: String) {
        viewModelScope.launch {
            container.libraryRepository.addTrackToLocalPlaylist(playlistId, trackId)
            if (_openPlaylist.value?.playlist?.id == playlistId) {
                val updatedTracks = container.libraryRepository.getLocalPlaylistTracks(playlistId)
                val current = _openPlaylist.value ?: return@launch
                val updatedPlaylist = current.playlist.copy(trackCount = updatedTracks.size)
                _openPlaylist.value = PlaylistDetailData(updatedPlaylist, updatedTracks, fromCache = true)
            }
        }
    }

    fun removeTrackFromPlaylist(playlistId: String, trackId: String) {
        viewModelScope.launch {
            container.libraryRepository.removeTrackFromLocalPlaylist(playlistId, trackId)
            if (_openPlaylist.value?.playlist?.id == playlistId) {
                val updatedTracks = container.libraryRepository.getLocalPlaylistTracks(playlistId)
                val current = _openPlaylist.value ?: return@launch
                val updatedPlaylist = current.playlist.copy(trackCount = updatedTracks.size)
                _openPlaylist.value = PlaylistDetailData(updatedPlaylist, updatedTracks, fromCache = true)
            }
        }
    }

    fun playTracks(tracks: List<TrackEntity>, index: Int) {
        viewModelScope.launch {
            container.playerController.playTracks(tracks, index)
        }
    }

    fun toggleFavorite(trackId: String) {
        val current = tracks.value.firstOrNull { it.id == trackId }
            ?: favorites.value.firstOrNull { it.id == trackId }
            ?: return
        viewModelScope.launch {
            container.libraryRepository.setFavorite(trackId, !current.isFavorite)
                .onFailure { _error.value = it }
        }
    }
}
