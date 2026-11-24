import type { ReactNode } from 'react';
import { useUi } from './UiProvider';
import { Icon, BrandLogo } from './icons';

export function AuthShell({ children, title, subtitle }: { children: ReactNode; title: string; subtitle?: string }) {
  const { t, theme, toggleTheme, language, setLanguage } = useUi();
  return (
    <div className="auth-shell">
      <main className="auth-card" aria-labelledby="auth-title">
        <div className="auth-card-top-bar">
          <div className="auth-lang-pill">
            <button
              type="button"
              className={`auth-lang-btn ${language === 'fa' ? 'active' : ''}`}
              onClick={() => setLanguage('fa')}
            >
              فارسی
            </button>
            <button
              type="button"
              className={`auth-lang-btn ${language === 'en' ? 'active' : ''}`}
              onClick={() => setLanguage('en')}
            >
              English
            </button>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon"
            onClick={toggleTheme}
            aria-label={theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}
            title={theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}
          >
            <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
          </button>
        </div>
        <div className="auth-brand">
          <BrandLogo size={52} className="auth-brand-logo" />
          <div>
            <p className="brand-name">{t('common.appName')}</p>
            <p className="brand-tagline">{t('common.tagline')}</p>
          </div>
        </div>
        <h1 id="auth-title">{title}</h1>
        {subtitle ? <p className="auth-subtitle">{subtitle}</p> : null}
        {children}
      </main>
    </div>
  );
}
