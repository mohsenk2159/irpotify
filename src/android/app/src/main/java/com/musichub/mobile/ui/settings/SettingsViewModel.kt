package com.musichub.mobile.ui.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.api.ApiCallException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

enum class HealthState { UNKNOWN, CHECKING, OK, FAILED }

sealed interface ChangePasswordOutcome {
    data object Idle : ChangePasswordOutcome
    data object Busy : ChangePasswordOutcome
    data object Success : ChangePasswordOutcome
    data class Failed(val message: String) : ChangePasswordOutcome
    data class Validation(val message: String) : ChangePasswordOutcome
}

data class SettingsUiState(
    val health: HealthState = HealthState.UNKNOWN,
    val busy: Boolean = false,
    val changePassword: ChangePasswordOutcome = ChangePasswordOutcome.Idle,
)

class SettingsViewModel(private val container: AppContainer) : ViewModel() {
    private val _state = MutableStateFlow(SettingsUiState())
    val state: StateFlow<SettingsUiState> = _state.asStateFlow()

    fun checkHealth() {
        viewModelScope.launch {
            _state.value = _state.value.copy(health = HealthState.CHECKING)
            val allowCleartext = container.settingsStore.settings.value.allowCleartext
            // Probes every saved endpoint (LAN first) and moves the active URL onto the first one
            // that answers, so the health card doubles as the manual failover control.
            val reachable = container.sessionManager.revalidateEndpoint(allowCleartext)
            _state.value = _state.value.copy(
                health = if (reachable != null) HealthState.OK else HealthState.FAILED,
            )
        }
    }

    /**
     * Validates locally first so obvious mistakes never reach the network, then delegates to
     * SessionManager. The server rotates the token family and the app stores the fresh pair.
     */
    fun changePassword(currentPassword: String, newPassword: String, confirmPassword: String) {
        if (_state.value.changePassword is ChangePasswordOutcome.Busy) return

        val problem = when {
            currentPassword.isBlank() -> "Enter your current password."
            newPassword.length < 12 -> "The new password must be at least 12 characters."
            newPassword != confirmPassword -> "The new passwords do not match."
            newPassword == currentPassword -> "Choose a password you have not used before."
            else -> null
        }
        if (problem != null) {
            _fail(problem)
            return
        }

        _state.value = _state.value.copy(changePassword = ChangePasswordOutcome.Busy)
        viewModelScope.launch {
            val deviceName = container.deviceIdentity.displayName
            container.sessionManager.changePassword(currentPassword.trim(), newPassword, deviceName)
                .onSuccess {
                    _state.value = _state.value.copy(changePassword = ChangePasswordOutcome.Success)
                }
                .onFailure { error ->
                    val message = when (error) {
                        is ApiCallException -> error.serverMessage
                            ?: if (error.statusCode == 0) "Cannot reach the server." else "Password change failed."
                        else -> error.message ?: "Password change failed."
                    }
                    _state.value = _state.value.copy(changePassword = ChangePasswordOutcome.Failed(message))
                }
        }
    }

    fun clearChangePasswordState() {
        _state.value = _state.value.copy(changePassword = ChangePasswordOutcome.Idle)
    }

    fun logout(allDevices: Boolean) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true)
            container.playerController.clear()
            container.sessionManager.logout(allDevices)
            _state.value = _state.value.copy(busy = false)
        }
    }

    private fun _fail(message: String) {
        _state.value = _state.value.copy(changePassword = ChangePasswordOutcome.Validation(message))
    }
}
