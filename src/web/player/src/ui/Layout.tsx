import { type ReactNode } from 'react';
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
  const { currentTrack, isPlaying, togglePlayPause, next, isBuffering } = usePlayer();
  const { t } = useUi();

  const isNowPlayingTab = activeTab === 'nowPlaying';

  return (
    <div className="player-app-shell">
      {/* Top Header - Hidden on Now Playing tab since Now Playing has its own immersive top bar */}
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

      {/* Main Content Area - Scrollable viewport with safe area padding */}
      <main className={`mobile-main-content ${isNowPlayingTab ? 'is-now-playing' : ''}`}>
        {children}
      </main>

      {/* Mini Player Bar - Visible on all tabs except 'nowPlaying' when a track is active */}
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
