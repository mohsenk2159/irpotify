package com.musichub.mobile.playback

import android.app.PendingIntent
import android.content.Intent
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.Player
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import com.musichub.mobile.MainActivity
import com.musichub.mobile.MusicHubApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

object PlaybackControlBridge {
    @Volatile
    var setSleepTimer: ((minutes: Int) -> Unit)? = null
}

class PlaybackService : MediaSessionService() {
    private var player: ExoPlayer? = null
    private var mediaSession: MediaSession? = null
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var sleepJob: Job? = null

    override fun onCreate() {
        super.onCreate()
        val container = (application as MusicHubApp).container

        val dataSourceFactory = MusicHubDataSource.Factory(
            client = container.apiFactory.authenticatedClient,
            baseUrlProvider = { container.sessionManager.currentBaseUrl() },
        )
        val mediaSourceFactory = DefaultMediaSourceFactory(DefaultDataSource.Factory(this, dataSourceFactory))

        val exoPlayer = ExoPlayer.Builder(this)
            .setMediaSourceFactory(mediaSourceFactory)
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(C.USAGE_MEDIA)
                    .setContentType(C.AUDIO_CONTENT_TYPE_MUSIC)
                    .build(),
                true,
            )
            .setHandleAudioBecomingNoisy(true)
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .build()

        exoPlayer.addListener(object : Player.Listener {
            override fun onAudioSessionIdChanged(audioSessionId: Int) {
                EqualizerBridge.attach(audioSessionId)
            }
        })

        val sessionActivity = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        mediaSession = MediaSession.Builder(this, exoPlayer)
            .setSessionActivity(sessionActivity)
            .build()

        player = exoPlayer
        PlaybackControlBridge.setSleepTimer = { minutes -> setSleepTimer(minutes) }

        serviceScope.launch {
            container.settingsStore.settings.collect { settings ->
                EqualizerBridge.setDesired(settings.equalizerEnabled, settings.equalizerBands)
            }
        }
        EqualizerBridge.setDesired(
            container.settingsStore.settings.value.equalizerEnabled,
            container.settingsStore.settings.value.equalizerBands,
        )
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = mediaSession

    override fun onTaskRemoved(rootIntent: Intent?) {
        val current = player
        if (current == null || !current.playWhenReady || current.playbackState == Player.STATE_IDLE) {
            stopSelf()
        }
    }

    override fun onDestroy() {
        PlaybackControlBridge.setSleepTimer = null
        sleepJob?.cancel()
        SleepTimerBridge.clear()
        EqualizerBridge.release()
        mediaSession?.release()
        mediaSession = null
        player?.release()
        player = null
        serviceScope.cancel()
        super.onDestroy()
    }

    private fun setSleepTimer(minutes: Int) {
        sleepJob?.cancel()
        if (minutes <= 0) {
            SleepTimerBridge.clear()
            return
        }
        SleepTimerBridge.set(minutes)
        sleepJob = serviceScope.launch {
            delay(minutes * 60_000L)
            player?.pause()
            SleepTimerBridge.clear()
        }
    }
}
