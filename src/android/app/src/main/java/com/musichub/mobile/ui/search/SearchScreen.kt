package com.musichub.mobile.ui.search

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.musichub.mobile.R
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.local.TrackEntity
import com.musichub.mobile.ui.components.ArtworkImage
import com.musichub.mobile.ui.components.FavoriteButton
import com.musichub.mobile.ui.errorMessage

@Composable
fun SearchScreen(
    container: AppContainer,
    onPlayTracks: () -> Unit,
) {
    val viewModel: SearchViewModel = viewModel(
        factory = viewModelFactory { initializer { SearchViewModel(container) } },
    )
    val state by viewModel.state.collectAsStateWithLifecycle()

    Column(modifier = Modifier.fillMaxSize()) {
        OutlinedTextField(
            value = state.query,
            onValueChange = viewModel::updateQuery,
            label = { Text(stringResource(R.string.search_hint)) },
            leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
        )

        if (state.searching) {
            CircularProgressIndicator(modifier = Modifier.padding(16.dp))
        }

        state.error?.let {
            Text(
                text = errorMessage(it),
                color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
            )
        }

        val results = state.results
        if (results == null) return@Column

        val tracks = results.tracks
        val hasAnything = tracks.isNotEmpty() || results.albums.isNotEmpty() ||
            results.artists.isNotEmpty() || results.folders.isNotEmpty()

        if (!hasAnything) {
            Text(
                text = stringResource(R.string.search_empty, state.query),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(16.dp),
            )
            return@Column
        }

        LazyColumn(contentPadding = PaddingValues(bottom = 24.dp)) {
            if (tracks.isNotEmpty()) {
                item { GroupHeader(stringResource(R.string.library_tracks)) }
                itemsIndexed(tracks, key = { _, track -> track.id }) { index, track ->
                    TrackResultRow(
                        track = track,
                        container = container,
                        onClick = {
                            viewModel.play(tracks, index)
                            onPlayTracks()
                        },
                        onToggleFavorite = { viewModel.toggleFavorite(track) },
                    )
                }
            }
            if (results.albums.isNotEmpty()) {
                item { GroupHeader(stringResource(R.string.library_albums)) }
                items(results.albums, key = { it.id }) { album ->
                    ResultRow(
                        title = album.title,
                        subtitle = listOfNotNull(album.albumArtist, album.year?.toString())
                            .joinToString(" • "),
                        onClick = { },
                    )
                }
            }
            if (results.artists.isNotEmpty()) {
                item { GroupHeader(stringResource(R.string.library_artists)) }
                items(results.artists, key = { it.id }) { artist ->
                    ResultRow(title = artist.name, subtitle = "", onClick = { })
                }
            }
            if (results.folders.isNotEmpty()) {
                item { GroupHeader(stringResource(R.string.library_folders)) }
                items(results.folders, key = { it.id }) { folder ->
                    ResultRow(title = folder.name, subtitle = "", onClick = { })
                }
            }
        }
    }
}

@Composable
private fun GroupHeader(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
    )
}

@Composable
private fun TrackResultRow(
    track: TrackEntity,
    container: AppContainer,
    onClick: () -> Unit,
    onToggleFavorite: () -> Unit,
) {
    Surface(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 16.dp, end = 4.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            ArtworkImage(
                artworkUrl = track.artworkUrl,
                seed = track.title,
                loader = container.artworkLoader,
                size = 44.dp,
            )
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    track.title,
                    style = MaterialTheme.typography.bodyLarge,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = listOf(track.artist, track.album)
                        .filter { it.isNotBlank() }
                        .joinToString(" • "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            FavoriteButton(isFavorite = track.isFavorite, onToggle = onToggleFavorite)
        }
    }
    HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
}

@Composable
private fun ResultRow(title: String, subtitle: String, onClick: () -> Unit) {
    Surface(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            Text(title, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (subtitle.isNotBlank()) {
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
    HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
}
