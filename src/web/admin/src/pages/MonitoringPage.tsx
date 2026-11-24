import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import { useUi } from '../ui/UiProvider';
import { roleLabel } from '../ui/helpers';
import {
  Badge,
  Button,
  Card,
  Checkbox,
  DefinitionList,
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeader,
  StatusBadge,
} from '../ui/primitives';

export function MonitoringPage() {
  const { t, formatNumber, formatBytes, formatDuration, formatDate } = useUi();
  const [autoRefresh, setAutoRefresh] = useState(true);
  const interval = autoRefresh ? 15_000 : false;

  const metricsQuery = useQuery({
    queryKey: ['monitoring', 'metrics'],
    queryFn: api.dashboard.metrics,
    refetchInterval: interval,
  });
  const healthQuery = useQuery({
    queryKey: ['system', 'health'],
    queryFn: api.system.health,
    refetchInterval: interval,
  });
  const eventsQuery = useQuery({
    queryKey: ['monitoring', 'events'],
    queryFn: () => api.dashboard.systemEvents(50),
    refetchInterval: interval,
  });
  const backupsQuery = useQuery({
    queryKey: ['monitoring', 'backups'],
    queryFn: api.dashboard.backups,
    refetchInterval: interval,
  });
  const onlineQuery = useQuery({
    queryKey: ['monitoring', 'online-users'],
    queryFn: api.dashboard.onlineUsers,
    refetchInterval: interval,
  });

  const refreshAll = () => {
    void metricsQuery.refetch();
    void healthQuery.refetch();
    void eventsQuery.refetch();
    void backupsQuery.refetch();
    void onlineQuery.refetch();
  };

  const metrics = metricsQuery.data;
  const health = healthQuery.data;
  // /api/health reports ok/degraded; /api/healthz reports Healthy/Unhealthy.
  const healthy = Boolean(health && (health.status === 'Healthy' || health.status.toLowerCase() === 'ok'));

  return (
    <div className="page">
      <PageHeader
        title={t('monitoring.title')}
        subtitle={t('monitoring.subtitle')}
        actions={
          <>
            <Checkbox
              label={t('monitoring.autoRefresh')}
              checked={autoRefresh}
              onChange={(event) => setAutoRefresh(event.target.checked)}
            />
            <Button icon="refresh" onClick={refreshAll}>
              {t('monitoring.refresh')}
            </Button>
          </>
        }
      />

      {metricsQuery.isLoading ? <LoadingState /> : null}
      {metricsQuery.isError ? (
        <ErrorState error={metricsQuery.error} title={t('monitoring.loadFailed')} onRetry={() => void metricsQuery.refetch()} />
      ) : null}

      {metrics ? (
        <>
          <div className="stat-grid">
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.cpu')}</span>
              <span className="stat-value">{formatNumber(metrics.process.cpuPercent, { maximumFractionDigits: 1 })}%</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.memory')}</span>
              <span className="stat-value">{formatBytes(metrics.process.workingSetBytes)}</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.privateMemory')}</span>
              <span className="stat-value">{formatBytes(metrics.process.privateMemoryBytes)}</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.gc')}</span>
              <span className="stat-value">{formatBytes(metrics.process.gcTotalBytes)}</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.threads')}</span>
              <span className="stat-value">{formatNumber(metrics.process.threadCount)}</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.uptime')}</span>
              <span className="stat-value">{formatDuration(metrics.process.uptimeSeconds)}</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.streams')}</span>
              <span className="stat-value">{formatNumber(metrics.streams.active)}</span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.connectedUsers')}</span>
              <span className="stat-value" style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                <span style={{ width: 10, height: 10, borderRadius: '50%', backgroundColor: '#10b981', display: 'inline-block', boxShadow: '0 0 10px #10b981' }}></span>
                {formatNumber(onlineQuery.data?.onlineCount ?? (metrics.onlineUsers ?? 0))}
              </span>
            </div>
            <div className="stat-card">
              <span className="stat-label">{t('monitoring.database')}</span>
              <span className="stat-value">
                {metrics.database.connected ? (
                  <Badge tone="success">{t('monitoring.connected')}</Badge>
                ) : (
                  <Badge tone="danger">{t('monitoring.disconnected')}</Badge>
                )}
              </span>
            </div>
          </div>

          <div className="split-layout">
            <Card title={t('monitoring.jobs')}>
              <DefinitionList
                items={[
                  { label: t('monitoring.pending'), value: formatNumber(metrics.jobs.pending) },
                  { label: t('monitoring.processing'), value: formatNumber(metrics.jobs.processing) },
                  { label: t('monitoring.failed'), value: formatNumber(metrics.jobs.failed) },
                  { label: t('monitoring.outbox'), value: formatNumber(metrics.jobs.outboxPending) },
                  { label: t('monitoring.telegramPending'), value: formatNumber(metrics.jobs.telegramPending) },
                  {
                    label: t('monitoring.dbResponseLabel'),
                    value: t('monitoring.dbResponse', { ms: formatNumber(metrics.database.responseMs) }),
                  },
                  {
                    label: t('monitoring.heartbeat'),
                    value: metrics.workerHeartbeat ? formatDate(metrics.workerHeartbeat) : t('monitoring.heartbeatMissing'),
                  },
                  {
                    label: t('monitoring.measuredAtLabel'),
                    value: formatDate(metrics.measuredAt),
                  },
                ]}
              />
            </Card>

            <Card title={t('monitoring.health')}>
              {healthQuery.isError ? (
                <ErrorState error={healthQuery.error} onRetry={() => void healthQuery.refetch()} />
              ) : null}
              {health ? (
                <div className="stack">
                  <div className="detail-header">
                    <Badge tone={healthy ? 'success' : 'warning'}>
                      {healthy ? t('monitoring.healthOk') : t('monitoring.healthDegraded')}
                    </Badge>
                    <span className="muted small">{health.version}</span>
                  </div>
                  <DefinitionList
                    items={[
                      {
                        label: t('monitoring.database'),
                        value: (
                          <Badge tone={health.checks.database ? 'success' : 'danger'}>
                            {health.checks.database ? t('monitoring.connected') : t('monitoring.disconnected')}
                          </Badge>
                        ),
                      },
                      {
                        label: t('monitoring.ffprobe'),
                        value: (
                          <Badge tone={health.checks.ffprobe ? 'success' : 'danger'}>
                            {health.checks.ffprobe ? t('common.yes') : t('common.no')}
                          </Badge>
                        ),
                      },
                      {
                        label: t('monitoring.storageWritable'),
                        value: (
                          <Badge tone={health.checks.storageWritable ? 'success' : 'danger'}>
                            {health.checks.storageWritable ? t('common.yes') : t('common.no')}
                          </Badge>
                        ),
                      },
                      {
                        label: t('monitoring.malwareScanner'),
                        value: (
                          <Badge tone={health.checks.malwareScannerConfigured ? 'success' : 'warning'}>
                            {health.checks.malwareScannerConfigured ? t('common.yes') : t('common.no')}
                          </Badge>
                        ),
                      },
                      {
                        label: t('monitoring.telegramConfigured'),
                        value: (
                          <Badge tone={health.checks.telegramConfigured ? 'success' : 'neutral'}>
                            {health.checks.telegramConfigured ? t('common.yes') : t('common.no')}
                          </Badge>
                        ),
                      },
                    ]}
                  />
                  {metrics.disk ? (
                    <p className="muted small">
                      {t('monitoring.diskUsed', {
                        used: formatBytes(metrics.disk.usedBytes),
                        total: formatBytes(metrics.disk.totalBytes),
                      })}
                    </p>
                  ) : null}
                  <DefinitionList
                    items={[
                      { label: t('monitoring.machine'), value: metrics.system.machineName },
                      { label: t('monitoring.os'), value: metrics.system.os },
                      { label: t('monitoring.runtime'), value: metrics.system.runtime },
                      { label: t('monitoring.processors'), value: formatNumber(metrics.system.processorCount) },
                    ]}
                  />
                </div>
              ) : null}
            </Card>
          </div>

          <Card title={t('monitoring.onlineBoardTitle')}>
            <p className="muted small" style={{ marginBottom: '12px' }}>
              {t('monitoring.onlineBoardDesc', {
                online: formatNumber(onlineQuery.data?.onlineCount ?? 0),
                sessions: formatNumber(onlineQuery.data?.activeSessionsCount ?? 0),
              })}
            </p>
            {onlineQuery.isLoading ? <LoadingState /> : null}
            {onlineQuery.isError ? <ErrorState error={onlineQuery.error} onRetry={() => void onlineQuery.refetch()} /> : null}
            {onlineQuery.data ? (
              onlineQuery.data.sessions.length === 0 ? (
                <EmptyState icon="users" title={t('monitoring.noOnlineUsers')} />
              ) : (
                <div className="table-wrap" style={{ maxHeight: '360px', overflowY: 'auto' }}>
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th scope="col">{t('common.name')}</th>
                        <th scope="col">{t('common.email')}</th>
                        <th scope="col">{t('common.role')}</th>
                        <th scope="col">{t('sessions.device')}</th>
                        <th scope="col">{t('sessions.ip')}</th>
                        <th scope="col">{t('sessions.lastSeen')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {onlineQuery.data.sessions.map((session) => (
                        <tr key={session.sessionId}>
                          <td style={{ fontWeight: 600 }}>{session.displayName || t('common.unknown')}</td>
                          <td className="muted small">{session.email}</td>
                          <td>
                            <Badge tone={session.role === 'Administrator' ? 'warning' : 'info'}>
                              {roleLabel(t, session.role)}
                            </Badge>
                          </td>
                          <td>{session.deviceName || t('monitoring.unspecifiedDevice')}</td>
                          <td className="muted small" dir="ltr">{session.ipAddress || '—'}</td>
                          <td className="muted small">{formatDate(session.lastSeenAt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )
            ) : null}
          </Card>
        </>
      ) : null}

      <Card title={t('monitoring.systemEvents')}>
        {eventsQuery.isLoading ? <LoadingState /> : null}
        {eventsQuery.isError ? <ErrorState error={eventsQuery.error} onRetry={() => void eventsQuery.refetch()} /> : null}
        {eventsQuery.data ? (
          eventsQuery.data.length === 0 ? (
            <EmptyState icon="monitoring" title={t('monitoring.noEvents')} />
          ) : (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col">{t('audit.time')}</th>
                    <th scope="col">{t('monitoring.level')}</th>
                    <th scope="col">{t('monitoring.category')}</th>
                    <th scope="col">{t('monitoring.message')}</th>
                  </tr>
                </thead>
                <tbody>
                  {eventsQuery.data.map((event) => (
                    <tr key={event.id}>
                      <td className="muted small">{formatDate(event.createdAt)}</td>
                      <td>
                        <StatusBadge status={event.level} />
                      </td>
                      <td>{event.category}</td>
                      <td title={event.dataJson ?? ''}>{event.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : null}
      </Card>

      <Card title={t('monitoring.backups')}>
        <p className="muted small">{t('monitoring.backupsReadOnly')}</p>
        {backupsQuery.isLoading ? <LoadingState /> : null}
        {backupsQuery.isError ? <ErrorState error={backupsQuery.error} onRetry={() => void backupsQuery.refetch()} /> : null}
        {backupsQuery.data ? (
          backupsQuery.data.length === 0 ? (
            <EmptyState icon="monitoring" title={t('monitoring.noBackups')} />
          ) : (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col">{t('monitoring.backupFile')}</th>
                    <th scope="col">{t('monitoring.backupStatus')}</th>
                    <th scope="col">{t('monitoring.backupSize')}</th>
                    <th scope="col">{t('monitoring.backupStarted')}</th>
                    <th scope="col">{t('monitoring.backupCompleted')}</th>
                  </tr>
                </thead>
                <tbody>
                  {backupsQuery.data.map((backup) => (
                    <tr key={backup.id}>
                      <td title={backup.sha256 ?? ''}>{backup.fileName}</td>
                      <td>
                        <StatusBadge status={backup.status} />
                      </td>
                      <td>{formatBytes(backup.sizeBytes)}</td>
                      <td className="muted small">{formatDate(backup.startedAt)}</td>
                      <td className="muted small">{formatDate(backup.completedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : null}
      </Card>
    </div>
  );
}
