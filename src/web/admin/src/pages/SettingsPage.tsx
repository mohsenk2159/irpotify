import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { useUi } from '../ui/UiProvider';
import { useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  Card,
  Checkbox,
  DefinitionList,
  ErrorState,
  Field,
  Input,
  LoadingState,
  PageHeader,
  Select,
} from '../ui/primitives';
import { errorMessage } from '../ui/helpers';

const MB = 1024 * 1024;

interface SettingsForm {
  serverName: string;
  publicBaseUrl: string;
  lanBaseUrl: string;
  detectedLocalIp: string;
  maintenanceMode: boolean;
  defaultTheme: 'dark' | 'light';
  defaultLanguage: 'en' | 'fa';
  maxUploadMb: string;
  maxConcurrentStreamsPerUser: string;
}

const FALLBACK_FORM: SettingsForm = {
  serverName: '',
  publicBaseUrl: '',
  lanBaseUrl: '',
  detectedLocalIp: '',
  maintenanceMode: false,
  defaultTheme: 'dark',
  defaultLanguage: 'en',
  maxUploadMb: '1024',
  maxConcurrentStreamsPerUser: '4',
};

export function SettingsPage() {
  const { t, formatNumber, formatDate } = useUi();
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<SettingsForm>(FALLBACK_FORM);
  const [formError, setFormError] = useState<string | null>(null);

  const settingsQuery = useQuery({ queryKey: ['settings'], queryFn: api.dashboard.settings });

  useEffect(() => {
    const values = settingsQuery.data?.values;
    if (!values) return;
    setForm({
      serverName: values.serverName,
      publicBaseUrl: values.publicBaseUrl,
      lanBaseUrl: values.lanBaseUrl ?? '',
      detectedLocalIp: values.detectedLocalIp ?? '',
      maintenanceMode: values.maintenanceMode,
      defaultTheme: values.defaultTheme === 'light' ? 'light' : 'dark',
      defaultLanguage: values.defaultLanguage === 'fa' ? 'fa' : 'en',
      maxUploadMb: String(Math.round((values.maxUploadBytes / MB) * 100) / 100),
      maxConcurrentStreamsPerUser: String(values.maxConcurrentStreamsPerUser),
    });
  }, [settingsQuery.data]);

  const themeOptions = useMemo(
    () => [
      { value: 'dark', label: t('settings.themeDark') },
      { value: 'light', label: t('settings.themeLight') },
    ],
    [t],
  );

  const languageOptions = useMemo(
    () => [
      { value: 'en', label: t('settings.langEnglish') },
      { value: 'fa', label: t('settings.langPersian') },
    ],
    [t],
  );

  const saveMutation = useMutation({
    mutationFn: (values: Record<string, string>) => api.dashboard.updateSettings(values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['settings'] });
      notify(t('settings.saved'), 'success');
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setFormError(null);

    const publicUrl = form.publicBaseUrl.trim();
    if (publicUrl && !publicUrl.startsWith('http://') && !publicUrl.startsWith('https://')) {
      setFormError(t('settings.urlProtocolRequired'));
      return;
    }

    const lanUrl = form.lanBaseUrl.trim();
    if (lanUrl && !lanUrl.startsWith('http://') && !lanUrl.startsWith('https://')) {
      setFormError(t('settings.urlProtocolRequired'));
      return;
    }

    const maxMb = Number(form.maxUploadMb);
    const streams = Number(form.maxConcurrentStreamsPerUser);
    if (!Number.isFinite(maxMb) || maxMb <= 0 || !Number.isFinite(streams) || streams <= 0 || !Number.isInteger(streams)) {
      setFormError(t('settings.invalidNumbers'));
      return;
    }

    saveMutation.mutate({
      'server.name': form.serverName.trim(),
      'server.publicBaseUrl': publicUrl,
      'server.lanBaseUrl': lanUrl,
      'server.maintenanceMode': form.maintenanceMode ? 'true' : 'false',
      'ui.defaultTheme': form.defaultTheme,
      'ui.defaultLanguage': form.defaultLanguage,
      'storage.maxUploadBytes': String(Math.round(maxMb * MB)),
      'streaming.maxConcurrentPerUser': String(streams),
    });
  };

  const capabilities = settingsQuery.data?.capabilities;

  return (
    <div className="page">
      <PageHeader
        title={t('settings.title')}
        subtitle={t('settings.subtitle')}
        actions={
          <Button icon="refresh" loading={settingsQuery.isFetching} onClick={() => void settingsQuery.refetch()}>
            {t('common.refresh')}
          </Button>
        }
      />

      {settingsQuery.isLoading ? <LoadingState /> : null}
      {settingsQuery.isError ? (
        <ErrorState error={settingsQuery.error} title={t('settings.loadFailed')} onRetry={() => void settingsQuery.refetch()} />
      ) : null}

      {settingsQuery.data ? (
        <div className="settings-grid">
          <Card title={t('settings.title')}>
            {formError ? (
              <div className="notice notice-error" role="alert">
                {formError}
              </div>
            ) : null}
            <form id="settings-form" className="form" onSubmit={submit} noValidate>
              <Field label={t('settings.serverName')} htmlFor="settings-server-name">
                <Input
                  id="settings-server-name"
                  value={form.serverName}
                  onChange={(event) => setForm((current) => ({ ...current, serverName: event.target.value }))}
                />
              </Field>

              {/* Network & Remote Connection Section */}
              <div style={{ borderTop: '1px solid var(--border)', paddingTop: '1.25rem', marginTop: '0.5rem' }}>
                <h3 style={{ fontSize: '15px', fontWeight: 600, margin: '0 0 0.35rem' }}>
                  {t('settings.networkTitle')}
                </h3>
                <p style={{ fontSize: '13px', color: 'var(--text-muted)', margin: '0 0 1rem' }}>
                  {t('settings.networkSubtitle')}
                  {form.detectedLocalIp ? ` (${t('settings.detectedLocalIp', { ip: form.detectedLocalIp })})` : ''}
                </p>

                <Field label={t('settings.lanBaseUrl')} htmlFor="settings-lan-url" hint={t('settings.lanBaseUrlHint')}>
                  <Input
                    id="settings-lan-url"
                    type="url"
                    inputMode="url"
                    placeholder="http://192.168.1.50:5080"
                    value={form.lanBaseUrl}
                    onChange={(event) => setForm((current) => ({ ...current, lanBaseUrl: event.target.value }))}
                  />
                </Field>

                <div style={{ marginTop: '0.75rem' }}>
                  <Field label={t('settings.publicBaseUrl')} htmlFor="settings-public-url" hint={t('settings.publicBaseUrlHint')}>
                    <Input
                      id="settings-public-url"
                      type="url"
                      inputMode="url"
                      placeholder="https://music.example.com or http://203.0.113.10:5080"
                      value={form.publicBaseUrl}
                      onChange={(event) => setForm((current) => ({ ...current, publicBaseUrl: event.target.value }))}
                    />
                  </Field>
                </div>
              </div>

              <div style={{ borderTop: '1px solid var(--border)', paddingTop: '1.25rem', marginTop: '0.75rem' }}>
                <Checkbox
                  label={t('settings.maintenance')}
                  description={t('settings.maintenanceHint')}
                  checked={form.maintenanceMode}
                  onChange={(event) => setForm((current) => ({ ...current, maintenanceMode: event.target.checked }))}
                />
              </div>

              <div className="form-grid two">
                <Field label={t('settings.defaultTheme')} htmlFor="settings-theme">
                  <Select
                    id="settings-theme"
                    options={themeOptions}
                    value={form.defaultTheme}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, defaultTheme: event.target.value === 'light' ? 'light' : 'dark' }))
                    }
                  />
                </Field>
                <Field label={t('settings.defaultLanguage')} htmlFor="settings-language">
                  <Select
                    id="settings-language"
                    options={languageOptions}
                    value={form.defaultLanguage}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, defaultLanguage: event.target.value === 'fa' ? 'fa' : 'en' }))
                    }
                  />
                </Field>
              </div>
              <div className="form-grid two">
                <Field label={t('settings.maxUpload')} htmlFor="settings-max-upload">
                  <Input
                    id="settings-max-upload"
                    type="number"
                    min={0.01}
                    step="0.5"
                    value={form.maxUploadMb}
                    onChange={(event) => setForm((current) => ({ ...current, maxUploadMb: event.target.value }))}
                  />
                </Field>
                <Field label={t('settings.concurrentStreams')} htmlFor="settings-streams">
                  <Input
                    id="settings-streams"
                    type="number"
                    min={1}
                    step={1}
                    value={form.maxConcurrentStreamsPerUser}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, maxConcurrentStreamsPerUser: event.target.value }))
                    }
                  />
                </Field>
              </div>
              <div className="form-actions">
                <Button variant="primary" type="submit" loading={saveMutation.isPending}>
                  {saveMutation.isPending ? t('common.saving') : t('common.save')}
                </Button>
              </div>
            </form>
          </Card>

          <Card title={t('settings.capabilities')}>
            {capabilities ? (
              <DefinitionList
                items={[
                  {
                    label: t('settings.ffprobe'),
                    value: (
                      <Badge tone={capabilities.ffprobeConfigured ? 'success' : 'danger'}>
                        {capabilities.ffprobeConfigured ? t('common.yes') : t('common.no')}
                      </Badge>
                    ),
                  },
                  {
                    label: t('settings.malwareScan'),
                    value: (
                      <Badge tone={capabilities.malwareScanRequired ? 'warning' : 'neutral'}>
                        {capabilities.malwareScanRequired ? t('common.yes') : t('common.no')}
                      </Badge>
                    ),
                  },
                  {
                    label: t('settings.clamav'),
                    value: (
                      <Badge tone={capabilities.clamAvConfigured ? 'success' : 'danger'}>
                        {capabilities.clamAvConfigured ? t('common.yes') : t('common.no')}
                      </Badge>
                    ),
                  },
                  {
                    label: t('settings.maxUpload'),
                    value: `${formatNumber(Math.round((settingsQuery.data.values.maxUploadBytes / MB) * 100) / 100)} MB`,
                  },
                  {
                    label: t('settings.concurrentStreams'),
                    value: formatNumber(settingsQuery.data.values.maxConcurrentStreamsPerUser),
                  },
                ]}
              />
            ) : null}
            <p className="muted small">
              {settingsQuery.data.updatedAt
                ? t('settings.updatedAt', { time: formatDate(settingsQuery.data.updatedAt) })
                : '—'}
            </p>
          </Card>
        </div>
      ) : null}
    </div>
  );
}
