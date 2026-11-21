package com.musichub.mobile.data

import android.content.Context
import com.musichub.mobile.data.api.ApiFactory
import com.musichub.mobile.data.api.ArtworkLoader
import com.musichub.mobile.data.api.RefreshDelegate
import com.musichub.mobile.data.api.TokenHolder
import com.musichub.mobile.data.auth.SecureTokenStore
import com.musichub.mobile.data.auth.SessionManager
import com.musichub.mobile.data.auth.SessionState
import com.musichub.mobile.data.download.DownloadRepository
import com.musichub.mobile.data.library.LibraryRepository
import com.musichub.mobile.data.local.MusicHubDatabase
import com.musichub.mobile.data.settings.SettingsStore
import com.musichub.mobile.data.sync.SyncScheduler
import com.musichub.mobile.playback.PlayerController
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * Manual dependency container. No global mutable service locator beyond the Application instance,
 * which keeps unit tests and WorkManager process restarts predictable.
 */
class AppContainer(context: Context) {
    private val appContext = context.applicationContext
    val context: Context get() = appContext
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    val deviceIdentity = DeviceIdentity(appContext)
    val settingsStore = SettingsStore(appContext, scope)
    val secureTokenStore = SecureTokenStore(appContext)
    val tokenHolder = TokenHolder()
    val refreshDelegate = RefreshDelegate()
    val apiFactory = ApiFactory(
        deviceLabel = "MusicHub-Android/${com.musichub.mobile.BuildConfig.VERSION_NAME} (${deviceIdentity.displayName})",
        settingsProvider = { settingsStore.settings.value },
        tokenHolder = tokenHolder,
        refreshDelegate = refreshDelegate,
    )
    val artworkLoader = ArtworkLoader(
        client = apiFactory.authenticatedClient,
        baseUrlProvider = { sessionManager.currentBaseUrl() },
    )
    val database = MusicHubDatabase.build(appContext)
    val syncScheduler = SyncScheduler(appContext)
    val sessionManager = SessionManager(
        settingsStore = settingsStore,
        secureTokenStore = secureTokenStore,
        apiFactory = apiFactory,
        tokenHolder = tokenHolder,
        refreshDelegate = refreshDelegate,
        scope = scope,
    )
    val libraryRepository = LibraryRepository(
        database = database,
        apiFactory = apiFactory,
        sessionManager = sessionManager,
        syncScheduler = syncScheduler,
    )
    val downloadRepository = DownloadRepository(
        context = appContext,
        database = database,
        sessionManager = sessionManager,
        settingsStore = settingsStore,
        apiFactory = apiFactory,
    )
    val playerController = PlayerController(
        context = appContext,
        sessionManager = sessionManager,
        libraryRepository = libraryRepository,
        downloadRepository = downloadRepository,
        deviceId = deviceIdentity.id,
    )

    private val cacheScopePreferences =
        appContext.getSharedPreferences("musichub_cache_scope", Context.MODE_PRIVATE)

    init {
        syncScheduler.schedulePeriodic()
        observeSession()
        scope.launch {
            sessionManager.awaitInitialized()
            if (sessionManager.state.value is SessionState.SignedIn) {
                syncScheduler.scheduleNow()
            }
        }
    }

    private fun observeSession() {
        scope.launch {
            sessionManager.state.collect { state ->
                if (state !is SessionState.SignedIn) return@collect
                val profileId = state.profile.id
                val previous = cacheScopePreferences.getString(KEY_CACHE_PROFILE, null)
                if (previous != null && previous != profileId) {
                    libraryRepository.clearCache()
                    playerController.clear()
                }
                cacheScopePreferences.edit().putString(KEY_CACHE_PROFILE, profileId).apply()
            }
        }
    }

    private companion object {
        const val KEY_CACHE_PROFILE = "cache_profile_id"
    }
}
