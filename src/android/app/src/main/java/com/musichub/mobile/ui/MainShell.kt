package com.musichub.mobile.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarDefaults
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.api.models.UserDto
import com.musichub.mobile.data.auth.SessionState
import com.musichub.mobile.data.settings.ServerProfile
import com.musichub.mobile.ui.auth.AuthFlow
import com.musichub.mobile.ui.home.HomeScreen
import com.musichub.mobile.ui.library.LibraryScreen
import com.musichub.mobile.ui.player.NowPlayingScreen
import com.musichub.mobile.ui.search.SearchScreen
import com.musichub.mobile.ui.settings.SettingsScreen
import androidx.compose.ui.res.stringResource
import com.musichub.mobile.R

internal const val ROUTE_HOME = "home"
internal const val ROUTE_SEARCH = "search"
internal const val ROUTE_LIBRARY = "library"
internal const val ROUTE_NOW_PLAYING = "now-playing"
internal const val ROUTE_SETTINGS = "settings"

@Composable
fun MusicHubRoot(appContainer: AppContainer = LocalAppContainer.current) {
    val sessionState by appContainer.sessionManager.state.collectAsStateWithLifecycle()
    when (val state = sessionState) {
        SessionState.Loading -> {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        }

        is SessionState.SignedOut -> AuthFlow(container = appContainer, signedOut = state)

        is SessionState.SignedIn -> MainShell(
            container = appContainer,
            profile = state.profile,
            user = state.user,
        )
    }
}

@Composable
fun MainShell(
    container: AppContainer,
    profile: ServerProfile,
    user: UserDto,
) {
    var route by rememberSaveable { mutableStateOf(ROUTE_HOME) }
    val navigate: (String) -> Unit = { target -> route = target }

    // The activity calls enableEdgeToEdge(), so the window extends behind the status and system
    // navigation bars. Insets are declared explicitly here instead of relying on Scaffold defaults
    // so the two consumers cannot double-count them:
    //   * the NavigationBar owns the bottom system-bar inset and grows by it, and
    //   * Scaffold hands the content the resulting bar height, which the content Box consumes.
    Scaffold(
        contentWindowInsets = WindowInsets.safeDrawing.only(
            WindowInsetsSides.Horizontal + WindowInsetsSides.Top,
        ),
        bottomBar = {
            NavigationBar(windowInsets = NavigationBarDefaults.windowInsets) {
                NavigationBarItem(
                    selected = route == ROUTE_HOME,
                    onClick = { navigate(ROUTE_HOME) },
                    icon = { Icon(Icons.Filled.Home, contentDescription = stringResource(R.string.nav_home)) },
                    label = { Text(stringResource(R.string.nav_home)) },
                )
                NavigationBarItem(
                    selected = route == ROUTE_SEARCH,
                    onClick = { navigate(ROUTE_SEARCH) },
                    icon = { Icon(Icons.Filled.Search, contentDescription = stringResource(R.string.nav_search)) },
                    label = { Text(stringResource(R.string.nav_search)) },
                )
                NavigationBarItem(
                    selected = route == ROUTE_LIBRARY,
                    onClick = { navigate(ROUTE_LIBRARY) },
                    icon = { Icon(Icons.AutoMirrored.Filled.List, contentDescription = stringResource(R.string.nav_library)) },
                    label = { Text(stringResource(R.string.nav_library)) },
                )
                NavigationBarItem(
                    selected = route == ROUTE_NOW_PLAYING,
                    onClick = { navigate(ROUTE_NOW_PLAYING) },
                    icon = { Icon(Icons.Filled.PlayArrow, contentDescription = stringResource(R.string.nav_now)) },
                    label = { Text(stringResource(R.string.nav_now)) },
                )
                NavigationBarItem(
                    selected = route == ROUTE_SETTINGS,
                    onClick = { navigate(ROUTE_SETTINGS) },
                    icon = { Icon(Icons.Filled.Settings, contentDescription = stringResource(R.string.nav_settings)) },
                    label = { Text(stringResource(R.string.nav_settings)) },
                )
            }
        },
    ) { padding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                // Without this every screen that calls navigationBarsPadding()/systemBarsPadding()
                // would pad a second time on top of the padding already applied above.
                .consumeWindowInsets(padding),
        ) {
            when (route) {
                ROUTE_HOME -> HomeScreen(
                    container = container,
                    onOpenAlbum = { navigate(ROUTE_LIBRARY) },
                    onOpenArtist = { navigate(ROUTE_LIBRARY) },
                    onPlayTracks = { navigate(ROUTE_NOW_PLAYING) },
                )

                ROUTE_SEARCH -> SearchScreen(
                    container = container,
                    onPlayTracks = { navigate(ROUTE_NOW_PLAYING) },
                )

                ROUTE_LIBRARY -> LibraryScreen(
                    container = container,
                    onPlayTracks = { navigate(ROUTE_NOW_PLAYING) },
                )

                ROUTE_NOW_PLAYING -> NowPlayingScreen(container = container)

                ROUTE_SETTINGS -> SettingsScreen(
                    container = container,
                    profile = profile,
                    user = user,
                )

                else -> HomeScreen(
                    container = container,
                    onOpenAlbum = { navigate(ROUTE_LIBRARY) },
                    onOpenArtist = { navigate(ROUTE_LIBRARY) },
                    onPlayTracks = { navigate(ROUTE_NOW_PLAYING) },
                )
            }
        }
    }
}
