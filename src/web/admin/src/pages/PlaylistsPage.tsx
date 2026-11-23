import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { PlaylistSummary } from '../api/types';
import type { TranslationKey } from '../i18n/translations';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { useDebounced } from '../ui/useDebounced';
import { ConfirmDialog, Modal, useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  Checkbox,
  EmptyState,
  ErrorState,
  Field,
  IconButton,
  Input,
  LoadingState,
  PageHeader,
  Select,
  Textarea,
  Toolbar,
} from '../ui/primitives';
import { TrackTable } from '../ui/TrackTable';
import { errorMessage, shortId } from '../ui/helpers';
import { Icon } from '../ui/icons';

const PLAYLIST_SORT_OPTIONS: { value: 'updated' | 'name' | 'name_desc' | 'trackCount'; labelKey: TranslationKey }[] = [
  { value: 'updated', labelKey: 'playlists.sortUpdated' },
  { value: 'name', labelKey: 'playlists.sortName' },
  { value: 'name_desc', labelKey: 'playlists.sortNameDesc' },
  { value: 'trackCount', labelKey: 'playlists.sortTrackCount' },
];

interface PlaylistForm {
  name: string;
  description: string;
  isPublic: boolean;
  isCollaborative: boolean;
}

const EMPTY_FORM: PlaylistForm = { name: '', description: '', isPublic: false, isCollaborative: false };

export function PlaylistsPage() {
  const { t, formatNumber, formatDate } = useUi();
  const { user, isAdmin } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [playlistSort, setPlaylistSort] = useState<'updated' | 'name' | 'name_desc' | 'trackCount'>('updated');
  const [order, setOrder] = useState<string[]>([]);
  const [addOpen, setAddOpen] = useState(false);
  const [addSearch, setAddSearch] = useState('');
  const debouncedAddSearch = useDebounced(addSearch.trim(), 300);
  const [removingTrackId, setRemovingTrackId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<PlaylistSummary | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [formMode, setFormMode] = useState<'create' | 'edit' | null>(null);
  const [form, setForm] = useState<PlaylistForm>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);

  const playlistsQuery = useQuery({ queryKey: ['playlists'], queryFn: api.playlists.list });
  const detailQuery = useQuery({
    queryKey: ['playlist', selectedId],
    queryFn: () => api.playlists.get(selectedId!),
    enabled: Boolean(selectedId),
  });

  const addCandidatesQuery = useQuery({
    queryKey: ['tracks', 'playlist-add', debouncedAddSearch],
    queryFn: () =>
      api.tracks.list({ q: debouncedAddSearch || undefined, page: 1, pageSize: 10, sort: 'title' }),
    enabled: addOpen,
  });

  const selected = useMemo(
    () => playlistsQuery.data?.find((playlist) => playlist.id === selectedId) ?? null,
    [playlistsQuery.data, selectedId],
  );

  const sortedPlaylists = useMemo(() => {
    const list = playlistsQuery.data ?? [];
    const sorted = [...list];
    switch (playlistSort) {
      case 'name':
        sorted.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }));
        break;
      case 'name_desc':
        sorted.sort((a, b) => b.name.localeCompare(a.name, undefined, { numeric: true, sensitivity: 'base' }));
        break;
      case 'trackCount':
        sorted.sort((a, b) => b.trackCount - a.trackCount);
        break;
      default:
        sorted.sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime());
        break;
    }
    return sorted;
  }, [playlistsQuery.data, playlistSort]);

  useEffect(() => {
    if (!selectedId && playlistsQuery.data && playlistsQuery.data.length > 0) {
      setSelectedId(playlistsQuery.data[0]?.id ?? null);
    }
  }, [playlistsQuery.data, selectedId]);

  useEffect(() => {
    setOrder(detailQuery.data?.tracks.map((track) => track.id) ?? []);
  }, [detailQuery.data]);

  const canEdit = Boolean(selected && user && (isAdmin || selected.ownerUserId === user.id));
  const detailTracks = detailQuery.data?.tracks ?? [];
  const orderedTracks = useMemo(() => {
    const byId = new Map(detailTracks.map((track) => [track.id, track]));
    return order.map((id) => byId.get(id)).filter((track): track is NonNullable<typeof track> => Boolean(track));
  }, [detailTracks, order]);
  const orderDirty =
    detailTracks.length === order.length && detailTracks.some((track, index) => track.id !== order[index]);

  const sortTracksBy = (mode: 'title' | 'artist' | 'duration') => {
    const sorted = [...orderedTracks];
    if (mode === 'title') {
      sorted.sort((a, b) => a.title.localeCompare(b.title, undefined, { numeric: true, sensitivity: 'base' }));
    } else if (mode === 'artist') {
      sorted.sort((a, b) => a.artist.localeCompare(b.artist, undefined, { numeric: true, sensitivity: 'base' }));
    } else if (mode === 'duration') {
      sorted.sort((a, b) => b.durationSeconds - a.durationSeconds);
    }
    setOrder(sorted.map((t) => t.id));
  };

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['playlists'] });
    void queryClient.invalidateQueries({ queryKey: ['playlist', selectedId] });
  };

  const createMutation = useMutation({
    mutationFn: (payload: PlaylistForm) =>
      api.playlists.create({
        name: payload.name,
        description: payload.description.trim() || null,
        isPublic: payload.isPublic,
        isCollaborative: payload.isCollaborative,
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ['playlists'] });
      setSelectedId(created.id);
      setFormMode(null);
      setForm(EMPTY_FORM);
      notify(t('playlists.created'), 'success');
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const updateMutation = useMutation({
    mutationFn: (payload: PlaylistForm) =>
      api.playlists.update(selectedId!, {
        name: payload.name,
        description: payload.description.trim() || null,
        isPublic: payload.isPublic,
        isCollaborative: payload.isCollaborative,
      }),
    onSuccess: () => {
      invalidate();
      setFormMode(null);
      notify(t('playlists.updated'), 'success');
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.playlists.remove(id),
    onSuccess: (_result, id) => {
      setDeleting(null);
      setSelectedId(null);
      // Drop the deleted playlist's cached detail instead of refetching it (which would 404).
      queryClient.removeQueries({ queryKey: ['playlist', id] });
      void queryClient.invalidateQueries({ queryKey: ['playlists'] });
      notify(t('playlists.deleted'), 'success');
    },
    onError: (error) => {
      setDeleting(null);
      notify(errorMessage(error, t('common.error')), 'error');
    },
  });

  const playlists = playlistsQuery.data ?? [];
  const editablePlaylists = playlists.filter((p) => isAdmin || (user && p.ownerUserId === user.id));
  const allSelected = editablePlaylists.length > 0 && editablePlaylists.every((p) => selectedIds.has(p.id));

  const toggleSelectAll = () => {
    if (allSelected) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(editablePlaylists.map((p) => p.id)));
    }
  };

  const toggleSelectOne = (id: string, e: React.MouseEvent | React.ChangeEvent) => {
    e.stopPropagation();
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const bulkDeleteMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await Promise.all(ids.map((id) => api.playlists.remove(id)));
    },
    onSuccess: (_, ids) => {
      for (const id of ids) {
        queryClient.removeQueries({ queryKey: ['playlist', id] });
      }
      void queryClient.invalidateQueries({ queryKey: ['playlists'] });
      notify(t('playlists.bulkDeleted', { count: formatNumber(ids.length) }), 'success');
      setSelectedIds(new Set());
      setBulkDeleteOpen(false);
      if (selectedId && ids.includes(selectedId)) {
        setSelectedId(null);
      }
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const reorderMutation = useMutation({
    mutationFn: (trackIds: string[]) => api.playlists.reorder(selectedId!, trackIds),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['playlist', selectedId] });
      notify(t('playlists.reordered'), 'success');
    },
    onError: (error) => {
      notify(errorMessage(error, t('common.error')), 'error');
      void detailQuery.refetch();
    },
  });

  const addMutation = useMutation({
    mutationFn: (trackId: string) => api.playlists.addTrack(selectedId!, trackId),
    onSuccess: () => {
      invalidate();
      notify(t('playlists.updated'), 'success');
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const removeMutation = useMutation({
    mutationFn: (trackId: string) => api.playlists.removeTrack(selectedId!, trackId),
    onSuccess: () => {
      setRemovingTrackId(null);
      invalidate();
      notify(t('playlists.updated'), 'success');
    },
    onError: (error) => {
      setRemovingTrackId(null);
      notify(errorMessage(error, t('common.error')), 'error');
    },
  });

  const openCreate = () => {
    setForm(EMPTY_FORM);
    setFormError(null);
    setFormMode('create');
  };

  const openEdit = () => {
    if (!selected) return;
    setForm({
      name: selected.name,
      description: selected.description ?? '',
      isPublic: selected.isPublic,
      isCollaborative: selected.isCollaborative,
    });
    setFormError(null);
    setFormMode('edit');
  };

  const submitForm = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const name = form.name.trim();
    if (!name) {
      setFormError(t('common.requiredField'));
      return;
    }
    const payload = { ...form, name };
    if (formMode === 'create') createMutation.mutate(payload);
    else if (formMode === 'edit') updateMutation.mutate(payload);
  };

  const moveTrack = (index: number, direction: -1 | 1) => {
    const target = index + direction;
    if (target < 0 || target >= order.length) return;
    const next = [...order];
    const current = next[index];
    const other = next[target];
    if (!current || !other) return;
    next[index] = other;
    next[target] = current;
    setOrder(next);
  };

  const existingIds = new Set(detailTracks.map((track) => track.id));

  return (
    <div className="page">
      <PageHeader
        title={t('playlists.title')}
        subtitle={t('playlists.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={playlistsQuery.isFetching} onClick={() => void playlistsQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            <Button variant="primary" icon="plus" onClick={openCreate}>
              {t('playlists.create')}
            </Button>
          </>
        }
      />

      {playlistsQuery.isLoading ? <LoadingState /> : null}
      {playlistsQuery.isError ? (
        <ErrorState error={playlistsQuery.error} title={t('playlists.loadFailed')} onRetry={() => void playlistsQuery.refetch()} />
      ) : null}

      {playlistsQuery.data ? (
        playlistsQuery.data.length === 0 ? (
          <EmptyState
            icon="playlists"
            title={t('playlists.empty')}
            body={t('playlists.emptyBody')}
            action={
              <Button variant="primary" icon="plus" onClick={openCreate}>
                {t('playlists.create')}
              </Button>
            }
          />
        ) : (
          <div className="split-layout">
            <section className="card playlist-list" aria-label={t('playlists.title')}>
              <div className="card-body">
                <div style={{ marginBottom: '10px' }}>
                  <Select
                    value={playlistSort}
                    onChange={(e) => setPlaylistSort(e.target.value as any)}
                    aria-label={t('playlists.sort')}
                    style={{ width: '100%', fontSize: '0.85rem' }}
                  >
                    {PLAYLIST_SORT_OPTIONS.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {t(opt.labelKey)}
                      </option>
                    ))}
                  </Select>
                </div>
                {editablePlaylists.length > 0 ? (
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px', gap: '8px' }}>
                    <Button variant="ghost" size="sm" onClick={toggleSelectAll}>
                      {allSelected ? t('common.clearSelection') : t('common.selectAll')}
                    </Button>
                    {selectedIds.size > 0 ? (
                      <Button variant="danger" icon="trash" size="sm" onClick={() => setBulkDeleteOpen(true)}>
                        {t('common.delete')} ({formatNumber(selectedIds.size)})
                      </Button>
                    ) : null}
                  </div>
                ) : null}
                <ul className="select-list">
                  {sortedPlaylists.map((playlist) => {
                    const canDeleteThis = isAdmin || (user && playlist.ownerUserId === user.id);
                    return (
                      <li key={playlist.id} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        {canDeleteThis ? (
                          <input
                            type="checkbox"
                            className="checkbox"
                            checked={selectedIds.has(playlist.id)}
                            onChange={(e) => toggleSelectOne(playlist.id, e)}
                            aria-label={playlist.name}
                          />
                        ) : null}
                        <button
                          type="button"
                          className={`select-list-item${playlist.id === selectedId ? ' is-active' : ''}`}
                          style={{ flex: 1 }}
                          aria-current={playlist.id === selectedId ? 'true' : undefined}
                          onClick={() => setSelectedId(playlist.id)}
                        >
                          <span className="select-list-title">{playlist.name}</span>
                          <span className="select-list-meta">
                            {t('playlists.trackCount', { count: formatNumber(playlist.trackCount) })}
                            {playlist.isPublic ? <Badge tone="info">{t('playlists.public')}</Badge> : null}
                            {playlist.isCollaborative ? <Badge tone="warning">{t('playlists.collaborative')}</Badge> : null}
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              </div>
            </section>

            <section className="card playlist-detail">
              <div className="card-body">
                {!selected ? (
                  <p className="muted">{t('playlists.select')}</p>
                ) : (
                  <>
                    <header className="detail-header">
                      <div>
                        <h2 className="card-title">{selected.name}</h2>
                        <p className="muted small">
                          {selected.description || '—'}
                        </p>
                        <p className="muted small">
                          {t('playlists.owner')}: <span className="mono">{shortId(selected.ownerUserId, 12)}</span>
                          {' · '}
                          {t('common.updatedAt')}: {formatDate(selected.updatedAt)}
                        </p>
                        <div className="tag-row">
                          {selected.isPublic ? <Badge tone="info">{t('playlists.public')}</Badge> : null}
                          {selected.isCollaborative ? <Badge tone="warning">{t('playlists.collaborative')}</Badge> : null}
                        </div>
                      </div>
                      {canEdit ? (
                        <div className="page-actions">
                          <Button icon="edit" onClick={openEdit}>
                            {t('common.edit')}
                          </Button>
                          <Button variant="danger" icon="trash" onClick={() => setDeleting(selected)}>
                            {t('common.delete')}
                          </Button>
                        </div>
                      ) : (
                        <p className="muted small">{t('playlists.readOnly')}</p>
                      )}
                    </header>

                    {detailQuery.isLoading ? <LoadingState /> : null}
                    {detailQuery.isError ? (
                      <ErrorState error={detailQuery.error} onRetry={() => void detailQuery.refetch()} />
                    ) : null}

                    {detailQuery.data ? (
                      <>
                        <Toolbar>
                          <Button
                            variant="primary"
                            icon="plus"
                            disabled={!canEdit}
                            onClick={() => {
                              setAddSearch('');
                              setAddOpen(true);
                            }}
                          >
                            {t('playlists.addTrack')}
                          </Button>
                          {canEdit && orderedTracks.length > 1 ? (
                            <Select
                              aria-label={t('playlists.sortTracks')}
                              onChange={(e) => {
                                const val = e.target.value as 'title' | 'artist' | 'duration';
                                if (val) sortTracksBy(val);
                              }}
                              defaultValue=""
                              style={{ width: 'auto', minWidth: '130px', fontSize: '0.85rem' }}
                            >
                              <option value="" disabled>{t('playlists.sortTracks')}</option>
                              <option value="title">{t('playlists.sortTitle')}</option>
                              <option value="artist">{t('playlists.sortArtist')}</option>
                              <option value="duration">{t('playlists.sortDuration')}</option>
                            </Select>
                          ) : null}
                          <Button
                            icon="refresh"
                            disabled={!canEdit || !orderDirty || reorderMutation.isPending}
                            loading={reorderMutation.isPending}
                            onClick={() => reorderMutation.mutate(order)}
                          >
                            {t('common.save')}
                          </Button>
                        </Toolbar>

                        {orderedTracks.length === 0 ? (
                          <EmptyState icon="playlists" title={t('playlists.noTracks')} body={t('playlists.addTrack')} />
                        ) : (
                          <TrackTable
                            tracks={orderedTracks}
                            actions={(track) => {
                              const index = order.indexOf(track.id);
                              return (
                                <>
                                  <IconButton
                                    icon="arrow-up"
                                    label={t('playlists.moveUp')}
                                    disabled={!canEdit || index <= 0}
                                    onClick={() => moveTrack(index, -1)}
                                  />
                                  <IconButton
                                    icon="arrow-down"
                                    label={t('playlists.moveDown')}
                                    disabled={!canEdit || index >= order.length - 1}
                                    onClick={() => moveTrack(index, 1)}
                                  />
                                  <IconButton
                                    icon="trash"
                                    label={t('playlists.removeTrack')}
                                    disabled={!canEdit}
                                    onClick={() => setRemovingTrackId(track.id)}
                                  />
                                </>
                              );
                            }}
                          />
                        )}
                      </>
                    ) : null}
                  </>
                )}
              </div>
            </section>
          </div>
        )
      ) : null}

      <Modal
        open={formMode !== null}
        title={formMode === 'edit' ? t('playlists.edit') : t('playlists.create')}
        onClose={() => setFormMode(null)}
        footer={
          <>
            <Button onClick={() => setFormMode(null)}>{t('common.cancel')}</Button>
            <Button
              variant="primary"
              type="submit"
              form="playlist-form"
              loading={createMutation.isPending || updateMutation.isPending}
            >
              {createMutation.isPending || updateMutation.isPending ? t('common.saving') : t('common.save')}
            </Button>
          </>
        }
      >
        {formError ? (
          <div className="notice notice-error" role="alert">
            {formError}
          </div>
        ) : null}
        <form id="playlist-form" className="form" onSubmit={submitForm} noValidate>
          <Field label={t('playlists.name')} htmlFor="playlist-name" required>
            <Input
              id="playlist-name"
              value={form.name}
              onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
              required
            />
          </Field>
          <Field label={t('playlists.description')} htmlFor="playlist-description">
            <Textarea
              id="playlist-description"
              rows={3}
              value={form.description}
              onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
            />
          </Field>
          <Checkbox
            label={t('playlists.public')}
            checked={form.isPublic}
            onChange={(event) => setForm((current) => ({ ...current, isPublic: event.target.checked }))}
          />
          <Checkbox
            label={t('playlists.collaborative')}
            checked={form.isCollaborative}
            onChange={(event) => setForm((current) => ({ ...current, isCollaborative: event.target.checked }))}
          />
        </form>
      </Modal>

      <Modal open={addOpen} title={t('playlists.addTrackTitle')} onClose={() => setAddOpen(false)} size="lg">
        <Toolbar>
          <div className="search-field">
            <Icon name="search" size={18} />
            <Input
              type="search"
              value={addSearch}
              onChange={(event) => setAddSearch(event.target.value)}
              placeholder={t('tracks.searchPlaceholder')}
              aria-label={t('common.search')}
            />
          </div>
        </Toolbar>
        {addCandidatesQuery.isLoading ? <LoadingState /> : null}
        {addCandidatesQuery.isError ? (
          <ErrorState error={addCandidatesQuery.error} onRetry={() => void addCandidatesQuery.refetch()} />
        ) : null}
        {addCandidatesQuery.data ? (
          <TrackTable
            tracks={addCandidatesQuery.data.items}
            showAdded={false}
            actions={(track) =>
              existingIds.has(track.id) ? (
                <Badge tone="success">{t('upload.uploaded')}</Badge>
              ) : (
                <Button
                  size="sm"
                  icon="plus"
                  loading={addMutation.isPending && addMutation.variables === track.id}
                  onClick={() => addMutation.mutate(track.id)}
                >
                  {t('playlists.addTrack')}
                </Button>
              )
            }
          />
        ) : null}
      </Modal>

      <ConfirmDialog
        open={deleting !== null}
        title={t('playlists.deleteTitle')}
        body={deleting ? t('playlists.deleteBody', { name: deleting.name }) : undefined}
        confirmLabel={t('common.delete')}
        busy={deleteMutation.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          if (deleting) deleteMutation.mutate(deleting.id);
        }}
      />

      <ConfirmDialog
        open={removingTrackId !== null}
        title={t('playlists.removeTrack')}
        body={t('playlists.removeConfirm', {
          title: detailTracks.find((track) => track.id === removingTrackId)?.title ?? '',
        })}
        confirmLabel={t('playlists.removeTrack')}
        busy={removeMutation.isPending}
        onCancel={() => setRemovingTrackId(null)}
        onConfirm={() => {
          if (removingTrackId) removeMutation.mutate(removingTrackId);
        }}
      />

      <ConfirmDialog
        open={bulkDeleteOpen}
        title={t('playlists.bulkDeleteTitle')}
        body={t('playlists.bulkDeleteBody', { count: formatNumber(selectedIds.size) })}
        confirmLabel={t('common.delete')}
        busy={bulkDeleteMutation.isPending}
        onCancel={() => setBulkDeleteOpen(false)}
        onConfirm={() => {
          bulkDeleteMutation.mutate(Array.from(selectedIds));
        }}
      />
    </div>
  );
}
