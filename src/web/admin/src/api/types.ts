/** Types mirroring the MusicHub API contract (camelCase JSON, string enums). */

export type RoleName = 'Administrator' | 'Contributor' | 'Listener';

export interface User {
  id: string;
  email: string;
  displayName: string;
  role: RoleName;
  roles: RoleName[];
  isActive: boolean;
  canUsePlayer: boolean;
  quotaBytes: number;
  expiresAt: string | null;
  listenQuotaSeconds: number;
  listenSecondsUsed: number;
  deactivatedAt: string | null;
  deactivationReason: string | null;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface AuthSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: User;
}

export interface SetupStatus {
  initialized: boolean;
  bootstrapTokenConfigured: boolean;
}

export interface SetupResult {
  initialized: boolean;
  user: { id: string; email: string; displayName: string };
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface Track {
  id: string;
  title: string;
  artist: string;
  album: string;
  genre: string | null;
  durationSeconds: number;
  year: number | null;
  trackNumber: number | null;
  discNumber: number | null;
  codec: string | null;
  sizeBytes: number;
  sha256: string;
  createdAt: string;
  isFavorite: boolean;
  artworkUrl: string | null;
}

export interface TrackDetail {
  track: Track;
  artists: { artistId: string; name: string; role: string }[];
  albums: { albumId: string; title: string; position: number }[];
  folders: { folderId: string; name: string }[];
  file: {
    sizeBytes: number;
    mimeType: string;
    extension: string;
    sha256: string;
    bitrate: number | null;
    sampleRate: number | null;
    channels: number | null;
  };
  streamUrl: string;
}

export interface TrackQuery {
  q?: string;
  folderId?: string;
  albumId?: string;
  artistId?: string;
  genreId?: string;
  favorites?: boolean;
  includeDeleted?: boolean;
  sort?: 'newest' | 'title' | 'artist' | 'oldest' | 'duration';
  page?: number;
  pageSize?: number;
}

export interface TrackUpdate {
  title?: string;
  artist?: string;
  album?: string;
  genreName?: string;
  year?: number | null;
  trackNumber?: number | null;
  discNumber?: number | null;
}

export interface StreamToken {
  token: string;
  expiresAt: string;
  streamUrl: string;
  absoluteUrl: string;
}

export interface AlbumSummary {
  id: string;
  title: string;
  albumArtist: string;
  artistId: string | null;
  year: number | null;
  genre: string | null;
  createdAt: string;
  trackCount: number;
  artworkUrl: string | null;
}

export interface AlbumDetail {
  album: {
    id: string;
    title: string;
    albumArtist: string;
    artistId: string | null;
    artistName: string | null;
    year: number | null;
    genre: string | null;
    description: string | null;
    createdAt: string;
    artworkUrl: string | null;
  };
  tracks: Track[];
}

export interface AlbumWrite {
  title?: string;
  albumArtist?: string;
  artistId?: string | null;
  artistName?: string;
  year?: number | null;
  genre?: string | null;
  description?: string | null;
}

export interface ArtistSummary {
  id: string;
  name: string;
  biography: string | null;
  trackCount: number;
  albumCount: number;
  artworkUrl: string | null;
}

export interface ArtistDetail {
  artist: { id: string; name: string; biography: string | null; artworkUrl: string | null };
  tracks: Track[];
  albums: { id: string; title: string; year: number | null; trackCount: number }[];
}

export interface FolderNode {
  id: string;
  name: string;
  parentId: string | null;
  description: string | null;
  trackCount: number;
  children: FolderNode[];
}

export interface GenreSummary {
  id: string;
  name: string;
  trackCount: number;
}

export interface PlaylistSummary {
  id: string;
  name: string;
  description: string | null;
  ownerUserId: string;
  isPublic: boolean;
  isCollaborative: boolean;
  trackCount: number;
  updatedAt: string;
  artworkUrl: string | null;
}

export interface PlaylistDetail {
  playlist: {
    id: string;
    name: string;
    description: string | null;
    ownerUserId: string;
    isPublic: boolean;
    isCollaborative: boolean;
    createdAt: string;
    updatedAt: string;
  };
  tracks: Track[];
}

export interface PlaylistWrite {
  name?: string;
  description?: string | null;
  isPublic?: boolean;
  isCollaborative?: boolean;
}

export interface UserSummary {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  canUsePlayer: boolean;
  quotaBytes: number;
  expiresAt: string | null;
  listenQuotaSeconds: number;
  listenSecondsUsed: number;
  deactivatedAt: string | null;
  deactivationReason: string | null;
  storageUsedBytes: number;
  createdAt: string;
  lastLoginAt: string | null;
  roles: RoleName[];
}

export interface UserCreate {
  email: string;
  password: string;
  displayName: string;
  role: RoleName;
  quotaBytes: number;
  canUsePlayer?: boolean;
  expiresAt?: string | null;
  listenQuotaSeconds?: number;
}

export interface UserUpdate {
  displayName?: string;
  role?: RoleName;
  isActive?: boolean;
  quotaBytes?: number;
  canUsePlayer?: boolean;
  expiresAt?: string | null;
  clearExpiresAt?: boolean;
  listenQuotaSeconds?: number;
  resetListenUsage?: boolean;
}

export interface LoginHistoryEntry {
  id: number;
  userId: string | null;
  email: string;
  success: boolean;
  ipAddress: string | null;
  userAgent: string | null;
  failureReason: string | null;
  createdAt: string;
}

export interface SessionInfo {
  id: string;
  deviceName: string;
  ipAddress: string | null;
  createdAt: string;
  lastSeenAt: string;
  revokedAt: string | null;
}

export interface UploadFileResult {
  fileName: string;
  success: boolean;
  duplicate: boolean;
  error: string | null;
  track: Track | null;
}

export interface UploadResponse {
  results: UploadFileResult[];
}

export interface UploadHistoryEntry {
  id: string;
  originalFileName: string;
  expectedSizeBytes: number;
  receivedBytes: number;
  sha256: string | null;
  status: string;
  trackId: string | null;
  error: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface DashboardStats {
  counts: {
    tracks: number;
    albums: number;
    artists: number;
    users: number;
    playlists: number;
    folders: number;
  };
  storage: {
    bytes: number;
    assets: number;
    disk: { name: string; totalBytes: number; freeBytes: number } | null;
  };
  activeUsers: number;
  activeStreams: number;
  recentTracks: Track[];
  mostPlayed: { plays: number; track: Track }[];
  uploadActivity: { date: string; uploaded: number; rejected: number }[];
  telegram: {
    configured: boolean;
    ingestionEnabled: boolean;
    autoPublishEnabled: boolean;
    destinationConfigured: boolean;
  };
  integrity: {
    id: string;
    status: string;
    assetCount: number;
    missingCount: number;
    orphanCount: number;
    startedAt: string;
    completedAt: string | null;
  } | null;
  backup: {
    id: string;
    fileName: string;
    sizeBytes: number;
    status: string;
    startedAt: string;
    completedAt: string | null;
  } | null;
  failedJobs: number;
  database: boolean;
  generatedAt: string;
}

export interface AdminMetrics {
  process: {
    cpuPercent: number;
    workingSetBytes: number;
    privateMemoryBytes: number;
    gcTotalBytes: number;
    threadCount: number;
    uptimeSeconds: number;
  };
  system: { processorCount: number; machineName: string; os: string; runtime: string };
  disk: { name: string; totalBytes: number; freeBytes: number; usedBytes: number } | null;
  database: { connected: boolean; responseMs: number };
  streams: { active: number };
  jobs: {
    pending: number;
    processing: number;
    failed: number;
    outboxPending: number;
    telegramPending: number;
  };
  workerHeartbeat: string | null;
  measuredAt: string;
}

export interface SystemEvent {
  id: number;
  level: string;
  category: string;
  message: string;
  dataJson: string | null;
  createdAt: string;
}

export interface BackupRecord {
  id: string;
  fileName: string;
  sizeBytes: number;
  status: string;
  sha256: string | null;
  error: string | null;
  startedAt: string;
  completedAt: string | null;
}

export interface AuditEntry {
  id: number;
  actorUserId: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  detailsJson: string | null;
  ipAddress: string | null;
  createdAt: string;
}

export interface SystemSettings {
  values: {
    serverName: string;
    publicBaseUrl: string;
    maintenanceMode: boolean;
    defaultTheme: string;
    defaultLanguage: string;
    maxUploadBytes: number;
    maxConcurrentStreamsPerUser: number;
  };
  capabilities: {
    ffprobeConfigured: boolean;
    malwareScanRequired: boolean;
    clamAvConfigured: boolean;
  };
  updatedAt: string | null;
}

export interface TelegramSummary {
  configured: boolean;
  tokenConfigured: boolean;
  adminTelegramUserId: number | null;
  adminChatId: number | null;
  destinationGroupId: number | null;
  destinationTopicId: number | null;
  autoPublishEnabled: boolean;
  ingestionEnabled: boolean;
  maxIncomingFileSizeBytes: number;
  maxAudioDurationSeconds: number;
  apiEndpoint: string;
  localBotApiEndpoint: string | null;
  maxRetryAttempts: number;
  updatedAt: string | null;
}

export interface TelegramConfigWrite {
  botToken?: string;
  adminTelegramUserId?: number | null;
  adminChatId?: number | null;
  destinationGroupId?: number | null;
  destinationTopicId?: number | null;
  autoPublishEnabled?: boolean;
  ingestionEnabled?: boolean;
  maxIncomingFileSizeBytes?: number;
  maxAudioDurationSeconds?: number;
  apiEndpoint?: string;
  localBotApiEndpoint?: string | null;
  maxRetryAttempts?: number;
}

export type TelegramTestResult =
  | { ok: true; botId: number; username: string | null; firstName: string | null }
  | { ok: false; error: string };

export interface TelegramTestIngestResult {
  success: boolean;
  duplicate: boolean;
  trackId: string | null;
}

export interface TelegramImport {
  id: string;
  telegramUpdateId: number | null;
  telegramUserId: number;
  chatId: number;
  fileId: string | null;
  fileName: string | null;
  status: string;
  trackId: string | null;
  error: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface TelegramPublication {
  id: string;
  trackId: string;
  trackTitle: string;
  status: string;
  attempts: number;
  nextAttemptAt: string;
  lastError: string | null;
  telegramMessageId: number | null;
  chatId: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface TelegramUpdate {
  id: number;
  telegramUpdateId: number;
  receivedAt: string;
  processedAt: string | null;
  error: string | null;
}

export interface HealthResponse {
  status: string;
  version: string;
  utc: string;
  checks: {
    database: boolean;
    ffprobe: boolean;
    storageWritable: boolean;
    malwareScanRequired: boolean;
    malwareScannerConfigured: boolean;
    telegramConfigured: boolean;
  };
}

export interface VersionInfo {
  name: string;
  version: string;
  environment: string;
  utc: string;
}
