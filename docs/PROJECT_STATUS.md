# MusicHub Enterprise — Project Status

Last updated: 2026-11-21 (local development host, Windows)

Status labels: `VERIFIED`, `IMPLEMENTED_NOT_TESTED`, `BLOCKED`, `NOT_IMPLEMENTED`.
`VERIFIED` always cites a command or runtime observation performed on this host.

## Environment (verified)

| Component | Details | Evidence |
| --- | --- | --- |
| .NET SDK | 10.x, user-scoped at `%LOCALAPPDATA%\Microsoft\dotnet` | `dotnet --version` |
| PostgreSQL | 17.6 portable, `tools/pgsql`, cluster `storage/postgres-data`, port 5433 | `pg_ctl status`, API queries succeed |
| FFmpeg / FFprobe | 9.0.2 essentials build under `tools/ffmpeg/.../bin` | fixture generation + metadata extraction in smoke run |
| Database | `musichub` / user `musichub`, migrations `20261116002835_InitialCreate`, `20261116113423_AddUserCanUsePlayer` and `20261117090706_AddUserEntitlements` applied | 44 application tables plus `__EFMigrationsHistory` |
| API | `http://127.0.0.1:5080`, Development environment | `/healthz` returns HTTP 200 `Healthy`; `/api/health` returns `{"status":"ok","checks":{...}}` (`/health` itself is 404) |
| Docker | Not installed on this host | `docker` unavailable; compose files only statically reviewable |
| Windows player | WPF `net10.0-windows`, built and run with the portable .NET 10 desktop runtime | see “Windows desktop player” below |

## Component status

| Area | Status | Notes |
| --- | --- | --- |
| Backend build | `VERIFIED` | `dotnet build MusicHub.slnx -nodeReuse:false` → 0 warnings, 0 errors |
| Domain model + EF Core schema | `VERIFIED` | `InitialCreate` + `AddUserCanUsePlayer` + `AddUserEntitlements` applied; 44 tables |
| Authentication (login, lockout, JWT, refresh rotation, reuse revocation, sessions, setup) | `VERIFIED` | smoke checks 3–4 (11 assertions) |
| Role authorization | `VERIFIED` | listener blocked from upload, user admin, Telegram admin (403) |
| Upload ingestion (SHA-256, FFprobe, duplicate, quarantine, atomic commit) | `VERIFIED` | real WAV fixture uploaded, metadata + SHA-256 asserted, duplicate detected |
| Invalid audio rejection | `VERIFIED` | random bytes with `.mp3` name → HTTP 422 |
| Range streaming + ETag + stream tokens | `VERIFIED` | full 200 with ETag, 206 with 100 bytes and `Content-Range`, 416, 401 paths |
| Library/read APIs | `VERIFIED` | tracks, albums, home, search, dashboard stats all HTTP 200 with data |
| Telegram admin configuration + token masking | `VERIFIED` | token saved, never echoed by PUT or GET; invalid format → 400 |
| Telegram live polling/ingestion/publication | `VERIFIED` | End-to-end against the real bot 2026-11-19: the real Worker long-polled three real messages (`1 imported, 2 rejected`), downloaded the audio document, validated it, stored it content-addressed with a matching SHA-256 and created the track owned by the admin user; offset persisted and all updates marked processed. Publication to a channel is wired but was not exercised because `AutoPublishEnabled` is off and no destination channel is configured (B-005) |
| Storage integrity scan + background jobs + outbox drain | `VERIFIED` | Worker heartbeat written; queued scan completed with 1 asset / 0 missing / 0 orphan; outbox messages processed |
| Admin dashboard build + unit tests | `VERIFIED` | `npm run build` exit 0 (`index-BHnTWma_.js`, `index-C5paYwUU.css`); `npm run test -- --run` 12/12 passing |
| Admin dashboard runtime — sign-in, users, player access | `VERIFIED` | served bundle loaded in Chromium with 0 console errors; created/edited `player.ui.created@musichub.local`, `Player` column flipped No → Yes, toast “User created.”/“User updated”; `/api/users` returned 200 |
| Admin dashboard runtime — entitlements | `VERIFIED` | `#/admin/users` renders `USAGE`/`EXPIRES` columns (`Storage: 0 B / 10 GB`, `Listening: 0 h / Unlimited`, `No expiry`); edit dialog exposes “Access expiry date”, “Listening allowance (hours)”, “Remove expiry date”, “Reset listening usage”; reactivate (▶) on an expired account produced the server message “Cannot reactivate: the access period has ended. Set a future expiry date first.”; extending the date + ticking “Account active” flipped the row to `Active` with `Nov 19, 2026`; FA switch rendered RTL with `مصرف`/`انقضا`, `فضا`, `گوش‌دادن` and fully translated dialog |
| Admin dashboard — remaining pages runtime | `IMPLEMENTED_NOT_TESTED` | library, upload, Telegram, monitoring, audit and sessions pages not yet exercised in a browser this increment |
| Windows desktop player | `VERIFIED` | see "Windows desktop player" section; login gate, DPAPI session, playback, seek, pause/resume, favorites, playlists, search, sign-out all observed at runtime |
| Telegram bot (admin panel foundation + reports) | `VERIFIED` | Router, admin gate, inline keyboards, session store, status screen, the library-storage screen, **per-file album filing with album creation**, and the library overview report. Proven by integration tests through the real router and a recording fake Bot API, plus the full suite at 338/338. The rejection of a real non-admin Telegram sender is still unexercised (no second account) |
| Telegram library reports, album editing and artist report | `IMPLEMENTED_NOT_TESTED` (bot) | `docs/TELEGRAM_BOT.md` sections 5.2. `/reports` shows albums/tracks/artists/users/active users/inactive users/total duration read from the database at render time via `LibraryReportService`; `/albums` opens an album screen whose buttons are `v1:amrm:<albumId>:<row>` (ask) and `v1:amrmdo:<albumId>` (confirm), with track add through `v1:amaddlist:<albumId>:<page>` / `v1:amadd:<albumId>:<row>`; artist report via `/artists` and `v1:artopen:<artistId>`. Track removal drops the album membership only and clears the denormalized `AlbumDisplay` when it was the last album; add and remove both write `TelegramAudit` rows. The row-to-track mapping lives in `TelegramAlbumEditStore` (`telegram.bot.albumedit.<chatId>.<messageId>`, 15-minute expiry, no migration). Proven by 8 integration tests in `TelegramReportAndAlbumEditingTests` (overview real counts, artist list->detail, album list->detail, page 2 differs from page 1, removal needs confirmation and keeps the audio, add-from-bot, stale remove index rebuilds and removes nothing, HTTP delete clears `AlbumDisplay`) plus unit tests for the ≤64-byte button encoding and the screens' texts; all proven by mutation during the implementation. Bot screens themselves remain unexercised against a real Telegram client on a phone |
| Telegram bot auto-start on install (version 1) | `VERIFIED` (defaults) / `IMPLEMENTED_NOT_TESTED` (fresh-install runtime) | Version 1 install contract: the Worker that the Windows supervisor task / `start-dev.ps1` starts and the Docker `worker` service starts runs `TelegramPollingWorker` (`Telegram__WorkerEnabled` default `true`; the generated developer secrets now set it to `true` instead of `false`). `docs/INSTALL_AND_RUN.md` section "Server and Telegram bot" and `deploy/docker/README.md` section 5 now carry the install commands, prerequisites and verification for both platforms. No application-code change was needed; only defaults/scripts/docs. A fresh-install runtime re-verification is pending the operator-provisioned bot token |
| Telegram per-file album filing | `IMPLEMENTED_NOT_TESTED` (bot) | `docs/TELEGRAM_BOT.md` section 5.1. After each successful import the bot asks "which album should this track be filed under?" **as its own message per file**, with an existing-album picker (paged), *create a new album*, and *no album*. Prompts are keyed by the asking message id in `TelegramAlbumPromptStore` under `telegram.bot.album.<chatId>.<messageId>`, so several questions can be open at once without retargeting each other; answered, skipped and 15-minute-expired rows are deleted. `AlbumService` is the single implementation shared by the bot and `POST /api/albums` / `POST /api/albums/{id}/tracks` (D-1 resolved as option B). Proven by 12 integration tests: two files produce two independent prompts (mutation-proven - keying by chat instead of message made it 2 -> 1), picking an album files that specific track, creating an album from the bot creates and files it, typing an existing name reuses it, skip leaves no membership, an answered prompt cannot be replayed, the picker's second page differs from the first (mutation-proven), the empty picker says so while still offering album creation, and ordinary text plus commands keep the frozen rejection. Full suite **338/338, 0 warnings**. Not yet exercised against the real bot on a device |
| Library storage location + relocation | `VERIFIED` (server, bot) / `IMPLEMENTED_NOT_TESTED` (player screen) | Spec `docs/STORAGE_RELOCATION.md`. Real move verified by tests against real folders, real files and a private database: copy, SHA-256 verification of every asset, pointer flip, old copy kept, failure leaves the pointer untouched, and two concurrent requests cannot queue two moves (B-068). The four endpoints were also exercised against the running development API at `127.0.0.1:5080`: `/location` reported the real library (`source: default`, 3 assets / 63,860,484 bytes, live per-area counts and disk free space) and `/validate` refused the data root, the keys folder, `C:\Windows`, `C:\`, a relative path and a traversal path with the expected check ids. The Windows player screen compiles with every binding resolved but has not been operated |
| Android client | `VERIFIED` | Debug APK run on `Medium_Phone_API_37.0` (API 37) against the real API. First run is credential-first with LAN + internet address fields (at least one required), both persist and fail over LAN-first, and the encrypted refresh token survives reinstall. Home/Library/Albums/Artists render real server data; Media3 playback is confirmed by `requestAudioFocus`, `MediaCodec`, `AudioTrack` and a live position ticker (`0:35 / 19:13`). Settings covers identity, active/LAN/internet addresses, health check with failover, change password (server-confirmed: new password accepted, old rejected), HTTP/Wi-Fi toggles, EN/FA with RTL, theme, sign-out and sign-out-everywhere. No administrative surface exists in the client (report R-005, evidence `storage/evidence/final-*.png`). Release-signed since B-069 |
| Android UX refresh (B-076..B-079) | `VERIFIED` | Pull-to-refresh on Home backed by a real `/api/home` round-trip (B-076), cover art with a gradient/initial fallback plus section icons, a gradient header and `ElevatedCard` (B-077), a favourite heart on every track row and in Now Playing with a new Favorites library section (B-078), and edge-to-edge bottom insets fixed so content no longer sits under the system gesture bar (B-079). `gradlew :app:testDebugUnitTest` -> **16/16** new JVM tests (`HomeViewModelTest` 8, `ArtworkPaletteTest` 8); two were proven by mutation - reverting the refresh state to the old `HomeState.Loading` path, and removing the favourite rollback, each made the matching test fail and pass again once restored. `:app:assembleRelease` also succeeds (`BUILD SUCCESSFUL`, correctly signed v2+v3). **Verified on the SM-G975F on 2026-11-20.** The R8-minified release APK installed (`lastUpdateTime` 01:55:51 -> 08:03:39) and launched with no `FATAL`/`AndroidRuntime`/`ClassNotFound`/`NoSuchMethod` and no ANR; the process stayed resident. The new UI rendered (new strings `Your music`, `Pull the list down to sync with the server` were in the view hierarchy), which also proves kotlinx.serialization survived minification because the Home payload decoded. Pull-to-refresh was operated by gesture and produced 72 new server log lines with the real `/api/home` EF queries. Bottom insets measured objectively from a 1080x2280 screenshot: the app surface ends at y=2153 and the system navigation bar (`[0,2154][1080,2280]`, 126 px) begins at y=2154, so no app content is drawn under it. B-080 closed on this evidence. B-079 is reclassified: the same measurement on the pre-fix build also stopped at 2153, so the old build was **not** visibly overlapped in portrait and the original claim stays unproven; the inset fix remains correct but is not a reproduced defect. A real overlap defect found during this pass is B-081. **Favourites were also exercised end-to-end on the device**: tapping a heart in Search results wrote `INSERT INTO "Favorites" ("TrackId", "UserId", "CreatedAt")` to the server, and tapping it again wrote `DELETE FROM "Favorites"`, so the round trip and the toggle-off path are both real. `run-as` correctly refuses (`package not debuggable`), confirming the release build is not debuggable. ActionBar removal (B-081) verified on the same device: `action_bar` is absent from the view hierarchy, the content root is `[0,0][1080,2047]`, and the Search field sits at `[42,128][1038,304]` with no title bar above it. Full JVM suite 18/18. **Artwork and the Favorites tab were also checked on the device**: sampling the 44 dp artwork thumbnail box of a Search result (x=42..157, y=452..567) found 74 distinct colours with a maximum per-pixel saturation spread of 112, and the top-left/bottom-right samples read `rgb(203,181,191)`/`rgb(195,171,185)`, i.e. real rendered image content rather than a flat placeholder rectangle. The Library chip row renders `Tracks`/`Favorites`/`Albums`/`Artists`/`Playlists` at y=147..194 (English identifiers are gone), and after favouriting two tracks the Favorites chip listed exactly those two - `Akhareh Ghesseh (Live)` and `Chizi Begou` - read back from the server, so the new section is populated by real data |
| Docker/Compose assets | `PARTIALLY_VERIFIED` | `deploy/docker/` (Dockerfile.api, Dockerfile.worker, docker-compose.yml, Caddyfile, README). **`docker compose config` now passes** (exit 0) against the real file with a placeholder `.env`, and the JSON form confirms the exposure contract: only Caddy publishes (80:80, 443:443, 443:443/udp); `api` publishes nothing and only `expose`s 5080; `postgres` and `worker` publish and expose nothing. Validating it exposed **B-060 (High, FIXED)**: `pids_limit` alongside `deploy.resources.limits` without `limits.pids` made the whole project invalid, so `docker compose up` could never have worked. Still NOT verified: an actual image build and `up`, because the Docker engine on this host cannot start (Docker Desktop 4.99.0 is installed without the privileged `com.docker.service` and without its WSL distros, and registering either needs elevation). See W-025 |
| Container runtime verification | `BLOCKED` | Docker absent on this host (B-007); see W-025 |
| Installer (random DB password, encrypted at rest) | `VERIFIED` | `deploy/scripts/install-server.ps1` on a fresh cluster: generates a CSPRNG superuser password and a separate unprivileged application password, stores both data-protection-encrypted under `keys/` with ACLs restricted to the installing account, takes database and `public` schema ownership, and verifies the result. The API booted with no connection string in its environment, applied all migrations and reported `checks.database=true`; `storage/verify-dbcredential.ps1` 12/12 (report R-006) |
| Automated backend tests | `VERIFIED` | `dotnet test MusicHub.slnx --nologo` -> **372 passed, 0 failed** (7 m 36 s, exit code 0), run on 2026-11-21 against the repository dev cluster on `127.0.0.1:5433`. The previous 353 figure in this row was stale: the suite had already reached 372 with the R-024 `AdminPasswordResetTests` increment. This run adds no new cases - it re-establishes the baseline after the `start-dev.ps1` edits and proves they regress nothing. The suite requires that cluster: with it stopped, all 111 integration tests fail with `Failed to connect to 127.0.0.1:5433` while the 261 unit tests still pass |
| Windows server package | `VERIFIED` | `artifacts\installers\MusicHubServer-win-x64-1.0.0.zip` (190,492,467 B / 181.7 MiB, SHA-256 `2FD93047...56CD414` + `.sha256` sidecar). Self-contained: API, Worker, dashboard, private .NET 10 runtime, trimmed portable PostgreSQL 17, ffmpeg/ffprobe - no SDK, no Docker, no network at install time. `deploy\windows\install.ps1` completed 7/7 steps from the final zip on a clean directory: 17/17 payload entries, fresh cluster initialised, `Health: status=ok database=True storageReadable=True`, `/api/setup/status` reachable, Worker running, `/index.html` 951 B and `/api/health` HTTP 200 over both `127.0.0.1` and the LAN address with CSP+HSTS, dashboard assets 525,322 B / 32,580 B, bootstrap 201 + login 200 + `/api/auth/me` roles=Administrator. The logon task is registered (`RestartCount 999`, unlimited execution time), `Start-ScheduledTask` brought the stack up from cold, the supervisor restored both killed hosts, and `-Uninstall` removed the task while preserving `storage\`. Seven defects found and fixed: B-085/B-086/B-087 (Critical), B-088/B-089 (High), B-090 (Medium), B-091 (Low). Report R-025. Not verified: a host with **no** .NET runtime at all, and a Windows build other than the development machine |
| Client tamper resistance | `VERIFIED` | Report `docs/security/CLIENT_TAMPER_ASSESSMENT.md`. The Android APK contains **no** licence/premium/paywall/billing logic (grep for `isPremium\|isPro\|license\|billing\|paywall\|entitle` returns nothing), so there is nothing client-side to patch; all authority is server-side and re-checked per request in `JwtBearerEvents.OnTokenValidated`. Installed APK verified non-debuggable, v2+v3, RSA 4096. **B-023 fixed**: listening time is measured from bytes served on `/stream` and charged as `max(clientReport, bytes/bitrate)`, proven by the integration test that fails on the pre-fix code and a live probe that charged 349 s to a client that reported 0 (`9 passed, 0 failed`). R8 enabled for release (APK 13.8 MB -> 4.7 MB). Remaining: B-073 cleartext NSC (investigated, deliberately unchanged), B-074 no attestation (accepted), B-075 release-key custody (operator action) |
| Security audit | `IN_PROGRESS` | W-029 credential code audited read-only (W-018), twice and reconciled: 19 findings B-040..B-058. **All four High findings are now FIXED and live-verified** (W-032/R-012): B-040 unreadable credential misreported as missing, B-041 Worker falls back to the development default in Production, B-042 generated password echoed by PostgreSQL error text, B-051 the rotate path preserved a pre-existing superuser role. Verified clean before and still clean: DPAPI `CurrentUser` scope, atomic write incl. rotation, 256-bit CSPRNG password with a safe alphabet, `QuoteIdentifier` doubling, `Unreadable` hard-error path, and expired-key grace. Remaining open findings are Medium or lower; the highest is B-052 (the same-role guard compares the client-declared username instead of `select current_user`). Full-repository threat model outstanding |
| Code review | `VERIFIED` | W-019 read-only review complete: 25 findings (CR-001..CR-025). All 25 are FIXED and regression-tested — High CR-001/CR-002 in R-010, the remaining 23 in W-030 (R-011). Ten fixes were proven by reverting them and observing the new test fail; the CR-003 migration was applied to a real database |
| API contract documentation | `VERIFIED` | `docs/openapi.json` captured from running API (69 paths) |

## Runtime smoke evidence

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-api.ps1
```

Observed result on 2026-11-16: **42 passed, 0 failed**. Report written to
`artifacts/smoke/smoke-api-result.json`.

Covered: health, setup state, login rejection/success, `/auth/me` auth requirement,
refresh rotation, refresh reuse detection, post-revocation login, FFmpeg fixture,
upload acceptance, metadata, SHA-256 format, no storage-path leakage, duplicate
detection, invalid-audio rejection, unauthenticated upload rejection, track/album/home/
search/stats reads, stream token, full stream + ETag, range 206, unsatisfiable 416,
unauthenticated and invalid-token stream rejection, admin-created listener, listener
403s (upload/users/Telegram), Telegram config read, invalid token format, token save,
token non-echo and non-readback, disabled test-ingest rejection, audit, metrics,
system events, and OpenAPI document availability.

A defect found by this run — EF Core `NavigationBaseIncludeIgnored` aborting every
import in `TrackImportService` — was fixed; see `BUG_TRACKER.md` B-009.

## Windows desktop player

New project `src/desktop/MusicHub.Player` (WPF, `net10.0-windows`, LibVLCSharp 3.10.2 +
VideoLAN.LibVLC.Windows 3.0.24, added to `MusicHub.slnx`).

| Capability | Evidence |
| --- | --- |
| Build | `dotnet build src\desktop\MusicHub.Player\MusicHub.Player.csproj` → 0 warnings, 0 errors |
| Sign-in | real login against `http://127.0.0.1:5080` with `clientType=player`; session restored silently on relaunch via `%APPDATA%\MusicHub\session.dat` (DPAPI `CurrentUser`) |
| Permission gate | `player.denied@musichub.local` (`canUsePlayer=false`) → sign-in shows *“This account is not permitted to use the player application.”* and stays on the login window; the same account still signs in with `clientType=dashboard` |
| Navigation | Home, Tracks, Albums, Artists, Playlists, Favorites, Search, Settings all rendered their bodies after the view-flag fix (B-014) |
| Browsing | home feed, album detail + “Play all”, artist detail, playlist detail, search for `chil` returned the seeded track |
| Playback | track played through a stream token; position advanced 0.63 → 5.33 s over 5 s; pause froze position (delta 0.00 over 3 s); resume advanced 3.14 s in 3 s |
| Seeking | mouse click at 50 % of the seek bar moved position from 23.27 s to 578.65 s of 1153.65 s |
| Favorites | player-bar star toggled the server state: `/api/tracks?favorites=true` total 0 → 1 → 0 |
| Playlists | created `Road trip` from the player, added the current track (`trackCount` 0 → 1), deleted it via the confirm dialog (`GET /api/playlists` back to 0) |
| Preferences | shuffle/repeat/volume persisted to `%APPDATA%\MusicHub\player-settings.json` |
| Sign-out | confirm dialog → session file removed → login window returned |
| Listening-time reporting | `MainViewModel` accumulates real played wall-clock seconds with a `Stopwatch`, flushes every 30 s and on pause/track change/sign-out/dispose via `POST /api/history/playback` (`listenedSeconds`); a `403` from that endpoint stops playback and shows the server message |
| Revoked access | `PlayerApiClient.SessionExpired` → `SignOutAsync`, so expiry/quota deactivation returns the user to the sign-in window instead of a dead session |

Streaming contract used by the player was probed directly: `POST /api/tracks/{id}/stream-token`
then `Range: bytes=0-1023` → `206 Partial Content`, `Content-Length: 1024`,
`Content-Type: audio/mpeg`, `Content-Range: bytes 0-1023/46246384`.

Player limitations are listed in `src/desktop/MusicHub.Player/README.md` (Windows only, no
offline cache, no lyrics/equaliser, no cross-device remote control).

## Player access permission

`User.CanUsePlayer` (`bool`, default `true`, migration `20261116113423_AddUserCanUsePlayer`)
lets an administrator decide whether an account may use the player application:

- Dashboard: “Allow player application” checkbox in the create and edit user dialogs, plus a
  `PLAYER` column showing `Yes`/`No`.
- API: `canUsePlayer` on `GET /api/users`, `POST /api/users` and `PATCH /api/users/{id}`.
- Sign-in gate: `POST /api/auth/login` with `clientType: "player"` returns HTTP 401
  `{ "error": "This account is not permitted to use the player application." }` when the
  flag is false; `clientType: "dashboard"` (the default) is unaffected.

Runtime evidence: a user created through the dashboard with the box cleared had `Player = No`
and could not sign in to the player, then signing in to the dashboard still worked; flipping
the checkbox to `Yes` allowed player sign-in with `canUsePlayer=True` returned by the API.

## Container and external-exposure assets

Authored under `deploy/docker/` and the repository-root `.env.example` (W-016):

| File | Purpose |
| --- | --- |
| `Dockerfile.api` | Multi-stage SDK 10 → aspnet 10 image for the API; ffmpeg/ffprobe and curl in the runtime, non-root `1654:1654`, `/var/lib/musichub` pre-owned for named-volume inheritance, `HEALTHCHECK` on `/healthz`. Optionally compiles the dashboard via a `web`/`web-export` stage. |
| `Dockerfile.worker` | Multi-stage image for the worker; ffmpeg/ffprobe/procps, non-root, no `EXPOSE`, process-liveness healthcheck. |
| `docker-compose.yml` | `postgres` + `api` + `worker` + `caddy`. Only Caddy publishes ports (80/443/443udp); the API, database and worker publish none. API and worker run `read_only` with `cap_drop: ALL`, `no-new-privileges`, `pids_limit`, log rotation and grace periods. |
| `Caddyfile` | Automatic HTTPS for `$MUSICHUB_PUBLIC_HOSTNAME`, HTTP→HTTPS redirect, 4 GB body cap, `reverse_proxy api:5080`, access logging off because stream/pairing URLs can carry bearer material. No TLS weakening. |
| `README.md` | Runbook: secret generation, dashboard build, bootstrap, Caddy / Cloudflare Tunnel / Tailscale / plain port-forward exposure, migrations, operations, backup-restore, security posture, known gaps. |
| `.env.example` | Every compose variable with placeholders and generation guidance; no real secrets. |

Configuration was traced to code, not guessed: `MUSICHUB_CONNECTION_STRING`, `MUSICHUB_JWT_SECRET`
(≥ 32 chars, fail-fast), `MUSICHUB_DATA_ROOT`, `MUSICHUB_PUBLIC_URL` (`PublicUrl.Resolve` treats a
blank value as unset and fails loudly on a non-absolute URL, B-020), `MUSICHUB_BOOTSTRAP_TOKEN`,
`MUSICHUB_FFPROBE_PATH`/`MUSICHUB_FFMPEG_PATH`, `MUSICHUB_REQUIRE_MALWARE_SCAN`,
`MUSICHUB_CLAMAV_HOST`, `Database__MigrateOnStartup`, `Telegram__WorkerEnabled`, `ASPNETCORE_URLS`,
`ForwardedHeaders__KnownProxies`/`KnownNetworks`/`ForwardLimit` (B-021; the blanket
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` switch is no longer used), `Cors__AllowedOrigins__N`.

Validation performed:

Static validation (before Docker was available on this host):

- Compose parsed with a real YAML 1.2 parser and checked for services, networks, volume references,
  unpublished ports, dependency conditions, security options, mounts and that all interpolated
  variables are documented in `.env.example`.
- Dockerfiles parsed with a continuation-aware validator: valid instructions, `ARG` before `FROM`,
  named `COPY --from`, `USER`, `ENTRYPOINT`, `HEALTHCHECK`, ffmpeg in the runtime stage, no secret
  or runtime-data `COPY`.
- Scan for TLS/secret weakening (`skip_verify`, `tls internal`, `auto_https off`, `curl -k`,
  `NODE_TLS_REJECT_UNAUTHORIZED`, `TrustServerCertificate=true`, trust auth) → none.
- All created files are LF-only (CRLF breaks Dockerfile continuations).

Runtime validation (Docker Desktop, linux/amd64, on this host):

- `docker compose config` exits `0` for the base file and for the base + LAN overlay.
- `docker build` produced `musichub/api:local` and `musichub/worker:local`, both `linux/amd64`,
  both `USER 1654:1654`.
- `docker compose up -d` brought up `postgres`, `api`, `worker` and `caddy`; `postgres`, `api` and
  `worker` report `healthy`.
- The LAN publication resolves to the intended socket and the dashboard answers through it:
  `GET /` → `302` → `/index.html` (`200`), the JS bundle (`200`), `POST /api/auth/login` → `200`,
  `GET /api/dashboard/stats` → `200`, `GET /api/users` without a token → `401`.
- The API image compiles the dashboard in its `web` stage and copies it into `wwwroot`, so a pulled
  image serves the UI without the operator pre-seeding anything.

Status is `VERIFIED` for the container topology on `linux/amd64`. Not yet done: an image
vulnerability scan, a `linux/arm64` build (the release workflow builds it; it has not run here),
and the Cloudflare/Tailscale overlays, which are documented patterns rather than tested
deployments. The remaining assumptions are listed in `deploy/docker/README.md` §12.

## Install-time network discovery and port selection

`VERIFIED` on this host.

Both install paths discover the machine's own address instead of assuming one, and let the operator
choose the listening port from the allowed range:

- `deploy/scripts/network.ps1` is the single implementation, dot-sourced through `common.ps1`:
  `Get-MusicHubLanAddress` (IPv4, loopback/APIPA/virtual adapters excluded, default-route adapter
  ranked first, with a Linux/macOS DNS fallback), `Test-MusicHubPortAvailable` (binds and releases),
  `Find-MusicHubFreePort`, `Select-MusicHubListenPort` (validates range and live socket state,
  re-asks instead of accepting a port that would fail), `Select-MusicHubLanAddress`,
  `Resolve-MusicHubEndpoint` and `Write-MusicHubAccessSummary`.
- The answer is persisted to `storage/install.endpoint.json` (gitignored, JSON rather than
  PowerShell so reading it cannot execute code), so the second install script reuses it. A port
  already held by this very stack is recognised through `/healthz` and reused instead of being
  treated as a conflict.
- `install-server.ps1` and `start-dev.ps1` call it before touching the database; `start-dev.ps1`
  writes the chosen port and host into the generated `ASPNETCORE_URLS` and `MUSICHUB_PUBLIC_URL`,
  and `-BindLan` switches the bind from loopback to all interfaces so phones can connect.
- `deploy/docker/install.ps1` does the same for containers, writes `MUSICHUB_LAN_ADDRESS` and
  `MUSICHUB_LAN_PORT` into `.env`, generates the secrets when they are still placeholders, and
  prints the URL to open. `docker-compose.lan.yml` reads both variables with the `:?` form, so the
  repository contains no literal address and a missing value fails the deployment.

Verified: address detection returned the real adapter with the default route; the free-port search
skipped an occupied 5080 and proposed 5081; `Resolve-MusicHubEndpoint` reused the recorded port on a
second call; `docker compose config` resolved the overlay to `host_ip`/`published`/`target` as
intended and refused to run without the variables. `InstallEndpointTests` (6 tests) asserts the
module's contract and that no installer file embeds a private address; the whole suite is
**360/360, 0 failed**.

## Account expiry and usage quotas

Regular users and listeners can be limited three ways; administrators are exempt. The
capability is `VERIFIED` on this host.

- **Schema** — `Users.ExpiresAt`, `ListenQuotaSeconds`, `ListenSecondsUsed`, `DeactivatedAt`,
  `DeactivationReason` (`20261117090706_AddUserEntitlements`, `Users.ExpiresAt` indexed).
- **Evaluation** — `UserEntitlementService` treats a limit as exhausted when
  `used >= quota`; any positive allowance, `0` = unlimited; deactivation sets `IsActive=false`,
  the reason, `DeactivatedAt`, revokes all refresh tokens and bumps the security stamp.
- **Enforcement points** — login and refresh (reason-specific messages), every authenticated
  request via `OnTokenValidated`, `POST /api/history/playback` (clamped to 3600 s per report),
  and the Worker sweep `EntitlementEnforcementWorker` (`EnforceAllAsync`, 2-minute interval).
- **Dashboard** — expiry date picker, listening allowance in hours, “Remove expiry date” and
  “Reset listening usage” flags, `USAGE` (storage + listening) and `EXPIRES` columns,
  deactivation-reason badges, and a reactivate action; EN/FA strings.
- **Reactivation** — `PATCH /api/users/{id}` with `isActive: true`; refused while the expiry is
  past or a quota is still met, so the administrator must extend/reset in the same request.
- **Tests** — `tests/server/MusicHub.Tests/Integration/EntitlementTests.cs` (7 tests): expiry +
  session revocation, listening quota via playback, storage quota at login, background sweep,
  admin exemption, manual deactivate/reactivate, validation and clear/reset flags.
- **Runtime evidence (2026-11-18)** — `quota.runtime@musichub.local` with a 60 s allowance:
  first report 30 s → `204`, usage `30/60`; second report → `403`
  `{"error":"This account has used its full listening allowance.","reason":"listening_quota"}`,
  account `isActive=false`, `reason=listening_quota`, `used=60/60`; re-login `401`; PATCH with
  `isActive=true, listenQuotaSeconds=3600, resetListenUsage=true` restored `used=0/3600` and
  login succeeded. `expiry.runtime@musichub.local` with a 15 s window: `/api/auth/me` `200`
  before, `401` after, re-login `401`
  `{"error":"This account's access period has ended."}`, `reason=expired`; restoring a future
  date + `isActive=true` in the dashboard flipped the row back to `Active`.
- **Worker sweep evidence** — a listener (`sweep.runtime@musichub.local`) was given a 90-second
  expiry and then left idle. The Worker log recorded `Entitlement enforcement worker started.`
  and `Entitlement sweep deactivated 1 account(s).`, and `GET /api/users` showed
  `isActive=false`, `reason=expired`, `deactivatedAt` set with no request from that account.
- **Resilience observed** — when PostgreSQL was stopped mid-session the next sweep logged
  `Entitlement enforcement sweep failed.` and carried on; after the database returned, the sweep
  deactivated the expired account on its next pass. The sign-in call made while the database was
  down returned `500` (B-025, availability hardening).

## Known limitations

1. Docker is not installed; `deploy/docker/` is authored and statically validated but no image,
   container or Caddy configuration has been run (W-025).
2. The Android client is verified on the API 37 emulator against the real API, but this host has
   7.9 GB of RAM and often under 0.5 GB free during a full stack, so the emulator VM intermittently
   ANRs (B-031) and `adb screencap` can return a blank ~21 KB PNG while the accessibility tree still
   holds the full UI - captures must be retaken after freeing memory. The APK is debug-signed only.
   B-032 and B-035 now have on-device regression tests (`:app:connectedDebugAndroidTest`, 13/13 on
   `Medium_Phone_API_37.0`); the rest of the client still has no instrumentation coverage.
3. Live Telegram behaviour cannot be verified without a real bot token; the admin test
   adapter and token-handling paths are exercised instead.
4. The automated backend suite covers auth, authorization, the media pipeline, entitlements,
   password change, track editing, rate-limiter partitioning, deployment configuration (pairing URL
   resolution and forwarded headers), crypto primitives, the database credential, the installer security
   findings and the W-019 review findings (**212 tests**), but not every endpoint or the dashboard/player
   clients. W-030 closed the test gaps the review named — playlist/favourite ownership, `includeDeleted`
   authorization, concurrent playback reports, concurrent over-quota uploads, and Telegram offset
   persistence — with 31 new test cases, and W-032 added 35 `InstallerSecurityTests` cases for
   B-040/B-041/B-042/B-051.
   The remaining untested fixes are listed as `IMPLEMENTED_NOT_TESTED` in R-011 (CR-009, CR-015, CR-016,
   CR-018, CR-020, CR-022, CR-024, CR-025): the code is in place and the suite passes, but reverting one
   of them would not fail a test.
   W-035/W-037 bring the suite to **314 tests** (21 new): the bot router and its storage screen, the
   storage endpoints, and a real library relocation against real folders, real files and a private
   database. Three of the new tests were proven to fail with their fix reverted - the failure-state
   write, the pre-queue safety validation, and the relocation queue lock (B-068).
5. A native install keeps the database password only in the encrypted credential file; the container
   deployment still supplies it through `MUSICHUB_CONNECTION_STRING` in `.env`, which is the intended
   operator override. `deploy/scripts/start-dev.ps1` now provisions the developer role through
   `MusicHub.Install` (`--create-role --create-database`, then `verify-credential --probe`) instead
   of writing a plaintext password, but this wiring has not been executed end to end: the existing
   cluster's superuser password is unknown, so a full run needs `-AdminConnection`. The current
   `storage/dev.secrets.ps1` still carries the old override until it is regenerated.
6. The Windows player targets Windows only and has no offline cache. The Android client is
   implemented (dual LAN/internet sign-in, session persistence, library/player/settings) and
   builds a debug APK; emulator runtime evidence is recorded separately in the Android section.
7. Background dev processes (PostgreSQL, API, Worker) do not survive a host restart and must
   be started again with `deploy\scripts\start-dev.ps1`.
8. Client IPs and the HTTPS scheme behind a reverse proxy now come from `ForwardedHeadersSetup`
   (`ForwardedHeaders__KnownNetworks`/`KnownProxies`), installed before the rate limiter and the
   audit writer (B-021, fixed). The framework switch `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is no
   longer set, because it clears the known-proxy list and trusts every peer. A deployment behind a
   proxy that is not listed will record the proxy's address as the client IP.
9. Listening time is reported by the client (`listenedSeconds`) and clamped to 3600 s per report
   server-side, so the server bounds a single forged report but cannot prove real playback; an
   honest client reports true wall-clock time. Playback is still fully blocked once the quota is
   spent (B-023 tracks the residual risk).

## Release readiness

Not release-ready. Blocking items: container runtime verification (W-025/B-007), live Telegram
verification (W-009/B-005), and browser verification of the remaining dashboard pages (W-014). The four
High-severity findings from the W-029 credential audit — B-040 unreadable credential misreported as
missing, B-041 Worker falling back to the development default in Production, B-042 generated password
echoed by PostgreSQL error text, and B-051 the rotate path persisting a pre-existing superuser role —
are **fixed and live-verified** (W-032/R-012), so the credential audit no longer blocks release. The
audit itself is no longer a blocker either — it has run twice and its findings are recorded; the
remaining W-018 scope is the full-repository threat model. Android build evidence is now in place
(APK plus 13/13 instrumentation tests), and the two deployment defects B-020 and B-021 are fixed and
verified, so W-015 is no longer a release blocker.

The W-019 review is complete: 25 findings (2 High, 9 Medium, 10 Low, 4 Informational). **All 25 are now
fixed and regression-tested**, so code review no longer blocks release. CR-001 (`PATCH /api/tracks/{id}`
returned 500 after committing the edit) and CR-002 (the rate limiter ran before authentication, so the
per-user password/upload partitions collapsed to per-IP) were fixed first because they blocked release.
W-030 (opened as W-020, renumbered because W-020 is the documentation task) then closed CR-003..CR-025:

- **Authorization (CR-004, the owner's stated policy).** Only `Administrator` may create, edit, upload,
  modify, move or delete library content — tracks, albums, artists, genres, folders, membership and
  uploads. Every other authenticated role is strictly read-only for the shared library; `Contributor` is
  kept as a role but is now read-only, so existing assignments stay valid. This is enforced with
  `Policy = "Admin"` on the server, not by hiding controls: an under-privileged write returns HTTP 403
  with a `{ error }` body. Per-user data remains writable by its owner — favourites, own playlists,
  collaborative playlists, playback history — and that exception has its own test so the restriction
  cannot be over-applied.
- **Persistence bugs that turn transient faults into permanent ones.** CR-003 (unique `(UserId, TrackId)`
  index plus a single-statement upsert, with a migration that collapses existing duplicates), CR-007 (the
  Telegram offset is persisted once per poll, including fully-duplicate batches), CR-008 (atomic
  `BackgroundJobClaimer` so two workers cannot both run a job), CR-024 (a committed file never outlives a
  failed import).
- **Concurrency and amplification.** CR-010 (quota re-asserted inside the commit transaction), CR-011
  (500-id batch bound and set-based membership writes), CR-018 (`ListenSecondsUsed` incremented in SQL),
  CR-019 (`streaming.maxConcurrentPerUser` enforced per account, so the dashboard control is real).
- **Hardening and cleanup.** CR-012 (the anonymous health probe no longer writes to the data root or
  discloses capabilities), CR-013/CR-014 (private artwork caching; album and artist artwork are live),
  CR-015..CR-017, CR-020..CR-023, CR-025 (dead files removed, `DefaultUserQuotaBytes` wired, outbox retry
  and dead-letter implemented).

Evidence: `dotnet test MusicHub.slnx` -> **211 passed, 0 failed** (152 before W-032, plus 35 new
`InstallerSecurityTests` cases; the 152 comprised 121 before W-030 plus 31 new cases, 25 in
`ReviewFindingsTests` and 6 in `IngestionAndWorkerTests`); `npm --prefix src/web/admin run
build` succeeds and its unit tests are 12/12. Ten of the 25 findings were proven by reverting the fix and
observing the new test fail — CR-003 (500), CR-004, CR-005 (401 and a 500), CR-006, CR-007, CR-008,
CR-010 (four uploads landing where one should), CR-011, CR-019 and CR-023 — and B-059, discovered while
proving CR-010, was proven the same way.

The CR-003 migration was applied to a real PostgreSQL database and the unique index was confirmed to
reject a duplicate pair. Per-finding status labels, including the eight that are
`IMPLEMENTED_NOT_TESTED`, are in R-011.

Two fixes were themselves wrong on first attempt and were caught by executing them rather than reading
them: the CR-003 dedup migration deleted the duplicate rows before aggregating, so the counted plays were
lost (6+3+5 collapsed to 6); and the first CR-010 fix only re-read the quota inside the transaction, which
under ReadCommitted still lets two concurrent imports pass on the same baseline — it needed a
`SELECT ... FOR UPDATE` on the account row, and now has a concurrent-upload test that fails without it.

Proving CR-010 also turned up a separate defect (B-059, now FIXED): the artist/genre lookups read and then
inserted, so two imports of untagged files — which all resolve to "Unknown Artist" — raced on the unique
index and the loser lost the whole upload. That is also why the first CR-010 pre-fix run looked like a
pass: it failed on the *refusal message* rather than on the bytes actually stored, and an earlier version
of this document described it wrongly. The claim was corrected after re-running the proof.

Remaining release blockers are environmental, not code: Docker runtime (B-007/W-025), a live Telegram bot
for end-to-end ingestion (B-005), an end-to-end `start-dev.ps1` run (the dev cluster's superuser
credential is unknown), and runtime dashboard/browser verification (W-014).

The Windows server install path is no longer one of them. `MusicHubServer-win-x64-1.0.0.zip` is built and
was installed from the final artifact on a clean directory, where it started PostgreSQL, the API and the
Worker (Telegram bot) by itself, served the dashboard over both loopback and the LAN address, and
registered a logon task that was then proven to bring the stack up from cold and to self-heal after both
hosts were killed (W-044 / R-025). That work also closed B-085..B-091. The package still has to be
published as a release asset, and it has not been tried on a host with no .NET runtime installed at all.

## W-032 — the four High installer findings

All four shared one shape: a check that could not tell "nothing is installed" from "installed but
unreadable", or a statement that changed one property while leaving a dangerous one alone.

- **B-040 (denied read looked like "not installed").** `TryRead` decided existence with `File.Exists`,
  which answers *false* for a file the caller may not read, so the resolver silently fell through to
  `ConnectionStrings:Default` or the development default. It now attempts the read and classifies the
  exception. `DirectoryNotFoundException` cannot classify on its own — .NET raises it both for an absent
  directory and for a file under an inaccessible parent — so the ambiguous case is resolved by
  enumerating `keys/`, and every unclear outcome becomes `Unreadable`.
- **B-041 (the Worker had its own answer).** It passed `allowDevelopmentDefault: true` while the API used
  `IsDevelopment()`, so a Production Worker with no credential connected to the development default. Both
  hosts now call `ResolveForHost(builder.Environment.EnvironmentName, ...)` and the decision exists once,
  in `AllowsDevelopmentDefault`.
- **B-042 (the generated password reached stderr).** Confirmed against a real PostgreSQL 17.6 cluster
  before changing anything: `42601` echoes the statement fragment (`at or near "'SecretProbe..."`) and
  `42704` echoes it inside an identifier, and the old `password=value` regex matched neither. Stripping
  `CONTEXT` would not have helped, because the value is in the primary message. The fix removes the value
  by identity through the new `SecretRedactor`, and never rethrows the original exception. A GUC/`DO`
  alternative was measured and rejected: PostgreSQL prints the deparsed statement in `CONTEXT`.
- **B-051 (rotating kept superuser).** The existing-role path only ran `alter role ... with login
  password ...`, so a pre-existing superuser stayed one and its credential was written anyway. The new
  `RoleProvisioning` states `login nosuperuser nocreatedb nocreaterole noreplication nobypassrls` on both
  paths, then re-reads `pg_roles` and refuses to write the credential if anything is still elevated.
  Proving it turned up a second hole: a role with `rolsuper=false` that is a **member** of a superuser role
  inherits the rights — `pg_has_role(..., 'usage')` is true while the flag stays false — so the attribute
  check alone would still have issued a working superuser credential. `AssertUnprivilegedAsync` now also
  reads `pg_auth_members`, and the installer exits 2 without writing a credential for such a role.

Live evidence, not assertion-only: on the dev cluster (where `musichub` is itself a superuser) the old
statement left `rolsuper=true` and the fixed installer produced `super=False createdb=False createrole=False
replication=False bypassrls=False`; both error shapes printed the password raw and lost it after scrubbing,
with `Include Error Detail` off and on; and Windows ACL probes produced `Unreadable` for a deny-read file,
a path occupied by a directory, and a deny-read `keys/`, while `Missing` was returned only for a genuinely
absent file or directory.

`dotnet test MusicHub.slnx` -> **211 passed, 0 failed**. With the B-040, B-041 and B-051 fixes reverted the
new filter reported **5 failed**, and passed again once restored; no `TEMP-REVERT` marker remains.
