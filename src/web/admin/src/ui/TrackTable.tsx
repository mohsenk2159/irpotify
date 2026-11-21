import type { ReactNode } from 'react';
import type { Track } from '../api/types';
import { usePlayer } from './Player';
import { useUi } from './UiProvider';
import { Artwork } from './Artwork';
import { Icon } from './icons';
import { EmptyState } from './primitives';

interface TrackTableProps {
  tracks: Track[];
  actions?: (track: Track) => ReactNode;
  emptyTitle?: string;
  emptyBody?: string;
  showAdded?: boolean;
}

export function TrackTable({ tracks, actions, emptyTitle, emptyBody, showAdded = true }: TrackTableProps) {
  const { t, formatBytes, formatDate, formatDuration } = useUi();
  const { playTrack, current, status } = usePlayer();

  if (tracks.length === 0) {
    return <EmptyState title={emptyTitle ?? t('tracks.empty')} body={emptyBody ?? t('tracks.emptyBody')} />;
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th scope="col">{t('tracks.titleColumn')}</th>
            <th scope="col">{t('tracks.artist')}</th>
            <th scope="col">{t('tracks.album')}</th>
            <th scope="col">{t('tracks.genre')}</th>
            <th scope="col" className="col-narrow">
              {t('tracks.duration')}
            </th>
            <th scope="col" className="col-narrow">
              {t('tracks.size')}
            </th>
            {showAdded ? (
              <th scope="col" className="col-date">
                {t('tracks.added')}
              </th>
            ) : null}
            <th scope="col" className="col-actions">
              <span className="sr-only">{t('common.actions')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {tracks.map((track) => {
            const isCurrent = current?.id === track.id;
            const isPlaying = isCurrent && status === 'playing';
            return (
              <tr key={track.id} className={isCurrent ? 'is-current' : undefined}>
                <td>
                  <div className="table-title-cell">
                    <Artwork url={track.artworkUrl} alt="" size={34} />
                    <div className="table-title-text">
                      <span className="table-title" title={track.title}>
                        {track.title}
                      </span>
                      <span className="table-sub mono" title={track.sha256}>
                        {track.codec ?? '—'} · {track.year ?? '—'}
                      </span>
                    </div>
                    <button
                      type="button"
                      className="btn btn-ghost btn-icon play-button"
                      onClick={() => playTrack(track)}
                      aria-label={isPlaying ? t('tracks.pause') : t('tracks.play')}
                      title={isPlaying ? t('tracks.pause') : t('tracks.play')}
                    >
                      <Icon name={isPlaying ? 'pause' : 'play'} size={16} />
                    </button>
                  </div>
                </td>
                <td title={track.artist}>{track.artist || '—'}</td>
                <td title={track.album}>{track.album || '—'}</td>
                <td>{track.genre ?? '—'}</td>
                <td className="col-narrow mono">{formatDuration(track.durationSeconds)}</td>
                <td className="col-narrow mono">{formatBytes(track.sizeBytes)}</td>
                {showAdded ? <td className="col-date">{formatDate(track.createdAt)}</td> : null}
                <td className="col-actions">
                  <div className="row-actions">{actions ? actions(track) : null}</div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
