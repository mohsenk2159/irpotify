package com.musichub.mobile.ui.library

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.data.AppContainer
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

enum class LibrarySection { Tracks, Favorites, Albums, Artists, Playlists }

data class AlbumTracksState(
    val album: AlbumEntity,
    val tracks: List<TrackEntity>,
)

class LibraryViewModel(private val container: AppContainer) : ViewModel() {
    private val _section = MutableStateFlow(LibrarySection.Tracks)
    val section: StateFlow<LibrarySection> = _section.asStateFlow()

    private val _refreshing = MutableStateFlow(false)
    val refreshing: StateFlow<Boolean> = _refreshing.asStateFlow()

    private val _error = MutableStateFlow<Throwable?>(null)
    val error: StateFlow<Throwable?> = _error.asStateFlow()

    private val _openAlbum = MutableStateFlow<AlbumTracksState?>(null)
    val openAlbum: StateFlow<AlbumTracksState?> = _openAlbum.asStateFlow()

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

    fun select(section: LibrarySection) {
        _section.value = section
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

    fun playFromAlbum(index: Int) {
        val current = _openAlbum.value ?: return
        viewModelScope.launch {
            container.playerController.playTracks(current.tracks, index)
        }
    }

    fun playTracks(tracks: List<TrackEntity>, index: Int) {
        viewModelScope.launch {
            container.playerController.playTracks(tracks, index)
        }
    }

    /**
     * Flips the heart for [trackId]. [LibraryRepository.setFavorite] writes Room first and only then
     * calls the server, so the Room flow behind `tracks`/`favorites` already republishes the change;
     * a hard failure is surfaced through [error] instead of silently diverging.
     */
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
