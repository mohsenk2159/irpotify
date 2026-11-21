package com.musichub.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val Purple = Color(0xFF6C5CE7)
private val PurpleLight = Color(0xFFB9AEFF)
private val Teal = Color(0xFF00CEC9)
private val SurfaceDark = Color(0xFF101014)
private val SurfaceDarkElevated = Color(0xFF1A1A22)
private val SurfaceLight = Color(0xFFFDFCFF)

private val DarkColors = darkColorScheme(
    primary = PurpleLight,
    onPrimary = Color(0xFF241A66),
    primaryContainer = Color(0xFF3B2E8F),
    onPrimaryContainer = Color(0xFFE6E1FF),
    secondary = Teal,
    onSecondary = Color(0xFF003735),
    background = SurfaceDark,
    onBackground = Color(0xFFE6E1E9),
    surface = SurfaceDark,
    onSurface = Color(0xFFE6E1E9),
    surfaceVariant = SurfaceDarkElevated,
    onSurfaceVariant = Color(0xFFC9C5D2),
    error = Color(0xFFFFB4AB),
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
