import type { AuthSession } from './types';
import { createHttpClient } from './http';
import { applyTokens, clearTokens, getAccessToken, getRefreshToken } from './session';

type Listener = () => void;
const expiredListeners = new Set<Listener>();

/** Registers a callback fired once when the session can no longer be refreshed. */
export function onSessionExpired(listener: Listener): () => void {
  expiredListeners.add(listener);
  return () => expiredListeners.delete(listener);
}

function notifyExpired(): void {
  clearTokens();
  for (const listener of expiredListeners) listener();
}

export const http = createHttpClient({
  getAccessToken,
  getRefreshToken,
  performRefresh: (refreshToken) =>
    http.request<AuthSession>('/api/auth/refresh', {
      method: 'POST',
      json: { refreshToken },
      auth: false,
    }),
  onSessionRefreshed: applyTokens,
  onSessionExpired: notifyExpired,
});
