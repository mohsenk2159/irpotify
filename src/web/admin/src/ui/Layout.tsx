import { useEffect, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { api } from '../api/endpoints';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from './UiProvider';
import { Icon, BrandLogo, type IconName } from './icons';
import { Badge, IconButton } from './primitives';
import { roleLabel } from './helpers';
import { ThreeDIcon } from './ThreeDIcons';
import { Modal } from './Modal';

interface NavItem {
  to: string;
  labelKey: Parameters<ReturnType<typeof useUi>['t']>[0];
  icon: IconName;
}

interface NavGroup {
  id: string;
  labelKey: Parameters<ReturnType<typeof useUi>['t']>[0];
  items: NavItem[];
  adminOnly?: boolean;
  contributorOnly?: boolean;
}

const NAV: NavGroup[] = [
  {
    id: 'main',
    labelKey: 'nav.dashboard',
    items: [{ to: '/dashboard', labelKey: 'nav.dashboard', icon: 'dashboard' }],
    adminOnly: true,
  },
  {
    id: 'library',
    labelKey: 'nav.library',
    items: [
      { to: '/library/tracks', labelKey: 'nav.tracks', icon: 'tracks' },
      { to: '/library/albums', labelKey: 'nav.albums', icon: 'albums' },
      { to: '/library/artists', labelKey: 'nav.artists', icon: 'artists' },
      { to: '/library/folders', labelKey: 'nav.folders', icon: 'folders' },
      { to: '/library/genres', labelKey: 'nav.genres', icon: 'genres' },
      { to: '/library/playlists', labelKey: 'nav.playlists', icon: 'playlists' },
    ],
  },
  {
    id: 'admin',
    labelKey: 'nav.administration',
    items: [
      { to: '/admin/notifications', labelKey: 'nav.notifications', icon: 'bell' },
      { to: '/admin/monitoring', labelKey: 'nav.monitoring', icon: 'monitoring' },
      { to: '/admin/users', labelKey: 'nav.users', icon: 'users' },
      { to: '/admin/settings', labelKey: 'nav.settings', icon: 'settings' },
      { to: '/admin/telegram', labelKey: 'nav.telegram', icon: 'telegram' },
      { to: '/admin/updates', labelKey: 'nav.updates', icon: 'refresh' },
      { to: '/admin/audit', labelKey: 'nav.audit', icon: 'audit' },
    ],
    adminOnly: true,
  },
  {
    id: 'upload',
    labelKey: 'nav.contribute',
    items: [{ to: '/library/upload', labelKey: 'nav.upload', icon: 'upload' }],
    contributorOnly: true,
  },
  {
    id: 'account',
    labelKey: 'nav.account',
    items: [{ to: '/account/sessions', labelKey: 'nav.sessions', icon: 'sessions' }],
  },
];

export function Layout() {
  const { t, language, setLanguage, theme, toggleTheme, formatDate } = useUi();
  const { user, isAdmin, canContribute, logout } = useAuth();
  const location = useLocation();
  const [navOpen, setNavOpen] = useState(false);

  const unreadQuery = useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: () => api.notifications.unreadCount(),
    enabled: !!user && isAdmin,
    refetchInterval: 15000,
  });
  const unreadCount = unreadQuery.data?.unreadCount ?? 0;
  const [installPrompt, setInstallPrompt] = useState<{
    prompt: () => Promise<void>;
    userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
  } | null>(null);
  const [showInstallModal, setShowInstallModal] = useState(false);

  useEffect(() => {
    const handleBeforeInstall = (e: Event) => {
      e.preventDefault();
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      setInstallPrompt(e as any);
    };
    window.addEventListener('beforeinstallprompt', handleBeforeInstall);
    return () => window.removeEventListener('beforeinstallprompt', handleBeforeInstall);
  }, []);

  const handleInstallClick = async () => {
    if (installPrompt) {
      await installPrompt.prompt();
      const choice = await installPrompt.userChoice;
      if (choice.outcome === 'accepted') {
        setInstallPrompt(null);
      }
    } else {
      setShowInstallModal(true);
    }
  };

  useEffect(() => {
    setNavOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!navOpen) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setNavOpen(false);
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [navOpen]);

  const versionQuery = useQuery({
    queryKey: ['system', 'version'],
    queryFn: api.system.version,
    staleTime: Infinity,
    retry: false,
  });

  const visibleGroups = NAV.filter((group) => {
    if (group.adminOnly && !isAdmin) return false;
    if (group.contributorOnly && !canContribute) return false;
    return true;
  });

  const allItems = visibleGroups.flatMap((group) => group.items);
  const currentItem = allItems.find(
    (item) => location.pathname === item.to || location.pathname.startsWith(`${item.to}/`),
  );
  const roleName = user ? roleLabel(t, user.role) : '';

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        {t('nav.skipToContent')}
      </a>

      {navOpen ? (
        <button
          type="button"
          className="sidebar-backdrop"
          aria-label={t('nav.closeMenu')}
          onClick={() => setNavOpen(false)}
        />
      ) : null}

      <aside className={`sidebar${navOpen ? ' is-open' : ''}`} aria-label={t('nav.mainNavigation')}>
        <div className="sidebar-brand">
          <Link to={isAdmin ? '/dashboard' : '/library/tracks'} className="brand-link">
            <BrandLogo size={34} />
            <span className="brand-text">
              <span className="brand-name">{t('common.appName')}</span>
              <span className="brand-tagline">{t('common.tagline')}</span>
            </span>
          </Link>
          <button
            type="button"
            className="btn btn-ghost btn-icon sidebar-close"
            aria-label={t('nav.closeMenu')}
            onClick={() => setNavOpen(false)}
          >
            <Icon name="close" size={18} />
          </button>
        </div>

        <nav className="sidebar-nav">
          {visibleGroups.map((group) => (
            <div key={group.id} className="nav-group">
              <p className="nav-group-label">{t(group.labelKey)}</p>
              <ul>
                {group.items.map((item) => (
                  <li key={item.to}>
                    <NavLink to={item.to} className={({ isActive }) => `nav-link${isActive ? ' is-active' : ''}`}>
                      <Icon name={item.icon} size={18} />
                      <span>{t(item.labelKey)}</span>
                      {item.to === '/admin/notifications' && unreadCount > 0 ? (
                        <span className="unread-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>
                      ) : null}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="preference-row">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => setLanguage(language === 'en' ? 'fa' : 'en')}
              aria-label={t('nav.language')}
              title={t('nav.language')}
            >
              <span aria-hidden="true">{language === 'en' ? 'فا' : 'EN'}</span>
              <span className="sr-only">{t('nav.language')}</span>
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={toggleTheme}
              aria-label={theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}
              aria-pressed={theme === 'dark'}
              title={theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}
            >
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={17} />
              <span>{theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}</span>
            </button>
          </div>
          {user ? (
            <div className="user-card">
              <span className="avatar" aria-hidden="true">
                {user.displayName.trim().charAt(0).toUpperCase() || '?'}
              </span>
              <div className="user-card-text">
                <span className="user-name" title={user.email}>
                  {user.displayName}
                </span>
                <Badge tone={user.role === 'Administrator' ? 'warning' : 'info'}>{roleName}</Badge>
              </div>
              <IconButton icon="signout" label={t('nav.signOut')} onClick={() => void logout()} />
            </div>
          ) : null}
          <p className="sidebar-version">
            {versionQuery.data
              ? t('nav.version', { version: versionQuery.data.version })
              : versionQuery.isError
                ? t('common.unknown')
                : ''}
            {versionQuery.data ? <span className="sidebar-env"> · {versionQuery.data.environment}</span> : null}
          </p>
        </div>
      </aside>

      <div className="app-column">
        <header className="topbar">
          <button
            type="button"
            className="btn btn-ghost btn-icon menu-button"
            aria-label={t('nav.openMenu')}
            aria-expanded={navOpen}
            onClick={() => setNavOpen(true)}
          >
            <Icon name="menu" size={20} />
          </button>
          <div className="topbar-title">
            <span className="topbar-section">{currentItem ? t(currentItem.labelKey) : t('common.appName')}</span>
            {user ? <span className="topbar-user">{t('nav.signedInAs', { name: user.displayName })}</span> : null}
          </div>
          <div className="topbar-actions">
            {isAdmin && unreadCount > 0 ? (
              <Link
                to="/admin/notifications"
                className="topbar-notification-alert"
                title={`شما ${unreadCount} اعلان جدید دارید`}
              >
                <span className="notification-pulse" />
                <Icon name="bell" size={16} />
                <span className="hide-on-mobile">{unreadCount} اعلان</span>
              </Link>
            ) : null}
            {user?.lastLoginAt ? (
              <span className="topbar-login" title={t('common.lastLogin')}>
                {formatDate(user.lastLoginAt)}
              </span>
            ) : null}
            <button
              type="button"
              className="btn btn-ghost btn-sm install-button"
              onClick={handleInstallClick}
              aria-label={t('nav.installApp')}
              title={t('nav.installApp')}
              style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
            >
              <ThreeDIcon type="rocket" size={20} />
              <span className="hide-on-mobile">{t('nav.installApp')}</span>
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              onClick={toggleTheme}
              aria-label={theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}
              title={theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')}
            >
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-sm lang-button"
              onClick={() => setLanguage(language === 'en' ? 'fa' : 'en')}
              aria-label={t('nav.language')}
              title={t('nav.language')}
            >
              {language === 'en' ? 'FA' : 'EN'}
            </button>
          </div>
        </header>

        <main id="main" className="app-main" tabIndex={-1}>
          <Outlet />
        </main>

        {/* Admin PWA Mobile Bottom Navigation Bar */}
        <nav className="mobile-bottom-nav" aria-label="Mobile Navigation">
          <NavLink
            to="/dashboard"
            className={({ isActive }) => `mobile-nav-link${isActive ? ' is-active' : ''}`}
          >
            <ThreeDIcon type="waves" size={24} />
            <span>{t('nav.dashboard')}</span>
          </NavLink>
          <NavLink
            to="/library/tracks"
            className={({ isActive }) => `mobile-nav-link${isActive ? ' is-active' : ''}`}
          >
            <ThreeDIcon type="album" size={24} />
            <span>{t('nav.tracks')}</span>
          </NavLink>
          <NavLink
            to="/library/folders"
            className={({ isActive }) => `mobile-nav-link${isActive ? ' is-active' : ''}`}
          >
            <ThreeDIcon type="cassette" size={24} />
            <span>{t('nav.folders')}</span>
          </NavLink>
          <NavLink
            to="/admin/monitoring"
            className={({ isActive }) => `mobile-nav-link${isActive ? ' is-active' : ''}`}
          >
            <ThreeDIcon type="rocket" size={24} />
            <span>{t('nav.monitoring')}</span>
          </NavLink>
          <NavLink
            to="/admin/users"
            className={({ isActive }) => `mobile-nav-link${isActive ? ' is-active' : ''}`}
          >
            <ThreeDIcon type="users" size={24} />
            <span>{t('nav.users')}</span>
          </NavLink>
          <NavLink
            to="/admin/settings"
            className={({ isActive }) => `mobile-nav-link${isActive ? ' is-active' : ''}`}
          >
            <ThreeDIcon type="disk" size={24} />
            <span>{t('nav.settings')}</span>
          </NavLink>
        </nav>

        {showInstallModal ? (
          <Modal
            open={showInstallModal}
            title={t('install.title')}
            onClose={() => setShowInstallModal(false)}
          >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', paddingBottom: '0.75rem', borderBottom: '1px solid rgba(255,255,255,0.08)' }}>
                <ThreeDIcon type="rocket" size={48} animated />
                <div>
                  <h4 style={{ margin: 0, fontSize: '1.1rem', fontWeight: 600 }}>{t('common.appName')}</h4>
                  <p style={{ margin: '0.25rem 0 0', opacity: 0.8, fontSize: '0.85rem' }}>
                    {/android/i.test(navigator.userAgent)
                      ? t('install.detectAndroid')
                      : /iphone|ipad|ipod/i.test(navigator.userAgent)
                      ? t('install.detectIos')
                      : t('install.detectDesktop')}
                  </p>
                </div>
              </div>

              {/android/i.test(navigator.userAgent) ? (
                <div className="card" style={{ padding: '1rem', background: 'rgba(56, 189, 248, 0.08)', borderRadius: '10px', border: '1px solid rgba(56, 189, 248, 0.25)' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem', fontWeight: 700, color: '#38bdf8' }}>
                    <ThreeDIcon type="disk" size={22} />
                    <span>{t('install.androidTitle')}</span>
                  </div>
                  <p style={{ margin: '0 0 0.75rem', fontSize: '0.88rem', lineHeight: 1.5, opacity: 0.9 }}>
                    {t('install.androidStep')}
                  </p>
                  <a
                    href="https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk"
                    target="_blank"
                    rel="noreferrer"
                    className="btn btn-primary"
                    style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem', width: '100%', justifyContent: 'center' }}
                  >
                    <span>{t('install.downloadApk')}</span>
                  </a>
                </div>
              ) : null}

              {/iphone|ipad|ipod/i.test(navigator.userAgent) ? (
                <div className="card" style={{ padding: '1rem', background: 'rgba(192, 132, 252, 0.08)', borderRadius: '10px', border: '1px solid rgba(192, 132, 252, 0.25)' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem', fontWeight: 700, color: '#c084fc' }}>
                    <ThreeDIcon type="headphones" size={22} />
                    <span>{t('install.iosTitle')}</span>
                  </div>
                  <ol style={{ margin: 0, paddingRight: '1.25rem', paddingLeft: '1.25rem', fontSize: '0.88rem', lineHeight: 1.6, opacity: 0.95 }}>
                    <li>{t('install.iosStep1')}</li>
                    <li>{t('install.iosStep2')}</li>
                    <li>{t('install.iosStep3')}</li>
                  </ol>
                </div>
              ) : null}

              <div className="card" style={{ padding: '0.85rem 1rem', background: 'rgba(255,255,255,0.03)', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.06)' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.35rem', fontWeight: 600, color: '#1ed760' }}>
                  <ThreeDIcon type="waves" size={20} />
                  <span>{t('install.desktopTitle')}</span>
                </div>
                <p style={{ margin: 0, fontSize: '0.88rem', lineHeight: 1.5, opacity: 0.9 }}>
                  {t('install.desktopStep')}
                </p>
              </div>

              <p style={{ margin: '0.25rem 0 0', fontSize: '0.8rem', opacity: 0.65, lineHeight: 1.4, fontStyle: 'italic' }}>
                {t('install.lanTip')}
              </p>
            </div>
          </Modal>
        ) : null}
      </div>
    </div>
  );
}

export function PageShell({ children }: { children: ReactNode }) {
  return <div className="page">{children}</div>;
}
