package com.musichub.mobile.ui.components

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.musichub.mobile.data.api.ArtworkLoader
import com.musichub.mobile.ui.theme.artworkGradientColors
import com.musichub.mobile.ui.theme.artworkInitial

/**
 * Album/track cover with a deterministic gradient fallback.
 *
 * The server only exposes artwork for tracks that actually have an extracted cover, so most rows
 * have nothing to draw. Instead of an empty box the fallback shows a two-tone gradient derived from
 * the title plus its first letter, which keeps every list visually consistent.
 *
 * Pass `size = null` when the caller supplies its own sizing modifier (for example a card cover that
 * should fill the card width). A fixed `size` combined with `fillMaxWidth()` is order dependent and
 * silently clips, so the two are mutually exclusive here.
 */
@Composable
fun ArtworkImage(
    artworkUrl: String?,
    seed: String,
    loader: ArtworkLoader,
    modifier: Modifier = Modifier,
    size: Dp? = 56.dp,
    cornerRadius: Dp = 10.dp,
) {
    val shape = RoundedCornerShape(cornerRadius)
    var bitmap by remember(artworkUrl) { mutableStateOf<android.graphics.Bitmap?>(null) }

    LaunchedEffect(artworkUrl) {
        bitmap = if (artworkUrl.isNullOrBlank()) null else loader.load(artworkUrl)
    }

    val sized = if (size != null) modifier.size(size) else modifier
    val current = bitmap
    Box(
        modifier = sized
            .clip(shape)
            .background(placeholderBrush(seed)),
        contentAlignment = Alignment.Center,
    ) {
        if (current != null) {
            Image(
                bitmap = current.asImageBitmap(),
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize(),
            )
        } else {
            androidx.compose.foundation.Canvas(modifier = Modifier.fillMaxSize()) {
                val maxR = kotlin.math.min(this.size.width, this.size.height) / 2f
                val grooveColor = androidx.compose.ui.graphics.Color.White.copy(alpha = 0.12f)
                drawCircle(color = grooveColor, radius = maxR * 0.88f, style = androidx.compose.ui.graphics.drawscope.Stroke(1.5f))
                drawCircle(color = grooveColor, radius = maxR * 0.72f, style = androidx.compose.ui.graphics.drawscope.Stroke(1.5f))
                drawCircle(color = grooveColor, radius = maxR * 0.56f, style = androidx.compose.ui.graphics.drawscope.Stroke(1.5f))
                drawCircle(color = androidx.compose.ui.graphics.Color.Black.copy(alpha = 0.35f), radius = maxR * 0.38f)
                drawCircle(color = androidx.compose.ui.graphics.Color.White.copy(alpha = 0.3f), radius = maxR * 0.38f, style = androidx.compose.ui.graphics.drawscope.Stroke(1.5f))
                drawCircle(color = androidx.compose.ui.graphics.Color.Black.copy(alpha = 0.75f), radius = maxR * 0.12f)
            }
            Text(
                text = artworkInitial(seed),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                color = androidx.compose.ui.graphics.Color.White.copy(alpha = 0.95f),
            )
        }
    }
}

/** Gradient used behind a cover, also reused by the Now Playing hero panel. */
@Composable
fun placeholderBrush(seed: String): Brush {
    val (start, end) = artworkGradientColors(seed)
    return Brush.linearGradient(listOf(start, end))
}
