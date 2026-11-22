package com.musichub.mobile.ui.home

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.Button
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ElevatedCard
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.musichub.mobile.R
import com.musichub.mobile.data.AppContainer
import com.musichub.mobile.data.library.HomeData
import com.musichub.mobile.data.local.AlbumEntity
import com.musichub.mobile.data.local.ArtistEntity
import com.musichub.mobile.data.local.TrackEntity
import com.musichub.mobile.ui.components.ArtworkImage
import com.musichub.mobile.ui.components.FavoriteButton
import com.musichub.mobile.ui.components.placeholderBrush
import com.musichub.mobile.ui.errorMessage

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    container: AppContainer,
    onOpenAlbum: (String) -> Unit,
    onOpenArtist: (String) -> Unit,
    onPlayTracks: () -> Unit,
) {
    val viewModel: HomeViewModel = viewModel(
        factory = viewModelFactory { initializer { HomeViewModel(container) } },
    )
    val state by viewModel.state.collectAsStateWithLifecycle()
    val selectedTab by viewModel.selectedTab.collectAsStateWithLifecycle()
    val allTracks by viewModel.tracks.collectAsStateWithLifecycle()
    val allAlbums by viewModel.albums.collectAsStateWithLifecycle()
    val allArtists by viewModel.artists.collectAsStateWithLifecycle()
    val allFavorites by viewModel.favorites.collectAsStateWithLifecycle()

    Column(modifier = Modifier.fillMaxSize()) {
        CategoryFilterRow(
            selectedTab = selectedTab,
            onSelectTab = viewModel::selectTab,
        )

        when (selectedTab) {
            HomeTab.All -> {
                when (val home = state) {
                    HomeState.Loading -> {
                        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            CircularProgressIndicator()
                        }
                    }

                    is HomeState.Failed -> {
                        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            Column(
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(12.dp),
                            ) {
                                Text(errorMessage(home.error))
                                Button(onClick = { viewModel.reload() }) {
                                    Text(stringResource(R.string.action_retry))
                                }
                            }
                        }
                    }

                    is HomeState.Success -> PullToRefreshBox(
                        isRefreshing = home.refreshing,
                        onRefresh = viewModel::refresh,
                        modifier = Modifier.fillMaxSize(),
                    ) {
                        HomeContent(
                            data = home.data,
                            container = container,
                            onOpenAlbum = onOpenAlbum,
                            onPlayTracks = { tracks, startIndex ->
                                viewModel.playTracks(tracks, startIndex)
                                onPlayTracks()
                            },
                            onToggleFavorite = viewModel::toggleFavorite,
                        )
                    }
                }
            }

            HomeTab.Tracks -> {
                FullTrackList(
                    tracks = allTracks,
                    container = container,
                    onPlayTracks = { tracks, startIndex ->
                        viewModel.playTracks(tracks, startIndex)
                        onPlayTracks()
                    },
                    onToggleFavorite = viewModel::toggleFavorite,
                )
            }

            HomeTab.Albums -> {
                FullAlbumList(
                    albums = allAlbums,
                    container = container,
                    onOpenAlbum = onOpenAlbum,
                )
            }

            HomeTab.Artists -> {
                FullArtistList(
                    artists = allArtists,
                    onOpenArtist = onOpenArtist,
                )
            }

            HomeTab.Favorites -> {
                FullTrackList(
                    tracks = allFavorites,
                    container = container,
                    onPlayTracks = { tracks, startIndex ->
                        viewModel.playTracks(tracks, startIndex)
                        onPlayTracks()
                    },
                    onToggleFavorite = viewModel::toggleFavorite,
                )
            }
        }
    }
}

@Composable
private fun CategoryFilterRow(
    selectedTab: HomeTab,
    onSelectTab: (HomeTab) -> Unit,
    modifier: Modifier = Modifier,
) {
    LazyRow(
        modifier = modifier.fillMaxWidth(),
        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        items(HomeTab.entries.toTypedArray()) { tab ->
            val isSelected = tab == selectedTab
            Surface(
                onClick = { onSelectTab(tab) },
                shape = RoundedCornerShape(50),
                color = if (isSelected) {
                    MaterialTheme.colorScheme.primary
                } else {
                    MaterialTheme.colorScheme.surfaceVariant
                },
                contentColor = if (isSelected) {
                    MaterialTheme.colorScheme.onPrimary
                } else {
                    MaterialTheme.colorScheme.onSurfaceVariant
                },
            ) {
                Text(
                    text = when (tab) {
                        HomeTab.All -> stringResource(R.string.home_tab_all)
                        HomeTab.Tracks -> stringResource(R.string.library_tracks)
                        HomeTab.Albums -> stringResource(R.string.library_albums)
                        HomeTab.Artists -> stringResource(R.string.library_artists)
                        HomeTab.Favorites -> stringResource(R.string.library_favorites)
                    },
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Medium,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                )
            }
        }
    }
}

@Composable
private fun FullTrackList(
    tracks: List<TrackEntity>,
    container: AppContainer,
    onPlayTracks: (tracks: List<TrackEntity>, startIndex: Int) -> Unit,
    onToggleFavorite: (String) -> Unit,
) {
    if (tracks.isEmpty()) {
        EmptyStateNotice(stringResource(R.string.library_empty))
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(top = 4.dp, bottom = 24.dp),
    ) {
        itemsIndexed(tracks, key = { _, track -> track.id }) { index, track ->
            Surface(
                onClick = { onPlayTracks(tracks, index) },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 8.dp),
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
                            text = track.title,
                            style = MaterialTheme.typography.bodyLarge,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = listOf(track.artist, track.album).filter { it.isNotBlank() }.joinToString(" • "),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    FavoriteButton(
                        isFavorite = track.isFavorite,
                        onToggle = { onToggleFavorite(track.id) },
                    )
                }
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f))
        }
    }
}

@Composable
private fun FullAlbumList(
    albums: List<AlbumEntity>,
    container: AppContainer,
    onOpenAlbum: (String) -> Unit,
) {
    if (albums.isEmpty()) {
        EmptyStateNotice(stringResource(R.string.library_empty))
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(top = 4.dp, bottom = 24.dp),
    ) {
        items(albums, key = { it.id }) { album ->
            Surface(
                onClick = { onOpenAlbum(album.id) },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    ArtworkImage(
                        artworkUrl = album.artworkUrl,
                        seed = album.title,
                        loader = container.artworkLoader,
                        size = 56.dp,
                    )
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = album.title,
                            style = MaterialTheme.typography.bodyLarge,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = listOfNotNull(album.albumArtist, album.year?.toString())
                                .filter { it.isNotBlank() }
                                .joinToString(" • "),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f))
        }
    }
}

@Composable
private fun FullArtistList(
    artists: List<ArtistEntity>,
    onOpenArtist: (String) -> Unit,
) {
    if (artists.isEmpty()) {
        EmptyStateNotice(stringResource(R.string.library_empty))
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(top = 4.dp, bottom = 24.dp),
    ) {
        items(artists, key = { it.id }) { artist ->
            Surface(
                onClick = { onOpenArtist(artist.id) },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .size(52.dp)
                            .clip(CircleShape)
                            .background(placeholderBrush(artist.name)),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Person,
                            contentDescription = null,
                            tint = androidx.compose.ui.graphics.Color.White,
                            modifier = Modifier.size(28.dp),
                        )
                    }
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = artist.name,
                            style = MaterialTheme.typography.bodyLarge,
                            fontWeight = FontWeight.SemiBold,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = stringResource(R.string.detail_artist),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f))
        }
    }
}

@Composable
private fun EmptyStateNotice(text: String) {
    Box(
        modifier = Modifier.fillMaxSize(),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = text,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(24.dp),
        )
    }
}

@Composable
private fun HomeContent(
    data: HomeData,
    container: AppContainer,
    onOpenAlbum: (String) -> Unit,
    onPlayTracks: (tracks: List<TrackEntity>, startIndex: Int) -> Unit,
    onToggleFavorite: (String) -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(top = 8.dp, bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        item { HomeHeader() }

        if (data.fromCache) {
            item {
                Text(
                    text = stringResource(R.string.home_cache_notice),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.secondary,
                    modifier = Modifier.padding(horizontal = 16.dp),
                )
            }
        }

        item {
            SectionTitle(
                text = stringResource(R.string.home_recently_added),
                icon = Icons.Filled.Star,
            )
        }
        item {
            TrackRow(
                tracks = data.recentlyAdded,
                container = container,
                onPlayTracks = onPlayTracks,
                onToggleFavorite = onToggleFavorite,
            )
        }

        item {
            SectionTitle(
                text = stringResource(R.string.home_recently_played),
                icon = Icons.Filled.PlayArrow,
            )
        }
        item {
            TrackRow(
                tracks = data.recentlyPlayed,
                container = container,
                onPlayTracks = onPlayTracks,
                onToggleFavorite = onToggleFavorite,
            )
        }

        item {
            SectionTitle(
                text = stringResource(R.string.home_most_played),
                icon = Icons.Filled.PlayArrow,
            )
        }
        item {
            TrackRow(
                tracks = data.mostPlayed,
                container = container,
                onPlayTracks = onPlayTracks,
                onToggleFavorite = onToggleFavorite,
            )
        }

        item {
            SectionTitle(
                text = stringResource(R.string.home_favorites),
                icon = Icons.Filled.Favorite,
            )
        }
        item {
            TrackRow(
                tracks = data.favorites,
                container = container,
                onPlayTracks = onPlayTracks,
                onToggleFavorite = onToggleFavorite,
            )
        }

        item { SectionTitle(text = stringResource(R.string.home_albums), icon = Icons.Filled.Star) }
        item { AlbumRow(data.albums, container, onOpenAlbum) }
    }
}

/** Gradient hero so the top of Home is not a bare list. */
@Composable
private fun HomeHeader() {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp)
            .clip(RoundedCornerShape(18.dp))
            .background(placeholderBrush("MusicHub"))
            .padding(horizontal = 18.dp, vertical = 20.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(
                text = stringResource(R.string.home_welcome),
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold,
                color = androidx.compose.ui.graphics.Color.White,
            )
            Text(
                text = stringResource(R.string.home_pull_hint),
                style = MaterialTheme.typography.bodySmall,
                color = androidx.compose.ui.graphics.Color.White.copy(alpha = 0.85f),
            )
        }
    }
}

@Composable
private fun SectionTitle(text: String, icon: ImageVector) {
    Row(
        modifier = Modifier.padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Icon(
            imageVector = icon,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.size(20.dp),
        )
        Text(
            text = text,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
        )
    }
}

@Composable
private fun TrackRow(
    tracks: List<TrackEntity>,
    container: AppContainer,
    onPlayTracks: (tracks: List<TrackEntity>, startIndex: Int) -> Unit,
    onToggleFavorite: (String) -> Unit,
) {
    if (tracks.isEmpty()) {
        EmptyRow()
        return
    }
    LazyRow(
        contentPadding = PaddingValues(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(tracks, key = { it.id }) { track ->
            ElevatedCard(
                onClick = { onPlayTracks(tracks, tracks.indexOf(track)) },
                shape = RoundedCornerShape(14.dp),
                colors = CardDefaults.elevatedCardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant,
                ),
                modifier = Modifier.width(160.dp),
            ) {
                ArtworkImage(
                    artworkUrl = track.artworkUrl,
                    seed = track.title,
                    loader = container.artworkLoader,
                    size = null,
                    cornerRadius = 0.dp,
                    modifier = Modifier.fillMaxWidth().height(160.dp),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(
                        modifier = Modifier
                            .weight(1f)
                            .padding(start = 12.dp, top = 8.dp, bottom = 10.dp),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(
                            track.title,
                            style = MaterialTheme.typography.titleSmall,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            track.artist,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    FavoriteButton(
                        isFavorite = track.isFavorite,
                        onToggle = { onToggleFavorite(track.id) },
                    )
                }
            }
        }
    }
}

@Composable
private fun AlbumRow(
    albums: List<AlbumEntity>,
    container: AppContainer,
    onOpenAlbum: (String) -> Unit,
) {
    if (albums.isEmpty()) {
        EmptyRow()
        return
    }
    LazyRow(
        contentPadding = PaddingValues(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(albums, key = { it.id }) { album ->
            ElevatedCard(
                onClick = { onOpenAlbum(album.id) },
                shape = RoundedCornerShape(14.dp),
                colors = CardDefaults.elevatedCardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant,
                ),
                modifier = Modifier.width(150.dp),
            ) {
                ArtworkImage(
                    artworkUrl = album.artworkUrl,
                    seed = album.title,
                    loader = container.artworkLoader,
                    size = null,
                    cornerRadius = 0.dp,
                    modifier = Modifier.fillMaxWidth().height(150.dp),
                )
                Column(
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
                    verticalArrangement = Arrangement.spacedBy(2.dp),
                ) {
                    Text(
                        album.title,
                        style = MaterialTheme.typography.titleSmall,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = listOfNotNull(album.albumArtist, album.year?.toString())
                            .filter { it.isNotBlank() }
                            .joinToString(" • "),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
    }
}

@Composable
private fun EmptyRow() {
    Column(modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
        Text(
            text = stringResource(R.string.library_empty),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
