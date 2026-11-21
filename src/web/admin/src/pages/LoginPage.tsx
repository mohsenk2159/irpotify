import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/http';
import { useAuth } from '../auth/AuthProvider';
import { AuthShell } from '../ui/AuthShell';
import { useToast } from '../ui/Modal';
import { useUi } from '../ui/UiProvider';
import { Button, Field, Input } from '../ui/primitives';
import { errorMessage } from '../ui/helpers';

export function LoginPage() {
  const { t } = useUi();
  const { login } = useAuth();
  const { notify } = useToast();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [email, setEmail] = useState(() => searchParams.get('email') ?? '');
  const [password, setPassword] = useState('');
  const [deviceName, setDeviceName] = useState('Web dashboard');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    if (!email.trim() || !password) {
      setError(t('common.requiredField'));
      return;
    }
    setBusy(true);
    try {
      await login(email.trim().toLowerCase(), password, deviceName.trim() || 'Web dashboard');
      notify(t('auth.signIn'), 'success');
      navigate('/', { replace: true });
    } catch (cause) {
      if (cause instanceof ApiError) {
        if (cause.status === 423) setError(t('auth.locked'));
        else if (cause.status === 429) {
          setError(
            cause.retryAfterSeconds
              ? `${t('auth.rateLimited')} (${cause.retryAfterSeconds}s)`
              : t('auth.rateLimited'),
          );
        } else if (cause.status === 401) setError(cause.message || t('auth.failed'));
        else setError(errorMessage(cause, t('auth.failed')));
      } else {
        setError(t('errors.unexpected'));
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthShell title={t('auth.loginTitle')} subtitle={t('auth.loginSubtitle')}>
      {error ? (
        <div className="notice notice-error" role="alert">
          {error}
        </div>
      ) : null}
      <form className="form" onSubmit={(event) => void submit(event)} noValidate>
        <Field label={t('auth.email')} htmlFor="login-email" required>
          <Input
            id="login-email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
            autoFocus
          />
        </Field>
        <Field label={t('auth.password')} htmlFor="login-password" required>
          <Input
            id="login-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </Field>
        <Field label={t('auth.deviceName')} htmlFor="login-device" hint={t('auth.deviceNameHint')}>
          <Input
            id="login-device"
            value={deviceName}
            maxLength={160}
            onChange={(event) => setDeviceName(event.target.value)}
          />
        </Field>
        <Button type="submit" variant="primary" loading={busy}>
          {busy ? t('auth.signingIn') : t('auth.signIn')}
        </Button>
      </form>
    </AuthShell>
  );
}
