import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { api } from '../api/endpoints';
import { ApiError } from '../api/http';
import type { Track } from '../api/types';
import { Artwork } from './Artwork';
import { useUi, formatDurationSeconds } from './UiProvider';
import { Icon } from './icons';

type PlayerStatus = 'idle' | 'loading-token' | 'loading' | 'playing' | 'paused' | 'error';

interface PlayerContextValue {
  current: Track | null;
  status: PlayerStatus;
  playTrack: (track: Track) => void;
  toggle: () => void;
  close: () => void;
  retry: () => void;
}

const PlayerContext = createContext<PlayerContextValue | null>(null);

const TOKEN_MAX_AGE_MS = 8 * 60 * 1000;

export function PlayerProvider({ children }: { children: ReactNode }) {
  const { t } = useUi();
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const requestId = useRef(0);
  const tokenIssuedAt = useRef(0);
  const errorRetried = useRef(false);
  const currentRef = useRef<Track | null>(null);

  const [current, setCurrent] = useState<Track | null>(null);
  const [streamUrl, setStreamUrl] = useState<string | null>(null);
  const [status, setStatus] = useState<PlayerStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [volume, setVolumeState] = useState(0.9);

  useEffect(() => {
    currentRef.current = current;
  }, [current]);

  useEffect(() => {
    if (audioRef.current) audioRef.current.volume = volume;
  }, [volume]);

  useEffect(() => {
    return () => {
      audioRef.current?.pause();
    };
  }, []);

  const loadStream = useCallback(async (track: Track) => {
    const id = ++requestId.current;
    errorRetried.current = false;
    setCurrent(track);
    setStatus('loading-token');
    setError(null);
    setPosition(0);
    setDuration(0);
    setStreamUrl(null);
    try {
      const token = await api.tracks.streamToken(track.id);
      if (id !== requestId.current) return;
      tokenIssuedAt.current = Date.now();
      setStreamUrl(token.streamUrl);
      setStatus('loading');
    } catch (cause) {
      if (id !== requestId.current) return;
      const message = cause instanceof ApiError ? cause.message : null;
      setError(message);
      setStatus('error');
    }
  }, []);

  const playTrack = useCallback(
    (track: Track) => {
      if (currentRef.current?.id === track.id) {
        if (status === 'error') {
          void loadStream(track);
          return;
        }
        const audio = audioRef.current;
        if (!audio) return;
        if (audio.paused) {
          if (Date.now() - tokenIssuedAt.current > TOKEN_MAX_AGE_MS) {
            void loadStream(track);
            return;
          }
          void audio.play().catch(() => setStatus('paused'));
        } else {
          audio.pause();
        }
        return;
      }
      void loadStream(track);
    },
    [loadStream, status],
  );

  const toggle = useCallback(() => {
    if (currentRef.current) playTrack(currentRef.current);
  }, [playTrack]);

  const retry = useCallback(() => {
    if (currentRef.current) void loadStream(currentRef.current);
  }, [loadStream]);

  const close = useCallback(() => {
    requestId.current += 1;
    audioRef.current?.pause();
    setStreamUrl(null);
    setCurrent(null);
    setStatus('idle');
    setError(null);
    setPosition(0);
    setDuration(0);
  }, []);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !streamUrl) return;
    audio.load();
    const attempt = audio.play();
    if (attempt) {
      void attempt.catch(() => {
        if (requestId.current !== 0) setStatus((value) => (value === 'error' ? value : 'paused'));
      });
    }
  }, [streamUrl]);

  const handleError = useCallback(() => {
    const track = currentRef.current;
    if (!track) return;
    if (!errorRetried.current) {
      errorRetried.current = true;
      // Token may have expired or the network dropped before media metadata loaded.
      void loadStream(track);
      return;
    }
    setError(null);
    setStatus('error');
  }, [loadStream]);

  const value = useMemo<PlayerContextValue>(
    () => ({ current, status, playTrack, toggle, close, retry }),
    [current, status, playTrack, toggle, close, retry],
  );

  const playing = status === 'playing';
  const busy = status === 'loading-token' || status === 'loading';

  return (
    <PlayerContext.Provider value={value}>
      {children}
      <audio
        ref={audioRef}
        src={streamUrl ?? undefined}
        preload="metadata"
        onLoadedMetadata={(event) => {
          const value = event.currentTarget.duration;
          setDuration(Number.isFinite(value) ? value : 0);
        }}
        onTimeUpdate={(event) => setPosition(event.currentTarget.currentTime)}
        onPlay={() => setStatus('playing')}
        onPlaying={() => setStatus('playing')}
        onPause={() => setStatus((value) => (value === 'error' || value === 'loading-token' ? value : 'paused'))}
        onWaiting={() => setStatus((value) => (value === 'error' ? value : 'loading'))}
        onEnded={() => {
          setStatus('paused');
          setPosition(0);
        }}
        onError={handleError}
      />
      {current ? (
        <div className="player" role="region" aria-label={t('player.title')}>
          <div className="player-track">
            <Artwork url={current.artworkUrl} alt="" size={42} />
            <div className="player-meta">
              <span className="player-title" title={current.title}>
                {current.title}
              </span>
              <span className="player-artist" title={current.artist}>
                {current.artist || '—'}
              </span>
            </div>
          </div>
          <div className="player-controls">
            <button
              type="button"
              className="btn btn-primary btn-round"
              onClick={toggle}
              aria-label={playing ? t('tracks.pause') : t('tracks.play')}
              aria-busy={busy || undefined}
              disabled={status === 'loading-token'}
            >
              {busy ? <span className="spinner spinner-sm" aria-hidden="true" /> : <Icon name={playing ? 'pause' : 'play'} size={18} />}
            </button>
            <span className="player-time" aria-hidden="true">
              {formatDurationSeconds(position)}
            </span>
            <input
              type="range"
              className="range player-seek"
              min={0}
              max={Math.max(1, duration)}
              step={1}
              value={Math.min(position, Math.max(1, duration))}
              onChange={(event) => {
                const next = Number(event.target.value);
                setPosition(next);
                if (audioRef.current) audioRef.current.currentTime = next;
              }}
              aria-label={t('player.seek')}
              aria-valuetext={`${formatDurationSeconds(position)} / ${formatDurationSeconds(duration)}`}
              disabled={duration <= 0}
            />
            <span className="player-time" aria-hidden="true">
              {formatDurationSeconds(duration)}
            </span>
            {status === 'error' ? (
              <div className="player-error" role="alert">
                <span>{error ?? t('player.error')}</span>
                <button type="button" className="btn btn-secondary btn-sm" onClick={retry}>
                  {t('player.retry')}
                </button>
              </div>
            ) : status === 'loading-token' ? (
              <span className="player-hint">{t('player.loadingToken')}</span>
            ) : status === 'loading' ? (
              <span className="player-hint">{t('player.loading')}</span>
            ) : null}
          </div>
          <div className="player-extra">
            <Icon name="volume" size={18} />
            <input
              type="range"
              className="range player-volume"
              min={0}
              max={1}
              step={0.05}
              value={volume}
              onChange={(event) => setVolumeState(Number(event.target.value))}
              aria-label={t('player.volume')}
            />
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              onClick={close}
              aria-label={t('player.close')}
              title={t('player.close')}
            >
              <Icon name="close" size={18} />
            </button>
          </div>
        </div>
      ) : null}
    </PlayerContext.Provider>
  );
}

export function usePlayer(): PlayerContextValue {
  const context = useContext(PlayerContext);
  if (!context) throw new Error('usePlayer must be used inside PlayerProvider');
  return context;
}
