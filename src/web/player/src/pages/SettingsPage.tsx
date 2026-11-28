import { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from '../ui/UiProvider';
import { Icon } from '../ui/icons';
import { ThreeDIcon } from '../ui/ThreeDIcons';
import { Modal } from '../ui/Modal';
import { api } from '../api/endpoints';

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

export function SettingsPage() {
  const { user, logout } = useAuth();
  const { language, setLanguage, theme, toggleTheme, t, notify } = useUi();

  const [installPrompt, setInstallPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isInstalled, setIsInstalled] = useState(false);
  const [showInstallModal, setShowInstallModal] = useState(false);
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isChangingPassword, setIsChangingPassword] = useState(false);
  const [showSignoutConfirm, setShowSignoutConfirm] = useState(false);

  const isAndroid = typeof navigator !== 'undefined' && /android/i.test(navigator.userAgent);
  const isIOS = typeof navigator !== 'undefined' && /iphone|ipad|ipod/i.test(navigator.userAgent);
  const isWindows = typeof navigator !== 'undefined' && /windows/i.test(navigator.userAgent);

  useEffect(() => {
    // Check if already in standalone PWA mode
    const isStandalone =
      window.matchMedia('(display-mode: standalone)').matches ||
      (window.navigator as unknown as { standalone?: boolean }).standalone === true;
    setIsInstalled(isStandalone);

    const handleBeforeInstall = (e: Event) => {
      e.preventDefault();
      setInstallPrompt(e as BeforeInstallPromptEvent);
    };

    window.addEventListener('beforeinstallprompt', handleBeforeInstall);
    return () => {
      window.removeEventListener('beforeinstallprompt', handleBeforeInstall);
    };
  }, []);

  const handleInstallClick = () => {
    setShowInstallModal(true);
  };

  const handlePwaDirectInstall = async () => {
    if (installPrompt) {
      await installPrompt.prompt();
      const choice = await installPrompt.userChoice;
      if (choice.outcome === 'accepted') {
        setIsInstalled(true);
        notify('اپلیکیشن با موفقیت روی دستگاه شما نصب شد', 'success');
        setShowInstallModal(false);
      }
      setInstallPrompt(null);
    } else if (typeof window !== 'undefined' && !window.isSecureContext) {
      notify(
        language === 'fa'
          ? 'نصب مستقیم PWA در مرورگرهای اندروید فقط روی HTTPS فعال است. در حالت HTTP از منوی مرورگر (≡ یا ⋮) گزینه «افزودن به صفحه اصلی / Add page to Home screen» را بزنید.'
          : 'Direct PWA install requires HTTPS on Android. On HTTP, use your browser menu → "Add page to Home screen".',
        'info'
      );
    } else {
      notify(
        language === 'fa'
          ? 'در منوی مرورگر، گزینه «افزودن به صفحه اصلی» (Add to Home screen) را انتخاب کنید.'
          : 'From browser menu, select "Add to Home Screen".',
        'info'
      );
    }
  };

  const handleChangePassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newPassword || newPassword.length < 6) {
      notify(language === 'fa' ? 'رمز عبور باید حداقل ۶ کاراکتر باشد' : 'Password must be at least 6 characters', 'error');
      return;
    }
    if (newPassword !== confirmPassword) {
      notify(language === 'fa' ? 'رمز عبور با تکرار آن یکسان نیست' : 'Passwords do not match', 'error');
      return;
    }

    if (!user?.id) return;

    try {
      setIsChangingPassword(true);
      await api.users.resetPassword(user.id, newPassword);
      notify(language === 'fa' ? 'رمز عبور با موفقیت تغییر کرد' : 'Password updated successfully', 'success');
      setShowPasswordModal(false);
      setNewPassword('');
      setConfirmPassword('');
    } catch {
      notify(language === 'fa' ? 'خطا در تغییر رمز عبور' : 'Failed to change password', 'error');
    } finally {
      setIsChangingPassword(false);
    }
  };

  return (
    <div className="settings-page">
      <header className="page-header">
        <h1 className="page-title">{t('settings.title')}</h1>
      </header>

      {/* User Profile Card */}
      <section className="settings-card profile-card">
        <div className="profile-avatar">
          <ThreeDIcon name="user" size={54} />
        </div>
        <div className="profile-details">
          <h2 className="profile-name">{user?.displayName || 'کاربر گرامی'}</h2>
          <span className="profile-email">{user?.email}</span>
          <span className="profile-role-badge">
            {user?.role === 'Administrator' ? '👑 مدیر ارشد' : '🎧 شنونده'}
          </span>
        </div>
        <button
          className="btn-outline small"
          onClick={() => setShowPasswordModal(true)}
        >
          <Icon name="edit" size={14} />
          <span>{t('settings.changePassword')}</span>
        </button>
      </section>

      {/* PWA Install Banner */}
      {!isInstalled && (
        <section className="settings-card pwa-card">
          <div className="pwa-icon-box">
            <ThreeDIcon name="lightning" size={38} />
          </div>
          <div className="pwa-info">
            <h3 className="pwa-title">نصب وب‌اپلیکیشن روی صفحه اصلی</h3>
            <p className="pwa-desc">
              برای دسترسی سریع، آیکون اختصاصی و تجربه تمام‌صفحه بدون نوار مرورگر، اپلیکیشن را نصب کنید.
            </p>
          </div>
          <button className="btn-primary install-action-btn" onClick={handleInstallClick}>
            <Icon name="arrow-down" size={16} />
            <span>نصب اپ</span>
          </button>
        </section>
      )}

      {/* Preferences Section */}
      <section className="settings-section">
        <h3 className="section-title">تنظیمات کاربری و ظاهر</h3>
        <div className="settings-card">
          {/* Language Row */}
          <div className="setting-row">
            <div className="setting-label-group">
              <Icon name="external" size={20} />
              <div>
                <span className="setting-title">{t('settings.language')}</span>
                <span className="setting-subtitle">انتخاب زبان محیط برنامه</span>
              </div>
            </div>
            <div className="lang-switcher">
              <button
                className={`lang-chip ${language === 'fa' ? 'active' : ''}`}
                onClick={() => setLanguage('fa')}
              >
                فارسی
              </button>
              <button
                className={`lang-chip ${language === 'en' ? 'active' : ''}`}
                onClick={() => setLanguage('en')}
              >
                English
              </button>
            </div>
          </div>

          <div className="divider" />

          {/* Theme Row */}
          <div className="setting-row">
            <div className="setting-label-group">
              <Icon name={theme === 'dark' ? 'moon' : 'sun'} size={20} />
              <div>
                <span className="setting-title">{t('settings.theme')}</span>
                <span className="setting-subtitle">
                  {theme === 'dark' ? 'حالت شب (تاریک مدرن)' : 'حالت روز (روشن)'}
                </span>
              </div>
            </div>
            <button className="theme-toggle-btn" onClick={toggleTheme}>
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
              <span>{theme === 'dark' ? 'تم روشن' : 'تم تیره'}</span>
            </button>
          </div>
        </div>
      </section>

      {/* About App & Connection */}
      <section className="settings-section">
        <h3 className="section-title">درباره برنامه</h3>
        <div className="settings-card info-card">
          <div className="info-row">
            <span className="info-label">نسخه برنامه</span>
            <span className="info-value">irpotify Player v1.0.0</span>
          </div>
          <div className="divider" />
          <div className="info-row">
            <span className="info-label">وضعیت اتصال</span>
            <span className="info-value status-online">
              <span className="pulse-dot" /> متصل به سرور ابری
            </span>
          </div>
          <div className="divider" />
          <div className="info-row">
            <span className="info-label">نوع کلاینت</span>
            <span className="info-value">Standalone PWA Mobile Player</span>
          </div>
        </div>
      </section>

      {/* Sign Out Button */}
      <section className="settings-section">
        <button
          className="btn-danger-outline full-width"
          onClick={() => setShowSignoutConfirm(true)}
        >
          <Icon name="signout" size={20} />
          <span>{t('settings.signOut')}</span>
        </button>
      </section>

      {/* Change Password Modal */}
      <Modal
        isOpen={showPasswordModal}
        onClose={() => setShowPasswordModal(false)}
        title={t('settings.changePassword')}
      >
        <form onSubmit={handleChangePassword} className="modal-form">
          <div className="form-group">
            <label className="form-label">رمز عبور جدید</label>
            <input
              type="password"
              className="form-input"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="حداقل ۶ کاراکتر"
              required
              minLength={6}
            />
          </div>

          <div className="form-group">
            <label className="form-label">تکرار رمز عبور جدید</label>
            <input
              type="password"
              className="form-input"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="تکرار رمز عبور"
              required
              minLength={6}
            />
          </div>

          <div className="modal-actions">
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setShowPasswordModal(false)}
            >
              {t('action.cancel')}
            </button>
            <button
              type="submit"
              className="btn-primary"
              disabled={isChangingPassword}
            >
              {isChangingPassword ? 'در حال ذخیره…' : t('action.save')}
            </button>
          </div>
        </form>
      </Modal>

      {/* Signout Confirmation Modal */}
      <Modal
        isOpen={showSignoutConfirm}
        onClose={() => setShowSignoutConfirm(false)}
        title={t('settings.signOut')}
      >
        <div className="signout-confirm-body">
          <p>{t('settings.signOutConfirm')}</p>
          <div className="modal-actions">
            <button
              className="btn-secondary"
              onClick={() => setShowSignoutConfirm(false)}
            >
              {t('action.cancel')}
            </button>
            <button
              className="btn-danger"
              onClick={() => {
                setShowSignoutConfirm(false);
                logout();
              }}
            >
              {t('settings.signOut')}
            </button>
          </div>
        </div>
      </Modal>

      {/* Smart OS-Aware Install App Modal */}
      <Modal
        isOpen={showInstallModal}
        onClose={() => setShowInstallModal(false)}
        title="نصب اپلیکیشن irpotify"
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', padding: '0.25rem 0' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.85rem', paddingBottom: '0.75rem', borderBottom: '1px solid rgba(255,255,255,0.08)' }}>
            <ThreeDIcon name="lightning" size={44} />
            <div>
              <h4 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700, color: '#f8fafc' }}>
                {isAndroid
                  ? '📱 گوشی شما: اندروید (Android)'
                  : isIOS
                  ? '🍏 گوشی شما: آیفون / آیپد (iOS)'
                  : '💻 سیستم شما: رایانه ویندوز / دسکتاپ'}
              </h4>
              <p style={{ margin: '0.2rem 0 0', fontSize: '0.82rem', color: '#94a3b8' }}>
                راهنمای نصب متناسب با سیستم‌عامل دستگاه شما
              </p>
            </div>
          </div>

          {/* Android Section */}
          {isAndroid && (
            <div style={{ background: 'rgba(34, 197, 94, 0.08)', border: '1px solid rgba(34, 197, 94, 0.25)', borderRadius: '10px', padding: '1rem' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem', fontWeight: 700, color: '#4ade80' }}>
                <ThreeDIcon name="lightning" size={20} />
                <span>۱. دانلود مستقیم اپلیکیشن اندروید (پیشنهادی)</span>
              </div>
              <p style={{ margin: '0 0 0.85rem', fontSize: '0.86rem', lineHeight: 1.5, color: '#e2e8f0' }}>
                نسخه بومی اندروید دارای پخش پیوسته در پس‌زمینه (حتی در حالت خاموشی صفحه)، کنترلر در قفل صفحه و کار با دکمه‌های هندزفری است.
              </p>
              <a
                href="https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk"
                target="_blank"
                rel="noreferrer"
                className="btn-primary"
                style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem', width: '100%', textDecoration: 'none', padding: '0.75rem' }}
              >
                <Icon name="arrow-down" size={16} />
                <span>دانلود مستقیم فایل irpotify-android.apk (4.9 MB)</span>
              </a>

              <div style={{ marginTop: '1rem', paddingTop: '0.85rem', borderTop: '1px solid rgba(255,255,255,0.08)' }}>
                <div style={{ fontWeight: 600, fontSize: '0.86rem', color: '#cbd5e1', marginBottom: '0.35rem' }}>
                  ۲. یا نصب به عنوان وب‌اپلیکیشن (PWA):
                </div>
                <p style={{ margin: '0 0 0.65rem', fontSize: '0.82rem', color: '#94a3b8', lineHeight: 1.5 }}>
                  در منوی سه‌نقطه بالای مرورگر کروم، گزینه <strong>«افزودن به صفحه اصلی» (Add to Home screen)</strong> یا <strong>«نصب برنامه»</strong> را انتخاب کنید.
                </p>
                {installPrompt && (
                  <button
                    className="btn-outline"
                    style={{ width: '100%', padding: '0.6rem' }}
                    onClick={handlePwaDirectInstall}
                  >
                    نصب خودکار وب‌اپ در صفحه اصلی
                  </button>
                )}
              </div>
            </div>
          )}

          {/* iOS Section */}
          {isIOS && (
            <div style={{ background: 'rgba(168, 85, 247, 0.08)', border: '1px solid rgba(168, 85, 247, 0.25)', borderRadius: '10px', padding: '1rem' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem', fontWeight: 700, color: '#c084fc' }}>
                <ThreeDIcon name="lightning" size={20} />
                <span>نصب در آیفون و آیپد (Apple iOS)</span>
              </div>
              <p style={{ margin: '0 0 0.65rem', fontSize: '0.85rem', color: '#e2e8f0' }}>
                برای نصب برنامه در آیفون کافیست مراحل ساده زیر را در مرورگر <strong>Safari</strong> انجام دهید:
              </p>
              <ol style={{ margin: 0, paddingRight: '1.25rem', fontSize: '0.85rem', lineHeight: 1.6, color: '#cbd5e1' }}>
                <li>در پایین صفحه Safari روی دکمه <strong>Share</strong> (مربع با علامت فلش رو به بالا) بزنید.</li>
                <li>در لیست به پایین رفته و گزینه <strong>Add to Home Screen (افزودن به صفحه اصلی)</strong> را انتخاب کنید.</li>
                <li>در گوشه بالا دکمه <strong>Add</strong> را لمس کنید.</li>
              </ol>
              <p style={{ margin: '0.65rem 0 0', fontSize: '0.82rem', color: '#94a3b8' }}>
                آیکون برنامه روی صفحه آیفون قرار گرفته و به صورت تمام‌صفحه بدون نوار مرورگر اجرا می‌شود.
              </p>
            </div>
          )}

          {/* Desktop Section */}
          {(isWindows || (!isAndroid && !isIOS)) && (
            <div style={{ background: 'rgba(56, 189, 248, 0.08)', border: '1px solid rgba(56, 189, 248, 0.25)', borderRadius: '10px', padding: '1rem' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem', fontWeight: 700, color: '#38bdf8' }}>
                <ThreeDIcon name="lightning" size={20} />
                <span>دانلود نرم‌افزار دسکتاپ ویندوز</span>
              </div>
              <p style={{ margin: '0 0 0.85rem', fontSize: '0.86rem', lineHeight: 1.5, color: '#e2e8f0' }}>
                نرم‌افزار رسمی ویندوز دارای موتور صوتی اختصاصی LibVLC با کیفیت فوق‌العاده بالا و شورت‌کات دسکتاپ است.
              </p>
              <a
                href="https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-player-win-x64-1.0.0-setup.exe"
                target="_blank"
                rel="noreferrer"
                className="btn-primary"
                style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem', width: '100%', textDecoration: 'none', padding: '0.75rem' }}
              >
                <Icon name="arrow-down" size={16} />
                <span>دانلود نسخه رسمی ویندوز (Setup EXE)</span>
              </a>
              {installPrompt && (
                <button
                  className="btn-outline"
                  style={{ width: '100%', marginTop: '0.75rem', padding: '0.6rem' }}
                  onClick={handlePwaDirectInstall}
                >
                  نصب به عنوان وب‌اپلیکیشن دسکتاپ
                </button>
              )}
            </div>
          )}

          <p style={{ margin: 0, fontSize: '0.78rem', color: '#64748b', lineHeight: 1.4, fontStyle: 'italic' }}>
            نکته فنی: در اتصالات بر پایه IP مستقیم (پروتکل HTTP)، مرورگر کروم اجازه نصب مستقیم وب‌اپ PWA را محدود می‌کند؛ در صورت مشاهده خطای مرورگر، حتماً از گزینه دانلود مستقیم APK اندروید استفاده نمایید.
          </p>
        </div>
      </Modal>
    </div>
  );
}
