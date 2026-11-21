import { useMemo, useState, type FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/endpoints';
import type { AuditEntry } from '../api/types';
import { useUi } from '../ui/UiProvider';
import { Modal } from '../ui/Modal';
import {
  Button,
  EmptyState,
  ErrorState,
  Field,
  IconButton,
  Input,
  LoadingState,
  PageHeader,
  Select,
  Toolbar,
} from '../ui/primitives';
import { Pagination } from '../ui/Pagination';
import { shortId } from '../ui/helpers';

function prettyJson(value: string | null): string {
  if (!value) return '—';
  try {
    return JSON.stringify(JSON.parse(value) as unknown, null, 2);
  } catch {
    return value;
  }
}

export function AuditPage() {
  const { t, formatDate } = useUi();
  const [action, setAction] = useState('');
  const [actor, setActor] = useState('');
  const [appliedAction, setAppliedAction] = useState('');
  const [appliedActor, setAppliedActor] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [details, setDetails] = useState<AuditEntry | null>(null);

  const usersQuery = useQuery({ queryKey: ['users'], queryFn: api.users.list });

  const auditQuery = useQuery({
    queryKey: ['audit', appliedAction, appliedActor, page, pageSize],
    queryFn: () =>
      api.dashboard.audit({
        action: appliedAction || undefined,
        actorUserId: appliedActor || undefined,
        page,
        pageSize,
      }),
    placeholderData: (previous) => previous,
  });

  const actorOptions = useMemo(
    () => [
      { value: '', label: t('common.all') },
      ...(usersQuery.data ?? []).map((user) => ({ value: user.id, label: `${user.displayName} (${user.email})` })),
    ],
    [usersQuery.data, t],
  );

  const apply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setAppliedAction(action.trim());
    setAppliedActor(actor);
    setPage(1);
  };

  const reset = () => {
    setAction('');
    setActor('');
    setAppliedAction('');
    setAppliedActor('');
    setPage(1);
  };

  const rows = auditQuery.data?.items ?? [];
  const total = auditQuery.data?.total ?? 0;

  return (
    <div className="page">
      <PageHeader
        title={t('audit.title')}
        subtitle={t('audit.subtitle')}
        actions={
          <Button icon="refresh" loading={auditQuery.isFetching} onClick={() => void auditQuery.refetch()}>
            {t('common.refresh')}
          </Button>
        }
      />

      <Toolbar>
        <form className="filter-form" onSubmit={apply}>
          <Field label={t('audit.filterAction')} htmlFor="audit-action">
            <Input
              id="audit-action"
              value={action}
              onChange={(event) => setAction(event.target.value)}
              placeholder="user.create"
            />
          </Field>
          <Field label={t('audit.filterActor')} htmlFor="audit-actor">
            <Select
              id="audit-actor"
              options={actorOptions}
              value={actor}
              disabled={usersQuery.isLoading}
              onChange={(event) => setActor(event.target.value)}
            />
          </Field>
          <div className="form-actions">
            <Button variant="primary" type="submit" icon="search">
              {t('audit.applyFilters')}
            </Button>
            <Button type="button" variant="ghost" onClick={reset}>
              {t('audit.reset')}
            </Button>
          </div>
        </form>
      </Toolbar>

      {auditQuery.isLoading ? <LoadingState /> : null}
      {auditQuery.isError ? (
        <ErrorState error={auditQuery.error} title={t('audit.loadFailed')} onRetry={() => void auditQuery.refetch()} />
      ) : null}

      {auditQuery.data ? (
        rows.length === 0 ? (
          <EmptyState icon="audit" title={t('audit.empty')} />
        ) : (
          <>
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col">{t('audit.time')}</th>
                    <th scope="col">{t('audit.action')}</th>
                    <th scope="col">{t('audit.actor')}</th>
                    <th scope="col">{t('audit.entityType')}</th>
                    <th scope="col">{t('audit.entityId')}</th>
                    <th scope="col">{t('audit.ip')}</th>
                    <th scope="col">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((entry) => (
                    <tr key={entry.id}>
                      <td className="muted small">{formatDate(entry.createdAt)}</td>
                      <td>
                        <code className="mono">{entry.action}</code>
                      </td>
                      <td className="mono small">{entry.actorUserId ? shortId(entry.actorUserId, 12) : '—'}</td>
                      <td>{entry.entityType}</td>
                      <td className="mono small" title={entry.entityId ?? ''}>
                        {entry.entityId ? shortId(entry.entityId, 12) : '—'}
                      </td>
                      <td className="mono small">{entry.ipAddress ?? '—'}</td>
                      <td>
                        <IconButton
                          icon="external"
                          label={t('audit.details')}
                          onClick={() => setDetails(entry)}
                        />
                      </td>
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

      <Modal open={details !== null} title={t('audit.details')} onClose={() => setDetails(null)} size="lg">
        {details ? (
          <div className="stack">
            <dl className="definition-list">
              <div className="definition-row">
                <dt>{t('audit.time')}</dt>
                <dd>{formatDate(details.createdAt)}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('audit.action')}</dt>
                <dd>
                  <code className="mono">{details.action}</code>
                </dd>
              </div>
              <div className="definition-row">
                <dt>{t('audit.actor')}</dt>
                <dd className="mono wrap">{details.actorUserId ?? '—'}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('audit.entityType')}</dt>
                <dd>{details.entityType}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('audit.entityId')}</dt>
                <dd className="mono wrap">{details.entityId ?? '—'}</dd>
              </div>
              <div className="definition-row">
                <dt>{t('audit.ip')}</dt>
                <dd className="mono">{details.ipAddress ?? '—'}</dd>
              </div>
            </dl>
            <div>
              <h4>{t('audit.details')}</h4>
              <pre className="code-block">{prettyJson(details.detailsJson)}</pre>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
