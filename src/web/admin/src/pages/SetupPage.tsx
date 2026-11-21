import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/endpoints';
import { ApiError } from '../api/http';
import { useAuth } from '../auth/AuthProvider';
import { AuthShell } from '../ui/AuthShell';
import { useToast } from '../ui/Modal';
import { useUi } from '../ui/UiProvider';
import { Button, ErrorState, Field, Input } from '../ui/primitives';
import { errorMessage } from '../ui/helpers';

export function SetupPage() {
  const { t } = useUi();
  const { setup, reloadSetup } = useAuth();
  const { notify } = useToast();
  const navigate = useNavigate();
  const [bootstrapToken, setBootstrapToken] = useState('');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [reloading, setReloading] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    const nextErrors: Record<string, string> = {};
    if (!bootstrapToken.trim()) nextErrors['bootstrapToken'] = t('common.requiredField');
    if (!email.trim() || !email.includes('@')) nextErrors['email'] = t('setup.emailInvalid');
    if (!displayName.trim()) nextErrors['displayName'] = t('common.requiredField');
    if (password.length < 12) nextErrors['password'] = t('setup.passwordHint');
    if (password !== confirmPassword) nextErrors['confirmPassword'] = t('setup.passwordMismatch');
    setFieldErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;

    setBusy(true);
    try {
      await api.setup.initialize({
        bootstrapToken: bootstrapToken.trim(),
        email: email.trim().toLowerCase(),
        password,
        displayName: displayName.trim(),
      });
      await reloadSetup();
      notify(t('setup.success'), 'success');
      navigate(`/login?email=${encodeURIComponent(email.trim().toLowerCase())}`, { replace: true });
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) setError(t('setup.invalidToken'));
      else if (cause instanceof ApiError && cause.isNetworkError) setError(t('setup.unavailable'));
      else setError(errorMessage(cause, t('setup.failed')));
    } finally {
      setBusy(false);
    }
  };

  const retrySetup = async () => {
    setReloading(true);
    try {
      await reloadSetup();
    } finally {
      setReloading(false);
    }
  };

  if (!setup) {
    return (
      <AuthShell title={t('setup.title')} subtitle={t('setup.subtitle')}>
        <ErrorState error={t('setup.unavailable')} onRetry={() => void retrySetup()} />
        {reloading ? <p className="field-hint">{t('common.loading')}</p> : null}
      </AuthShell>
    );
  }

  return (
    <AuthShell title={t('setup.title')} subtitle={t('setup.subtitle')}>
      {!setup.bootstrapTokenConfigured ? (
        <div className="notice notice-warning" role="status">
          {t('setup.bootstrapDisabled')}
        </div>
      ) : null}
      {error ? (
        <div className="notice notice-error" role="alert">
          {error}
        </div>
      ) : null}
      <form className="form" onSubmit={(event) => void submit(event)} noValidate>
        <Field label={t('setup.token')} htmlFor="setup-token" hint={t('setup.tokenHint')} required error={fieldErrors['bootstrapToken']}>
          <Input
            id="setup-token"
            type="password"
            autoComplete="off"
            value={bootstrapToken}
            onChange={(event) => setBootstrapToken(event.target.value)}
            required
          />
        </Field>
        <Field label={t('setup.displayName')} htmlFor="setup-name" required error={fieldErrors['displayName']}>
          <Input
            id="setup-name"
            autoComplete="name"
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            required
          />
        </Field>
        <Field label={t('auth.email')} htmlFor="setup-email" required error={fieldErrors['email']}>
          <Input
            id="setup-email"
            type="email"
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </Field>
        <Field label={t('auth.password')} htmlFor="setup-password" hint={t('setup.passwordHint')} required error={fieldErrors['password']}>
          <Input
            id="setup-password"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </Field>
        <Field label={t('setup.confirmPassword')} htmlFor="setup-confirm" required error={fieldErrors['confirmPassword']}>
          <Input
            id="setup-confirm"
            type="password"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
            required
          />
        </Field>
        <Button type="submit" variant="primary" loading={busy}>
          {busy ? t('setup.initializing') : t('setup.initialize')}
        </Button>
      </form>
    </AuthShell>
  );
}
