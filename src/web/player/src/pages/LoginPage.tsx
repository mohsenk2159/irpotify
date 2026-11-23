import { useState } from 'react';
import { api } from '../api/endpoints';
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

  // Forgot password modal state
  const [showForgot, setShowForgot] = useState(false);
  const [forgotEmail, setForgotEmail] = useState('');
  const [forgotLoading, setForgotLoading] = useState(false);
  const [forgotError, setForgotError] = useState<string | null>(null);
  const [forgotSuccess, setForgotSuccess] = useState<string | null>(null);

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

  const handleForgotSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!forgotEmail.trim()) return;

    setForgotError(null);
    setForgotLoading(true);

    try {
      const res = await api.auth.forgotPassword(forgotEmail.trim());
      setForgotSuccess(res.message || 'درخواست تغییر رمز عبور شما برای مدیریت ارسال شد.');
    } catch (err: unknown) {
      if (err instanceof Error) {
        setForgotError(err.message);
      } else {
        setForgotError('خطا در ارسال درخواست. لطفاً دوباره تلاش کنید.');
      }
    } finally {
      setForgotLoading(false);
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
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
              <button
                type="button"
                onClick={() => {
                  setForgotEmail(email);
                  setForgotError(null);
                  setForgotSuccess(null);
                  setShowForgot(true);
                }}
                style={{
                  fontSize: 13,
                  color: 'var(--color-primary)',
                  background: 'none',
                  border: 'none',
                  cursor: 'pointer',
                  padding: 0,
                  textDecoration: 'underline',
                  fontFamily: 'inherit'
                }}
              >
                فراموشی رمز عبور؟
              </button>
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

      {showForgot && (
        <div className="modal-overlay" onClick={() => !forgotLoading && setShowForgot(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <ThreeDIcon name="shield" size={26} />
                <h3 style={{ margin: 0, fontSize: 16 }}>بازیابی رمز عبور</h3>
              </div>
              <button
                type="button"
                className="icon-btn"
                onClick={() => setShowForgot(false)}
                disabled={forgotLoading}
                title="بستن"
              >
                <Icon name="close" size={18} />
              </button>
            </div>

            {forgotSuccess ? (
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14, padding: '16px 0', textAlign: 'center' }}>
                <div style={{ width: 48, height: 48, borderRadius: '50%', background: 'rgba(34, 197, 94, 0.15)', color: '#22c55e', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                  <Icon name="check" size={24} />
                </div>
                <p style={{ margin: 0, fontSize: 14, fontWeight: 600, color: '#22c55e', lineHeight: 1.6 }}>
                  {forgotSuccess}
                </p>
                <p style={{ margin: 0, fontSize: 12, color: 'var(--text-muted)', lineHeight: 1.6 }}>
                  مدیریت سیستم پس از بررسی درخواست، رمز عبور جدید را برای شما تنظیم خواهد کرد.
                </p>
                <button
                  type="button"
                  className="btn-primary"
                  style={{ width: '100%', marginTop: 8 }}
                  onClick={() => {
                    setShowForgot(false);
                    setForgotSuccess(null);
                  }}
                >
                  متوجه شدم
                </button>
              </div>
            ) : (
              <form onSubmit={handleForgotSubmit}>
                <p style={{ margin: '0 0 14px', fontSize: 13, color: 'var(--text-muted)', lineHeight: 1.6 }}>
                  ایمیل ثبت‌شده در حساب کاربری خود را وارد کنید تا پیام درخواست تغییر رمز برای مدیریت ارسال گردد:
                </p>

                {forgotError && (
                  <div className="login-error-alert" style={{ marginBottom: 14 }} role="alert">
                    <Icon name="warning" size={16} />
                    <span>{forgotError}</span>
                  </div>
                )}

                <div className="form-group" style={{ marginBottom: 16 }}>
                  <label className="form-label" htmlFor="forgot-email">
                    ایمیل شما
                  </label>
                  <input
                    id="forgot-email"
                    type="email"
                    className="form-input"
                    placeholder="user@example.com"
                    value={forgotEmail}
                    onChange={(e) => setForgotEmail(e.target.value)}
                    required
                    autoFocus
                  />
                </div>

                <div className="modal-actions">
                  <button
                    type="button"
                    className="btn-secondary"
                    onClick={() => setShowForgot(false)}
                    disabled={forgotLoading}
                  >
                    انصراف
                  </button>
                  <button
                    type="submit"
                    className="btn-primary"
                    disabled={forgotLoading || !forgotEmail.trim()}
                  >
                    {forgotLoading ? 'در حال ارسال…' : 'ارسال درخواست'}
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
