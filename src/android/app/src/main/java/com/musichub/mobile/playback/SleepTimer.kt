package com.musichub.mobile.playback

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

object PlaybackCommands {
    const val ACTION_SLEEP_TIMER = "com.musichub.mobile.SLEEP_TIMER"
    const val ARG_MINUTES = "minutes"
}

object SleepTimerBridge {
    private val _deadlineEpochMs = MutableStateFlow<Long?>(null)
    val deadlineEpochMs: StateFlow<Long?> = _deadlineEpochMs.asStateFlow()

    fun set(minutes: Int) {
        _deadlineEpochMs.value = if (minutes <= 0) null else System.currentTimeMillis() + minutes * 60_000L
    }

    fun clear() {
        _deadlineEpochMs.value = null
    }
}
