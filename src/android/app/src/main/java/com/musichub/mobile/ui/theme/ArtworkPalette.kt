package com.musichub.mobile.ui.theme

import androidx.compose.ui.graphics.Color

/**
 * Placeholder artwork gradients.
 *
 * Tracks and albums without an extracted cover still need to look like part of the product, so the
 * card background is derived from the title. Deriving it from the title (instead of a random value)
 * keeps a given track's colour stable across recompositions and across screens.
 *
 * This file deliberately depends only on `androidx.compose.ui.graphics.Color`, which is pure Kotlin,
 * so the derivation can be covered by JVM unit tests.
 */
private val ArtworkGradients: List<Pair<Color, Color>> = listOf(
    Color(0xFF6C5CE7) to Color(0xFF3B2E8F),
    Color(0xFF00CEC9) to Color(0xFF00726F),
    Color(0xFFE17055) to Color(0xFFB33951),
    Color(0xFF00B894) to Color(0xFF00695C),
    Color(0xFFFD79A8) to Color(0xFFB03A6B),
    Color(0xFFFDCB6E) to Color(0xFFB8860B),
)

/** Number of distinct placeholder gradients. Exposed for tests and for accessibility copy. */
val artworkGradientCount: Int get() = ArtworkGradients.size

/**
 * Returns the start/end colours of the placeholder gradient for [seed].
 *
 * The index uses `floorMod` so a negative [String.hashCode] can never index outside the palette.
 */
fun artworkGradientColors(seed: String): Pair<Color, Color> {
    val index = Math.floorMod(seed.hashCode(), ArtworkGradients.size)
    return ArtworkGradients[index]
}

/** First visible character of [seed], used as the placeholder letter. */
fun artworkInitial(seed: String): String {
    val trimmed = seed.trim()
    if (trimmed.isEmpty()) return "\u266A"
    // take(1) would split a surrogate pair; codePoints keeps emoji titles intact.
    val first = trimmed.codePointAt(0)
    return String(Character.toChars(first)).uppercase()
}
