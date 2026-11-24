import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';

type Language = 'fa' | 'en';
type Theme = 'dark' | 'light';

interface UiContextValue {
  language: Language;
  setLanguage: (lang: Language) => void;
  theme: Theme;
  toggleTheme: () => void;
  t: (key: string, params?: Record<string, string | number>) => string;
  formatTime: (seconds: number) => string;
  formatNumber: (n: number) => string;
  notify: (message: string, tone?: 'success' | 'error' | 'info') => void;
}

const translations: Record<Language, Record<string, string>> = {
  fa: {
    'app.name': 'irpotify پلیر',
    'nav.home': 'خانه',
    'nav.search': 'جستجو',
    'nav.library': 'کتابخانه',
    'nav.nowPlaying': 'در حال پخش',
    'nav.settings': 'تنظیمات',
    'home.recentlyAdded': 'تازه‌ترین آهنگ‌ها',
    'home.recentlyPlayed': 'به‌تازگی پخش‌شده',
    'home.mostPlayed': 'بیشترین پخش',
    'home.likedTracks': 'آهنگ‌های پسندیده‌شده',
    'home.featuredAlbums': 'آلبوم‌های منتخب',
    'tab.all': 'همه',
    'tab.tracks': 'آهنگ‌ها',
    'tab.albums': 'آلبوم‌ها',
    'tab.artists': 'هنرمندان',
    'tab.favorites': 'علاقه‌مندی‌ها',
    'tab.playlists': 'پلی‌لیست‌ها',
    'search.placeholder': 'جستجوی آهنگ، آلبوم، خواننده…',
    'search.empty': 'موردی برای «{query}» یافت نشد.',
    'library.title': 'کتابخانه شما',
    'library.likedSongs': 'آهنگ‌های پسندیده‌شده',
    'library.newPlaylist': 'لیست پخش جدید',
    'library.createPlaylist': 'ساخت پلی‌لیست',
    'library.playlistName': 'نام لیست پخش',
    'library.playlistDesc': 'توضیحات (اختیاری)',
    'library.playAll': 'پخش همه',
    'library.addSongs': 'افزودن آهنگ',
    'library.deletePlaylist': 'حذف پلی‌لیست',
    'player.queue': 'صف پخش',
    'player.noTrack': 'هیچ آهنگی انتخاب نشده',
    'player.noTrackHint': 'برای شروع، از بخش خانه یا جستجو یک آهنگ را لمس کنید.',
    'settings.title': 'تنظیمات',
    'settings.account': 'حساب کاربری',
    'settings.language': 'زبان',
    'settings.theme': 'تم ظاهر',
    'settings.installApp': 'نصب اپلیکیشن روی گوشی',
    'settings.changePassword': 'تغییر رمز عبور',
    'settings.signOut': 'خروج از حساب',
    'settings.signOutConfirm': 'آیا مطمئن هستید که می‌خواهید خارج شوید؟',
    'action.cancel': 'انصراف',
    'action.create': 'ایجاد',
    'action.delete': 'حذف',
    'action.save': 'ذخیره',
    'action.close': 'بستن',
    'auth.email': 'ایمیل',
    'auth.password': 'رمز عبور',
    'auth.signIn': 'ورود به حساب کاربری',
    'auth.signingIn': 'در حال ورود…',
    'auth.title': 'ورود به irpotify',
    'auth.subtitle': 'کتابخانه موسیقی خصوصی و اختصاصی شما',
    'auth.invalidCredentials': 'نام کاربری یا رمز عبور اشتباه است.',
    'auth.forgotPassword': 'فراموشی رمز عبور؟',
    'auth.forgotTitle': 'بازیابی رمز عبور',
    'auth.forgotSubtitle': 'ایمیل ثبت‌شده در حساب کاربری خود را وارد کنید تا پیام درخواست تغییر رمز برای مدیریت ارسال گردد:',
    'auth.forgotSuccess': 'درخواست تغییر رمز عبور شما برای مدیریت ارسال شد.',
    'auth.forgotSuccessDetail': 'مدیریت سیستم پس از بررسی درخواست، رمز عبور جدید را برای شما تنظیم خواهد کرد.',
    'auth.forgotError': 'خطا در ارسال درخواست. لطفاً دوباره تلاش کنید.',
    'auth.yourEmail': 'ایمیل شما',
    'auth.understood': 'متوجه شدم',
    'auth.sendRequest': 'ارسال درخواست',
    'auth.sending': 'در حال ارسال…',
    'auth.footerHint': '🎧 قابل استفاده برای تمام کاربران و مدیران جهت پخش موسیقی',
    'auth.language': 'زبان',
  },
  en: {
    'app.name': 'irpotify Player',
    'nav.home': 'Home',
    'nav.search': 'Search',
    'nav.library': 'Library',
    'nav.nowPlaying': 'Now Playing',
    'nav.settings': 'Settings',
    'home.recentlyAdded': 'Recently Added',
    'home.recentlyPlayed': 'Recently Played',
    'home.mostPlayed': 'Most Played',
    'home.likedTracks': 'Liked Tracks',
    'home.featuredAlbums': 'Featured Albums',
    'tab.all': 'All',
    'tab.tracks': 'Tracks',
    'tab.albums': 'Albums',
    'tab.artists': 'Artists',
    'tab.favorites': 'Favorites',
    'tab.playlists': 'Playlists',
    'search.placeholder': 'Search tracks, albums, artists…',
    'search.empty': 'No results for "{query}".',
    'library.title': 'Your Library',
    'library.likedSongs': 'Liked Songs',
    'library.newPlaylist': 'New Playlist',
    'library.createPlaylist': 'Create Playlist',
    'library.playlistName': 'Playlist Name',
    'library.playlistDesc': 'Description (optional)',
    'library.playAll': 'Play All',
    'library.addSongs': 'Add Songs',
    'library.deletePlaylist': 'Delete Playlist',
    'player.queue': 'Up Next / Queue',
    'player.noTrack': 'No track selected',
    'player.noTrackHint': 'Choose a song from Home or Search to start playing.',
    'settings.title': 'Settings',
    'settings.account': 'Account',
    'settings.language': 'Language',
    'settings.theme': 'Theme',
    'settings.installApp': 'Install App to Home Screen',
    'settings.changePassword': 'Change Password',
    'settings.signOut': 'Sign Out',
    'settings.signOutConfirm': 'Are you sure you want to sign out?',
    'action.cancel': 'Cancel',
    'action.create': 'Create',
    'action.delete': 'Delete',
    'action.save': 'Save',
    'action.close': 'Close',
    'auth.email': 'Email',
    'auth.password': 'Password',
    'auth.signIn': 'Sign In',
    'auth.signingIn': 'Signing in…',
    'auth.title': 'Welcome to irpotify',
    'auth.subtitle': 'Your private cloud music experience',
    'auth.invalidCredentials': 'Invalid email or password.',
    'auth.forgotPassword': 'Forgot password?',
    'auth.forgotTitle': 'Password Recovery',
    'auth.forgotSubtitle': 'Enter your registered email address to send a password reset request to the administrator:',
    'auth.forgotSuccess': 'Password reset request sent to administrator.',
    'auth.forgotSuccessDetail': 'The administrator will review your request and set up your new credentials.',
    'auth.forgotError': 'Failed to send request. Please try again.',
    'auth.yourEmail': 'Your email',
    'auth.understood': 'Got it',
    'auth.sendRequest': 'Send Request',
    'auth.sending': 'Sending…',
    'auth.footerHint': '🎧 Available for all users and administrators to stream music',
    'auth.language': 'Language',
  },
};

const UiContext = createContext<UiContextValue | null>(null);

export function UiProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(() => {
    return (localStorage.getItem('irpotify_lang') as Language) || 'fa';
  });

  const [theme, setThemeState] = useState<Theme>(() => {
    return (localStorage.getItem('irpotify_theme') as Theme) || 'dark';
  });

  const [toast, setToast] = useState<{ message: string; tone: string } | null>(null);

  const setLanguage = useCallback((lang: Language) => {
    setLanguageState(lang);
    localStorage.setItem('irpotify_lang', lang);
  }, []);

  const toggleTheme = useCallback(() => {
    setThemeState((prev) => {
      const nextTheme = prev === 'dark' ? 'light' : 'dark';
      localStorage.setItem('irpotify_theme', nextTheme);
      return nextTheme;
    });
  }, []);

  useEffect(() => {
    document.documentElement.lang = language;
    document.documentElement.dir = language === 'fa' ? 'rtl' : 'ltr';
    document.documentElement.setAttribute('data-theme', theme);
  }, [language, theme]);

  const notify = useCallback((message: string, tone: 'success' | 'error' | 'info' = 'info') => {
    setToast({ message, tone });
    setTimeout(() => setToast(null), 3500);
  }, []);

  const t = useCallback(
    (key: string, params?: Record<string, string | number>) => {
      const dict = translations[language] || translations.fa;
      let val = dict[key] || translations.en[key] || key;
      if (params) {
        for (const [pKey, pVal] of Object.entries(params)) {
          val = val.replace(`{${pKey}}`, String(pVal));
        }
      }
      return val;
    },
    [language],
  );

  const formatTime = useCallback((seconds: number) => {
    const s = Math.max(0, Math.floor(seconds));
    const m = Math.floor(s / 60);
    const remS = s % 60;
    return `${m}:${remS < 10 ? '0' : ''}${remS}`;
  }, []);

  const formatNumber = useCallback((n: number) => {
    return n.toLocaleString();
  }, []);

  const value = useMemo(
    () => ({
      language,
      setLanguage,
      theme,
      toggleTheme,
      t,
      formatTime,
      formatNumber,
      notify,
    }),
    [language, setLanguage, theme, toggleTheme, t, formatTime, formatNumber, notify],
  );

  return (
    <UiContext.Provider value={value}>
      {children}
      {toast ? (
        <div className={`toast-notification toast-${toast.tone}`}>
          <span>{toast.message}</span>
        </div>
      ) : null}
    </UiContext.Provider>
  );
}

export function useUi(): UiContextValue {
  const context = useContext(UiContext);
  if (!context) throw new Error('useUi must be used inside UiProvider');
  return context;
}
