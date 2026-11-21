package com.musichub.mobile.ui.search

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.library.SearchData
import com.musichub.mobile.data.local.TrackEntity
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

data class SearchUiState(
    val query: String = "",
    val searching: Boolean = false,
    val results: SearchData? = null,
    val error: Throwable? = null,
)

class SearchViewModel(private val container: AppContainer) : ViewModel() {
    private val _state = MutableStateFlow(SearchUiState())
    val state: StateFlow<SearchUiState> = _state.asStateFlow()

    private var searchJob: Job? = null

    fun updateQuery(value: String) {
        _state.value = _state.value.copy(query = value, error = null)
        searchJob?.cancel()
        if (value.isBlank()) {
            _state.value = _state.value.copy(results = null, searching = false)
            return
        }
        searchJob = viewModelScope.launch {
            delay(350)
            runSearch(value)
        }
    }

    fun searchNow() {
        val query = _state.value.query.trim()
        if (query.isEmpty()) return
        searchJob?.cancel()
        searchJob = viewModelScope.launch { runSearch(query) }
    }

    private suspend fun runSearch(query: String) {
        _state.value = _state.value.copy(searching = true, error = null)
        container.libraryRepository.search(query)
            .onSuccess { _state.value = _state.value.copy(searching = false, results = it) }
            .onFailure { _state.value = _state.value.copy(searching = false, error = it) }
    }

    fun play(tracks: List<TrackEntity>, index: Int) {
        viewModelScope.launch { container.playerController.playTracks(tracks, index) }
    }

    /**
     * Favourites from search results. SearchData is a snapshot rather than a Room flow, so the row
     * is updated optimistically here and rolled back if the server rejects the write; otherwise the
     * heart would appear dead until the next search.
     */
    fun toggleFavorite(track: TrackEntity) {
        val target = !track.isFavorite
        applyFavorite(track.id, target)
        viewModelScope.launch {
            container.libraryRepository.setFavorite(track.id, target)
                .onFailure {
                    applyFavorite(track.id, !target)
                    _state.value = _state.value.copy(error = it)
                }
        }
    }

    private fun applyFavorite(trackId: String, favorite: Boolean) {
        val current = _state.value
        val results = current.results ?: return
        _state.value = current.copy(
            results = results.copy(
                tracks = results.tracks.map {
                    if (it.id == trackId) it.copy(isFavorite = favorite) else it
                },
            ),
        )
    }
}
