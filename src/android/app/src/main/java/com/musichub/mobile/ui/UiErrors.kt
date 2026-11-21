package com.musichub.mobile.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.res.stringResource
import com.musichub.mobile.R
import com.musichub.mobile.core.UrlValidationFailure
import com.musichub.mobile.data.api.ApiCallException
import com.musichub.mobile.data.auth.ServerUrlException
import com.musichub.mobile.core.PairingParseFailure
import com.musichub.mobile.ui.auth.QrPairingException
import com.musichub.mobile.ui.auth.ServerUrlExceptionForInput

val LocalAppContainer = staticCompositionLocalOf<com.musichub.mobile.data.AppContainer> {
    error("AppContainer is not available")
}

@Composable
fun errorMessage(error: Throwable?): String {
    if (error == null) return stringResource(R.string.error_unknown)
    return when (error) {
        is ServerUrlExceptionForInput -> stringResource(R.string.error_invalid_url)
        is QrPairingException -> when (error.reason) {
            PairingParseFailure.EXPIRED -> stringResource(R.string.qr_expired)
            else -> stringResource(R.string.qr_invalid)
        }

        is ServerUrlException -> when (error.reason) {
            UrlValidationFailure.EMPTY, UrlValidationFailure.INVALID_URL -> stringResource(R.string.error_invalid_url)
            UrlValidationFailure.UNSUPPORTED_SCHEME -> stringResource(R.string.error_invalid_url)
            UrlValidationFailure.CLEARTEXT_DISABLED -> stringResource(R.string.error_cleartext_disabled)
            UrlValidationFailure.CLEARTEXT_NOT_LOCAL -> stringResource(R.string.error_cleartext_not_local)
        }

        is ApiCallException -> when (error.statusCode) {
            0 -> stringResource(R.string.error_network)
            401 -> stringResource(R.string.error_unauthorized)
            403 -> stringResource(R.string.error_forbidden)
            404 -> stringResource(R.string.error_not_found)
            423 -> stringResource(R.string.error_locked)
            429 -> stringResource(R.string.error_rate_limited)
            in 500..599 -> stringResource(R.string.error_server)
            else -> error.serverMessage ?: stringResource(R.string.error_unknown)
        }

        else -> error.message ?: stringResource(R.string.error_unknown)
    }
}
