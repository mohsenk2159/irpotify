package com.musichub.mobile.data.auth

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/**
 * Refresh tokens are the only long-lived credentials. They live in EncryptedSharedPreferences,
 * backed by an AES-256-GCM Android Keystore master key. Access tokens are never persisted.
 *
 * No token value is ever logged, included in exception messages, or exposed in UI state.
 */
class SecureTokenStore(context: Context) {
    private val preferences: SharedPreferences = createPreferences(context)

    fun getRefreshToken(profileId: String): String? =
        preferences.getString(key(profileId), null)

    fun setRefreshToken(profileId: String, token: String) {
        preferences.edit().putString(key(profileId), token).apply()
    }

    fun clear(profileId: String) {
        preferences.edit().remove(key(profileId)).apply()
    }

    fun clearAll() {
        preferences.edit().clear().apply()
    }

    private fun key(profileId: String): String = "refresh_token_$profileId"

    private fun createPreferences(context: Context): SharedPreferences {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        return EncryptedSharedPreferences.create(
            context,
            FILE_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    private companion object {
        const val FILE_NAME = "musichub_secure_tokens"
    }
}
