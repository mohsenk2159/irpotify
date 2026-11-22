import { useEffect, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { api } from '../api/endpoints';
import { useAuth } from '../auth/AuthProvider';
import { useUi } from './UiProvider';
import { Icon, BrandLogo, type IconName } from './icons';
import { Badge, IconButton } from './primitives';
import { roleLabel } from './helpers';

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
    id: 'upload',
    labelKey: 'nav.contribute',
    items: [{ to: '/library/upload', labelKey: 'nav.upload', icon: 'upload' }],
    contributorOnly: true,
  },
  {
    id: 'admin',
    labelKey: 'nav.administration',
    items: [
      { to: '/admin/users', labelKey: 'nav.users', icon: 'users' },
      { to: '/admin/settings', labelKey: 'nav.settings', icon: 'settings' },
      { to: '/admin/telegram', labelKey: 'nav.telegram', icon: 'telegram' },
      { to: '/admin/monitoring', labelKey: 'nav.monitoring', icon: 'monitoring' },
      { to: '/admin/audit', labelKey: 'nav.audit', icon: 'audit' },
    ],
    adminOnly: true,
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
            {user?.lastLoginAt ? (
              <span className="topbar-login" title={t('common.lastLogin')}>
                {formatDate(user.lastLoginAt)}
              </span>
            ) : null}
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
      </div>
    </div>
  );
}

export function PageShell({ children }: { children: ReactNode }) {
  return <div className="page">{children}</div>;
}
