package com.musichub.mobile.core

import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * Result of a refresh-token exchange. Kept free of Android and HTTP types so the single-flight
 * behavior is unit testable on the JVM.
 */
data class RefreshedTokens(
    val accessToken: String,
    val accessTokenExpiresAtEpochSeconds: Long,
    val refreshToken: String?,
)

/**
 * Ensures that concurrent 401 responses cause exactly one refresh network call.
 *
 * The OkHttp [okhttp3.Authenticator] runs on OkHttp worker threads and calls [refresh] through
 * runBlocking. Whichever caller wins the mutex performs the refresh; every other caller waits and
 * then observes the rotated access token, so it does not issue a second refresh.
 */
class SingleFlightTokenRefresher(
    private val currentAccessToken: () -> String?,
    private val refreshCall: suspend () -> RefreshedTokens?,
    private val onRefreshed: (RefreshedTokens) -> Unit,
) {
    private val mutex = Mutex()

    @Volatile
    var lastFailure: Throwable? = null
        private set

    suspend fun refresh(failedAccessToken: String?): Boolean {
        // Another request may already have rotated the token while this request was in flight.
        currentAccessToken()?.let { current ->
            if (current != failedAccessToken) return true
        }

        return mutex.withLock {
            currentAccessToken()?.let { current ->
                if (current != failedAccessToken) return@withLock true
            }

            val refreshed = try {
                refreshCall()
            } catch (throwable: Throwable) {
                lastFailure = throwable
                null
            }

            if (refreshed == null) {
                false
            } else {
                lastFailure = null
                onRefreshed(refreshed)
                true
            }
        }
    }
}
