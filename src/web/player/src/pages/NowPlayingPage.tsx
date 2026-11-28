import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { Icon } from '../ui/icons';
import { Artwork } from '../ui/Artwork';
import { ThreeDIcon } from '../ui/ThreeDIcons';
import { Modal } from '../ui/Modal';

interface NowPlayingPageProps {
  onBackToHome?: () => void;
  onBrowseTracks?: () => void;
}

export function NowPlayingPage({ onBackToHome, onBrowseTracks }: NowPlayingPageProps) {
  const {
    currentTrack,
    isPlaying,
    position,
    duration,
    queue,
    queueIndex,
    shuffle,
    repeatMode,
    volume,
    isBuffering,
    togglePlayPause,
    next,
    previous,
    seekTo,
    setVolume,
    toggleShuffle,
    cycleRepeat,
    toggleFavorite,
    isFavorite,
    playQueueItem,
    addToQueue,
    removeFromQueue,
  } = usePlayer();

  const { t, language, formatTime, notify } = useUi();
  const isFa = language === 'fa';
  const [showQueue, setShowQueue] = useState(false);
  const [showAddToQueue, setShowAddToQueue] = useState(false);
  const [queueSearch, setQueueSearch] = useState('');
  const [isSeeking, setIsSeeking] = useState(false);
  const [seekValue, setSeekValue] = useState(0);

  const catalogQuery = useQuery({
    queryKey: ['tracks', 'queue-picker'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 200 }),
    enabled: showAddToQueue,
  });
  const catalogTracks = (catalogQuery.data?.items ?? []).filter((tr) => {
    const q = queueSearch.trim().toLowerCase();
    if (!q) return true;
    return tr.title.toLowerCase().includes(q) || (tr.artist ?? '').toLowerCase().includes(q);
  });

  if (!currentTrack) {
    return (
      <div className="now-playing-empty">
        <div className="empty-vinyl-container">
          <ThreeDIcon name="vinyl" size={140} />
        </div>
        <h2 className="empty-title">{t('player.noTrack')}</h2>
        <p className="empty-subtitle">{t('player.noTrackHint')}</p>
        {(onBrowseTracks || onBackToHome) && (
          <button className="btn-primary" onClick={onBrowseTracks ?? onBackToHome}>
            <Icon name="music" size={18} />
            <span>{isFa ? 'مشاهده و انتخاب آهنگ‌ها' : 'Browse & Play Tracks'}</span>
          </button>
        )}
      </div>
    );
  }

  const liked = isFavorite(currentTrack.id);
  const progressRatio = duration > 0 ? (isSeeking ? seekValue : position) / duration : 0;

  const handleSeekStart = () => {
    setIsSeeking(true);
    setSeekValue(position);
  };

  const handleSeekChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSeekValue(Number(e.target.value));
  };

  const handleSeekEnd = () => {
    seekTo(seekValue);
    setIsSeeking(false);
  };

  return (
    <div className="now-playing-page">
      {/* Top Header Bar */}
      <header className="now-playing-topbar">
        {onBackToHome && (
          <button
            className="icon-btn-circle"
            onClick={onBackToHome}
            aria-label="بازگشت"
            title="بازگشت"
          >
            <Icon name="chevron-down" size={24} />
          </button>
        )}
        <div className="topbar-info">
          <span className="topbar-subtitle">
            {currentTrack.album || 'irpotify'}
          </span>
          <span className="topbar-title">{t('nav.nowPlaying')}</span>
        </div>
        <button
          className={`icon-btn-circle ${showQueue ? 'active' : ''}`}
          onClick={() => setShowQueue(!showQueue)}
          aria-label={t('player.queue')}
          title={t('player.queue')}
        >
          <Icon name="list-music" size={22} />
        </button>
      </header>

      {/* Main Center Area: Artwork & Info */}
      <div className="now-playing-content">
        <div className="now-playing-art-wrapper">
          <div className="now-playing-art-shadow" />
          <Artwork
            url={currentTrack.artworkUrl}
            alt={currentTrack.title}
            size={360}
            className="now-playing-art"
          />
        </div>

        {/* Track Title, Artist, & Favorite */}
        <div className="now-playing-meta-row">
          <div className="meta-text">
            <h1 className="now-playing-track-title" title={currentTrack.title}>
              {currentTrack.title}
            </h1>
            <p className="now-playing-artist-title">
              {currentTrack.artist || 'هنرمند ناشناس'}
            </p>
          </div>
          <button
            className={`favorite-btn ${liked ? 'is-liked' : ''}`}
            onClick={() => toggleFavorite(currentTrack)}
            aria-label={liked ? 'حذف از علاقه‌مندی‌ها' : 'افزودن به علاقه‌مندی‌ها'}
          >
            <Icon name={liked ? 'heart-filled' : 'heart'} size={26} />
          </button>
        </div>

        {/* Seek Bar & Times */}
        <div className="seek-container">
          <div className="seek-slider-wrapper">
            <input
              type="range"
              min={0}
              max={duration || 100}
              step={1}
              value={isSeeking ? seekValue : position}
              onMouseDown={handleSeekStart}
              onTouchStart={handleSeekStart}
              onChange={handleSeekChange}
              onMouseUp={handleSeekEnd}
              onTouchEnd={handleSeekEnd}
              className="seek-slider"
              style={{
                background: `linear-gradient(to right, var(--color-primary) ${progressRatio * 100}%, rgba(255,255,255,0.15) ${progressRatio * 100}%)`,
              }}
            />
          </div>
          <div className="seek-time-row">
            <span className="time-text">
              {formatTime(isSeeking ? seekValue : position)}
            </span>
            <span className="time-text">
              {formatTime(duration)}
            </span>
          </div>
        </div>

        {/* Playback Controls */}
        <div className="playback-controls">
          {/* Shuffle */}
          <button
            className={`control-btn secondary-btn ${shuffle ? 'active' : ''}`}
            onClick={toggleShuffle}
            aria-label="پخش تصادفی"
            title="پخش تصادفی"
          >
            <Icon name="shuffle" size={20} />
          </button>

          {/* Previous */}
          <button
            className="control-btn primary-step-btn"
            onClick={previous}
            aria-label="آهنگ قبلی"
            title="آهنگ قبلی"
          >
            <Icon name="skip-back" size={26} />
          </button>

          {/* Play / Pause Main */}
          <button
            className="main-play-btn"
            onClick={togglePlayPause}
            aria-label={isPlaying ? 'توقف' : 'پخش'}
            disabled={isBuffering}
          >
            {isBuffering ? (
              <div className="btn-spinner" />
            ) : (
              <Icon name={isPlaying ? 'pause' : 'play'} size={32} />
            )}
          </button>

          {/* Next */}
          <button
            className="control-btn primary-step-btn"
            onClick={next}
            aria-label="آهنگ بعدی"
            title="آهنگ بعدی"
          >
            <Icon name="skip-forward" size={26} />
          </button>

          {/* Repeat */}
          <button
            className={`control-btn secondary-btn ${repeatMode > 0 ? 'active' : ''}`}
            onClick={cycleRepeat}
            aria-label="تکرار"
            title="تکرار"
          >
            <Icon name={repeatMode === 2 ? 'repeat-one' : 'repeat'} size={20} />
            {repeatMode === 2 && <span className="repeat-badge">1</span>}
          </button>
        </div>

        {/* Volume & Audio Info Row */}
        <div className="volume-info-row">
          <button
            className="vol-icon-btn"
            onClick={() => setVolume(volume > 0 ? 0 : 0.8)}
            aria-label="صدا"
          >
            <Icon name="volume" size={18} />
          </button>
          <input
            type="range"
            min={0}
            max={1}
            step={0.02}
            value={volume}
            onChange={(e) => setVolume(Number(e.target.value))}
            className="volume-slider"
            style={{
              background: `linear-gradient(to right, var(--color-primary) ${volume * 100}%, rgba(255,255,255,0.15) ${volume * 100}%)`,
            }}
          />
          <span className="audio-badge">320K HQ</span>
        </div>
      </div>

      {/* Queue Drawer / Overlay */}
      {showQueue && (
        <div className="queue-drawer-backdrop" onClick={() => setShowQueue(false)}>
          <div
            className="queue-drawer"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="queue-drawer-header">
              <div className="queue-drag-handle" />
              <div className="queue-header-row">
                <h3>{t('player.queue')} ({queue.length})</h3>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <button
                    type="button"
                    className="btn-outline"
                    style={{ padding: '5px 10px', fontSize: 12, display: 'inline-flex', alignItems: 'center', gap: 4 }}
                    onClick={() => {
                      setQueueSearch('');
                      setShowAddToQueue(true);
                    }}
                  >
                    <Icon name="plus" size={14} />
                    <span>{isFa ? 'افزودن آهنگ' : 'Add Song'}</span>
                  </button>
                  <button
                    className="icon-btn-circle small"
                    onClick={() => setShowQueue(false)}
                  >
                    <Icon name="close" size={18} />
                  </button>
                </div>
              </div>
            </div>

            <div className="queue-list">
              {queue.map((track, idx) => {
                const isCurrent = idx === queueIndex;
                return (
                  <div
                    key={`${track.id}-${idx}`}
                    className={`queue-item ${isCurrent ? 'active' : ''}`}
                    onClick={() => playQueueItem(idx)}
                  >
                    <div className="queue-item-index">
                      {isCurrent ? (
                        <div className="playing-bars">
                          <span className="bar bar-1" />
                          <span className="bar bar-2" />
                          <span className="bar bar-3" />
                        </div>
                      ) : (
                        <span>{idx + 1}</span>
                      )}
                    </div>
                    <Artwork
                      url={track.artworkUrl}
                      alt={track.title}
                      className="queue-item-art"
                    />
                    <div className="queue-item-details">
                      <span className="queue-item-title">{track.title}</span>
                      <span className="queue-item-artist">{track.artist || '—'}</span>
                    </div>
                    <span className="queue-item-time">{formatTime(track.durationSeconds)}</span>
                    <button
                      type="button"
                      className="icon-btn-circle small"
                      title={isFa ? 'حذف از صف پخش' : 'Remove from queue'}
                      aria-label={isFa ? 'حذف از صف پخش' : 'Remove from queue'}
                      onClick={(e) => {
                        e.stopPropagation();
                        removeFromQueue(idx);
                      }}
                    >
                      <Icon name="close" size={15} />
                    </button>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* Add Song to Queue Modal */}
      {showAddToQueue && (
        <Modal
          open={showAddToQueue}
          onClose={() => setShowAddToQueue(false)}
          title={isFa ? 'افزودن آهنگ به صف پخش' : 'Add Song to Queue'}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <input
              type="text"
              className="form-input"
              placeholder={isFa ? 'جستجوی نام آهنگ یا هنرمند...' : 'Search track or artist...'}
              value={queueSearch}
              onChange={(e) => setQueueSearch(e.target.value)}
            />
            <div style={{ maxHeight: '50vh', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 6 }}>
              {catalogTracks.map((tr) => (
                <div
                  key={tr.id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: 10,
                    padding: '8px 10px',
                    borderRadius: 8,
                    background: 'rgba(255,255,255,0.04)',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                    <Artwork url={tr.artworkUrl} alt={tr.title} size={40} />
                    <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                      <span style={{ fontSize: 13, fontWeight: 600, color: '#fff', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {tr.title}
                      </span>
                      <span style={{ fontSize: 12, color: '#b3b3b3' }}>{tr.artist || '—'}</span>
                    </div>
                  </div>
                  <button
                    type="button"
                    className="btn-primary"
                    style={{ padding: '6px 12px', fontSize: 12, flexShrink: 0 }}
                    onClick={() => {
                      addToQueue(tr);
                      notify(isFa ? `«${tr.title}» به صف پخش اضافه شد` : `Added "${tr.title}" to queue`, 'success');
                    }}
                  >
                    {isFa ? '+ افزودن' : '+ Add'}
                  </button>
                </div>
              ))}
              {catalogTracks.length === 0 && (
                <p style={{ textAlign: 'center', color: '#b3b3b3', fontSize: 13 }}>
                  {isFa ? 'آهنگی یافت نشد.' : 'No tracks found.'}
                </p>
              )}
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
