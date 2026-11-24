import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { TrackListItem } from './HomePage';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export function SearchPage({ onOpenAlbum: _onOpenAlbum }: { onOpenAlbum?: (id: string) => void }) {
  const { t } = useUi();
  const { playTrack, currentTrack, isPlaying, toggleFavorite, isFavorite } = usePlayer();
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');

  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedQuery(query.trim());
    }, 300);
    return () => clearTimeout(handler);
  }, [query]);

  const searchResults = useQuery({
    queryKey: ['search', debouncedQuery],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 30, q: debouncedQuery }),
    enabled: debouncedQuery.length > 0,
  });

  const tracks = searchResults.data?.items ?? [];

  return (
    <div className="search-page">
      {/* Search Input Box */}
      <div className="search-input-wrapper">
        <span className="search-input-icon">🔍</span>
        <input
          type="text"
          className="search-input"
          placeholder={t('search.placeholder')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          autoFocus
        />
        {query.length > 0 && (
          <button
            type="button"
            className="search-clear-btn"
            onClick={() => setQuery('')}
          >
            ✕
          </button>
        )}
      </div>

      {debouncedQuery.length === 0 ? (
        <div className="empty-box" style={{ marginTop: 40 }}>
          <ThreeDIcon type="waves" size={60} animated />
          <h3>جستجوی هوشمند در کل موزیک‌ها</h3>
          <p>نام آهنگ، خواننده یا آلبوم مورد نظر خود را بنویسید.</p>
        </div>
      ) : searchResults.isLoading ? (
        <div className="loading-box">
          <ThreeDIcon type="vinyl" size={48} animated />
          <p>در حال جستجو…</p>
        </div>
      ) : tracks.length === 0 ? (
        <div className="empty-box">
          <ThreeDIcon type="cassette" size={54} />
          <p>{t('search.empty', { query: debouncedQuery })}</p>
        </div>
      ) : (
        <div className="vertical-track-list" style={{ marginTop: 16 }}>
          <div className="section-header">
            <h3>آهنگ‌های یافت‌شده ({tracks.length})</h3>
          </div>
          {tracks.map((track) => (
            <TrackListItem
              key={track.id}
              track={track}
              isCurrent={currentTrack?.id === track.id}
              isPlaying={currentTrack?.id === track.id && isPlaying}
              isFav={isFavorite(track.id)}
              onPlay={() => void playTrack(track, tracks)}
              onToggleFav={() => void toggleFavorite(track)}
            />
          ))}
        </div>
      )}
    </div>
  );
}
