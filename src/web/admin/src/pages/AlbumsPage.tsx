import { useEffect, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { AlbumSummary, AlbumWrite, Track } from '../api/types';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { useDebounced } from '../ui/useDebounced';
import { ConfirmDialog, Modal, useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  Field,
  IconButton,
  Input,
  LoadingState,
  PageHeader,
  Textarea,
  Toolbar,
} from '../ui/primitives';
import { Artwork } from '../ui/Artwork';
import { Pagination } from '../ui/Pagination';
import { TrackTable } from '../ui/TrackTable';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

interface AlbumForm {
  title: string;
  albumArtist: string;
  year: string;
  genre: string;
  description: string;
}

const EMPTY_FORM: AlbumForm = { title: '', albumArtist: '', year: '', genre: '', description: '' };

function toPayload(form: AlbumForm): AlbumWrite {
  const year = form.year.trim() ? Number(form.year) : undefined;
  return {
    title: form.title.trim(),
    albumArtist: form.albumArtist.trim(),
    year: Number.isFinite(year) ? year : undefined,
    genre: form.genre.trim(),
    description: form.description.trim(),
  };
}

export function AlbumsPage() {
  const { t, formatNumber } = useUi();
  const { isAdmin, canContribute } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [q, setQ] = useState('');
  const debouncedQ = useDebounced(q.trim(), 350);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const [detailId, setDetailId] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<AlbumSummary | null>(null);
  const [form, setForm] = useState<AlbumForm>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [loadingEdit, setLoadingEdit] = useState(false);
  const [deleting, setDeleting] = useState<AlbumSummary | null>(null);
  const [addTrackOpen, setAddTrackOpen] = useState(false);
  const [trackSearch, setTrackSearch] = useState('');
  const debouncedTrackSearch = useDebounced(trackSearch.trim(), 350);
  const [removingTrack, setRemovingTrack] = useState<Track | null>(null);

  useEffect(() => {
    setPage(1);
  }, [debouncedQ, pageSize]);

  const listQuery = useQuery({
    queryKey: ['albums', { q: debouncedQ, page, pageSize }],
    queryFn: () => api.albums.list({ q: debouncedQ || undefined, page, pageSize }),
    placeholderData: (previous) => previous,
  });

  const detailQuery = useQuery({
    queryKey: ['album', detailId],
    queryFn: () => api.albums.get(detailId!),
    enabled: Boolean(detailId),
  });

  const trackSearchQuery = useQuery({
    queryKey: ['tracks', 'album-add', debouncedTrackSearch],
    queryFn: () => api.tracks.list({ q: debouncedTrackSearch || undefined, pageSize: 20, sort: 'title' }),
    enabled: addTrackOpen,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['albums'] });
    void queryClient.invalidateQueries({ queryKey: ['album'] });
    void queryClient.invalidateQueries({ queryKey: ['tracks'] });
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const saveMutation = useMutation({
    mutationFn: async (payload: { id: string | null; body: AlbumWrite }) => {
      if (payload.id) await api.albums.update(payload.id, payload.body);
      else await api.albums.create(payload.body);
    },
    onSuccess: () => {
      invalidate();
      notify(editing ? t('albums.updated') : t('albums.created'), 'success');
      setFormOpen(false);
      setEditing(null);
      setForm(EMPTY_FORM);
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const deleteMutation = useMutation({
    mutationFn: (album: AlbumSummary) => api.albums.remove(album.id),
    onSuccess: () => {
      invalidate();
      notify(t('albums.deleted'), 'success');
      setDeleting(null);
      setDetailId(null);
    },
    onError: (error) => {
      notify(errorMessage(error, t('common.error')), 'error');
      setDeleting(null);
    },
  });

  const addTrackMutation = useMutation({
    mutationFn: ({ albumId, trackId }: { albumId: string; trackId: string }) => api.albums.addTrack(albumId, trackId),
    onSuccess: () => {
      invalidate();
      notify(t('albums.updated'), 'success');
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const removeTrackMutation = useMutation({
    mutationFn: ({ albumId, trackId }: { albumId: string; trackId: string }) =>
      api.albums.removeTrack(albumId, trackId),
    onSuccess: () => {
      invalidate();
      notify(t('albums.updated'), 'success');
      setRemovingTrack(null);
    },
    onError: (error) => {
      notify(errorMessage(error, t('common.error')), 'error');
      setRemovingTrack(null);
    },
  });

  const openCreate = () => {
    setEditing(null);
    setForm(EMPTY_FORM);
    setFormError(null);
    setFormOpen(true);
  };

  const openEdit = async (album: AlbumSummary) => {
    setEditing(album);
    setFormError(null);
    setForm({
      title: album.title,
      albumArtist: album.albumArtist,
      year: album.year === null ? '' : String(album.year),
      genre: album.genre ?? '',
      description: '',
    });
    setFormOpen(true);
    setLoadingEdit(true);
    try {
      const detail = await api.albums.get(album.id);
      setForm({
        title: detail.album.title,
        albumArtist: detail.album.albumArtist,
        year: detail.album.year === null ? '' : String(detail.album.year),
        genre: detail.album.genre ?? '',
        description: detail.album.description ?? '',
      });
    } catch (error) {
      notify(errorMessage(error, t('common.error')), 'error');
    } finally {
      setLoadingEdit(false);
    }
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!form.title.trim()) {
      setFormError(t('common.requiredField'));
      return;
    }
    saveMutation.mutate({ id: editing?.id ?? null, body: toPayload(form) });
  };

  const detail = detailQuery.data;
  const existingTrackIds = new Set(detail?.tracks.map((track) => track.id) ?? []);
  const addableTracks = (trackSearchQuery.data?.items ?? []).filter((track) => !existingTrackIds.has(track.id));

  return (
    <div className="page">
      <PageHeader
        title={t('albums.title')}
        subtitle={t('albums.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={listQuery.isFetching} onClick={() => void listQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            {canContribute ? (
              <Button variant="primary" icon="plus" onClick={openCreate}>
                {t('albums.create')}
              </Button>
            ) : null}
          </>
        }
      />

      <Toolbar>
        <div className="search-field">
          <Icon name="search" size={18} />
          <Input
            type="search"
            value={q}
            onChange={(event) => setQ(event.target.value)}
            placeholder={t('albums.search')}
            aria-label={t('common.search')}
          />
        </div>
        {q ? (
          <Button variant="ghost" size="sm" onClick={() => setQ('')}>
            {t('common.clear')}
          </Button>
        ) : null}
      </Toolbar>

      <p className="muted small">{t('albums.artworkUnavailable')}</p>

      {listQuery.isLoading ? <LoadingState /> : null}
      {listQuery.isError ? (
        <ErrorState error={listQuery.error} onRetry={() => void listQuery.refetch()} />
      ) : null}

      {listQuery.data ? (
        listQuery.data.items.length === 0 ? (
          <EmptyState
            title={t('albums.empty')}
            body={t('albums.emptyBody')}
            icon="albums"
            action={
              canContribute ? (
                <Button variant="primary" icon="plus" onClick={openCreate}>
                  {t('albums.create')}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <div className="card-grid">
              {listQuery.data.items.map((album) => (
                <article key={album.id} className="album-card">
                  <button type="button" className="album-card-main" onClick={() => setDetailId(album.id)}>
                    <Artwork url={null} alt="" size={64} />
                    <span className="album-card-text">
                      <span className="album-card-title">{album.title}</span>
                      <span className="album-card-sub">{album.albumArtist || '—'}</span>
                      <span className="album-card-meta">
                        {album.year ?? '—'} · {t('albums.trackCount', { count: formatNumber(album.trackCount) })}
                      </span>
                    </span>
                  </button>
                  <div className="album-card-actions">
                    {canContribute ? (
                      <IconButton icon="edit" label={t('common.edit')} onClick={() => void openEdit(album)} />
                    ) : null}
                    {isAdmin ? (
                      <IconButton icon="trash" label={t('common.delete')} onClick={() => setDeleting(album)} />
                    ) : null}
                  </div>
                </article>
              ))}
            </div>
            <Pagination
              page={page}
              pageSize={pageSize}
              total={listQuery.data.total}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size);
                setPage(1);
              }}
            />
          </>
        )
      ) : null}

      <Modal open={formOpen} title={editing ? t('albums.edit') : t('albums.create')} onClose={() => setFormOpen(false)} footer={
        <>
          <Button onClick={() => setFormOpen(false)}>{t('common.cancel')}</Button>
          <Button
            type="submit"
            form="album-form"
            variant="primary"
            loading={saveMutation.isPending || loadingEdit}
            disabled={loadingEdit}
          >
            {saveMutation.isPending ? t('common.saving') : t('common.save')}
          </Button>
        </>
      }>
        {loadingEdit ? <LoadingState /> : null}
        {formError ? (
          <div className="notice notice-error" role="alert">
            {formError}
          </div>
        ) : null}
        <form id="album-form" className="form" onSubmit={submit} noValidate>
          <Field label={t('tracks.titleColumn')} htmlFor="album-title" required>
            <Input
              id="album-title"
              value={form.title}
              onChange={(event) => setForm((current) => ({ ...current, title: event.target.value }))}
              required
            />
          </Field>
          <Field label={t('albums.albumArtist')} htmlFor="album-artist">
            <Input
              id="album-artist"
              value={form.albumArtist}
              onChange={(event) => setForm((current) => ({ ...current, albumArtist: event.target.value }))}
            />
          </Field>
          <div className="form-grid two">
            <Field label={t('albums.year')} htmlFor="album-year" hint={t('albums.clearUnsupported')}>
              <Input
                id="album-year"
                type="number"
                inputMode="numeric"
                value={form.year}
                onChange={(event) => setForm((current) => ({ ...current, year: event.target.value }))}
              />
            </Field>
            <Field label={t('albums.genre')} htmlFor="album-genre">
              <Input
                id="album-genre"
                value={form.genre}
                onChange={(event) => setForm((current) => ({ ...current, genre: event.target.value }))}
              />
            </Field>
          </div>
          <Field label={t('albums.description')} htmlFor="album-description">
            <Textarea
              id="album-description"
              rows={3}
              value={form.description}
              onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
            />
          </Field>
        </form>
      </Modal>

      <Modal
        open={detailId !== null}
        title={detail?.album.title ?? t('albums.detailTitle')}
        onClose={() => setDetailId(null)}
        size="lg"
        footer={
          canContribute && detail ? (
            <Button variant="primary" icon="plus" onClick={() => setAddTrackOpen(true)}>
              {t('albums.addTrack')}
            </Button>
          ) : undefined
        }
      >
        {detailQuery.isLoading ? <LoadingState /> : null}
        {detailQuery.isError ? (
          <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
        ) : null}
        {detail ? (
          <div className="stack">
            <div className="detail-hero">
              <Artwork url={detail.album.artworkUrl} alt="" size={96} />
              <div>
                <h3>{detail.album.title}</h3>
                <p className="muted">
                  {detail.album.albumArtist || '—'} · {detail.album.year ?? '—'}
                </p>
                <div className="tag-row">
                  {detail.album.genre ? <Badge tone="info">{detail.album.genre}</Badge> : null}
                  <Badge>{t('albums.trackCount', { count: formatNumber(detail.tracks.length) })}</Badge>
                  <Badge>{detail.album.artistName ?? t('albums.noArtist')}</Badge>
                </div>
                {detail.album.description ? <p>{detail.album.description}</p> : null}
              </div>
            </div>
            <Card title={t('albums.tracks')}>
              <TrackTable
                tracks={detail.tracks}
                showAdded={false}
                emptyTitle={t('albums.noTracks')}
                actions={(track) =>
                  canContribute ? (
                    <IconButton icon="trash" label={t('albums.removeTrack')} onClick={() => setRemovingTrack(track)} />
                  ) : null
                }
              />
            </Card>
          </div>
        ) : null}
      </Modal>

      <Modal
        open={addTrackOpen}
        title={t('albums.addTrackTitle')}
        onClose={() => setAddTrackOpen(false)}
        size="lg"
      >
        <div className="search-field">
          <Icon name="search" size={18} />
          <Input
            type="search"
            value={trackSearch}
            onChange={(event) => setTrackSearch(event.target.value)}
            placeholder={t('albums.addTrackSearch')}
            aria-label={t('albums.addTrackSearch')}
          />
        </div>
        {trackSearchQuery.isLoading ? <LoadingState /> : null}
        {trackSearchQuery.isError ? (
          <ErrorState error={trackSearchQuery.error} onRetry={() => void trackSearchQuery.refetch()} />
        ) : null}
        {trackSearchQuery.data ? (
          addableTracks.length === 0 ? (
            <p className="muted">{t('common.noResults')}</p>
          ) : (
            <ul className="select-list">
              {addableTracks.map((track) => (
                <li key={track.id} className="select-row">
                  <Artwork url={track.artworkUrl} alt="" size={34} />
                  <div className="select-row-text">
                    <span className="table-title">{track.title}</span>
                    <span className="table-sub">{track.artist || '—'}</span>
                  </div>
                  <Button
                    size="sm"
                    loading={addTrackMutation.isPending}
                    onClick={() => {
                      if (!detailId) return;
                      addTrackMutation.mutate({ albumId: detailId, trackId: track.id });
                    }}
                  >
                    {t('common.create')}
                  </Button>
                </li>
              ))}
            </ul>
          )
        ) : null}
      </Modal>

      <ConfirmDialog
        open={deleting !== null}
        title={t('albums.deleteTitle')}
        body={t('albums.deleteBody', { title: deleting?.title ?? '' })}
        confirmLabel={t('common.delete')}
        busy={deleteMutation.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          if (deleting) deleteMutation.mutate(deleting);
        }}
      />

      <ConfirmDialog
        open={removingTrack !== null}
        title={t('albums.removeTrack')}
        body={t('albums.removeConfirm', { title: removingTrack?.title ?? '' })}
        confirmLabel={t('common.delete')}
        busy={removeTrackMutation.isPending}
        onCancel={() => setRemovingTrack(null)}
        onConfirm={() => {
          if (removingTrack && detailId) {
            removeTrackMutation.mutate({ albumId: detailId, trackId: removingTrack.id });
          }
        }}
      />
    </div>
  );
}
