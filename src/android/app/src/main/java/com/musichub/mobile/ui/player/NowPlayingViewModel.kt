package com.musichub.mobile.ui.player

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.local.TrackEntity
import com.musichub.mobile.playback.PlayerUiState
import com.musichub.mobile.playback.QueueItem
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@OptIn(ExperimentalCoroutinesApi::class)
class NowPlayingViewModel(private val container: AppContainer) : ViewModel() {
    val state: StateFlow<PlayerUiState> = container.playerController.state
    val queue: StateFlow<List<QueueItem>> = container.playerController.queue
    val allTracks: StateFlow<List<TrackEntity>> = container.libraryRepository.observeTracks()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    /**
     * The cached row for the playing track. Now Playing needs it for two things the MediaSession
     * metadata does not carry: the artwork URL and the current favourite flag. Re-subscribing on
     * every track change keeps the heart correct as the queue advances.
     */
    val currentTrack: StateFlow<TrackEntity?> = state
        .flatMapLatest { current ->
            val id = current.mediaId
            if (id == null) flowOf(null) else container.libraryRepository.observeTrack(id)
        }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    init {
        viewModelScope.launch { container.playerController.connect() }
    }

    fun togglePlayPause() = viewModelScope.launch { container.playerController.playPause() }
    fun next() = viewModelScope.launch { container.playerController.next() }
    fun previous() = viewModelScope.launch { container.playerController.previous() }
    fun seekTo(positionMs: Long) = viewModelScope.launch { container.playerController.seekTo(positionMs) }
    fun toggleShuffle() = viewModelScope.launch { container.playerController.toggleShuffle() }
    fun cycleRepeat() = viewModelScope.launch { container.playerController.cycleRepeatMode() }

    fun playQueueItem(index: Int) = viewModelScope.launch {
        container.playerController.seekToQueueIndex(index)
    }

    fun addToQueue(track: TrackEntity) = viewModelScope.launch {
        container.playerController.addToQueue(track)
    }

    fun removeFromQueue(index: Int) = viewModelScope.launch {
        container.playerController.removeFromQueue(index)
    }

    fun toggleFavorite() {
        val track = currentTrack.value ?: return
        viewModelScope.launch {
            container.libraryRepository.setFavorite(track.id, !track.isFavorite)
        }
    }
}
