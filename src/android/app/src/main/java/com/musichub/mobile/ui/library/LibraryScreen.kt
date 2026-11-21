package com.musichub.mobile.ui.library

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
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
fun LibraryScreen(
    container: AppContainer,
    onPlayTracks: () -> Unit,
    initialSection: LibrarySection = LibrarySection.Tracks,
) {
    val viewModel: LibraryViewModel = viewModel(
        factory = viewModelFactory { initializer { LibraryViewModel(container) } },
    )
    val section by viewModel.section.collectAsStateWithLifecycle()
    val refreshing by viewModel.refreshing.collectAsStateWithLifecycle()
    val error by viewModel.error.collectAsStateWithLifecycle()
    val openAlbum by viewModel.openAlbum.collectAsStateWithLifecycle()

    androidx.compose.runtime.LaunchedEffect(initialSection) {
        viewModel.select(initialSection)
    }

    val tracks by viewModel.tracks.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()

    val current = openAlbum
    if (current != null) {
        AlbumTrackList(
            state = current,
            container = container,
            onBack = viewModel::closeAlbum,
            onPlay = { index ->
                viewModel.playFromAlbum(index)
                onPlayTracks()
            },
            onToggleFavorite = viewModel::toggleFavorite,
        )
        return
    }

    Column(modifier = Modifier.fillMaxSize()) {
        SectionTabs(selected = section, onSelect = viewModel::select)
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = when (section) {
                    LibrarySection.Tracks -> stringResource(R.string.library_tracks)
                    LibrarySection.Albums -> stringResource(R.string.library_albums)
                    LibrarySection.Artists -> stringResource(R.string.library_artists)
                    LibrarySection.Playlists -> stringResource(R.string.library_playlists)
                    LibrarySection.Favorites -> stringResource(R.string.library_favorites)
                },
                style = MaterialTheme.typography.titleLarge,
            )
            TextButton(onClick = viewModel::refresh, enabled = !refreshing) {
                Text(stringResource(R.string.action_refresh))
            }
        }
        if (refreshing) {
            LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
        }
        error?.let {
            Text(
                text = errorMessage(it),
                color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
            )
        }

        when (section) {
            LibrarySection.Tracks -> TrackList(
                tracks = tracks,
                container = container,
                onPlay = { index ->
                    viewModel.playTracks(tracks, index)
                    onPlayTracks()
                },
                onToggleFavorite = viewModel::toggleFavorite,
            )

            LibrarySection.Favorites -> TrackList(
                tracks = favorites,
                container = container,
                onPlay = { index ->
                    viewModel.playTracks(favorites, index)
                    onPlayTracks()
                },
                onToggleFavorite = viewModel::toggleFavorite,
            )

            LibrarySection.Albums -> SimpleList(
                items = albums.map { it.id to it.title },
                emptyText = stringResource(R.string.library_empty),
                onSelect = viewModel::openAlbum,
            )

            LibrarySection.Artists -> SimpleList(
                items = artists.map { it.id to it.name },
                emptyText = stringResource(R.string.library_empty),
                onSelect = { },
            )

            LibrarySection.Playlists -> SimpleList(
                items = playlists.map { it.id to it.name },
                emptyText = stringResource(R.string.library_empty),
                onSelect = { },
            )
        }
    }
}

@Composable
private fun SectionTabs(selected: LibrarySection, onSelect: (LibrarySection) -> Unit) {
    Column {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
        ) {
            LibrarySection.entries.forEach { entry ->
                TextButton(onClick = { onSelect(entry) }) {
                    Text(
                        text = when (entry) {
                            LibrarySection.Tracks -> stringResource(R.string.library_tracks)
                            LibrarySection.Albums -> stringResource(R.string.library_albums)
                            LibrarySection.Artists -> stringResource(R.string.library_artists)
                            LibrarySection.Playlists -> stringResource(R.string.library_playlists)
                            LibrarySection.Favorites -> stringResource(R.string.library_favorites)
                        },
                        color = if (entry == selected) {
                            MaterialTheme.colorScheme.primary
                        } else {
                            MaterialTheme.colorScheme.onSurfaceVariant
                        },
                        style = MaterialTheme.typography.labelMedium,
                        maxLines = 1,
                    )
                }
            }
        }
        HorizontalDivider()
    }
}

@Composable
private fun TrackList(
    tracks: List<TrackEntity>,
    container: AppContainer,
    onPlay: (Int) -> Unit,
    onToggleFavorite: (String) -> Unit,
) {
    if (tracks.isEmpty()) {
        EmptyState()
        return
    }
    LazyColumn(contentPadding = PaddingValues(bottom = 24.dp)) {
        itemsIndexed(tracks, key = { _, track -> track.id }) { index, track ->
            TrackRowItem(
                track = track,
                container = container,
                onClick = { onPlay(index) },
                onToggleFavorite = { onToggleFavorite(track.id) },
            )
            HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
        }
    }
}

@Composable
private fun TrackRowItem(
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
                size = 48.dp,
            )
            Column(modifier = Modifier.weight(1f)) {
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
}

@Composable
private fun SimpleList(
    items: List<Pair<String, String>>,
    emptyText: String,
    onSelect: (String) -> Unit,
) {
    if (items.isEmpty()) {
        EmptyState(text = emptyText)
        return
    }
    LazyColumn(contentPadding = PaddingValues(bottom = 24.dp)) {
        items(items, key = { it.first }) { (id, label) ->
            Surface(onClick = { onSelect(id) }, modifier = Modifier.fillMaxWidth()) {
                Text(
                    text = label,
                    style = MaterialTheme.typography.bodyLarge,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 14.dp),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
        }
    }
}

@Composable
private fun AlbumTrackList(
    state: AlbumTracksState,
    container: AppContainer,
    onBack: () -> Unit,
    onPlay: (Int) -> Unit,
    onToggleFavorite: (String) -> Unit,
) {
    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TextButton(onClick = onBack) {
                Text(stringResource(R.string.common_see_all))
            }
            Text(
                text = state.album.title,
                style = MaterialTheme.typography.titleMedium,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (state.tracks.isNotEmpty()) {
            Button(
                onClick = { onPlay(0) },
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
            ) {
                Text(stringResource(R.string.action_play_all))
            }
        }
        if (state.tracks.isEmpty()) {
            EmptyState(text = stringResource(R.string.detail_no_tracks))
        } else {
            LazyColumn(contentPadding = PaddingValues(bottom = 24.dp)) {
                itemsIndexed(state.tracks, key = { _, track -> track.id }) { index, track ->
                    TrackRowItem(
                        track = track,
                        container = container,
                        onClick = { onPlay(index) },
                        onToggleFavorite = { onToggleFavorite(track.id) },
                    )
                    HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
                }
            }
        }
    }
}

@Composable
private fun EmptyState(text: String = stringResource(R.string.library_empty)) {
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(
            text = text,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(24.dp),
        )
    }
}
