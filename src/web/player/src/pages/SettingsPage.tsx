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
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isChangingPassword, setIsChangingPassword] = useState(false);
  const [showSignoutConfirm, setShowSignoutConfirm] = useState(false);

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

  const handleInstallClick = async () => {
    if (installPrompt) {
      await installPrompt.prompt();
      const choice = await installPrompt.userChoice;
      if (choice.outcome === 'accepted') {
        setIsInstalled(true);
        notify('اپلیکیشن با موفقیت نصب شد', 'success');
      }
      setInstallPrompt(null);
    } else {
      notify(
        language === 'fa'
          ? 'از منوی مرورگر، گزینه «افزودن به صفحه اصلی» (Add to Home Screen) را انتخاب کنید.'
          : 'From your browser menu, select "Add to Home Screen".',
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
    </div>
  );
}
