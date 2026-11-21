import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { ApiError } from '../api/http';
import type { TelegramConfigWrite, TelegramSummary } from '../api/types';
import { useUi } from '../ui/UiProvider';
import { useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  Card,
  Checkbox,
  EmptyState,
  ErrorState,
  Field,
  Input,
  LoadingState,
  PageHeader,
  Select,
  StatusBadge,
  Toolbar,
} from '../ui/primitives';
import { Pagination } from '../ui/Pagination';
import { errorMessage } from '../ui/helpers';

const MB = 1024 * 1024;
const PUBLICATION_STATUSES = ['Pending', 'Publishing', 'Published', 'Failed', 'DeadLetter'];

interface TelegramForm {
  token: string;
  clearToken: boolean;
  adminTelegramUserId: string;
  adminChatId: string;
  destinationGroupId: string;
  destinationTopicId: string;
  autoPublishEnabled: boolean;
  ingestionEnabled: boolean;
  maxFileMb: string;
  maxAudioDurationSeconds: string;
  apiEndpoint: string;
  localBotApiEndpoint: string;
  maxRetryAttempts: string;
}

function formFrom(config: TelegramSummary): TelegramForm {
  return {
    token: '',
    clearToken: false,
    adminTelegramUserId: config.adminTelegramUserId === null ? '' : String(config.adminTelegramUserId),
    adminChatId: config.adminChatId === null ? '' : String(config.adminChatId),
    destinationGroupId: config.destinationGroupId === null ? '' : String(config.destinationGroupId),
    destinationTopicId: config.destinationTopicId === null ? '' : String(config.destinationTopicId),
    autoPublishEnabled: config.autoPublishEnabled,
    ingestionEnabled: config.ingestionEnabled,
    maxFileMb: String(Math.round((config.maxIncomingFileSizeBytes / MB) * 100) / 100),
    maxAudioDurationSeconds: String(config.maxAudioDurationSeconds),
    apiEndpoint: config.apiEndpoint,
    localBotApiEndpoint: config.localBotApiEndpoint ?? '',
    maxRetryAttempts: String(config.maxRetryAttempts),
  };
}

function optionalInteger(value: string): number | undefined {
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  const parsed = Number(trimmed);
  return Number.isSafeInteger(parsed) ? parsed : Number.NaN;
}

export function TelegramPage() {
  const { t, formatNumber, formatDate, formatBytes } = useUi();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [form, setForm] = useState<TelegramForm | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);
  const [testFile, setTestFile] = useState<File | null>(null);
  const [testIngestMessage, setTestIngestMessage] = useState<string | null>(null);
  const [importsPage, setImportsPage] = useState(1);
  const [importsPageSize, setImportsPageSize] = useState(10);
  const [publicationsPage, setPublicationsPage] = useState(1);
  const [publicationsPageSize, setPublicationsPageSize] = useState(10);
  const [publicationStatus, setPublicationStatus] = useState('');

  const configQuery = useQuery({ queryKey: ['telegram', 'config'], queryFn: api.telegram.get });
  const importsQuery = useQuery({
    queryKey: ['telegram', 'imports', importsPage, importsPageSize],
    queryFn: () => api.telegram.imports({ page: importsPage, pageSize: importsPageSize }),
    placeholderData: (previous) => previous,
  });
  const publicationsQuery = useQuery({
    queryKey: ['telegram', 'publications', publicationStatus, publicationsPage, publicationsPageSize],
    queryFn: () =>
      api.telegram.publications({
        status: publicationStatus || undefined,
        page: publicationsPage,
        pageSize: publicationsPageSize,
      }),
    placeholderData: (previous) => previous,
  });
  const updatesQuery = useQuery({
    queryKey: ['telegram', 'updates'],
    queryFn: () => api.telegram.updates(50),
  });

  useEffect(() => {
    if (configQuery.data) setForm(formFrom(configQuery.data));
  }, [configQuery.data]);

  const statusOptions = useMemo(
    () => [
      { value: '', label: t('telegram.allStatuses') },
      ...PUBLICATION_STATUSES.map((status) => ({ value: status, label: status })),
    ],
    [t],
  );

  const invalidateAll = () => {
    void queryClient.invalidateQueries({ queryKey: ['telegram'] });
  };

  const saveMutation = useMutation({
    mutationFn: (payload: TelegramConfigWrite) => api.telegram.update(payload),
    onSuccess: (config) => {
      invalidateAll();
      setForm(formFrom(config));
      notify(t('telegram.saved'), 'success');
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const testMutation = useMutation({
    mutationFn: api.telegram.test,
    onSuccess: (result) => {
      if (result.ok) {
        setTestResult(
          result.username
            ? t('telegram.testOk', { username: result.username, id: formatNumber(result.botId) })
            : t('telegram.testOkNoUsername', { id: formatNumber(result.botId) }),
        );
      } else {
        setTestResult(t('telegram.testFail', { error: result.error }));
      }
    },
    onError: (error) => setTestResult(t('telegram.testFail', { error: errorMessage(error, t('common.error')) })),
  });

  const testIngestMutation = useMutation({
    mutationFn: (file: File) => api.telegram.testIngest(file),
    onSuccess: (result) => {
      setTestIngestMessage(
        t('telegram.testIngestSuccess', {
          duplicate: result.duplicate ? t('telegram.testIngestDuplicate') : '',
        }),
      );
      invalidateAll();
    },
    onError: (error) => setTestIngestMessage(t('telegram.testIngestFailed', { error: errorMessage(error, t('common.error')) })),
  });

  const enableMutation = useMutation({
    mutationFn: (enabled: boolean) => api.telegram.setEnabled(enabled),
    onSuccess: (_result, enabled) => {
      invalidateAll();
      notify(enabled ? t('telegram.enabled') : t('telegram.disabled'), 'success');
    },
    onError: (error) => notify(errorMessage(error, t('common.error')), 'error'),
  });

  const retryMutation = useMutation({
    mutationFn: (id: string) => api.telegram.retryPublication(id),
    onSuccess: () => {
      invalidateAll();
      notify(t('telegram.retrySent'), 'success');
    },
    onError: (error) => {
      notify(error instanceof ApiError && error.status === 409 ? t('telegram.retryConflict') : errorMessage(error, t('common.error')), 'error');
    },
  });

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!form) return;
    setFormError(null);
    const maxFileMb = Number(form.maxFileMb);
    const maxDuration = Number(form.maxAudioDurationSeconds);
    const retries = Number(form.maxRetryAttempts);
    if (!Number.isFinite(maxFileMb) || maxFileMb <= 0) {
      setFormError(t('common.error'));
      return;
    }
    if (!Number.isInteger(maxDuration) || maxDuration <= 0 || !Number.isInteger(retries) || retries <= 0) {
      setFormError(t('common.error'));
      return;
    }
    const adminTelegramUserId = optionalInteger(form.adminTelegramUserId);
    const adminChatId = optionalInteger(form.adminChatId);
    const destinationGroupId = optionalInteger(form.destinationGroupId);
    const destinationTopicId = optionalInteger(form.destinationTopicId);
    if ([adminTelegramUserId, adminChatId, destinationGroupId, destinationTopicId].some((value) => Number.isNaN(value))) {
      setFormError(t('common.error'));
      return;
    }

    const payload: TelegramConfigWrite = {
      autoPublishEnabled: form.autoPublishEnabled,
      ingestionEnabled: form.ingestionEnabled,
      maxIncomingFileSizeBytes: Math.round(maxFileMb * MB),
      maxAudioDurationSeconds: maxDuration,
      maxRetryAttempts: retries,
      apiEndpoint: form.apiEndpoint.trim(),
      localBotApiEndpoint: form.localBotApiEndpoint.trim() || null,
      adminTelegramUserId: adminTelegramUserId ?? null,
      adminChatId: adminChatId ?? null,
      destinationGroupId: destinationGroupId ?? null,
      destinationTopicId: destinationTopicId ?? null,
    };
    if (form.clearToken) payload.botToken = '';
    else if (form.token.trim()) payload.botToken = form.token.trim();
    saveMutation.mutate(payload);
  };

  const config = configQuery.data ?? null;
  const tokenAvailable = Boolean(form?.token.trim()) || Boolean(config?.tokenConfigured);
  const adminUserConfigured = Boolean(form?.adminTelegramUserId.trim());
  const enableBlocked = !tokenAvailable || !adminUserConfigured;

  return (
    <div className="page">
      <PageHeader
        title={t('telegram.title')}
        subtitle={t('telegram.subtitle')}
        actions={
          <Button icon="refresh" loading={configQuery.isFetching} onClick={() => void configQuery.refetch()}>
            {t('common.refresh')}
          </Button>
        }
      />

      {configQuery.isLoading ? <LoadingState /> : null}
      {configQuery.isError ? (
        <ErrorState error={configQuery.error} title={t('telegram.loadFailed')} onRetry={() => void configQuery.refetch()} />
      ) : null}

      {config && form ? (
        <>
          <div className="notice notice-info">
            <span>{t('telegram.writeOnly')}</span>
            <Badge tone={config.tokenConfigured ? 'success' : 'danger'}>
              {config.tokenConfigured ? t('telegram.tokenConfigured') : t('telegram.tokenNotConfigured')}
            </Badge>
          </div>

          <div className="split-layout">
            <Card title={t('telegram.title')}>
              {formError ? (
                <div className="notice notice-error" role="alert">
                  {formError}
                </div>
              ) : null}
              <form id="telegram-form" className="form" onSubmit={submit} noValidate>
                <Field label={t('telegram.token')} htmlFor="telegram-token" hint={t('telegram.tokenFormat')}>
                  <Input
                    id="telegram-token"
                    type="password"
                    autoComplete="off"
                    placeholder={config.tokenConfigured ? '••••••••' : ''}
                    value={form.token}
                    onChange={(event) => setForm((current) => (current ? { ...current, token: event.target.value } : current))}
                  />
                </Field>
                <Checkbox
                  label={t('telegram.clearToken')}
                  description={t('telegram.clearTokenHint')}
                  checked={form.clearToken}
                  onChange={(event) =>
                    setForm((current) => (current ? { ...current, clearToken: event.target.checked } : current))
                  }
                />
                <div className="form-grid two">
                  <Field label={t('telegram.adminUserId')} htmlFor="telegram-admin-user">
                    <Input
                      id="telegram-admin-user"
                      inputMode="numeric"
                      value={form.adminTelegramUserId}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, adminTelegramUserId: event.target.value } : current))
                      }
                    />
                  </Field>
                  <Field label={t('telegram.adminChatId')} htmlFor="telegram-admin-chat">
                    <Input
                      id="telegram-admin-chat"
                      inputMode="numeric"
                      value={form.adminChatId}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, adminChatId: event.target.value } : current))
                      }
                    />
                  </Field>
                </div>
                <div className="form-grid two">
                  <Field label={t('telegram.destinationGroup')} htmlFor="telegram-destination-group">
                    <Input
                      id="telegram-destination-group"
                      inputMode="numeric"
                      value={form.destinationGroupId}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, destinationGroupId: event.target.value } : current))
                      }
                    />
                  </Field>
                  <Field label={t('telegram.destinationTopic')} htmlFor="telegram-destination-topic">
                    <Input
                      id="telegram-destination-topic"
                      inputMode="numeric"
                      value={form.destinationTopicId}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, destinationTopicId: event.target.value } : current))
                      }
                    />
                  </Field>
                </div>
                <div className="form-grid two">
                  <Field label={t('telegram.apiEndpoint')} htmlFor="telegram-api-endpoint">
                    <Input
                      id="telegram-api-endpoint"
                      value={form.apiEndpoint}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, apiEndpoint: event.target.value } : current))
                      }
                    />
                  </Field>
                  <Field label={t('telegram.localApi')} htmlFor="telegram-local-api">
                    <Input
                      id="telegram-local-api"
                      value={form.localBotApiEndpoint}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, localBotApiEndpoint: event.target.value } : current))
                      }
                    />
                  </Field>
                </div>
                <div className="form-grid three">
                  <Field label={t('telegram.maxFileMb')} htmlFor="telegram-max-file">
                    <Input
                      id="telegram-max-file"
                      type="number"
                      min={0.01}
                      step="0.5"
                      value={form.maxFileMb}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, maxFileMb: event.target.value } : current))
                      }
                    />
                  </Field>
                  <Field label={t('telegram.maxDuration')} htmlFor="telegram-max-duration">
                    <Input
                      id="telegram-max-duration"
                      type="number"
                      min={1}
                      step={1}
                      value={form.maxAudioDurationSeconds}
                      onChange={(event) =>
                        setForm((current) =>
                          current ? { ...current, maxAudioDurationSeconds: event.target.value } : current,
                        )
                      }
                    />
                  </Field>
                  <Field label={t('telegram.retryAttempts')} htmlFor="telegram-retries">
                    <Input
                      id="telegram-retries"
                      type="number"
                      min={1}
                      max={20}
                      step={1}
                      value={form.maxRetryAttempts}
                      onChange={(event) =>
                        setForm((current) => (current ? { ...current, maxRetryAttempts: event.target.value } : current))
                      }
                    />
                  </Field>
                </div>
                <Checkbox
                  label={t('telegram.autoPublish')}
                  checked={form.autoPublishEnabled}
                  onChange={(event) =>
                    setForm((current) => (current ? { ...current, autoPublishEnabled: event.target.checked } : current))
                  }
                />
                <Checkbox
                  label={t('telegram.ingestion')}
                  checked={form.ingestionEnabled}
                  onChange={(event) =>
                    setForm((current) => (current ? { ...current, ingestionEnabled: event.target.checked } : current))
                  }
                />
                <Toolbar>
                  <Button variant="primary" type="submit" loading={saveMutation.isPending}>
                    {saveMutation.isPending ? t('common.saving') : t('telegram.save')}
                  </Button>
                  <Button
                    icon="shield"
                    loading={testMutation.isPending}
                    disabled={!tokenAvailable}
                    onClick={() => {
                      setTestResult(null);
                      testMutation.mutate();
                    }}
                  >
                    {testMutation.isPending ? t('telegram.testing') : t('telegram.test')}
                  </Button>
                  <Button
                    variant={config.ingestionEnabled ? 'danger' : 'secondary'}
                    loading={enableMutation.isPending}
                    disabled={!config.ingestionEnabled && enableBlocked}
                    onClick={() => enableMutation.mutate(!config.ingestionEnabled)}
                  >
                    {config.ingestionEnabled ? t('telegram.disable') : t('telegram.enable')}
                  </Button>
                </Toolbar>
                {!config.ingestionEnabled && enableBlocked ? (
                  <p className="field-hint">{t('telegram.enableBlocked')}</p>
                ) : null}
                {testResult ? (
                  <div className="notice notice-info" role="status">
                    {testResult}
                  </div>
                ) : null}
                <p className="muted small">
                  {t('common.updatedAt')}: {formatDate(config.updatedAt)}
                </p>
              </form>
            </Card>

            <Card title={t('telegram.testIngest')}>
              <p className="muted small">{t('telegram.testIngestHint')}</p>
              {!config.ingestionEnabled ? <div className="notice notice-warning">{t('telegram.enableBlocked')}</div> : null}
              <Field label={t('telegram.testIngestFile')} htmlFor="telegram-test-file">
                <input
                  id="telegram-test-file"
                  className="input"
                  type="file"
                  accept=".mp3,.flac,.wav,.m4a,.aac,.ogg,.opus,audio/*"
                  onChange={(event) => setTestFile(event.target.files?.[0] ?? null)}
                />
              </Field>
              <Button
                variant="primary"
                icon="upload"
                loading={testIngestMutation.isPending}
                disabled={!testFile || !config.ingestionEnabled || !config.tokenConfigured}
                onClick={() => {
                  if (testFile) testIngestMutation.mutate(testFile);
                }}
              >
                {testIngestMutation.isPending ? t('telegram.testIngestRunning') : t('telegram.testIngestRun')}
              </Button>
              {testIngestMessage ? (
                <div className="notice notice-info" role="status">
                  {testIngestMessage}
                </div>
              ) : null}
            </Card>
          </div>

          <Card title={t('telegram.imports')}>
            {importsQuery.isLoading ? <LoadingState /> : null}
            {importsQuery.isError ? <ErrorState error={importsQuery.error} onRetry={() => void importsQuery.refetch()} /> : null}
            {importsQuery.data ? (
              importsQuery.data.items.length === 0 ? (
                <EmptyState icon="telegram" title={t('telegram.noImports')} />
              ) : (
                <>
                  <div className="table-wrap">
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th scope="col">{t('telegram.fileName')}</th>
                          <th scope="col">{t('telegram.telegramUser')}</th>
                          <th scope="col">{t('telegram.chat')}</th>
                          <th scope="col">{t('telegram.status')}</th>
                          <th scope="col">{t('telegram.receivedAt')}</th>
                          <th scope="col">{t('telegram.processedAt')}</th>
                          <th scope="col">{t('telegram.lastError')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {importsQuery.data.items.map((entry) => (
                          <tr key={entry.id}>
                            <td title={entry.fileName ?? ''}>{entry.fileName ?? '—'}</td>
                            <td className="mono small">{entry.telegramUserId}</td>
                            <td className="mono small">{entry.chatId}</td>
                            <td>
                              <StatusBadge status={entry.status} />
                            </td>
                            <td className="muted small">{formatDate(entry.createdAt)}</td>
                            <td className="muted small">{formatDate(entry.completedAt)}</td>
                            <td className="muted small">{entry.error ?? '—'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <Pagination
                    page={importsPage}
                    pageSize={importsPageSize}
                    total={importsQuery.data.total}
                    pageSizes={[10, 25, 50]}
                    onPageChange={setImportsPage}
                    onPageSizeChange={(size) => {
                      setImportsPageSize(size);
                      setImportsPage(1);
                    }}
                  />
                </>
              )
            ) : null}
          </Card>

          <Card
            title={t('telegram.publications')}
            actions={
              <Select
                aria-label={t('telegram.filterStatus')}
                options={statusOptions}
                value={publicationStatus}
                onChange={(event) => {
                  setPublicationStatus(event.target.value);
                  setPublicationsPage(1);
                }}
              />
            }
          >
            {publicationsQuery.isLoading ? <LoadingState /> : null}
            {publicationsQuery.isError ? (
              <ErrorState error={publicationsQuery.error} onRetry={() => void publicationsQuery.refetch()} />
            ) : null}
            {publicationsQuery.data ? (
              publicationsQuery.data.items.length === 0 ? (
                <EmptyState icon="telegram" title={t('telegram.noPublications')} />
              ) : (
                <>
                  <div className="table-wrap">
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th scope="col">{t('tracks.titleColumn')}</th>
                          <th scope="col">{t('telegram.status')}</th>
                          <th scope="col">{t('telegram.attempts')}</th>
                          <th scope="col">{t('telegram.nextAttempt')}</th>
                          <th scope="col">{t('telegram.messageId')}</th>
                          <th scope="col">{t('telegram.lastError')}</th>
                          <th scope="col">{t('common.actions')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {publicationsQuery.data.items.map((publication) => (
                          <tr key={publication.id}>
                            <td>{publication.trackTitle}</td>
                            <td>
                              <StatusBadge status={publication.status} />
                            </td>
                            <td>{formatNumber(publication.attempts)}</td>
                            <td className="muted small">{formatDate(publication.nextAttemptAt)}</td>
                            <td className="mono small">{publication.telegramMessageId ?? '—'}</td>
                            <td className="muted small">{publication.lastError ?? '—'}</td>
                            <td>
                              <Button
                                size="sm"
                                icon="refresh"
                                disabled={publication.status === 'Published'}
                                loading={retryMutation.isPending && retryMutation.variables === publication.id}
                                onClick={() => retryMutation.mutate(publication.id)}
                              >
                                {t('telegram.retry')}
                              </Button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <Pagination
                    page={publicationsPage}
                    pageSize={publicationsPageSize}
                    total={publicationsQuery.data.total}
                    pageSizes={[10, 25, 50]}
                    onPageChange={setPublicationsPage}
                    onPageSizeChange={(size) => {
                      setPublicationsPageSize(size);
                      setPublicationsPage(1);
                    }}
                  />
                </>
              )
            ) : null}
          </Card>

          <Card title={t('telegram.updates')}>
            {updatesQuery.isLoading ? <LoadingState /> : null}
            {updatesQuery.isError ? <ErrorState error={updatesQuery.error} onRetry={() => void updatesQuery.refetch()} /> : null}
            {updatesQuery.data ? (
              updatesQuery.data.length === 0 ? (
                <EmptyState icon="telegram" title={t('telegram.noUpdates')} />
              ) : (
                <div className="table-wrap">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th scope="col">ID</th>
                        <th scope="col">{t('telegram.receivedAt')}</th>
                        <th scope="col">{t('telegram.processedAt')}</th>
                        <th scope="col">{t('telegram.lastError')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {updatesQuery.data.map((update) => (
                        <tr key={update.id}>
                          <td className="mono small">{update.telegramUpdateId}</td>
                          <td className="muted small">{formatDate(update.receivedAt)}</td>
                          <td className="muted small">{formatDate(update.processedAt)}</td>
                          <td className="muted small">{update.error ?? '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )
            ) : null}
          </Card>

          <p className="muted small">
            {formatBytes(config.maxIncomingFileSizeBytes)} · {formatNumber(config.maxAudioDurationSeconds)}s ·{' '}
            {config.configured ? t('telegram.tokenConfigured') : t('telegram.notConfigured')}
          </p>
        </>
      ) : null}
    </div>
  );
}
