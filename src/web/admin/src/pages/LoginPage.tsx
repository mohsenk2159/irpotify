import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/endpoints';
import { ApiError } from '../api/http';
import { useAuth } from '../auth/AuthProvider';
import { AuthShell } from '../ui/AuthShell';
import { Modal, useToast } from '../ui/Modal';
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

  // Password Reset Modal state
  const [showResetModal, setShowResetModal] = useState(false);
  const [resetToken, setResetToken] = useState('');
  const [resetEmail, setResetEmail] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [resetBusy, setResetBusy] = useState(false);
  const [resetError, setResetError] = useState<string | null>(null);

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

  const handleResetPassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setResetError(null);
    if (!resetToken.trim()) {
      setResetError(t('common.requiredField'));
      return;
    }
    if (newPassword.length < 12) {
      setResetError(t('auth.passwordTooShort'));
      return;
    }
    if (newPassword !== confirmPassword) {
      setResetError(t('auth.passwordsDoNotMatch'));
      return;
    }

    setResetBusy(true);
    try {
      const res = await api.setup.resetPassword({
        bootstrapToken: resetToken.trim(),
        email: resetEmail.trim() || undefined,
        newPassword,
      });
      notify(t('auth.resetPasswordSuccess'), 'success');
      setShowResetModal(false);
      setResetToken('');
      setNewPassword('');
      setConfirmPassword('');
      if (res.email) {
        setEmail(res.email);
      }
      setPassword('');
    } catch (cause) {
      if (cause instanceof ApiError) {
        setResetError(cause.message || t('errors.unexpected'));
      } else {
        setResetError(errorMessage(cause, t('errors.unexpected')));
      }
    } finally {
      setResetBusy(false);
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
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '-0.25rem' }}>
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            style={{ fontSize: '13px', padding: '2px 6px', textDecoration: 'underline' }}
            onClick={() => {
              setResetEmail(email);
              setResetError(null);
              setShowResetModal(true);
            }}
          >
            {t('auth.forgotPassword')}
          </button>
        </div>
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

      <Modal
        open={showResetModal}
        title={t('auth.resetPasswordTitle')}
        onClose={() => setShowResetModal(false)}
        size="md"
      >
        <form className="form" onSubmit={(event) => void handleResetPassword(event)} noValidate>
          <p style={{ margin: '0 0 1rem', color: 'var(--text-muted)', fontSize: '13px' }}>
            {t('auth.resetPasswordSubtitle')}
          </p>

          {resetError ? (
            <div className="notice notice-error" role="alert" style={{ marginBottom: '1rem' }}>
              {resetError}
            </div>
          ) : null}

          <Field
            label={t('setup.token')}
            htmlFor="reset-token"
            hint={t('auth.resetPasswordTokenHint')}
            required
          >
            <Input
              id="reset-token"
              type="password"
              autoComplete="off"
              value={resetToken}
              onChange={(event) => setResetToken(event.target.value)}
              required
              autoFocus
            />
          </Field>

          <Field
            label={t('auth.email')}
            htmlFor="reset-email"
            hint={t('common.optional')}
          >
            <Input
              id="reset-email"
              type="email"
              autoComplete="username"
              placeholder="admin@local (or leave blank for admin)"
              value={resetEmail}
              onChange={(event) => setResetEmail(event.target.value)}
            />
          </Field>

          <Field
            label={t('auth.newPassword')}
            htmlFor="reset-new-password"
            hint={t('setup.passwordHint')}
            required
          >
            <Input
              id="reset-new-password"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
            />
          </Field>

          <Field
            label={t('auth.confirmNewPassword')}
            htmlFor="reset-confirm-password"
            required
          >
            <Input
              id="reset-confirm-password"
              type="password"
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
            />
          </Field>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '1.5rem' }}>
            <Button variant="secondary" onClick={() => setShowResetModal(false)} disabled={resetBusy}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" variant="primary" loading={resetBusy}>
              {resetBusy ? t('auth.resettingPassword') : t('auth.resetPasswordButton')}
            </Button>
          </div>
        </form>
      </Modal>
    </AuthShell>
  );
}
