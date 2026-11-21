package com.musichub.mobile.playback

import android.media.audiofx.Equalizer
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Wraps the platform Equalizer only when the device actually exposes the effect for the current
 * audio session. If construction fails, [supported] is false and the UI hides the controls; no
 * device-specific effects are claimed.
 */
object EqualizerBridge {
    private val lock = Any()
    private var equalizer: Equalizer? = null
    private var attachedSession = 0
    private var desiredEnabled = false
    private var desiredBands: List<Int> = emptyList()

    private val _supported = MutableStateFlow(false)
    val supported: StateFlow<Boolean> = _supported.asStateFlow()

    private val _enabled = MutableStateFlow(false)
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()

    private val _bands = MutableStateFlow<List<Int>>(emptyList())
    val bands: StateFlow<List<Int>> = _bands.asStateFlow()

    private val _bandCount = MutableStateFlow(0)
    val bandCount: StateFlow<Int> = _bandCount.asStateFlow()

    private val _bandRange = MutableStateFlow(-1500..1500)
    val bandRange: StateFlow<IntRange> = _bandRange.asStateFlow()

    fun attach(audioSessionId: Int) {
        synchronized(lock) {
            releaseLocked()
            attachedSession = audioSessionId
            if (audioSessionId == 0) {
                _supported.value = false
                return
            }
            try {
                val effect = Equalizer(0, audioSessionId)
                equalizer = effect
                val range = effect.bandLevelRange
                _bandRange.value = range[0].toInt()..range[1].toInt()
                _bandCount.value = effect.numberOfBands.toInt()
                _supported.value = effect.numberOfBands > 0
                _bands.value = List(effect.numberOfBands.toInt()) { index ->
                    effect.getBandLevel(index.toShort()).toInt()
                }
                applyDesiredLocked()
            } catch (_: RuntimeException) {
                equalizer = null
                _supported.value = false
            } catch (_: UnsupportedOperationException) {
                equalizer = null
                _supported.value = false
            }
        }
    }

    fun setDesired(enabled: Boolean, bands: List<Int>) {
        synchronized(lock) {
            desiredEnabled = enabled
            if (bands.isNotEmpty()) desiredBands = bands
            applyDesiredLocked()
        }
    }

    fun setBand(index: Int, level: Int) {
        synchronized(lock) {
            val effect = equalizer ?: return
            val range = _bandRange.value
            val safe = level.coerceIn(range.first, range.last)
            try {
                effect.setBandLevel(index.toShort(), safe.toShort())
                _bands.value = _bands.value.toMutableList().also {
                    if (index in it.indices) it[index] = safe
                }
                desiredBands = _bands.value
            } catch (_: RuntimeException) {
                // Band index unsupported on this device.
            }
        }
    }

    fun setEnabled(enabled: Boolean) {
        synchronized(lock) {
            desiredEnabled = enabled
            applyDesiredLocked()
        }
    }

    fun release() {
        synchronized(lock) { releaseLocked() }
    }

    private fun applyDesiredLocked() {
        val effect = equalizer ?: return
        try {
            effect.enabled = desiredEnabled
            if (desiredBands.isNotEmpty()) {
                val count = effect.numberOfBands.toInt()
                desiredBands.forEachIndexed { index, level ->
                    if (index < count) {
                        effect.setBandLevel(index.toShort(), level.coerceIn(-1500, 1500).toShort())
                    }
                }
                _bands.value = List(count) { effect.getBandLevel(it.toShort()).toInt() }
            }
            _enabled.value = effect.enabled
        } catch (_: RuntimeException) {
            _supported.value = false
        }
    }

    private fun releaseLocked() {
        try {
            equalizer?.enabled = false
        } catch (_: RuntimeException) {
            // Ignore teardown failures.
        }
        try {
            equalizer?.release()
        } catch (_: RuntimeException) {
            // Ignore teardown failures.
        }
        equalizer = null
        attachedSession = 0
        _enabled.value = false
        _supported.value = false
    }
}
