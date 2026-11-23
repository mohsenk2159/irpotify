import { useEffect, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { ArtistSummary } from '../api/types';
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
import { Link } from 'react-router-dom';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

interface ArtistForm {
  name: string;
  biography: string;
}

export function ArtistsPage() {
  const { t, formatNumber } = useUi();
  const { canContribute } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [q, setQ] = useState('');
  const debouncedQ = useDebounced(q.trim(), 350);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const [detailId, setDetailId] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<ArtistSummary | null>(null);
  const [form, setForm] = useState<ArtistForm>({ name: '', biography: '' });
  const [formError, setFormError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<ArtistSummary | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);

  useEffect(() => {
    setPage(1);
    setSelectedIds(new Set());
  }, [debouncedQ, pageSize]);

  const listQuery = useQuery({
    queryKey: ['artists', { q: debouncedQ, page, pageSize }],
    queryFn: () => api.artists.list({ q: debouncedQ || undefined, page, pageSize }),
    placeholderData: (previous) => previous,
  });

  const detailQuery = useQuery({
    queryKey: ['artist', detailId],
    queryFn: () => api.artists.get(detailId!),
    enabled: Boolean(detailId),
  });

  const saveMutation = useMutation({
    mutationFn: async (payload: { id: string | null; name: string; biography: string | null }) => {
      if (payload.id) {
        await api.artists.update(payload.id, { name: payload.name, biography: payload.biography });
      } else {
        await api.artists.create({ name: payload.name, biography: payload.biography });
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['artists'] });
      void queryClient.invalidateQueries({ queryKey: ['artist'] });
      void queryClient.invalidateQueries({ queryKey: ['albums'] });
      void queryClient.invalidateQueries({ queryKey: ['tracks'] });
      notify(editing ? t('artists.updated') : t('artists.created'), 'success');
      setFormOpen(false);
      setEditing(null);
      setForm({ name: '', biography: '' });
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const deleteMutation = useMutation({
    mutationFn: (artist: ArtistSummary) => api.artists.remove(artist.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['artists'] });
      void queryClient.invalidateQueries({ queryKey: ['artist'] });
      void queryClient.invalidateQueries({ queryKey: ['albums'] });
      void queryClient.invalidateQueries({ queryKey: ['tracks'] });
      notify(t('artists.deleted'), 'success');
      setDeleting(null);
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const bulkDeleteMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await Promise.all(ids.map((id) => api.artists.remove(id)));
    },
    onSuccess: (_, ids) => {
      void queryClient.invalidateQueries({ queryKey: ['artists'] });
      void queryClient.invalidateQueries({ queryKey: ['artist'] });
      void queryClient.invalidateQueries({ queryKey: ['albums'] });
      void queryClient.invalidateQueries({ queryKey: ['tracks'] });
      notify(t('artists.bulkDeleted', { count: formatNumber(ids.length) }), 'success');
      setSelectedIds(new Set());
      setBulkDeleteOpen(false);
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const items = listQuery.data?.items ?? [];
  const allSelected = items.length > 0 && items.every((a) => selectedIds.has(a.id));
  const someSelected = items.some((a) => selectedIds.has(a.id)) && !allSelected;

  const toggleSelectAll = () => {
    if (allSelected) {
      setSelectedIds((current) => {
        const next = new Set(current);
        for (const item of items) next.delete(item.id);
        return next;
      });
    } else {
      setSelectedIds((current) => {
        const next = new Set(current);
        for (const item of items) next.add(item.id);
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

  const openCreate = () => {
    setEditing(null);
    setForm({ name: '', biography: '' });
    setFormError(null);
    setFormOpen(true);
  };

  const openEdit = (artist: ArtistSummary) => {
    setEditing(artist);
    setForm({ name: artist.name, biography: artist.biography ?? '' });
    setFormError(null);
    setFormOpen(true);
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!form.name.trim()) {
      setFormError(t('common.requiredField'));
      return;
    }
    saveMutation.mutate({
      id: editing?.id ?? null,
      name: form.name.trim(),
      biography: form.biography.trim() ? form.biography.trim() : null,
    });
  };

  const detail = detailQuery.data;

  return (
    <div className="page">
      <PageHeader
        title={t('artists.title')}
        subtitle={t('artists.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={listQuery.isFetching} onClick={() => void listQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            {canContribute ? (
              <Button variant="primary" icon="plus" onClick={openCreate}>
                {t('artists.create')}
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
            placeholder={t('artists.search')}
            aria-label={t('common.search')}
          />
        </div>
        {q ? (
          <Button variant="ghost" size="sm" onClick={() => setQ('')}>
            {t('common.clear')}
          </Button>
        ) : null}
      </Toolbar>

      {selectedIds.size > 0 ? (
        <div className="bulk-actions-bar">
          <div className="bulk-actions-info">
            <span>{t('common.selectedCount', { count: formatNumber(selectedIds.size) })}</span>
          </div>
          <div className="bulk-actions-btns">
            {canContribute ? (
              <Button variant="danger" icon="trash" size="sm" onClick={() => setBulkDeleteOpen(true)}>
                {t('common.deleteSelected')}
              </Button>
            ) : null}
            <Button variant="ghost" size="sm" onClick={() => setSelectedIds(new Set())}>
              {t('common.clearSelection')}
            </Button>
          </div>
        </div>
      ) : null}

      {listQuery.isLoading ? <LoadingState /> : null}
      {listQuery.isError ? (
        <ErrorState error={listQuery.error} title={t('artists.loadFailed')} onRetry={() => void listQuery.refetch()} />
      ) : null}

      {listQuery.data ? (
        listQuery.data.items.length === 0 ? (
          <EmptyState
            title={t('artists.empty')}
            body={t('artists.emptyBody')}
            icon="artists"
            action={
              canContribute ? (
                <Button variant="primary" icon="plus" onClick={openCreate}>
                  {t('artists.create')}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col" className="col-select">
                      <input
                        type="checkbox"
                        className="checkbox"
                        checked={allSelected}
                        ref={(el) => {
                          if (el) el.indeterminate = someSelected;
                        }}
                        onChange={toggleSelectAll}
                        aria-label={t('common.selectAll')}
                      />
                    </th>
                    <th scope="col">{t('common.name')}</th>
                    <th scope="col" className="col-narrow">
                      {t('nav.tracks')}
                    </th>
                    <th scope="col" className="col-narrow">
                      {t('nav.albums')}
                    </th>
                    <th scope="col" className="col-actions">
                      <span className="sr-only">{t('common.actions')}</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {listQuery.data.items.map((artist) => (
                    <tr key={artist.id}>
                      <td className="col-select">
                        <input
                          type="checkbox"
                          className="checkbox"
                          checked={selectedIds.has(artist.id)}
                          onChange={() => toggleSelectOne(artist.id)}
                          aria-label={artist.name}
                        />
                      </td>
                      <td>
                        <div className="table-title-cell">
                          <Artwork url={artist.artworkUrl} alt="" size={34} />
                          <div className="table-title-text">
                            <span className="table-title">{artist.name}</span>
                            {artist.biography ? <span className="table-sub truncate">{artist.biography}</span> : null}
                          </div>
                        </div>
                      </td>
                      <td className="col-narrow mono">{formatNumber(artist.trackCount)}</td>
                      <td className="col-narrow mono">{formatNumber(artist.albumCount)}</td>
                      <td className="col-actions">
                        <div className="row-actions">
                          <IconButton icon="external" label={t('common.details')} onClick={() => setDetailId(artist.id)} />
                          <Link className="btn btn-ghost btn-icon" to={`/library/tracks?artistId=${artist.id}`} title={t('genres.viewTracks')} aria-label={t('genres.viewTracks')}>
                            <Icon name="tracks" size={18} />
                          </Link>
                          {canContribute ? (
                            <>
                              <IconButton icon="edit" label={t('common.edit')} onClick={() => openEdit(artist)} />
                              <IconButton icon="trash" label={t('common.delete')} onClick={() => setDeleting(artist)} />
                            </>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
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

      <Modal
        open={formOpen}
        title={editing ? t('artists.edit') : t('artists.create')}
        onClose={() => setFormOpen(false)}
        footer={
          <>
            <Button onClick={() => setFormOpen(false)}>{t('common.cancel')}</Button>
            <Button type="submit" form="artist-form" variant="primary" loading={saveMutation.isPending}>
              {saveMutation.isPending ? t('common.saving') : t('common.save')}
            </Button>
          </>
        }
      >
        {formError ? (
          <div className="notice notice-error" role="alert">
            {formError}
          </div>
        ) : null}
        <form id="artist-form" className="form" onSubmit={submit} noValidate>
          <Field label={t('common.name')} htmlFor="artist-name" required>
            <Input
              id="artist-name"
              value={form.name}
              onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
              required
            />
          </Field>
          <Field label={t('artists.biography')} htmlFor="artist-bio">
            <Textarea
              id="artist-bio"
              rows={5}
              value={form.biography}
              onChange={(event) => setForm((current) => ({ ...current, biography: event.target.value }))}
            />
          </Field>
        </form>
      </Modal>

      <Modal
        open={detailId !== null}
        title={detail?.artist.name ?? t('artists.detailTitle')}
        onClose={() => setDetailId(null)}
        size="lg"
      >
        {detailQuery.isLoading ? <LoadingState /> : null}
        {detailQuery.isError ? (
          <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
        ) : null}
        {detail ? (
          <div className="stack">
            <div className="detail-hero">
              <Artwork url={detail.artist.artworkUrl} alt="" size={88} />
              <div>
                <h3>{detail.artist.name}</h3>
                {detail.artist.biography ? <p>{detail.artist.biography}</p> : null}
                <div className="tag-row">
                  <Badge>{t('artists.trackCount', { count: formatNumber(detail.tracks.length) })}</Badge>
                  <Badge>{t('artists.albumCount', { count: formatNumber(detail.albums.length) })}</Badge>
                </div>
              </div>
            </div>
            <Card title={t('artists.albums')}>
              {detail.albums.length === 0 ? (
                <p className="muted">{t('artists.noAlbums')}</p>
              ) : (
                <ul className="plain-list">
                  {detail.albums.map((album) => (
                    <li key={album.id} className="plain-list-row">
                      <span>{album.title}</span>
                      <span className="muted">
                        {album.year ?? '—'} · {t('albums.trackCount', { count: formatNumber(album.trackCount) })}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
            <Card title={t('tracks.title')}>
              <TrackTable tracks={detail.tracks} showAdded={false} />
            </Card>
          </div>
        ) : null}
      </Modal>

      <ConfirmDialog
        open={deleting !== null}
        title={t('artists.deleteTitle')}
        body={t('artists.deleteBody', { name: deleting?.name ?? '' })}
        confirmLabel={t('common.delete')}
        busy={deleteMutation.isPending}
        onConfirm={() => {
          if (deleting) deleteMutation.mutate(deleting);
        }}
        onCancel={() => setDeleting(null)}
      />

      <ConfirmDialog
        open={bulkDeleteOpen}
        title={t('artists.bulkDeleteTitle')}
        body={t('artists.bulkDeleteBody', { count: formatNumber(selectedIds.size) })}
        confirmLabel={t('common.delete')}
        busy={bulkDeleteMutation.isPending}
        onConfirm={() => {
          bulkDeleteMutation.mutate(Array.from(selectedIds));
        }}
        onCancel={() => setBulkDeleteOpen(false)}
      />
    </div>
  );
}
