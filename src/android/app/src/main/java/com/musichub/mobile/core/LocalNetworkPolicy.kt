package com.musichub.mobile.core

import java.net.URI

enum class UrlValidationFailure {
    EMPTY,
    INVALID_URL,
    UNSUPPORTED_SCHEME,
    CLEARTEXT_DISABLED,
    CLEARTEXT_NOT_LOCAL,
}

sealed interface UrlValidationResult {
    data class Valid(val baseUrl: String, val cleartext: Boolean) : UrlValidationResult
    data class Invalid(val reason: UrlValidationFailure) : UrlValidationResult
}

/**
 * Normalizes and validates server URLs.
 *
 * TLS is never weakened: HTTPS URLs keep the platform trust store and certificate verification.
 * Cleartext HTTP is accepted only when the user explicitly enabled the local-network setting and
 * the host is a loopback/private-network address.
 */
object LocalNetworkPolicy {
    fun validateBaseUrl(raw: String, allowCleartext: Boolean): UrlValidationResult {
        var value = raw.trim()
        if (value.isEmpty()) return UrlValidationResult.Invalid(UrlValidationFailure.EMPTY)

        if (!value.contains("://")) {
            value = "https://$value"
        }

        val uri = try {
            URI(value)
        } catch (_: Exception) {
            return UrlValidationResult.Invalid(UrlValidationFailure.INVALID_URL)
        }

        val scheme = uri.scheme?.lowercase()
        if (scheme != "http" && scheme != "https") {
            return UrlValidationResult.Invalid(UrlValidationFailure.UNSUPPORTED_SCHEME)
        }

        val host = uri.host?.trim().orEmpty()
        if (host.isEmpty() || uri.userInfo != null || uri.fragment != null || uri.query != null) {
            return UrlValidationResult.Invalid(UrlValidationFailure.INVALID_URL)
        }

        val isCleartext = scheme == "http"
        if (isCleartext && !allowCleartext) {
            return UrlValidationResult.Invalid(UrlValidationFailure.CLEARTEXT_DISABLED)
        }

        val path = uri.rawPath.orEmpty().trimEnd('/')
        val port = if (uri.port == -1) "" else ":${uri.port}"
        val normalized = buildString {
            append(scheme)
            append("://")
            append(host.lowercase())
            append(port)
            append(path)
        }
        return UrlValidationResult.Valid(normalized, isCleartext)
    }

    /**
     * True for loopback and RFC1918/link-local addresses, mDNS names, and common LAN suffixes.
     * Public DNS names are deliberately rejected for cleartext HTTP.
     */
    fun isLocalOrPrivateHost(host: String): Boolean {
        val normalized = host.lowercase().removeSurrounding("[", "]")
        if (normalized == "localhost" || normalized == "::1" || normalized == "127.0.0.1") return true
        if (normalized.endsWith(".local") || normalized.endsWith(".lan") || normalized.endsWith(".home.arpa")) return true

        val octets = normalized.split('.')
        if (octets.size == 4 && octets.all { it.isNotEmpty() && it.all(Char::isDigit) && it.toIntOrNull() in 0..255 }) {
            val first = octets[0].toInt()
            val second = octets[1].toInt()
            return when {
                first == 10 -> true
                first == 127 -> true
                first == 192 && second == 168 -> true
                first == 172 && second in 16..31 -> true
                first == 169 && second == 254 -> true
                else -> false
            }
        }
        return false
    }

    fun apiBaseUrl(serverUrl: String): String =
        if (serverUrl.endsWith('/')) serverUrl else "$serverUrl/"
}
