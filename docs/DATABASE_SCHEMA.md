# MusicHub Enterprise — Database Schema

Provider: PostgreSQL 17. ORM: EF Core 10 (`MusicHubDbContext`, Infrastructure assembly).
Migration assembly: `MusicHub.Infrastructure`. Migrations: `20261116002835_InitialCreate`,
`20261116113423_AddUserCanUsePlayer`, `20261117090706_AddUserEntitlements` (current).
Database: `musichub` (host `127.0.0.1`, port `5433` in local development).

All primary keys on aggregate roots are `uuid`. Audit/timestamp columns are
`timestamp with time zone` with UTC values. PostgreSQL column type names are shown
in the source-of-truth form used by EF (`character varying`, `text`, `bigint`, ...).

## Identity and access

### Users
`Id uuid PK`, `Email varchar`, `NormalizedEmail varchar` (unique), `DisplayName varchar`,
`PasswordHash varchar`, `SecurityStamp text`, `IsActive bool`, `QuotaBytes bigint`,
`CanUsePlayer bool` (default `true`; gates `clientType: "player"` sign-in),
`ExpiresAt timestamptz` (nullable access end date),
`ListenQuotaSeconds bigint` (default `0` = unlimited),
`ListenSecondsUsed bigint` (default `0`; clamped per report),
`DeactivatedAt timestamptz` (nullable; set by automatic or manual deactivation),
`DeactivationReason varchar(32)` (nullable; `expired`, `storage_quota`,
`listening_quota`, `manual`),
`FailedLoginCount int`, `LockoutEnd timestamptz`, `CreatedAt timestamptz`,
`UpdatedAt timestamptz`, `LastLoginAt timestamptz`.

Entitlements apply to regular users only; administrators (`Administrator` role) are
exempt from expiry and both quotas. The five entitlement columns are written by
`UserEntitlementService`, the admin user endpoints, and the two-minute Worker sweep.

### Roles
`Id int PK`, `Name varchar` (unique), `Description text`, `IsSystem bool`.
Seeded roles: `Administrator`, `Contributor`, `Listener`.

### UserRoles
`UserId uuid` PK/FK → Users, `RoleId int` PK/FK → Roles.

### RefreshTokens
`Id uuid PK`, `UserId uuid` FK, `FamilyId uuid`, `TokenHash varchar` (unique),
`ExpiresAt timestamptz`, `CreatedAt timestamptz`, `RevokedAt timestamptz`,
`ReplacedByTokenHash text`, `CreatedByIp text`, `RevokedByIp text`, `DeviceName text`.
Reuse of a rotated token revokes the whole `FamilyId`.

### UserSessions
`Id uuid PK`, `UserId uuid` FK, `RefreshTokenId uuid` FK, `DeviceName text`,
`IpAddress text`, `UserAgent text`, `CreatedAt timestamptz`, `LastSeenAt timestamptz`,
`RevokedAt timestamptz`.

### LoginHistory
`Id bigint PK`, `UserId uuid` nullable FK, `Email text`, `Success bool`,
`IpAddress text`, `UserAgent text`, `FailureReason text`, `CreatedAt timestamptz`.

### PairingSessions
`Id uuid PK`, `CodeHash text`, `UserId uuid` FK, `DeviceName text`,
`ExpiresAt timestamptz`, `UsedAt timestamptz`, `UsedByIp text`, `CreatedAt timestamptz`.
Pairing codes are stored hashed and are single-use.

### Devices
`Id uuid PK`, `UserId uuid` FK, `Name text`, `Platform text`, `AppVersion text`,
`CreatedAt timestamptz`, `LastSeenAt timestamptz`.

### Invitations
`Id uuid PK`, `Email text`, `RoleName text`, `TokenHash text`, `CreatedByUserId uuid`,
`ExpiresAt timestamptz`, `AcceptedAt timestamptz`, `CreatedAt timestamptz`.

## Media and library

### FileHashes
`Id uuid PK`, `Sha256 varchar(64)` (unique), `SizeBytes bigint`, `CreatedAt timestamptz`.
Content identity for deduplication.

### AudioAssets
`Id uuid PK`, `FileHashId uuid` FK → FileHashes, `OriginalFileName text`,
`Extension text`, `MimeType text`, `StoragePath varchar`, `SizeBytes bigint`,
`CreatedAt timestamptz`.
`StoragePath` is `music/<sha[0:2]>/<sha[2:4]>/<sha><ext>` and is never exposed by the API.

### Tracks
`Id uuid PK`, `Title varchar`, `NormalizedTitle varchar`, `ArtistDisplay varchar`,
`AlbumDisplay varchar`, `GenreId uuid` FK → Genres, `AudioAssetId uuid` FK → AudioAssets,
`Year int`, `TrackNumber int`, `DiscNumber int`, `DurationSeconds double`,
`Bitrate int`, `SampleRate int`, `Channels int`, `Codec text`, `ArtworkPath text`,
`IsDeleted bool`, `DeletedAt timestamptz`, `CreatedByUserId uuid` FK,
`CreatedAt timestamptz`, `UpdatedAt timestamptz`. Soft delete with restore support.

### Artists
`Id uuid PK`, `Name varchar`, `NormalizedName varchar` (unique), `Biography text`,
`ArtworkPath text`, `CreatedAt timestamptz`, `UpdatedAt timestamptz`.

### TrackArtists
`TrackId uuid`, `ArtistId uuid` (composite PK), `Role varchar`, `Position int`.

### Albums
`Id uuid PK`, `Title varchar`, `NormalizedTitle varchar`, `AlbumArtist varchar`,
`ArtistId uuid` FK → Artists, `Year int`, `Genre text`, `Description text`,
`ArtworkPath text`, `IsDeleted bool`, `CreatedAt timestamptz`, `UpdatedAt timestamptz`.

### AlbumTracks
`AlbumId uuid`, `TrackId uuid` (composite PK), `Position int`.

### Genres
`Id uuid PK`, `Name varchar`, `NormalizedName varchar` (unique), `CreatedAt timestamptz`.

### Folders
`Id uuid PK`, `Name varchar`, `ParentId uuid` self-FK, `Description text`,
`ArtworkPath text`, `CreatedByUserId uuid`, `IsDeleted bool`, `CreatedAt timestamptz`,
`UpdatedAt timestamptz`.

### FolderTracks
`FolderId uuid`, `TrackId uuid` (composite PK), `AddedAt timestamptz`.

### Playlists
`Id uuid PK`, `Name varchar`, `Description text`, `OwnerUserId uuid` FK,
`IsPublic bool`, `IsCollaborative bool`, `ArtworkPath text`, `IsDeleted bool`,
`CreatedAt timestamptz`, `UpdatedAt timestamptz`.

### PlaylistTracks
`PlaylistId uuid`, `TrackId uuid` (composite PK), `Position int`,
`AddedByUserId uuid`, `AddedAt timestamptz`.

### Favorites
`UserId uuid`, `TrackId uuid` (composite PK), `CreatedAt timestamptz`.

### PlaybackEvents
`Id bigint PK`, `UserId uuid` FK, `TrackId uuid` FK, `DeviceId uuid` nullable,
`PositionSeconds double`, `Completed bool`, `StartedAt timestamptz`.

### ListeningHistory
`Id bigint PK`, `UserId uuid` FK, `TrackId uuid` FK, `PlayedAt timestamptz`,
`PlayCount int`.

`IX_ListeningHistory_UserId_TrackId` is a **unique** index on `(UserId, TrackId)`: there is
one row per user and track, not one row per play. A playback report is a single
`INSERT ... ON CONFLICT (UserId, TrackId) DO UPDATE` that increments `PlayCount` and
refreshes `PlayedAt`, which makes reporting idempotent under concurrency. `PlayedAt` carries
its own non-unique index for the "recently played" query. The migration
`20261118231510_ListeningHistoryPerTrack` collapses pre-existing duplicates before creating
the index: it first aggregates `SUM(PlayCount)` and `MAX(PlayedAt)` per pair back onto the
surviving row, then deletes the extra rows. Doing the `DELETE` first would discard the
counts.

## Uploads and import

### UploadSessions
`Id uuid PK`, `UserId uuid` FK, `OriginalFileName text`, `ExpectedSizeBytes bigint`,
`ReceivedBytes bigint`, `Sha256 text`, `Status text`, `QuarantinePath text`,
`TrackId uuid` nullable, `Error text`, `CreatedAt timestamptz`, `CompletedAt timestamptz`.
Status values: `Pending`, `Receiving`, `Stored`, `Duplicate`, `Failed`.

### UploadJobs
`Id uuid PK`, `UploadSessionId uuid` FK, `Status text`, `Attempts int`, `Error text`,
`CreatedAt timestamptz`, `CompletedAt timestamptz`.

### ImportJobs
`Id uuid PK`, `Source text`, `SourceReference text`, `Status text`, `Attempts int`,
`Error text`, `TrackId uuid` nullable, `CreatedAt timestamptz`, `CompletedAt timestamptz`.
Status values: `Pending`, `Processing`, `Completed`, `Failed`, `DeadLetter`.

### DuplicateReports
`Id uuid PK`, `UserId uuid`, `ExistingTrackId uuid`, `IncomingFileName text`,
`Sha256 text`, `CreatedAt timestamptz`.

## Telegram

### TelegramConfigurations
`Id uuid PK`, `BotTokenProtected varchar` (Data Protection ciphertext),
`AdminTelegramUserId bigint`, `AdminChatId bigint`, `DestinationGroupId bigint`,
`DestinationTopicId bigint`, `AutoPublishEnabled bool`, `IngestionEnabled bool`,
`MaxIncomingFileSizeBytes bigint`, `MaxAudioDurationSeconds int`,
`ApiEndpoint varchar`, `LocalBotApiEndpoint varchar`, `MaxRetryAttempts int`,
`UpdatedAt timestamptz`, `UpdatedByUserId uuid`.

### TelegramUpdates
`Id bigint PK`, `TelegramUpdateId bigint` (unique), `PayloadJson text`,
`ReceivedAt timestamptz`, `ProcessedAt timestamptz`, `Error text`.

### TelegramImports
`Id uuid PK`, `TelegramUpdateId bigint`, `TelegramUserId bigint`, `ChatId bigint`,
`FileId text`, `FileName text`, `Status text`, `TrackId uuid` nullable, `Error text`,
`CreatedAt timestamptz`, `CompletedAt timestamptz`.

### TelegramPublications
`Id uuid PK`, `TrackId uuid` FK, `Status text`, `Attempts int`,
`NextAttemptAt timestamptz`, `LastError text`, `TelegramMessageId bigint`,
`ChatId bigint`, `CreatedAt timestamptz`, `UpdatedAt timestamptz`.
Status values: `Pending`, `Publishing`, `Published`, `Failed`, `DeadLetter`.
Durable outbox with exponential backoff.

### TelegramMessageMappings
`Id uuid PK`, `TrackId uuid` FK, `ChatId bigint`, `MessageId bigint`,
`PublishedAt timestamptz`.

## Reliability and operations

### BackgroundJobs
`Id uuid PK`, `Type varchar`, `PayloadJson text`, `Status text`, `Attempts int`,
`MaxAttempts int`, `AvailableAt timestamptz`, `LockedUntil timestamptz`,
`LastError text`, `CreatedAt timestamptz`, `CompletedAt timestamptz`.

### FailedJobs
`Id uuid PK`, `BackgroundJobId uuid`, `Type text`, `PayloadJson text`, `Error text`,
`FailedAt timestamptz`.

### OutboxMessages
`Id uuid PK`, `Type varchar`, `PayloadJson text`, `Attempts int`,
`CreatedAt timestamptz`, `ProcessedAt timestamptz`, `NextAttemptAt timestamptz`,
`Error text`.

### SystemEvents
`Id bigint PK`, `Level text`, `Category text`, `Message text`, `DataJson text`,
`CreatedAt timestamptz`.

### AuditLogs
`Id bigint PK`, `ActorUserId uuid` nullable, `Action text`, `EntityType text`,
`EntityId text`, `DetailsJson text`, `IpAddress text`, `CreatedAt timestamptz`.

### AppSettings
`Key varchar PK`, `Value text`, `IsSecret bool`, `UpdatedAt timestamptz`.
Holds worker heartbeat and Telegram polling offset; secrets are not stored here.

### FeatureFlags
`Name text PK`, `Enabled bool`, `Description text`, `UpdatedAt timestamptz`.

### StorageLocations
`Id uuid PK`, `Name text`, `RootPath varchar`, `IsDefault bool`, `CreatedAt timestamptz`.

### StorageIntegrityChecks
`Id uuid PK`, `Status text`, `AssetCount int`, `MissingCount int`, `OrphanCount int`,
`DetailsJson text`, `StartedAt timestamptz`, `CompletedAt timestamptz`.

### BackupHistory
`Id uuid PK`, `FileName text`, `SizeBytes bigint`, `Status text`, `Sha256 text`,
`Error text`, `StartedAt timestamptz`, `CompletedAt timestamptz`.

## Indexes and constraints (selected)

- `Users.NormalizedEmail` unique; `Roles.Name` unique; `FileHashes.Sha256` unique.
- `Users.ExpiresAt` indexed for the entitlement sweep.
- `Artists.NormalizedName` and `Genres.NormalizedName` unique.
- `AudioAssets.FileHashId` indexed; `Tracks.AudioAssetId`, `Tracks.GenreId`,
  `Tracks.NormalizedTitle`, `Tracks.IsDeleted` indexed.
- `RefreshTokens.TokenHash` unique; `RefreshTokens.FamilyId` indexed.
- `TelegramUpdates.TelegramUpdateId` unique.
- `BackgroundJobs.Status` + `AvailableAt` indexed; `OutboxMessages.ProcessedAt` indexed.
- Composite primary keys for join tables (`UserRoles`, `TrackArtists`, `AlbumTracks`,
  `PlaylistTracks`, `FolderTracks`, `Favorites`).

## Data handling rules

- Uploaded audio never lives in the source tree or an ephemeral container layer;
  it is stored under the configured data root (`MUSICHUB_DATA_ROOT`).
- Quarantine files are removed on rejection, duplicate, or successful commit.
- Deleting a track is a soft delete; the API exposes a restore endpoint.
- The API never returns `AudioAssets.StoragePath` or quarantine paths.
- Bot tokens are stored only as Data Protection ciphertext, with keys persisted under
  `<data root>/keys/dataprotection` (DPAPI-protected on Windows).
