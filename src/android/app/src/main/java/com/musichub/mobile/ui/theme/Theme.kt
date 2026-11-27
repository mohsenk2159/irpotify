package com.musichub.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val SpotifyGreen = Color(0xFF1DB954)
private val SpotifyGreenLight = Color(0xFF1ED760)
private val SpotifyDark = Color(0xFF121212)
private val SpotifySurface = Color(0xFF181818)
private val SpotifySurfaceElevated = Color(0xFF242424)
private val SpotifyTextWhite = Color(0xFFFFFFFF)
private val SpotifyTextMuted = Color(0xFFB3B3B3)
private val SurfaceLight = Color(0xFFFDFCFF)

private val DarkColors = darkColorScheme(
    primary = SpotifyGreen,
    onPrimary = Color.Black,
    primaryContainer = Color(0xFF169C46),
    onPrimaryContainer = Color.White,
    secondary = SpotifyGreenLight,
    onSecondary = Color.Black,
    background = SpotifyDark,
    onBackground = SpotifyTextWhite,
    surface = SpotifySurface,
    onSurface = SpotifyTextWhite,
    surfaceVariant = SpotifySurfaceElevated,
    onSurfaceVariant = SpotifyTextMuted,
    error = Color(0xFFE91429),
)

private val LightColors = lightColorScheme(
    primary = Color(0xFF5A4BC4),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFE5DEFF),
    onPrimaryContainer = Color(0xFF1A0F5C),
    secondary = Color(0xFF007D79),
    onSecondary = Color.White,
    background = SurfaceLight,
    onBackground = Color(0xFF1B1B21),
    surface = SurfaceLight,
    onSurface = Color(0xFF1B1B21),
    surfaceVariant = Color(0xFFEAE7F0),
    onSurfaceVariant = Color(0xFF48454F),
)

@Composable
fun MusicHubTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = androidx.compose.material3.Typography(),
        content = content,
    )
}
