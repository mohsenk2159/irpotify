package com.musichub.mobile.data.api

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.RequestBody.Companion.toRequestBody
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/**
 * On-device regression tests for B-032.
 *
 * <para>
 * `AuthAuthenticator` used to bail out for every path starting `/api/auth/`, so an expired
 * 15-minute access token could never be refreshed on `me`, `sessions`, `logout`,
 * `change-password` or `logout-all`: those calls failed and the app never recovered. The fix
 * narrowed the exclusion to `login` and `refresh` only.
 * </para>
 *
 * <para>
 * These tests drive a real [OkHttpClient] against a real [MockWebServer], so the OkHttp
 * interceptor/authenticator contract is exercised rather than stubbed. That matters because the
 * earlier bug was invisible to JVM-only reasoning about the same class.
 * </para>
 */
class AuthAuthenticatorInstrumentedTest {

    private lateinit var server: MockWebServer
    private lateinit var client: OkHttpClient
    private lateinit var tokenHolder: TokenHolder
    private lateinit var refreshDelegate: RefreshDelegate

    private val refreshCalls = AtomicInteger(0)

    @Before
    fun setUp() {
        server = MockWebServer()
        server.start()

        tokenHolder = TokenHolder()
        refreshDelegate = RefreshDelegate()

        client = OkHttpClient.Builder()
            .connectTimeout(5, TimeUnit.SECONDS)
            .readTimeout(5, TimeUnit.SECONDS)
            .authenticator(AuthAuthenticator(tokenHolder, refreshDelegate))
            .build()

        // Stands in for SessionManager's single-flight refresher: hand out a fresh token once.
        refreshDelegate.handler = { failedToken ->
            refreshCalls.incrementAndGet()
            if (failedToken == null) {
                false
            } else {
                tokenHolder.accessToken = "fresh-access-token"
                true
            }
        }

        tokenHolder.accessToken = "expired-access-token"
    }

    @After
    fun tearDown() {
        server.shutdown()
    }

    private fun url(path: String) = server.url(path)

    /** Encodes an authenticated request the way [AuthInterceptor] would have. */
    private fun authenticatedRequest(path: String) = Request.Builder()
        .url(url(path))
        .header("Authorization", "Bearer expired-access-token")
        .build()

    private fun enqueueUnauthorizedThenOk() {
        server.enqueue(MockResponse().setResponseCode(401).setBody("""{"error":"expired"}"""))
        server.enqueue(MockResponse().setResponseCode(200).setBody("""{"ok":true}"""))
    }

    @Test
    fun authenticatedAuthRoutes_areRefreshedAndRetried() {
        // Every authenticated /api/auth/* route that B-032 broke.
        val routes = listOf(
            "/api/auth/me",
            "/api/auth/sessions",
            "/api/auth/change-password",
            "/api/auth/logout",
            "/api/auth/logout-all",
        )

        routes.forEach { route ->
            enqueueUnauthorizedThenOk()
            refreshCalls.set(0)

            client.newCall(authenticatedRequest(route)).execute().use { response ->
                assertEquals("$route should be retried and succeed", 200, response.code)
            }

            assertEquals("$route should have refreshed exactly once", 1, refreshCalls.get())

            // The retry must carry the NEW token, not the expired one.
            server.takeRequest(2, TimeUnit.SECONDS) // first, 401
            val retried = server.takeRequest(2, TimeUnit.SECONDS)
            assertEquals("Bearer fresh-access-token", retried?.getHeader("Authorization"))
        }
    }

    @Test
    fun login_doesNotRefresh() {
        // Login has no token yet; retrying it through the refresher would recurse or loop.
        server.enqueue(MockResponse().setResponseCode(401).setBody("""{"error":"bad credentials"}"""))

        client.newCall(
            Request.Builder().url(url("/api/auth/login")).post("{}".toRequestBody()).build()
        ).execute().use { response ->
            assertEquals(401, response.code)
        }

        assertEquals("login must never trigger a refresh", 0, refreshCalls.get())
        assertEquals("no retry should be attempted", 1, server.requestCount)
    }

    @Test
    fun refresh_doesNotRecurse() {
        // A 401 from the refresh endpoint itself must not start another refresh.
        server.enqueue(MockResponse().setResponseCode(401).setBody("""{"error":"invalid refresh token"}"""))

        client.newCall(
            Request.Builder().url(url("/api/auth/refresh")).post("{}".toRequestBody()).build()
        ).execute().use { response ->
            assertEquals(401, response.code)
        }

        assertEquals("refresh must never trigger a refresh", 0, refreshCalls.get())
        assertEquals("no retry should be attempted", 1, server.requestCount)
    }

    @Test
    fun secondUnauthorized_isNotRetriedForever() {
        // If the refreshed token is also rejected, OkHttp's priorResponse guard stops the loop.
        server.enqueue(MockResponse().setResponseCode(401).setBody("""{"error":"expired"}"""))
        server.enqueue(MockResponse().setResponseCode(401).setBody("""{"error":"still expired"}"""))

        client.newCall(authenticatedRequest("/api/auth/me")).execute().use { response ->
            assertEquals(401, response.code)
        }

        assertEquals("only one refresh attempt", 1, refreshCalls.get())
        assertEquals("exactly one retry", 2, server.requestCount)
    }

    @Test
    fun successfulRequest_neverRefreshes() {
        server.enqueue(MockResponse().setResponseCode(200).setBody("""{"ok":true}"""))

        client.newCall(authenticatedRequest("/api/auth/me")).execute().use { response ->
            assertEquals(200, response.code)
        }

        assertEquals(0, refreshCalls.get())
        assertTrue(server.requestCount == 1)
    }

    @Test
    fun requestWithoutToken_isNotRefreshed() {
        // Nothing to refresh: the refresher is told the failed token was null and declines.
        server.enqueue(MockResponse().setResponseCode(401).setBody("""{"error":"unauthorized"}"""))

        client.newCall(Request.Builder().url(url("/api/auth/me")).build()).execute().use { response ->
            assertEquals(401, response.code)
        }

        assertEquals("no token means nothing to refresh", 0, server.requestCount - 1)
        assertEquals(1, server.requestCount)
    }
}
