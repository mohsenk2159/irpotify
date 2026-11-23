import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { Track } from '../api/types';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { Artwork } from '../ui/Artwork';
import { ThreeDIcon } from '../ui/ThreeDIcons';

type Tab = 'all' | 'tracks' | 'albums' | 'artists' | 'favorites';

export function HomePage({
  onOpenAlbum,
}: {
  onOpenAlbum?: (albumId: string) => void;
}) {
  const { t } = useUi();
  const { playTrack, currentTrack, isPlaying, toggleFavorite, isFavorite } = usePlayer();
  const [activeTab, setActiveTab] = useState<Tab>('all');

  // Fetch recent tracks
  const tracksQuery = useQuery({
    queryKey: ['tracks', 'recent'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 20, sort: 'newest' }),
  });

  // Fetch most played / popular
  const popularQuery = useQuery({
    queryKey: ['tracks', 'popular'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 15 }),
  });

  // Fetch albums
  const albumsQuery = useQuery({
    queryKey: ['albums', 'recent'],
    queryFn: () => api.albums.list({ page: 1, pageSize: 12 }),
  });

  const recentTracks = tracksQuery.data?.items ?? [];
  const popularTracks = popularQuery.data?.items ?? [];
  const albums = albumsQuery.data?.items ?? [];
  const favoriteTracks = recentTracks.filter((tr) => isFavorite(tr.id));

  return (
    <div className="page-container">
      {/* Category Filter Chips */}
      <div className="filter-chips-row">
        {(['all', 'tracks', 'albums', 'artists', 'favorites'] as Tab[]).map((tab) => (
          <button
            key={tab}
            type="button"
            className={`filter-chip${activeTab === tab ? ' is-active' : ''}`}
            onClick={() => setActiveTab(tab)}
          >
            {t(`tab.${tab}`)}
          </button>
        ))}
      </div>

      {/* Main Home Sections */}
      {activeTab === 'all' && (
        <>
          {/* Welcome Card */}
          <div className="welcome-banner">
            <div className="welcome-text">
              <span className="welcome-badge">● آنلاین</span>
              <h2>{t('app.name')}</h2>
              <p>کتابخانه شخصی موسیقی شما با بالاترین کیفیت</p>
            </div>
            <ThreeDIcon type="headphones" size={54} animated />
          </div>

          {/* Section: Recently Added */}
          {recentTracks.length > 0 && (
            <section className="home-section">
              <div className="section-header">
                <h3>{t('home.recentlyAdded')}</h3>
              </div>
              <div className="horizontal-scroll-row">
                {recentTracks.slice(0, 10).map((track) => (
                  <div
                    key={track.id}
                    className="track-card-compact"
                    onClick={() => void playTrack(track, recentTracks)}
                  >
                    <div className="card-artwork-wrap">
                      <Artwork url={track.artworkUrl} size={130} />
                      <button
                        type="button"
                        className={`card-play-badge${currentTrack?.id === track.id && isPlaying ? ' is-playing' : ''}`}
                        aria-label="Play"
                      >
                        {currentTrack?.id === track.id && isPlaying ? '⏸' : '▶'}
                      </button>
                    </div>
                    <span className="card-track-title">{track.title}</span>
                    <span className="card-track-artist">{track.artist || '—'}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Section: Featured Albums */}
          {albums.length > 0 && (
            <section className="home-section">
              <div className="section-header">
                <h3>{t('home.featuredAlbums')}</h3>
              </div>
              <div className="horizontal-scroll-row">
                {albums.map((album) => (
                  <div
                    key={album.id}
                    className="album-card-compact"
                    onClick={() => onOpenAlbum?.(album.id)}
                  >
                    <Artwork url={album.artworkUrl} size={130} />
                    <span className="card-track-title">{album.title}</span>
                    <span className="card-track-artist">{album.albumArtist || '—'}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Section: Most Played Tracks */}
          {popularTracks.length > 0 && (
            <section className="home-section">
              <div className="section-header">
                <h3>{t('home.mostPlayed')}</h3>
              </div>
              <div className="vertical-track-list">
                {popularTracks.slice(0, 8).map((track) => (
                  <TrackListItem
                    key={track.id}
                    track={track}
                    isCurrent={currentTrack?.id === track.id}
                    isPlaying={currentTrack?.id === track.id && isPlaying}
                    isFav={isFavorite(track.id)}
                    onPlay={() => void playTrack(track, popularTracks)}
                    onToggleFav={() => void toggleFavorite(track)}
                  />
                ))}
              </div>
            </section>
          )}
        </>
      )}

      {/* Tab: Tracks */}
      {activeTab === 'tracks' && (
        <div className="vertical-track-list" style={{ marginTop: 12 }}>
          {recentTracks.map((track) => (
            <TrackListItem
              key={track.id}
              track={track}
              isCurrent={currentTrack?.id === track.id}
              isPlaying={currentTrack?.id === track.id && isPlaying}
              isFav={isFavorite(track.id)}
              onPlay={() => void playTrack(track, recentTracks)}
              onToggleFav={() => void toggleFavorite(track)}
            />
          ))}
        </div>
      )}

      {/* Tab: Albums */}
      {activeTab === 'albums' && (
        <div className="grid-2-col" style={{ marginTop: 12 }}>
          {albums.map((album) => (
            <div
              key={album.id}
              className="album-card-grid"
              onClick={() => onOpenAlbum?.(album.id)}
            >
              <Artwork url={album.artworkUrl} size={150} />
              <span className="card-track-title">{album.title}</span>
              <span className="card-track-artist">{album.albumArtist || '—'}</span>
            </div>
          ))}
        </div>
      )}

      {/* Tab: Favorites */}
      {activeTab === 'favorites' && (
        <div className="vertical-track-list" style={{ marginTop: 12 }}>
          {favoriteTracks.length === 0 ? (
            <div className="empty-box">
              <ThreeDIcon type="heart" size={54} />
              <p>هنوز آهنگی را به علاقه‌مندی‌ها اضافه نکرده‌اید.</p>
            </div>
          ) : (
            favoriteTracks.map((track) => (
              <TrackListItem
                key={track.id}
                track={track}
                isCurrent={currentTrack?.id === track.id}
                isPlaying={currentTrack?.id === track.id && isPlaying}
                isFav={true}
                onPlay={() => void playTrack(track, favoriteTracks)}
                onToggleFav={() => void toggleFavorite(track)}
              />
            ))
          )}
        </div>
      )}
    </div>
  );
}

export function TrackListItem({
  track,
  isCurrent,
  isPlaying,
  isFav,
  onPlay,
  onToggleFav,
}: {
  track: Track;
  isCurrent: boolean;
  isPlaying: boolean;
  isFav: boolean;
  onPlay: () => void;
  onToggleFav?: () => void;
}) {
  return (
    <div className={`track-row${isCurrent ? ' is-active' : ''}`} onClick={onPlay}>
      <div className="track-row-art">
        <Artwork url={track.artworkUrl} size={48} />
        {isCurrent && (
          <span className="playing-pulse-indicator">
            {isPlaying ? '▶' : '⏸'}
          </span>
        )}
      </div>
      <div className="track-row-meta">
        <span className={`track-title${isCurrent ? ' text-green' : ''}`}>{track.title}</span>
        <span className="track-artist">{track.artist || '—'}</span>
      </div>
      {onToggleFav && (
        <button
          type="button"
          className="fav-toggle-btn"
          onClick={(e) => {
            e.stopPropagation();
            onToggleFav();
          }}
          aria-label="Favorite"
        >
          {isFav ? '❤️' : '🤍'}
        </button>
      )}
    </div>
  );
}
