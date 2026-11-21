import { useEffect, useState } from 'react';
import { http } from '../api/client';
import { Icon } from './icons';

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

export function Artwork({
  url,
  alt,
  size = 44,
  className,
}: {
  url: string | null | undefined;
  alt: string;
  size?: number;
  className?: string;
}) {
  const { blobUrl, loading } = useAuthedBlobUrl(url);
  return (
    <span
      className={['artwork', className ?? ''].filter(Boolean).join(' ')}
      style={{ width: size, height: size }}
      aria-hidden={!alt ? true : undefined}
    >
      {blobUrl ? (
        <img src={blobUrl} alt={alt} width={size} height={size} loading="lazy" />
      ) : loading ? (
        <span className="artwork-placeholder artwork-loading" aria-hidden="true">
          <span className="spinner spinner-sm" />
        </span>
      ) : (
        <span className="artwork-placeholder" aria-hidden="true">
          <Icon name="music" size={Math.max(16, Math.round(size * 0.42))} />
        </span>
      )}
    </span>
  );
}
