import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { Track } from '../api/types';
import { usePlayer } from '../player/PlayerContext';
import { Artwork } from '../ui/Artwork';
import { ThreeDIcon } from '../ui/ThreeDIcons';

type Tab = 'all' | 'tracks' | 'albums' | 'favorites';

function getGreeting(): string {
  const hour = new Date().getHours();
  if (hour >= 5 && hour < 12) return 'صبح بخیر';
  if (hour >= 12 && hour < 17) return 'عصر بخیر';
  if (hour >= 17 && hour < 24) return 'شب بخیر';
  return 'بامداد بخیر';
}

export function HomePage({
  onOpenAlbum,
}: {
  onOpenAlbum?: (albumId: string) => void;
}) {
  const { playTrack, currentTrack, isPlaying, toggleFavorite, isFavorite } = usePlayer();
  const [activeTab, setActiveTab] = useState<Tab>('all');

  // Fetch recent tracks
  const tracksQuery = useQuery({
    queryKey: ['tracks', 'recent'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 30, sort: 'newest' }),
  });

  // Fetch most played / popular
  const popularQuery = useQuery({
    queryKey: ['tracks', 'popular'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 20 }),
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

  // Spotify-style 6 quick-access tiles
  const quickTracks = recentTracks.slice(0, 5);

  return (
    <div className="spotify-home-container">
      {/* Top Greeting Header */}
      <header className="spotify-greeting-header">
        <div className="spotify-greeting-row">
          <h1 className="spotify-greeting-title">{getGreeting()}</h1>
          <div className="spotify-status-indicator" title="متصل به سرور">
            <span className="spotify-status-dot"></span>
            <span className="spotify-status-text">آنلاین</span>
          </div>
        </div>

        {/* Spotify Category Filter Pills */}
        <div className="spotify-filter-pills">
          <button
            type="button"
            className={`spotify-pill${activeTab === 'all' ? ' is-active' : ''}`}
            onClick={() => setActiveTab('all')}
          >
            همه
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'tracks' ? ' is-active' : ''}`}
            onClick={() => setActiveTab('tracks')}
          >
            موسیقی
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'albums' ? ' is-active' : ''}`}
            onClick={() => setActiveTab('albums')}
          >
            آلبوم‌ها
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'favorites' ? ' is-active' : ''}`}
            onClick={() => setActiveTab('favorites')}
          >
            علاقه‌مندی‌ها
          </button>
        </div>
      </header>

      {/* View: All (Default Spotify Home) */}
      {activeTab === 'all' && (
        <div className="spotify-home-content">
          {/* 1. Spotify Signature 6-Grid Quick Access */}
          <section className="spotify-section">
            <div className="spotify-quick-grid">
              {/* Liked Songs Tile */}
              <div
                className="spotify-quick-tile liked-tile"
                onClick={() => {
                  if (favoriteTracks.length > 0) {
                    void playTrack(favoriteTracks[0]!, favoriteTracks);
                  } else {
                    setActiveTab('favorites');
                  }
                }}
              >
                <div className="spotify-quick-art liked-art">
                  <ThreeDIcon type="heart" size={28} />
                </div>
                <span className="spotify-quick-title">آهنگ‌های پسندیده‌شده</span>
                <button
                  type="button"
                  className="spotify-quick-play"
                  aria-label="پخش"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (favoriteTracks.length > 0) {
                      void playTrack(favoriteTracks[0]!, favoriteTracks);
                    }
                  }}
                >
                  ▶
                </button>
              </div>

              {/* Up to 5 Quick Tracks */}
              {quickTracks.map((tr) => (
                <div
                  key={tr.id}
                  className={`spotify-quick-tile${currentTrack?.id === tr.id ? ' is-active' : ''}`}
                  onClick={() => void playTrack(tr, recentTracks)}
                >
                  <div className="spotify-quick-art">
                    <Artwork url={tr.artworkUrl} size={52} />
                  </div>
                  <span className="spotify-quick-title">{tr.title}</span>
                  <button
                    type="button"
                    className={`spotify-quick-play${currentTrack?.id === tr.id && isPlaying ? ' is-playing' : ''}`}
                    aria-label="پخش"
                  >
                    {currentTrack?.id === tr.id && isPlaying ? '⏸' : '▶'}
                  </button>
                </div>
              ))}
            </div>
          </section>

          {/* 2. Recently Added Carousel */}
          {recentTracks.length > 0 && (
            <section className="spotify-section">
              <div className="spotify-section-header">
                <h2 className="spotify-section-title">تازه‌ها و اخیراً بارگذاری‌شده</h2>
              </div>
              <div className="spotify-carousel">
                {recentTracks.slice(0, 12).map((track) => (
                  <div
                    key={track.id}
                    className="spotify-card"
                    onClick={() => void playTrack(track, recentTracks)}
                  >
                    <div className="spotify-card-art-wrap">
                      <Artwork url={track.artworkUrl} size={140} />
                      <button
                        type="button"
                        className={`spotify-card-play-btn${currentTrack?.id === track.id && isPlaying ? ' is-playing' : ''}`}
                        aria-label="پخش"
                      >
                        {currentTrack?.id === track.id && isPlaying ? '⏸' : '▶'}
                      </button>
                    </div>
                    <span className="spotify-card-title">{track.title}</span>
                    <span className="spotify-card-subtitle">{track.artist || '—'}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* 3. Featured Albums Carousel */}
          {albums.length > 0 && (
            <section className="spotify-section">
              <div className="spotify-section-header">
                <h2 className="spotify-section-title">آلبوم‌های برگزیده</h2>
              </div>
              <div className="spotify-carousel">
                {albums.map((album) => (
                  <div
                    key={album.id}
                    className="spotify-card"
                    onClick={() => onOpenAlbum?.(album.id)}
                  >
                    <div className="spotify-card-art-wrap">
                      <Artwork url={album.artworkUrl} size={140} />
                      <button
                        type="button"
                        className="spotify-card-play-btn"
                        aria-label="پخش"
                      >
                        ▶
                      </button>
                    </div>
                    <span className="spotify-card-title">{album.title}</span>
                    <span className="spotify-card-subtitle">{album.albumArtist || 'آلبوم'}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* 4. Most Popular / Listened Tracks */}
          {popularTracks.length > 0 && (
            <section className="spotify-section">
              <div className="spotify-section-header">
                <h2 className="spotify-section-title">بیشترین پخش و محبوب‌ترین‌ها</h2>
              </div>
              <div className="spotify-track-list">
                {popularTracks.slice(0, 8).map((track, idx) => (
                  <TrackListItem
                    key={track.id}
                    track={track}
                    index={idx + 1}
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
        </div>
      )}

      {/* View: Tracks Tab */}
      {activeTab === 'tracks' && (
        <div className="spotify-track-list" style={{ marginTop: 14 }}>
          {recentTracks.map((track, idx) => (
            <TrackListItem
              key={track.id}
              track={track}
              index={idx + 1}
              isCurrent={currentTrack?.id === track.id}
              isPlaying={currentTrack?.id === track.id && isPlaying}
              isFav={isFavorite(track.id)}
              onPlay={() => void playTrack(track, recentTracks)}
              onToggleFav={() => void toggleFavorite(track)}
            />
          ))}
        </div>
      )}

      {/* View: Albums Tab */}
      {activeTab === 'albums' && (
        <div className="spotify-albums-grid" style={{ marginTop: 14 }}>
          {albums.map((album) => (
            <div
              key={album.id}
              className="spotify-grid-card"
              onClick={() => onOpenAlbum?.(album.id)}
            >
              <Artwork url={album.artworkUrl} size={150} />
              <span className="spotify-card-title">{album.title}</span>
              <span className="spotify-card-subtitle">{album.albumArtist || '—'}</span>
            </div>
          ))}
        </div>
      )}

      {/* View: Favorites Tab */}
      {activeTab === 'favorites' && (
        <div className="spotify-favorites-view" style={{ marginTop: 14 }}>
          {favoriteTracks.length === 0 ? (
            <div className="spotify-empty-state">
              <ThreeDIcon type="heart" size={60} />
              <h3>هنوز آهنگی را لایک نکرده‌اید</h3>
              <p>هر آهنگی که دوست دارید را با دکمه قلب ذخیره کنید تا اینجا ظاهر شود.</p>
            </div>
          ) : (
            <div className="spotify-track-list">
              {favoriteTracks.map((track, idx) => (
                <TrackListItem
                  key={track.id}
                  track={track}
                  index={idx + 1}
                  isCurrent={currentTrack?.id === track.id}
                  isPlaying={currentTrack?.id === track.id && isPlaying}
                  isFav={true}
                  onPlay={() => void playTrack(track, favoriteTracks)}
                  onToggleFav={() => void toggleFavorite(track)}
                />
              ))}
            </div>
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
  index,
}: {
  track: Track;
  isCurrent: boolean;
  isPlaying: boolean;
  isFav: boolean;
  onPlay: () => void;
  onToggleFav?: () => void;
  index?: number;
}) {
  return (
    <div className={`spotify-track-row${isCurrent ? ' is-active' : ''}`} onClick={onPlay}>
      {index !== undefined && (
        <span className={`spotify-track-index${isCurrent ? ' text-spotify-green' : ''}`}>
          {isCurrent && isPlaying ? '▶' : index}
        </span>
      )}
      <div className="spotify-track-thumb">
        <Artwork url={track.artworkUrl} size={46} />
      </div>
      <div className="spotify-track-info">
        <span className={`spotify-track-name${isCurrent ? ' text-spotify-green' : ''}`}>
          {track.title}
        </span>
        <span className="spotify-track-meta">{track.artist || '—'}</span>
      </div>
      {onToggleFav && (
        <button
          type="button"
          className="spotify-heart-btn"
          onClick={(e) => {
            e.stopPropagation();
            onToggleFav();
          }}
          aria-label="Favorite"
        >
          {isFav ? '💚' : '🤍'}
        </button>
      )}
    </div>
  );
}
