/**
 * Web session storage.
 *
 * Tradeoff (documented in README): the refresh token lives in *sessionStorage*, not
 * localStorage, so it is discarded when the tab closes and is never shared between
 * tabs. The API only supports bearer/refresh-token auth (no httpOnly cookie), so an
 * XSS vulnerability could read this value; the dashboard therefore ships without
 * third-party scripts and relies on the server CSP (`script-src 'self'`).
 *
 * The access token is kept in memory only.
 */

const REFRESH_TOKEN_KEY = 'musichub.dashboard.refreshToken';

let accessToken: string | null = null;
let memoryRefreshToken: string | null = null;

function storage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null;
    return window.sessionStorage;
  } catch {
    return null;
  }
}

function readRefreshToken(): string | null {
  const store = storage();
  if (!store) return memoryRefreshToken;
  try {
    return store.getItem(REFRESH_TOKEN_KEY);
  } catch {
    return memoryRefreshToken;
  }
}

let refreshToken: string | null = readRefreshToken();

export interface TokenPair {
  accessToken: string;
  refreshToken: string;
}

export function getAccessToken(): string | null {
  return accessToken;
}

export function getRefreshToken(): string | null {
  return refreshToken;
}

export function hasStoredRefreshToken(): boolean {
  return refreshToken !== null && refreshToken.length > 0;
}

/** Applies a fresh token pair. Called after login and after every rotation. */
export function applyTokens(tokens: TokenPair): void {
  accessToken = tokens.accessToken;
  refreshToken = tokens.refreshToken;
  memoryRefreshToken = tokens.refreshToken;
  const store = storage();
  if (!store) return;
  try {
    store.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
  } catch {
    // Storage may be unavailable (private mode/quota); the in-memory token still works
    // for the lifetime of the document.
  }
}

/** Clears both tokens, including the persisted refresh token. */
export function clearTokens(): void {
  accessToken = null;
  refreshToken = null;
  memoryRefreshToken = null;
  const store = storage();
  if (!store) return;
  try {
    store.removeItem(REFRESH_TOKEN_KEY);
  } catch {
    // ignore
  }
}
