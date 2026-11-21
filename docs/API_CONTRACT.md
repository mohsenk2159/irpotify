# MusicHub Enterprise — API Contract

Base URL (local development): `http://127.0.0.1:5080`
Machine-readable contract: `docs/openapi.json` (captured from the running API).

## Conventions

- JSON request and response bodies use camelCase.
- Authentication uses `Authorization: Bearer <accessToken>` unless stated otherwise.
- Access tokens are short-lived JWTs; refresh tokens rotate on every use.
- Errors use ASP.NET Problem Details (`application/problem+json`) or `{ "error": "..." }`
  for streaming/auth edge cases. Validation failures return HTTP 400, authorization
  failures 403, authentication failures 401, missing resources 404, conflicts 409,
  unsupported media 415, and invalid audio 422.
- Rate limits return HTTP 429. Policies: `login` 10/min, `refresh` 60/min,
  `setup` 5/min, `pairing` 10/min, `upload` 20/min per user, `password` 10/min per
  user, global 600/min per IP.
- Health probes: `GET /healthz` and `GET /api/healthz` (anonymous).
- `GET /` returns a small service banner (anonymous).
- `GET /api/health` answers readiness (database reachable, data volume readable) anonymously.
  It never writes to the data root and does not disclose which optional capabilities
  (FFprobe, malware scanner, Telegram) are configured.

### Pagination

Paged endpoints (`/api/tracks`, `/api/albums`, `/api/artists`, `/api/uploads/history`,
`/api/admin/audit`, `/api/telegram/imports`, `/api/telegram/publications`,
`/api/admin/jobs`) accept `page` (1-based, default `1`) and `pageSize` (default `50`,
clamped to `1..200`) and return:

```json
{ "items": [ ... ], "page": 1, "pageSize": 50, "total": 137 }
```

`total` is the count before paging, so `page * pageSize >= total` marks the last page.
Every ordering has a unique tie-breaker (`id`, descending where the sort is descending),
so paging the same collection twice yields each row exactly once even when many rows
share a timestamp.

`GET /api/playlists/{id}` is not paged: it returns `{ playlist, tracks: [...] }` for one
playlist, in `position` order.

### Error bodies

Two shapes exist, and both are contract:

- `{ "error": "..." }` — used by endpoint-level rejections (403/409/429 for library,
  streaming, pairing and rate-limit paths).
- Problem Details, `application/problem+json` — used by the exception handler and by
  ASP.NET's own 400/401 responses:
  `{ "type": "https://httpstatuses.com/<status>", "title": "...", "status": 422, "traceId": "..." }`.

A client should read `error` when present and fall back to `title`. `traceId` is the
correlation handle for a server log line. `401` and `403` always carry a body — an empty
rejection is a defect, not a variant. Exception-mapped statuses: uploaded media that fails
validation is `422` (too large is `413`), a bad request shape is `400`, and any unhandled
failure is `500` with the generic title; database and exception text never reach the client.

## Roles and write access

Only `Administrator` may change library content: uploads, tracks, albums, artists, genres,
folders and their membership. `Contributor` is **read-only** for the shared library (the role
is retained so existing assignments stay valid) and `Listener` is read-only as well. Every
unauthenticated or under-privileged write returns HTTP 403 with a `{ "error": "..." }` body.
This is enforced on the server; hiding a control in a client is never the boundary.

Per-user data is the documented exception and remains writable by its owner: favourites,
playlists owned by the caller, collaborative playlists the caller can see, and playback
history. `Administrator` can act on any playlist.

| Policy | Effective roles |
| --- | --- |
| `Admin` | `Administrator` |
| `[Authorize]` (authenticated) | any active user |

The legacy `Contributor` authorization policy still exists for compatibility but no endpoint
uses it; all library writes are gated on `Admin`.

## Authentication and setup

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/api/setup/status` | anonymous | Report whether initial bootstrap is complete |
| POST | `/api/setup/initialize` | anonymous + `MUSICHUB_BOOTSTRAP_TOKEN` | One-time admin bootstrap; second call returns 409 |
| POST | `/api/auth/login` | anonymous, `login` limit | Email/password login; returns access + refresh token |
| POST | `/api/auth/refresh` | anonymous, `refresh` limit | Rotate refresh token; reuse revokes the token family |
| POST | `/api/auth/logout` | anonymous (token in body) | Revoke the presented refresh token |
| POST | `/api/auth/logout-all` | authenticated | Revoke every session for the user |
| POST | `/api/auth/change-password` | authenticated, `password` limit | Self-service password change; returns a fresh token pair for the calling device |
| GET | `/api/auth/me` | authenticated | Current user profile and roles |
| GET | `/api/auth/sessions` | authenticated | List active sessions |
| DELETE | `/api/auth/sessions/{id}` | authenticated (owner) | Revoke one session |

Login is lockout-protected after repeated failures. Every authenticated request also
validates the database security stamp, so password resets and role changes invalidate
existing access tokens.

`POST /api/auth/login` accepts an optional `clientType` of `"dashboard"` (default) or
`"player"`. When `clientType` is `"player"` and the account has `canUsePlayer = false`,
the request is rejected with HTTP 401 and `{ "error": "This account is not permitted to
use the player application." }`. The same account can still sign in with
`clientType: "dashboard"`. The access token itself is not scoped to a client type: the
check is a sign-in gate, so a player-only account still needs a role that permits the
library read endpoints it uses (`Listener` is sufficient).

### Password change

`POST /api/auth/change-password` is authenticated and is the self-service path used by
the mobile client (Android) settings screen. Request body:
`{ "currentPassword": "...", "newPassword": "...", "deviceName": "..." }`.
`newPassword` must be at least 12 characters and must differ from the current one.
On success the server rehashes with the current algorithm (PBKDF2-SHA256, 210 000
iterations), rotates the security stamp, revokes every refresh token and session, and
returns a fresh token pair (access + refresh) for the calling device, so the user stays
signed in only where the change was made.

Response `200`: `{ accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt, user }`.
Failures return `400` with `{ "error": "..." }` (wrong current password, short or reused
password, account not found). The pre-change access token and refresh tokens are
immediately invalidated.

The `password` rate limit is configurable via `RateLimiting:PasswordPermitLimit`
(environment form `RateLimiting__PasswordPermitLimit`, default `10` per minute per user);
the integration fixture raises it because the whole suite shares one IP.

## User administration

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/api/users` | Admin | List users with roles, entitlements, usage and `storageUsedBytes` |
| POST | `/api/users` | Admin | Create user with role, quota, entitlement window and `canUsePlayer` |
| PATCH | `/api/users/{id}` | Admin | Update profile, role, active state, quota, entitlements, `canUsePlayer` |
| POST | `/api/users/{id}/reset-password` | Admin | Reset password and rotate security stamp |
| GET | `/api/users/{id}/login-history` | Admin | Login audit trail for a user |

`quotaBytes` of `0` means unlimited storage; any positive value is a byte cap. Uploads
that would exceed the cap are rejected before the file is committed.

### Account entitlements

Regular users and listeners carry three independent limits. Administrators are exempt
from all of them (they are never deactivated by expiry or by a quota).

| Field | Type | Meaning |
| --- | --- | --- |
| `expiresAt` | ISO-8601 timestamp or `null` | Access end date; `null` means no expiry |
| `listenQuotaSeconds` | integer | Listening-time allowance; `0` means unlimited, max `315360000` (10 years) |
| `listenSecondsUsed` | integer (read-only) | Consumed listening time, reported by clients |
| `storageUsedBytes` | integer (read-only) | Current stored bytes for the user's uploads |
| `deactivatedAt` | ISO-8601 timestamp or `null` (read-only) | When the account was automatically or manually disabled |
| `deactivationReason` | string or `null` (read-only) | `expired`, `storage_quota`, `listening_quota`, `manual` |

A limit is exhausted when `used >= quota`, so a storage cap equal to current usage also
deactivates the account. Deactivation is immediate and total: `isActive` becomes `false`,
all refresh tokens are revoked, already-issued access tokens are rejected on the next
request (`401`), and playback reporting returns `403`.

`POST /api/users` accepts `expiresAt` and `listenQuotaSeconds`; an expiry in the past is
rejected with `400`. `PATCH /api/users/{id}` accepts the same fields plus:

| Flag | Effect |
| --- | --- |
| `clearExpiresAt: true` | Removes the expiry date |
| `resetListenUsage: true` | Sets `listenSecondsUsed` back to `0` |

Setting `isActive: true` is an explicit reactivation. It is refused with `400` while the
expiry is still in the past or storage/listening usage still meets the quota; the same
request (or an earlier one) must extend the expiry or lower usage first. Manual
deactivation (`isActive: false`) revokes the account's sessions and records
`deactivationReason = "manual"`. Every change is written to the audit log with the
affected fields.

Sign-in and token validation reuse the same evaluation:

- Login is refused with `401` and a reason-specific `error` ("This account's access
  period has ended.", "This account has used its full storage allowance.", "This account
  has used its full listening allowance.", "This account has been disabled by an
  administrator.").
- The Worker runs `UserEntitlementService.EnforceAllAsync` every two minutes, so an
  expired account is deactivated even when it makes no requests.

The sign-in rate limit is configurable via `RateLimiting:LoginPermitLimit`
(environment form `RateLimiting__LoginPermitLimit`, default `10` per minute per client
IP). The integration fixture raises it because the whole suite shares one IP.

## Device pairing

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| POST | `/api/pairing/create` | authenticated | Create a short-lived, single-use pairing code |
| POST | `/api/pairing/claim` | anonymous, `pairing` limit | Exchange a pairing code for tokens |
| GET | `/api/pairing/sessions` | authenticated | List the user's pairing sessions |

## Uploads and import history

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| POST | `/api/uploads` | Admin, `upload` limit | Multipart upload; accepts one or more `files` |
| GET | `/api/uploads/history` | authenticated (own) | Paginated upload sessions |

`POST /api/uploads` returns HTTP 201 with per-file results:
`{ results: [{ fileName, success, duplicate, error, track }] }`.
The pipeline streams to quarantine, verifies the audio signature, probes metadata
with FFprobe, hashes with SHA-256, runs the malware scanner abstraction, deduplicates
by SHA-256, commits atomically to content-addressed storage, and enqueues outbox and
optional Telegram publication work. Track payloads never include storage paths.

## Tracks and streaming

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/api/tracks` | authenticated | Paged/filterable list (`q`, `folderId`, `albumId`, `artistId`, `genreId`, `favorites`, `includeDeleted`, `sort`, `page`, `pageSize`) |
| GET | `/api/tracks/{id}` | authenticated | Track detail |
| PATCH | `/api/tracks/{id}` | Admin | Update metadata |
| DELETE | `/api/tracks/{id}` | Admin | Soft delete |
| POST | `/api/tracks/{id}/restore` | Admin | Restore soft-deleted track |
| GET | `/api/tracks/{id}/artwork` | authenticated | Extracted artwork |
| POST | `/api/tracks/{id}/stream-token` | authenticated | Short-lived HMAC stream token (10 min) |
| GET/HEAD | `/api/tracks/{id}/stream` | bearer token **or** `?token=` | Audio stream |

Streaming guarantees:

- `Accept-Ranges: bytes`, strong `ETag` equal to the SHA-256, `Last-Modified`,
  `Cache-Control: private,max-age=0`, `Content-Disposition: inline`.
- Full response: HTTP 200 with the exact asset length.
- Valid range: HTTP 206 with `Content-Range: bytes start-end/length` and exact bytes.
- Invalid/unsatisfiable range: HTTP 416 with `Content-Range: bytes */length`.
- Missing/invalid auth or stream token: HTTP 401.
- Bounded 64 KiB buffer, honours request cancellation, exposes no filesystem path.

## Albums, artists, folders, genres

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/api/albums`, `/api/albums/{id}` | authenticated | List/detail |
| POST | `/api/albums`, PATCH `/api/albums/{id}` | Admin | Create/update |
| DELETE | `/api/albums/{id}` | Admin | Soft delete |
| POST | `/api/albums/{id}/tracks`, DELETE `/api/albums/{id}/tracks/{trackId}` | Admin | Membership (max 500 ids per request) |
| GET | `/api/albums/{id}/artwork` | authenticated | Album artwork |
| GET | `/api/artists`, `/api/artists/{id}` | authenticated | List/detail |
| POST | `/api/artists`, PATCH `/api/artists/{id}` | Admin | Create/update |
| GET | `/api/folders` | authenticated | Folder tree |
| POST | `/api/folders`, PATCH `/api/folders/{id}` | Admin | Create/update |
| DELETE | `/api/folders/{id}` | Admin | Soft delete |
| POST | `/api/folders/{id}/tracks`, DELETE `/api/folders/{id}/tracks/{trackId}` | Admin | Membership (max 500 ids per request) |
| GET | `/api/genres` | authenticated | Genre list |
| POST | `/api/genres` | Admin | Create genre |

## Playlists, favorites, history, discovery

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/api/playlists`, `/api/playlists/{id}` | authenticated | Owned/public/collaborative playlists |
| POST | `/api/playlists` | authenticated | Create playlist |
| PATCH/DELETE | `/api/playlists/{id}` | owner | Update/delete |
| POST/DELETE | `/api/playlists/{id}/tracks[/{trackId}]` | owner/collaborator | Add/remove track |
| PUT | `/api/playlists/{id}/reorder` | owner/collaborator | Reorder tracks |
| POST/DELETE | `/api/favorites/{trackId}` | authenticated | Add/remove favorite |
| POST | `/api/history/playback` | authenticated | Record playback event and consume listening time |
| GET | `/api/history` | authenticated | Recent listening history |
| GET | `/api/search?q=` | authenticated | Cross-entity search |
| GET | `/api/home` | authenticated | Home feed (recent, frequent, favorites) |

`POST /api/history/playback` accepts
`{ trackId, positionSeconds, completed, listenedSeconds }`. `listenedSeconds` is the real
played wall-clock time since the previous report; the server clamps each report to
`3600` seconds, adds it to the account's usage, and ignores it for administrators. When
the report exhausts the listening allowance, the request returns HTTP `403` with
`{ "error": "...", "reason": "listening_quota" }`, the account is deactivated, and the
player stops playback and shows the message. Playback reporting for an account that is
already deactivated returns `401` once its access token is rejected.

## Dashboard and administration

All endpoints in this section require the `Admin` policy.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/dashboard/stats` | Library, user, storage, and active-stream counters |
| GET | `/api/admin/metrics` | Process metrics, storage, stream leases |
| GET | `/api/admin/audit` | Paged audit log with action/actor filters |
| GET | `/api/admin/backups` | Backup history |
| GET | `/api/admin/system-events` | Recent system events |
| GET | `/api/admin/settings` | Effective non-secret settings |
| PUT | `/api/admin/settings` | Update allow-listed settings |
| POST | `/api/admin/storage/integrity-scans` | Enqueue a SHA-256 integrity/orphan scan |
| GET | `/api/admin/storage/integrity-scans` | List scans |
| GET | `/api/admin/storage/integrity-scans/{id}` | Scan detail |
| GET | `/api/admin/jobs` | Background jobs by status |
| POST | `/api/admin/jobs/{id}/retry` | Retry a failed job |
| GET | `/api/admin/outbox` | Outbox messages |
| GET | `/api/admin/storage/location` | Both roots, library areas/disk, asset totals, last move |
| POST | `/api/admin/storage/validate` | Check a candidate library folder; changes nothing |
| POST | `/api/admin/storage/relocate` | Queue `storage.relocate`; requires `"confirmation": "relocate"`, returns `202` |
| GET | `/api/admin/storage/relocation` | The latest move's job and progress |

`relocate` answers `400` when a safety check fails and `409` when a move is already queued or an
upload is in flight. Full contract in `docs/STORAGE_RELOCATION.md`.

## Telegram administration

Every route under `/api/admin/telegram` requires `Admin`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/admin/telegram` | Configuration status; never returns the bot token |
| PUT | `/api/admin/telegram` | Save configuration; token encrypted with Data Protection |
| POST | `/api/admin/telegram/enable` | Enable/disable ingestion |
| POST | `/api/admin/telegram/test` | Validate token with Telegram `getMe` |
| POST | `/api/admin/telegram/test-ingest` | Local ingestion adapter (multipart audio) |
| GET | `/api/admin/telegram/imports` | Import history |
| GET | `/api/admin/telegram/publications` | Publication queue |
| POST | `/api/admin/telegram/publications/{id}/retry` | Retry publication |
| GET | `/api/admin/telegram/updates` | Recent updates |

Telegram requests are accepted only from the configured admin Telegram user ID.
Downloads are validated by size, extension, signature, FFprobe metadata, and duration
before import. Duplicate SHA-256 values are reported, not re-imported. Publications
are durable, retried with exponential backoff, and capped by `MaxRetryAttempts`.

## Versioning policy

The API currently exposes version 1 semantics without a URL segment. Any breaking
change requires a new documented version and a migration note. Additive fields are
allowed. `docs/openapi.json` is regenerated from the running API and is the
authoritative shape reference.
