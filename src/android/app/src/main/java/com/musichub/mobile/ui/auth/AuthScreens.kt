package com.musichub.mobile.ui.auth

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.musichub.mobile.R
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.auth.SessionState
import com.musichub.mobile.ui.errorMessage

@Composable
fun AuthFlow(container: AppContainer, signedOut: SessionState.SignedOut) {
    val viewModel: AuthViewModel = viewModel(
        factory = viewModelFactory { initializer { AuthViewModel(container) } },
    )
    val state by viewModel.state.collectAsStateWithLifecycle()
    val settings by container.settingsStore.settings.collectAsStateWithLifecycle()

    LaunchedEffect(signedOut.profile?.id) {
        val profile = signedOut.profile
        viewModel.prefillProfile(
            profileServer = profile?.baseUrl,
            lanServer = profile?.lanBaseUrl,
            internetServer = profile?.internetBaseUrl,
            email = profile?.userEmail,
        )
    }

    if (state.scannerOpen) {
        QrScannerScreen(
            onResult = viewModel::onQrScanned,
            onCancel = viewModel::closeScanner,
        )
        return
    }

    val background = Brush.verticalGradient(
        listOf(
            MaterialTheme.colorScheme.primary.copy(alpha = 0.18f),
            MaterialTheme.colorScheme.background,
        ),
    )

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(background)
            .imePadding(),
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 24.dp, vertical = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Surface(
                shape = CircleShape,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(72.dp),
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(
                        painterResource(R.drawable.ic_music_note),
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimary,
                        modifier = Modifier.size(38.dp),
                    )
                }
            }
            Spacer(Modifier.height(16.dp))
            Text(
                stringResource(R.string.auth_title),
                style = MaterialTheme.typography.headlineMedium,
                fontWeight = FontWeight.SemiBold,
                textAlign = TextAlign.Center,
            )
            Spacer(Modifier.height(6.dp))
            Text(
                stringResource(R.string.auth_subtitle),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )

            Spacer(Modifier.height(24.dp))

            if (signedOut.profile != null) {
                SavedSessionCard(
                    server = signedOut.profile.baseUrl,
                    message = when (signedOut.reason) {
                        com.musichub.mobile.data.auth.SessionEndReason.EXPIRED ->
                            stringResource(R.string.error_unauthorized)

                        com.musichub.mobile.data.auth.SessionEndReason.NETWORK_ERROR ->
                            stringResource(R.string.settings_offline_session)

                        else -> null
                    },
                    busy = state.busy,
                    onRetry = viewModel::retrySavedSession,
                )
                Spacer(Modifier.height(16.dp))
            }

            if (state.mode == AuthMode.PAIRING) {
                PairingForm(
                    state = state,
                    onServer = viewModel::updateServerUrl,
                    onCode = viewModel::updatePairingCode,
                    onDevice = viewModel::updateDeviceName,
                    onSubmit = viewModel::claim,
                    onScan = viewModel::openScanner,
                )
                TextButton(onClick = { viewModel.setMode(AuthMode.LOGIN) }) {
                    Text(stringResource(R.string.action_use_password))
                }
            } else {
                LoginForm(
                    state = state,
                    onLanServer = viewModel::updateLanServerUrl,
                    onInternetServer = viewModel::updateInternetServerUrl,
                    onEmail = viewModel::updateEmail,
                    onPassword = viewModel::updatePassword,
                    onDevice = viewModel::updateDeviceName,
                    onSubmit = viewModel::login,
                    onScan = viewModel::openScanner,
                )
                TextButton(onClick = { viewModel.setMode(AuthMode.PAIRING) }) {
                    Text(stringResource(R.string.action_use_pairing))
                }
            }

            state.error?.let { error ->
                Spacer(Modifier.height(8.dp))
                Card(
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer,
                    ),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text(
                        errorMessage(error),
                        modifier = Modifier.padding(12.dp),
                        color = MaterialTheme.colorScheme.onErrorContainer,
                    )
                }
            }

            Spacer(Modifier.height(16.dp))
            HttpOption(
                allowCleartext = settings.allowCleartext,
                onAllowCleartext = viewModel::setAllowCleartext,
            )

            Spacer(Modifier.height(24.dp))
            Text(
                stringResource(R.string.settings_security_note),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
    }
}

@Composable
private fun SavedSessionCard(
    server: String,
    message: String?,
    busy: Boolean,
    onRetry: () -> Unit,
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(stringResource(R.string.profile_server), style = MaterialTheme.typography.labelMedium)
            Text(server, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium)
            if (message != null) {
                Text(
                    message,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            OutlinedButton(onClick = onRetry, enabled = !busy) {
                Text(stringResource(R.string.action_retry))
            }
        }
    }
}

@Composable
private fun PairingForm(
    state: AuthUiState,
    onServer: (String) -> Unit,
    onCode: (String) -> Unit,
    onDevice: (String) -> Unit,
    onSubmit: () -> Unit,
    onScan: () -> Unit,
) {
    OutlinedButton(
        onClick = onScan,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(stringResource(R.string.action_scan_qr))
    }
    Spacer(Modifier.height(16.dp))
    Text(
        stringResource(R.string.pairing_help),
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    Spacer(Modifier.height(12.dp))
    OutlinedTextField(
        value = state.serverUrl,
        onValueChange = onServer,
        label = { Text(stringResource(R.string.pairing_server_label)) },
        placeholder = { Text(stringResource(R.string.pairing_server_hint)) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next),
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    OutlinedTextField(
        value = state.pairingCode,
        onValueChange = onCode,
        label = { Text(stringResource(R.string.pairing_code_label)) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    OutlinedTextField(
        value = state.deviceName,
        onValueChange = onDevice,
        label = { Text(stringResource(R.string.pairing_device_label)) },
        placeholder = { Text(stringResource(R.string.pairing_device_hint)) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(8.dp))
    Text(
        stringResource(R.string.pairing_manual_hint),
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    Spacer(Modifier.height(16.dp))
    Button(
        onClick = onSubmit,
        enabled = !state.busy,
        modifier = Modifier.fillMaxWidth(),
    ) {
        if (state.busy) {
            CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
        } else {
            Text(stringResource(R.string.action_pair))
        }
    }
}

@Composable
private fun LoginForm(
    state: AuthUiState,
    onLanServer: (String) -> Unit,
    onInternetServer: (String) -> Unit,
    onEmail: (String) -> Unit,
    onPassword: (String) -> Unit,
    onDevice: (String) -> Unit,
    onSubmit: () -> Unit,
    onScan: () -> Unit,
) {
    OutlinedButton(
        onClick = onScan,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(stringResource(R.string.action_scan_qr))
    }
    Spacer(Modifier.height(16.dp))
    OutlinedTextField(
        value = state.lanServerUrl,
        onValueChange = onLanServer,
        label = { Text(stringResource(R.string.login_lan_server)) },
        placeholder = { Text(stringResource(R.string.login_lan_hint)) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next),
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    OutlinedTextField(
        value = state.internetServerUrl,
        onValueChange = onInternetServer,
        label = { Text(stringResource(R.string.login_internet_server)) },
        placeholder = { Text(stringResource(R.string.login_internet_hint)) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next),
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    OutlinedTextField(
        value = state.email,
        onValueChange = onEmail,
        label = { Text(stringResource(R.string.login_email)) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email, imeAction = ImeAction.Next),
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    var passwordVisible by rememberSaveable { mutableStateOf(false) }
    OutlinedTextField(
        value = state.password,
        onValueChange = onPassword,
        label = { Text(stringResource(R.string.login_password)) },
        singleLine = true,
        visualTransformation = if (passwordVisible) VisualTransformation.None else PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
        trailingIcon = {
            IconButton(onClick = { passwordVisible = !passwordVisible }) {
                Icon(
                    painterResource(
                        if (passwordVisible) R.drawable.ic_visibility_off else R.drawable.ic_visibility,
                    ),
                    contentDescription = null,
                )
            }
        },
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    OutlinedTextField(
        value = state.deviceName,
        onValueChange = onDevice,
        label = { Text(stringResource(R.string.pairing_device_label)) },
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
    )
    Spacer(Modifier.height(10.dp))
    if (state.lanServerUrl.isBlank() && state.internetServerUrl.isBlank()) {
        Text(
            stringResource(R.string.login_at_least_one),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
    Spacer(Modifier.height(16.dp))
    Button(
        onClick = onSubmit,
        enabled = !state.busy,
        modifier = Modifier.fillMaxWidth(),
    ) {
        if (state.busy) {
            CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
        } else {
            Text(stringResource(R.string.action_sign_in))
        }
    }
}

@Composable
private fun HttpOption(allowCleartext: Boolean, onAllowCleartext: (Boolean) -> Unit) {
    Card(
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.6f),
        ),
    ) {
        Column(modifier = Modifier.padding(14.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    stringResource(R.string.http_allow_label),
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.weight(1f),
                )
                Switch(checked = allowCleartext, onCheckedChange = onAllowCleartext)
            }
            if (allowCleartext) {
                Spacer(Modifier.height(8.dp))
                Text(
                    stringResource(R.string.http_warning_title),
                    style = MaterialTheme.typography.titleSmall,
                    color = MaterialTheme.colorScheme.error,
                )
                Text(
                    stringResource(R.string.http_warning_body),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}
