package com.musichub.mobile.data

import android.content.Context
import android.os.Build
import java.util.UUID

/**
 * Stable, non-secret installation identity. Used as a device label and in playback events.
 */
class DeviceIdentity(context: Context) {
    private val preferences = context.getSharedPreferences("musichub_device", Context.MODE_PRIVATE)

    val id: String = preferences.getString(KEY_ID, null)
        ?: UUID.randomUUID().toString().also { preferences.edit().putString(KEY_ID, it).apply() }

    val displayName: String = runCatching {
        "${Build.MANUFACTURER.orEmpty().trim()} ${Build.MODEL.orEmpty().trim()} (Android ${Build.VERSION.RELEASE})"
            .trim()
    }.getOrDefault("MusicHub Android")

    private companion object {
        const val KEY_ID = "installation_id"
    }
}
