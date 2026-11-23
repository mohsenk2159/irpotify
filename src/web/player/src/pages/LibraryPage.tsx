import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { usePlayer } from '../player/PlayerContext';
import { useUi } from '../ui/UiProvider';
import { TrackListItem } from './HomePage';
import { Artwork } from '../ui/Artwork';
import { Modal } from '../ui/Modal';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export function LibraryPage({
  openAlbumId,
  onCloseAlbum,
}: {
  openAlbumId?: string | null;
  onCloseAlbum?: () => void;
}) {
  const { t, notify } = useUi();
  const queryClient = useQueryClient();
  const { playTrack, currentTrack, isPlaying, toggleFavorite, isFavorite } = usePlayer();

  const [activeFilter, setActiveFilter] = useState<'playlists' | 'albums' | 'tracks'>('playlists');
  const [selectedPlaylistId, setSelectedPlaylistId] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [newPlaylistName, setNewPlaylistName] = useState('');
  const [newPlaylistDesc, setNewPlaylistDesc] = useState('');
  const [showAddSongsModal, setShowAddSongsModal] = useState(false);

  // Playlists query
  const playlistsQuery = useQuery({
    queryKey: ['playlists'],
    queryFn: api.playlists.list,
  });

  // Albums query
  const albumsQuery = useQuery({
    queryKey: ['albums'],
    queryFn: () => api.albums.list({ page: 1, pageSize: 30 }),
  });

  // All tracks query for library
  const tracksQuery = useQuery({
    queryKey: ['library', 'tracks'],
    queryFn: () => api.tracks.list({ page: 1, pageSize: 50 }),
  });

  // Selected Playlist detail
  const playlistDetailQuery = useQuery({
    queryKey: ['playlist', selectedPlaylistId],
    queryFn: () => api.playlists.get(selectedPlaylistId!),
    enabled: selectedPlaylistId != null,
  });

  // Selected Album detail
  const albumDetailQuery = useQuery({
    queryKey: ['album', openAlbumId],
    queryFn: () => api.albums.get(openAlbumId!),
    enabled: openAlbumId != null,
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
      setSelectedPlaylistId(null);
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
  const albums = albumsQuery.data?.items ?? [];
  const allTracks = tracksQuery.data?.items ?? [];

  // View: Album Detail View
  if (openAlbumId && albumDetailQuery.data) {
    const album = albumDetailQuery.data.album;
    const albumTracks = albumDetailQuery.data.tracks ?? [];
    return (
      <div className="page-container">
        <button type="button" className="btn-back" onClick={onCloseAlbum}>
          ← بازگشت
        </button>
        <div className="detail-hero">
          <Artwork url={album.artworkUrl} size={140} />
          <div className="detail-meta">
            <span className="detail-tag">آلبوم</span>
            <h2>{album.title}</h2>
            <p>{album.albumArtist || album.artistName || '—'}</p>
            <p className="detail-count">{albumTracks.length} قطعه</p>
          </div>
        </div>
        {albumTracks.length > 0 && (
          <button
            type="button"
            className="btn-play-all"
            onClick={() => void playTrack(albumTracks[0]!, albumTracks)}
          >
            ▶ {t('library.playAll')}
          </button>
        )}
        <div className="vertical-track-list" style={{ marginTop: 16 }}>
          {albumTracks.map((tr) => (
            <TrackListItem
              key={tr.id}
              track={tr}
              isCurrent={currentTrack?.id === tr.id}
              isPlaying={currentTrack?.id === tr.id && isPlaying}
              isFav={isFavorite(tr.id)}
              onPlay={() => void playTrack(tr, albumTracks)}
              onToggleFav={() => void toggleFavorite(tr)}
            />
          ))}
        </div>
      </div>
    );
  }

  // View: Playlist Detail View
  if (selectedPlaylistId && playlistDetailQuery.data) {
    const playlist = playlistDetailQuery.data.playlist;
    const pTracks = playlistDetailQuery.data.tracks ?? [];
    return (
      <div className="page-container">
        <button
          type="button"
          className="btn-back"
          onClick={() => setSelectedPlaylistId(null)}
        >
          ← بازگشت
        </button>
        <div className="detail-hero">
          <div className="playlist-cover-placeholder">
            <ThreeDIcon type="cassette" size={60} />
          </div>
          <div className="detail-meta">
            <span className="detail-tag">لیست پخش</span>
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

        {/* Modal: Add Songs */}
        <Modal
          open={showAddSongsModal}
          title={t('library.addSongs')}
          onClose={() => setShowAddSongsModal(false)}
        >
          <div className="vertical-track-list" style={{ maxHeight: '60vh', overflowY: 'auto' }}>
            {allTracks.map((tr) => {
              const alreadyIn = pTracks.some((pt) => pt.id === tr.id);
              return (
                <div key={tr.id} className="add-song-item">
                  <div className="add-song-text">
                    <span className="add-song-title">{tr.title}</span>
                    <span className="add-song-artist">{tr.artist || '—'}</span>
                  </div>
                  <button
                    type="button"
                    className={`btn-add-circle${alreadyIn ? ' is-added' : ''}`}
                    disabled={alreadyIn}
                    onClick={() => addSongMutation.mutate(tr.id)}
                  >
                    {alreadyIn ? '✓' : '+'}
                  </button>
                </div>
              );
            })}
          </div>
        </Modal>
      </div>
    );
  }

  // View: Main Library Overview
  return (
    <div className="page-container">
      {/* Top Header */}
      <div className="library-header-row">
        <h2>{t('library.title')}</h2>
        <button
          type="button"
          className="btn-add-playlist"
          onClick={() => setShowCreateModal(true)}
          title={t('library.newPlaylist')}
        >
          + {t('library.newPlaylist')}
        </button>
      </div>

      {/* Filter Tabs */}
      <div className="filter-chips-row">
        <button
          type="button"
          className={`filter-chip${activeFilter === 'playlists' ? ' is-active' : ''}`}
          onClick={() => setActiveFilter('playlists')}
        >
          {t('tab.playlists')}
        </button>
        <button
          type="button"
          className={`filter-chip${activeFilter === 'albums' ? ' is-active' : ''}`}
          onClick={() => setActiveFilter('albums')}
        >
          {t('tab.albums')}
        </button>
        <button
          type="button"
          className={`filter-chip${activeFilter === 'tracks' ? ' is-active' : ''}`}
          onClick={() => setActiveFilter('tracks')}
        >
          {t('tab.tracks')}
        </button>
      </div>

      {/* Filter: Playlists */}
      {activeFilter === 'playlists' && (
        <div className="library-list-col">
          {/* Liked Songs Purple Gradient Box */}
          <div
            className="liked-songs-card"
            onClick={() => setActiveFilter('tracks')}
          >
            <div className="liked-card-art">
              <ThreeDIcon type="heart" size={36} />
            </div>
            <div className="liked-card-meta">
              <h4>{t('library.likedSongs')}</h4>
              <p>مجموعه تمام آهنگ‌های برگزیده شما</p>
            </div>
          </div>

          {/* User Playlists */}
          {playlists.map((pl) => (
            <div
              key={pl.id}
              className="playlist-item-row"
              onClick={() => setSelectedPlaylistId(pl.id)}
            >
              <div className="playlist-icon-badge">
                <ThreeDIcon type="cassette" size={28} />
              </div>
              <div className="playlist-item-meta">
                <h4>{pl.name}</h4>
                <p>{pl.trackCount} قطعه</p>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Filter: Albums */}
      {activeFilter === 'albums' && (
        <div className="grid-2-col" style={{ marginTop: 12 }}>
          {albums.map((album) => (
            <div
              key={album.id}
              className="album-card-grid"
              onClick={() => setSelectedPlaylistId(album.id)}
            >
              <Artwork url={album.artworkUrl} size={150} />
              <span className="card-track-title">{album.title}</span>
              <span className="card-track-artist">{album.albumArtist || '—'}</span>
            </div>
          ))}
        </div>
      )}

      {/* Filter: Tracks */}
      {activeFilter === 'tracks' && (
        <div className="vertical-track-list" style={{ marginTop: 12 }}>
          {allTracks.map((tr) => (
            <TrackListItem
              key={tr.id}
              track={tr}
              isCurrent={currentTrack?.id === tr.id}
              isPlaying={currentTrack?.id === tr.id && isPlaying}
              isFav={isFavorite(tr.id)}
              onPlay={() => void playTrack(tr, allTracks)}
              onToggleFav={() => void toggleFavorite(tr)}
            />
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
            placeholder="توضیح کوتاه درباره لیست"
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
