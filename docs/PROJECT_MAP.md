# MusicHub Enterprise — Project Map

> **Purpose:** a fast index of where everything lives, so an agent can jump straight to the
> right file instead of scanning the tree. Read this **before** exploring. Update it when
> files are added, moved or deleted.
>
> Last verified: 2026-11-19. 283 source files (excluding `bin/`, `obj/`, `node_modules/`,
> `build/`, `dist/`, `storage/`, `tools/`, `artifacts/`).

## How to use this map

1. Find the concern in a table below → open the listed file.
2. Only fall back to `grep`/glob when the concern is genuinely not listed.
3. **Do not** recursively scan the repo root: `tools/` alone holds ~2800 vendored binaries
   (ffmpeg, PostgreSQL) and will swamp any search.

Useful narrow searches:

```powershell
# API surface of a module
Select-String -Path src\server\MusicHub.Api\Endpoints\*.cs -Pattern 'Map(Get|Post|Put|Patch|Delete)\('

# Test counts per file
Get-ChildItem tests\server\MusicHub.Tests -Recurse -Filter *.cs |
  ForEach-Object { "$((Select-String -Path $_.FullName -Pattern '\[Fact\]|\[Theory\]').Count)`t$($_.Name)" }
```

---

## 1. Solution layout

| Project | Path | Role |
| --- | --- | --- |
| `MusicHub.Domain` | `src/server/MusicHub.Domain` | Entities + enums, no dependencies |
| `MusicHub.Application` | `src/server/MusicHub.Application` | Thin layer (currently no source files) |
| `MusicHub.Infrastructure` | `src/server/MusicHub.Infrastructure` | EF Core, media, security, Telegram, maintenance |
| `MusicHub.Api` | `src/server/MusicHub.Api` | Minimal-API host, endpoints, JWT, SignalR |
| `MusicHub.Worker` | `src/server/MusicHub.Worker` | Telegram polling/publishing, maintenance, entitlements |
| `MusicHub.Install` | `src/server/MusicHub.Install` | Installer CLI (cluster role, credential file) |
| `MusicHub.Tests` | `tests/server/MusicHub.Tests` | xUnit: integration + unit |
| Web admin | `src/web/admin` | React + TS + Vite dashboard |
| Desktop player | `src/desktop/MusicHub.Player` | WPF `net10.0-windows` + LibVLCSharp |
| Android client | `src/android` | Kotlin + Compose + Media3 + Room |

Solution file: `MusicHub.slnx`

---

## 2. Domain entities

| File | Entities |
| --- | --- |
| `src/server/MusicHub.Domain/IdentityEntities.cs` | `Role`, `User`, `UserRole`, `RefreshToken`, `UserSession`, `Device`, `LoginHistory`, `Invitation`, `PairingSession`, `AuditLog` |
| `src/server/MusicHub.Domain/LibraryEntities.cs` | `FileHash`, `AudioAsset`, `Track`, `Artist`, `TrackArtist`, `Album`, `AlbumTrack`, `Genre`, `Folder`, `FolderTrack`, `Playlist`, `PlaylistTrack`, `Favorite`, `PlaybackEvent`, `ListeningHistory` |
| `src/server/MusicHub.Domain/OperationsEntities.cs` | `UploadSession`, `UploadJob`, `ImportJob`, `StorageLocation`, `StorageIntegrityCheck`, `DuplicateReport`, `TelegramConfiguration`, `TelegramUpdate`, `TelegramImport`, `TelegramPublication`, `TelegramMessageMapping`, `BackgroundJob`, `OutboxMessage`, `FailedJob`, `AppSetting`, `SystemEvent`, `BackupHistory`, `FeatureFlag` |
| `src/server/MusicHub.Domain/Enums.cs` | `UploadStatus`, `JobStatus`, `TelegramPublishStatus`, `DeactivationReasons`, … |

---

## 3. API endpoints (84 `Map*` calls; 69 documented paths in `openapi.json`)

> The two numbers differ because a path with several verbs is one OpenAPI path but several
> `Map*` calls, and anonymous helpers (`/`, `/healthz`) are counted here but not in the
> captured contract.

| File | Routes | Concerns |
| --- | --- | --- |
| `src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs` | **40** | Tracks, albums, artists, genres, folders, playlists, favourites, playback reports, dashboard. **Largest file (767 lines)** — the bulk of authorization work lives here |
| `src/server/MusicHub.Api/Endpoints/AuthEndpoints.cs` | 13 | Login, refresh, logout, setup, users CRUD, change-password, role assignment |
| `src/server/MusicHub.Api/Endpoints/TelegramEndpoints.cs` | 9 | Telegram config, test adapter, imports, publications |
| `src/server/MusicHub.Api/Endpoints/DashboardEndpoints.cs` | 7 | Admin stats, audit log, jobs |
| `src/server/MusicHub.Api/Endpoints/StorageEndpoints.cs` | **10** | Integrity scans, storage jobs, and the library-location contract (`location`, `validate`, `relocate`, `relocation`) |
| `src/server/MusicHub.Api/Endpoints/PairingEndpoints.cs` | 3 | `create` / `claim` / `sessions` |
| `src/server/MusicHub.Api/Endpoints/UploadEndpoints.cs` | 2 | `POST /api/uploads`, `GET /api/uploads/history` |
| `src/server/MusicHub.Api/Endpoints/SetupEndpoints.cs` | 2 | First-run bootstrap |
| `src/server/MusicHub.Api/Endpoints/StreamEndpoints.cs` | 1 | Range streaming (`/api/tracks/{id}/stream`) |
| `src/server/MusicHub.Api/Endpoints/HealthEndpoints.cs` | 1 | `/api/health` readiness |

Supporting API files:

| File | Role |
| --- | --- |
| `src/server/MusicHub.Api/Program.cs` | 381 lines. Middleware order, DI, rate limiter, JWT, exception mapping. **CR-002 order lives here** |
| `src/server/MusicHub.Api/Common.cs` | `PagedResult<T>`, `RequestHelpers.Page`, `PublicUrl`, `ApiResults`, `ArtworkResults` |
| `src/server/MusicHub.Api/CommonDtos.cs` | Shared DTOs / `DtoMapper` |
| `src/server/MusicHub.Api/Security/AuthService.cs` | Token issue/rotate, password hashing, security stamp |
| `src/server/MusicHub.Api/Realtime/LibraryHub.cs` | SignalR `/hubs/library`; also hosts `StreamTracker` (concurrent-stream accounting) |
| `src/server/MusicHub.Api/Realtime/ListeningTimeTracker.cs` | Server-measured listening time: counts bytes written to the stream response, drains per playback report, converts via bitrate (B-023) |
| `src/server/MusicHub.Api/ForwardedHeadersSetup.cs` | Trusted proxies (B-021) |

---

## 4. Infrastructure

| File | Role |
| --- | --- |
| `.../MusicHubDbContext.cs` | 312 lines. All entity config + indexes. **Unique indexes are the concurrency contract** |
| `.../MusicHubDbContextFactory.cs` | Design-time factory for `dotnet ef` |
| `.../Options/Options.cs` | `StorageOptions`, `JwtOptions`, rate-limit + Telegram options |

### Media pipeline

| File | Role |
| --- | --- |
| `.../Media/TrackImportService.cs` | 467 lines. **The** ingestion path shared by HTTP, Telegram, test adapter. Quota lock + `GetOrCreateArtist/Genre` |
| `.../Media/ContentAddressedStorage.cs` | SHA-256, quarantine, atomic commit, signature check, `TryDelete`. Roots are **live properties** read from `LibraryLocationStore`, never captured at construction |
| `.../Media/AudioProbeService.cs` | FFprobe wrapper, `AudioMetadata`, artwork extraction |
| `.../Media/ClamAvMalwareScanner.cs` | Optional `IMalwareScanner` |

### Security

| File | Role |
| --- | --- |
| `.../Security/ConnectionStringResolver.cs` | Env → credential file → dev default, **only in Development** (B-041); `ResolveForHost` is the shared policy |
| `.../Security/DatabaseCredentialFile.cs` | DPAPI-protected credential read/write; classifies the read so a denial is `Unreadable`, not `Missing` (B-040) |
| `.../Security/SecretRedactor.cs` | Removes a known secret by value and secret-shaped patterns from text about to be printed (B-042) |
| `.../Security/DataProtectionSetup.cs` | Keyring bootstrap (B-043/053) |
| `.../Security/CryptoUtil.cs` | Hashing, HMAC stream tokens |
| `.../Security/UserEntitlementService.cs` | Expiry/quota deactivation sweep |

### Storage location

| File | Role |
| --- | --- |
| `.../Storage/LibraryLocationStore.cs` | The two roots: fixed data root (keys, credential) vs movable library root. Pointer `<data root>/library-location.json`, re-read when its timestamp or length changes, so a move by the Worker is visible in the API without a restart |
| `.../Storage/StorageLocationValidator.cs` | The only path-safety implementation (`StorageCheck`). Ordered checks; returns before touching the filesystem once one fails |
| `.../Storage/LibraryRelocationService.cs` | `storage.relocate` worker job: copy → verify every file against the database SHA-256 → flip the pointer. One repair pass then hard failure; never deletes the old copy; records `failed` and leaves the pointer alone |
| `.../Storage/StorageAdminService.cs` | The single "may this move start" rule (validation, one move at a time, no upload in flight, `MaxAttempts = 1`). Called by both the HTTP endpoint and the bot, so the rules exist once |

### Telegram

| File | Role |
| --- | --- |
| `.../Telegram/TelegramBotClient.cs` | Bot API HTTP client |
| `.../Telegram/TelegramIngestionService.cs` | 369 lines. Validation → download → import; offset persistence (CR-007) |
| `.../Telegram/TelegramPublishService.cs` | Outbound publishing with backoff |
| `.../Telegram/TelegramUpdateRouter.cs` | Classifies an update (message/command/callback/ignored) and authorizes **before** dispatch |
| `.../Telegram/TelegramBotHandler.cs` | `/start`, `/status`, `/storage [path]`, `/reports`, `/albums`, `/artists`, `/help`, `/cancel` and the inline callbacks (reports refresh, album management, album open/remove/add, artist list/detail, storage, home). A thin presentation layer - no EF queries for domain rules; aggregates come from `LibraryReportService` and writes from `AlbumService` |
| `.../Telegram/TelegramBotTexts.cs` | Every string in Persian and English; a test asserts each id exists in both |
| `.../Telegram/TelegramKeyboard.cs` | Inline keyboards; `TelegramCallbackData` is `v1:<action>[:<arg>]`, ≤64 bytes, lowercase alphanumeric actions |
| `.../Telegram/TelegramBotStateStore.cs` | Chat state in `AppSettings` under `telegram.bot.state.<chatId>`, 15-minute expiry |
| `.../Telegram/TelegramAlbumPromptStore.cs` | Pending per-file album questions keyed by the **asking message id** under `telegram.bot.album.<chatId>.<messageId>`; 15-minute expiry. Keying by message is what lets several questions be open at once (see `docs/TELEGRAM_BOT.md` 5.1) |
| `.../Telegram/TelegramAlbumEditStore.cs` | Per-message **album edit** screens: row-to-track mapping behind buttons like `v1:amrm:<albumId>:<row>` under `telegram.bot.albumedit.<chatId>.<messageId>`, 15-minute expiry. The full album id + track id pair cannot fit the 64-byte `callback_data` budget, so the row index is the button's payload and this store resolves it |
| `.../Telegram/TelegramStorageService.cs` | Builds the storage screen's data (both roots, per-folder usage, last move) and runs the shared validator |
| `.../Telegram/TelegramAudit.cs` | Writes the audit row for bot-initiated library changes (`album.create`, `album.track.assign`, `album.track.remove`, `album.track.add`); Telegram sender is recorded in `AuditLog.DetailsJson` under the `actor: "telegram"` marker because a bot callback has no user-account principal |
| `.../Library/AlbumService.cs` | **The** album create/assign/list/report/remove rules, shared by the HTTP endpoints and the bot (D-1 option B). Authorization-agnostic; callers authorize |
| `.../Library/LibraryReportService.cs` | Read-only library aggregates for the bot's report screens: `LibraryOverview` (albums/tracks/artists/users/active/inactive/duration), `ArtistSummary`, `ArtistReport` and their paged/list variants. Counts are computed from real EF queries on render, so a refresh shows current state |

### Maintenance

| File | Role |
| --- | --- |
| `.../Maintenance/BackgroundJobClaimer.cs` | Atomic job claim (CR-008) |
| `.../Maintenance/StorageIntegrityService.cs` | Orphan/missing scan |

### Migrations

`src/server/MusicHub.Infrastructure/Persistence/Migrations/`

| Migration | Purpose |
| --- | --- |
| `20261116002835_InitialCreate` | 1274 lines, whole schema |
| `20261116113423_AddUserCanUsePlayer` | `User.CanUsePlayer` |
| `20261117090706_AddUserEntitlements` | Expiry, quotas, listen quota |
| `20261118231510_ListeningHistoryPerTrack` | Unique `(UserId, TrackId)` + dedup SQL (CR-003) |

> `.Designer.cs` and `MusicHubDbContextModelSnapshot.cs` are ~1400 lines each and are
> **generated** — never read them to understand behaviour, and never hand-edit them.

---

## 5. Worker

| File | Role |
| --- | --- |
| `src/server/MusicHub.Worker/Program.cs` | Host wiring, shares DI with API |
| `.../TelegramWorkers.cs` | Polling + publication loops |
| `.../MaintenanceWorker.cs` | Heartbeat, job drain, outbox, retention |
| `.../EntitlementEnforcementWorker.cs` | 2-minute entitlement sweep |

---

## 6. Installer

| File | Role |
| --- | --- |
| `src/server/MusicHub.Install/Program.cs` | 490 lines. Role/database provisioning, credential write, `initdb` handling. **B-043..B-058 live here** |
| `src/server/MusicHub.Install/RoleProvisioning.cs` | The only place that decides the application role's privileges; both statements state every `no*` attribute and both `pg_roles` and `pg_auth_members` are re-read before the credential is written (B-042/B-051) |
| `src/server/MusicHub.Install/TelegramTokenCheck.cs` | Operator CLI: `check-telegram-token` (read-only `getMe` + `getWebhookInfo`; token only from `MUSICHUB_TG_TOKEN` or stdin, never an argument) and `set-telegram-admin` (writes the id `TelegramIngestionService` authorizes against, scoped to the same row the API reads - B-061) |
| `src/server/MusicHub.Install/AdminPasswordReset.cs` | Operator CLI: `reset-admin-password`, the only supported recovery for a forgotten administrator password. Replaces the hash (never recovers it), rotates `SecurityStamp`, revokes refresh tokens and device sessions, clears the lockout, writes an `admin.password.reset` audit row with a null actor. Password only from `MUSICHUB_NEW_ADMIN_PASSWORD` or stdin, never an argument, never printed - B-083 |

---

## 7. Web admin (`src/web/admin/src`)

| Path | Role |
| --- | --- |
| `api/http.ts` | Fetch wrapper, 401 refresh, error mapping (**12 unit tests in `http.test.ts`**) |
| `api/endpoints.ts` | Typed endpoint map (274 lines) |
| `api/types.ts` | 473 lines of API types — **mirror of `docs/API_CONTRACT.md`** |
| `api/session.ts`, `api/client.ts` | Token storage, client assembly |
| `auth/AuthProvider.tsx`, `auth/guards.tsx` | Auth context, route guards, `canContribute` |
| `i18n/translations.ts` | 1242 lines — EN + FA strings |
| `pages/*.tsx` | 16 pages: Login, Setup, Dashboard, Tracks, Albums, Artists, Genres, Folders, Playlists, Upload, Users, Sessions, Audit, Monitoring, Settings, Telegram |
| `ui/*.tsx` | `Layout`, `Player`, `TrackTable`, `Pagination`, `Modal`, `Artwork`, `primitives`, `UiProvider` |

Largest pages: `UsersPage.tsx` (703), `TelegramPage.tsx` (616), `FoldersPage.tsx` (502).

Build output is copied to `src/server/MusicHub.Api/wwwroot` (W-024) — do not edit that copy.

---

## 8. Desktop player (`src/desktop/MusicHub.Player`)

| File | Role |
| --- | --- |
| `ViewModels/MainViewModel.cs` | 1194 lines — the whole player state machine, including the Storage view (location, per-folder usage, path check, relocation progress polling) |
| `Services/PlayerApiClient.cs` | API client, including the four storage calls (`location`, `validate`, `relocate`, `relocation`) |
| `Services/AudioEngine.cs` | LibVLC playback |
| `Services/SessionStore.cs` | DPAPI session storage |
| `Views/MainWindow.xaml` | 550 lines of UI, including the Storage screen |
| `Views/LoginWindow.xaml`, `PlaylistEditorWindow.xaml`, `PlaylistPickerWindow.xaml` | Secondary windows |

---

## 9. Android (`src/android/app/src`)

| Path | Role |
| --- | --- |
| `main/java/.../data/auth/SessionManager.kt` | 448 lines — token refresh, single-flight |
| `main/java/.../data/auth/SecureTokenStore.kt` | Encrypted storage |
| `main/java/.../data/library/LibraryRepository.kt` | 445 lines |
| `main/java/.../data/download/*.kt` | `DownloadRepository`, `DownloadWorker` (WorkManager) |
| `main/java/.../data/sync/*.kt` | `SyncScheduler`, `SyncWorker` |
| `main/java/.../data/local/*.kt` | Room: `Entities`, `Daos`, `Converters`, `MusicHubDatabase` |
| `main/java/.../playback/*.kt` | `PlaybackService` (Media3), `PlayerController`, `MusicHubDataSource`, `EqualizerBridge`, `SleepTimer` |
| `main/java/.../ui/**` | Compose screens: auth (+ `QrScannerScreen`), home, library, player, search, settings |
| `main/java/.../ui/components/ArtworkImage.kt` | Cover image with a deterministic gradient + initial-letter fallback (most tracks have no extracted artwork) |
| `main/java/.../ui/components/FavoriteButton.kt` | Heart toggle shared by Home, Library, Search and Now Playing |
| `main/java/.../ui/theme/ArtworkPalette.kt` | Placeholder palette; pure Kotlin so it is unit-testable |
| `main/java/.../data/library/LibraryContracts.kt` | `HomeDataSource` / `FavoriteWriter` seams that keep the Home state machine testable without Android |
| `main/res/values-fa/strings.xml` | Persian strings |
| `main/res/values/themes.xml`, `main/res/values-night/themes.xml` | Launch theme `Theme.MusicHub` (NoActionBar, transparent system bars). Applied via `android:theme` in `AndroidManifest.xml`; the day/night pair keeps the window background matching the Compose surface |
| `androidTest/java/**` | Instrumentation tests (13 passing) |
| `test/java/**` | JVM unit tests (18 passing): `HomeViewModelTest` (8), `ArtworkPaletteTest` (8), `ManifestThemeTest` (2 - guards that `AndroidManifest.xml` applies `Theme.MusicHub` and that the theme is `NoActionBar`, because a missing `android:theme` makes the platform draw an ActionBar over the Compose content) |

APK output:

```
src\android\app\build\outputs\apk\debug\app-debug.apk                    (18.68 MB)
src\android\app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk (1.01 MB)
artifacts\installers\MusicHub-Android-1.0.0-release.apk                   (release, signed)
```

---

## 10. Tests (`tests/server/MusicHub.Tests`) - 314 passing

| File | Cases | Covers |
| --- | --- | --- |
| `Unit/InstallerSecurityTests.cs` | 49 | B-040 credential-state discrimination, B-041 environment policy, B-042 secret redaction, B-051 role privilege stripping, plus the Telegram operator CLI (token never accepted as an argument, never echoed, scrubbed by value, read-only, and scoped to the single configuration row) |
| `Unit/ComposeTopologyTests.cs` | 10 | Structural guard for `deploy/docker/docker-compose.yml`: B-060 (`pids_limit` must agree with `deploy.resources.limits.pids`), only Caddy publishes ports, `api` exposes 5080 and publishes nothing, `postgres`/`worker` expose nothing, every required variable uses the `:?` form |
| `Unit/DeploymentConfigurationTests.cs` | 33 | Public URL, forwarded headers, compose |
| `Unit/StorageAndSignatureTests.cs` | 32 | `StoragePathTests` (25) + `AudioSignatureTests` (7): SHA-256, magic bytes, traversal |
| `Unit/TelegramBotTests.cs` | 67 | `TelegramCallbackDataTests` (21) + `TelegramKeyboardTests` (13) + `TelegramBotTextsTests` (10, incl. placeholder parity across languages) + `TelegramUpdateClassificationTests` (17) + `TelegramBotAdminGateTests` (6): the 64-byte callback envelope and its narrow character sets, keyboard shapes, both languages present, update classification, admin gate |
| `Integration/StorageRelocationTests.cs` | 11 | Real move against real folders, real files and a **private** database (`musichub_test_relocation`): copy + SHA-256 verify + pointer flip + old copy kept; unverifiable copy leaves the pointer alone and records `failed`; non-empty/protected/current/data-root targets refused without being touched; a failed move is resumable and a damaged partial copy is repaired; store defaults, hot pointer pickup, pointer removal, corrupt pointer reported; validator never probes a folder that already failed a check; two concurrent requests cannot both queue a move (B-068) |
| `Integration/StorageLocationTests.cs` | 9 | The four location/relocate endpoints: two-root split, `401` unauthenticated and `403` for a Listener, validation leaves nothing behind, dangerous targets never created, non-empty and current-library refusals, missing confirmation, dangerous relocate refused before queueing |
| `Integration/TelegramBotStorageTests.cs` | 6 | The bot storage screen through the real router and a recording fake Bot API: stranger refused with no keyboard, real location + both roots shown, the confirm button exists only after a passing validation, confirm queues the shared-service job with `MaxAttempts = 1` and a full payload target, expired session queues nothing, every storage string present in both languages |
| `Integration/TelegramBotRouterTests.cs` | 10 | Router + ingestion coexistence: a document still reaches `TelegramIngestionService`, the frozen non-audio rejection is unchanged, `/start` menu, `/cancel`, unknown commands, session expiry, pagination bounds. **A regression here is a release blocker** |
| `Integration/TelegramAlbumTests.cs` | 12 | Per-file album filing through the real router: two files produce two independent prompts (mutation-proven: keying prompts by chat instead of message id yields 2 → 1), picking an album files that specific track and clears its prompt, creating an album from the bot creates and files it, typing an existing name reuses the album, skip leaves no membership, a replayed callback is refused, the picker lists real albums with only live callbacks and page 2 differs from page 1 (mutation-proven), ordinary text and commands keep the frozen rejection, and the shared `AlbumService` rejects a blank title or a missing track |
| `Integration/TelegramReportAndAlbumEditingTests.cs` | 8 | Library reports + album editing + artist report through the real router: overview shows the real database counts, artist list → detail (2 tracks / 1 album / 2:30), album list → detail, album page 2 differs from page 1, removal needs the confirmation and keeps the audio + clears `AlbumDisplay` on confirm, add-from-bot with shortlist rebuild, stale remove index rebuilds and removes nothing, HTTP delete clears `AlbumDisplay` |
| `Integration/ReviewFindingsTests.cs` | 26 | CR-003/004/005/006/011/012/013/014/017/019/023 + concurrent quota (CR-010) + artist race (B-059) |
| `Unit/DatabaseCredentialTests.cs` | 17 | Credential file, resolver |
| `Unit/PasswordAndTokenTests.cs` | 21 | `PasswordHasherTests` (12), `StreamTokenServiceTests` (5), `RefreshTokenHashingTests` (4) |
| `Integration/EntitlementTests.cs` | 7 | Expiry, storage/listen quota, sweep, reactivation |
| `Integration/IngestionAndWorkerTests.cs` | 6 | CR-007 offset, CR-008 job claim (loopback Bot API stub) |
| `Integration/ChangePasswordTests.cs` | 3 | Password change → 400 not 401 |
| `Integration/TrackEditTests.cs` | 3 | CR-001 PATCH includes asset hash |
| `Integration/RateLimiterPartitionTests.cs` | 2 | CR-002 limiter partitioning |
| `Integration/AuthFlowTests.cs` | 1 | Login/refresh/rotation |
| `Integration/AuthorizationAndTelegramTests.cs` | 1 | Role matrix + Telegram config |
| `Integration/MediaPipelineTests.cs` | 1 | Upload → probe → store |

> `Integration/StorageRelocationTests.cs` uses its own database on purpose: a move verifies every
> audio asset the database knows about, and the shared integration database holds rows written by
> other tests, whose files the move never copied.

> Case counts are from `dotnet test --list-tests` (theory rows expanded), **not** attribute
> counts. One file can hold several classes — `PasswordAndTokenTests.cs` and
> `StorageAndSignatureTests.cs` each declare two or three.

**`Integration/ApiFactory.cs`** — shared fixture. Sets **process-wide env vars in its
constructor**, so the suite is order-dependent. New tests must clean shared rows
(`ClearTelegramStateAsync`) and vary `BuildWav(sampleCount)` because fixtures dedupe by SHA-256.

---

## 11. Deployment (`deploy/`)

| File | Role |
| --- | --- |
| `scripts/common.ps1` | 651 lines. Shared helpers, `Find-MusicHubTool` |
| `scripts/start-dev.ps1` | Starts PostgreSQL + API + Worker (skips the SDK check on a package install, reuses a verified credential instead of re-provisioning it) |
| `scripts/stop-dev.ps1` | Stops dev services |
| `scripts/install-server.ps1` | Production install |
| `scripts/backup.ps1`, `restore.ps1` | Backup/restore |
| `scripts/healthcheck.ps1`, `verify.ps1` | Verification (`verify.ps1` needs a plaintext DB password: use `MUSICHUB_CONNECTION_STRING` on a package install) |
| `docker/docker-compose.yml` | PostgreSQL + API + Worker |
| `docker/README.md` | Container notes |
| `windows/build-package.ps1` | Builds `artifacts\installers\MusicHubServer-win-x64-<version>.zip`: publishes the hosts, bakes the dashboard into the API project `wwwroot`, bundles a private .NET runtime, trimmed portable PostgreSQL and ffmpeg/ffprobe |
| `windows/install.ps1` | One-command install from the package, plus `-Status`, `-Start`, `-Stop`, `-Uninstall`, `-PurgeData`, `-NoScheduledTask` |
| `windows/supervisor.ps1` | Portable supervisor that keeps PostgreSQL, the API and the Worker alive; started by the `MusicHub Server` logon task |

---

## 12. Documentation (`docs/`)

| File | Authoritative for |
| --- | --- |
| `PROJECT_STATUS.md` | **Read first.** Verified status per area, release blockers |
| `WORK_QUEUE.md` | Task rows, owners, status |
| `BUG_TRACKER.md` | `B-*` and `CR-*` findings with fix + evidence blocks |
| `AGENT_REPORTS.md` | Accepted reports (R-*) in the mandated format |
| `AGENT_OWNERSHIP.md` | Which agent owns which paths |
| `AGENT_OPERATING_PROTOCOL.md` | Report format, review rules, status labels |
| `API_CONTRACT.md` | Routes, roles, error shapes, pagination |
| `DATABASE_SCHEMA.md` | Tables, columns, indexes |
| `ARCHITECTURE.md` | Components, ingestion flow, authorization model |
| `openapi.json` | Captured OpenAPI (69 paths) |
| `STORAGE_RELOCATION.md` | The two roots, the pointer file, the move order, path safety, and the four storage endpoints + the bot screen |
| `INSTALL_AND_RUN.md` | Installable artifacts, the release signing key, how the server and the Telegram bot are kept running (supervisor task), firewall rule, and the diagnosis steps for "the phone cannot connect" |
| `ICON_ASSET_INVENTORY.md` | Every text-only button and placeholder glyph across Android, Windows and Web, each with a unique `MH-*` code and a ready-to-send image prompt. Returned artwork is attributed by its code, never by description |

---

## 13. Environment facts (avoid rediscovering)

| Fact | Value |
| --- | --- |
| API base URL | `http://127.0.0.1:5080` |
| PostgreSQL | `tools\pgsql\bin\pg_ctl.exe`, data `storage\postgres-data`, port **5433** |
| Dev DB credentials | placeholder `musichub` / `musichub` |
| Admin login | `admin@musichub.local`. Its password is **not** in `storage/dev.secrets.ps1` - that file's `MUSICHUB_ADMIN_PASSWORD` is written by `start-dev.ps1` but no server project reads it, and a live login with it returns 401 (B-083). If the password is unknown, recover it with `MusicHub.Install reset-admin-password`; see `docs/INSTALL_AND_RUN.md` |
| dotnet | `%LOCALAPPDATA%\Microsoft\dotnet` must be prepended to `PATH`; set `DOTNET_ROOT` |
| `dotnet-ef` | `%USERPROFILE%\.dotnet\tools\dotnet-ef.exe`, run from `src/server/MusicHub.Infrastructure` with `--startup-project ..\MusicHub.Api` |
| Android builds | `JAVA_HOME=C:\Program Files\Microsoft\jdk-17.0.21.21-hotspot` (Gradle 8.9 rejects Java 25) |
| APK | `src\android\app\build\outputs\apk\debug\app-debug.apk` |

### Environment traps

- Dev services **do not survive between shell commands** — start PostgreSQL before each test run.
- `pg_ctl`/`initdb` hang a PowerShell pipeline → use `Start-Process` with separate
  `-RedirectStandardOutput`/`-RedirectStandardError`.
- `psql` mangles multi-statement `-c` and quoted identifiers → write SQL to a file, use `-f`
  with `-v ON_ERROR_STOP=1`.
- The shell scanner rejects very large inline PowerShell → write to `%TEMP%\opencode\*.ps1`
  and run with `powershell -NoProfile -ExecutionPolicy Bypass -File`.
- Stop `dotnet.exe` API/Worker processes before rebuilding.
