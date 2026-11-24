/**
 * Web session storage with Remember Me support.
 *
 * When "Remember Me" is selected, the refresh token is persisted in localStorage,
 * keeping the user logged in across browser restarts until they explicitly log out.
 * When unchecked, it is stored in sessionStorage only (discarded on tab close).
 * The access token is always kept in memory only.
 */

const REFRESH_TOKEN_KEY = 'musichub.dashboard.refreshToken';
const REMEMBER_ME_KEY = 'musichub.rememberMe';

let accessToken: string | null = null;
let memoryRefreshToken: string | null = null;

function getLocalStorage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null;
    return window.localStorage;
  } catch {
    return null;
  }
}

function getSessionStorage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null;
    return window.sessionStorage;
  } catch {
    return null;
  }
}

export function isRememberMeActive(): boolean {
  const local = getLocalStorage();
  if (!local) return false;
  try {
    return local.getItem(REMEMBER_ME_KEY) === 'true';
  } catch {
    return false;
  }
}

function readRefreshToken(): string | null {
  const local = getLocalStorage();
  if (local) {
    try {
      const val = local.getItem(REFRESH_TOKEN_KEY);
      if (val) return val;
    } catch {
      // ignore
    }
  }
  const session = getSessionStorage();
  if (session) {
    try {
      const val = session.getItem(REFRESH_TOKEN_KEY);
      if (val) return val;
    } catch {
      // ignore
    }
  }
  return memoryRefreshToken;
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
export function applyTokens(tokens: TokenPair, rememberMe?: boolean): void {
  accessToken = tokens.accessToken;
  refreshToken = tokens.refreshToken;
  memoryRefreshToken = tokens.refreshToken;

  const local = getLocalStorage();
  const session = getSessionStorage();

  const shouldRemember = rememberMe !== undefined ? rememberMe : isRememberMeActive();

  if (local) {
    try {
      if (shouldRemember) {
        local.setItem(REMEMBER_ME_KEY, 'true');
        local.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
      } else {
        local.removeItem(REMEMBER_ME_KEY);
        local.removeItem(REFRESH_TOKEN_KEY);
      }
    } catch {
      // ignore
    }
  }

  if (session) {
    try {
      session.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
    } catch {
      // ignore
    }
  }
}

/** Clears both tokens, including the persisted refresh token. */
export function clearTokens(): void {
  accessToken = null;
  refreshToken = null;
  memoryRefreshToken = null;

  const local = getLocalStorage();
  if (local) {
    try {
      local.removeItem(REFRESH_TOKEN_KEY);
      local.removeItem(REMEMBER_ME_KEY);
    } catch {
      // ignore
    }
  }

  const session = getSessionStorage();
  if (session) {
    try {
      session.removeItem(REFRESH_TOKEN_KEY);
    } catch {
      // ignore
    }
  }
}
