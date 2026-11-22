package com.musichub.mobile.ui.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.library.FavoriteWriter
import com.musichub.mobile.data.library.HomeData
import com.musichub.mobile.data.library.HomeDataSource
import com.musichub.mobile.data.library.LibraryRepository
import com.musichub.mobile.data.local.AlbumEntity
import com.musichub.mobile.data.local.ArtistEntity
import com.musichub.mobile.data.local.TrackEntity
import com.musichub.mobile.playback.PlayerController
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

enum class HomeTab {
    All,
    Tracks,
    Albums,
    Artists,
    Favorites,
}

sealed interface HomeState {
    /** First load: nothing to show yet, so a full-screen indicator is correct. */
    data object Loading : HomeState

    data class Success(
        val data: HomeData,
        /** True while a pull-to-refresh round-trip is in flight; content stays on screen. */
        val refreshing: Boolean = false,
    ) : HomeState

    data class Failed(val error: Throwable) : HomeState
}

class HomeViewModel(
    private val homeSource: HomeDataSource,
    private val favoriteWriter: FavoriteWriter,
    private val libraryRepo: LibraryRepository? = null,
    private val playerController: PlayerController? = null,
) : ViewModel() {

    constructor(container: AppContainer) : this(
        homeSource = HomeDataSource { container.libraryRepository.loadHome() },
        favoriteWriter = FavoriteWriter { id, favorite ->
            container.libraryRepository.setFavorite(id, favorite)
        },
        libraryRepo = container.libraryRepository,
        playerController = container.playerController,
    )

    private val _state = MutableStateFlow<HomeState>(HomeState.Loading)
    val state: StateFlow<HomeState> = _state.asStateFlow()

    private val _selectedTab = MutableStateFlow(HomeTab.All)
    val selectedTab: StateFlow<HomeTab> = _selectedTab.asStateFlow()

    val tracks: StateFlow<List<TrackEntity>> = libraryRepo?.observeTracks()
        ?.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())
        ?: MutableStateFlow(emptyList())

    val albums: StateFlow<List<AlbumEntity>> = libraryRepo?.observeAlbums()
        ?.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())
        ?: MutableStateFlow(emptyList())

    val artists: StateFlow<List<ArtistEntity>> = libraryRepo?.observeArtists()
        ?.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())
        ?: MutableStateFlow(emptyList())

    val favorites: StateFlow<List<TrackEntity>> = libraryRepo?.observeFavorites()
        ?.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())
        ?: MutableStateFlow(emptyList())

    init {
        reload()
    }

    fun selectTab(tab: HomeTab) {
        _selectedTab.value = tab
    }

    /** Initial load and retry-from-error path: replaces the screen with the loading state. */
    fun reload() {
        viewModelScope.launch {
            _state.value = HomeState.Loading
            homeSource.loadHome()
                .onSuccess { _state.value = HomeState.Success(it) }
                .onFailure { _state.value = HomeState.Failed(it) }
        }
    }

    /**
     * Pull-to-refresh. Keeps the current content visible and only flips [HomeState.Success.refreshing]
     * so the swipe gesture shows its own indicator instead of blanking the screen.
     */
    fun refresh() {
        val current = _state.value
        if (current is HomeState.Success && current.refreshing) return
        viewModelScope.launch {
            if (current is HomeState.Success) {
                _state.value = current.copy(refreshing = true)
                homeSource.loadHome()
                    .onSuccess { _state.value = HomeState.Success(it) }
                    .onFailure { _state.value = current.copy(refreshing = false) }
            } else {
                // Nothing on screen to preserve, so fall back to the full loading path.
                reload()
            }
        }
    }

    /**
     * Optimistically flips the heart, then persists. A failure rolls the local state back so the UI
     * never claims a favourite that the server rejected.
     */
    fun toggleFavorite(trackId: String) {
        val current = _state.value
        if (current is HomeState.Success) {
            val existing = current.data.allTracks().firstOrNull { it.id == trackId }
            if (existing != null) {
                val target = !existing.isFavorite
                _state.value = HomeState.Success(current.data.withFavorite(trackId, target), current.refreshing)
                viewModelScope.launch {
                    favoriteWriter.setFavorite(trackId, target).onFailure {
                        val now = _state.value
                        if (now is HomeState.Success) {
                            _state.value = HomeState.Success(
                                now.data.withFavorite(trackId, !target),
                                now.refreshing,
                            )
                        }
                    }
                }
                return
            }
        }
        val isFav = favorites.value.any { it.id == trackId } ||
            (tracks.value.firstOrNull { it.id == trackId }?.isFavorite == true)
        val target = !isFav
        viewModelScope.launch {
            favoriteWriter.setFavorite(trackId, target)
        }
    }

    fun playTracks(tracks: List<TrackEntity>, index: Int) {
        viewModelScope.launch {
            playerController?.playTracks(tracks, index)
        }
    }
}

private fun HomeData.allTracks(): List<TrackEntity> =
    recentlyAdded + recentlyPlayed + mostPlayed + favorites

/**
 * Applies a favourite flip across every section, and keeps the Favorites section honest: the track
 * is inserted when it was just favourited and removed when it was unfavourited, so the row does not
 * keep showing a stale list until the next server round-trip.
 */
private fun HomeData.withFavorite(trackId: String, favorite: Boolean): HomeData {
    fun List<TrackEntity>.map() = map { if (it.id == trackId) it.copy(isFavorite = favorite) else it }

    val source = allTracks().firstOrNull { it.id == trackId }
    val updatedFavorites = when {
        !favorite -> favorites.filterNot { it.id == trackId }
        favorites.any { it.id == trackId } -> favorites.map()
        source != null -> (favorites + source.copy(isFavorite = true)).sortedBy { it.title.lowercase() }
        else -> favorites
    }

    return copy(
        recentlyAdded = recentlyAdded.map(),
        recentlyPlayed = recentlyPlayed.map(),
        mostPlayed = mostPlayed.map(),
        favorites = updatedFavorites,
    )
}
