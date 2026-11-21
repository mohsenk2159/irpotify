import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { ApiError } from '../api/http';
import type { FolderNode, Track } from '../api/types';
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
} from '../ui/primitives';
import { Artwork } from '../ui/Artwork';
import { TrackTable } from '../ui/TrackTable';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

interface FolderForm {
  name: string;
  parentId: string;
  description: string;
}

const EMPTY_FORM: FolderForm = { name: '', parentId: '', description: '' };

function flattenFolders(nodes: FolderNode[], depth = 0): { node: FolderNode; depth: number }[] {
  const result: { node: FolderNode; depth: number }[] = [];
  for (const node of nodes) {
    result.push({ node, depth });
    result.push(...flattenFolders(node.children, depth + 1));
  }
  return result;
}

export function FoldersPage() {
  const { t, formatNumber } = useUi();
  const { isAdmin, canContribute } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<FolderNode | null>(null);
  const [form, setForm] = useState<FolderForm>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<FolderNode | null>(null);
  const [addTracksOpen, setAddTracksOpen] = useState(false);
  const [trackSearch, setTrackSearch] = useState('');
  const debouncedTrackSearch = useDebounced(trackSearch.trim(), 350);
  const [checkedTracks, setCheckedTracks] = useState<Set<string>>(new Set());
  const [removingTrack, setRemovingTrack] = useState<Track | null>(null);

  const foldersQuery = useQuery({ queryKey: ['folders'], queryFn: api.folders.list });
  const flatFolders = useMemo(() => flattenFolders(foldersQuery.data ?? []), [foldersQuery.data]);
  const selected = flatFolders.find((entry) => entry.node.id === selectedId)?.node ?? null;

  const tracksQuery = useQuery({
    queryKey: ['tracks', 'folder', selectedId],
    queryFn: () => api.tracks.list({ folderId: selectedId!, pageSize: 200, sort: 'title' }),
    enabled: Boolean(selectedId),
  });

  const searchQuery = useQuery({
    queryKey: ['tracks', 'folder-add', debouncedTrackSearch],
    queryFn: () => api.tracks.list({ q: debouncedTrackSearch || undefined, pageSize: 20, sort: 'title' }),
    enabled: addTracksOpen,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['folders'] });
    void queryClient.invalidateQueries({ queryKey: ['tracks'] });
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const saveMutation = useMutation({
    mutationFn: async (payload: { id: string | null; body: { name: string; parentId?: string | null; description?: string | null } }) => {
      if (payload.id) await api.folders.update(payload.id, payload.body);
      else await api.folders.create(payload.body);
    },
    onSuccess: () => {
      invalidate();
      notify(editing ? t('folders.updated') : t('folders.created'), 'success');
      setFormOpen(false);
      setEditing(null);
      setForm(EMPTY_FORM);
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const deleteMutation = useMutation({
    mutationFn: (folder: FolderNode) => api.folders.remove(folder.id),
    onSuccess: () => {
      invalidate();
      notify(t('folders.deleted'), 'success');
      if (deleting && deleting.id === selectedId) setSelectedId(null);
      setDeleting(null);
    },
    onError: (error) => {
      if (error instanceof ApiError && error.status === 409) notify(t('folders.deleteBlocked'), 'error');
      else notify(errorMessage(error, t('common.error')), 'error');
      setDeleting(null);
    },
  });

  const addTracksMutation = useMutation({
    mutationFn: ({ folderId, trackIds }: { folderId: string; trackIds: string[] }) =>
      api.folders.addTracks(folderId, trackIds),
    onSuccess: (_data, variables) => {
      invalidate();
      notify(t('folders.addedTracks', { count: formatNumber(variables.trackIds.length) }), 'success');
      setCheckedTracks(new Set());
      setAddTracksOpen(false);
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const removeTrackMutation = useMutation({
    mutationFn: ({ folderId, trackId }: { folderId: string; trackId: string }) =>
      api.folders.removeTrack(folderId, trackId),
    onSuccess: () => {
      invalidate();
      notify(t('folders.updated'), 'success');
      setRemovingTrack(null);
    },
    onError: (error) => {
      notify(errorMessage(error, t('common.error')), 'error');
      setRemovingTrack(null);
    },
  });

  useEffect(() => {
    if (selectedId && foldersQuery.data && !flatFolders.some((entry) => entry.node.id === selectedId)) {
      setSelectedId(null);
    }
  }, [flatFolders, foldersQuery.data, selectedId]);

  const openCreate = (parentId: string | null = null) => {
    setEditing(null);
    setForm({ name: '', parentId: parentId ?? '', description: '' });
    setFormError(null);
    setFormOpen(true);
  };

  const openEdit = (folder: FolderNode) => {
    setEditing(folder);
    setForm({ name: folder.name, parentId: folder.parentId ?? '', description: folder.description ?? '' });
    setFormError(null);
    setFormOpen(true);
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!form.name.trim()) {
      setFormError(t('common.requiredField'));
      return;
    }
    if (editing) {
      saveMutation.mutate({
        id: editing.id,
        body: { name: form.name.trim(), description: form.description.trim() },
      });
    } else {
      saveMutation.mutate({
        id: null,
        body: {
          name: form.name.trim(),
          parentId: form.parentId || null,
          description: form.description.trim(),
        },
      });
    }
  };

  const existingIds = new Set(tracksQuery.data?.items.map((track) => track.id) ?? []);
  const addableTracks = (searchQuery.data?.items ?? []).filter((track) => !existingIds.has(track.id));

  return (
    <div className="page">
      <PageHeader
        title={t('folders.title')}
        subtitle={t('folders.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={foldersQuery.isFetching} onClick={() => void foldersQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            {canContribute ? (
              <Button variant="primary" icon="plus" onClick={() => openCreate(null)}>
                {t('folders.create')}
              </Button>
            ) : null}
          </>
        }
      />

      {foldersQuery.isLoading ? <LoadingState /> : null}
      {foldersQuery.isError ? (
        <ErrorState error={foldersQuery.error} title={t('folders.loadFailed')} onRetry={() => void foldersQuery.refetch()} />
      ) : null}

      {foldersQuery.data ? (
        foldersQuery.data.length === 0 ? (
          <EmptyState
            title={t('folders.empty')}
            body={t('folders.emptyBody')}
            icon="folders"
            action={
              canContribute ? (
                <Button variant="primary" icon="plus" onClick={() => openCreate(null)}>
                  {t('folders.create')}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <div className="split-view">
            <section className="panel tree-panel" aria-label={t('folders.tree')}>
              <div className="panel-header">
                <h2>{t('folders.tree')}</h2>
              </div>
              <ul className="tree" role="tree">
                {foldersQuery.data.map((node) => (
                  <FolderTreeNode
                    key={node.id}
                    node={node}
                    depth={0}
                    selectedId={selectedId}
                    onSelect={setSelectedId}
                    canEdit={canContribute}
                    canDelete={isAdmin}
                    onEdit={openEdit}
                    onDelete={setDeleting}
                    onCreateChild={(folder) => openCreate(folder.id)}
                  />
                ))}
              </ul>
            </section>

            <section className="panel" aria-label={selected ? t('folders.tracksIn', { name: selected.name }) : t('folders.select')}>
              {selected ? (
                <>
                  <div className="panel-header">
                    <div>
                      <h2>{t('folders.tracksIn', { name: selected.name })}</h2>
                      <p className="muted small">
                        {t('folders.trackCount', { count: formatNumber(selected.trackCount) })}
                        {selected.description ? ` · ${selected.description}` : ''}
                      </p>
                    </div>
                    <div className="row-actions">
                      {canContribute ? (
                        <Button size="sm" icon="plus" onClick={() => { setCheckedTracks(new Set()); setAddTracksOpen(true); }}>
                          {t('folders.addTracks')}
                        </Button>
                      ) : null}
                      {canContribute ? (
                        <IconButton icon="edit" label={t('common.edit')} onClick={() => openEdit(selected)} />
                      ) : null}
                      {isAdmin ? (
                        <IconButton icon="trash" label={t('common.delete')} onClick={() => setDeleting(selected)} />
                      ) : null}
                    </div>
                  </div>
                  {tracksQuery.isLoading ? <LoadingState /> : null}
                  {tracksQuery.isError ? (
                    <ErrorState error={tracksQuery.error} onRetry={() => void tracksQuery.refetch()} />
                  ) : null}
                  {tracksQuery.data ? (
                    <TrackTable
                      tracks={tracksQuery.data.items}
                      showAdded={false}
                      emptyTitle={t('folders.noTracks')}
                      actions={(track) =>
                        canContribute ? (
                          <IconButton icon="trash" label={t('folders.removeTrack')} onClick={() => setRemovingTrack(track)} />
                        ) : null
                      }
                    />
                  ) : null}
                </>
              ) : (
                <EmptyState title={t('folders.select')} icon="folders" />
              )}
            </section>
          </div>
        )
      ) : null}

      <Modal
        open={formOpen}
        title={editing ? t('folders.edit') : t('folders.create')}
        onClose={() => setFormOpen(false)}
        footer={
          <>
            <Button onClick={() => setFormOpen(false)}>{t('common.cancel')}</Button>
            <Button type="submit" form="folder-form" variant="primary" loading={saveMutation.isPending}>
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
        <form id="folder-form" className="form" onSubmit={submit} noValidate>
          <Field label={t('folders.name')} htmlFor="folder-name" required>
            <Input
              id="folder-name"
              value={form.name}
              onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
              required
            />
          </Field>
          {editing ? (
            <p className="muted small">{t('folders.parentRootUnsupported')}</p>
          ) : (
            <Field label={t('folders.parent')} htmlFor="folder-parent">
              <Select
                id="folder-parent"
                value={form.parentId}
                onChange={(event) => setForm((current) => ({ ...current, parentId: event.target.value }))}
                options={[
                  { value: '', label: t('folders.noParent') },
                  ...flatFolders.map(({ node, depth }) => ({
                    value: node.id,
                    label: `${'—'.repeat(depth)} ${node.name}`.trim(),
                  })),
                ]}
              />
            </Field>
          )}
          <Field label={t('folders.description')} htmlFor="folder-description">
            <Textarea
              id="folder-description"
              rows={3}
              value={form.description}
              onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
            />
          </Field>
        </form>
      </Modal>

      <Modal
        open={addTracksOpen && selected !== null}
        title={t('folders.addTracksTitle', { name: selected?.name ?? '' })}
        onClose={() => setAddTracksOpen(false)}
        size="lg"
        footer={
          <>
            <Button onClick={() => setAddTracksOpen(false)}>{t('common.cancel')}</Button>
            <Button
              variant="primary"
              disabled={checkedTracks.size === 0}
              loading={addTracksMutation.isPending}
              onClick={() => {
                if (selected) addTracksMutation.mutate({ folderId: selected.id, trackIds: [...checkedTracks] });
              }}
            >
              {t('folders.addTracks')}
            </Button>
          </>
        }
      >
        <div className="search-field">
          <Icon name="search" size={18} />
          <Input
            type="search"
            value={trackSearch}
            onChange={(event) => setTrackSearch(event.target.value)}
            placeholder={t('folders.searchTracks')}
            aria-label={t('folders.searchTracks')}
          />
        </div>
        {searchQuery.isLoading ? <LoadingState /> : null}
        {searchQuery.isError ? (
          <ErrorState error={searchQuery.error} onRetry={() => void searchQuery.refetch()} />
        ) : null}
        {searchQuery.data ? (
          addableTracks.length === 0 ? (
            <p className="muted">{t('common.noResults')}</p>
          ) : (
            <ul className="select-list">
              {addableTracks.map((track) => (
                <li key={track.id} className="select-row">
                  <Checkbox
                    label=""
                    aria-label={track.title}
                    checked={checkedTracks.has(track.id)}
                    onChange={(event) => {
                      setCheckedTracks((current) => {
                        const next = new Set(current);
                        if (event.target.checked) next.add(track.id);
                        else next.delete(track.id);
                        return next;
                      });
                    }}
                  />
                  <Artwork url={track.artworkUrl} alt="" size={34} />
                  <div className="select-row-text">
                    <span className="table-title">{track.title}</span>
                    <span className="table-sub">
                      {track.artist || '—'} · {track.album || '—'}
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          )
        ) : null}
      </Modal>

      <ConfirmDialog
        open={deleting !== null}
        title={t('folders.deleteTitle')}
        body={`${t('folders.deleteBody', { name: deleting?.name ?? '' })} ${t('folders.deleteBlocked')}`}
        confirmLabel={t('common.delete')}
        busy={deleteMutation.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          if (deleting) deleteMutation.mutate(deleting);
        }}
      />

      <ConfirmDialog
        open={removingTrack !== null}
        title={t('folders.removeTrack')}
        body={t('folders.removeConfirm', { title: removingTrack?.title ?? '' })}
        confirmLabel={t('common.delete')}
        busy={removeTrackMutation.isPending}
        onCancel={() => setRemovingTrack(null)}
        onConfirm={() => {
          if (removingTrack && selected) {
            removeTrackMutation.mutate({ folderId: selected.id, trackId: removingTrack.id });
          }
        }}
      />
    </div>
  );
}

function FolderTreeNode({
  node,
  depth,
  selectedId,
  onSelect,
  canEdit,
  canDelete,
  onEdit,
  onDelete,
  onCreateChild,
}: {
  node: FolderNode;
  depth: number;
  selectedId: string | null;
  onSelect: (id: string) => void;
  canEdit: boolean;
  canDelete: boolean;
  onEdit: (folder: FolderNode) => void;
  onDelete: (folder: FolderNode) => void;
  onCreateChild: (folder: FolderNode) => void;
}) {
  const { t, formatNumber } = useUi();
  const [expanded, setExpanded] = useState(depth === 0);
  const hasChildren = node.children.length > 0;

  return (
    <li role="treeitem" aria-expanded={hasChildren ? expanded : undefined} aria-selected={selectedId === node.id}>
      <div className={`tree-row${selectedId === node.id ? ' is-selected' : ''}`} style={{ paddingInlineStart: `${depth * 18}px` }}>
        {hasChildren ? (
          <button
            type="button"
            className="tree-toggle"
            aria-label={expanded ? t('a11y.collapseFolder') : t('a11y.expandFolder')}
            aria-expanded={expanded}
            onClick={() => setExpanded((value) => !value)}
          >
            <Icon name={expanded ? 'chevron-down' : 'chevron-right'} size={16} />
          </button>
        ) : (
          <span className="tree-toggle-placeholder" aria-hidden="true" />
        )}
        <button type="button" className="tree-select" onClick={() => onSelect(node.id)}>
          <Icon name="folders" size={16} />
          <span className="tree-name">{node.name}</span>
          <Badge>{formatNumber(node.trackCount)}</Badge>
        </button>
        <span className="row-actions">
          {canEdit ? (
            <>
              <IconButton icon="plus" label={t('folders.create')} onClick={() => onCreateChild(node)} />
              <IconButton icon="edit" label={t('common.edit')} onClick={() => onEdit(node)} />
            </>
          ) : null}
          {canDelete ? <IconButton icon="trash" label={t('common.delete')} onClick={() => onDelete(node)} /> : null}
        </span>
      </div>
      {hasChildren && expanded ? (
        <ul role="group">
          {node.children.map((child) => (
            <FolderTreeNode
              key={child.id}
              node={child}
              depth={depth + 1}
              selectedId={selectedId}
              onSelect={onSelect}
              canEdit={canEdit}
              canDelete={canDelete}
              onEdit={onEdit}
              onDelete={onDelete}
              onCreateChild={onCreateChild}
            />
          ))}
        </ul>
      ) : null}
    </li>
  );
}
