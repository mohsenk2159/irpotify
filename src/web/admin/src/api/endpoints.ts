import { http } from './client';
import { ApiError, messageFromPayload, type ProblemDetails } from './http';
import { getAccessToken } from './session';
import type {
  AdminMetrics,
  AlbumDetail,
  AlbumQuery,
  AlbumSummary,
  AlbumWrite,
  ArtistDetail,
  ArtistQuery,
  ArtistSummary,
  AuditEntry,
  AuthSession,
  BackupRecord,
  DashboardStats,
  FolderNode,
  GenreSummary,
  HealthResponse,
  LoginHistoryEntry,
  OnlineUsersResponse,
  Paged,
  PlaylistDetail,
  PlaylistSummary,
  PlaylistWrite,
  RoleName,
  SessionInfo,
  SetupResult,
  SetupStatus,
  StreamToken,
  SystemEvent,
  SystemSettings,
  TelegramConfigWrite,
  TelegramImport,
  TelegramPublication,
  TelegramSummary,
  TelegramTestIngestResult,
  TelegramTestResult,
  TelegramUpdate,
  Track,
  TrackDetail,
  TrackQuery,
  TrackUpdate,
  UploadFileResult,
  UploadHistoryEntry,
  UploadResponse,
  User,
  UserCreate,
  UserSummary,
  UserUpdate,
  VersionInfo,
  NotificationsResponse,
} from './types';

/* ------------------------------------------------------------------ */
/* Setup & auth                                                        */
/* ------------------------------------------------------------------ */

export const api = {
  setup: {
    status: () => http.request<SetupStatus>('/api/setup/status', { auth: false }),
    initialize: (payload: { bootstrapToken: string; email: string; password: string; displayName: string }) =>
      http.request<SetupResult>('/api/setup/initialize', { method: 'POST', json: payload, auth: false }),
    resetPassword: (payload: { bootstrapToken: string; email?: string; newPassword: string }) =>
      http.request<{ success: boolean; email: string; message: string }>('/api/setup/reset-password', {
        method: 'POST',
        json: payload,
        auth: false,
      }),
  },
  auth: {
    login: (payload: { email: string; password: string; deviceName: string; clientType?: string }) =>
      http.request<AuthSession>('/api/auth/login', { method: 'POST', json: payload, auth: false }),
    refresh: (refreshToken: string) =>
      http.request<AuthSession>('/api/auth/refresh', { method: 'POST', json: { refreshToken }, auth: false }),
    logout: (refreshToken: string | null) =>
      http.request<void>('/api/auth/logout', { method: 'POST', json: { refreshToken }, auth: false }),
    logoutAll: () => http.request<void>('/api/auth/logout-all', { method: 'POST' }),
    me: () => http.request<User>('/api/auth/me'),
    sessions: () => http.request<SessionInfo[]>('/api/auth/sessions'),
    revokeSession: (id: string) => http.request<void>(`/api/auth/sessions/${id}`, { method: 'DELETE' }),
  },
  users: {
    list: () => http.request<UserSummary[]>('/api/users'),
    create: (payload: UserCreate) => http.request<{ id: string }>('/api/users', { method: 'POST', json: payload }),
    update: (id: string, payload: UserUpdate) => http.request<void>(`/api/users/${id}`, { method: 'PATCH', json: payload }),
    delete: (id: string) => http.request<void>(`/api/users/${id}`, { method: 'DELETE' }),
    resetPassword: (id: string, newPassword: string) =>
      http.request<void>(`/api/users/${id}/reset-password`, { method: 'POST', json: { newPassword } }),
    loginHistory: (id: string) => http.request<LoginHistoryEntry[]>(`/api/users/${id}/login-history`),
    pairingQr: (id: string, payload?: { deviceName?: string; expiryDays?: number }) =>
      http.request<{
        userId: string;
        email: string;
        displayName: string;
        deviceName: string;
        code: string;
        expiresAt: string;
        publicServerUrl: string | null;
        lanServerUrl: string | null;
        effectiveServerUrl: string;
        qrPayload: string;
        qrCardUrl?: string;
        qrImageUrl?: string;
      }>(`/api/users/${id}/pairing-qr`, { method: 'POST', json: payload ?? {} }),
  },
  tracks: {
    list: (query: TrackQuery) => http.request<Paged<Track>>('/api/tracks', { query: { ...query } }),
    get: (id: string) => http.request<TrackDetail>(`/api/tracks/${id}`),
    update: (id: string, payload: TrackUpdate) => http.request<Track>(`/api/tracks/${id}`, { method: 'PATCH', json: payload }),
    remove: (id: string) => http.request<void>(`/api/tracks/${id}`, { method: 'DELETE' }),
    restore: (id: string) => http.request<Track>(`/api/tracks/${id}/restore`, { method: 'POST' }),
    favorite: (id: string, favorite: boolean) =>
      favorite
        ? http.request<void>(`/api/favorites/${id}`, { method: 'POST' })
        : http.request<void>(`/api/favorites/${id}`, { method: 'DELETE' }),
    streamToken: (id: string) => http.request<StreamToken>(`/api/tracks/${id}/stream-token`, { method: 'POST' }),
  },
  albums: {
    list: (query: AlbumQuery) =>
      http.request<Paged<AlbumSummary>>('/api/albums', { query: { ...query } }),
    get: (id: string) => http.request<AlbumDetail>(`/api/albums/${id}`),
    create: (payload: AlbumWrite) => http.request<{ id: string; title: string }>('/api/albums', { method: 'POST', json: payload }),
    update: (id: string, payload: AlbumWrite) => http.request<void>(`/api/albums/${id}`, { method: 'PATCH', json: payload }),
    remove: (id: string) => http.request<void>(`/api/albums/${id}`, { method: 'DELETE' }),
    addTrack: (id: string, trackId: string, position?: number) =>
      http.request<void>(`/api/albums/${id}/tracks`, { method: 'POST', json: { trackId, position } }),
    removeTrack: (id: string, trackId: string) =>
      http.request<void>(`/api/albums/${id}/tracks/${trackId}`, { method: 'DELETE' }),
  },
  artists: {
    list: (query: ArtistQuery) =>
      http.request<Paged<ArtistSummary>>('/api/artists', { query: { ...query } }),
    get: (id: string) => http.request<ArtistDetail>(`/api/artists/${id}`),
    create: (payload: { name: string; biography?: string | null }) =>
      http.request<{ id: string; name: string }>('/api/artists', { method: 'POST', json: payload }),
    update: (id: string, payload: { name?: string; biography?: string | null }) =>
      http.request<void>(`/api/artists/${id}`, { method: 'PATCH', json: payload }),
    remove: (id: string) => http.request<void>(`/api/artists/${id}`, { method: 'DELETE' }),
  },
  folders: {
    list: () => http.request<FolderNode[]>('/api/folders'),
    create: (payload: { name: string; parentId?: string | null; description?: string | null }) =>
      http.request<{ id: string; name: string; parentId: string | null }>('/api/folders', { method: 'POST', json: payload }),
    update: (id: string, payload: { name?: string; parentId?: string | null; description?: string | null }) =>
      http.request<void>(`/api/folders/${id}`, { method: 'PATCH', json: payload }),
    remove: (id: string) => http.request<void>(`/api/folders/${id}`, { method: 'DELETE' }),
    addTracks: (id: string, trackIds: string[]) =>
      http.request<void>(`/api/folders/${id}/tracks`, { method: 'POST', json: { trackIds } }),
    removeTrack: (id: string, trackId: string) =>
      http.request<void>(`/api/folders/${id}/tracks/${trackId}`, { method: 'DELETE' }),
  },
  genres: {
    list: () => http.request<GenreSummary[]>('/api/genres'),
    create: (name: string) => http.request<{ id: string; name: string }>('/api/genres', { method: 'POST', json: { name } }),
  },
  playlists: {
    list: () => http.request<PlaylistSummary[]>('/api/playlists'),
    get: (id: string) => http.request<PlaylistDetail>(`/api/playlists/${id}`),
    create: (payload: PlaylistWrite) =>
      http.request<{ id: string; name: string }>('/api/playlists', { method: 'POST', json: payload }),
    update: (id: string, payload: PlaylistWrite) =>
      http.request<void>(`/api/playlists/${id}`, { method: 'PATCH', json: payload }),
    remove: (id: string) => http.request<void>(`/api/playlists/${id}`, { method: 'DELETE' }),
    addTrack: (id: string, trackId: string) =>
      http.request<void>(`/api/playlists/${id}/tracks`, { method: 'POST', json: { trackId } }),
    removeTrack: (id: string, trackId: string) =>
      http.request<void>(`/api/playlists/${id}/tracks/${trackId}`, { method: 'DELETE' }),
    reorder: (id: string, trackIds: string[]) =>
      http.request<void>(`/api/playlists/${id}/reorder`, { method: 'PUT', json: { trackIds } }),
  },
  dashboard: {
    stats: () => http.request<DashboardStats>('/api/dashboard/stats'),
    metrics: () => http.request<AdminMetrics>('/api/admin/metrics'),
    onlineUsers: () => http.request<OnlineUsersResponse>('/api/admin/online-users'),
    audit: (query: { page?: number; pageSize?: number; action?: string; actorUserId?: string }) =>
      http.request<Paged<AuditEntry>>('/api/admin/audit', { query }),
    systemEvents: (limit: number) => http.request<SystemEvent[]>('/api/admin/system-events', { query: { limit } }),
    settings: () => http.request<SystemSettings>('/api/admin/settings'),
    updateSettings: (values: Record<string, string>) =>
      http.request<void>('/api/admin/settings', { method: 'PUT', json: values }),
    backups: () => http.request<BackupRecord[]>('/api/admin/backups'),
  },
  notifications: {
    list: (query?: { status?: string; sort?: string; page?: number; pageSize?: number }) =>
      http.request<NotificationsResponse>('/api/notifications', { query: query ? { ...query } : undefined }),
    unreadCount: () => http.request<{ unreadCount: number }>('/api/notifications/unread-count'),
    markRead: (id: string) =>
      http.request<{ id: string; isRead: boolean; readAt: string }>(`/api/notifications/${id}/read`, { method: 'PATCH' }),
    markAllRead: () => http.request<void>('/api/notifications/mark-all-read', { method: 'POST' }),
    delete: (id: string) => http.request<void>(`/api/notifications/${id}`, { method: 'DELETE' }),
    batchDelete: (ids: string[]) =>
      http.request<void>('/api/notifications/batch-delete', { method: 'POST', json: { ids } }),
  },
  telegram: {
    get: () => http.request<TelegramSummary>('/api/admin/telegram'),
    update: (payload: TelegramConfigWrite) =>
      http.request<TelegramSummary>('/api/admin/telegram', { method: 'PUT', json: payload }),
    test: () => http.request<TelegramTestResult>('/api/admin/telegram/test', { method: 'POST' }),
    testIngest: async (file: File) => {
      const formData = new FormData();
      formData.append('file', file);
      return http.request<TelegramTestIngestResult>('/api/admin/telegram/test-ingest', {
        method: 'POST',
        formData,
      });
    },
    imports: (query: { page?: number; pageSize?: number }) =>
      http.request<Paged<TelegramImport>>('/api/admin/telegram/imports', { query }),
    publications: (query: { status?: string; page?: number; pageSize?: number }) =>
      http.request<Paged<TelegramPublication>>('/api/admin/telegram/publications', { query }),
    retryPublication: (id: string) =>
      http.request<void>(`/api/admin/telegram/publications/${id}/retry`, { method: 'POST' }),
    updates: (take: number) => http.request<TelegramUpdate[]>('/api/admin/telegram/updates', { query: { take } }),
    setEnabled: (enabled: boolean) =>
      http.request<{ enabled: boolean }>('/api/admin/telegram/enable', { method: 'POST', query: { enabled } }),
  },
  uploads: {
    history: (query: { page?: number; pageSize?: number }) =>
      http.request<Paged<UploadHistoryEntry>>('/api/uploads/history', { query }),
  },
  system: {
    health: () => http.request<HealthResponse>('/api/health', { auth: false }),
    version: () => http.request<VersionInfo>('/api/version', { auth: false }),
    updateStatus: () =>
      http.request<{
        currentVersion: string;
        currentCommitSha: string | null;
        latestCommitSha: string | null;
        latestCommitMessage: string | null;
        latestCommitDate: string | null;
        lastCheckedAt: string | null;
        updateAvailable: boolean;
        repoUrl: string;
        lastError: string | null;
      }>('/api/admin/system/update-status'),
    checkUpdate: () =>
      http.request<{
        currentVersion: string;
        currentCommitSha: string | null;
        latestCommitSha: string | null;
        latestCommitMessage: string | null;
        latestCommitDate: string | null;
        lastCheckedAt: string | null;
        updateAvailable: boolean;
        repoUrl: string;
        lastError: string | null;
      }>('/api/admin/system/check-update', { method: 'POST' }),
    applyUpdate: () =>
      http.request<{
        applied: boolean;
        requiresHostAction: boolean;
        message: string;
        commandHint: string | null;
      }>('/api/admin/system/apply-update', { method: 'POST' }),
  },
};

export async function downloadBackupArchive(): Promise<string> {
  const token = getAccessToken();
  const res = await fetch('/api/admin/backups/export', {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!res.ok) {
    throw new Error('خطا در ایجاد فایل بکاپ کامل');
  }
  const blob = await res.blob();
  const disposition = res.headers.get('content-disposition') ?? '';
  const match = /filename="?([^";]+)"?/i.exec(disposition);
  const fileName = match?.[1] ?? `irpotify-backup-${new Date().toISOString().slice(0, 10)}.zip`;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
  return fileName;
}

export function restoreBackupArchive(
  file: File,
  onProgress?: (percent: number) => void,
): Promise<{
  success: boolean;
  usersRestored: number;
  artistsRestored: number;
  albumsRestored: number;
  tracksRestored: number;
  playlistsRestored: number;
  filesRestored: number;
  message: string;
}> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/admin/backups/restore');
    const token = getAccessToken();
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.setRequestHeader('Accept', 'application/json');

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress?.(Math.min(100, Math.round((event.loaded / event.total) * 100)));
      }
    };
    xhr.onerror = () => reject(new Error('خطا در ارسال فایل بکاپ به سرور'));
    xhr.onload = () => {
      try {
        const data = JSON.parse(xhr.responseText);
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(data);
        } else {
          reject(new Error(data?.error || data?.detail || 'خطا در بازیابی بکاپ'));
        }
      } catch {
        reject(new Error('پاسخ نامعتبر از سرور هنگام بازیابی'));
      }
    };

    const formData = new FormData();
    formData.append('file', file, file.name);
    xhr.send(formData);
  });
}

export type { UploadFileResult };

/* ------------------------------------------------------------------ */
/* Upload with progress (XHR; fetch cannot report upload progress)      */
/* ------------------------------------------------------------------ */

interface UploadOptions {
  folderId?: string | null;
  albumId?: string | null;
  onProgress?: (percent: number) => void;
  signal?: AbortSignal;
}

function parseXhrError(xhr: XMLHttpRequest): ApiError {
  const contentType = xhr.getResponseHeader('content-type') ?? '';
  let payload: unknown = null;
  if (contentType.includes('json')) {
    try {
      payload = JSON.parse(xhr.responseText) as unknown;
    } catch {
      payload = null;
    }
  } else if (xhr.responseText) {
    payload = xhr.responseText;
  }
  const problem =
    typeof payload === 'object' && payload !== null ? (payload as ProblemDetails) : null;
  return new ApiError(xhr.status, messageFromPayload(payload, xhr.status), problem);
}

function sendUpload(formData: FormData, options: UploadOptions): Promise<UploadResponse> {
  return new Promise<UploadResponse>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/uploads');
    const token = getAccessToken();
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.setRequestHeader('Accept', 'application/json');

    const abortHandler = () => xhr.abort();
    options.signal?.addEventListener('abort', abortHandler);

    const cleanup = () => options.signal?.removeEventListener('abort', abortHandler);

    xhr.upload.onprogress = (event) => {
      if (!event.lengthComputable) return;
      options.onProgress?.(Math.min(100, Math.round((event.loaded / event.total) * 100)));
    };

    xhr.onerror = () => {
      cleanup();
      reject(new ApiError(0, 'The upload could not reach the server.', null, { isNetworkError: true }));
    };
    xhr.onabort = () => {
      cleanup();
      reject(new DOMException('The upload was aborted.', 'AbortError'));
    };
    xhr.onload = () => {
      cleanup();
      let payload: unknown = null;
      try {
        payload = JSON.parse(xhr.responseText) as unknown;
      } catch {
        payload = null;
      }
      const results = (payload as { results?: UploadFileResult[] } | null)?.results;
      if (Array.isArray(results)) {
        resolve({ results });
        return;
      }
      reject(parseXhrError(xhr));
    };

    xhr.send(formData);
  });
}

export async function uploadTracks(files: File[], options: UploadOptions = {}): Promise<UploadResponse> {
  const formData = new FormData();
  for (const file of files) formData.append('files', file, file.name);
  if (options.folderId) formData.append('folderId', options.folderId);
  if (options.albumId) formData.append('albumId', options.albumId);

  try {
    return await sendUpload(formData, options);
  } catch (error) {
    // A 401 can happen when the access token expired between page load and upload.
    // Refresh once through the single-flight client and retry a single time.
    if (error instanceof ApiError && error.status === 401 && !options.signal?.aborted) {
      const refreshed = await http.refreshAccessToken();
      if (refreshed) {
        options.onProgress?.(0);
        return sendUpload(formData, options);
      }
    }
    throw error;
  }
}

export function isRole(value: string): value is RoleName {
  return value === 'Administrator' || value === 'Contributor' || value === 'Listener';
}
