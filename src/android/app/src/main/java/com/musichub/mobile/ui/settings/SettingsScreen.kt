package com.musichub.mobile.ui.settings

import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.musichub.mobile.BuildConfig
import com.musichub.mobile.R
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.api.models.UserDto
import com.musichub.mobile.data.settings.AppLanguage
import com.musichub.mobile.data.settings.ServerProfile
import com.musichub.mobile.data.settings.ThemeMode
import com.musichub.mobile.ui.LocaleHelper
import kotlinx.coroutines.launch

@Composable
fun SettingsScreen(
    container: AppContainer,
    profile: ServerProfile,
    user: UserDto,
) {
    val viewModel: SettingsViewModel = viewModel(
        factory = viewModelFactory { initializer { SettingsViewModel(container) } },
    )
    val state by viewModel.state.collectAsStateWithLifecycle()
    val settings by container.settingsStore.settings.collectAsStateWithLifecycle()

    var showLogoutDialog by remember { mutableStateOf(false) }
    var showLogoutAllDialog by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { viewModel.checkHealth() }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .imePadding()
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text(stringResource(R.string.settings_account), style = MaterialTheme.typography.titleMedium)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                LabeledValue(stringResource(R.string.profile_email), user.email)
                LabeledValue(stringResource(R.string.profile_roles), user.role)
                LabeledValue(stringResource(R.string.profile_server), profile.baseUrl)
                if (profile.userDisplayName != null) {
                    LabeledValue(
                        stringResource(R.string.pairing_device_label),
                        profile.userDisplayName,
                    )
                }
            }
        }

        Text(stringResource(R.string.settings_servers), style = MaterialTheme.typography.titleMedium)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                LabeledValue(stringResource(R.string.settings_active_server), profile.baseUrl)
                profile.lanBaseUrl?.takeIf { it.isNotBlank() && it != profile.baseUrl }?.let {
                    LabeledValue(stringResource(R.string.login_lan_server), it)
                }
                profile.internetBaseUrl?.takeIf { it.isNotBlank() && it != profile.baseUrl }?.let {
                    LabeledValue(stringResource(R.string.login_internet_server), it)
                }
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text(
                        text = when (state.health) {
                            HealthState.UNKNOWN -> stringResource(R.string.settings_health_checking)
                            HealthState.CHECKING -> stringResource(R.string.settings_health_checking)
                            HealthState.OK -> stringResource(R.string.settings_health_ok)
                            HealthState.FAILED -> stringResource(R.string.settings_health_failed)
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = when (state.health) {
                            HealthState.OK -> MaterialTheme.colorScheme.primary
                            HealthState.FAILED -> MaterialTheme.colorScheme.error
                            else -> MaterialTheme.colorScheme.onSurfaceVariant
                        },
                    )
                    if (state.health == HealthState.CHECKING) {
                        CircularProgressIndicator(modifier = Modifier.size(14.dp), strokeWidth = 2.dp)
                    }
                }
                OutlinedButton(onClick = viewModel::checkHealth, enabled = state.health != HealthState.CHECKING) {
                    Text(stringResource(R.string.action_refresh))
                }
            }
        }

        ChangePasswordCard(
            state = state.changePassword,
            onSave = viewModel::changePassword,
            onDismissMessage = viewModel::clearChangePasswordState,
        )

        Text(stringResource(R.string.settings_playback), style = MaterialTheme.typography.titleMedium)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                ToggleRow(
                    label = stringResource(R.string.settings_allow_http),
                    checked = settings.allowCleartext,
                    onCheckedChange = { enabled ->
                        container.scope.launch {
                            container.settingsStore.setAllowCleartext(enabled)
                        }
                    },
                )
                HorizontalDivider()
                ToggleRow(
                    label = stringResource(R.string.settings_download_wifi),
                    checked = settings.downloadWifiOnly,
                    onCheckedChange = { enabled ->
                        container.scope.launch {
                            container.settingsStore.setDownloadWifiOnly(enabled)
                        }
                    },
                )
            }
        }

        Text(stringResource(R.string.settings_language), style = MaterialTheme.typography.titleMedium)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(14.dp)) {
                LanguageRow(settings.language, container)
            }
        }

        Text(stringResource(R.string.settings_theme), style = MaterialTheme.typography.titleMedium)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(14.dp)) {
                ThemeRow(settings.themeMode, container)
            }
        }

        Text(stringResource(R.string.settings_about), style = MaterialTheme.typography.titleMedium)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    stringResource(R.string.settings_version, BuildConfig.VERSION_NAME),
                    style = MaterialTheme.typography.bodySmall,
                )
                Text(
                    stringResource(R.string.settings_security_note),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

        Spacer(Modifier.height(4.dp))
        Button(
            onClick = { showLogoutDialog = true },
            enabled = !state.busy,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(stringResource(R.string.action_logout))
        }
        OutlinedButton(
            onClick = { showLogoutAllDialog = true },
            enabled = !state.busy,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(stringResource(R.string.action_logout_all))
        }
        Spacer(Modifier.height(24.dp))
        // The screen scrolls inside a Scaffold that is edge-to-edge, so the last controls need their
        // own gap. Scaffold already supplies the bottom bar height; this keeps the final button off
        // the very edge of the scroll viewport on short screens.
        Spacer(Modifier.height(12.dp))
    }

    if (showLogoutDialog) {
        AlertDialog(
            onDismissRequest = { showLogoutDialog = false },
            title = { Text(stringResource(R.string.action_logout)) },
            text = { Text(stringResource(R.string.settings_logout_body)) },
            confirmButton = {
                TextButton(onClick = {
                    showLogoutDialog = false
                    viewModel.logout(allDevices = false)
                }) { Text(stringResource(R.string.action_logout)) }
            },
            dismissButton = {
                TextButton(onClick = { showLogoutDialog = false }) {
                    Text(stringResource(R.string.action_cancel))
                }
            },
        )
    }

    if (showLogoutAllDialog) {
        AlertDialog(
            onDismissRequest = { showLogoutAllDialog = false },
            title = { Text(stringResource(R.string.action_logout_all)) },
            text = { Text(stringResource(R.string.settings_logout_all_body)) },
            confirmButton = {
                TextButton(onClick = {
                    showLogoutAllDialog = false
                    viewModel.logout(allDevices = true)
                }) { Text(stringResource(R.string.action_logout_all)) }
            },
            dismissButton = {
                TextButton(onClick = { showLogoutAllDialog = false }) {
                    Text(stringResource(R.string.action_cancel))
                }
            },
        )
    }
}

@Composable
private fun ChangePasswordCard(
    state: ChangePasswordOutcome,
    onSave: (String, String, String) -> Unit,
    onDismissMessage: () -> Unit,
) {
    var current by rememberSaveable { mutableStateOf("") }
    var next by rememberSaveable { mutableStateOf("") }
    var confirm by rememberSaveable { mutableStateOf("") }

    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
    ) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(stringResource(R.string.settings_change_password), style = MaterialTheme.typography.titleSmall)

            OutlinedTextField(
                value = current,
                onValueChange = {
                    current = it
                    onDismissMessage()
                },
                label = { Text(stringResource(R.string.settings_current_password)) },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Password,
                    imeAction = ImeAction.Next,
                ),
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = next,
                onValueChange = {
                    next = it
                    onDismissMessage()
                },
                label = { Text(stringResource(R.string.settings_new_password)) },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Password,
                    imeAction = ImeAction.Next,
                ),
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = confirm,
                onValueChange = {
                    confirm = it
                    onDismissMessage()
                },
                label = { Text(stringResource(R.string.settings_confirm_password)) },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Password,
                    imeAction = ImeAction.Done,
                ),
                modifier = Modifier.fillMaxWidth(),
            )

            when (state) {
                is ChangePasswordOutcome.Validation -> Text(
                    state.message,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodySmall,
                )

                is ChangePasswordOutcome.Failed -> Text(
                    state.message,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodySmall,
                )

                ChangePasswordOutcome.Success -> Text(
                    stringResource(R.string.settings_password_changed),
                    color = MaterialTheme.colorScheme.primary,
                    style = MaterialTheme.typography.bodySmall,
                )

                else -> Unit
            }

            Button(
                onClick = {
                    onSave(current, next, confirm)
                    if (state !is ChangePasswordOutcome.Validation) {
                        current = ""
                        next = ""
                        confirm = ""
                    }
                },
                enabled = state !is ChangePasswordOutcome.Busy,
                modifier = Modifier.fillMaxWidth(),
            ) {
                if (state is ChangePasswordOutcome.Busy) {
                    CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
                } else {
                    Text(stringResource(R.string.settings_change_password))
                }
            }
        }
    }
}

@Composable
private fun LabeledValue(label: String, value: String) {
    Column {
        Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(label, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}

@Composable
private fun LanguageRow(current: AppLanguage, container: AppContainer) {
    val context = LocalContext.current
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        AppLanguage.entries.forEach { entry ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    text = when (entry) {
                        AppLanguage.SYSTEM -> stringResource(R.string.settings_language_system)
                        AppLanguage.ENGLISH -> stringResource(R.string.settings_language_english)
                        AppLanguage.PERSIAN -> stringResource(R.string.settings_language_persian)
                    },
                    style = MaterialTheme.typography.bodyMedium,
                )
                androidx.compose.material3.RadioButton(
                    selected = current == entry,
                    onClick = {
                        container.scope.launch {
                            container.settingsStore.setLanguage(entry)
                            LocaleHelper.setLanguage(container.context, entry.tag)
                            // The locale is applied in attachBaseContext, so the Activity has to be
                            // recreated before Compose resolves string resources again. Without this
                            // the choice was stored but the UI stayed in the previous language.
                            withContext(Dispatchers.Main) {
                                // LocalContext in a Compose hierarchy is the Activity context, so
                                // recreation re-runs attachBaseContext with the new locale.
                                (context as? android.app.Activity)?.recreate()
                            }
                        }
                    },
                )
            }
        }
    }
}

@Composable
private fun ThemeRow(current: ThemeMode, container: AppContainer) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        ThemeMode.entries.forEach { entry ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    text = when (entry) {
                        ThemeMode.SYSTEM -> stringResource(R.string.settings_theme_system)
                        ThemeMode.LIGHT -> stringResource(R.string.settings_theme_light)
                        ThemeMode.DARK -> stringResource(R.string.settings_theme_dark)
                    },
                    style = MaterialTheme.typography.bodyMedium,
                )
                androidx.compose.material3.RadioButton(
                    selected = current == entry,
                    onClick = {
                        container.scope.launch { container.settingsStore.setTheme(entry) }
                    },
                )
            }
        }
    }
}
