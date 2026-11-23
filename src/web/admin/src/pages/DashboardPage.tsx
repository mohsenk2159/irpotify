import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api/endpoints';
import type { Track } from '../api/types';
import { Badge, Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from '../ui/primitives';
import { Icon, type IconName } from '../ui/icons';
import { useUi } from '../ui/UiProvider';
import { usePlayer } from '../ui/Player';
import { Artwork } from '../ui/Artwork';
import { ThreeDIcon, type ThreeDIconType } from '../ui/ThreeDIcons';

export function DashboardPage() {
  const { t, formatBytes, formatNumber, formatDate, locale } = useUi();
  const stats = useQuery({
    queryKey: ['dashboard', 'stats'],
    queryFn: api.dashboard.stats,
    refetchInterval: 30_000,
  });
  const metrics = useQuery({
    queryKey: ['admin', 'metrics'],
    queryFn: api.dashboard.metrics,
    refetchInterval: 30_000,
  });
  const events = useQuery({
    queryKey: ['admin', 'system-events', 10],
    queryFn: () => api.dashboard.systemEvents(10),
    refetchInterval: 30_000,
  });

  const refreshAll = () => {
    void stats.refetch();
    void metrics.refetch();
    void events.refetch();
  };

  const data = stats.data;

  return (
    <div className="page">
      <PageHeader
        title={t('dashboard.title')}
        subtitle={t('dashboard.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={stats.isFetching || metrics.isFetching || events.isFetching} onClick={refreshAll}>
              {t('common.refresh')}
            </Button>
            <Link className="btn btn-secondary" to="/admin/monitoring">
              <Icon name="monitoring" size={18} />
              <span>{t('dashboard.viewMonitoring')}</span>
            </Link>
          </>
        }
      />

      {stats.isLoading ? <LoadingState /> : null}
      {stats.isError ? <ErrorState error={stats.error} onRetry={() => void stats.refetch()} title={t('dashboard.loadFailed')} /> : null}

      {data ? (
        <>
          <DashboardHeroBanner trackCount={data.counts.tracks} albumCount={data.counts.albums} />

          <div className="stats-grid">
            <StatCard icon="tracks" label={t('dashboard.tracks')} value={formatNumber(data.counts.tracks)} />
            <StatCard icon="albums" label={t('dashboard.albums')} value={formatNumber(data.counts.albums)} />
            <StatCard icon="artists" label={t('dashboard.artists')} value={formatNumber(data.counts.artists)} />
            <StatCard icon="users" label={t('dashboard.users')} value={formatNumber(data.counts.users)} />
            <StatCard icon="playlists" label={t('dashboard.playlists')} value={formatNumber(data.counts.playlists)} />
            <StatCard icon="folders" label={t('dashboard.folders')} value={formatNumber(data.counts.folders)} />
          </div>

          <div className="stats-grid stats-grid-wide">
            <StatCard
              icon="albums"
              label={t('dashboard.storage')}
              value={formatBytes(data.storage.bytes)}
              hint={t('dashboard.assets', { count: formatNumber(data.storage.assets) })}
            />
            <StatCard
              icon="monitoring"
              label={t('dashboard.disk')}
              value={
                data.storage.disk
                  ? t('dashboard.diskFree', {
                      free: formatBytes(data.storage.disk.freeBytes),
                      total: formatBytes(data.storage.disk.totalBytes),
                    })
                  : t('common.notAvailable')
              }
              hint={data.storage.disk?.name}
            />
            <StatCard icon="users" label={t('dashboard.activeUsers')} value={formatNumber(data.activeUsers)} />
            <StatCard icon="tracks" label={t('dashboard.activeStreams')} value={formatNumber(data.activeStreams)} />
            <StatCard
              icon="warning"
              label={t('dashboard.failedJobs')}
              value={formatNumber(data.failedJobs)}
              tone={data.failedJobs > 0 ? 'danger' : undefined}
            />
            <StatCard
              icon="shield"
              label={t('dashboard.database')}
              value={data.database ? t('dashboard.databaseOk') : t('dashboard.databaseDown')}
              tone={data.database ? 'success' : 'danger'}
            />
          </div>

          <div className="content-grid two-col">
            <Card title={t('dashboard.recentTracks')}>
              {data.recentTracks.length === 0 ? (
                <EmptyState title={t('tracks.empty')} body={t('tracks.emptyBody')} />
              ) : (
                <ul className="track-list">
                  {data.recentTracks.map((track) => (
                    <TrackListItem key={track.id} track={track} />
                  ))}
                </ul>
              )}
            </Card>
            <Card title={t('dashboard.mostPlayed')}>
              {data.mostPlayed.length === 0 ? (
                <EmptyState title={t('dashboard.mostPlayed')} body={t('dashboard.noActivity')} icon="star" />
              ) : (
                <ul className="track-list">
                  {data.mostPlayed.map((entry) => (
                    <li key={entry.track.id} className="track-list-row">
                      <TrackListItem track={entry.track} />
                      <Badge tone="info">{t('dashboard.plays', { count: formatNumber(entry.plays) })}</Badge>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>

          <Card title={t('dashboard.uploadActivity')}>
            <div className="chart-legend">
              <span className="legend-item">
                <span className="legend-swatch legend-uploaded" aria-hidden="true" />
                {t('dashboard.uploaded')}
              </span>
              <span className="legend-item">
                <span className="legend-swatch legend-rejected" aria-hidden="true" />
                {t('dashboard.rejected')}
              </span>
            </div>
            {data.uploadActivity.length === 0 ? (
              <p className="muted">{t('dashboard.noActivity')}</p>
            ) : (
              <div className="bar-chart" role="img" aria-label={t('dashboard.uploadActivity')}>
                {data.uploadActivity.map((day) => {
                  const total = day.uploaded + day.rejected;
                  const max = Math.max(1, ...data.uploadActivity.map((entry) => entry.uploaded + entry.rejected));
                  const uploadedHeight = total === 0 ? 0 : Math.max(2, Math.round((day.uploaded / max) * 100));
                  const rejectedHeight = total === 0 ? 0 : Math.max(day.rejected > 0 ? 2 : 0, Math.round((day.rejected / max) * 100));
                  return (
                    <div
                      key={day.date}
                      className="bar-chart-column"
                      title={`${day.date}: ${t('dashboard.uploaded')} ${day.uploaded} · ${t('dashboard.rejected')} ${day.rejected}`}
                    >
                      <div className="bar-chart-stack">
                        <span className="bar-segment legend-uploaded" style={{ height: `${uploadedHeight}%` }} />
                        <span className="bar-segment legend-rejected" style={{ height: `${rejectedHeight}%` }} />
                      </div>
                      <span className="bar-chart-label">
                        {formatDate(`${day.date}T00:00:00Z`, { month: '2-digit', day: '2-digit' })}
                      </span>
                    </div>
                  );
                })}
              </div>
            )}
          </Card>

          <div className="content-grid two-col">
            <Card
              title={t('dashboard.telegram')}
              actions={<Link className="btn btn-ghost btn-sm" to="/admin/telegram">{t('nav.telegram')}</Link>}
            >
              <dl className="definition-list">
                <div className="definition-row">
                  <dt>{t('telegram.token')}</dt>
                  <dd>
                    <Badge tone={data.telegram.configured ? 'success' : 'neutral'}>
                      {data.telegram.configured ? t('dashboard.telegramConfigured') : t('dashboard.telegramNotConfigured')}
                    </Badge>
                  </dd>
                </div>
                <div className="definition-row">
                  <dt>{t('dashboard.ingestion')}</dt>
                  <dd><StatusBadge status={data.telegram.ingestionEnabled ? 'active' : 'inactive'} /></dd>
                </div>
                <div className="definition-row">
                  <dt>{t('dashboard.autoPublish')}</dt>
                  <dd><StatusBadge status={data.telegram.autoPublishEnabled ? 'active' : 'inactive'} /></dd>
                </div>
                <div className="definition-row">
                  <dt>{t('dashboard.destination')}</dt>
                  <dd>{data.telegram.destinationConfigured ? t('common.yes') : t('common.no')}</dd>
                </div>
              </dl>
            </Card>

            <Card title={t('dashboard.integrity')}>
              {data.integrity ? (
                <dl className="definition-list">
                  <div className="definition-row">
                    <dt>{t('common.status')}</dt>
                    <dd><StatusBadge status={data.integrity.status} /></dd>
                  </div>
                  <div className="definition-row">
                    <dt>{t('dashboard.assets', { count: '' }).trim()}</dt>
                    <dd>{formatNumber(data.integrity.assetCount)}</dd>
                  </div>
                  <div className="definition-row">
                    <dt>Missing</dt>
                    <dd>{formatNumber(data.integrity.missingCount)}</dd>
                  </div>
                  <div className="definition-row">
                    <dt>Orphan</dt>
                    <dd>{formatNumber(data.integrity.orphanCount)}</dd>
                  </div>
                  <div className="definition-row">
                    <dt>{t('common.updatedAt')}</dt>
                    <dd>{formatDate(data.integrity.completedAt ?? data.integrity.startedAt)}</dd>
                  </div>
                </dl>
              ) : (
                <p className="muted">{t('dashboard.noIntegrity')}</p>
              )}
            </Card>
          </div>

          <div className="content-grid two-col">
            <Card title={t('dashboard.backup')}>
              {data.backup ? (
                <dl className="definition-list">
                  <div className="definition-row">
                    <dt>{t('monitoring.backupFile')}</dt>
                    <dd className="mono">{data.backup.fileName}</dd>
                  </div>
                  <div className="definition-row">
                    <dt>{t('common.status')}</dt>
                    <dd><StatusBadge status={data.backup.status} /></dd>
                  </div>
                  <div className="definition-row">
                    <dt>{t('monitoring.backupSize')}</dt>
                    <dd>{formatBytes(data.backup.sizeBytes)}</dd>
                  </div>
                  <div className="definition-row">
                    <dt>{t('monitoring.backupCompleted')}</dt>
                    <dd>{formatDate(data.backup.completedAt ?? data.backup.startedAt)}</dd>
                  </div>
                </dl>
              ) : (
                <p className="muted">{t('dashboard.noBackup')}</p>
              )}
            </Card>

            <Card title={t('dashboard.quickLinks')}>
              <div className="quick-links">
                <Link className="btn btn-secondary" to="/library/tracks">{t('dashboard.manageLibrary')}</Link>
                <Link className="btn btn-secondary" to="/admin/users">{t('dashboard.manageUsers')}</Link>
                <Link className="btn btn-secondary" to="/admin/audit">{t('dashboard.reviewAudit')}</Link>
              </div>
              <p className="muted small">
                {t('dashboard.generatedAt', {
                  time: formatDate(data.generatedAt, { timeStyle: 'medium' }),
                })}
              </p>
            </Card>
          </div>

          <div className="content-grid two-col">
            <Card title={t('monitoring.jobs')} actions={<Link className="btn btn-ghost btn-sm" to="/admin/monitoring">{t('common.details')}</Link>}>
              {metrics.isLoading ? <LoadingState /> : metrics.isError ? (
                <ErrorState error={metrics.error} onRetry={() => void metrics.refetch()} />
              ) : metrics.data ? (
                <dl className="definition-list">
                  <div className="definition-row"><dt>{t('monitoring.cpu')}</dt><dd>{formatNumber(metrics.data.process.cpuPercent)}%</dd></div>
                  <div className="definition-row"><dt>{t('monitoring.memory')}</dt><dd>{formatBytes(metrics.data.process.workingSetBytes)}</dd></div>
                  <div className="definition-row"><dt>{t('monitoring.database')}</dt><dd>{metrics.data.database.connected ? t('monitoring.connected') : t('monitoring.disconnected')} · {t('monitoring.dbResponse', { ms: formatNumber(metrics.data.database.responseMs) })}</dd></div>
                  <div className="definition-row"><dt>{t('monitoring.pending')}</dt><dd>{formatNumber(metrics.data.jobs.pending)}</dd></div>
                  <div className="definition-row"><dt>{t('monitoring.failed')}</dt><dd>{formatNumber(metrics.data.jobs.failed)}</dd></div>
                  <div className="definition-row"><dt>{t('monitoring.measuredAt', { time: formatDate(metrics.data.measuredAt, { timeStyle: 'medium' }) })}</dt><dd /></div>
                </dl>
              ) : null}
            </Card>

            <Card
              title={t('monitoring.systemEvents')}
              actions={<Button size="sm" variant="ghost" icon="refresh" onClick={() => void events.refetch()}>{t('common.refresh')}</Button>}
            >
              {events.isLoading ? <LoadingState /> : events.isError ? (
                <ErrorState error={events.error} onRetry={() => void events.refetch()} />
              ) : events.data && events.data.length > 0 ? (
                <ul className="event-list">
                  {events.data.map((event) => (
                    <li key={event.id} className="event-row">
                      <StatusBadge status={event.level} />
                      <span className="event-category">{event.category}</span>
                      <span className="event-message">{event.message}</span>
                      <span className="event-time">{formatDate(event.createdAt)}</span>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="muted">{t('monitoring.noEvents')}</p>
              )}
            </Card>
          </div>
          <p className="muted small" lang={locale}>
            {t('dashboard.generatedAt', { time: formatDate(data.generatedAt) })}
          </p>
        </>
      ) : null}
    </div>
  );
}

const ICON_TO_3D: Record<string, ThreeDIconType> = {
  tracks: 'notes',
  albums: 'album',
  artists: 'mic',
  users: 'users',
  playlists: 'cassette',
  folders: 'folder',
  storage: 'disk',
  monitoring: 'rocket',
  shield: 'shield',
  warning: 'shield',
  cloud: 'cloud',
};

function StatCard({
  icon,
  label,
  value,
  hint,
  tone,
  threeDType,
}: {
  icon: IconName;
  label: string;
  value: string;
  hint?: string;
  tone?: 'success' | 'danger';
  threeDType?: ThreeDIconType;
}) {
  const threeD = threeDType ?? ICON_TO_3D[icon] ?? 'vinyl';
  const isWord = value.length > 5 || /[a-zA-Z\u0600-\u06FF]/.test(value);
  return (
    <div className={`stat-card stat-card-3d${tone ? ` stat-${tone}` : ''}`}>
      <ThreeDIcon type={threeD} size={34} />
      <div className="stat-text">
        <span className="stat-label">{label}</span>
        <span className={`stat-value${isWord ? ' stat-value-text' : ''}`}>{value}</span>
        {hint ? <span className="stat-hint">{hint}</span> : null}
      </div>
    </div>
  );
}

function DashboardHeroBanner({ trackCount, albumCount }: { trackCount: number; albumCount: number }) {
  const { t } = useUi();
  return (
    <div className="dashboard-hero-banner">
      <div className="hero-banner-content">
        <div className="hero-badge">
          <span className="live-pulse" />
          <span>irpotify Cloud Core v1.0</span>
        </div>
        <h2 className="hero-title">{t('dashboard.welcomeTitle')}</h2>
        <p className="hero-description">{t('dashboard.welcomeDesc')}</p>
        <div className="hero-metrics-pill">
          <span>🎵 {trackCount} {t('dashboard.tracks')}</span>
          <span className="divider">•</span>
          <span>💿 {albumCount} {t('dashboard.albums')}</span>
          <span className="divider">•</span>
          <span className="status-online">● {t('monitoring.connected')}</span>
        </div>
      </div>
      <div className="hero-banner-art" aria-hidden="true" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <ThreeDIcon type="headphones" size={46} />
        <ThreeDIcon type="vinyl" size={56} animated />
        <ThreeDIcon type="waves" size={38} animated />
      </div>
    </div>
  );
}

function TrackListItem({ track }: { track: Track }) {
  const { t } = useUi();
  const { playTrack, current, status } = usePlayer();
  const isCurrent = current?.id === track.id;
  const isPlaying = isCurrent && status === 'playing';
  return (
    <div className="track-list-item">
      <Artwork url={track.artworkUrl} alt="" size={36} />
      <div className="track-list-text">
        <span className="track-list-title">{track.title}</span>
        <span className="track-list-sub">{track.artist || '—'}</span>
      </div>
      <button
        type="button"
        className="btn btn-ghost btn-icon"
        onClick={() => playTrack(track)}
        aria-label={isPlaying ? t('tracks.pause') : t('tracks.play')}
      >
        <Icon name={isPlaying ? 'pause' : 'play'} size={16} />
      </button>
    </div>
  );
}
