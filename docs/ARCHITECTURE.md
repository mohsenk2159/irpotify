# Architecture

## Overview

```text
Browser (dashboard) ──┐
Windows player (WPF) ─┤
Android client ───────┼── HTTPS/JSON + WebSocket ──> MusicHub.Api ──> PostgreSQL
Telegram Bot API <────┘                                   │
                                                          ├── content-addressed storage (music/artwork/quarantine)
                                                          └── SignalR hub (/hubs/library)

MusicHub.Worker ──> PostgreSQL (jobs, outbox, telegram publications)
                └─> Telegram Bot API (polling + sendAudio)
```

## Components

### MusicHub.Api

- ASP.NET Core minimal API, JWT bearer authentication, policy-based authorization (`Admin`, `Authenticated`; `Contributor` retained only for compatibility).
- Endpoint groups: setup, auth/users/sessions, library (tracks/albums/artists/folders/genres/playlists/favorites/history/search/home), uploads, streaming, dashboard/admin, pairing, Telegram admin, settings, storage maintenance, health.
- Rate limiting: global per-IP, stricter login/setup buckets, refresh bucket, upload bucket per user, password-change bucket per user.
- Error handling: `problem+json` mapping with no internal paths or stack traces in responses.
- Security headers and a production CSP; HSTS outside Development.
- SignalR hub at `/hubs/library`; bearer token accepted via query string for the hub only.

### MusicHub.Worker

- Telegram polling worker: long-polls `getUpdates`, persists the offset in `AppSettings`, authorizes the sender, validates the declared audio type/size/duration before download, streams the file to a temporary location with a hard byte limit, then runs the shared import pipeline.
- Telegram publication worker: drains `TelegramPublications` with bounded exponential backoff.
- Maintenance worker: worker heartbeat, queued background jobs (storage integrity scans), outbox drain into the system event feed, and retention pruning.
- Job claiming is atomic. `BackgroundJobClaimer` reads the oldest runnable job and then issues one conditional `UPDATE` that re-tests the claimability predicate, so two worker instances cannot both run the same row — the loser's `UPDATE` matches zero rows and returns no claim. That is the mechanism behind "a single instance processes a job"; there is no advisory lock row.
- Outbox retry: a message that fails is left pending with `Error` set and `NextAttemptAt = now + 2^attempts` seconds, and is dead-lettered after 5 attempts. Only rows whose `NextAttemptAt` has elapsed are claimed.
- Telegram ingestion persists the update offset once per poll, after processing, so a batch that contains only already-seen updates still advances it and polling cannot wedge.

### Shared ingestion pipeline

`TrackImportService` is the single implementation used by HTTP uploads, the Telegram importer, and the admin test adapter:

1. Validate extension and quota/size limits.
2. Stream to quarantine while computing SHA-256 (bounded memory).
3. Verify magic bytes for the claimed format.
4. Extract metadata with FFprobe; reject missing/zero-duration audio and >12 h files.
5. Optional malware scan through `IMalwareScanner` (ClamAV implementation).
6. Duplicate detection by SHA-256, including a concurrent-insert race handled via the unique index.
7. Atomic move to `music/<aa>/<bb>/<sha><ext>`, then DB transaction for asset/track/outbox/publication rows.
8. Emit realtime notifications and an audit entry.

### Storage layout

```text
<data root>/
  music/aa/bb/<sha256><ext>    committed audio
  artwork/<trackId>.jpg        extracted cover art
  quarantine/                  in-flight uploads (never served)
  keys/dataprotection/         Data Protection keys (also encrypt the database credential)
  keys/db-credential.protected installer-written application connection string, encrypted
  keys/db-admin-credential.protected  superuser string used once, on a fresh install only
  backups/                     operator backups (see runbooks)
```

Paths are resolved and normalized; any path escaping the data root is rejected.

### Database credential

The installer (`deploy/scripts/install-server.ps1`, helper `MusicHub.Install`) generates two
independent 256-bit random passwords:

- **superuser** — used only by `initdb` on a fresh cluster and by the follow-up step that creates
  the application role. It is stored encrypted at `keys/db-admin-credential.protected` and is
  never read by the API or the Worker.
- **application role** — set on the `musichub` role, which owns the database and the `public`
  schema but is *not* a superuser. Its connection string is stored encrypted at
  `keys/db-credential.protected`.

Both files are protected with the application's data-protection keyring, which on Windows is itself
DPAPI-encrypted for the installing account, and their ACLs grant only that account. Plaintext
passwords are never printed, never placed on a command line, and never written anywhere else: the
only transient copy is an owner-only file that `initdb` consumes and the installer deletes.

Ownership of the database and the `public` schema matters on PostgreSQL 15+, which no longer grants
`CREATE` on `public` to non-owners; without it every migration fails with
`42501 permission denied for schema public`.

The API and the Worker resolve their connection string through `ConnectionStringResolver`:

1. `MUSICHUB_CONNECTION_STRING`, when set — the container/operator override;
2. the credential file;
3. `ConnectionStrings:Default` from configuration;
4. the development default, only when the environment is Development.

A credential file that exists but cannot be decrypted (different account, restored data root
without its keyring, damaged content) is a startup error, never a silent fallback: falling back
would connect with a different password and produce an authentication failure far from the cause.
Because the credential lives in the file, the plaintext password does not appear in the API or
Worker environment on a native install.

### Clients

- **Admin dashboard** (`src/web/admin`): React + TypeScript + Vite, hash routing, EN/FA with RTL,
  light/dark. Built assets are copied into `MusicHub.Api/wwwroot` so a single origin serves the
  API and the SPA in production.
- **Windows player** (`src/desktop/MusicHub.Player`): WPF `net10.0-windows`, LibVLCSharp for
  decoding (FLAC/MP3/M4A/OGG/OPUS/WAV), DPAPI-encrypted session, short-lived stream tokens,
  transparent access-token refresh. It talks to the same public JSON API as the dashboard and
  never uses an internal storage path.
- **Android client** (`src/android`): Kotlin/Compose/Media3 with pairing and offline downloads.

## Authorization model

- Policy-based authorization with two live policies: `Admin` (the `Administrator` role) and
  `[Authorize]` (any active, authenticated user). A `Contributor` policy still exists for
  compatibility but no endpoint references it.
- **Only `Administrator` may change library content**: uploads, tracks, albums, artists, genres,
  folders and their membership. `Contributor` (kept so existing assignments remain valid) and
  `Listener` are strictly read-only for the shared library. This is enforced on the server — an
  under-privileged write returns HTTP 403 with a `{ "error": "..." }` body — so hiding a control in
  the dashboard or the Android client is never the security boundary.
- **Per-user data is the exception and remains writable by its owner**: favourites, playlists the
  caller owns, collaborative playlists the caller can see, and playback history. Administrators can
  act on any playlist.
- Authorization is decided per endpoint from the route table, not from a role description, so the
  role text is documentation only. `docs/API_CONTRACT.md` holds the authoritative per-route table.

## Identity and sessions

- Access token: JWT with user id, roles, and `security_stamp`.
- Refresh token: random value, only its hash is stored; rotation creates a replacement and links the old hash.
- Reuse of a rotated token revokes the entire token family.
- Every authenticated request re-checks the user's active flag and security stamp in the database.
- The security stamp rotates only on events that must revoke access — a password change, a role
  change, deactivation, or sign-out-everywhere. A display-name or quota edit deliberately does not
  rotate it, so an administrator editing a profile does not sign the user out of a live client.
- Pairing: short-lived code hashed at rest, claimed once, bound to a device name/IP. The claim runs
  the same entitlement check as login and refresh, so it cannot mint tokens for an account that is
  already over its expiry, storage or listening allowance.
- Player access: `Users.CanUsePlayer` is checked at sign-in only. `POST /api/auth/login` with
  `clientType: "player"` returns 401 for a disabled account; `clientType: "dashboard"` (default)
  is unaffected. The issued token is not client-scoped, so authorization stays role-based.
- Mobile self-service: `POST /api/auth/change-password` (authenticated, `password` rate limit) is
  the Android settings entry point. It verifies the current password, rehashes, rotates the
  security stamp, revokes every refresh token, and returns a fresh token pair for the calling
  device; failures return 400 with `{ "error": ... }`. The status is deliberately 400 rather than
  401 because the caller is already authenticated by the bearer token on that request, and every
  client treats a 401 as session expiry.

## Account entitlements

- `Users.ExpiresAt`, `ListenQuotaSeconds` and `ListenSecondsUsed` limit regular users and
  listeners; administrators are exempt by role.
- `UserEntitlementService` owns evaluation and deactivation: a limit is exhausted at
  `used >= quota` (`0` = unlimited), and deactivation sets `IsActive = false`, records
  `DeactivationReason`/`DeactivatedAt`, revokes all refresh tokens and rotates the security
  stamp so live access tokens fail on their next request.
- Enforcement is layered: login/refresh gates, `OnTokenValidated` per request, playback
  reporting (`listenedSeconds`, clamped to 3600 s per report) and a Worker sweep
  (`EnforceAllAsync`, every two minutes) for accounts that make no requests.
- Reactivation is an explicit admin action (`PATCH /api/users/{id}` with `isActive: true`) and is
  refused while the expiry is past or a quota is still met. See `API_CONTRACT.md`.

## Telegram

- Admin-only configuration endpoints; bot token encrypted with Data Protection.
- Ingestion requires both `IngestionEnabled` and a configured admin Telegram user id; messages from any other sender are rejected and recorded.
- Only `audio` messages and audio MIME documents with allowed extensions are accepted; voice/photo/video/archives are rejected before download.
- Publication is durable in `TelegramPublications`, retried with backoff, and records the resulting message mapping.

## Deployment modes

- Native Windows: published API + Worker binaries, PostgreSQL local or external, reverse proxy optional.
- Docker Compose: PostgreSQL, API, Worker, persistent volumes, healthchecks, optional TLS reverse
  proxy and external-exposure options (see `deploy/docker/README.md` when present).
- Remote clients (Windows player, Android) connect to the public HTTPS origin or a private
  tailnet address; TLS is always terminated by a proxy with a valid certificate.

Both modes use the same binaries and environment-variable configuration.

## Known limitations

Tracked in `docs/BUG_TRACKER.md`, including: Docker runtime unavailable on the development host, Telegram live verification requiring real credentials, Android toolchain availability, and multipart uploads buffering before hashing.
