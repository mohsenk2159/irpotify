package com.musichub.mobile.ui.theme

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The placeholder gradient has to be stable (otherwise a card changes colour on every
 * recomposition) and always in range (a negative String.hashCode must not crash the palette).
 */
class ArtworkPaletteTest {

    @Test
    fun `same seed always yields the same gradient`() {
        val first = artworkGradientColors("Akhareh Ghesseh")
        val second = artworkGradientColors("Akhareh Ghesseh")
        assertEquals(first, second)
    }

    @Test
    fun `different seeds can yield different gradients`() {
        val seeds = listOf("a", "b", "c", "d", "e", "f", "g", "h")
        val distinct = seeds.map { artworkGradientColors(it) }.distinct()
        assertTrue("expected more than one gradient across $seeds, got $distinct", distinct.size > 1)
    }

    @Test
    fun `start and end colours differ so the gradient is visible`() {
        val seeds = (1..50).map { "track-$it" }
        seeds.forEach { seed ->
            val (start, end) = artworkGradientColors(seed)
            assertNotEquals("gradient for '$seed' is flat", start, end)
        }
    }

    @Test
    fun `negative hash codes stay inside the palette`() {
        // "polygenelubricants" is a well known string with a negative hashCode.
        assertTrue("polygenelubricants".hashCode() < 0)
        val (start, end) = artworkGradientColors("polygenelubricants")
        assertNotEquals(start, end)
    }

    @Test
    fun `every palette index is reachable`() {
        val seen = mutableSetOf<Pair<androidx.compose.ui.graphics.Color, androidx.compose.ui.graphics.Color>>()
        (0 until 2_000).forEach { seen += artworkGradientColors("seed-$it") }
        assertEquals(artworkGradientCount, seen.size)
    }

    @Test
    fun `initial uses the first letter of the title`() {
        assertEquals("A", artworkInitial("Akhareh"))
        assertEquals("M", artworkInitial("  mix 2"))
    }

    @Test
    fun `initial falls back to a note for blank titles`() {
        assertEquals("\u266A", artworkInitial(""))
        assertEquals("\u266A", artworkInitial("   "))
    }

    @Test
    fun `initial keeps surrogate pairs intact`() {
        val emoji = "\uD83C\uDFB5 Song"
        val initial = artworkInitial(emoji)
        assertEquals(2, initial.length)
        assertEquals(emoji.substring(0, 2).uppercase(), initial)
    }
}
