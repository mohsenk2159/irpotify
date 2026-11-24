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
import type { SetupStatus, User } from '../api/types';
import { useToast } from '../ui/Modal';
import { useUi } from '../ui/UiProvider';

export type AuthStatus = 'booting' | 'anonymous' | 'authenticated';

interface AuthContextValue {
  status: AuthStatus;
  user: User | null;
  setup: SetupStatus | null;
  bootError: unknown;
  isAdmin: boolean;
  /**
   * Whether the current user may change library content. Only administrators may: Contributor is
   * read-only for the shared library, and the API enforces the same rule, so hiding these controls
   * is a usability measure rather than the authorization boundary.
   */
  canContribute: boolean;
  login: (email: string, password: string, deviceName: string, rememberMe?: boolean) => Promise<User>;
  logout: () => Promise<void>;
  logoutAll: () => Promise<void>;
  refreshUser: () => Promise<void>;
  reloadSetup: () => Promise<SetupStatus>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const { t } = useUi();
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AuthStatus>('booting');
  const [user, setUser] = useState<User | null>(null);
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [bootError, setBootError] = useState<unknown>(null);

  useEffect(() => {
    let cancelled = false;
    const bootstrap = async () => {
      try {
        const setupStatus = await api.setup.status();
        if (cancelled) return;
        setSetup(setupStatus);
        if (!setupStatus.initialized) {
          setStatus('anonymous');
          return;
        }
        if (hasStoredRefreshToken()) {
          const token = await http.refreshAccessToken();
          if (cancelled) return;
          if (token) {
            const me = await api.auth.me();
            if (cancelled) return;
            if (!me.roles.includes('Administrator')) {
              clearTokens();
              setStatus('anonymous');
              return;
            }
            setUser(me);
            setStatus('authenticated');
            return;
          }
        }
        if (!cancelled) setStatus('anonymous');
      } catch (error) {
        if (cancelled) return;
        setBootError(error);
        setStatus('anonymous');
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
        notify(t('auth.sessionExpired'), 'error');
      }),
    [notify, queryClient, t],
  );

  const login = useCallback(
    async (email: string, password: string, deviceName: string, rememberMe: boolean = true) => {
      const session = await api.auth.login({ email, password, deviceName, clientType: 'admin' });
      if (!session.user.roles.includes('Administrator')) {
        throw new Error('دسترسی غیرمجاز: این پنل منحصراً برای مدیران سیستم است.');
      }
      applyTokens(session, rememberMe);
      queryClient.clear();
      setUser(session.user);
      setStatus('authenticated');
      setBootError(null);
      return session.user;
    },
    [queryClient],
  );

  const logout = useCallback(async () => {
    const refreshToken = getRefreshToken();
    try {
      await api.auth.logout(refreshToken);
    } catch {
      notify(t('auth.logoutFailed'), 'error');
    } finally {
      clearTokens();
      queryClient.clear();
      setUser(null);
      setStatus('anonymous');
    }
  }, [notify, queryClient, t]);

  const logoutAll = useCallback(async () => {
    try {
      await api.auth.logoutAll();
    } catch {
      notify(t('errors.unexpected'), 'error');
    } finally {
      clearTokens();
      queryClient.clear();
      setUser(null);
      setStatus('anonymous');
    }
  }, [notify, queryClient, t]);

  const refreshUser = useCallback(async () => {
    const me = await api.auth.me();
    setUser(me);
  }, []);

  const reloadSetup = useCallback(async () => {
    const setupStatus = await api.setup.status();
    setSetup(setupStatus);
    return setupStatus;
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      setup,
      bootError,
      isAdmin: user?.roles.includes('Administrator') ?? false,
      canContribute: user ? user.roles.includes('Administrator') : false,
      login,
      logout,
      logoutAll,
      refreshUser,
      reloadSetup,
    }),
    [status, user, setup, bootError, login, logout, logoutAll, refreshUser, reloadSetup],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
}
