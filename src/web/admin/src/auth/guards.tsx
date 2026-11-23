import { Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from './AuthProvider';
import { useUi } from '../ui/UiProvider';
import { Button, ErrorState } from '../ui/primitives';
import { Icon } from '../ui/icons';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export function BootScreen({ error }: { error?: unknown }) {
  const { t } = useUi();
  return (
    <div className="boot-screen">
      <div className="brand-mark brand-mark-lg" aria-hidden="true">
        <Icon name="music" size={30} />
      </div>
      <p className="brand-name">{t('common.appName')}</p>
      {error ? (
        <ErrorState error={error} onRetry={() => window.location.reload()} />
      ) : (
        <div className="state-block" role="status" aria-live="polite">
          <span className="spinner" aria-hidden="true" />
          <p>{t('common.loading')}</p>
        </div>
      )}
    </div>
  );
}

export function RequireAuth() {
  const { status, setup, bootError } = useAuth();
  const location = useLocation();

  if (status === 'booting') return <BootScreen error={bootError} />;
  if (setup && !setup.initialized) return <Navigate to="/setup" replace />;
  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  return <Outlet />;
}

export function RequireAdmin({ children }: { children: ReactNode }) {
  const { isAdmin } = useAuth();
  if (!isAdmin) {
    return <AccessDenied />;
  }
  return <>{children}</>;
}

export function RequireContributor({ children }: { children: ReactNode }) {
  const { canContribute } = useAuth();
  if (!canContribute) return <AccessDenied />;
  return <>{children}</>;
}

export function AccessDenied() {
  const { t } = useUi();
  const { logout } = useAuth();
  return (
    <div className="access-denied" style={{ padding: '2rem 1rem', textAlign: 'center', maxWidth: 480, margin: '2rem auto' }}>
      <div style={{ marginBottom: '1rem', display: 'flex', justifyContent: 'center' }}>
        <ThreeDIcon type="shield" size={64} animated />
      </div>
      <h3 style={{ color: '#ef4444', marginBottom: '0.75rem' }}>{t('errors.forbidden')}</h3>
      <p style={{ opacity: 0.85, lineHeight: 1.6, marginBottom: '1.5rem' }}>
        دسترسی غیرمجاز: این پنل منحصراً برای مدیران سیستم است. لطفاً از وب‌اپلیکیشن پلیر یا برنامه موبایل استفاده کنید.
      </p>
      <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'center' }}>
        <Button variant="danger" onClick={() => void logout()}>
          {t('nav.signOut')}
        </Button>
      </div>
    </div>
  );
}

export function SetupRoute({ children }: { children: ReactNode }) {
  const { status, setup, bootError } = useAuth();
  if (status === 'booting') return <BootScreen error={bootError} />;
  if (setup?.initialized) {
    return <Navigate to={status === 'authenticated' ? '/dashboard' : '/login'} replace />;
  }
  return <>{children}</>;
}

export function LoginRoute({ children }: { children: ReactNode }) {
  const { status, setup, bootError } = useAuth();
  if (status === 'booting') return <BootScreen error={bootError} />;
  if (setup && !setup.initialized) return <Navigate to="/setup" replace />;
  if (status === 'authenticated') return <Navigate to="/dashboard" replace />;
  return <>{children}</>;
}

export function NotFoundPage() {
  const { t } = useUi();
  const navigate = useNavigate();
  return (
    <div className="state-block empty-state">
      <span className="state-icon" aria-hidden="true">
        <Icon name="warning" size={28} />
      </span>
      <h2>{t('notFound.title')}</h2>
      <p>{t('notFound.body')}</p>
      <Button variant="primary" onClick={() => navigate('/dashboard')}>
        {t('errors.backToDashboard')}
      </Button>
    </div>
  );
}
