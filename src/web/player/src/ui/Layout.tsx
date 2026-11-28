import { type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { Icon, BrandLogo } from './icons';
import { Artwork } from './Artwork';

export type PlayerTab = 'home' | 'search' | 'library' | 'nowPlaying' | 'settings';

interface LayoutProps {
  activeTab: PlayerTab;
  onSelectTab: (tab: PlayerTab) => void;
  children: ReactNode;
}

export function Layout({ activeTab, onSelectTab, children }: LayoutProps) {
  const {
    currentTrack,
    isPlaying,
    position,
    duration,
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
  } = usePlayer();
  const { t, language, formatTime } = useUi();
  const isFa = language === 'fa';

  const playlistsQuery = useQuery({
    queryKey: ['playlists'],
    queryFn: api.playlists.list,
  });
  const playlists = playlistsQuery.data ?? [];

  const isNowPlayingTab = activeTab === 'nowPlaying';
  const liked = currentTrack ? isFavorite(currentTrack.id) : false;

  return (
    <div className="player-app-shell spotify-shell">
      {/* Desktop Top Global Bar (Spotify 2026 Web Player style) */}
      <header className="spotify-desktop-topbar">
        <div className="spotify-topbar-brand" onClick={() => onSelectTab('home')}>
          <BrandLogo size={32} />
          <span className="app-brand-name">irpotify</span>
        </div>

        <div className="spotify-topbar-center">
          <button
            className={`spotify-home-circle ${activeTab === 'home' ? 'active' : ''}`}
            onClick={() => onSelectTab('home')}
            aria-label={t('nav.home')}
            title={t('nav.home')}
          >
            <Icon name="dashboard" size={20} />
          </button>
          <button
            className={`spotify-search-pill ${activeTab === 'search' ? 'active' : ''}`}
            onClick={() => onSelectTab('search')}
          >
            <Icon name="search" size={18} />
            <span>{isFa ? 'چه چیزی می‌خواهید بشنوید؟' : 'What do you want to play?'}</span>
          </button>
        </div>

        <div className="spotify-topbar-actions">
          <button
            className={`header-icon-btn ${activeTab === 'settings' ? 'active' : ''}`}
            onClick={() => onSelectTab('settings')}
            aria-label={t('nav.settings')}
            title={t('nav.settings')}
          >
            <Icon name="settings" size={20} />
          </button>
        </div>
      </header>

      {/* Mobile Top Header - Hidden on Desktop and Now Playing tab */}
      {!isNowPlayingTab && (
        <header className="mobile-app-header">
          <div className="app-header-left">
            <BrandLogo size={32} />
            <span className="app-brand-name">irpotify</span>
          </div>
          <div className="app-header-right">
            <button
              className="header-icon-btn"
              onClick={() => onSelectTab('search')}
              aria-label={t('nav.search')}
            >
              <Icon name="search" size={20} />
            </button>
            <button
              className="header-icon-btn"
              onClick={() => onSelectTab('settings')}
              aria-label={t('nav.settings')}
            >
              <Icon name="settings" size={20} />
            </button>
          </div>
        </header>
      )}

      {/* Desktop Body Wrapper (Left Library Sidebar + Main Content) */}
      <div className="spotify-workspace-body">
        <aside className="spotify-desktop-sidebar" aria-label="کتابخانه شما">
          <div className="spotify-sidebar-header">
            <button
              className="spotify-sidebar-title-btn"
              onClick={() => onSelectTab('library')}
            >
              <Icon name="playlists" size={22} />
              <span>{t('library.title')}</span>
            </button>
            <button
              className="spotify-sidebar-add-btn"
              onClick={() => onSelectTab('library')}
              title={isFa ? 'مدیریت و ساخت لیست پخش' : 'Create & manage playlists'}
              aria-label={t('library.createPlaylist')}
            >
              <Icon name="plus" size={18} />
            </button>
          </div>

          <div className="spotify-sidebar-list">
            <button
              className={`spotify-sidebar-item ${activeTab === 'home' ? 'active' : ''}`}
              onClick={() => onSelectTab('home')}
            >
              <div className="spotify-liked-badge">
                <Icon name="heart-filled" size={18} />
              </div>
              <div className="spotify-sidebar-item-meta">
                <span className="spotify-sidebar-item-title">{t('library.likedSongs')}</span>
                <span className="spotify-sidebar-item-sub">{isFa ? 'مجموعه شخصی' : 'Personal collection'}</span>
              </div>
            </button>

            {playlists.map((pl) => (
              <button
                key={pl.id}
                className="spotify-sidebar-item"
                onClick={() => onSelectTab('library')}
              >
                <div className="spotify-playlist-badge">
                  <Icon name="music" size={18} />
                </div>
                <div className="spotify-sidebar-item-meta">
                  <span className="spotify-sidebar-item-title">{pl.name}</span>
                  <span className="spotify-sidebar-item-sub">
                    {isFa ? `لیست پخش • ${pl.trackCount} آهنگ` : `Playlist • ${pl.trackCount} tracks`}
                  </span>
                </div>
              </button>
            ))}

            {playlists.length === 0 && (
              <div className="spotify-sidebar-empty">
                <p>{isFa ? 'اولین لیست پخش خود را بسازید' : 'Create your first playlist'}</p>
                <button
                  className="spotify-sidebar-cta"
                  onClick={() => onSelectTab('library')}
                >
                  {t('library.createPlaylist')}
                </button>
              </div>
            )}
          </div>
        </aside>

        {/* Main Content Area - Scrollable viewport */}
        <main className={`mobile-main-content ${isNowPlayingTab ? 'is-now-playing' : ''}`}>
          {children}
        </main>
      </div>

      {/* Desktop Persistent 3-Column Bottom Player Bar */}
      <footer className="spotify-desktop-playerbar" aria-label="نوار پخش">
        {/* Left: Current Track Info */}
        <div className="spotify-bar-left">
          {currentTrack ? (
            <>
              <div
                className="spotify-bar-art"
                onClick={() => onSelectTab('nowPlaying')}
                title="نمایش تمام‌صفحه"
              >
                <Artwork url={currentTrack.artworkUrl} alt={currentTrack.title} size={56} />
              </div>
              <div className="spotify-bar-meta">
                <span
                  className="spotify-bar-title"
                  onClick={() => onSelectTab('nowPlaying')}
                >
                  {currentTrack.title}
                </span>
                <span className="spotify-bar-artist">{currentTrack.artist || 'irpotify'}</span>
              </div>
              <button
                className={`spotify-bar-icon-btn ${liked ? 'liked' : ''}`}
                onClick={() => void toggleFavorite(currentTrack)}
                aria-label="پسندیدن"
              >
                <Icon name={liked ? 'heart-filled' : 'heart'} size={18} />
              </button>
            </>
          ) : (
            <div className="spotify-bar-placeholder">{t('player.noTrack')}</div>
          )}
        </div>

        {/* Center: Playback Controls & Seekbar */}
        <div className="spotify-bar-center">
          <div className="spotify-bar-controls">
            <button
              className={`spotify-bar-icon-btn ${shuffle ? 'active' : ''}`}
              onClick={toggleShuffle}
              disabled={!currentTrack}
              aria-label="پخش تصادفی"
              title="پخش تصادفی"
            >
              <Icon name="shuffle" size={16} />
            </button>
            <button
              className="spotify-bar-icon-btn"
              onClick={previous}
              disabled={!currentTrack}
              aria-label="قبلی"
              title="قبلی"
            >
              <Icon name="skip-back" size={18} />
            </button>
            <button
              className="spotify-bar-play-btn"
              onClick={togglePlayPause}
              disabled={!currentTrack || isBuffering}
              aria-label={isPlaying ? 'توقف' : 'پخش'}
            >
              {isBuffering ? (
                <div className="btn-spinner mini" />
              ) : (
                <Icon name={isPlaying ? 'pause' : 'play'} size={18} />
              )}
            </button>
            <button
              className="spotify-bar-icon-btn"
              onClick={next}
              disabled={!currentTrack}
              aria-label="بعدی"
              title="بعدی"
            >
              <Icon name="skip-forward" size={18} />
            </button>
            <button
              className={`spotify-bar-icon-btn ${repeatMode > 0 ? 'active' : ''}`}
              onClick={cycleRepeat}
              disabled={!currentTrack}
              aria-label="تکرار"
              title="تکرار"
            >
              <Icon name={repeatMode === 2 ? 'repeat-one' : 'repeat'} size={16} />
            </button>
          </div>

          <div className="spotify-bar-progress">
            <span className="spotify-bar-time">{formatTime(position)}</span>
            <input
              type="range"
              className="spotify-range-slider"
              min={0}
              max={duration || 100}
              value={Math.min(position, duration || 100)}
              disabled={!currentTrack}
              onChange={(e) => seekTo(Number(e.target.value))}
              aria-label="پیشرفت پخش"
            />
            <span className="spotify-bar-time">{formatTime(duration)}</span>
          </div>
        </div>

        {/* Right: Now Playing View & Volume */}
        <div className="spotify-bar-right">
          <button
            className={`spotify-bar-icon-btn ${isNowPlayingTab ? 'active' : ''}`}
            onClick={() => onSelectTab(isNowPlayingTab ? 'home' : 'nowPlaying')}
            aria-label="نمای در حال پخش"
            title="نمای در حال پخش و صف پخش"
          >
            <Icon name="list-music" size={18} />
          </button>
          <button
            className="spotify-bar-icon-btn"
            onClick={() => setVolume(volume > 0 ? 0 : 1)}
            aria-label="صدا"
            title="قطع / وصل صدا"
          >
            <Icon name="volume" size={18} />
          </button>
          <input
            type="range"
            className="spotify-range-slider volume"
            min={0}
            max={1}
            step={0.02}
            value={volume}
            onChange={(e) => setVolume(Number(e.target.value))}
            aria-label="تنظیم صدا"
          />
        </div>
      </footer>

      {/* Mobile Mini Player Bar - Hidden on Desktop */}
      {!isNowPlayingTab && currentTrack && (
        <aside
          className="floating-mini-player"
          onClick={() => onSelectTab('nowPlaying')}
          role="region"
          aria-label="پخش‌کننده شناور"
        >
          <div className="mini-player-artwork-box">
            <Artwork
              url={currentTrack.artworkUrl}
              alt={currentTrack.title}
              className="mini-player-art"
            />
          </div>
          <div className="mini-player-details">
            <span className="mini-player-title">{currentTrack.title}</span>
            <span className="mini-player-artist">{currentTrack.artist || 'irpotify'}</span>
          </div>
          <div className="mini-player-actions" onClick={(e) => e.stopPropagation()}>
            <button
              className="mini-action-btn"
              onClick={togglePlayPause}
              disabled={isBuffering}
              aria-label={isPlaying ? 'توقف' : 'پخش'}
            >
              {isBuffering ? (
                <div className="btn-spinner mini" />
              ) : (
                <Icon name={isPlaying ? 'pause' : 'play'} size={20} />
              )}
            </button>
            <button
              className="mini-action-btn"
              onClick={next}
              aria-label="آهنگ بعدی"
            >
              <Icon name="skip-forward" size={20} />
            </button>
          </div>
        </aside>
      )}

      {/* 5-Item Bottom Navigation Bar */}
      <nav className="mobile-bottom-nav" aria-label="ناوبری اصلی">
        {/* 1. Home */}
        <button
          className={`nav-tab-btn ${activeTab === 'home' ? 'active' : ''}`}
          onClick={() => onSelectTab('home')}
          aria-label={t('nav.home')}
        >
          <div className="nav-icon-wrapper">
            <Icon name="dashboard" size={22} />
          </div>
          <span className="nav-tab-label">{t('nav.home')}</span>
        </button>

        {/* 2. Search */}
        <button
          className={`nav-tab-btn ${activeTab === 'search' ? 'active' : ''}`}
          onClick={() => onSelectTab('search')}
          aria-label={t('nav.search')}
        >
          <div className="nav-icon-wrapper">
            <Icon name="search" size={22} />
          </div>
          <span className="nav-tab-label">{t('nav.search')}</span>
        </button>

        {/* 3. Library */}
        <button
          className={`nav-tab-btn ${activeTab === 'library' ? 'active' : ''}`}
          onClick={() => onSelectTab('library')}
          aria-label={t('nav.library')}
        >
          <div className="nav-icon-wrapper">
            <Icon name="playlists" size={22} />
          </div>
          <span className="nav-tab-label">{t('nav.library')}</span>
        </button>

        {/* 4. Now Playing */}
        <button
          className={`nav-tab-btn ${activeTab === 'nowPlaying' ? 'active' : ''}`}
          onClick={() => onSelectTab('nowPlaying')}
          aria-label={t('nav.nowPlaying')}
        >
          <div className="nav-icon-wrapper">
            <Icon name="music" size={22} />
          </div>
          <span className="nav-tab-label">{t('nav.nowPlaying')}</span>
        </button>

        {/* 5. Settings */}
        <button
          className={`nav-tab-btn ${activeTab === 'settings' ? 'active' : ''}`}
          onClick={() => onSelectTab('settings')}
          aria-label={t('nav.settings')}
        >
          <div className="nav-icon-wrapper">
            <Icon name="settings" size={22} />
          </div>
          <span className="nav-tab-label">{t('nav.settings')}</span>
        </button>
      </nav>
    </div>
  );
}
