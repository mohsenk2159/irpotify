import { useEffect, useState } from 'react';
import { http } from '../api/client';

/**
 * Fetches a protected artwork endpoint with the bearer token and exposes an
 * object URL. `<img src>` cannot attach the Authorization header, so artwork
 * must be fetched through the API client.
 */
export function useAuthedBlobUrl(url: string | null | undefined): { blobUrl: string | null; loading: boolean; failed: boolean } {
  const [state, setState] = useState<{ blobUrl: string | null; loading: boolean; failed: boolean }>({
    blobUrl: null,
    loading: Boolean(url),
    failed: false,
  });

  useEffect(() => {
    if (!url) {
      setState({ blobUrl: null, loading: false, failed: false });
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    const controller = new AbortController();
    setState({ blobUrl: null, loading: true, failed: false });

    http
      .requestBlob(url, { signal: controller.signal })
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setState({ blobUrl: objectUrl, loading: false, failed: false });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ blobUrl: null, loading: false, failed: true });
      });

    return () => {
      cancelled = true;
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url]);

  return state;
}

const GRADIENT_PAIRS: [string, string][] = [
  ['#6366f1', '#8b5cf6'], // Indigo -> Purple
  ['#ec4899', '#f43f5e'], // Pink -> Rose
  ['#10b981', '#06b6d4'], // Emerald -> Cyan
  ['#f59e0b', '#ef4444'], // Amber -> Red
  ['#3b82f6', '#6366f1'], // Blue -> Indigo
  ['#14b8a6', '#0ea5e9'], // Teal -> Sky
  ['#8b5cf6', '#d946ef'], // Violet -> Fuchsia
  ['#059669', '#10b981'], // Forest -> Emerald
];

function getPalette(seed: string): { start: string; end: string; id: string } {
  let hash = 0;
  const str = seed || 'irpotify';
  for (let i = 0; i < str.length; i++) {
    hash = (hash << 5) - hash + str.charCodeAt(i);
    hash |= 0;
  }
  const idx = Math.abs(hash) % GRADIENT_PAIRS.length;
  const pair = GRADIENT_PAIRS[idx] ?? ['#10b981', '#06b6d4'];
  const [start, end] = pair;
  return { start, end, id: `grad-${Math.abs(hash)}` };
}

export function VinylCoverPlaceholder({ title, size = 44 }: { title: string; size?: number }) {
  const { start, end, id } = getPalette(title);
  const initial = (title || '♫').trim().charAt(0).toUpperCase();

  return (
    <svg
      viewBox="0 0 100 100"
      width={size}
      height={size}
      className="vinyl-cover-svg"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
    >
      <defs>
        <linearGradient id={id} x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor={start} />
          <stop offset="100%" stopColor={end} />
        </linearGradient>
        <linearGradient id={`vinyl-sheen-${id}`} x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="rgba(255,255,255,0.12)" />
          <stop offset="50%" stopColor="rgba(255,255,255,0.02)" />
          <stop offset="100%" stopColor="rgba(0,0,0,0.3)" />
        </linearGradient>
      </defs>

      {/* Outer Record Jacket / Disc Base */}
      <rect width="100" height="100" rx="16" fill="#141419" />
      <circle cx="50" cy="50" r="46" fill="#101014" stroke="rgba(255,255,255,0.06)" strokeWidth="1" />

      {/* Vinyl Grooves */}
      <circle cx="50" cy="50" r="41" fill="none" stroke="rgba(255,255,255,0.07)" strokeWidth="0.8" />
      <circle cx="50" cy="50" r="36" fill="none" stroke="rgba(255,255,255,0.06)" strokeWidth="0.8" />
      <circle cx="50" cy="50" r="31" fill="none" stroke="rgba(255,255,255,0.07)" strokeWidth="0.8" />
      <circle cx="50" cy="50" r="26" fill="none" stroke="rgba(255,255,255,0.06)" strokeWidth="0.8" />

      {/* Light Reflection Sheen */}
      <path
        d="M 14 50 A 36 36 0 0 1 86 50 Z"
        fill={`url(#vinyl-sheen-${id})`}
        opacity="0.6"
      />

      {/* Center Label */}
      <circle cx="50" cy="50" r="18" fill={`url(#${id})`} />
      <circle cx="50" cy="50" r="18" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />

      {/* Spindle Hole */}
      <circle cx="50" cy="50" r="4" fill="#141419" stroke="rgba(255,255,255,0.3)" strokeWidth="0.8" />

      {/* Soundwaves / Initial Indicator */}
      {size >= 64 ? (
        <text
          x="50"
          y="42"
          textAnchor="middle"
          fill="#ffffff"
          fontSize="9"
          fontWeight="bold"
          fontFamily="system-ui, sans-serif"
          opacity="0.95"
        >
          {initial}
        </text>
      ) : null}
    </svg>
  );
}

export function Artwork({
  url,
  src,
  alt = '',
  size = 44,
  className,
}: {
  url?: string | null;
  src?: string | null;
  alt?: string;
  size?: number;
  className?: string;
}) {
  const targetUrl = url ?? src ?? null;
  const { blobUrl, loading } = useAuthedBlobUrl(targetUrl);
  return (
    <span
      className={['artwork', className ?? ''].filter(Boolean).join(' ')}
      style={{ width: size, height: size }}
      aria-hidden={!alt ? true : undefined}
    >
      {blobUrl ? (
        <img src={blobUrl} alt={alt || 'cover'} width={size} height={size} loading="lazy" />
      ) : loading ? (
        <span className="artwork-placeholder artwork-loading" aria-hidden="true">
          <span className="spinner spinner-sm" />
        </span>
      ) : (
        <VinylCoverPlaceholder title={alt || 'irpotify'} size={size} />
      )}
    </span>
  );
}
