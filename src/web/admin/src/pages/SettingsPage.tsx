import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { useUi } from '../ui/UiProvider';
import { useToast } from '../ui/Modal';
import {
  Badge,
  Button,
  Card,
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
  localIp: string;
  dashboardPort: string;
  playerPort: string;
  publicIpOrDomain: string;
  publicBaseUrl: string;
  lanBaseUrl: string;
  detectedLocalIp: string;
  detectedPublicIp: string;
  defaultTheme: 'dark' | 'light';
  defaultLanguage: 'en' | 'fa';
  maxUploadMb: string;
  maxConcurrentStreamsPerUser: string;
}

const FALLBACK_FORM: SettingsForm = {
  serverName: '',
  localIp: '',
  dashboardPort: '5080',
  playerPort: '5081',
  publicIpOrDomain: '',
  publicBaseUrl: '',
  lanBaseUrl: '',
  detectedLocalIp: '',
  detectedPublicIp: '',
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

    let localIp = values.localIp || '';
    if (!localIp && values.lanBaseUrl) {
      try {
        const u = new URL(values.lanBaseUrl);
        localIp = u.hostname;
      } catch {}
    }
    if (!localIp && values.detectedLocalIp) {
      localIp = values.detectedLocalIp;
    }

    let publicIpOrDomain = values.publicIpOrDomain || '';
    if (!publicIpOrDomain && values.publicBaseUrl) {
      try {
        const u = new URL(values.publicBaseUrl);
        publicIpOrDomain = u.hostname;
      } catch {}
    }
    if (!publicIpOrDomain && values.detectedPublicIp) {
      publicIpOrDomain = values.detectedPublicIp;
    }

    setForm({
      serverName: values.serverName,
      localIp,
      dashboardPort: values.dashboardPort || '5080',
      playerPort: values.playerPort || '5081',
      publicIpOrDomain,
      publicBaseUrl: values.publicBaseUrl,
      lanBaseUrl: values.lanBaseUrl ?? '',
      detectedLocalIp: values.detectedLocalIp ?? '',
      detectedPublicIp: values.detectedPublicIp ?? '',
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

    const dPort = parseInt(form.dashboardPort, 10);
    const pPort = parseInt(form.playerPort, 10);
    if (!Number.isInteger(dPort) || dPort < 1024 || dPort > 65535 || !Number.isInteger(pPort) || pPort < 1024 || pPort > 65535) {
      setFormError(t('settings.invalidPortRange'));
      return;
    }
    if (dPort === pPort) {
      setFormError(t('settings.portsMustDiffer'));
      return;
    }

    const cleanLocalIp = form.localIp.trim().replace(/^https?:\/\//i, '').replace(/:\d+$/, '').trim();
    const cleanPublicIpOrDomain = form.publicIpOrDomain.trim().replace(/^https?:\/\//i, '').replace(/:\d+$/, '').trim();

    const computedLanUrl = cleanLocalIp ? `http://${cleanLocalIp}:${pPort}` : '';
    let computedPublicUrl = '';
    if (cleanPublicIpOrDomain) {
      const isIp = /^(\d{1,3}\.){3}\d{1,3}$/.test(cleanPublicIpOrDomain);
      computedPublicUrl = isIp ? `http://${cleanPublicIpOrDomain}:${pPort}` : `https://${cleanPublicIpOrDomain}`;
    }

    const maxMb = Number(form.maxUploadMb);
    const streams = Number(form.maxConcurrentStreamsPerUser);
    if (!Number.isFinite(maxMb) || maxMb <= 0 || !Number.isFinite(streams) || streams <= 0 || !Number.isInteger(streams)) {
      setFormError(t('settings.invalidNumbers'));
      return;
    }

    saveMutation.mutate({
      'server.name': form.serverName.trim(),
      'server.localIp': cleanLocalIp,
      'server.dashboardPort': String(dPort),
      'server.playerPort': String(pPort),
      'server.publicIpOrDomain': cleanPublicIpOrDomain,
      'server.lanBaseUrl': computedLanUrl,
      'server.publicBaseUrl': computedPublicUrl,
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

              {/* 1. Local / LAN Server Configuration Card */}
              <div
                style={{
                  borderTop: '1px solid var(--border)',
                  paddingTop: '1.25rem',
                  marginTop: '1.25rem',
                }}
              >
                <div
                  style={{
                    background: 'var(--surface-2)',
                    border: '1px solid var(--border)',
                    borderRadius: '12px',
                    padding: '1.25rem',
                    marginBottom: '1.25rem',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '0.35rem' }}>
                    <span style={{ fontSize: '18px' }}>🏠</span>
                    <h3 style={{ fontSize: '15px', fontWeight: 600, margin: 0 }}>
                      {t('settings.lanSectionTitle')}
                    </h3>
                  </div>
                  <p style={{ fontSize: '13px', color: 'var(--text-muted)', margin: '0 0 1rem' }}>
                    {t('settings.lanSectionSubtitle')}
                  </p>

                  <Field
                    label={t('settings.localIp')}
                    htmlFor="settings-local-ip"
                    hint={
                      form.detectedLocalIp ? (
                        <span>
                          {t('settings.localIpHint')}{' '}
                          <button
                            type="button"
                            onClick={() => setForm((f) => ({ ...f, localIp: form.detectedLocalIp }))}
                            style={{
                              background: 'none',
                              border: 'none',
                              color: 'var(--primary)',
                              cursor: 'pointer',
                              textDecoration: 'underline',
                              padding: 0,
                              fontFamily: 'inherit',
                            }}
                          >
                            {t('settings.useDetectedLocalIp')}: {form.detectedLocalIp}
                          </button>
                        </span>
                      ) : (
                        t('settings.localIpHint')
                      )
                    }
                  >
                    <Input
                      id="settings-local-ip"
                      value={form.localIp}
                      placeholder="192.168.10.20"
                      onChange={(e) => setForm((f) => ({ ...f, localIp: e.target.value }))}
                    />
                  </Field>

                  <div className="form-grid two" style={{ marginTop: '0.75rem' }}>
                    <Field label={t('settings.dashboardPort')} htmlFor="settings-dashboard-port" hint={t('settings.dashboardPortHint')}>
                      <Input
                        id="settings-dashboard-port"
                        type="number"
                        min={1024}
                        max={65535}
                        value={form.dashboardPort}
                        onChange={(e) => setForm((f) => ({ ...f, dashboardPort: e.target.value }))}
                      />
                    </Field>
                    <Field label={t('settings.playerPort')} htmlFor="settings-player-port" hint={t('settings.playerPortHint')}>
                      <Input
                        id="settings-player-port"
                        type="number"
                        min={1024}
                        max={65535}
                        value={form.playerPort}
                        onChange={(e) => setForm((f) => ({ ...f, playerPort: e.target.value }))}
                      />
                    </Field>
                  </div>

                  <div
                    style={{
                      marginTop: '1rem',
                      padding: '10px 14px',
                      background: 'var(--surface-1)',
                      border: '1px dashed var(--border)',
                      borderRadius: '8px',
                      fontSize: '12px',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '6px',
                    }}
                  >
                    <div>
                      <span style={{ color: 'var(--text-muted)' }}>👑 {t('settings.lanDashboardUrl')}: </span>
                      <code style={{ color: 'var(--primary)', fontWeight: 600 }}>
                        http://{form.localIp || '192.168.x.x'}:{form.dashboardPort}
                      </code>
                    </div>
                    <div>
                      <span style={{ color: 'var(--text-muted)' }}>🎧 {t('settings.lanPlayerUrl')}: </span>
                      <code style={{ color: '#1db954', fontWeight: 600 }}>
                        http://{form.localIp || '192.168.x.x'}:{form.playerPort}
                      </code>
                    </div>
                  </div>
                </div>

                {/* 2. Public / Internet Server Configuration Card */}
                <div
                  style={{
                    background: 'var(--surface-2)',
                    border: '1px solid var(--border)',
                    borderRadius: '12px',
                    padding: '1.25rem',
                    marginBottom: '1.25rem',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '0.35rem' }}>
                    <span style={{ fontSize: '18px' }}>🌐</span>
                    <h3 style={{ fontSize: '15px', fontWeight: 600, margin: 0 }}>
                      {t('settings.publicSectionTitle')}
                    </h3>
                  </div>
                  <p style={{ fontSize: '13px', color: 'var(--text-muted)', margin: '0 0 1rem' }}>
                    {t('settings.publicSectionSubtitle')}
                  </p>

                  <Field
                    label={t('settings.publicIpOrDomain')}
                    htmlFor="settings-public-host"
                    hint={
                      form.detectedPublicIp ? (
                        <span>
                          {t('settings.publicIpOrDomainHint')}{' '}
                          <button
                            type="button"
                            onClick={() => setForm((f) => ({ ...f, publicIpOrDomain: form.detectedPublicIp }))}
                            style={{
                              background: 'none',
                              border: 'none',
                              color: 'var(--primary)',
                              cursor: 'pointer',
                              textDecoration: 'underline',
                              padding: 0,
                              fontFamily: 'inherit',
                            }}
                          >
                            {t('settings.useDetectedPublicIp')}: {form.detectedPublicIp}
                          </button>
                        </span>
                      ) : (
                        t('settings.publicIpOrDomainHint')
                      )
                    }
                  >
                    <Input
                      id="settings-public-host"
                      value={form.publicIpOrDomain}
                      placeholder="203.0.113.10 or music.example.com"
                      onChange={(e) => setForm((f) => ({ ...f, publicIpOrDomain: e.target.value }))}
                    />
                  </Field>

                  <div className="form-grid two" style={{ marginTop: '0.75rem' }}>
                    <Field label={t('settings.dashboardPort')} htmlFor="settings-public-dashboard-port" hint={t('settings.publicDashboardPortHint')}>
                      <Input
                        id="settings-public-dashboard-port"
                        type="number"
                        value={form.dashboardPort}
                        disabled
                        style={{ opacity: 0.75, cursor: 'not-allowed', background: 'var(--surface-3)' }}
                      />
                    </Field>
                    <Field label={t('settings.playerPort')} htmlFor="settings-public-player-port" hint={t('settings.publicPlayerPortHint')}>
                      <Input
                        id="settings-public-player-port"
                        type="number"
                        value={form.playerPort}
                        disabled
                        style={{ opacity: 0.75, cursor: 'not-allowed', background: 'var(--surface-3)' }}
                      />
                    </Field>
                  </div>

                  <div
                    style={{
                      marginTop: '1rem',
                      padding: '10px 14px',
                      background: 'var(--surface-1)',
                      border: '1px dashed var(--border)',
                      borderRadius: '8px',
                      fontSize: '12px',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '6px',
                    }}
                  >
                    <div>
                      <span style={{ color: 'var(--text-muted)' }}>👑 {t('settings.publicDashboardUrl')}: </span>
                      <code style={{ color: 'var(--primary)', fontWeight: 600 }}>
                        {form.publicIpOrDomain
                          ? (/^(\d{1,3}\.){3}\d{1,3}$/.test(form.publicIpOrDomain.trim())
                              ? `http://${form.publicIpOrDomain.trim()}:${form.dashboardPort}`
                              : `https://${form.publicIpOrDomain.trim()}`)
                          : `http://public-ip:${form.dashboardPort}`}
                      </code>
                    </div>
                    <div>
                      <span style={{ color: 'var(--text-muted)' }}>🎧 {t('settings.publicPlayerUrl')}: </span>
                      <code style={{ color: '#1db954', fontWeight: 600 }}>
                        {form.publicIpOrDomain
                          ? (/^(\d{1,3}\.){3}\d{1,3}$/.test(form.publicIpOrDomain.trim())
                              ? `http://${form.publicIpOrDomain.trim()}:${form.playerPort}`
                              : `https://${form.publicIpOrDomain.trim()}`)
                          : `http://public-ip:${form.playerPort}`}
                      </code>
                    </div>
                  </div>
                </div>

                {/* Architecture Notice */}
                <div
                  style={{
                    background: 'rgba(29, 185, 84, 0.08)',
                    border: '1px solid rgba(29, 185, 84, 0.25)',
                    borderRadius: '10px',
                    padding: '12px 16px',
                    marginBottom: '1rem',
                    fontSize: '12.5px',
                    lineHeight: 1.6,
                    color: 'var(--text)',
                  }}
                >
                  🛡️ <strong>{t('settings.portSyncNotice')}</strong>
                </div>
              </div>

              <div className="form-grid two" style={{ borderTop: '1px solid var(--border)', paddingTop: '1.25rem', marginTop: '0.75rem' }}>
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
