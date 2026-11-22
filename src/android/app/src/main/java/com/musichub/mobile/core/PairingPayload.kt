package com.musichub.mobile.core

import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets

data class PairingPayload(
    val serverUrl: String,
    val code: String,
    val expiresAtEpochSeconds: Long?,
    val lanServerUrl: String? = null,
    val internetServerUrl: String? = null,
    val deviceName: String? = null,
    val userEmail: String? = null,
)

enum class PairingParseFailure {
    INVALID_FORMAT,
    MISSING_FIELDS,
    EXPIRED,
}

sealed interface PairingParseResult {
    data class Success(val payload: PairingPayload) : PairingParseResult
    data class Failure(val reason: PairingParseFailure) : PairingParseResult
}

/**
 * Parses the payload produced by `POST /api/pairing/create`:
 * `musichub://pair?server=<url>&code=<code>&expires=<unix-seconds>`.
 *
 * Implemented with java.net.URI (instead of android.net.Uri) so it can be covered by JVM tests.
 */
object PairingPayloadParser {
    fun parse(raw: String, nowEpochSeconds: Long = System.currentTimeMillis() / 1000): PairingParseResult {
        val trimmed = raw.trim()
        if (trimmed.isEmpty()) return PairingParseResult.Failure(PairingParseFailure.INVALID_FORMAT)

        val uri = try {
            URI(trimmed)
        } catch (_: Exception) {
            return PairingParseResult.Failure(PairingParseFailure.INVALID_FORMAT)
        }

        if (!uri.scheme.equals("musichub", ignoreCase = true) || !uri.host.equals("pair", ignoreCase = true)) {
            return PairingParseResult.Failure(PairingParseFailure.INVALID_FORMAT)
        }

        val parameters = parseQuery(uri.rawQuery)
        val server = parameters["server"]?.trim().orEmpty()
        val lan = parameters["lan"]?.trim()?.takeIf { it.isNotBlank() }
        val code = parameters["code"]?.trim().orEmpty()
        val device = parameters["device"]?.trim()?.takeIf { it.isNotBlank() }
        val email = parameters["email"]?.trim()?.takeIf { it.isNotBlank() }
        if (server.isEmpty() || code.isEmpty()) {
            return PairingParseResult.Failure(PairingParseFailure.MISSING_FIELDS)
        }

        val expires = parameters["expires"]?.toLongOrNull()
        if (expires != null && expires <= nowEpochSeconds) {
            return PairingParseResult.Failure(PairingParseFailure.EXPIRED)
        }

        return PairingParseResult.Success(
            PairingPayload(
                serverUrl = server,
                code = code,
                expiresAtEpochSeconds = expires,
                lanServerUrl = lan,
                internetServerUrl = server.takeIf { it.isNotBlank() },
                deviceName = device,
                userEmail = email,
            ),
        )
    }

    private fun parseQuery(rawQuery: String?): Map<String, String> {
        if (rawQuery.isNullOrBlank()) return emptyMap()
        val result = LinkedHashMap<String, String>()
        for (part in rawQuery.split('&')) {
            if (part.isBlank()) continue
            val separator = part.indexOf('=')
            if (separator <= 0) continue
            val key = decode(part.substring(0, separator))
            val value = decode(part.substring(separator + 1))
            result[key] = value
        }
        return result
    }

    private fun decode(value: String): String =
        try {
            URLDecoder.decode(value, StandardCharsets.UTF_8.name())
        } catch (_: IllegalArgumentException) {
            value
        }
}
