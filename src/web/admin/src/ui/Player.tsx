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
import { ThreeDIcon } from './ThreeDIcons';

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
  const [isExpanded, setIsExpanded] = useState(false);

  useEffect(() => {
    if (!('mediaSession' in navigator)) return;
    if (current) {
      navigator.mediaSession.metadata = new MediaMetadata({
        title: current.title,
        artist: current.artist || 'irpotify',
        album: current.album || 'irpotify Cloud',
        artwork: [
          { src: '/icon-192.svg', sizes: '192x192', type: 'image/svg+xml' },
          { src: '/icon-512.svg', sizes: '512x512', type: 'image/svg+xml' },
        ],
      });
      navigator.mediaSession.setActionHandler('play', () => {
        void audioRef.current?.play();
      });
      navigator.mediaSession.setActionHandler('pause', () => {
        audioRef.current?.pause();
      });
      navigator.mediaSession.setActionHandler('seekto', (details) => {
        if (details.seekTime != null && audioRef.current) {
          audioRef.current.currentTime = details.seekTime;
          setPosition(details.seekTime);
        }
      });
    } else {
      navigator.mediaSession.metadata = null;
    }
  }, [current]);

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
        <>
          <div className="player" role="region" aria-label={t('player.title')}>
            <div
              className="player-track"
              onClick={() => setIsExpanded(true)}
              style={{ cursor: 'pointer' }}
              title={t('player.title')}
            >
              <div style={{ position: 'relative' }}>
                <Artwork url={current.artworkUrl} alt="" size={44} />
                {playing ? (
                  <span
                    style={{
                      position: 'absolute',
                      bottom: -2,
                      right: -2,
                      borderRadius: '50%',
                      background: '#1ed760',
                      padding: 2,
                      display: 'flex',
                    }}
                  >
                    <ThreeDIcon type="waves" size={14} animated />
                  </span>
                ) : null}
              </div>
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
              <button
                type="button"
                className="btn btn-ghost btn-icon"
                onClick={() => setIsExpanded(true)}
                title="Expand 3D Player"
                aria-label="Expand 3D Player"
              >
                <Icon name="arrow-up" size={18} />
              </button>
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

          {/* Full-Screen Immersive 3D Player Modal */}
          {isExpanded ? (
            <div className="modal-3d-player" role="dialog" aria-modal="true" aria-label={current.title}>
              {/* Modal Top Bar */}
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  paddingBottom: 24,
                  borderBottom: '1px solid rgba(255,255,255,0.08)',
                }}
              >
                <button
                  type="button"
                  className="btn btn-ghost btn-icon"
                  onClick={() => setIsExpanded(false)}
                  aria-label="Collapse"
                >
                  <Icon name="chevron-down" size={26} />
                </button>
                <div style={{ textAlign: 'center' }}>
                  <span
                    style={{
                      fontSize: 11,
                      letterSpacing: '0.12em',
                      textTransform: 'uppercase',
                      color: '#1ed760',
                      fontWeight: 700,
                    }}
                  >
                    PLAYING FROM IRPOTIFY CLOUD
                  </span>
                  <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 2 }}>
                    {current.album || 'Original Audio'}
                  </div>
                </div>
                <button
                  type="button"
                  className="btn btn-ghost btn-icon"
                  onClick={close}
                  aria-label={t('player.close')}
                >
                  <Icon name="close" size={24} />
                </button>
              </div>

              {/* Center 3D Showcase */}
              <div
                style={{
                  flex: 1,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: '32px 16px',
                  position: 'relative',
                }}
              >
                <div
                  style={{
                    position: 'relative',
                    filter: 'drop-shadow(0 20px 40px rgba(0,0,0,0.75))',
                    margin: '0 auto',
                  }}
                >
                  {current.artworkUrl ? (
                    <div style={{ position: 'relative' }}>
                      <div
                        style={{
                          borderRadius: 24,
                          overflow: 'hidden',
                          boxShadow: '0 16px 48px rgba(0,0,0,0.8)',
                          border: '2px solid rgba(255,255,255,0.1)',
                        }}
                      >
                        <Artwork url={current.artworkUrl} alt={current.title} size={240} />
                      </div>
                      {/* Spinning vinyl peeking out */}
                      <div
                        style={{
                          position: 'absolute',
                          top: -12,
                          right: -24,
                          zIndex: -1,
                          opacity: 0.85,
                        }}
                      >
                        <ThreeDIcon type="vinyl" size={160} animated={playing} />
                      </div>
                    </div>
                  ) : (
                    <ThreeDIcon type="vinyl" size={240} animated={playing} />
                  )}
                </div>

                {/* 3D Equalizer Waves Bar */}
                <div style={{ marginTop: 32 }}>
                  <ThreeDIcon type="waves" size={54} animated={playing} />
                </div>
              </div>

              {/* Bottom Playback & Track Details */}
              <div style={{ maxWidth: 540, width: '100%', margin: '0 auto', paddingBottom: 16 }}>
                <div style={{ marginBottom: 20 }}>
                  <h3
                    style={{
                      fontSize: 24,
                      fontWeight: 800,
                      margin: 0,
                      color: 'var(--text)',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {current.title}
                  </h3>
                  <p
                    style={{
                      fontSize: 16,
                      color: '#1ed760',
                      fontWeight: 600,
                      margin: '4px 0 0 0',
                    }}
                  >
                    {current.artist || 'Unknown Artist'}
                  </p>
                </div>

                {/* Seek Bar */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginBottom: 20 }}>
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
                    style={{ height: 6 }}
                    aria-label={t('player.seek')}
                  />
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      fontSize: 12,
                      color: 'var(--text-muted)',
                      fontFamily: 'var(--font-mono)',
                    }}
                  >
                    <span>{formatDurationSeconds(position)}</span>
                    <span>{formatDurationSeconds(duration)}</span>
                  </div>
                </div>

                {/* Controls (Big Round Play Button) */}
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: 32,
                    marginBottom: 20,
                  }}
                >
                  <button
                    type="button"
                    className="btn btn-primary btn-round"
                    onClick={toggle}
                    style={{
                      width: 68,
                      height: 68,
                      borderRadius: '50%',
                      background: '#1ed760',
                      color: '#000',
                      boxShadow: '0 8px 24px rgba(30, 215, 96, 0.4)',
                    }}
                    aria-label={playing ? t('tracks.pause') : t('tracks.play')}
                  >
                    {busy ? (
                      <span className="spinner" style={{ width: 24, height: 24 }} />
                    ) : (
                      <Icon name={playing ? 'pause' : 'play'} size={28} />
                    )}
                  </button>
                </div>

                {/* Volume Slider in Fullscreen */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 12, opacity: 0.85 }}>
                  <Icon name="volume" size={20} />
                  <input
                    type="range"
                    className="range player-volume"
                    min={0}
                    max={1}
                    step={0.05}
                    value={volume}
                    onChange={(event) => setVolumeState(Number(event.target.value))}
                    style={{ flex: 1 }}
                    aria-label={t('player.volume')}
                  />
                </div>
              </div>
            </div>
          ) : null}
        </>
      ) : null}
    </PlayerContext.Provider>
  );
}

export function usePlayer(): PlayerContextValue {
  const context = useContext(PlayerContext);
  if (!context) throw new Error('usePlayer must be used inside PlayerProvider');
  return context;
}
