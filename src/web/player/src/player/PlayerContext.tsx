import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { api } from '../api/endpoints';
import type { Track } from '../api/types';

interface PlayerContextValue {
  currentTrack: Track | null;
  isPlaying: boolean;
  position: number;
  duration: number;
  queue: Track[];
  queueIndex: number;
  shuffle: boolean;
  repeatMode: 0 | 1 | 2; // 0: off, 1: all, 2: one
  volume: number;
  isBuffering: boolean;
  error: string | null;
  favoritesSet: Set<string>;
  playTrack: (track: Track, newQueue?: Track[]) => Promise<void>;
  playQueueItem: (index: number) => Promise<void>;
  togglePlayPause: () => void;
  next: () => void;
  previous: () => void;
  seekTo: (seconds: number) => void;
  setVolume: (vol: number) => void;
  toggleShuffle: () => void;
  cycleRepeat: () => void;
  toggleFavorite: (track: Track) => Promise<void>;
  isFavorite: (trackId: string) => boolean;
}

const PlayerContext = createContext<PlayerContextValue | null>(null);

export function PlayerProvider({ children }: { children: ReactNode }) {
  const [currentTrack, setCurrentTrack] = useState<Track | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [queue, setQueue] = useState<Track[]>([]);
  const [queueIndex, setQueueIndex] = useState(-1);
  const [shuffle, setShuffle] = useState(false);
  const [repeatMode, setRepeatMode] = useState<0 | 1 | 2>(0);
  const [volume, setVolumeState] = useState(1);
  const [isBuffering, setIsBuffering] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [favoritesSet, setFavoritesSet] = useState<Set<string>>(new Set());

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const originalQueueRef = useRef<Track[]>([]);
  const streamTokenRef = useRef<{ token: string; expiresAt: string } | null>(null);

  // Initialize audio element once
  useEffect(() => {
    const audio = new Audio();
    audio.preload = 'metadata';
    audioRef.current = audio;

    const onTimeUpdate = () => setPosition(audio.currentTime);
    const onDurationChange = () => setDuration(Number.isFinite(audio.duration) ? audio.duration : 0);
    const onPlay = () => setIsPlaying(true);
    const onPause = () => setIsPlaying(false);
    const onWaiting = () => setIsBuffering(true);
    const onPlaying = () => setIsBuffering(false);
    const onEnded = () => handleTrackEnded();
    const onError = () => {
      setIsBuffering(false);
      setIsPlaying(false);
      setError('خطا در پخش فایل صوتی');
    };

    audio.addEventListener('timeupdate', onTimeUpdate);
    audio.addEventListener('durationchange', onDurationChange);
    audio.addEventListener('play', onPlay);
    audio.addEventListener('pause', onPause);
    audio.addEventListener('waiting', onWaiting);
    audio.addEventListener('playing', onPlaying);
    audio.addEventListener('ended', onEnded);
    audio.addEventListener('error', onError);

    return () => {
      audio.pause();
      audio.removeEventListener('timeupdate', onTimeUpdate);
      audio.removeEventListener('durationchange', onDurationChange);
      audio.removeEventListener('play', onPlay);
      audio.removeEventListener('pause', onPause);
      audio.removeEventListener('waiting', onWaiting);
      audio.removeEventListener('playing', onPlaying);
      audio.removeEventListener('ended', onEnded);
      audio.removeEventListener('error', onError);
    };
  }, []);

  const loadAndPlay = useCallback(async (track: Track) => {
    const audio = audioRef.current;
    if (!audio) return;
    setError(null);
    setIsBuffering(true);
    try {
      const token = await api.tracks.streamToken(track.id);
      streamTokenRef.current = { token: token.token, expiresAt: token.expiresAt };
      audio.src = token.streamUrl;
      audio.currentTime = 0;
      setCurrentTrack(track);
      try {
        await audio.play();
        setIsPlaying(true);
      } catch {
        setIsPlaying(false);
      }

      // Update MediaSession for hardware / lock screen controls
      if ('mediaSession' in navigator) {
        navigator.mediaSession.metadata = new MediaMetadata({
          title: track.title,
          artist: track.artist || 'هنرمند ناشناس',
          album: track.album || 'تک‌آهنگ',
          artwork: track.artworkUrl
            ? [{ src: track.artworkUrl, sizes: '512x512', type: 'image/jpeg' }]
            : [{ src: '/icon-512.png', sizes: '512x512', type: 'image/png' }],
        });
      }
    } catch (err) {
      setError('خطا در برقراری ارتباط با سرور پخش');
      setIsPlaying(false);
    } finally {
      setIsBuffering(false);
    }
  }, []);

  const handleTrackEnded = useCallback(() => {
    if (repeatMode === 2) {
      // Repeat One
      const audio = audioRef.current;
      if (audio) {
        audio.currentTime = 0;
        void audio.play();
      }
      return;
    }
    // Repeat All or Next
    setQueueIndex((prevIndex) => {
      const nextIndex = prevIndex + 1;
      if (nextIndex < queue.length) {
        const nextTrack = queue[nextIndex];
        if (nextTrack) void loadAndPlay(nextTrack);
        return nextIndex;
      }
      if (repeatMode === 1 && queue.length > 0) {
        // Loop back to start
        const firstTrack = queue[0];
        if (firstTrack) void loadAndPlay(firstTrack);
        return 0;
      }
      setIsPlaying(false);
      return prevIndex;
    });
  }, [queue, repeatMode, loadAndPlay]);

  const playTrack = useCallback(
    async (track: Track, newQueue?: Track[]) => {
      const currentQueue = newQueue ?? (queue.length > 0 ? queue : [track]);
      originalQueueRef.current = currentQueue;
      const effectiveQueue = shuffle
        ? [track, ...currentQueue.filter((t) => t.id !== track.id).sort(() => Math.random() - 0.5)]
        : currentQueue;
      setQueue(effectiveQueue);
      const idx = effectiveQueue.findIndex((t) => t.id === track.id);
      setQueueIndex(idx >= 0 ? idx : 0);
      await loadAndPlay(track);
    },
    [queue, shuffle, loadAndPlay],
  );

  const playQueueItem = useCallback(
    async (index: number) => {
      if (index >= 0 && index < queue.length) {
        setQueueIndex(index);
        const track = queue[index];
        if (track) await loadAndPlay(track);
      }
    },
    [queue, loadAndPlay],
  );

  const togglePlayPause = useCallback(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;
    if (isPlaying) {
      audio.pause();
    } else {
      void audio.play();
    }
  }, [isPlaying, currentTrack]);

  const next = useCallback(() => {
    if (queue.length === 0) return;
    const nextIdx = queueIndex + 1;
    if (nextIdx < queue.length) {
      setQueueIndex(nextIdx);
      const nextTrack = queue[nextIdx];
      if (nextTrack) void loadAndPlay(nextTrack);
    } else if (repeatMode === 1) {
      setQueueIndex(0);
      const firstTrack = queue[0];
      if (firstTrack) void loadAndPlay(firstTrack);
    }
  }, [queue, queueIndex, repeatMode, loadAndPlay]);

  const previous = useCallback(() => {
    const audio = audioRef.current;
    if (audio && audio.currentTime > 3) {
      audio.currentTime = 0;
      return;
    }
    if (queue.length === 0) return;
    const prevIdx = queueIndex - 1;
    if (prevIdx >= 0) {
      setQueueIndex(prevIdx);
      const prevTrack = queue[prevIdx];
      if (prevTrack) void loadAndPlay(prevTrack);
    } else if (audio) {
      audio.currentTime = 0;
    }
  }, [queue, queueIndex, loadAndPlay]);

  const seekTo = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (!audio) return;
    audio.currentTime = seconds;
    setPosition(seconds);
  }, []);

  const setVolume = useCallback((vol: number) => {
    const audio = audioRef.current;
    if (audio) audio.volume = Math.max(0, Math.min(1, vol));
    setVolumeState(vol);
  }, []);

  const toggleShuffle = useCallback(() => {
    setShuffle((prev) => {
      const nextShuffle = !prev;
      if (nextShuffle) {
        if (currentTrack) {
          const others = originalQueueRef.current.filter((t) => t.id !== currentTrack.id);
          const shuffled = [currentTrack, ...others.sort(() => Math.random() - 0.5)];
          setQueue(shuffled);
          setQueueIndex(0);
        }
      } else {
        setQueue(originalQueueRef.current);
        if (currentTrack) {
          const origIdx = originalQueueRef.current.findIndex((t) => t.id === currentTrack.id);
          setQueueIndex(origIdx >= 0 ? origIdx : 0);
        }
      }
      return nextShuffle;
    });
  }, [currentTrack]);

  const cycleRepeat = useCallback(() => {
    setRepeatMode((prev) => ((prev + 1) % 3) as 0 | 1 | 2);
  }, []);

  const toggleFavorite = useCallback(
    async (track: Track) => {
      const isFav = favoritesSet.has(track.id);
      try {
        await api.tracks.favorite(track.id, !isFav);
        setFavoritesSet((prev) => {
          const nextSet = new Set(prev);
          if (isFav) nextSet.delete(track.id);
          else nextSet.add(track.id);
          return nextSet;
        });
      } catch (err) {
        console.error('Failed to toggle favorite', err);
      }
    },
    [favoritesSet],
  );

  const isFavorite = useCallback(
    (trackId: string) => favoritesSet.has(trackId),
    [favoritesSet],
  );

  // Hook MediaSession Action Handlers
  useEffect(() => {
    if (!('mediaSession' in navigator)) return;
    navigator.mediaSession.setActionHandler('play', togglePlayPause);
    navigator.mediaSession.setActionHandler('pause', togglePlayPause);
    navigator.mediaSession.setActionHandler('previoustrack', previous);
    navigator.mediaSession.setActionHandler('nexttrack', next);
    navigator.mediaSession.setActionHandler('seekto', (details) => {
      if (details.seekTime != null) seekTo(details.seekTime);
    });
    return () => {
      navigator.mediaSession.setActionHandler('play', null);
      navigator.mediaSession.setActionHandler('pause', null);
      navigator.mediaSession.setActionHandler('previoustrack', null);
      navigator.mediaSession.setActionHandler('nexttrack', null);
      navigator.mediaSession.setActionHandler('seekto', null);
    };
  }, [togglePlayPause, previous, next, seekTo]);

  return (
    <PlayerContext.Provider
      value={{
        currentTrack,
        isPlaying,
        position,
        duration,
        queue,
        queueIndex,
        shuffle,
        repeatMode,
        volume,
        isBuffering,
        error,
        favoritesSet,
        playTrack,
        playQueueItem,
        togglePlayPause,
        next,
        previous,
        seekTo,
        setVolume,
        toggleShuffle,
        cycleRepeat,
        toggleFavorite,
        isFavorite,
      }}
    >
      {children}
    </PlayerContext.Provider>
  );
}

export function usePlayer(): PlayerContextValue {
  const context = useContext(PlayerContext);
  if (!context) throw new Error('usePlayer must be used within PlayerProvider');
  return context;
}
