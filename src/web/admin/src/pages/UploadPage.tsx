import { useMemo, useRef, useState, type DragEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, uploadTracks } from '../api/endpoints';
import type { UploadFileResult } from '../api/types';
import { useUi } from '../ui/UiProvider';
import { useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Field,
  LoadingState,
  PageHeader,
  Select,
  StatusBadge,
  Toolbar,
} from '../ui/primitives';
import { Pagination } from '../ui/Pagination';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

const MAX_FILES = 25;
const AUDIO_ACCEPT = '.mp3,.flac,.wav,.m4a,.aac,.ogg,.opus,audio/*';

export function UploadPage() {
  const { t, formatBytes, formatDate, formatNumber } = useUi();
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const [queue, setQueue] = useState<File[]>([]);
  const [folderId, setFolderId] = useState('');
  const [albumId, setAlbumId] = useState('');
  const [progress, setProgress] = useState<number | null>(null);
  const [results, setResults] = useState<UploadFileResult[] | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const foldersQuery = useQuery({ queryKey: ['folders'], queryFn: api.folders.list });
  const albumsQuery = useQuery({
    queryKey: ['albums', 'upload-options'],
    queryFn: () => api.albums.list({ page: 1, pageSize: 200 }),
  });
  const historyQuery = useQuery({
    queryKey: ['uploads', 'history', page, pageSize],
    queryFn: () => api.uploads.history({ page, pageSize }),
    placeholderData: (previous) => previous,
  });

  const folderOptions = useMemo(() => {
    const options = [{ value: '', label: t('upload.noFolder') }];
    const walk = (nodes: typeof foldersQuery.data, depth: number) => {
      for (const node of nodes ?? []) {
        options.push({ value: node.id, label: `${'— '.repeat(depth)}${node.name}` });
        walk(node.children, depth + 1);
      }
    };
    walk(foldersQuery.data, 0);
    return options;
  }, [foldersQuery.data, t]);

  const albumOptions = useMemo(
    () => [
      { value: '', label: t('upload.noAlbum') },
      ...(albumsQuery.data?.items ?? []).map((album) => ({ value: album.id, label: album.title })),
    ],
    [albumsQuery.data, t],
  );

  const addFiles = (files: FileList | File[]) => {
    const incoming = Array.from(files);
    if (incoming.length === 0) return;
    setResults(null);
    setUploadError(null);
    setQueue((current) => {
      const next = [...current];
      for (const file of incoming) {
        if (next.length >= MAX_FILES) break;
        if (!next.some((existing) => existing.name === file.name && existing.size === file.size)) next.push(file);
      }
      if (current.length + incoming.length > MAX_FILES) notify(t('upload.tooMany'), 'error');
      return next;
    });
  };

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    addFiles(event.dataTransfer.files);
  };

  const uploadMutation = useMutation({
    mutationFn: async () => {
      const controller = new AbortController();
      abortRef.current = controller;
      setProgress(0);
      try {
        return await uploadTracks(queue, {
          folderId: folderId || null,
          albumId: albumId || null,
          onProgress: setProgress,
          signal: controller.signal,
        });
      } finally {
        abortRef.current = null;
      }
    },
    onSuccess: (response) => {
      setResults(response.results);
      const ok = response.results.filter((result) => result.success && !result.duplicate).length;
      const duplicates = response.results.filter((result) => result.success && result.duplicate).length;
      const failed = response.results.filter((result) => !result.success).length;
      setQueue([]);
      notify(t('upload.done', { ok, duplicates, failed }), failed > 0 ? 'error' : 'success');
      void queryClient.invalidateQueries({ queryKey: ['uploads', 'history'] });
      void queryClient.invalidateQueries({ queryKey: ['tracks'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
    onError: (error) => {
      if (error instanceof DOMException && error.name === 'AbortError') {
        notify(t('upload.cancelled'), 'info');
      } else {
        setUploadError(errorMessage(error, t('common.error')));
      }
    },
    onSettled: () => setProgress(null),
  });

  const cancel = () => abortRef.current?.abort();

  const history = historyQuery.data?.items ?? [];
  const total = historyQuery.data?.total ?? 0;

  return (
    <div className="page">
      <PageHeader
        title={t('upload.title')}
        subtitle={t('upload.subtitle')}
        actions={
          <Button icon="refresh" loading={historyQuery.isFetching} onClick={() => void historyQuery.refetch()}>
            {t('common.refresh')}
          </Button>
        }
      />

      <div className="upload-grid">
        <div
          className={`dropzone${dragging ? ' is-dragging' : ''}`}
          onDragOver={(event) => {
            event.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
        >
          <span className="dropzone-icon" aria-hidden="true">
            <Icon name="upload" size={30} />
          </span>
          <p className="dropzone-title">{t('upload.dropzone')}</p>
          <p className="muted small">{t('upload.hint')}</p>
          <Button variant="primary" icon="upload" onClick={() => inputRef.current?.click()}>
            {t('upload.browse')}
          </Button>
          <input
            ref={inputRef}
            type="file"
            multiple
            accept={AUDIO_ACCEPT}
            className="sr-only"
            aria-label={t('upload.browse')}
            onChange={(event) => {
              if (event.target.files) addFiles(event.target.files);
              event.target.value = '';
            }}
          />
        </div>

        <div className="upload-options">
          <Field label={t('upload.folder')} htmlFor="upload-folder">
            <Select
              id="upload-folder"
              options={folderOptions}
              value={folderId}
              disabled={foldersQuery.isLoading}
              onChange={(event) => setFolderId(event.target.value)}
            />
          </Field>
          <Field label={t('upload.album')} htmlFor="upload-album">
            <Select
              id="upload-album"
              options={albumOptions}
              value={albumId}
              disabled={albumsQuery.isLoading}
              onChange={(event) => setAlbumId(event.target.value)}
            />
          </Field>
        </div>
      </div>

      <section className="card">
        <div className="card-header">
          <h2 className="card-title">{t('upload.queue')}</h2>
          <div className="card-actions">
            <Badge>{formatNumber(queue.length)} / {formatNumber(MAX_FILES)}</Badge>
            <Button size="sm" variant="ghost" disabled={queue.length === 0 || uploadMutation.isPending} onClick={() => setQueue([])}>
              {t('upload.clear')}
            </Button>
          </div>
        </div>
        <div className="card-body">
          {queue.length === 0 ? (
            <p className="muted">{t('upload.emptyQueue')}</p>
          ) : (
            <ul className="upload-queue">
              {queue.map((file) => (
                <li key={`${file.name}-${file.size}`}>
                  <span className="upload-file-name" title={file.name}>
                    {file.name}
                  </span>
                  <span className="muted small">{formatBytes(file.size)}</span>
                  <Button
                    size="sm"
                    variant="ghost"
                    icon="close"
                    aria-label={t('upload.remove')}
                    disabled={uploadMutation.isPending}
                    onClick={() => setQueue((current) => current.filter((item) => item !== file))}
                  />
                </li>
              ))}
            </ul>
          )}

          {progress !== null ? (
            <div className="progress-block">
              <div
                className="progress"
                role="progressbar"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={progress}
                aria-label={t('upload.uploading', { percent: progress })}
              >
                <span style={{ width: `${progress}%` }} />
              </div>
              <span className="muted small">{t('upload.uploading', { percent: progress })}</span>
            </div>
          ) : null}

          {uploadError ? (
            <div className="notice notice-error" role="alert">
              {uploadError}
            </div>
          ) : null}

          <Toolbar>
            <Button
              variant="primary"
              icon="upload"
              disabled={queue.length === 0 || uploadMutation.isPending}
              loading={uploadMutation.isPending}
              onClick={() => uploadMutation.mutate()}
            >
              {queue.length > 0 ? t('upload.start', { count: queue.length }) : t('upload.start', { count: 0 })}
            </Button>
            {uploadMutation.isPending ? (
              <Button variant="danger" onClick={cancel}>
                {t('upload.cancel')}
              </Button>
            ) : null}
          </Toolbar>
        </div>
      </section>

      {results ? (
        <section className="card">
          <div className="card-header">
            <h2 className="card-title">{t('upload.results')}</h2>
          </div>
          <div className="card-body">
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col">{t('upload.historyFile')}</th>
                    <th scope="col">{t('common.status')}</th>
                    <th scope="col">{t('upload.historyError')}</th>
                  </tr>
                </thead>
                <tbody>
                  {results.map((result, index) => (
                    <tr key={`${result.fileName}-${index}`}>
                      <td>{result.fileName}</td>
                      <td>
                        {result.success ? (
                          result.duplicate ? (
                            <Badge tone="warning">{t('upload.duplicate')}</Badge>
                          ) : (
                            <Badge tone="success">{t('upload.uploaded')}</Badge>
                          )
                        ) : (
                          <Badge tone="danger">{t('upload.failed')}</Badge>
                        )}
                      </td>
                      <td className="muted small">{result.error ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      ) : null}

      <section className="card">
        <div className="card-header">
          <h2 className="card-title">{t('upload.history')}</h2>
        </div>
        <div className="card-body">
          {historyQuery.isLoading ? <LoadingState /> : null}
          {historyQuery.isError ? (
            <ErrorState error={historyQuery.error} title={t('upload.loadFailed')} onRetry={() => void historyQuery.refetch()} />
          ) : null}
          {historyQuery.data ? (
            history.length === 0 ? (
              <EmptyState icon="upload" title={t('upload.noHistory')} />
            ) : (
              <>
                <div className="table-wrap">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th scope="col">{t('upload.historyFile')}</th>
                        <th scope="col">{t('upload.historyStatus')}</th>
                        <th scope="col">{t('upload.historySize')}</th>
                        <th scope="col">{t('upload.historyCreated')}</th>
                        <th scope="col">{t('upload.historyCompleted')}</th>
                        <th scope="col">{t('upload.historyError')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {history.map((entry) => (
                        <tr key={entry.id}>
                          <td title={entry.originalFileName}>{entry.originalFileName}</td>
                          <td>
                            <StatusBadge status={entry.status} />
                          </td>
                          <td>{formatBytes(entry.receivedBytes || entry.expectedSizeBytes)}</td>
                          <td className="muted small">{formatDate(entry.createdAt)}</td>
                          <td className="muted small">{formatDate(entry.completedAt)}</td>
                          <td className="muted small">{entry.error ?? '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
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
            )
          ) : null}
        </div>
      </section>
    </div>
  );
}
