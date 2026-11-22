package com.musichub.mobile.data.auth

import com.musichub.mobile.core.LocalNetworkPolicy
import com.musichub.mobile.core.RefreshedTokens
import com.musichub.mobile.core.SingleFlightTokenRefresher
import com.musichub.mobile.core.UrlValidationFailure
import com.musichub.mobile.core.UrlValidationResult
import com.musichub.mobile.data.api.ApiCallException
import com.musichub.mobile.data.api.ApiFactory
import com.musichub.mobile.data.api.MusicHubApi
import com.musichub.mobile.data.api.RefreshDelegate
import com.musichub.mobile.data.api.TokenHolder
import com.musichub.mobile.data.api.models.ChangePasswordRequest
import com.musichub.mobile.data.api.models.LoginRequest
import com.musichub.mobile.data.api.models.PairingClaimRequest
import com.musichub.mobile.data.api.models.RefreshRequest
import com.musichub.mobile.data.api.models.TokenResponse
import com.musichub.mobile.data.api.models.UserDto
import com.musichub.mobile.data.api.runApi
import com.musichub.mobile.data.settings.ServerProfile
import com.musichub.mobile.data.settings.SettingsStore
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import retrofit2.HttpException
import java.io.IOException
import java.net.URI
import java.time.Instant
import java.time.OffsetDateTime
import java.util.concurrent.TimeUnit

enum class SessionEndReason { NONE, LOGGED_OUT, EXPIRED, NETWORK_ERROR }

class ServerUrlException(val reason: UrlValidationFailure) : Exception(reason.name)

sealed interface SessionState {
    data object Loading : SessionState
    data class SignedOut(
        val profile: ServerProfile? = null,
        val reason: SessionEndReason = SessionEndReason.NONE,
    ) : SessionState

    data class SignedIn(val profile: ServerProfile, val user: UserDto) : SessionState
}

/**
 * Owns the access token (memory only), the refresh token (EncryptedSharedPreferences), server
 * profiles, and session transitions. Every refresh goes through [SingleFlightTokenRefresher].
 */
class SessionManager(
    private val settingsStore: SettingsStore,
    private val secureTokenStore: SecureTokenStore,
    private val apiFactory: ApiFactory,
    private val tokenHolder: TokenHolder,
    private val refreshDelegate: RefreshDelegate,
    private val scope: CoroutineScope,
) {
    private val _state = MutableStateFlow<SessionState>(SessionState.Loading)
    val state: StateFlow<SessionState> = _state.asStateFlow()

    @Volatile
    private var activeProfile: ServerProfile? = null

    @Volatile
    private var lastRefreshedUser: UserDto? = null

    @Volatile
    private var lastRefreshFailure: SessionEndReason = SessionEndReason.NONE

    private val initialized = CompletableDeferred<Unit>()

    private val refresher = SingleFlightTokenRefresher(
        currentAccessToken = { tokenHolder.accessToken },
        refreshCall = { performRefreshCall() },
        onRefreshed = { applyRefreshedTokens(it) },
    )

    init {
        refreshDelegate.handler = { failedAccessToken ->
            // OkHttp Authenticator runs on a background thread; blocking here is expected and
            // serialized by the refresher mutex.
            runBlocking { refresher.refresh(failedAccessToken) }
        }
        scope.launch { initialize() }
    }

    fun currentProfile(): ServerProfile? = activeProfile

    fun currentBaseUrl(): String? = activeProfile?.baseUrl

    fun accessToken(): String? = tokenHolder.accessToken

    fun isOffline(): Boolean =
        _state.value is SessionState.SignedIn && tokenHolder.accessToken.isNullOrBlank()

    suspend fun awaitInitialized() {
        initialized.await()
    }

    /**
     * Used by WorkManager workers that may start in a fresh process: wait for session load, then
     * rotate the access token if required. Never logs or returns token values.
     */
    suspend fun ensureAuthenticated(): Boolean {
        awaitInitialized()
        if (_state.value is SessionState.SignedIn && !tokenHolder.accessToken.isNullOrBlank()) return true

        val profile = activeProfile ?: settingsStore.settings.value.activeProfile ?: return false
        activeProfile = profile
        if (secureTokenStore.getRefreshToken(profile.id) == null) return false
        return refresher.refresh(tokenHolder.accessToken)
    }

    suspend fun login(
        rawServerUrl: String,
        allowCleartext: Boolean,
        email: String,
        password: String,
        deviceName: String,
        lanBaseUrl: String? = null,
        internetBaseUrl: String? = null,
    ): Result<Unit> {
        settingsStore.awaitLoaded()
        val validation = LocalNetworkPolicy.validateBaseUrl(rawServerUrl, allowCleartext)
        if (validation is UrlValidationResult.Invalid) {
            return Result.failure(ServerUrlException(validation.reason))
        }
        val valid = validation as UrlValidationResult.Valid
        return try {
            val response = runApi {
                apiFactory.publicApi(valid.baseUrl).login(
                    LoginRequest(
                        email = email.trim(),
                        password = password,
                        deviceName = deviceName,
                        clientType = CLIENT_TYPE_PLAYER,
                    ),
                )
            }
            persistSession(
                baseUrl = valid.baseUrl,
                cleartext = valid.cleartext,
                response = response,
                lanBaseUrl = lanBaseUrl?.trim()?.takeIf { it.isNotBlank() },
                internetBaseUrl = internetBaseUrl?.trim()?.takeIf { it.isNotBlank() },
            )
            Result.success(Unit)
        } catch (exception: ApiCallException) {
            Result.failure(exception)
        }
    }

    /**
     * Re-probes the saved endpoints (LAN first) and, when a different one answers, moves the
     * active base URL onto it. Returns the base URL now in use, or null when none is reachable.
     * Used when the app comes back to the foreground or recovers from a network error.
     */
    suspend fun revalidateEndpoint(allowCleartext: Boolean): String? {
        val profile = activeProfile ?: return null
        val candidates = profile.candidateBaseUrls()
        if (candidates.size <= 1) {
            return profile.baseUrl.takeIf { probeServer(it, allowCleartext) }
        }
        for (candidate in candidates) {
            if (!probeServer(candidate, allowCleartext)) continue
            if (candidate != profile.baseUrl) {
                val updated = profile.copy(baseUrl = candidate)
                activeProfile = updated
                settingsStore.saveProfile(updated, makeActive = true)
                val state = _state.value
                if (state is SessionState.SignedIn) {
                    _state.value = SessionState.SignedIn(updated, state.user)
                }
            }
            return candidate
        }
        return null
    }

    /**
     * Probes a server base URL with GET /api/healthz. Uses a 2-second timeout so LAN/probing never hangs.
     */
    suspend fun probeServer(rawServerUrl: String, allowCleartext: Boolean): Boolean {
        settingsStore.awaitLoaded()
        val validation = LocalNetworkPolicy.validateBaseUrl(rawServerUrl, allowCleartext)
        if (validation is UrlValidationResult.Invalid) return false
        val valid = validation as UrlValidationResult.Valid
        return withContext(Dispatchers.IO) {
            try {
                val probeClient = apiFactory.publicClient.newBuilder()
                    .connectTimeout(2, TimeUnit.SECONDS)
                    .readTimeout(2, TimeUnit.SECONDS)
                    .build()
                val request = okhttp3.Request.Builder()
                    .url(LocalNetworkPolicy.apiBaseUrl(valid.baseUrl) + "api/healthz")
                    .get()
                    .build()
                probeClient.newCall(request).execute().use { response ->
                    response.isSuccessful
                }
            } catch (_: Exception) {
                false
            }
        }
    }

    /**
     * Changes the account password. The server rotates the token family and returns a fresh token
     * pair for this device, so the session stays signed in after a successful change.
     */
    suspend fun changePassword(
        currentPassword: String,
        newPassword: String,
        deviceName: String,
    ): Result<Unit> {
        val profile = activeProfile ?: return Result.failure(IllegalStateException("No active MusicHub server profile"))
        return try {
            val response = runApi {
                apiFactory.authenticatedApi(profile.baseUrl).changePassword(
                    ChangePasswordRequest(
                        currentPassword = currentPassword,
                        newPassword = newPassword,
                        deviceName = deviceName.ifBlank { profile.userDisplayName ?: deviceName },
                    ),
                )
            }
            persistSession(profile.baseUrl, profile.cleartext, response)
            Result.success(Unit)
        } catch (exception: ApiCallException) {
            Result.failure(exception)
        }
    }

    suspend fun claimPairing(
        rawServerUrl: String,
        allowCleartext: Boolean,
        code: String,
        deviceName: String,
        lanBaseUrl: String? = null,
        internetBaseUrl: String? = null,
    ): Result<Unit> {
        settingsStore.awaitLoaded()
        val targetServerUrl = if (!lanBaseUrl.isNullOrBlank() && probeServer(lanBaseUrl, allowCleartext)) {
            lanBaseUrl
        } else {
            rawServerUrl
        }
        val validation = LocalNetworkPolicy.validateBaseUrl(targetServerUrl, allowCleartext)
        if (validation is UrlValidationResult.Invalid) {
            return Result.failure(ServerUrlException(validation.reason))
        }
        val valid = validation as UrlValidationResult.Valid
        return try {
            val response = runApi {
                apiFactory.publicApi(valid.baseUrl).claimPairing(
                    PairingClaimRequest(code = code.trim(), deviceName = deviceName),
                )
            }
            persistSession(
                baseUrl = valid.baseUrl,
                cleartext = valid.cleartext,
                response = response,
                lanBaseUrl = lanBaseUrl?.trim()?.takeIf { it.isNotBlank() },
                internetBaseUrl = internetBaseUrl?.trim()?.takeIf { it.isNotBlank() } ?: rawServerUrl.trim().takeIf { it.isNotBlank() },
            )
            Result.success(Unit)
        } catch (exception: ApiCallException) {
            Result.failure(exception)
        }
    }

    suspend fun switchProfile(profileId: String): Result<Unit> {
        settingsStore.awaitLoaded()
        val profile = settingsStore.settings.value.profiles.firstOrNull { it.id == profileId }
            ?: return Result.failure(IllegalStateException("Unknown server profile"))
        activeProfile = profile
        tokenHolder.accessToken = null
        tokenHolder.expiresAtEpochSeconds = 0
        settingsStore.setActiveProfile(profile.id)

        if (secureTokenStore.getRefreshToken(profile.id) == null) {
            _state.value = SessionState.SignedOut(profile, SessionEndReason.NONE)
            return Result.success(Unit)
        }

        val refreshed = refresher.refresh(null)
        if (refreshed) {
            if (_state.value is SessionState.Loading || _state.value is SessionState.SignedOut) {
                val user = lastRefreshedUser
                if (user != null) _state.value = SessionState.SignedIn(profile, user)
            }
            return Result.success(Unit)
        }
        val reason = lastRefreshFailure.takeIf { it != SessionEndReason.NONE } ?: SessionEndReason.NETWORK_ERROR
        val cached = cachedUser(profile)
        if (reason == SessionEndReason.NETWORK_ERROR && cached != null) {
            _state.value = SessionState.SignedIn(profile, cached)
            return Result.success(Unit)
        }
        _state.value = SessionState.SignedOut(profile, reason)
        return Result.failure(ApiCallException(0, "Could not restore the session for this server."))    }

    suspend fun retrySavedSession(): Boolean {
        settingsStore.awaitLoaded()
        val profile = activeProfile ?: settingsStore.settings.value.activeProfile ?: return false
        activeProfile = profile
        if (secureTokenStore.getRefreshToken(profile.id) == null) return false
        return refresher.refresh(tokenHolder.accessToken)
    }

    suspend fun logout(allDevices: Boolean) {
        val profile = activeProfile
        val api = profile?.let { runCatching { apiFactory.authenticatedApi(it.baseUrl) }.getOrNull() }
        val refreshToken = profile?.let { secureTokenStore.getRefreshToken(it.id) }
        try {
            if (api != null) {
                if (allDevices) {
                    api.logoutAll()
                } else if (refreshToken != null) {
                    api.logout(RefreshRequest(refreshToken))
                }
            }
        } catch (_: Exception) {
            // Local credentials are cleared even if the server is unreachable.
        }
        clearSession(profile, SessionEndReason.LOGGED_OUT)
    }

    /** Forgets a saved profile and its refresh token without contacting the server. */
    suspend fun forgetProfile(profileId: String) {
        secureTokenStore.clear(profileId)
        settingsStore.removeProfile(profileId)
        if (activeProfile?.id == profileId) {
            activeProfile = null
            tokenHolder.accessToken = null
            _state.value = SessionState.SignedOut(null, SessionEndReason.LOGGED_OUT)
        }
    }

    private suspend fun initialize() {
        try {
            settingsStore.awaitLoaded()
            val profile = settingsStore.settings.value.activeProfile
            if (profile == null) {
                _state.value = SessionState.SignedOut(null, SessionEndReason.NONE)
                return
            }
            activeProfile = profile
            if (secureTokenStore.getRefreshToken(profile.id) == null) {
                _state.value = SessionState.SignedOut(profile, SessionEndReason.NONE)
                return
            }
            val refreshed = refresher.refresh(null)
            if (!refreshed) {
                val reason = lastRefreshFailure.takeIf { it != SessionEndReason.NONE } ?: SessionEndReason.NETWORK_ERROR
                val cached = cachedUser(profile)
                if (reason == SessionEndReason.NETWORK_ERROR && cached != null) {
                    // Offline start: keep the signed-in shell so cached metadata, the queue and
                    // downloaded files stay usable. No access token is fabricated or persisted.
                    _state.value = SessionState.SignedIn(profile, cached)
                } else {
                    _state.value = SessionState.SignedOut(profile, reason)
                }
                return
            }
            if (_state.value !is SessionState.SignedIn) {
                val user = runCatching { apiFactory.authenticatedApi(profile.baseUrl).me() }.getOrNull()
                if (user != null) {
                    _state.value = SessionState.SignedIn(profile, user)
                } else {
                    _state.value = SessionState.SignedOut(profile, SessionEndReason.EXPIRED)
                }
            }
        } finally {
            initialized.complete(Unit)
        }
    }

    private suspend fun persistSession(
        baseUrl: String,
        cleartext: Boolean,
        response: TokenResponse,
        lanBaseUrl: String? = null,
        internetBaseUrl: String? = null,
    ) {
        val settings = settingsStore.settings.value
        val existing = settings.profiles.firstOrNull {
            it.baseUrl == baseUrl && it.userId == response.user.id
        }
        val host = runCatching { URI(baseUrl).host }.getOrNull().orEmpty()
        val profile = (existing ?: ServerProfile(
            name = host.ifBlank { "MusicHub" },
            baseUrl = baseUrl,
            cleartext = cleartext,
        )).copy(
            baseUrl = baseUrl,
            cleartext = cleartext,
            // Keep previously saved endpoints when a caller (password change, pairing) does not
            // supply them, so a credential sign-in's LAN/internet pair is never silently erased.
            lanBaseUrl = lanBaseUrl ?: existing?.lanBaseUrl,
            internetBaseUrl = internetBaseUrl ?: existing?.internetBaseUrl,
            userId = response.user.id,
            userEmail = response.user.email,
            userDisplayName = response.user.displayName,
            lastUsedAt = System.currentTimeMillis(),
        )

        activeProfile = profile
        tokenHolder.accessToken = response.accessToken
        tokenHolder.expiresAtEpochSeconds = parseEpochSeconds(response.accessTokenExpiresAt)
        secureTokenStore.setRefreshToken(profile.id, response.refreshToken)
        settingsStore.saveProfile(profile, makeActive = true)
        _state.value = SessionState.SignedIn(profile, response.user)
    }

    private suspend fun performRefreshCall(): RefreshedTokens? {
        val profile = activeProfile ?: return null
        val refreshToken = secureTokenStore.getRefreshToken(profile.id) ?: return null
        lastRefreshFailure = SessionEndReason.NONE
        return try {
            val response = runApi {
                apiFactory.publicApi(profile.baseUrl).refresh(RefreshRequest(refreshToken))
            }
            lastRefreshedUser = response.user
            RefreshedTokens(
                accessToken = response.accessToken,
                accessTokenExpiresAtEpochSeconds = parseEpochSeconds(response.accessTokenExpiresAt),
                refreshToken = response.refreshToken,
            )
        } catch (exception: ApiCallException) {
            if (exception.statusCode == 401) {
                lastRefreshFailure = SessionEndReason.EXPIRED
                secureTokenStore.clear(profile.id)
                tokenHolder.accessToken = null
                tokenHolder.expiresAtEpochSeconds = 0
                _state.value = SessionState.SignedOut(profile, SessionEndReason.EXPIRED)
            } else {
                lastRefreshFailure = SessionEndReason.NETWORK_ERROR
            }
            null
        } catch (exception: HttpException) {
            lastRefreshFailure = SessionEndReason.NETWORK_ERROR
            null
        } catch (exception: IOException) {
            lastRefreshFailure = SessionEndReason.NETWORK_ERROR
            null
        }
    }

    private fun applyRefreshedTokens(tokens: RefreshedTokens) {
        tokenHolder.accessToken = tokens.accessToken
        tokenHolder.expiresAtEpochSeconds = tokens.accessTokenExpiresAtEpochSeconds
        val profile = activeProfile
        if (profile != null) {
            tokens.refreshToken?.takeIf { it.isNotBlank() }?.let {
                secureTokenStore.setRefreshToken(profile.id, it)
            }
        }
        val user = lastRefreshedUser
        if (profile != null && user != null) {
            val updated = profile.copy(
                userId = user.id,
                userEmail = user.email,
                userDisplayName = user.displayName,
                lastUsedAt = System.currentTimeMillis(),
            )
            activeProfile = updated
            _state.value = SessionState.SignedIn(updated, user)
            scope.launch { settingsStore.saveProfile(updated, makeActive = true) }
        }
    }

    private fun clearSession(profile: ServerProfile?, reason: SessionEndReason) {
        if (profile != null) secureTokenStore.clear(profile.id)
        tokenHolder.accessToken = null
        tokenHolder.expiresAtEpochSeconds = 0
        lastRefreshedUser = null
        lastRefreshFailure = SessionEndReason.NONE
        _state.value = SessionState.SignedOut(profile, reason)
    }

    companion object {
        const val CLIENT_TYPE_PLAYER = "player"

        fun parseEpochSeconds(raw: String?): Long {
            if (raw.isNullOrBlank()) return 0
            return runCatching { OffsetDateTime.parse(raw).toEpochSecond() }
                .recoverCatching { Instant.parse(raw).epochSecond }
                .getOrDefault(0)
        }

        /**
         * Reconstructs the minimal user identity cached in a server profile. Used only for an
         * offline start where the server cannot be reached; it is never treated as an authenticated
         * API identity and carries no token.
         */
        private fun cachedUser(profile: ServerProfile): UserDto? {
            val userId = profile.userId ?: return null
            val email = profile.userEmail ?: return null
            return UserDto(
                id = userId,
                email = email,
                displayName = profile.userDisplayName ?: email,
            )
        }
    }
}
