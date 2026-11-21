import { ApiError } from '../api/http';
import type { TranslationKey } from '../i18n/translations';

export type TranslateFn = (key: TranslationKey, vars?: Record<string, string | number>) => string;

/** Returns a user-facing error string without leaking request internals. */
export function errorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.isNetworkError ? fallback : error.message || fallback;
  }
  if (typeof error === 'string' && error.trim()) return error;
  return fallback;
}

export function roleLabel(t: TranslateFn, role: string): string {
  if (role === 'Administrator') return t('users.roleAdministrator');
  if (role === 'Contributor') return t('users.roleContributor');
  if (role === 'Listener') return t('users.roleListener');
  return role;
}

export function shortId(id: string, length = 8): string {
  return id.length > length ? id.slice(0, length) : id;
}

export function errorStatus(error: unknown): number | null {
  return error instanceof ApiError ? error.status : null;
}
