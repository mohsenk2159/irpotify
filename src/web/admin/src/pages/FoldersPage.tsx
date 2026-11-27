import { useEffect, useMemo, useRef, useState, type DragEvent, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, uploadTracks } from '../api/endpoints';
import { ApiError } from '../api/http';
import type { FolderNode, Track, TrackQuery } from '../api/types';
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
} from '../ui/primitives';
import { Artwork } from '../ui/Artwork';
import { TrackTable } from '../ui/TrackTable';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

const MAX_FILES = 25;
const AUDIO_ACCEPT = '.mp3,.flac,.wav,.m4a,.aac,.ogg,.opus,audio/*';

const FOLDER_TRACK_SORT_OPTIONS: { value: NonNullable<TrackQuery['sort']>; labelKey: TranslationKey }[] = [
  { value: 'title', labelKey: 'folders.sortTitle' },
  { value: 'artist', labelKey: 'folders.sortArtist' },
  { value: 'duration', labelKey: 'folders.sortDuration' },
  { value: 'newest', labelKey: 'folders.sortNewest' },
  { value: 'oldest', labelKey: 'folders.sortOldest' },
];

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

function getFolderPath(nodes: FolderNode[], targetId: string, currentPath: string[] = []): string[] | null {
  for (const node of nodes) {
    const nextPath = [...currentPath, node.name];
    if (node.id === targetId) return nextPath;
    const childPath = getFolderPath(node.children, targetId, nextPath);
    if (childPath) return childPath;
  }
  return null;
}

export function FoldersPage() {
  const { t, formatBytes, formatNumber } = useUi();
  const { isAdmin, canContribute } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [folderSort, setFolderSort] = useState<NonNullable<TrackQuery['sort']>>('title');
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<FolderNode | null>(null);
  const [form, setForm] = useState<FolderForm>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<FolderNode | null>(null);

  // Upload to folder state
  const uploadInputRef = useRef<HTMLInputElement | null>(null);
  const uploadFolderInputRef = useRef<HTMLInputElement | null>(null);
  const uploadAbortRef = useRef<AbortController | null>(null);
  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadQueue, setUploadQueue] = useState<File[]>([]);
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [uploadDragging, setUploadDragging] = useState(false);

  // Add tracks to folder state
  const [addTracksOpen, setAddTracksOpen] = useState(false);
  const [showAllTracks, setShowAllTracks] = useState(false);
  const [trackSearch, setTrackSearch] = useState('');
  const debouncedTrackSearch = useDebounced(trackSearch.trim(), 350);
  const [checkedTracks, setCheckedTracks] = useState<Set<string>>(new Set());
  const [removingTrack, setRemovingTrack] = useState<Track | null>(null);

  const foldersQuery = useQuery({ queryKey: ['folders'], queryFn: api.folders.list });
  const flatFolders = useMemo(() => flattenFolders(foldersQuery.data ?? []), [foldersQuery.data]);
  const selected = flatFolders.find((entry) => entry.node.id === selectedId)?.node ?? null;

  const selectedFolderPath = useMemo(() => {
    if (!selectedId || !foldersQuery.data) return '';
    const path = getFolderPath(foldersQuery.data, selectedId);
    return path ? path.join(' / ') : '';
  }, [foldersQuery.data, selectedId]);

  const tracksQuery = useQuery({
    queryKey: ['tracks', 'folder', selectedId, folderSort],
    queryFn: () => api.tracks.list({ folderId: selectedId!, pageSize: 200, sort: folderSort }),
    enabled: Boolean(selectedId),
  });

  const searchQuery = useQuery({
    queryKey: ['tracks', 'folder-add', debouncedTrackSearch, showAllTracks],
    queryFn: () =>
      api.tracks.list({
        q: debouncedTrackSearch || undefined,
        withoutFolder: !showAllTracks ? true : undefined,
        pageSize: 50,
        sort: 'title',
      }),
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

  const isAudioFile = (file: File) =>
    file.type.startsWith('audio/') || /\.(mp3|flac|wav|m4a|aac|ogg|opus)$/i.test(file.name);

  const addUploadFiles = (files: FileList | File[]) => {
    const incoming = Array.from(files).filter(isAudioFile);
    if (incoming.length === 0) return;
    setUploadError(null);
    setUploadQueue((current) => {
      const next = [...current];
      for (const file of incoming) {
        if (next.length >= MAX_FILES) break;
        if (!next.some((existing) => existing.name === file.name && existing.size === file.size)) next.push(file);
      }
      if (current.length + incoming.length > MAX_FILES) notify(t('upload.tooMany'), 'error');
      return next;
    });
  };

  const readEntryFiles = async (entry: FileSystemEntry): Promise<File[]> => {
    if (entry.isFile) {
      return new Promise((resolve) => {
        (entry as FileSystemFileEntry).file(
          (file) => resolve([file]),
          () => resolve([]),
        );
      });
    }
    if (entry.isDirectory) {
      const reader = (entry as FileSystemDirectoryEntry).createReader();
      const entries: FileSystemEntry[] = [];
      while (true) {
        const batch = await new Promise<FileSystemEntry[]>((resolve) => {
          reader.readEntries(resolve, () => resolve([]));
        });
        if (batch.length === 0) break;
        entries.push(...batch);
      }
      const nested = await Promise.all(entries.map(readEntryFiles));
      return nested.flat();
    }
    return [];
  };

  const onUploadDrop = async (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setUploadDragging(false);
    const items = event.dataTransfer.items;
    if (items && items.length > 0 && typeof items[0]?.webkitGetAsEntry === 'function') {
      const entries = Array.from(items)
        .map((item) => item.webkitGetAsEntry())
        .filter((e): e is FileSystemEntry => e !== null);
      const allFiles = (await Promise.all(entries.map(readEntryFiles))).flat();
      if (allFiles.length > 0) {
        addUploadFiles(allFiles);
        return;
      }
    }
    addUploadFiles(event.dataTransfer.files);
  };

  const uploadMutation = useMutation({
    mutationFn: async () => {
      if (!selected) return null;
      const controller = new AbortController();
      uploadAbortRef.current = controller;
      setUploadProgress(0);
      try {
        return await uploadTracks(uploadQueue, {
          folderId: selected.id,
          onProgress: setUploadProgress,
          signal: controller.signal,
        });
      } finally {
        uploadAbortRef.current = null;
      }
    },
    onSuccess: (response) => {
      if (!response) return;
      const ok = response.results.filter((result) => result.success && !result.duplicate).length;
      const duplicates = response.results.filter((result) => result.success && result.duplicate).length;
      const failed = response.results.filter((result) => !result.success).length;
      setUploadQueue([]);
      setUploadOpen(false);
      invalidate();
      if (failed > 0) {
        notify(t('upload.done', { ok, duplicates, failed }), 'error');
      } else {
        notify(
          t('folders.uploadSuccess', {
            name: selected?.name ?? '',
            count: formatNumber(ok + duplicates),
          }),
          'success',
        );
      }
    },
    onError: (error) => {
      if (error instanceof DOMException && error.name === 'AbortError') {
        notify(t('upload.cancelled'), 'info');
      } else {
        setUploadError(errorMessage(error, t('common.error')));
      }
    },
    onSettled: () => setUploadProgress(null),
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
                        {selectedFolderPath ? `${selectedFolderPath} · ` : ''}
                        {t('folders.trackCount', { count: formatNumber(selected.trackCount) })}
                        {selected.description ? ` · ${selected.description}` : ''}
                      </p>
                    </div>
                    <div className="row-actions" style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                      <Select
                        value={folderSort}
                        onChange={(e) => setFolderSort(e.target.value as NonNullable<TrackQuery['sort']>)}
                        aria-label={t('folders.sort')}
                        style={{ width: 'auto', minWidth: '130px', fontSize: '0.85rem', padding: '4px 8px' }}
                      >
                        {FOLDER_TRACK_SORT_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>
                            {t(opt.labelKey)}
                          </option>
                        ))}
                      </Select>
                      {canContribute ? (
                        <>
                          <Button
                            size="sm"
                            variant="primary"
                            icon="upload"
                            onClick={() => {
                              setUploadQueue([]);
                              setUploadError(null);
                              setUploadProgress(null);
                              setUploadOpen(true);
                            }}
                          >
                            {t('folders.uploadMusic')}
                          </Button>
                          <Button
                            size="sm"
                            icon="plus"
                            onClick={() => {
                              setCheckedTracks(new Set());
                              setTrackSearch('');
                              setShowAllTracks(false);
                              setAddTracksOpen(true);
                            }}
                          >
                            {t('folders.addTracks')}
                          </Button>
                        </>
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
                    tracksQuery.data.items.length === 0 ? (
                      <EmptyState
                        title={t('folders.noTracks')}
                        icon="tracks"
                        action={
                          canContribute ? (
                            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', justifyContent: 'center' }}>
                              <Button
                                variant="primary"
                                icon="upload"
                                onClick={() => {
                                  setUploadQueue([]);
                                  setUploadError(null);
                                  setUploadProgress(null);
                                  setUploadOpen(true);
                                }}
                              >
                                {t('folders.uploadMusic')}
                              </Button>
                              <Button
                                icon="plus"
                                onClick={() => {
                                  setCheckedTracks(new Set());
                                  setTrackSearch('');
                                  setShowAllTracks(false);
                                  setAddTracksOpen(true);
                                }}
                              >
                                {t('folders.addTracks')}
                              </Button>
                            </div>
                          ) : undefined
                        }
                      />
                    ) : (
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
                    )
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

      {/* Upload to Folder Modal */}
      <Modal
        open={uploadOpen && selected !== null}
        title={t('folders.uploadTitle', { name: selected?.name ?? '' })}
        onClose={() => {
          if (!uploadMutation.isPending) {
            setUploadOpen(false);
            setUploadQueue([]);
            setUploadError(null);
          }
        }}
        size="lg"
        footer={
          <>
            <Button
              onClick={() => {
                if (uploadMutation.isPending) {
                  uploadAbortRef.current?.abort();
                } else {
                  setUploadOpen(false);
                  setUploadQueue([]);
                  setUploadError(null);
                }
              }}
            >
              {uploadMutation.isPending ? t('upload.cancel') : t('common.cancel')}
            </Button>
            <Button
              variant="primary"
              icon="upload"
              disabled={uploadQueue.length === 0 || uploadMutation.isPending}
              loading={uploadMutation.isPending}
              onClick={() => uploadMutation.mutate()}
            >
              {uploadMutation.isPending
                ? t('folders.uploading', { percent: uploadProgress ?? 0 })
                : t('upload.start', { count: formatNumber(uploadQueue.length) })}
            </Button>
          </>
        }
      >
        <div style={{ display: 'grid', gap: '16px' }}>
          {selectedFolderPath ? (
            <div className="card-sub" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <Icon name="folders" size={16} />
              <span>
                <strong>{t('folders.uploadDestination')}:</strong> {selectedFolderPath}
              </span>
            </div>
          ) : null}

          {uploadError ? (
            <div className="notice notice-error" role="alert">
              {uploadError}
            </div>
          ) : null}

          {uploadProgress !== null ? (
            <div className="progress-block">
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '13px' }}>
                <span>{t('folders.uploading', { percent: uploadProgress })}</span>
                <span>{uploadProgress}%</span>
              </div>
              <div className="progress" role="progressbar" aria-valuenow={uploadProgress} aria-valuemin={0} aria-valuemax={100}>
                <span style={{ width: `${uploadProgress}%` }} />
              </div>
            </div>
          ) : null}

          <div
            className={`dropzone${uploadDragging ? ' is-dragging' : ''}`}
            onDragOver={(event) => {
              event.preventDefault();
              setUploadDragging(true);
            }}
            onDragLeave={() => setUploadDragging(false)}
            onDrop={onUploadDrop}
          >
            <span className="dropzone-icon" aria-hidden="true">
              <Icon name="upload" size={28} />
            </span>
            <p className="dropzone-title">{t('upload.dropzone')}</p>
            <p className="muted small">{t('folders.uploadHint')}</p>
            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', justifyContent: 'center' }}>
              <Button
                variant="secondary"
                icon="upload"
                disabled={uploadMutation.isPending}
                onClick={() => uploadInputRef.current?.click()}
              >
                {t('upload.browse')}
              </Button>
              <Button
                variant="ghost"
                icon="folders"
                disabled={uploadMutation.isPending}
                onClick={() => uploadFolderInputRef.current?.click()}
              >
                انتخاب پوشه (Folder)
              </Button>
            </div>
            <input
              ref={uploadInputRef}
              type="file"
              multiple
              accept={AUDIO_ACCEPT}
              className="sr-only"
              aria-label={t('upload.browse')}
              onChange={(event) => {
                if (event.target.files) addUploadFiles(event.target.files);
                event.target.value = '';
              }}
            />
            <input
              ref={uploadFolderInputRef}
              type="file"
              multiple
              {...({ webkitdirectory: '', directory: '' } as Record<string, string>)}
              className="sr-only"
              aria-label="انتخاب پوشه"
              onChange={(event) => {
                if (event.target.files) addUploadFiles(event.target.files);
                event.target.value = '';
              }}
            />
          </div>

          {uploadQueue.length > 0 ? (
            <div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                <span className="muted small">
                  {t('folders.uploadFilesSelected', { count: formatNumber(uploadQueue.length) })} (max {MAX_FILES})
                </span>
                {!uploadMutation.isPending ? (
                  <Button size="sm" onClick={() => setUploadQueue([])}>
                    {t('upload.clear')}
                  </Button>
                ) : null}
              </div>
              <ul className="upload-queue" style={{ maxHeight: '200px', overflowY: 'auto' }}>
                {uploadQueue.map((file, index) => (
                  <li key={`${file.name}-${file.size}-${index}`}>
                    <Icon name="music" size={16} />
                    <span className="upload-file-name" title={file.name}>
                      {file.name}
                    </span>
                    <span className="muted small">{formatBytes(file.size)}</span>
                    {!uploadMutation.isPending ? (
                      <IconButton
                        icon="trash"
                        label={t('upload.remove')}
                        onClick={() => setUploadQueue((prev) => prev.filter((_, i) => i !== index))}
                      />
                    ) : null}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      </Modal>

      {/* Add Existing Tracks to Folder Modal */}
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
              {checkedTracks.size > 0
                ? `${t('folders.addTracks')} (${formatNumber(checkedTracks.size)})`
                : t('folders.addTracks')}
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

        <div style={{ marginBlock: '10px' }}>
          <Checkbox
            label={t('folders.showAllTracks')}
            description={t('folders.showAllTracksHint')}
            checked={showAllTracks}
            onChange={(event) => {
              setShowAllTracks(event.target.checked);
              setCheckedTracks(new Set());
            }}
          />
        </div>

        {searchQuery.isLoading ? <LoadingState /> : null}
        {searchQuery.isError ? (
          <ErrorState error={searchQuery.error} onRetry={() => void searchQuery.refetch()} />
        ) : null}
        {searchQuery.data ? (
          addableTracks.length === 0 ? (
            <p className="muted" style={{ padding: '16px 0', textAlign: 'center' }}>
              {t('common.noResults')}
            </p>
          ) : (
            <>
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '6px 4px',
                  borderBottom: '1px solid var(--border)',
                  marginBottom: '8px',
                }}
              >
                <Checkbox
                  label={
                    checkedTracks.size === addableTracks.length && addableTracks.length > 0
                      ? t('common.none')
                      : t('common.selectAll')
                  }
                  checked={addableTracks.length > 0 && checkedTracks.size === addableTracks.length}
                  onChange={(event) => {
                    if (event.target.checked) {
                      setCheckedTracks(new Set(addableTracks.map((tr) => tr.id)));
                    } else {
                      setCheckedTracks(new Set());
                    }
                  }}
                />
                <span className="muted small">
                  {t('common.selectedCount', { count: formatNumber(checkedTracks.size) })} / {formatNumber(addableTracks.length)}
                </span>
              </div>
              <ul className="select-list" style={{ maxHeight: '340px', overflowY: 'auto' }}>
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
            </>
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
