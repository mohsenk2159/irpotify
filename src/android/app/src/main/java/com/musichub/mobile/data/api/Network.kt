package com.musichub.mobile.data.api

import com.musichub.mobile.core.LocalNetworkPolicy
import com.musichub.mobile.data.settings.AppSettings
import okhttp3.Authenticator
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.Route
import java.io.IOException
import java.util.concurrent.TimeUnit

class TokenHolder {
    @Volatile
    var accessToken: String? = null

    @Volatile
    var expiresAtEpochSeconds: Long = 0
}

class RefreshDelegate {
    @Volatile
    var handler: ((failedAccessToken: String?) -> Boolean)? = null
}

/**
 * Rejects cleartext HTTP unless the user enabled the local-network option and the host is a
 * loopback/private address. TLS verification is never changed or bypassed.
 */
class CleartextPolicyInterceptor(private val settingsProvider: () -> AppSettings) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        if (request.url.scheme == "http") {
            val settings = settingsProvider()
            if (!settings.allowCleartext) {
                throw IOException("Cleartext HTTP is disabled for this server. Enable the local-network option or use HTTPS.")
            }
        }
        return chain.proceed(request)
    }
}

class AuthInterceptor(
    private val tokenHolder: TokenHolder,
    private val deviceLabel: String,
) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): Response {
        val original = chain.request()
        val token = tokenHolder.accessToken
        val builder: Request.Builder = original.newBuilder()
            .header("Accept", "application/json")
            .header("User-Agent", deviceLabel)
        if (!token.isNullOrBlank() && original.header("Authorization") == null &&
            !original.url.encodedPath.startsWith("/api/auth/login") &&
            !original.url.encodedPath.startsWith("/api/auth/refresh")
        ) {
            builder.header("Authorization", "Bearer $token")
        }
        return chain.proceed(builder.build())
    }
}

/**
 * Handles 401 responses. The single-flight refresher (owned by SessionManager) guarantees that
 * a burst of failed requests produces one refresh call and every request is retried exactly once
 * with the rotated token.
 */
class AuthAuthenticator(
    private val tokenHolder: TokenHolder,
    private val refreshDelegate: RefreshDelegate,
) : Authenticator {
    override fun authenticate(route: Route?, response: Response): Request? {
        if (response.code != 401) return null
        if (response.priorResponse != null) return null
        val path = response.request.url.encodedPath
        // Only the credential endpoints must not recurse through the refresher: login has no token
        // yet and refresh would loop. The remaining /api/auth/ routes (me, sessions, logout,
        // change-password) are authenticated and must still be retried with a rotated token.
        if (path.startsWith("/api/auth/login") || path.startsWith("/api/auth/refresh")) return null

        val failedToken = response.request.header("Authorization")
            ?.removePrefix("Bearer ")
            ?.trim()
            ?.takeIf { it.isNotEmpty() }

        val handler = refreshDelegate.handler ?: return null
        if (!handler(failedToken)) return null

        val newToken = tokenHolder.accessToken ?: return null
        if (newToken == failedToken) return null

        return response.request.newBuilder()
            .header("Authorization", "Bearer $newToken")
            .build()
    }
}

class ApiFactory(
    deviceLabel: String,
    settingsProvider: () -> AppSettings,
    private val tokenHolder: TokenHolder,
    private val refreshDelegate: RefreshDelegate,
) {
    private val cleartextPolicy = CleartextPolicyInterceptor(settingsProvider)
    private val authInterceptor = AuthInterceptor(tokenHolder, deviceLabel)
    private val authenticator = AuthAuthenticator(tokenHolder, refreshDelegate)

    val authenticatedClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(90, TimeUnit.SECONDS)
        .writeTimeout(90, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .addInterceptor(cleartextPolicy)
        .addInterceptor(authInterceptor)
        .authenticator(authenticator)
        .build()

    /** Refresh calls must not recurse through the authenticator or attach an expired token. */
    val publicClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .addInterceptor(cleartextPolicy)
        .build()

    private val apiCache = LinkedHashMap<String, MusicHubApi>()

    fun authenticatedApi(serverUrl: String): MusicHubApi = synchronized(apiCache) {
        val key = "auth:$serverUrl"
        apiCache.getOrPut(key) { build(serverUrl, authenticatedClient) }
    }

    fun publicApi(serverUrl: String): MusicHubApi = synchronized(apiCache) {
        val key = "public:$serverUrl"
        apiCache.getOrPut(key) { build(serverUrl, publicClient) }
    }

    private fun build(serverUrl: String, client: OkHttpClient): MusicHubApi {
        val baseUrl = if (serverUrl.endsWith('/')) serverUrl else "$serverUrl/"
        return retrofit2.Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(client)
            .addConverterFactory(KotlinxSerializationConverterFactory(JSON))
            .build()
            .create(MusicHubApi::class.java)
    }

    private companion object {
        val JSON = kotlinx.serialization.json.Json {
            ignoreUnknownKeys = true
            explicitNulls = false
            encodeDefaults = true
            coerceInputValues = true
        }
    }
}

object ApiJson {
    val instance: kotlinx.serialization.json.Json = kotlinx.serialization.json.Json {
        ignoreUnknownKeys = true
        explicitNulls = false
        encodeDefaults = true
        coerceInputValues = true
    }
}
