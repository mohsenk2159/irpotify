import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { TrackListItem } from './HomePage';
import { Modal } from '../ui/Modal';
import { Icon } from '../ui/icons';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export function LibraryPage() {
  const { t, notify } = useUi();
  const queryClient = useQueryClient();
  const { playTrack, currentTrack, isPlaying, toggleFavorite, isFavorite } = usePlayer();

  const [selectedPlaylistId, setSelectedPlaylistId] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [newPlaylistName, setNewPlaylistName] = useState('');
  const [newPlaylistDesc, setNewPlaylistDesc] = useState('');
  const [showAddSongsModal, setShowAddSongsModal] = useState(false);
  const [songSearchQuery, setSongSearchQuery] = useState('');

  // Handle hardware / browser back button for playlist drilldown
  useEffect(() => {
    const handlePopState = (e: PopStateEvent) => {
      const state = e.state;
      if (!state || state.app !== 'irpotify') return;
      if (state.tab === 'library') {
        setSelectedPlaylistId(state.playlistId || null);
      }
    };

    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, []);

  const handleOpenPlaylist = (playlistId: string) => {
    setSelectedPlaylistId(playlistId);
    if (typeof window !== 'undefined') {
      window.history.pushState({ app: 'irpotify', tab: 'library', playlistId }, '');
    }
  };

  const handleBackToPlaylists = () => {
    if (typeof window !== 'undefined' && window.history.length > 1) {
      window.history.back();
    } else {
      setSelectedPlaylistId(null);
    }
  };

  // Playlists query
  const playlistsQuery = useQuery({
    queryKey: ['playlists'],
    queryFn: api.playlists.list,
  });

  // All tracks query for Add Songs modal
  const tracksQuery = useQuery({
    queryKey: ['library', 'tracks'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 150 }),
  });

  // Selected Playlist detail query
  const playlistDetailQuery = useQuery({
    queryKey: ['playlist', selectedPlaylistId],
    queryFn: () => api.playlists.get(selectedPlaylistId!),
    enabled: selectedPlaylistId != null,
  });

  // Create playlist mutation
  const createMutation = useMutation({
    mutationFn: (payload: { name: string; description?: string }) =>
      api.playlists.create(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['playlists'] });
      setShowCreateModal(false);
      setNewPlaylistName('');
      setNewPlaylistDesc('');
      notify('لیست پخش با موفقیت ایجاد شد', 'success');
    },
  });

  // Delete playlist mutation
  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.playlists.remove(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['playlists'] });
      handleBackToPlaylists();
      notify('لیست پخش حذف شد', 'info');
    },
  });

  // Add song to playlist
  const addSongMutation = useMutation({
    mutationFn: (trackId: string) =>
      api.playlists.addTrack(selectedPlaylistId!, trackId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['playlist', selectedPlaylistId] });
      notify('آهنگ به لیست پخش اضافه شد', 'success');
    },
  });

  // Remove song from playlist
  const removeSongMutation = useMutation({
    mutationFn: (trackId: string) =>
      api.playlists.removeTrack(selectedPlaylistId!, trackId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['playlist', selectedPlaylistId] });
      notify('آهنگ از لیست پخش حذف شد', 'info');
    },
  });

  const playlists = playlistsQuery.data ?? [];
  const allTracks = tracksQuery.data?.items ?? [];

  /* ------------------------------------------------------------------ */
  /* SUBVIEW: Playlist Detail View                                      */
  /* ------------------------------------------------------------------ */
  if (selectedPlaylistId && playlistDetailQuery.data) {
    const playlist = playlistDetailQuery.data.playlist;
    const pTracks = playlistDetailQuery.data.tracks ?? [];

    return (
      <div className="page-container">
        <button
          type="button"
          className="btn-back"
          onClick={handleBackToPlaylists}
        >
          ← بازگشت به لیست‌ها
        </button>

        <div className="detail-hero" style={{ marginTop: 14 }}>
          <div className="playlist-cover-placeholder">
            <ThreeDIcon type="cassette" size={60} />
          </div>
          <div className="detail-meta">
            <span className="detail-tag">لیست پخش خصوصی</span>
            <h2>{playlist.name}</h2>
            <p>{playlist.description || 'بدون توضیحات'}</p>
            <p className="detail-count">{pTracks.length} قطعه</p>
          </div>
        </div>

        <div className="detail-actions-row">
          {pTracks.length > 0 && (
            <button
              type="button"
              className="btn-play-all"
              onClick={() => void playTrack(pTracks[0]!, pTracks)}
            >
              ▶ {t('library.playAll')}
            </button>
          )}
          <button
            type="button"
            className="btn-secondary"
            onClick={() => setShowAddSongsModal(true)}
          >
            + {t('library.addSongs')}
          </button>
          <button
            type="button"
            className="btn-danger-outline"
            onClick={() => {
              if (window.confirm('آیا از حذف این لیست پخش اطمینان دارید؟')) {
                deleteMutation.mutate(playlist.id);
              }
            }}
          >
            {t('action.delete')}
          </button>
        </div>

        {pTracks.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '40px 16px', color: '#94a3b8' }}>
            <p style={{ margin: 0, fontSize: 14 }}>این لیست پخش هنوز خالی است.</p>
            <button
              type="button"
              className="btn-secondary"
              style={{ marginTop: 12 }}
              onClick={() => setShowAddSongsModal(true)}
            >
              + افزودن اولین آهنگ
            </button>
          </div>
        ) : (
          <div className="vertical-track-list" style={{ marginTop: 16 }}>
            {pTracks.map((tr) => (
              <div key={tr.id} className="playlist-track-row-wrap">
                <TrackListItem
                  track={tr}
                  isCurrent={currentTrack?.id === tr.id}
                  isPlaying={currentTrack?.id === tr.id && isPlaying}
                  isFav={isFavorite(tr.id)}
                  onPlay={() => void playTrack(tr, pTracks)}
                  onToggleFav={() => void toggleFavorite(tr)}
                />
                <button
                  type="button"
                  className="btn-remove-track"
                  onClick={() => removeSongMutation.mutate(tr.id)}
                  title="حذف از لیست"
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        )}

        {/* Modal: Add Songs with Real-time Search */}
        <Modal
          open={showAddSongsModal}
          title={t('library.addSongs')}
          onClose={() => {
            setShowAddSongsModal(false);
            setSongSearchQuery('');
          }}
        >
          <div style={{ marginBottom: 14 }}>
            <div style={{ position: 'relative', display: 'flex', alignItems: 'center' }}>
              <input
                type="text"
                value={songSearchQuery}
                onChange={(e) => setSongSearchQuery(e.target.value)}
                placeholder="جستجوی نام آهنگ، خواننده یا آلبوم..."
                className="form-input"
                style={{
                  paddingLeft: 38,
                  paddingRight: 14,
                  fontSize: 14,
                  borderRadius: 10,
                  background: 'var(--color-surface, #1e293b)',
                  border: '1px solid var(--color-border, #334155)',
                  color: '#fff',
                  width: '100%',
                  height: 42,
                }}
                autoFocus
              />
              <div style={{ position: 'absolute', left: 12, pointerEvents: 'none', color: '#94a3b8' }}>
                <Icon name="search" size={18} />
              </div>
              {songSearchQuery && (
                <button
                  type="button"
                  onClick={() => setSongSearchQuery('')}
                  style={{
                    position: 'absolute',
                    right: 12,
                    background: 'none',
                    border: 'none',
                    color: '#94a3b8',
                    cursor: 'pointer',
                    fontSize: 16,
                  }}
                >
                  ✕
                </button>
              )}
            </div>
          </div>

          <div className="vertical-track-list" style={{ maxHeight: '55vh', overflowY: 'auto' }}>
            {(() => {
              const q = songSearchQuery.toLowerCase().trim();
              const filtered = allTracks.filter((tr) => {
                if (!q) return true;
                return (
                  tr.title?.toLowerCase().includes(q) ||
                  tr.artist?.toLowerCase().includes(q) ||
                  tr.album?.toLowerCase().includes(q)
                );
              });

              if (filtered.length === 0) {
                return (
                  <div style={{ padding: '24px 0', textAlign: 'center', color: '#94a3b8' }}>
                    <p style={{ margin: 0, fontSize: 14 }}>هیچ آهنگی مطابق جستجو یافت نشد.</p>
                  </div>
                );
              }

              return filtered.map((tr) => {
                const alreadyIn = pTracks.some((pt) => pt.id === tr.id);
                return (
                  <div key={tr.id} className="add-song-item">
                    <div className="add-song-text">
                      <span className="add-song-title">{tr.title}</span>
                      <span className="add-song-artist">
                        {tr.artist || '—'} {tr.album ? `• ${tr.album}` : ''}
                      </span>
                    </div>
                    <button
                      type="button"
                      className={`btn-add-circle${alreadyIn ? ' is-added' : ''}`}
                      disabled={alreadyIn}
                      onClick={() => addSongMutation.mutate(tr.id)}
                      title={alreadyIn ? 'قبلاً اضافه شده' : 'افزودن به لیست'}
                    >
                      {alreadyIn ? '✓' : '+'}
                    </button>
                  </div>
                );
              });
            })()}
          </div>
        </Modal>
      </div>
    );
  }

  /* ------------------------------------------------------------------ */
  /* ROOT VIEW: User's Playlists Exclusively (No tabs at the top)       */
  /* ------------------------------------------------------------------ */
  return (
    <div className="page-container">
      {/* Top Header */}
      <div className="library-header-row">
        <div>
          <h2>لیست‌های پخش شما</h2>
          <span style={{ fontSize: 13, color: '#94a3b8' }}>
            {playlists.length} لیست پخش اختصاصی
          </span>
        </div>
        <button
          type="button"
          className="btn-add-playlist"
          onClick={() => setShowCreateModal(true)}
          title={t('library.newPlaylist')}
        >
          + {t('library.newPlaylist')}
        </button>
      </div>

      {/* Playlist List or Empty State */}
      {playlists.length === 0 ? (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            textAlign: 'center',
            padding: '48px 20px',
            gap: 14,
          }}
        >
          <ThreeDIcon type="cassette" size={72} />
          <h3 style={{ margin: 0, fontSize: 18, color: '#fff', fontWeight: 700 }}>
            هنوز لیست پخشی ایجاد نکرده‌اید
          </h3>
          <p style={{ margin: 0, fontSize: 13, color: '#94a3b8', maxWidth: 320 }}>
            این بخش منحصراً برای لیست‌های پخش خصوصی شماست. اولین لیست پخش خود را بسازید و آهنگ‌های دلخواهتان را به آن اضافه کنید.
          </p>
          <button
            type="button"
            className="btn-primary"
            style={{ marginTop: 8 }}
            onClick={() => setShowCreateModal(true)}
          >
            + {t('library.newPlaylist')}
          </button>
        </div>
      ) : (
        <div className="library-list-col" style={{ marginTop: 14 }}>
          {playlists.map((pl) => (
            <div
              key={pl.id}
              className="playlist-item-row"
              onClick={() => handleOpenPlaylist(pl.id)}
            >
              <div className="playlist-icon-badge">
                <ThreeDIcon type="cassette" size={28} />
              </div>
              <div className="playlist-item-meta">
                <h4>{pl.name}</h4>
                <p>
                  {pl.trackCount} قطعه {pl.description ? `• ${pl.description}` : ''}
                </p>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Modal: Create Playlist */}
      <Modal
        open={showCreateModal}
        title={t('library.createPlaylist')}
        onClose={() => setShowCreateModal(false)}
      >
        <div className="modal-form">
          <label className="form-label">{t('library.playlistName')}</label>
          <input
            type="text"
            className="form-input"
            value={newPlaylistName}
            onChange={(e) => setNewPlaylistName(e.target.value)}
            placeholder="مثال: آهنگ‌های آرامش‌بخش"
            autoFocus
          />
          <label className="form-label" style={{ marginTop: 12 }}>
            {t('library.playlistDesc')}
          </label>
          <input
            type="text"
            className="form-input"
            value={newPlaylistDesc}
            onChange={(e) => setNewPlaylistDesc(e.target.value)}
            placeholder="توضیح کوتاه درباره لیست (اختیاری)"
          />
          <div className="modal-actions-row">
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setShowCreateModal(false)}
            >
              {t('action.cancel')}
            </button>
            <button
              type="button"
              className="btn-primary"
              disabled={!newPlaylistName.trim() || createMutation.isPending}
              onClick={() =>
                createMutation.mutate({
                  name: newPlaylistName.trim(),
                  description: newPlaylistDesc.trim() || undefined,
                })
              }
            >
              {t('action.create')}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
export default LibraryPage;
