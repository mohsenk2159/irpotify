import { useState } from 'react';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { BrandLogo, Icon } from '../ui/icons';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export function LoginPage() {
  const { login } = useAuth();
  const { t } = useUi();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      await login(email.trim(), password, 'irpotify Player WebApp');
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('نام کاربری یا رمز عبور اشتباه است.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-brand">
          <div className="login-logo-glow">
            <BrandLogo size={52} />
          </div>
          <h1 className="login-title">{t('auth.title')}</h1>
          <p className="login-subtitle">{t('auth.subtitle')}</p>
        </div>

        <div className="login-3d-banner">
          <ThreeDIcon name="headphones" size={64} />
        </div>

        {error && (
          <div className="login-error-alert" role="alert">
            <Icon name="warning" size={18} />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="login-form">
          <div className="form-group">
            <label className="form-label" htmlFor="player-email">
              {t('auth.email')}
            </label>
            <div className="input-with-icon">
              <input
                id="player-email"
                type="email"
                className="form-input"
                placeholder="user@example.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoComplete="email"
                autoFocus
              />
            </div>
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="player-pass">
              {t('auth.password')}
            </label>
            <div className="input-with-icon">
              <input
                id="player-pass"
                type="password"
                className="form-input"
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                autoComplete="current-password"
              />
            </div>
          </div>

          <button
            type="submit"
            className="btn-primary full-width login-submit-btn"
            disabled={loading}
          >
            {loading ? (
              <>
                <div className="btn-spinner" />
                <span>در حال ورود…</span>
              </>
            ) : (
              <span>{t('auth.signIn')}</span>
            )}
          </button>
        </form>

        <footer className="login-footer">
          <p className="login-footer-hint">
            🎧 قابل استفاده برای تمام کاربران و مدیران جهت پخش موسیقی
          </p>
        </footer>
      </div>
    </div>
  );
}
