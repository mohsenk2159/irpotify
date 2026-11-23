import type { SVGProps } from 'react';

export type IconName =
  | 'dashboard'
  | 'tracks'
  | 'albums'
  | 'artists'
  | 'folders'
  | 'genres'
  | 'playlists'
  | 'upload'
  | 'users'
  | 'settings'
  | 'telegram'
  | 'monitoring'
  | 'audit'
  | 'sessions'
  | 'signout'
  | 'menu'
  | 'close'
  | 'sun'
  | 'moon'
  | 'search'
  | 'plus'
  | 'edit'
  | 'trash'
  | 'restore'
  | 'star'
  | 'star-filled'
  | 'play'
  | 'pause'
  | 'volume'
  | 'chevron-left'
  | 'chevron-right'
  | 'chevron-down'
  | 'refresh'
  | 'music'
  | 'shield'
  | 'check'
  | 'warning'
  | 'arrow-up'
  | 'arrow-down'
  | 'qr'
  | 'whatsapp'
  | 'calendar'
  | 'clock'
  | 'external'
  | 'skip-back'
  | 'skip-forward'
  | 'shuffle'
  | 'repeat'
  | 'repeat-one'
  | 'heart'
  | 'heart-filled'
  | 'list-music';

const paths: Record<IconName, string> = {
  dashboard: 'M4 4h7v7H4zM13 4h7v4h-7zM13 10h7v10h-7zM4 13h7v7H4z',
  tracks: 'M9 18V6l10-2v12M9 18a3 3 0 1 1-3-3 3 3 0 0 1 3 3Zm10-2a3 3 0 1 1-3-3 3 3 0 0 1 3 3Z',
  albums: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm0-6a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z',
  artists: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 8a7 7 0 0 1 14 0',
  folders: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z',
  genres: 'M4 4h7l9 9-7 7-9-9Zm3 3h.01',
  playlists: 'M4 6h10M4 12h10M4 18h7M18 6v12M18 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z',
  upload: 'M12 16V4m0 0 5 5m-5-5L7 9M4 17v2a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-2',
  users: 'M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 9a7 7 0 0 1 14 0M17 11a3 3 0 1 0 0-6M21 20a6 6 0 0 0-4-5.7',
  settings:
    'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm7.4-3a7.4 7.4 0 0 0-.1-1.2l2-1.5-2-3.4-2.3 1a7.5 7.5 0 0 0-2-1.2L14.6 3h-4l-.4 2.5a7.5 7.5 0 0 0-2 1.2l-2.3-1-2 3.4 2 1.5a7.4 7.4 0 0 0 0 2.4l-2 1.5 2 3.4 2.3-1a7.5 7.5 0 0 0 2 1.2l.4 2.5h4l.4-2.5a7.5 7.5 0 0 0 2-1.2l2.3 1 2-3.4-2-1.5c.06-.4.1-.8.1-1.2Z',
  telegram: 'M21 4 3 11l5 2 2 6 3-4 5 3Z M8 13l10-7',
  monitoring: 'M3 12h4l3-7 4 14 3-7h4',
  audit: 'M8 4h8a2 2 0 0 1 2 2v14l-5-2-5 2V6a2 2 0 0 1 2-2ZM9 8h6M9 12h6',
  sessions: 'M4 5h16v10H4zM9 19h6M12 15v4',
  signout: 'M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 8l-4 4 4 4M6 12h10',
  menu: 'M4 7h16M4 12h16M4 17h16',
  close: 'M6 6l12 12M18 6 6 18',
  sun: 'M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10ZM12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.5 1.5M17.5 17.5 19 19M19 5l-1.5 1.5M6.5 17.5 5 19',
  moon: 'M20 14.5A8 8 0 0 1 9.5 4 8.5 8.5 0 1 0 20 14.5Z',
  search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm10 2-4.5-4.5',
  plus: 'M12 5v14M5 12h14',
  edit: 'M4 20h4L20 8l-4-4L4 16ZM14 6l4 4',
  trash: 'M4 7h16M9 7V5h6v2M6 7l1 13h10l1-13M10 11v5M14 11v5',
  restore: 'M4 10a8 8 0 1 1 2 10M4 4v6h6',
  star: 'm12 3 2.8 5.7 6.2.9-4.5 4.4 1 6.2-5.5-2.9-5.5 2.9 1-6.2L3 9.6l6.2-.9Z',
  'star-filled': 'm12 3 2.8 5.7 6.2.9-4.5 4.4 1 6.2-5.5-2.9-5.5 2.9 1-6.2L3 9.6l6.2-.9Z',
  play: 'M8 5v14l11-7Z',
  pause: 'M8 5v14M16 5v14',
  volume: 'M4 10v4h4l5 4V6L8 10ZM17 9a4 4 0 0 1 0 6M19.5 6.5a8 8 0 0 1 0 11',
  'chevron-left': 'm14 6-6 6 6 6',
  'chevron-right': 'm10 6 6 6-6 6',
  'chevron-down': 'm6 10 6 6 6-6',
  refresh: 'M20 11a8 8 0 1 0-2.3 6.3M20 4v7h-7',
  music: 'M9 18V6l10-2v12M9 18a3 3 0 1 1-3-3 3 3 0 0 1 3 3Zm10-2a3 3 0 1 1-3-3 3 3 0 0 1 3 3Z',
  shield: 'M12 3 5 6v6c0 4.5 3 7.5 7 9 4-1.5 7-4.5 7-9V6Zm-3 9 2 2 4-4',
  check: 'm5 13 4 4L19 7',
  warning: 'M12 4 3 20h18Zm0 6v5m0 3h.01',
  'arrow-up': 'M12 19V5m0 0-6 6m6-6 6 6',
  'arrow-down': 'M12 5v14m0 0 6-6m-6 6-6-6',
  qr: 'M3 3h8v8H3V3zm2 2v4h4V5H5zm8-2h8v8h-8V3zm2 2v4h4V5h-4zM3 13h8v8H3v-8zm2 2v4h4v-4H5zm13-2h3v2h-3v-2zm-5 0h2v5h-2v-5zm3 3h2v2h-2v-2zm2 2h3v3h-3v-3zm-2 1h-3v2h3v-2z',
  whatsapp: 'M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z',
  calendar: 'M19 4h-1V2h-2v2H8V2H6v2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2Zm0 16H5V9h14v11Z',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20Zm0-18a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm1 4h-2v5l4 2.5 1-1.6-3-1.9V8Z',
  external: 'M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5',
  'skip-back': 'M19 20L9 12l10-8v16zM5 19V5h2v14H5z',
  'skip-forward': 'M5 4l10 8-10 8V4zM19 5v14h-2V5h2z',
  shuffle: 'M16 3h5v5M4 20L21 3M21 16v5h-5M15 15l6 6M4 4l5 5',
  repeat: 'M17 1l4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3',
  'repeat-one': 'M17 1l4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3 M11 10h1v4m0 0h-1m1 0h1',
  heart: 'M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z',
  'heart-filled': 'M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z',
  'list-music': 'M21 15V6M18.5 18a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM3 6h11M3 12h11M3 18h8',
};

interface IconProps extends Omit<SVGProps<SVGSVGElement>, 'name'> {
  name: IconName;
  size?: number;
}

export function Icon({ name, size = 20, ...rest }: IconProps) {
  const filled = name === 'star-filled' || name === 'heart-filled';
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill={filled ? 'currentColor' : 'none'}
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...rest}
    >
      <path d={paths[name]} />
    </svg>
  );
}

export function BrandLogo({ size = 32, className }: { size?: number; className?: string }) {
  return (
    <svg
      viewBox="0 0 48 48"
      width={size}
      height={size}
      className={['brand-logo-svg', className ?? ''].filter(Boolean).join(' ')}
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
    >
      <defs>
        <linearGradient id="brand-grad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#10b981" />
          <stop offset="50%" stopColor="#06b6d4" />
          <stop offset="100%" stopColor="#6366f1" />
        </linearGradient>
        <linearGradient id="brand-disc" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#1f1f2e" />
          <stop offset="100%" stopColor="#0f0f17" />
        </linearGradient>
      </defs>
      <rect width="48" height="48" rx="14" fill="url(#brand-grad)" />
      <circle cx="24" cy="24" r="19" fill="url(#brand-disc)" />
      <circle cx="24" cy="24" r="15" stroke="rgba(255,255,255,0.08)" strokeWidth="1" />
      <circle cx="24" cy="24" r="11" stroke="rgba(255,255,255,0.12)" strokeWidth="1" />
      <rect x="14" y="21" width="3" height="6" rx="1.5" fill="#10b981" />
      <rect x="19" y="16" width="3" height="16" rx="1.5" fill="#06b6d4" />
      <rect x="24" y="12" width="3" height="24" rx="1.5" fill="#38bdf8" />
      <rect x="29" y="15" width="3" height="18" rx="1.5" fill="#818cf8" />
      <rect x="34" y="20" width="3" height="8" rx="1.5" fill="#a78bfa" />
    </svg>
  );
}
