import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api/endpoints';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { Modal, useToast } from '../ui/Modal';
import {
  Button,
  EmptyState,
  ErrorState,
  Field,
  Input,
  LoadingState,
  PageHeader,
} from '../ui/primitives';
import { errorMessage } from '../ui/helpers';
import { Icon } from '../ui/icons';

export function GenresPage() {
  const { t, formatNumber } = useUi();
  const { isAdmin } = useAuth();
  const { notify } = useToast();
  const queryClient = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState('');
  const [formError, setFormError] = useState<string | null>(null);

  const genresQuery = useQuery({ queryKey: ['genres'], queryFn: api.genres.list });

  const createMutation = useMutation({
    mutationFn: (value: string) => api.genres.create(value),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['genres'] });
      notify(t('genres.created'), 'success');
      setCreateOpen(false);
      setName('');
    },
    onError: (error) => setFormError(errorMessage(error, t('common.error'))),
  });

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!name.trim()) {
      setFormError(t('common.requiredField'));
      return;
    }
    createMutation.mutate(name.trim());
  };

  return (
    <div className="page">
      <PageHeader
        title={t('genres.title')}
        subtitle={t('genres.subtitle')}
        actions={
          <>
            <Button icon="refresh" loading={genresQuery.isFetching} onClick={() => void genresQuery.refetch()}>
              {t('common.refresh')}
            </Button>
            {isAdmin ? (
              <Button variant="primary" icon="plus" onClick={() => { setName(''); setFormError(null); setCreateOpen(true); }}>
                {t('genres.create')}
              </Button>
            ) : null}
          </>
        }
      />

      <div className={`notice ${isAdmin ? 'notice-info' : 'notice-warning'}`}>{t('genres.readOnly')}</div>
      {!isAdmin ? <div className="notice notice-info">{t('genres.adminOnly')}</div> : null}

      {genresQuery.isLoading ? <LoadingState /> : null}
      {genresQuery.isError ? (
        <ErrorState error={genresQuery.error} title={t('genres.loadFailed')} onRetry={() => void genresQuery.refetch()} />
      ) : null}

      {genresQuery.data ? (
        genresQuery.data.length === 0 ? (
          <EmptyState
            title={t('genres.empty')}
            body={t('genres.emptyBody')}
            icon="genres"
            action={
              isAdmin ? (
                <Button variant="primary" icon="plus" onClick={() => setCreateOpen(true)}>
                  {t('genres.create')}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">{t('common.name')}</th>
                  <th scope="col" className="col-narrow">
                    {t('nav.tracks')}
                  </th>
                  <th scope="col" className="col-actions">
                    <span className="sr-only">{t('common.actions')}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {genresQuery.data.map((genre) => (
                  <tr key={genre.id}>
                    <td>{genre.name}</td>
                    <td className="col-narrow mono">{formatNumber(genre.trackCount)}</td>
                    <td className="col-actions">
                      <Link className="btn btn-secondary btn-sm" to={`/library/tracks?genreId=${genre.id}`}>
                        <Icon name="tracks" size={16} />
                        <span>{t('genres.viewTracks')}</span>
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      ) : null}

      <Modal
        open={createOpen}
        title={t('genres.create')}
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <Button onClick={() => setCreateOpen(false)}>{t('common.cancel')}</Button>
            <Button type="submit" form="genre-form" variant="primary" loading={createMutation.isPending}>
              {createMutation.isPending ? t('common.saving') : t('common.create')}
            </Button>
          </>
        }
      >
        {formError ? (
          <div className="notice notice-error" role="alert">
            {formError}
          </div>
        ) : null}
        <form id="genre-form" className="form" onSubmit={submit} noValidate>
          <Field label={t('common.name')} htmlFor="genre-name" required>
            <Input id="genre-name" value={name} onChange={(event) => setName(event.target.value)} required autoFocus />
          </Field>
          <p className="muted small">{t('genres.readOnly')}</p>
        </form>
      </Modal>
    </div>
  );
}
