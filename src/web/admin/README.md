# MusicHub Enterprise — Admin Dashboard

React 19 + TypeScript + Vite administration and library UI for MusicHub Enterprise.
Every screen is backed by the real ASP.NET Core API; there are no mock data paths.

## Requirements

- Node.js 20+ and npm
- A running MusicHub API (default development URL `http://127.0.0.1:5080`)

## Development

```powershell
npm install
npm run dev
```

Vite serves the dashboard on `http://localhost:5173` and proxies `/api` and `/hubs`
to `http://127.0.0.1:5080` (see `vite.config.ts`). Start the API first; otherwise the
UI shows its offline error state instead of fabricated data.

## Quality gates

```powershell
npm run typecheck   # tsc --noEmit
npm run build       # typecheck + production bundle into dist/
npm run test -- --run
```

The production build is a static bundle; it contains no secrets and no server code.

## Deployment

The API does not implement SPA fallback routing, so the dashboard uses `HashRouter`
(`/#/library/tracks`). Deploy by copying the build output into the API web root:

```powershell
npm run build
Copy-Item -Recurse -Force .\dist\* ..\..\server\MusicHub.Api\wwwroot\
```

The API serves static assets from `wwwroot` and CORS is unnecessary because the
browser talks to the same origin. In development the Vite proxy handles this.

## Authentication model

- Access and refresh tokens are stored in `sessionStorage` (per-tab, cleared when the
  tab closes). This limits exposure compared with `localStorage` but is still readable
  by any script running in the page, so the deployment must not introduce untrusted
  third-party scripts and must keep TLS enabled.
- The client refreshes the access token single-flight; a failed refresh emits a session
  expiry event, clears cached queries, and returns the user to the sign-in screen.
- Authorization is enforced by the API. Hiding navigation or buttons is a usability
  measure, not a security control.

## Roles

| Role | Dashboard behaviour |
| --- | --- |
| Administrator | Full access, including users, settings, Telegram, monitoring, audit and backups (read-only). |
| Contributor | Library read/write, playlists, uploads. No administration routes. |
| Listener | Library read, playlists, sessions. No uploads or administration routes. |

## Player access

`Administration → Users` has an **Allow player application** checkbox in the create and
edit dialogs and a `PLAYER` column (`Yes`/`No`). It maps to `canUsePlayer` on
`POST /api/users` and `PATCH /api/users/{id}` and is enforced by the API at player sign-in
only: `POST /api/auth/login` with `clientType: "player"` returns HTTP 401
*“This account is not permitted to use the player application.”* when the flag is off, while
dashboard sign-in (`clientType: "dashboard"`, the default) continues to work. The flag
defaults to `true` for new accounts and existing accounts after the migration.

## Account expiry and usage limits

The same page carries the three consumption limits for regular users and listeners
(administrators are exempt):

- **Access expiry date** — `<input type="date">`; the account is deactivated at the end of the
  selected local day. “Remove expiry date” clears it.
- **Listening allowance (hours)** — converted to `listenQuotaSeconds`; `0` means unlimited.
  “Reset listening usage” sets `listenSecondsUsed` back to zero.
- **Quota (GB)** — the existing storage allowance; usage is compared against it with
  `used >= quota`.

The `USAGE` column shows `Storage: <used> / <quota>` and `Listening: <used> h / <limit>`, the
`EXPIRES` column shows the date or “No expiry”, and inactive rows carry a badge for the
deactivation reason (`expired`, `storage_quota`, `listening_quota`, `manual`). A ▶ action
reactivates an inactive account; the API refuses it while the expiry is past or a quota is still
met, so extend the date or raise/reset the allowance in the same save. All strings exist in
English and Persian (RTL).

## Honest limitations surfaced in the UI

These gaps are called out in the interface instead of being hidden behind dead controls:

- Backups are read-only in the dashboard; creation is performed by deployment tooling.
- Genres are create-only (no rename or delete endpoint).
- Album deletion is soft and there is no restore endpoint.
- There is no artist deletion endpoint.
- Track `includeDeleted` lists soft-deleted rows but the API exposes no `isDeleted` flag.
- Telegram numeric identifiers cannot be cleared; the API ignores `null` for those fields.
- Telegram ingestion is enabled through a separate `POST /api/admin/telegram/enable` call.
- The sessions API does not identify the current session, so the UI explains how to
  recognise this browser instead of guessing.
- Album list responses always return `artworkUrl: null`; artwork is available on the
  album detail endpoint.
- Track and album `PATCH` requests ignore `null` numeric fields, so years and track
  numbers cannot be cleared.

## Structure

```text
src/
  api/        typed API client, session storage, error mapping
  auth/       AuthProvider and route guards
  i18n/       English and Persian dictionaries (RTL/LTR, 580+ keys)
  pages/      one component per route
  ui/         layout, primitives, modal/toast, player, tables, icons
  App.tsx     route table
  main.tsx    provider composition
  styles.css  themes, layout, responsive and RTL rules
```
