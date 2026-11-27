import { useEffect, useRef, useState } from 'react';
import jsQR from 'jsqr';
import { api } from '../api/endpoints';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { BrandLogo, Icon } from '../ui/icons';
import { ThreeDIcon } from '../ui/ThreeDIcons';

function extractPairingCode(raw: string): string {
  const trimmed = raw.trim();
  if (!trimmed) return '';
  if (trimmed.startsWith('{')) {
    try {
      const parsed = JSON.parse(trimmed) as { code?: string };
      if (typeof parsed.code === 'string' && parsed.code.trim()) {
        return parsed.code.trim();
      }
    } catch {
      // fall through
    }
  }
  return trimmed;
}

export function LoginPage() {
  const { login, loginWithPairing } = useAuth();
  const { t, language, setLanguage, theme, toggleTheme } = useUi();

  const [loginMode, setLoginMode] = useState<'password' | 'qr'>('password');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // QR Login state
  const [qrInput, setQrInput] = useState('');
  const [cameraActive, setCameraActive] = useState(false);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const stopCamera = () => {
    if (streamRef.current) {
      for (const track of streamRef.current.getTracks()) {
        track.stop();
      }
      streamRef.current = null;
    }
    setCameraActive(false);
  };

  useEffect(() => {
    return () => stopCamera();
  }, []);

  useEffect(() => {
    if (loginMode !== 'qr') stopCamera();
  }, [loginMode]);

  const submitPairingCode = async (rawPayload: string) => {
    const code = extractPairingCode(rawPayload);
    if (!code) {
      setError('کد QR یا کد جفت‌سازی معتبر نیست.');
      return;
    }
    setError(null);
    setLoading(true);
    try {
      await loginWithPairing(code, 'irpotify Player WebApp (QR)', rememberMe);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'کد QR نامعتبر یا منقضی شده است.');
    } finally {
      setLoading(false);
    }
  };

  const startCameraScan = async () => {
    setError(null);
    if (!navigator.mediaDevices?.getUserMedia) {
      setError('مرورگر شما از دوربین پشتیبانی نمی‌کند. از آپلود تصویر QR یا وارد کردن کد استفاده کنید.');
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment' },
      });
      streamRef.current = stream;
      setCameraActive(true);
      requestAnimationFrame(() => {
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          void videoRef.current.play();
        }
      });
    } catch {
      setError('دسترسی به دوربین رد شد یا دوربین متصل نیست.');
      stopCamera();
    }
  };

  useEffect(() => {
    if (!cameraActive) return;
    let cancelled = false;
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');

    const scanFrame = () => {
      if (cancelled || !videoRef.current || !ctx) return;
      const video = videoRef.current;
      if (video.readyState === video.HAVE_ENOUGH_DATA && video.videoWidth > 0) {
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
        const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const decoded = jsQR(imageData.data, imageData.width, imageData.height);
        if (decoded?.data) {
          stopCamera();
          void submitPairingCode(decoded.data);
          return;
        }
      }
      if (!cancelled) {
        requestAnimationFrame(scanFrame);
      }
    };

    const raf = requestAnimationFrame(scanFrame);
    return () => {
      cancelled = true;
      cancelAnimationFrame(raf);
    };
  }, [cameraActive]);

  const handleQrImageUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setError(null);
    const reader = new FileReader();
    reader.onload = () => {
      const img = new Image();
      img.onload = () => {
        const canvas = document.createElement('canvas');
        canvas.width = img.width;
        canvas.height = img.height;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;
        ctx.drawImage(img, 0, 0);
        const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const decoded = jsQR(imageData.data, imageData.width, imageData.height);
        if (decoded?.data) {
          void submitPairingCode(decoded.data);
        } else {
          setError('کد QR در تصویر انتخاب‌شده شناسایی نشد.');
        }
      };
      img.src = String(reader.result);
    };
    reader.readAsDataURL(file);
    e.target.value = '';
  };

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
      await login(email.trim(), password, 'irpotify Player WebApp', rememberMe);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError(t('auth.invalidCredentials'));
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
      setForgotSuccess(res.message || t('auth.forgotSuccess'));
    } catch (err: unknown) {
      if (err instanceof Error) {
        setForgotError(err.message);
      } else {
        setForgotError(t('auth.forgotError'));
      }
    } finally {
      setForgotLoading(false);
    }
  };

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-header-bar">
          <div className="lang-switcher">
            <button
              type="button"
              className={`lang-chip ${language === 'fa' ? 'active' : ''}`}
              onClick={() => setLanguage('fa')}
            >
              فارسی
            </button>
            <button
              type="button"
              className={`lang-chip ${language === 'en' ? 'active' : ''}`}
              onClick={() => setLanguage('en')}
            >
              English
            </button>
          </div>
          <button
            type="button"
            className="login-theme-btn"
            onClick={toggleTheme}
            aria-label={theme === 'dark' ? 'Light mode' : 'Dark mode'}
            title={theme === 'dark' ? 'Light mode' : 'Dark mode'}
          >
            <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={17} />
          </button>
        </div>

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

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, marginBottom: 18 }}>
          <button
            type="button"
            className={loginMode === 'password' ? 'btn-primary' : 'btn-outline'}
            style={{ justifyContent: 'center', padding: '10px 12px', fontSize: 13 }}
            onClick={() => {
              setError(null);
              setLoginMode('password');
            }}
          >
            <Icon name="shield" size={16} />
            <span>رمز عبور</span>
          </button>
          <button
            type="button"
            className={loginMode === 'qr' ? 'btn-primary' : 'btn-outline'}
            style={{ justifyContent: 'center', padding: '10px 12px', fontSize: 13 }}
            onClick={() => {
              setError(null);
              setLoginMode('qr');
            }}
          >
            <Icon name="qr" size={16} />
            <span>ورود با QR Code</span>
          </button>
        </div>

        {loginMode === 'qr' ? (
          <div className="login-form" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {cameraActive ? (
              <div style={{ position: 'relative', borderRadius: 12, overflow: 'hidden', background: '#000' }}>
                <video ref={videoRef} style={{ width: '100%', maxHeight: 240, objectFit: 'cover', display: 'block' }} playsInline muted />
                <button
                  type="button"
                  className="btn-outline"
                  onClick={stopCamera}
                  style={{ width: '100%', marginTop: 8, justifyContent: 'center' }}
                >
                  توقف دوربین
                </button>
              </div>
            ) : (
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <button
                  type="button"
                  className="btn-outline"
                  onClick={() => void startCameraScan()}
                  style={{ justifyContent: 'center', padding: '12px 10px', fontSize: 13 }}
                >
                  <Icon name="qr" size={18} />
                  <span>اسکن با دوربین</span>
                </button>
                <button
                  type="button"
                  className="btn-outline"
                  onClick={() => fileInputRef.current?.click()}
                  style={{ justifyContent: 'center', padding: '12px 10px', fontSize: 13 }}
                >
                  <Icon name="upload" size={18} />
                  <span>انتخاب عکس QR</span>
                </button>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/*"
                  style={{ display: 'none' }}
                  onChange={handleQrImageUpload}
                />
              </div>
            )}

            <div className="form-group" style={{ marginTop: 4 }}>
              <label className="form-label" htmlFor="player-qr-code">
                یا کد جفت‌سازی / متن QR را وارد کنید
              </label>
              <input
                id="player-qr-code"
                type="text"
                className="form-input"
                placeholder="کد QR دریافت‌شده از مدیر..."
                value={qrInput}
                onChange={(e) => setQrInput(e.target.value)}
                dir="ltr"
              />
            </div>

            <button
              type="button"
              className="btn-primary full-width login-submit-btn"
              disabled={loading || !qrInput.trim()}
              onClick={() => void submitPairingCode(qrInput)}
            >
              {loading ? (
                <>
                  <div className="btn-spinner" />
                  <span>در حال اتصال...</span>
                </>
              ) : (
                <span>ورود سریع با کد</span>
              )}
            </button>
          </div>
        ) : (
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
            <div className="input-with-icon" style={{ position: 'relative' }}>
              <input
                id="player-pass"
                type={showPassword ? 'text' : 'password'}
                className="form-input"
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                autoComplete="current-password"
                style={{ paddingRight: 40 }}
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                aria-label={showPassword ? 'پنهان کردن رمز' : 'نمایش رمز'}
                title={showPassword ? 'پنهان کردن رمز' : 'نمایش رمز'}
                style={{
                  position: 'absolute',
                  right: 10,
                  top: '50%',
                  transform: 'translateY(-50%)',
                  background: 'none',
                  border: 'none',
                  color: 'var(--color-text-muted, #94a3b8)',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: 4,
                }}
              >
                <Icon name={showPassword ? 'eye-off' : 'eye'} size={18} />
              </button>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 12, marginBottom: 4, flexWrap: 'wrap', gap: 8 }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 13, userSelect: 'none', color: 'var(--color-text-secondary, #cbd5e1)' }}>
                <input
                  type="checkbox"
                  checked={rememberMe}
                  onChange={(e) => setRememberMe(e.target.checked)}
                  style={{
                    accentColor: 'var(--color-primary, #10b981)',
                    width: 16,
                    height: 16,
                    cursor: 'pointer'
                  }}
                />
                <span>{t('auth.rememberMe')}</span>
              </label>
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
                  color: 'var(--color-primary, #10b981)',
                  background: 'none',
                  border: 'none',
                  cursor: 'pointer',
                  padding: 0,
                  textDecoration: 'underline',
                  fontFamily: 'inherit'
                }}
              >
                {t('auth.forgotPassword')}
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
                <span>{t('auth.signingIn')}</span>
              </>
            ) : (
              <span>{t('auth.signIn')}</span>
            )}
          </button>
        </form>
        )}

        <footer className="login-footer">
          <p className="login-footer-hint">
            {t('auth.footerHint')}
          </p>
        </footer>
      </div>

      {showForgot && (
        <div className="modal-overlay" onClick={() => !forgotLoading && setShowForgot(false)}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <ThreeDIcon name="shield" size={26} />
                <h3 style={{ margin: 0, fontSize: 16 }}>{t('auth.forgotTitle')}</h3>
              </div>
              <button
                type="button"
                className="icon-btn"
                onClick={() => setShowForgot(false)}
                disabled={forgotLoading}
                title={t('action.close')}
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
                  {t('auth.forgotSuccessDetail')}
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
                  {t('auth.understood')}
                </button>
              </div>
            ) : (
              <form onSubmit={handleForgotSubmit}>
                <p style={{ margin: '0 0 14px', fontSize: 13, color: 'var(--text-muted)', lineHeight: 1.6 }}>
                  {t('auth.forgotSubtitle')}
                </p>

                {forgotError && (
                  <div className="login-error-alert" style={{ marginBottom: 14 }} role="alert">
                    <Icon name="warning" size={16} />
                    <span>{forgotError}</span>
                  </div>
                )}

                <div className="form-group" style={{ marginBottom: 16 }}>
                  <label className="form-label" htmlFor="forgot-email">
                    {t('auth.yourEmail')}
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
                    {t('action.cancel')}
                  </button>
                  <button
                    type="submit"
                    className="btn-primary"
                    disabled={forgotLoading || !forgotEmail.trim()}
                  >
                    {forgotLoading ? t('auth.sending') : t('auth.sendRequest')}
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
