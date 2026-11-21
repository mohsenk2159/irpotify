import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { SessionInfo } from '../api/types';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { useToast } from '../ui/Modal';
import { ConfirmDialog } from '../ui/Modal';
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeader,
} from '../ui/primitives';
import { errorMessage } from '../ui/helpers';

export function SessionsPage() {
  const { t, formatDate } = useUi();
  const { logoutAll } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const [revoking, setRevoking] = useState<SessionInfo | null>(null);
  const [revokeAllOpen, setRevokeAllOpen] = useState(false);
  const [revokeAllBusy, setRevokeAllBusy] = useState(false);

  const sessionsQuery = useQuery({ queryKey: ['auth', 'sessions'], queryFn: api.auth.sessions });

  const revokeMutation = useMutation({
    mutationFn: (id: string) => api.auth.revokeSession(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['auth', 'sessions'] });
      setRevoking(null);
      notify(t('sessions.revoked'), 'success');
    },
    onError: (error) => {
      setRevoking(null);
      notify(errorMessage(error, t('common.error')), 'error');
    },
  });

  const revokeAll = async () => {
    setRevokeAllBusy(true);
    try {
      await logoutAll();
      notify(t('sessions.revokeAllDone'), 'success');
    } finally {
      setRevokeAllBusy(false);
      setRevokeAllOpen(false);
    }
  };

  const sessions = sessionsQuery.data ?? [];

  return (
    <div className="page">
      <PageHeader
        title={t('sessions.title')}
        subtitle={t('sessions.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={sessionsQuery.isFetching} onClick={() => void sessionsQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            <Button variant="danger" icon="signout" disabled={sessions.length === 0} onClick={() => setRevokeAllOpen(true)}>
              {t('sessions.revokeAll')}
            </Button>
          </>
        }
      />

      <div className="notice notice-info">{t('sessions.currentUnavailable')}</div>

      {sessionsQuery.isLoading ? <LoadingState /> : null}
      {sessionsQuery.isError ? (
        <ErrorState error={sessionsQuery.error} title={t('sessions.loadFailed')} onRetry={() => void sessionsQuery.refetch()} />
      ) : null}

      {sessionsQuery.data ? (
        sessions.length === 0 ? (
          <EmptyState icon="sessions" title={t('sessions.empty')} />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">{t('sessions.device')}</th>
                  <th scope="col">{t('sessions.ip')}</th>
                  <th scope="col">{t('sessions.created')}</th>
                  <th scope="col">{t('sessions.lastSeen')}</th>
                  <th scope="col">{t('common.status')}</th>
                  <th scope="col">{t('sessions.revokedAt')}</th>
                  <th scope="col">{t('common.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {sessions.map((session) => (
                  <tr key={session.id}>
                    <td>{session.deviceName || '—'}</td>
                    <td className="mono small">{session.ipAddress ?? t('sessions.ipUnavailable')}</td>
                    <td className="muted small">{formatDate(session.createdAt)}</td>
                    <td className="muted small">{formatDate(session.lastSeenAt)}</td>
                    <td>
                      {session.revokedAt ? (
                        <Badge tone="danger">{t('sessions.revokedBadge')}</Badge>
                      ) : (
                        <Badge tone="success">{t('sessions.activeBadge')}</Badge>
                      )}
                    </td>
                    <td className="muted small">{formatDate(session.revokedAt)}</td>
                    <td>
                      <Button
                        size="sm"
                        variant="danger"
                        disabled={Boolean(session.revokedAt)}
                        loading={revokeMutation.isPending && revokeMutation.variables === session.id}
                        onClick={() => setRevoking(session)}
                      >
                        {t('sessions.revoke')}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      ) : null}

      <ConfirmDialog
        open={revoking !== null}
        title={t('sessions.revokeTitle')}
        body={`${revoking?.deviceName ?? ''} — ${t('sessions.revokeBody')}`}
        confirmLabel={t('sessions.revoke')}
        busy={revokeMutation.isPending}
        onCancel={() => setRevoking(null)}
        onConfirm={() => {
          if (revoking) revokeMutation.mutate(revoking.id);
        }}
      />

      <ConfirmDialog
        open={revokeAllOpen}
        title={t('sessions.revokeAllTitle')}
        body={t('sessions.revokeAllBody')}
        confirmLabel={t('sessions.revokeAll')}
        busy={revokeAllBusy}
        onCancel={() => setRevokeAllOpen(false)}
        onConfirm={() => void revokeAll()}
      />
    </div>
  );
}
