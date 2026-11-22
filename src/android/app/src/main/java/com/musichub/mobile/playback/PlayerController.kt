package com.musichub.mobile.playback

import android.content.ComponentName
import android.content.Context
import android.net.Uri
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import com.google.common.util.concurrent.ListenableFuture
import com.google.common.util.concurrent.MoreExecutors
import com.musichub.mobile.data.auth.SessionManager
import com.musichub.mobile.data.download.DownloadRepository
import com.musichub.mobile.data.library.LibraryRepository
import com.musichub.mobile.data.local.TrackEntity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

data class QueueItem(
    val index: Int,
    val mediaId: String,
    val title: String,
    val artist: String,
    val album: String,
    val durationMs: Long,
    val isCurrent: Boolean,
)

data class PlayerUiState(
    val connected: Boolean = false,
    val isPlaying: Boolean = false,
    val isBuffering: Boolean = false,
    val mediaId: String? = null,
    val title: String = "",
    val artist: String = "",
    val album: String = "",
    val positionMs: Long = 0,
    val durationMs: Long = 0,
    val queueSize: Int = 0,
    val hasNext: Boolean = false,
    val hasPrevious: Boolean = false,
    val shuffle: Boolean = false,
    val repeatMode: Int = Player.REPEAT_MODE_OFF,
    val error: String? = null,
)

class PlayerController(
    context: Context,
    private val sessionManager: SessionManager,
    private val libraryRepository: LibraryRepository,
    private val downloadRepository: DownloadRepository,
    private val deviceId: String,
) {
    private val appContext = context.applicationContext
    private val connectMutex = Mutex()
    private var future: ListenableFuture<MediaController>? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var progressJob: Job? = null
    private var positionTickerJob: Job? = null

    @Volatile
    private var lastReportedTrackId: String? = null

    @Volatile
    private var controller: MediaController? = null

    private val _state = MutableStateFlow(PlayerUiState())
    val state: StateFlow<PlayerUiState> = _state.asStateFlow()

    private val _queue = MutableStateFlow<List<QueueItem>>(emptyList())
    val queue: StateFlow<List<QueueItem>> = _queue.asStateFlow()

    private val listener = object : Player.Listener {
        override fun onEvents(player: Player, events: Player.Events) {
            publish(player)
        }

        override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
            val trackId = mediaItem?.mediaId ?: return
            if (trackId != lastReportedTrackId) {
                lastReportedTrackId = trackId
                scope.launch(Dispatchers.IO) {
                    libraryRepository.recordPlayback(
                        trackId = trackId,
                        positionSeconds = 0.0,
                        completed = false,
                        deviceId = deviceId,
                    )
                }
            }
        }

        override fun onPlaybackStateChanged(playbackState: Int) {
            if (playbackState == Player.STATE_ENDED) {
                val trackId = controller?.currentMediaItem?.mediaId ?: return
                scope.launch(Dispatchers.IO) {
                    libraryRepository.recordPlayback(
                        trackId = trackId,
                        positionSeconds = controller?.duration?.div(1000.0) ?: 0.0,
                        completed = true,
                        deviceId = deviceId,
                    )
                }
            }
        }

        override fun onPlayerError(error: PlaybackException) {
            _state.value = _state.value.copy(
                error = error.errorCodeName,
                isBuffering = false,
            )
        }
    }

    suspend fun connect(): Boolean = connectMutex.withLock {
        controller?.let { return@withLock true }
        val token = SessionToken(
            appContext,
            ComponentName(appContext, PlaybackService::class.java),
        )
        val pending = future ?: MediaController.Builder(appContext, token).buildAsync().also { future = it }
        val connected = suspendCancellableCoroutine<MediaController?> { continuation ->
            pending.addListener(
                {
                    val result = runCatching { pending.get() }.getOrNull()
                    if (continuation.isActive) continuation.resume(result)
                },
                MoreExecutors.directExecutor(),
            )
        } ?: return@withLock false
        connected.addListener(listener)
        controller = connected
        publish(connected)
        startProgressPersistence()
        true
    }

    private fun startProgressPersistence() {
        if (progressJob != null) return
        progressJob = scope.launch {
            while (true) {
                delay(10_000)
                val player = controller ?: continue
                if (!player.isPlaying) continue
                val trackId = player.currentMediaItem?.mediaId ?: continue
                val positionMs = player.currentPosition.coerceAtLeast(0)
                withContext(Dispatchers.IO) {
                    libraryRepository.savePlaybackPosition(trackId, positionMs)
                }
            }
        }
        startPositionTicker()
    }

    /**
     * Publishes the playhead to the UI roughly every second so the seek bar advances. Kept separate
     * from persistence: this is display-only and must not touch disk or the network.
     */
    private fun startPositionTicker() {
        if (positionTickerJob != null) return
        positionTickerJob = scope.launch {
            while (true) {
                delay(1_000)
                val player = controller ?: continue
                if (!player.isPlaying) continue
                val current = _state.value
                if (!current.connected) continue
                val position = player.currentPosition.coerceAtLeast(0)
                if (position != current.positionMs) {
                    _state.value = current.copy(positionMs = position)
                }
            }
        }
    }

    fun currentPositionMs(): Long = controller?.currentPosition?.coerceAtLeast(0) ?: 0

    suspend fun playTracks(tracks: List<TrackEntity>, startIndex: Int) {
        if (tracks.isEmpty()) return
        val mediaController = awaitController() ?: return
        val profileId = sessionManager.currentProfile()?.id
        val items = withContext(Dispatchers.IO) {
            tracks.map { track ->
                val local = profileId?.let { downloadRepository.localFile(it, track.id) }
                val uri = if (local != null) Uri.fromFile(local) else Uri.parse("musichub://track/${track.id}")
                val metadata = MediaMetadata.Builder()
                    .setTitle(track.title)
                    .setArtist(track.artist)
                    .setAlbumTitle(track.album)
                    .setDurationMs(track.durationMs)
                    .build()
                MediaItem.Builder()
                    .setMediaId(track.id)
                    .setUri(uri)
                    .setMediaMetadata(metadata)
                    .build()
            }
        }
        val index = startIndex.coerceIn(0, items.lastIndex)
        val resumeMs = withContext(Dispatchers.IO) {
            libraryRepository.playbackPosition(tracks[index].id)
        }.coerceIn(0, tracks[index].durationMs.coerceAtLeast(0))
        withContext(Dispatchers.Main) {
            mediaController.setMediaItems(items, index, resumeMs)
            mediaController.prepare()
            mediaController.play()
        }
        withContext(Dispatchers.IO) { libraryRepository.saveQueue(tracks) }
    }

    suspend fun playPause() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) {
            if (player.isPlaying) player.pause() else player.play()
        }
    }

    suspend fun play() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) { player.play() }
    }

    suspend fun pause() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) { player.pause() }
    }

    suspend fun seekTo(positionMs: Long) {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) { player.seekTo(positionMs.coerceAtLeast(0)) }
    }

    suspend fun next() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) {
            if (player.hasNextMediaItem()) player.seekToNextMediaItem()
        }
    }

    suspend fun previous() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) {
            if (player.currentPosition > 5_000) {
                player.seekTo(0)
            } else if (player.hasPreviousMediaItem()) {
                player.seekToPreviousMediaItem()
            } else {
                player.seekTo(0)
            }
        }
    }

    suspend fun seekToQueueIndex(index: Int) {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) {
            if (index in 0 until player.mediaItemCount) {
                player.seekTo(index, 0)
                player.prepare()
                player.play()
            }
        }
    }

    suspend fun toggleShuffle() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) {
            player.shuffleModeEnabled = !player.shuffleModeEnabled
        }
    }

    suspend fun cycleRepeatMode() {
        val player = awaitController() ?: return
        withContext(Dispatchers.Main) {
            player.repeatMode = when (player.repeatMode) {
                Player.REPEAT_MODE_OFF -> Player.REPEAT_MODE_ALL
                Player.REPEAT_MODE_ALL -> Player.REPEAT_MODE_ONE
                else -> Player.REPEAT_MODE_OFF
            }
        }
    }

    fun setSleepTimer(minutes: Int) {
        PlaybackControlBridge.setSleepTimer?.invoke(minutes)
        if (minutes <= 0) SleepTimerBridge.clear()
    }

    suspend fun clear() {
        withContext(Dispatchers.Main) {
            controller?.stop()
            controller?.clearMediaItems()
            publish(controller)
        }
        withContext(Dispatchers.IO) { libraryRepository.saveQueue(emptyList()) }
    }

    private suspend fun awaitController(): MediaController? {
        controller?.let { return it }
        return if (connect()) controller else null
    }

    private fun publish(player: Player?) {
        if (player == null) {
            _state.value = PlayerUiState()
            _queue.value = emptyList()
            return
        }
        val metadata = player.currentMediaItem?.mediaMetadata
        val duration = player.duration.takeIf { it > 0 } ?: metadata?.durationMs ?: 0
        _state.value = PlayerUiState(
            connected = true,
            isPlaying = player.isPlaying,
            isBuffering = player.playbackState == Player.STATE_BUFFERING,
            mediaId = player.currentMediaItem?.mediaId,
            title = metadata?.title?.toString().orEmpty(),
            artist = metadata?.artist?.toString().orEmpty(),
            album = metadata?.albumTitle?.toString().orEmpty(),
            positionMs = player.currentPosition.coerceAtLeast(0),
            durationMs = duration,
            queueSize = player.mediaItemCount,
            hasNext = player.hasNextMediaItem(),
            hasPrevious = player.hasPreviousMediaItem(),
            shuffle = player.shuffleModeEnabled,
            repeatMode = player.repeatMode,
            error = null,
        )
        val currentIndex = player.currentMediaItemIndex
        _queue.value = (0 until player.mediaItemCount).map { index ->
            val item = player.getMediaItemAt(index)
            val itemMetadata = item.mediaMetadata
            QueueItem(
                index = index,
                mediaId = item.mediaId,
                title = itemMetadata.title?.toString().orEmpty(),
                artist = itemMetadata.artist?.toString().orEmpty(),
                album = itemMetadata.albumTitle?.toString().orEmpty(),
                durationMs = itemMetadata.durationMs ?: 0,
                isCurrent = index == currentIndex,
            )
        }
    }
}
