package com.musichub.mobile.data.api

import com.musichub.mobile.data.api.models.ApiErrorBody
import retrofit2.HttpException
import java.io.IOException

/**
 * Normalized API failure. statusCode 0 means a transport failure (offline, DNS, timeout).
 * Server-provided messages are safe to display; tokens and request headers are never included.
 */
class ApiCallException(
    val statusCode: Int,
    val serverMessage: String?,
    cause: Throwable? = null,
) : Exception(serverMessage ?: if (statusCode == 0) "Network unavailable" else "HTTP $statusCode", cause) {
    val isOffline: Boolean get() = statusCode == 0
    val isUnauthorized: Boolean get() = statusCode == 401
}

suspend fun <T> runApi(block: suspend () -> T): T {
    try {
        return block()
    } catch (exception: HttpException) {
        val body = try {
            exception.response()?.errorBody()?.string()
        } catch (_: Exception) {
            null
        }
        val message = body?.let {
            runCatching { ApiJson.instance.decodeFromString<ApiErrorBody>(it) }.getOrNull()
        }?.let { it.error ?: it.title ?: it.detail }
        throw ApiCallException(exception.code(), message, exception)
    } catch (exception: IOException) {
        throw ApiCallException(0, null, exception)
    }
}
