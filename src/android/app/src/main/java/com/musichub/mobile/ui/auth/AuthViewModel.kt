package com.musichub.mobile.ui.auth

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.core.LocalNetworkPolicy
import com.musichub.mobile.core.PairingParseFailure
import com.musichub.mobile.core.PairingParseResult
import com.musichub.mobile.core.PairingPayloadParser
import com.musichub.mobile.core.UrlValidationResult
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.api.ApiCallException
import com.musichub.mobile.data.auth.SessionState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

enum class AuthMode { PAIRING, LOGIN }

class QrPairingException(val reason: PairingParseFailure) : Exception(reason.name)

data class AuthUiState(
    // Credential sign-in is the default: it is the only flow that works without an admin
    // creating a pairing code first, and it is the documented first-run path.
    val mode: AuthMode = AuthMode.LOGIN,
    val serverUrl: String = "",
    val pairingCode: String = "",
    val lanServerUrl: String = "",
    val internetServerUrl: String = "",
    val email: String = "",
    val password: String = "",
    val deviceName: String = "",
    val busy: Boolean = false,
    val error: Throwable? = null,
    val scannerOpen: Boolean = false,
)

class AuthViewModel(private val container: AppContainer) : ViewModel() {
    private val _state = MutableStateFlow(AuthUiState(deviceName = container.deviceIdentity.displayName))
    val state: StateFlow<AuthUiState> = _state.asStateFlow()

    fun prefillFrom(profileServer: String?, email: String?) {
        val server = profileServer.orEmpty()
        _state.value = _state.value.copy(
            serverUrl = _state.value.serverUrl.ifBlank { server },
            lanServerUrl = _state.value.lanServerUrl.ifBlank { server },
            internetServerUrl = _state.value.internetServerUrl.ifBlank { server },
            email = _state.value.email.ifBlank { email.orEmpty() },
        )
    }

    /**
     * Restores the addresses the user saved previously, keeping LAN and internet distinct instead
     * of duplicating one URL into both fields. Falls back to [profileServer] when a saved profile
     * predates the dual-address fields.
     */
    fun prefillProfile(
        profileServer: String?,
        lanServer: String?,
        internetServer: String?,
        email: String?,
    ) {
        val fallback = profileServer.orEmpty()
        _state.value = _state.value.copy(
            serverUrl = _state.value.serverUrl.ifBlank { fallback },
            lanServerUrl = _state.value.lanServerUrl.ifBlank { lanServer.orEmpty() },
            internetServerUrl = _state.value.internetServerUrl.ifBlank { internetServer.orEmpty() },
            email = _state.value.email.ifBlank { email.orEmpty() },
        )
    }

    fun setMode(mode: AuthMode) {
        _state.value = _state.value.copy(mode = mode, error = null)
    }

    fun updateServerUrl(value: String) {
        _state.value = _state.value.copy(serverUrl = value, error = null)
    }

    fun updatePairingCode(value: String) {
        _state.value = _state.value.copy(pairingCode = value, error = null)
    }

    fun updateLanServerUrl(value: String) {
        _state.value = _state.value.copy(lanServerUrl = value, error = null)
    }

    fun updateInternetServerUrl(value: String) {
        _state.value = _state.value.copy(internetServerUrl = value, error = null)
    }

    fun updateEmail(value: String) {
        _state.value = _state.value.copy(email = value, error = null)
    }

    fun updatePassword(value: String) {
        _state.value = _state.value.copy(password = value, error = null)
    }

    fun updateDeviceName(value: String) {
        _state.value = _state.value.copy(deviceName = value)
    }

    fun openScanner() {
        _state.value = _state.value.copy(scannerOpen = true, error = null)
    }

    fun closeScanner() {
        _state.value = _state.value.copy(scannerOpen = false)
    }

    fun onQrScanned(raw: String) {
        when (val result = PairingPayloadParser.parse(raw)) {
            is PairingParseResult.Success -> {
                _state.value = _state.value.copy(
                    serverUrl = result.payload.serverUrl,
                    lanServerUrl = result.payload.lanServerUrl ?: _state.value.lanServerUrl,
                    internetServerUrl = result.payload.internetServerUrl ?: _state.value.internetServerUrl,
                    pairingCode = result.payload.code,
                    deviceName = result.payload.deviceName ?: _state.value.deviceName,
                    email = result.payload.userEmail ?: _state.value.email,
                    scannerOpen = false,
                    error = null,
                )
                claim()
            }

            is PairingParseResult.Failure -> {
                _state.value = _state.value.copy(
                    scannerOpen = false,
                    error = QrPairingException(result.reason),
                )
            }
        }
    }

    fun claim() {
        val current = _state.value
        if (current.busy) return
        if (current.serverUrl.isBlank() || current.pairingCode.isBlank()) {
            _state.value = current.copy(error = ServerUrlExceptionForInput())
            return
        }
        _state.value = current.copy(busy = true, error = null)
        viewModelScope.launch {
            val result = container.sessionManager.claimPairing(
                rawServerUrl = current.serverUrl,
                allowCleartext = container.settingsStore.settings.value.allowCleartext,
                code = current.pairingCode,
                deviceName = current.deviceName.ifBlank { container.deviceIdentity.displayName },
                lanBaseUrl = current.lanServerUrl.takeIf { it.isNotBlank() },
                internetBaseUrl = current.internetServerUrl.takeIf { it.isNotBlank() },
            )
            _state.value = _state.value.copy(
                busy = false,
                error = result.exceptionOrNull(),
            )
        }
    }

    fun login() {
        val current = _state.value
        if (current.busy) return
        if (current.email.isBlank() || current.password.isBlank()) {
            _state.value = current.copy(error = ServerUrlExceptionForInput())
            return
        }
        val allowCleartext = container.settingsStore.settings.value.allowCleartext
        val candidates = mutableListOf<String>()
        current.lanServerUrl.trim().takeIf { it.isNotBlank() }?.let { candidates.add(it) }
        current.internetServerUrl.trim().takeIf { it.isNotBlank() }?.let { candidates.add(it) }
        if (candidates.isEmpty()) {
            _state.value = current.copy(error = ServerUrlExceptionForInput())
            return
        }
        _state.value = current.copy(busy = true, error = null)
        viewModelScope.launch {
            // Validate each filled address, then probe GET /api/healthz. The first healthy address
            // wins; candidates are ordered LAN-first so a healthy LAN server is preferred over an
            // internet server when both are filled.
            val validCandidates = candidates.mapNotNull { raw ->
                val validation = LocalNetworkPolicy.validateBaseUrl(raw, allowCleartext)
                when (validation) {
                    is UrlValidationResult.Invalid -> null
                    is UrlValidationResult.Valid -> validation.baseUrl
                }
            }
            if (validCandidates.isEmpty()) {
                _state.value = _state.value.copy(busy = false, error = ApiCallException(0, "Enter a valid server address."))
                return@launch
            }
            val healthy = mutableListOf<String>()
            for (base in validCandidates) {
                if (container.sessionManager.probeServer(base, allowCleartext)) healthy.add(base)
            }
            if (healthy.isEmpty()) {
                _state.value = _state.value.copy(
                    busy = false,
                    error = ApiCallException(0, "Could not reach the MusicHub server. Check the address and try again."),
                )
                return@launch
            }
            val chosen = healthy.first()
            val result = container.sessionManager.login(
                rawServerUrl = chosen,
                allowCleartext = allowCleartext,
                email = current.email,
                password = current.password,
                deviceName = current.deviceName.ifBlank { container.deviceIdentity.displayName },
                lanBaseUrl = current.lanServerUrl,
                internetBaseUrl = current.internetServerUrl,
            )
            _state.value = _state.value.copy(
                busy = false,
                error = result.exceptionOrNull(),
            )
        }
    }

    fun setAllowCleartext(enabled: Boolean) {
        viewModelScope.launch { container.settingsStore.setAllowCleartext(enabled) }
    }

    fun retrySavedSession() {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true, error = null)
            container.sessionManager.retrySavedSession()
            _state.value = _state.value.copy(busy = false)
        }
    }

    fun currentSignedOut(): SessionState.SignedOut? =
        container.sessionManager.state.value as? SessionState.SignedOut

    fun clearError() {
        _state.value = _state.value.copy(error = null)
    }
}

/** Marker for empty required fields; mapped to a generic validation message in the UI. */
class ServerUrlExceptionForInput : Exception("missing fields")
