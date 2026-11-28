import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { Track, ArtistSummary } from '../api/types';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { Artwork } from '../ui/Artwork';
import { Icon } from '../ui/icons';
import { Modal } from '../ui/Modal';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export type HomeTab = 'all' | 'tracks' | 'artists' | 'albums' | 'favorites';

interface DrillState {
  artistId: string | null;
  artistName: string | null;
  albumId: string | null;
  albumTitle: string | null;
}

function getGreeting(isFa: boolean): string {
  const hour = new Date().getHours();
  if (hour >= 5 && hour < 12) return isFa ? 'صبح بخیر' : 'Good morning';
  if (hour >= 12 && hour < 17) return isFa ? 'عصر بخیر' : 'Good afternoon';
  if (hour >= 17 && hour < 24) return isFa ? 'شب بخیر' : 'Good evening';
  return isFa ? 'بامداد بخیر' : 'Good night';
}

export function HomePage({
  onOpenAlbum: _onOpenAlbum,
}: {
  onOpenAlbum?: (albumId: string) => void;
}) {
  const { playTrack, currentTrack, isPlaying, toggleFavorite, isFavorite } = usePlayer();
  const { t, language, notify } = useUi();
  const isFa = language === 'fa';
  const queryClient = useQueryClient();

  const [activeTab, setActiveTab] = useState<HomeTab>('all');
  const [drill, setDrill] = useState<DrillState>({
    artistId: null,
    artistName: null,
    albumId: null,
    albumTitle: null,
  });

  // Track being selected to add to a personal playlist
  const [playlistModalTrack, setPlaylistModalTrack] = useState<Track | null>(null);

  // Sync with browser/Android hardware back button
  useEffect(() => {
    const handlePopState = (e: PopStateEvent) => {
      const state = e.state;
      if (!state || state.app !== 'irpotify') return;
      if (state.tab === 'home' || !state.tab) {
        setDrill({
          artistId: state.artistId || null,
          artistName: state.artistName || null,
          albumId: state.albumId || null,
          albumTitle: state.albumTitle || null,
        });
        if (state.homeTab) {
          setActiveTab(state.homeTab);
        }
      }
    };

    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, []);

  const handleTabChange = (tab: HomeTab) => {
    setActiveTab(tab);
    setDrill({ artistId: null, artistName: null, albumId: null, albumTitle: null });
    if (typeof window !== 'undefined') {
      window.history.pushState({ app: 'irpotify', tab: 'home', homeTab: tab, artistId: null, albumId: null }, '');
    }
  };

  const handleOpenArtist = (artist: ArtistSummary) => {
    const nextDrill: DrillState = {
      artistId: artist.id,
      artistName: artist.name,
      albumId: null,
      albumTitle: null,
    };
    setDrill(nextDrill);
    if (typeof window !== 'undefined') {
      window.history.pushState(
        { app: 'irpotify', tab: 'home', homeTab: activeTab, artistId: artist.id, artistName: artist.name, albumId: null },
        '',
      );
    }
  };

  const handleOpenAlbumInHome = (albumId: string, albumTitle?: string) => {
    const nextDrill: DrillState = {
      artistId: drill.artistId,
      artistName: drill.artistName,
      albumId,
      albumTitle: albumTitle || null,
    };
    setDrill(nextDrill);
    if (typeof window !== 'undefined') {
      window.history.pushState(
        {
          app: 'irpotify',
          tab: 'home',
          homeTab: activeTab,
          artistId: drill.artistId,
          artistName: drill.artistName,
          albumId,
          albumTitle: albumTitle || null,
        },
        '',
      );
    }
  };

  const handleBack = () => {
    if (typeof window !== 'undefined' && window.history.length > 1) {
      window.history.back();
    } else {
      if (drill.albumId && drill.artistId) {
        setDrill((prev) => ({ ...prev, albumId: null, albumTitle: null }));
      } else {
        setDrill({ artistId: null, artistName: null, albumId: null, albumTitle: null });
      }
    }
  };

  // Queries
  const recentTracksQuery = useQuery({
    queryKey: ['tracks', 'recent'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 30, sort: 'newest' }),
  });

  const popularTracksQuery = useQuery({
    queryKey: ['tracks', 'popular'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 20 }),
  });

  const allTracksQuery = useQuery({
    queryKey: ['tracks', 'all-home'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 150 }),
  });

  const albumsQuery = useQuery({
    queryKey: ['albums', 'home'],
    queryFn: () => api.albums.list({ page: 1, pageSize: 40 }),
  });

  const artistsQuery = useQuery({
    queryKey: ['artists', 'home'],
    queryFn: () => api.artists.list({ pageSize: 100 }),
  });

  const playlistsQuery = useQuery({
    queryKey: ['playlists'],
    queryFn: api.playlists.list,
  });

  // Artist Detail Query (when an artist is selected)
  const artistDetailQuery = useQuery({
    queryKey: ['artist', drill.artistId],
    queryFn: () => api.artists.get(drill.artistId!),
    enabled: Boolean(drill.artistId),
  });

  // Album Detail Query (when an album is selected)
  const albumDetailQuery = useQuery({
    queryKey: ['album', drill.albumId],
    queryFn: () => api.albums.get(drill.albumId!),
    enabled: Boolean(drill.albumId),
  });

  // Add track to personal playlist mutation
  const addToPlaylistMutation = useMutation({
    mutationFn: ({ playlistId, trackId }: { playlistId: string; trackId: string; playlistName: string }) =>
      api.playlists.addTrack(playlistId, trackId),
    onSuccess: (_, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['playlist', variables.playlistId] });
      void queryClient.invalidateQueries({ queryKey: ['playlists'] });
      notify(`آهنگ با موفقیت به «${variables.playlistName}» اضافه شد`, 'success');
      setPlaylistModalTrack(null);
    },
    onError: () => {
      notify('خطا در افزودن آهنگ به لیست پخش', 'error');
    },
  });

  const recentTracks = recentTracksQuery.data?.items ?? [];
  const popularTracks = popularTracksQuery.data?.items ?? [];
  const allTracks = allTracksQuery.data?.items ?? [];
  const albums = albumsQuery.data?.items ?? [];
  const artists = artistsQuery.data?.items ?? [];
  const userPlaylists = playlistsQuery.data ?? [];
  const favoriteTracks = allTracks.filter((tr) => isFavorite(tr.id));

  // Tracks without artist (unassigned / orphan)
  const orphanTracks = allTracks.filter(
    (tr) => !tr.artist || !tr.artist.trim() || tr.artist.toLowerCase() === 'unknown' || tr.artist === 'ناشناس',
  );

  const quickTracks = recentTracks.slice(0, 5);

  /* ------------------------------------------------------------------ */
  /* SUBVIEW 1: Album Detail View (in-place in Home)                     */
  /* ------------------------------------------------------------------ */
  if (drill.albumId && albumDetailQuery.data) {
    const album = albumDetailQuery.data.album;
    const albumTracks = albumDetailQuery.data.tracks ?? [];

    return (
      <div className="spotify-home-container">
        <div className="inhome-detail-view">
          <div className="inhome-back-bar">
            <button type="button" className="btn-back" onClick={handleBack}>
              ← بازگشت
            </button>
          </div>

          <div className="inhome-hero">
            <div className="inhome-hero-art">
              <Artwork url={album.artworkUrl} size={120} />
            </div>
            <div className="inhome-hero-meta">
              <span className="inhome-hero-tag">آلبوم</span>
              <h2 className="inhome-hero-title">{album.title}</h2>
              <p className="inhome-hero-sub">
                {album.albumArtist || album.artistName || '—'}
                {album.year ? ` • سال ${album.year}` : ''} • {albumTracks.length} قطعه
              </p>
            </div>
          </div>

          {albumTracks.length > 0 && (
            <div className="inhome-actions-bar">
              <button
                type="button"
                className="btn-play-all"
                onClick={() => void playTrack(albumTracks[0]!, albumTracks)}
              >
                ▶ پخش همه آهنگ‌ها
              </button>
            </div>
          )}

          <div className="spotify-track-list" style={{ marginTop: 8 }}>
            {albumTracks.map((track, idx) => (
              <TrackListItem
                key={track.id}
                track={track}
                index={idx + 1}
                isCurrent={currentTrack?.id === track.id}
                isPlaying={currentTrack?.id === track.id && isPlaying}
                isFav={isFavorite(track.id)}
                onPlay={() => void playTrack(track, albumTracks)}
                onToggleFav={() => void toggleFavorite(track)}
                onAddToPlaylist={() => setPlaylistModalTrack(track)}
              />
            ))}
          </div>
        </div>

        {/* Modal for adding to personal playlist */}
        {renderPlaylistPickerModal()}
      </div>
    );
  }

  /* ------------------------------------------------------------------ */
  /* SUBVIEW 2: Artist Detail View (in-place in Home)                    */
  /* ------------------------------------------------------------------ */
  if (drill.artistId && artistDetailQuery.data) {
    const artist = artistDetailQuery.data.artist;
    const artistAlbums = artistDetailQuery.data.albums ?? [];
    const artistTracks = artistDetailQuery.data.tracks ?? [];

    return (
      <div className="spotify-home-container">
        <div className="inhome-detail-view">
          <div className="inhome-back-bar">
            <button type="button" className="btn-back" onClick={handleBack}>
              ← بازگشت به خواننده‌ها
            </button>
          </div>

          <div className="inhome-hero">
            <div className="inhome-hero-art is-circle">
              <Artwork url={artist.artworkUrl} size={120} />
            </div>
            <div className="inhome-hero-meta">
              <span className="inhome-hero-tag">خواننده / هنرمند</span>
              <h2 className="inhome-hero-title">{artist.name}</h2>
              <p className="inhome-hero-sub">
                {artistAlbums.length} آلبوم {artistTracks.length > 0 ? `• ${artistTracks.length} قطعه` : ''}
              </p>
              {artist.biography && (
                <p style={{ fontSize: 13, color: '#cbd5e1', marginTop: 4, lineHeight: 1.4 }}>
                  {artist.biography}
                </p>
              )}
            </div>
          </div>

          {/* Albums of this Artist */}
          <section className="spotify-section">
            <div className="spotify-section-header">
              <h3 className="spotify-section-title">آلبوم‌های این خواننده</h3>
            </div>
            {artistAlbums.length === 0 ? (
              <p style={{ color: '#94a3b8', fontSize: 13, margin: 0 }}>
                هیچ آلبومی برای این خواننده ثبت نشده است.
              </p>
            ) : (
              <div className="spotify-albums-grid">
                {artistAlbums.map((alb) => (
                  <div
                    key={alb.id}
                    className="spotify-grid-card"
                    onClick={() => handleOpenAlbumInHome(alb.id, alb.title)}
                  >
                    <Artwork url={null} size={150} />
                    <span className="spotify-card-title">{alb.title}</span>
                    <span className="spotify-card-subtitle">
                      {alb.year ? `سال ${alb.year}` : ''} • {alb.trackCount} قطعه
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* Tracks of this Artist */}
          {artistTracks.length > 0 && (
            <section className="spotify-section">
              <div className="spotify-section-header">
                <h3 className="spotify-section-title">قطعات این خواننده</h3>
              </div>
              <div className="spotify-track-list">
                {artistTracks.map((track, idx) => (
                  <TrackListItem
                    key={track.id}
                    track={track}
                    index={idx + 1}
                    isCurrent={currentTrack?.id === track.id}
                    isPlaying={currentTrack?.id === track.id && isPlaying}
                    isFav={isFavorite(track.id)}
                    onPlay={() => void playTrack(track, artistTracks)}
                    onToggleFav={() => void toggleFavorite(track)}
                    onAddToPlaylist={() => setPlaylistModalTrack(track)}
                  />
                ))}
              </div>
            </section>
          )}
        </div>

        {renderPlaylistPickerModal()}
      </div>
    );
  }

  /* ------------------------------------------------------------------ */
  /* ROOT HOME VIEW (Tabs: All, Tracks, Artists, Albums, Favorites)      */
  /* ------------------------------------------------------------------ */
  return (
    <div className="spotify-home-container">
      {/* Top Greeting Header */}
      <header className="spotify-greeting-header">
        <div className="spotify-greeting-row">
          <h1 className="spotify-greeting-title">{getGreeting(isFa)}</h1>
          <div className="spotify-status-indicator" title={isFa ? 'متصل به سرور' : 'Connected'}>
            <span className="spotify-status-dot"></span>
            <span className="spotify-status-text">{isFa ? 'آنلاین' : 'Online'}</span>
          </div>
        </div>

        {/* 5-Item Spotify Category Filter Pills */}
        <div className="spotify-filter-pills">
          <button
            type="button"
            className={`spotify-pill${activeTab === 'all' ? ' is-active' : ''}`}
            onClick={() => handleTabChange('all')}
          >
            {t('tab.all')}
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'tracks' ? ' is-active' : ''}`}
            onClick={() => handleTabChange('tracks')}
          >
            {t('tab.tracks')}
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'artists' ? ' is-active' : ''}`}
            onClick={() => handleTabChange('artists')}
          >
            {t('tab.artists')}
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'albums' ? ' is-active' : ''}`}
            onClick={() => handleTabChange('albums')}
          >
            {t('tab.albums')}
          </button>
          <button
            type="button"
            className={`spotify-pill${activeTab === 'favorites' ? ' is-active' : ''}`}
            onClick={() => handleTabChange('favorites')}
          >
            {t('tab.favorites')}
          </button>
        </div>
      </header>

      {/* TAB 1: ALL (Default Spotify Home) */}
      {activeTab === 'all' && (
        <div className="spotify-home-content">
          {/* Quick-Access 6-Grid */}
          <section className="spotify-section">
            <div className="spotify-quick-grid">
              <div
                className="spotify-quick-tile liked-tile"
                onClick={() => {
                  if (favoriteTracks.length > 0) {
                    void playTrack(favoriteTracks[0]!, favoriteTracks);
                  } else {
                    handleTabChange('favorites');
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

          {/* Recently Added Carousel */}
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

          {/* Featured Albums Carousel */}
          {albums.length > 0 && (
            <section className="spotify-section">
              <div className="spotify-section-header">
                <h2 className="spotify-section-title">آلبوم‌های منتخب</h2>
              </div>
              <div className="spotify-carousel">
                {albums.slice(0, 10).map((album) => (
                  <div
                    key={album.id}
                    className="spotify-card"
                    onClick={() => handleOpenAlbumInHome(album.id, album.title)}
                  >
                    <div className="spotify-card-art-wrap">
                      <Artwork url={album.artworkUrl} size={140} />
                      <button
                        type="button"
                        className="spotify-card-play-btn"
                        aria-label="مشاهده آلبوم"
                      >
                        ▶
                      </button>
                    </div>
                    <span className="spotify-card-title">{album.title}</span>
                    <span className="spotify-card-subtitle">{album.albumArtist || '—'}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Popular Tracks Section */}
          <section className="spotify-section">
            <div className="spotify-section-header">
              <h2 className="spotify-section-title">بیشترین پخش و پیشنهادی</h2>
            </div>
            <div className="spotify-track-list">
              {(popularTracks.length > 0 ? popularTracks : recentTracks).slice(0, 8).map((track, idx) => (
                <TrackListItem
                  key={track.id}
                  track={track}
                  index={idx + 1}
                  isCurrent={currentTrack?.id === track.id}
                  isPlaying={currentTrack?.id === track.id && isPlaying}
                  isFav={isFavorite(track.id)}
                  onPlay={() => void playTrack(track, recentTracks)}
                  onToggleFav={() => void toggleFavorite(track)}
                  onAddToPlaylist={() => setPlaylistModalTrack(track)}
                />
              ))}
            </div>
          </section>
        </div>
      )}

      {/* TAB 2: TRACKS */}
      {activeTab === 'tracks' && (
        <div className="spotify-track-list" style={{ marginTop: 14 }}>
          {allTracks.map((track, idx) => (
            <TrackListItem
              key={track.id}
              track={track}
              index={idx + 1}
              isCurrent={currentTrack?.id === track.id}
              isPlaying={currentTrack?.id === track.id && isPlaying}
              isFav={isFavorite(track.id)}
              onPlay={() => void playTrack(track, allTracks)}
              onToggleFav={() => void toggleFavorite(track)}
              onAddToPlaylist={() => setPlaylistModalTrack(track)}
            />
          ))}
        </div>
      )}

      {/* TAB 3: ARTISTS (NEW!) */}
      {activeTab === 'artists' && (
        <div style={{ marginTop: 14, display: 'flex', flexDirection: 'column', gap: 24 }}>
          <section className="spotify-section">
            <div className="spotify-section-header">
              <h2 className="spotify-section-title">تمام خواننده‌ها و هنرمندان</h2>
              <span style={{ fontSize: 13, color: '#94a3b8' }}>{artists.length} هنرمند</span>
            </div>

            {artists.length === 0 ? (
              <div className="spotify-empty-state">
                <ThreeDIcon type="mic" size={60} />
                <h3>هنرمندی یافت نشد</h3>
                <p>پس از بارگذاری فایل‌ها، اطلاعات خواننده‌ها به‌صورت خودکار در اینجا قرار می‌گیرد.</p>
              </div>
            ) : (
              <div className="spotify-artists-grid">
                {artists.map((artist) => (
                  <div
                    key={artist.id}
                    className="spotify-artist-card"
                    onClick={() => handleOpenArtist(artist)}
                  >
                    <div className="spotify-artist-avatar-wrap">
                      {artist.artworkUrl ? (
                        <img src={artist.artworkUrl} alt={artist.name} />
                      ) : (
                        <span style={{ fontSize: 28, fontWeight: 800, color: '#10b981' }}>
                          {artist.name.slice(0, 1).toUpperCase()}
                        </span>
                      )}
                    </div>
                    <span className="spotify-artist-title">{artist.name}</span>
                    <span className="spotify-artist-subtitle">
                      {artist.albumCount || 0} آلبوم • {artist.trackCount || 0} آهنگ
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* Miscellaneous / Tracks without Artist Section */}
          {orphanTracks.length > 0 && (
            <section className="spotify-section" style={{ borderTop: '1px solid rgba(255,255,255,0.08)', paddingTop: 20 }}>
              <div className="spotify-section-header">
                <div>
                  <h2 className="spotify-section-title">🎵 آهنگ‌های متفرقه / بدون خواننده</h2>
                  <p style={{ margin: '4px 0 0', fontSize: 12, color: '#94a3b8' }}>
                    قطعاتی که برچسب خواننده ندارند یا خواننده آن‌ها نامشخص است
                  </p>
                </div>
                <span style={{ fontSize: 13, color: '#94a3b8' }}>{orphanTracks.length} قطعه</span>
              </div>
              <div className="spotify-track-list">
                {orphanTracks.map((track, idx) => (
                  <TrackListItem
                    key={track.id}
                    track={track}
                    index={idx + 1}
                    isCurrent={currentTrack?.id === track.id}
                    isPlaying={currentTrack?.id === track.id && isPlaying}
                    isFav={isFavorite(track.id)}
                    onPlay={() => void playTrack(track, orphanTracks)}
                    onToggleFav={() => void toggleFavorite(track)}
                    onAddToPlaylist={() => setPlaylistModalTrack(track)}
                  />
                ))}
              </div>
            </section>
          )}
        </div>
      )}

      {/* TAB 4: ALBUMS */}
      {activeTab === 'albums' && (
        <div className="spotify-albums-grid" style={{ marginTop: 14 }}>
          {albums.map((album) => (
            <div
              key={album.id}
              className="spotify-grid-card"
              onClick={() => handleOpenAlbumInHome(album.id, album.title)}
            >
              <Artwork url={album.artworkUrl} size={150} />
              <span className="spotify-card-title">{album.title}</span>
              <span className="spotify-card-subtitle">{album.albumArtist || '—'}</span>
            </div>
          ))}
        </div>
      )}

      {/* TAB 5: FAVORITES */}
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
                  onAddToPlaylist={() => setPlaylistModalTrack(track)}
                />
              ))}
            </div>
          )}
        </div>
      )}

      {/* Modal for adding to personal playlist */}
      {renderPlaylistPickerModal()}
    </div>
  );

  function renderPlaylistPickerModal() {
    return (
      <Modal
        open={playlistModalTrack !== null}
        title="افزودن به لیست پخش"
        onClose={() => setPlaylistModalTrack(null)}
      >
        {playlistModalTrack && (
          <div>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                paddingBottom: 14,
                marginBottom: 14,
                borderBottom: '1px solid rgba(255,255,255,0.08)',
              }}
            >
              <Artwork url={playlistModalTrack.artworkUrl} size={48} />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontWeight: 700, fontSize: 14, color: '#fff', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  {playlistModalTrack.title}
                </div>
                <div style={{ fontSize: 12, color: '#94a3b8' }}>
                  {playlistModalTrack.artist || '—'}
                </div>
              </div>
            </div>

            <p style={{ fontSize: 13, color: '#cbd5e1', marginBottom: 10 }}>
              انتخاب لیست پخش شخصی برای افزودن این آهنگ:
            </p>

            {userPlaylists.length === 0 ? (
              <div style={{ textAlign: 'center', padding: '20px 0', color: '#94a3b8' }}>
                <p style={{ margin: 0, fontSize: 13 }}>هنوز لیست پخشی ایجاد نکرده‌اید.</p>
                <p style={{ margin: '4px 0 0', fontSize: 12 }}>از بخش «کتابخانه» یک لیست پخش جدید بسازید.</p>
              </div>
            ) : (
              <div className="playlist-select-list">
                {userPlaylists.map((pl) => (
                  <div
                    key={pl.id}
                    className="playlist-select-item"
                    onClick={() =>
                      addToPlaylistMutation.mutate({
                        playlistId: pl.id,
                        trackId: playlistModalTrack.id,
                        playlistName: pl.name,
                      })
                    }
                  >
                    <div className="playlist-select-info">
                      <span className="playlist-select-name">{pl.name}</span>
                      <span className="playlist-select-count">{pl.trackCount} قطعه</span>
                    </div>
                    <button
                      type="button"
                      className="btn-add-circle"
                      title="افزودن به این لیست"
                      style={{ pointerEvents: 'none' }}
                    >
                      +
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </Modal>
    );
  }
}

export function TrackListItem({
  track,
  isCurrent,
  isPlaying,
  isFav,
  onPlay,
  onToggleFav,
  onAddToPlaylist,
  index,
}: {
  track: Track;
  isCurrent: boolean;
  isPlaying: boolean;
  isFav: boolean;
  onPlay: () => void;
  onToggleFav?: () => void;
  onAddToPlaylist?: () => void;
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
        <span className="spotify-track-meta">
          {track.artist || '—'} {track.album ? `• ${track.album}` : ''}
        </span>
      </div>

      <div className="spotify-track-actions" onClick={(e) => e.stopPropagation()}>
        {onAddToPlaylist && (
          <button
            type="button"
            className="spotify-action-btn"
            onClick={onAddToPlaylist}
            title="افزودن به لیست پخش"
            aria-label="افزودن به لیست پخش"
          >
            <Icon name="list-music" size={17} />
          </button>
        )}
        {onToggleFav && (
          <button
            type="button"
            className={`spotify-action-btn${isFav ? ' is-fav' : ''}`}
            onClick={onToggleFav}
            title={isFav ? 'حذف از علاقه‌مندی‌ها' : 'افزودن به علاقه‌مندی‌ها'}
            aria-label="Favorite"
          >
            <Icon name={isFav ? 'heart-filled' : 'heart'} size={17} />
          </button>
        )}
        <button
          type="button"
          className="spotify-action-btn"
          onClick={onPlay}
          title={isCurrent && isPlaying ? 'توقف' : 'پخش'}
          aria-label={isCurrent && isPlaying ? 'توقف' : 'پخش'}
        >
          <Icon name={isCurrent && isPlaying ? 'pause' : 'play'} size={17} />
        </button>
      </div>
    </div>
  );
}
