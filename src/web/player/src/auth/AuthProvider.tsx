import { useQueryClient } from '@tanstack/react-query';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { api } from '../api/endpoints';
import { http, onSessionExpired } from '../api/client';
import { applyTokens, clearTokens, getRefreshToken, hasStoredRefreshToken } from '../api/session';
import type { User } from '../api/types';

export type AuthStatus = 'booting' | 'anonymous' | 'authenticated';

interface AuthContextValue {
  status: AuthStatus;
  user: User | null;
  isAdmin: boolean;
  login: (email: string, password: string, deviceName?: string, rememberMe?: boolean) => Promise<User>;
  loginWithPairing: (code: string, deviceName?: string, rememberMe?: boolean) => Promise<User>;
  logout: () => Promise<void>;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AuthStatus>('booting');
  const [user, setUser] = useState<User | null>(null);

  useEffect(() => {
    let cancelled = false;
    const bootstrap = async () => {
      try {
        if (hasStoredRefreshToken()) {
          const token = await http.refreshAccessToken();
          if (cancelled) return;
          if (token) {
            const me = await api.auth.me();
            if (cancelled) return;
            setUser(me);
            setStatus('authenticated');
            return;
          }
        }
        if (!cancelled) setStatus('anonymous');
      } catch {
        if (!cancelled) setStatus('anonymous');
      }
    };
    void bootstrap();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(
    () =>
      onSessionExpired(() => {
        setUser(null);
        setStatus('anonymous');
        queryClient.clear();
      }),
    [queryClient],
  );

  const login = useCallback(
    async (email: string, password: string, deviceName: string = 'irpotify Mobile Web', rememberMe: boolean = true) => {
      const session = await api.auth.login({
        email,
        password,
        deviceName,
        clientType: 'player',
      });
      applyTokens(session, rememberMe);
      queryClient.clear();
      setUser(session.user);
      setStatus('authenticated');
      return session.user;
    },
    [queryClient],
  );

  const loginWithPairing = useCallback(
    async (code: string, deviceName: string = 'irpotify Player WebApp', rememberMe: boolean = true) => {
      const session = await api.pairing.claim({ code, deviceName });
      applyTokens(session, rememberMe);
      queryClient.clear();
      setUser(session.user);
      setStatus('authenticated');
      return session.user;
    },
    [queryClient],
  );

  const logout = useCallback(async () => {
    const refreshToken = getRefreshToken();
    try {
      await api.auth.logout(refreshToken);
    } catch {
      // ignore
    } finally {
      clearTokens();
      queryClient.clear();
      setUser(null);
      setStatus('anonymous');
    }
  }, [queryClient]);

  const refreshUser = useCallback(async () => {
    const me = await api.auth.me();
    setUser(me);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      isAdmin: user?.roles.includes('Administrator') ?? false,
      login,
      loginWithPairing,
      logout,
      refreshUser,
    }),
    [status, user, login, loginWithPairing, logout, refreshUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
}
