import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { dictionaries, en, type TranslationDict, type TranslationKey } from '../i18n/translations';

export type Language = 'en' | 'fa';
export type Theme = 'dark' | 'light';

const THEME_KEY = 'musichub.dashboard.theme';
const LANGUAGE_KEY = 'musichub.dashboard.language';

function readStored(key: string): string | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Preferences are non-critical; ignore storage failures.
  }
}

function initialTheme(): Theme {
  const stored = readStored(THEME_KEY);
  if (stored === 'light' || stored === 'dark') return stored;
  return 'dark';
}

function initialLanguage(): Language {
  const stored = readStored(LANGUAGE_KEY);
  if (stored === 'en' || stored === 'fa') return stored;
  try {
    return navigator.language.toLowerCase().startsWith('fa') ? 'fa' : 'en';
  } catch {
    return 'en';
  }
}

export type Translate = (key: TranslationKey, vars?: Record<string, string | number>) => string;

interface UiContextValue {
  theme: Theme;
  language: Language;
  direction: 'ltr' | 'rtl';
  t: Translate;
  setTheme: (theme: Theme) => void;
  toggleTheme: () => void;
  setLanguage: (language: Language) => void;
  formatNumber: (value: number, options?: Intl.NumberFormatOptions) => string;
  formatBytes: (bytes: number) => string;
  formatDuration: (seconds: number) => string;
  formatDate: (iso: string | null | undefined, options?: Intl.DateTimeFormatOptions) => string;
  locale: string;
}

const UiContext = createContext<UiContextValue | null>(null);

export function translate(dict: TranslationDict, key: TranslationKey, vars?: Record<string, string | number>): string {
  const template = dict[key] ?? en[key];
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (match: string, name: string) =>
    Object.prototype.hasOwnProperty.call(vars, name) ? String(vars[name]) : match,
  );
}

export function formatDurationSeconds(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '0:00';
  const total = Math.round(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = total % 60;
  const pad = (value: number) => String(value).padStart(2, '0');
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(rest)}` : `${minutes}:${pad(rest)}`;
}

export function formatByteSize(bytes: number, locale: string, formatNumber: (value: number) => string): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < 1024) return `${formatNumber(bytes)} B`;
  const units = ['KB', 'MB', 'GB', 'TB', 'PB'];
  let value = bytes / 1024;
  let unit = units[0] ?? 'KB';
  for (const next of units.slice(1)) {
    if (value < 1024) break;
    value /= 1024;
    unit = next;
  }
  const formatted = new Intl.NumberFormat(locale, {
    maximumFractionDigits: value >= 100 ? 0 : value >= 10 ? 1 : 2,
  }).format(value);
  return `${formatted} ${unit}`;
}

export function UiProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(initialTheme);
  const [language, setLanguageState] = useState<Language>(initialLanguage);

  useEffect(() => {
    document.documentElement.dataset['theme'] = theme;
    document.documentElement.style.colorScheme = theme;
    writeStored(THEME_KEY, theme);
  }, [theme]);

  useEffect(() => {
    const direction = 'ltr';
    document.documentElement.lang = language;
    document.documentElement.dir = direction;
    writeStored(LANGUAGE_KEY, language);
  }, [language]);

  const setTheme = useCallback((next: Theme) => setThemeState(next), []);
  const toggleTheme = useCallback(() => setThemeState((current) => (current === 'dark' ? 'light' : 'dark')), []);
  const setLanguage = useCallback((next: Language) => setLanguageState(next), []);

  const locale = language === 'fa' ? 'fa-IR' : 'en-US';

  const formatNumber = useCallback(
    (value: number, options?: Intl.NumberFormatOptions) => new Intl.NumberFormat(locale, options).format(value),
    [locale],
  );

  const formatBytes = useCallback(
    (bytes: number) => formatByteSize(bytes, locale, (value) => new Intl.NumberFormat(locale).format(value)),
    [locale],
  );

  const formatDuration = useCallback((seconds: number) => formatDurationSeconds(seconds), []);

  const formatDate = useCallback(
    (iso: string | null | undefined, options?: Intl.DateTimeFormatOptions) => {
      if (!iso) return '—';
      const date = new Date(iso);
      if (Number.isNaN(date.getTime())) return '—';
      return new Intl.DateTimeFormat(locale, options ?? { dateStyle: 'medium', timeStyle: 'short' }).format(date);
    },
    [locale],
  );

  const value = useMemo<UiContextValue>(() => {
    const dict = dictionaries[language];
    return {
      theme,
      language,
      direction: language === 'fa' ? 'rtl' : 'ltr',
      t: (key, vars) => translate(dict, key, vars),
      setTheme,
      toggleTheme,
      setLanguage,
      formatNumber,
      formatBytes,
      formatDuration,
      formatDate,
      locale,
    };
  }, [theme, language, setTheme, toggleTheme, setLanguage, formatNumber, formatBytes, formatDuration, formatDate, locale]);

  return <UiContext.Provider value={value}>{children}</UiContext.Provider>;
}

export function useUi(): UiContextValue {
  const context = useContext(UiContext);
  if (!context) throw new Error('useUi must be used inside UiProvider');
  return context;
}
