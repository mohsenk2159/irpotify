package com.musichub.mobile.data.settings

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.util.UUID

private val Context.musicHubDataStore: DataStore<Preferences> by preferencesDataStore(name = "musichub_settings")

/**
 * A saved server binding.
 *
 * [baseUrl] is the address currently in use. [lanBaseUrl] and [internetBaseUrl] keep the addresses
 * the user entered at sign-in so the client can fall back between the home network and the
 * internet without asking again. Either endpoint may be absent; at least one always exists.
 */
@Serializable
data class ServerProfile(
    val id: String = UUID.randomUUID().toString(),
    val name: String = "",
    val baseUrl: String,
    val cleartext: Boolean = false,
    val lanBaseUrl: String? = null,
    val internetBaseUrl: String? = null,
    val userId: String? = null,
    val userEmail: String? = null,
    val userDisplayName: String? = null,
    val lastUsedAt: Long = System.currentTimeMillis(),
) {
    /** Distinct saved endpoints, LAN first. Used for connect-time probing. */
    fun candidateBaseUrls(): List<String> =
        listOfNotNull(lanBaseUrl?.takeIf { it.isNotBlank() }, internetBaseUrl?.takeIf { it.isNotBlank() })
            .distinct()
            .ifEmpty { listOf(baseUrl) }
}

enum class ThemeMode { SYSTEM, LIGHT, DARK }

enum class AppLanguage(val tag: String) {
    SYSTEM(""),
    ENGLISH("en"),
    PERSIAN("fa"),
}

data class AppSettings(
    val profiles: List<ServerProfile> = emptyList(),
    val activeProfileId: String? = null,
    val themeMode: ThemeMode = ThemeMode.SYSTEM,
    val language: AppLanguage = AppLanguage.SYSTEM,
    val allowCleartext: Boolean = true,
    val downloadWifiOnly: Boolean = true,
    val equalizerEnabled: Boolean = false,
    val equalizerBands: List<Int> = emptyList(),
) {
    val activeProfile: ServerProfile?
        get() = profiles.firstOrNull { it.id == activeProfileId }
}

/**
 * DataStore-backed settings and server profiles. A StateFlow mirror is kept so OkHttp interceptors
 * can enforce the cleartext policy synchronously on their worker threads.
 */
class SettingsStore(
    context: Context,
    scope: CoroutineScope,
) {
    private val json = Json { ignoreUnknownKeys = true }
    private val dataStore = context.musicHubDataStore
    private val _settings = MutableStateFlow(AppSettings())
    val settings: StateFlow<AppSettings> = _settings.asStateFlow()
    private val loaded = CompletableDeferred<Unit>()

    init {
        scope.launch {
            dataStore.data
                .catch { emit(emptyPreferences()) }
                .collect { preferences ->
                    _settings.value = read(preferences)
                    loaded.complete(Unit)
                }
        }
    }

    suspend fun awaitLoaded() {
        loaded.await()
    }

    suspend fun update(transform: (AppSettings) -> AppSettings) {
        dataStore.edit { preferences ->
            write(preferences, transform(read(preferences)))
        }
    }

    suspend fun setTheme(mode: ThemeMode) = update { it.copy(themeMode = mode) }

    suspend fun setLanguage(language: AppLanguage) = update { it.copy(language = language) }

    suspend fun setAllowCleartext(enabled: Boolean) = update { it.copy(allowCleartext = enabled) }

    suspend fun setDownloadWifiOnly(enabled: Boolean) = update { it.copy(downloadWifiOnly = enabled) }

    suspend fun setEqualizer(enabled: Boolean, bands: List<Int>? = null) = update {
        it.copy(equalizerEnabled = enabled, equalizerBands = bands ?: it.equalizerBands)
    }

    suspend fun saveProfile(profile: ServerProfile, makeActive: Boolean = true) = update { current ->
        val existing = current.profiles
        val replaced = existing.filterNot { it.id == profile.id } + profile
        current.copy(
            profiles = replaced.sortedBy { it.name.lowercase() },
            activeProfileId = if (makeActive) profile.id else current.activeProfileId,
        )
    }

    suspend fun setActiveProfile(profileId: String?) = update { it.copy(activeProfileId = profileId) }

    suspend fun removeProfile(profileId: String) = update { current ->
        current.copy(
            profiles = current.profiles.filterNot { it.id == profileId },
            activeProfileId = if (current.activeProfileId == profileId) null else current.activeProfileId,
        )
    }

    suspend fun updateProfileUser(profileId: String, userId: String, email: String, displayName: String) = update {
        current ->
        current.copy(
            profiles = current.profiles.map {
                if (it.id == profileId) {
                    it.copy(userId = userId, userEmail = email, userDisplayName = displayName, lastUsedAt = System.currentTimeMillis())
                } else {
                    it
                }
            },
        )
    }

    private fun read(preferences: Preferences): AppSettings {
        val profiles = preferences[PROFILES]?.let { raw ->
            runCatching { json.decodeFromString<List<ServerProfile>>(raw) }.getOrDefault(emptyList())
        } ?: emptyList()

        val theme = runCatching {
            ThemeMode.valueOf(preferences[THEME] ?: ThemeMode.SYSTEM.name)
        }.getOrDefault(ThemeMode.SYSTEM)

        val language = runCatching {
            AppLanguage.valueOf(preferences[LANGUAGE] ?: AppLanguage.SYSTEM.name)
        }.getOrDefault(AppLanguage.SYSTEM)

        val bands = preferences[EQUALIZER_BANDS]
            ?.split(',')
            ?.mapNotNull { it.trim().toIntOrNull() }
            .orEmpty()

        return AppSettings(
            profiles = profiles,
            activeProfileId = preferences[ACTIVE_PROFILE],
            themeMode = theme,
            language = language,
            allowCleartext = preferences[ALLOW_CLEARTEXT] ?: true,
            downloadWifiOnly = preferences[DOWNLOAD_WIFI_ONLY] ?: true,
            equalizerEnabled = preferences[EQUALIZER_ENABLED] ?: false,
            equalizerBands = bands,
        )
    }

    private fun write(preferences: MutablePreferences, settings: AppSettings) {
        preferences[PROFILES] = json.encodeToString(settings.profiles)
        if (settings.activeProfileId == null) {
            preferences.remove(ACTIVE_PROFILE)
        } else {
            preferences[ACTIVE_PROFILE] = settings.activeProfileId
        }
        preferences[THEME] = settings.themeMode.name
        preferences[LANGUAGE] = settings.language.name
        preferences[ALLOW_CLEARTEXT] = settings.allowCleartext
        preferences[DOWNLOAD_WIFI_ONLY] = settings.downloadWifiOnly
        preferences[EQUALIZER_ENABLED] = settings.equalizerEnabled
        preferences[EQUALIZER_BANDS] = settings.equalizerBands.joinToString(",")
    }

    private companion object {
        val PROFILES = stringPreferencesKey("profiles_json")
        val ACTIVE_PROFILE = stringPreferencesKey("active_profile_id")
        val THEME = stringPreferencesKey("theme_mode")
        val LANGUAGE = stringPreferencesKey("language")
        val ALLOW_CLEARTEXT = booleanPreferencesKey("allow_cleartext")
        val DOWNLOAD_WIFI_ONLY = booleanPreferencesKey("download_wifi_only")
        val EQUALIZER_ENABLED = booleanPreferencesKey("equalizer_enabled")
        val EQUALIZER_BANDS = stringPreferencesKey("equalizer_bands")
        val SLEEP_TIMER_MINUTES = intPreferencesKey("sleep_timer_minutes")
    }
}
