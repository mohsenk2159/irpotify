import { Navigate, Outlet } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from './AuthProvider';
import { ThreeDIcon } from '../ui/ThreeDIcons';

export function BootScreen() {
  return (
    <div className="boot-screen">
      <ThreeDIcon type="vinyl" size={80} animated />
      <h2 style={{ marginTop: 16, color: '#1ed760', fontWeight: 700 }}>irpotify</h2>
      <p style={{ opacity: 0.7, fontSize: 14 }}>در حال بارگذاری…</p>
    </div>
  );
}

export function RequireAuth() {
  const { status } = useAuth();
  if (status === 'booting') return <BootScreen />;
  if (status === 'anonymous') return <Navigate to="/login" replace />;
  return <Outlet />;
}

export function LoginRoute({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  if (status === 'booting') return <BootScreen />;
  if (status === 'authenticated') return <Navigate to="/" replace />;
  return <>{children}</>;
}
