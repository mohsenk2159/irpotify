import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/endpoints';
import type { Track, TrackQuery } from '../api/types';
import type { TranslationKey } from '../i18n/translations';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { useDebounced } from '../ui/useDebounced';
import { useToast } from '../ui/Modal';
import { ConfirmDialog, Modal } from '../ui/Modal';
import {
  Badge,
  Button,
  Checkbox,
  ErrorState,
  Field,
  IconButton,
  Input,
  LoadingState,
  PageHeader,
  Select,
  Toolbar,
} from '../ui/primitives';
import { Pagination } from '../ui/Pagination';
import { TrackTable } from '../ui/TrackTable';
import { Artwork } from '../ui/Artwork';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

const SORT_OPTIONS: { value: NonNullable<TrackQuery['sort']>; labelKey: TranslationKey }[] = [
  { value: 'newest', labelKey: 'tracks.sortNewest' },
  { value: 'title', labelKey: 'tracks.sortTitle' },
  { value: 'artist', labelKey: 'tracks.sortArtist' },
  { value: 'oldest', labelKey: 'tracks.sortOldest' },
  { value: 'duration', labelKey: 'tracks.sortDuration' },
];

interface EditForm {
  title: string;
  artist: string;
  album: string;
  genreName: string;
  year: string;
  trackNumber: string;
  discNumber: string;
}

function formFrom(track: Track): EditForm {
  return {
    title: track.title,
    artist: track.artist,
    album: track.album,
    genreName: track.genre ?? '',
    year: track.year === null ? '' : String(track.year),
    trackNumber: track.trackNumber === null ? '' : String(track.trackNumber),
    discNumber: track.discNumber === null ? '' : String(track.discNumber),
  };
}

function optionalNumber(value: string): number | undefined {
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? Math.trunc(parsed) : undefined;
}

export function TracksPage() {
  const { t, formatNumber } = useUi();
  const { isAdmin, canContribute } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();

  const presetGenre = searchParams.get('genreId') ?? undefined;
  const presetAlbum = searchParams.get('albumId') ?? undefined;
  const presetArtist = searchParams.get('artistId') ?? undefined;
  const presetFolder = searchParams.get('folderId') ?? undefined;
  const presetFilterLabel = presetGenre ?? presetAlbum ?? presetArtist ?? presetFolder;

  const [q, setQ] = useState('');
  const debouncedQ = useDebounced(q.trim(), 350);
  const [sort, setSort] = useState<NonNullable<TrackQuery['sort']>>('newest');
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const [editing, setEditing] = useState<Track | null>(null);
  const [editForm, setEditForm] = useState<EditForm | null>(null);
  const [editError, setEditError] = useState<string | null>(null);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Track | null>(null);
  const [restoring, setRestoring] = useState<Track | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);

  useEffect(() => {
    setPage(1);
    setSelectedIds(new Set());
  }, [debouncedQ, sort, favoritesOnly, includeDeleted, pageSize, presetGenre, presetAlbum, presetArtist, presetFolder]);

  const listQuery = useQuery({
    queryKey: [
      'tracks',
      { q: debouncedQ, sort, favoritesOnly, includeDeleted, page, pageSize, presetGenre, presetAlbum, presetArtist, presetFolder },
    ],
    queryFn: () =>
      api.tracks.list({
        q: debouncedQ || undefined,
        genreId: presetGenre,
        albumId: presetAlbum,
        artistId: presetArtist,
        folderId: presetFolder,
        favorites: favoritesOnly || undefined,
        includeDeleted: isAdmin && includeDeleted ? true : undefined,
        sort,
        page,
        pageSize,
      }),
    placeholderData: (previous) => previous,
  });

  const detailQuery = useQuery({
    queryKey: ['track', detailId],
    queryFn: () => api.tracks.get(detailId!),
    enabled: Boolean(detailId),
  });

  const invalidateTracks = () => {
    void queryClient.invalidateQueries({ queryKey: ['tracks'] });
    void queryClient.invalidateQueries({ queryKey: ['track'] });
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const favoriteMutation = useMutation({
    mutationFn: ({ track, favorite }: { track: Track; favorite: boolean }) => api.tracks.favorite(track.id, favorite),
    onSuccess: () => {
      invalidateTracks();
      notify(t('tracks.favoriteSuccess'), 'success');
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: Parameters<typeof api.tracks.update>[1] }) =>
      api.tracks.update(id, payload),
    onSuccess: () => {
      invalidateTracks();
      notify(t('tracks.updateSuccess'), 'success');
      setEditing(null);
      setEditForm(null);
    },
    onError: (error) => setEditError(errorMessage(error, t('common.error'))),
  });

  const deleteMutation = useMutation({
    mutationFn: (track: Track) => api.tracks.remove(track.id),
    onSuccess: () => {
      invalidateTracks();
      notify(t('tracks.deleteSuccess'), 'success');
      setDeleting(null);
    },
    onError: (error) => {
      notify(errorMessage(error, t('common.error')), 'error');
      setDeleting(null);
    },
  });

  const restoreMutation = useMutation({
    mutationFn: (track: Track) => api.tracks.restore(track.id),
    onSuccess: () => {
      invalidateTracks();
      notify(t('tracks.restoreSuccess'), 'success');
      setRestoring(null);
    },
    onError: (error) => {
      notify(errorMessage(error, t('common.error')), 'error');
      setRestoring(null);
    },
  });

  const bulkDeleteMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await Promise.all(ids.map((id) => api.tracks.remove(id)));
    },
    onSuccess: (_, ids) => {
      invalidateTracks();
      notify(t('tracks.bulkDeleted', { count: formatNumber(ids.length) }), 'success');
      setSelectedIds(new Set());
      setBulkDeleteOpen(false);
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const toggleSelectAll = () => {
    const allSelected = rows.length > 0 && rows.every((tr) => selectedIds.has(tr.id));
    if (allSelected) {
      setSelectedIds((current) => {
        const next = new Set(current);
        for (const item of rows) next.delete(item.id);
        return next;
      });
    } else {
      setSelectedIds((current) => {
        const next = new Set(current);
        for (const item of rows) next.add(item.id);
        return next;
      });
    }
  };

  const toggleSelectOne = (id: string) => {
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const openEdit = (track: Track) => {
    setEditing(track);
    setEditForm(formFrom(track));
    setEditError(null);
  };

  const submitEdit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editing || !editForm) return;
    const title = editForm.title.trim();
    if (!title) {
      setEditError(t('common.requiredField'));
      return;
    }
    updateMutation.mutate({
      id: editing.id,
      payload: {
        title,
        artist: editForm.artist.trim(),
        album: editForm.album.trim(),
        genreName: editForm.genreName.trim(),
        year: optionalNumber(editForm.year),
        trackNumber: optionalNumber(editForm.trackNumber),
        discNumber: optionalNumber(editForm.discNumber),
      },
    });
  };

  const clearPreset = () => {
    const next = new URLSearchParams(searchParams);
    for (const key of ['genreId', 'albumId', 'artistId', 'folderId']) next.delete(key);
    setSearchParams(next);
  };

  const rows = listQuery.data?.items ?? [];
  const total = listQuery.data?.total ?? 0;
  const sortOptions = useMemo(
    () => SORT_OPTIONS.map((option) => ({ value: option.value, label: t(option.labelKey) })),
    [t],
  );

  return (
    <div className="page">
      <PageHeader
        title={t('tracks.title')}
        subtitle={t('tracks.subtitle')}
        actions={
          <Button icon="refresh" loading={listQuery.isFetching} onClick={() => void listQuery.refetch()}>
            {t('common.refresh')}
          </Button>
        }
      />

      <Toolbar>
        <div className="search-field">
          <Icon name="search" size={18} />
          <Input
            type="search"
            value={q}
            onChange={(event) => setQ(event.target.value)}
            placeholder={t('tracks.searchPlaceholder')}
            aria-label={t('common.search')}
          />
        </div>
        <label className="inline-field">
          <span>{t('tracks.sort')}</span>
          <Select
            options={sortOptions}
            value={sort}
            onChange={(event) => setSort(event.target.value as NonNullable<TrackQuery['sort']>)}
          />
        </label>
        <Checkbox label={t('tracks.favoritesOnly')} checked={favoritesOnly} onChange={(event) => setFavoritesOnly(event.target.checked)} />
        {isAdmin ? (
          <Checkbox
            label={t('tracks.includeDeleted')}
            checked={includeDeleted}
            onChange={(event) => setIncludeDeleted(event.target.checked)}
          />
        ) : null}
        {q ? (
          <Button variant="ghost" size="sm" onClick={() => setQ('')}>
            {t('common.clear')}
          </Button>
        ) : null}
      </Toolbar>

      {presetFilterLabel ? (
        <div className="notice notice-info">
          <span>
            {t('common.filteredBy')} <code className="mono">{presetFilterLabel}</code>
          </span>
          <Button size="sm" onClick={clearPreset}>
            {t('common.clear')}
          </Button>
        </div>
      ) : null}
      {isAdmin && includeDeleted ? <div className="notice notice-warning">{t('tracks.includeDeletedHint')}</div> : null}

      {isAdmin && !includeDeleted && selectedIds.size > 0 ? (
        <div className="bulk-actions-bar">
          <div className="bulk-actions-info">
            <span>{t('common.selectedCount', { count: formatNumber(selectedIds.size) })}</span>
          </div>
          <div className="bulk-actions-btns">
            <Button variant="danger" icon="trash" size="sm" onClick={() => setBulkDeleteOpen(true)}>
              {t('common.deleteSelected')}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => setSelectedIds(new Set())}>
              {t('common.clearSelection')}
            </Button>
          </div>
        </div>
      ) : null}

      {listQuery.isLoading ? <LoadingState /> : null}
      {listQuery.isError ? (
        <ErrorState error={listQuery.error} title={t('tracks.loadFailed')} onRetry={() => void listQuery.refetch()} />
      ) : null}

      {listQuery.data ? (
        <>
          <TrackTable
            tracks={rows}
            selectable={isAdmin && !includeDeleted}
            selectedIds={selectedIds}
            onToggleSelectOne={toggleSelectOne}
            onToggleSelectAll={toggleSelectAll}
            actions={(track) => (
              <>
                <IconButton
                  icon={track.isFavorite ? 'star-filled' : 'star'}
                  label={track.isFavorite ? t('tracks.unfavorite') : t('tracks.favorite')}
                  onClick={() => favoriteMutation.mutate({ track, favorite: !track.isFavorite })}
                  disabled={favoriteMutation.isPending}
                />
                <IconButton icon="external" label={t('common.details')} onClick={() => setDetailId(track.id)} />
                {canContribute ? <IconButton icon="edit" label={t('common.edit')} onClick={() => openEdit(track)} /> : null}
                {isAdmin && !includeDeleted ? (
                  <IconButton icon="trash" label={t('common.delete')} onClick={() => setDeleting(track)} />
                ) : null}
                {isAdmin && includeDeleted ? (
                  <IconButton icon="restore" label={t('common.restore')} onClick={() => setRestoring(track)} />
                ) : null}
              </>
            )}
          />
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPage(1);
            }}
          />
        </>
      ) : null}

      <Modal
        open={editing !== null && editForm !== null}
        title={t('tracks.editTitle')}
        onClose={() => {
          setEditing(null);
          setEditForm(null);
          setEditError(null);
        }}
        footer={
          <>
            <Button
              onClick={() => {
                setEditing(null);
                setEditForm(null);
              }}
            >
              {t('common.cancel')}
            </Button>
            <Button variant="primary" type="submit" form="track-edit-form" loading={updateMutation.isPending}>
              {updateMutation.isPending ? t('common.saving') : t('common.save')}
            </Button>
          </>
        }
      >
        <p className="muted small">{t('tracks.editSubtitle')}</p>
        {editError ? (
          <div className="notice notice-error" role="alert">
            {editError}
          </div>
        ) : null}
        <form id="track-edit-form" className="form" onSubmit={submitEdit} noValidate>
          <Field label={t('tracks.titleColumn')} htmlFor="track-title" required>
            <Input
              id="track-title"
              value={editForm?.title ?? ''}
              onChange={(event) => setEditForm((form) => (form ? { ...form, title: event.target.value } : form))}
              required
            />
          </Field>
          <div className="form-grid two">
            <Field label={t('tracks.artist')} htmlFor="track-artist">
              <Input
                id="track-artist"
                value={editForm?.artist ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, artist: event.target.value } : form))}
              />
            </Field>
            <Field label={t('tracks.album')} htmlFor="track-album">
              <Input
                id="track-album"
                value={editForm?.album ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, album: event.target.value } : form))}
              />
            </Field>
          </div>
          <Field label={t('tracks.genre')} htmlFor="track-genre" hint={t('tracks.genrePlaceholder')}>
            <Input
              id="track-genre"
              value={editForm?.genreName ?? ''}
              onChange={(event) => setEditForm((form) => (form ? { ...form, genreName: event.target.value } : form))}
            />
          </Field>
          <div className="form-grid three">
            <Field label={t('tracks.year')} htmlFor="track-year" hint={t('tracks.clearUnsupported')}>
              <Input
                id="track-year"
                type="number"
                inputMode="numeric"
                value={editForm?.year ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, year: event.target.value } : form))}
              />
            </Field>
            <Field label={t('tracks.trackNumber')} htmlFor="track-number">
              <Input
                id="track-number"
                type="number"
                inputMode="numeric"
                value={editForm?.trackNumber ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, trackNumber: event.target.value } : form))}
              />
            </Field>
            <Field label={t('tracks.discNumber')} htmlFor="track-disc">
              <Input
                id="track-disc"
                type="number"
                inputMode="numeric"
                value={editForm?.discNumber ?? ''}
                onChange={(event) => setEditForm((form) => (form ? { ...form, discNumber: event.target.value } : form))}
              />
            </Field>
          </div>
        </form>
      </Modal>

      <Modal
        open={detailId !== null}
        title={t('tracks.detailsTitle')}
        onClose={() => setDetailId(null)}
        size="lg"
      >
        {detailQuery.isLoading ? <LoadingState /> : null}
        {detailQuery.isError ? (
          <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
        ) : null}
        {detailQuery.data ? (
          <div className="stack">
            <div className="detail-hero">
              <Artwork url={detailQuery.data.track.artworkUrl} alt="" size={88} />
              <div>
                <h3>{detailQuery.data.track.title}</h3>
                <p className="muted">
                  {detailQuery.data.track.artist || '—'} · {detailQuery.data.track.album || '—'}
                </p>
                <div className="tag-row">
                  {detailQuery.data.track.genre ? <Badge tone="info">{detailQuery.data.track.genre}</Badge> : null}
                  {detailQuery.data.track.year ? <Badge>{detailQuery.data.track.year}</Badge> : null}
                  {detailQuery.data.track.isFavorite ? <Badge tone="warning">{t('tracks.favoritesOnly')}</Badge> : null}
                </div>
              </div>
            </div>
            <dl className="definition-list">
              <div className="definition-row">
                <dt>{t('tracks.fileInfo')}</dt>
                <dd>
                  {detailQuery.data.file.mimeType} · {detailQuery.data.file.extension} ·{' '}
                  {detailQuery.data.file.sizeBytes.toLocaleString()} B
                </dd>
              </div>
              <div className="definition-row">
                <dt>{t('tracks.codec')}</dt>
                <dd>{detailQuery.data.track.codec ?? '—'}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('tracks.checksum')}</dt>
                <dd className="mono wrap">{detailQuery.data.file.sha256}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('tracks.bitrate')}</dt>
                <dd>{detailQuery.data.file.bitrate ?? '—'}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('tracks.sampleRate')}</dt>
                <dd>{detailQuery.data.file.sampleRate ?? '—'}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('tracks.channels')}</dt>
                <dd>{detailQuery.data.file.channels ?? '—'}</dd>
              </div>
            </dl>
            <DefinitionGroup
              title={t('tracks.artist')}
              values={detailQuery.data.artists.map((artist) => `${artist.name} (${artist.role})`)}
            />
            <DefinitionGroup title={t('albums.title')} values={detailQuery.data.albums.map((album) => album.title)} />
            <DefinitionGroup title={t('folders.title')} values={detailQuery.data.folders.map((folder) => folder.name)} />
          </div>
        ) : null}
      </Modal>

      <ConfirmDialog
        open={deleting !== null}
        title={t('tracks.deleteTitle')}
        body={t('tracks.deleteBody', { title: deleting?.title ?? '' })}
        confirmLabel={t('common.delete')}
        busy={deleteMutation.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          if (deleting) deleteMutation.mutate(deleting);
        }}
      />

      <ConfirmDialog
        open={restoring !== null}
        title={t('tracks.restoreTitle')}
        body={t('tracks.restoreBody', { title: restoring?.title ?? '' })}
        confirmLabel={t('common.restore')}
        tone="primary"
        busy={restoreMutation.isPending}
        onCancel={() => setRestoring(null)}
        onConfirm={() => {
          if (restoring) restoreMutation.mutate(restoring);
        }}
      />

      <ConfirmDialog
        open={bulkDeleteOpen}
        title={t('tracks.bulkDeleteTitle')}
        body={t('tracks.bulkDeleteBody', { count: formatNumber(selectedIds.size) })}
        confirmLabel={t('common.delete')}
        busy={bulkDeleteMutation.isPending}
        onCancel={() => setBulkDeleteOpen(false)}
        onConfirm={() => {
          bulkDeleteMutation.mutate(Array.from(selectedIds));
        }}
      />

      <p className="muted small">
        <Link to="/library/upload">{t('nav.upload')}</Link>
      </p>
    </div>
  );
}

function DefinitionGroup({ title, values }: { title: string; values: string[] }) {
  if (values.length === 0) return null;
  return (
    <div>
      <h4>{title}</h4>
      <div className="tag-row">
        {values.map((value) => (
          <Badge key={value}>{value}</Badge>
        ))}
      </div>
    </div>
  );
}
