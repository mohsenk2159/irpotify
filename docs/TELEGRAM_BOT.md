# Telegram Bot — Design and Scope

Status: Phase 0 `VERIFIED`, Phase 1 partially implemented (`IMPLEMENTED_NOT_TESTED` on a phone:
the bot has not been exercised against a real Telegram client since the report/album-editing
screens landed). This document is the contract for the bot interaction layer. It records the scope
decisions taken on 2026-11-19 and the constraints that must not be broken.

## 1. What exists today

Verified end to end on 2026-11-19 (B-005):

- `TelegramPollingWorker` long-polls and persists `telegram.updateOffset`.
- `TelegramIngestionService` accepts an audio/document message from the configured admin, downloads
  it via `getFile`, validates signature + SHA-256 + ffprobe, stores it content-addressed, creates the
  track owned by the admin user, and writes a `TelegramImports` audit row.
- Every other message is rejected with `"Message did not contain a supported audio document."`
- `TelegramPublishService` sends published tracks to a configured destination channel.

Added since, all covered by automated tests (§4, §5):

- `TelegramUpdateRouter` classifies `message` / command / `callback_query` / ignored and applies the
  admin gate exactly once before any handler runs.
- `reply_markup` inline keyboards, `answerCallbackQuery` and `editMessageText`, so screens are edited
  in place instead of accumulating menus in the chat.
- A conversation state store, a per-message album prompt store and a per-message album-edit store,
  all on `AppSettings`, all with expiry, no migration.
- Real screens: status/refresh, storage location, per-file album filing, library overview report,
  album management (add/remove tracks), album report and artist report (§5).
- Persian and English text sets with an enforced identical placeholder set.

Still missing: the Library screen (search/paginate/open/delete/restore) and the Users screen
(list/create/edit quota/ban/reset usage). Phase 2 (personal features) is untouched.

## 2. Scope decisions (2026-11-19)

| Requested | Decision |
| --- | --- |
| Admin panel with inline ("glass") buttons: add/delete track, album creation, user add/remove, quota/ban/reset | **In scope** (Phase 1) |
| Personal playlists, favourites, `file_id` caching, audio tools, lyrics, cover, force-subscribe, broadcast, i18n | **In scope** (Phase 2) |
| Download from YouTube / Spotify / SoundCloud / Deezer / Instagram | **Removed from scope.** Contradicts `AGENTS.md` ("independent of Spotify and third-party music catalogues") and the ToS of those services |
| Voice-chat streaming | **Removed from scope** (needs MTProto + tgcalls; not possible on this stack) |
| Shazam-style recognition | Not in scope for now (would need `fpcalc` + AcoustID, a new external dependency) |
| Inline mode | Deferred; needs a webhook and public HTTPS to be usable |

The mission statement in `AGENTS.md` stays unchanged. No module in this repository may download
audio from a third-party catalogue.

## 3. Architecture

The bot is a **presentation layer**, not a second implementation of the domain.

```
Telegram getUpdates
        |
        v
TelegramPollingWorker            (unchanged outer loop, existing offset handling)
        |
        v
TelegramUpdateRouter
   |         |          |
   |         |          +--> inline_query        -> ignored (no handler exists)
   |         +-------------> callback_query      -> TelegramBotHandler (admin-gated)
   +-----------------------> message
                               |-- command (/start, /status, /reports, /albums, /artists, /help, /cancel)
                               |                          -> TelegramBotHandler (admin-gated)
                               +-- audio/document        -> TelegramIngestionService (unchanged)
                               +-- typed album name      -> album prompt store (when one is awaiting)
                               +-- anything else         -> rejection (unchanged)
```

Hard rules:

1. **Existing ingestion behaviour is frozen.** Audio/document handling, the rejection text and the
   offset semantics must not change. Phase 0 is additive only; the only audit rows added since are
   ones for the library changes the admin buttons themselves perform (album create/assign, album
   remove, album add).
2. **Authorization is the server's decision, never the keyboard's.** Every callback and every
   command re-reads `config.AdminTelegramUserId` (and `AdminChatId` when set) from the database and
   compares it with the update's `from.id`. `callback_data` is treated as untrusted input: it may
   name a target id, but the authorization check happens before that id is used, and the target is
   re-loaded from the database, never trusted as a payload claim.
3. **Destructive actions need a second confirmation tap.** Delete track, delete user, reset quota and
   ban are two-step (the album track flows use `v1:amrm:` for the ask and `v1:amrmdo:<albumId>` for the
   confirm); the confirmation expires with the session.
4. **Every state-changing bot action writes an audit row** with the Telegram sender as the actor. The
   bot writes through `TelegramAudit` (Infrastructure) because there is no user-account principal
   behind a Telegram callback; the actor is therefore recorded in `AuditLog.DetailsJson` under the
   `actor: "telegram"` marker alongside the numeric Telegram user id, and `ActorUserId` stays null
   so dashboard rows (which fill it from the claims principal) can be told apart.
5. **`answerCallbackQuery` is always called**, including on the error path, or the client shows a
   spinner until it times out.

### Conversation state

Phase 0 stores state in the existing `AppSettings` key/value table under
`telegram.bot.state.<chatId>`, holding `{ "state": "...", "payload": { ... }, "updatedAt": "..." }`.
Rationale: the bot is admin-only, so this is a handful of rows, and it avoids a schema migration that
would cross ownership boundaries (schema is `solution-architect` + `backend-engineer`). A dedicated
table is the right move once customer-facing features (Phase 2) arrive; that is a deliberate
follow-up, not an oversight.

Sessions expire after 15 minutes of inactivity and are deleted on `/cancel`.

### Keyboard conventions

- `callback_data` is compact and versioned: `v1:<action>[:<arg>]`, max 64 bytes.
- Every screen is a single message that is edited in place (`editMessageText`), so the chat does not
  fill with dead menus.
- Every screen carries a `Back` and a `Home` button.
- Pagination keeps its screen's action and puts the page in the argument, e.g. `v1:amalbs:2` for the
  album list and `v1:ampgt:<albumId>:2` for an album's tracks, so the album identity survives paging.
  Lists render 8 rows per page and only the directions that exist — a "next" on the last page is
  never built.
- Screens that need more state than a button can carry use a per-message store keyed by
  `<chatId>.<messageId>` on `AppSettings` (album prompts, album edit rows).

## 4. Phase 0 — foundation (W-035)

Deliverables, all inside `telegram-engineer` ownership:

- `TelegramUpdateRouter` that classifies an update and dispatches; audio/document still goes to
  `TelegramIngestionService`.
- `TelegramBotClient` additions: `AnswerCallbackQueryAsync`, `EditMessageTextAsync`,
  `SendMessageAsync` overload with `reply_markup`, `SendChatActionAsync`.
- `TelegramKeyboard` builders (inline rows, confirm/cancel, pagination).
- `TelegramBotSession` store on `AppSettings` with expiry.
- Admin gate applied once, before any handler runs; non-admin gets one fixed denial reply and no
  state is written.
- `TelegramBotStateStore` plus tests: routing table, keyboard encoding/decoding including the 64-byte
  limit, session expiry, non-admin denial, and the guarantee that a document still reaches the
  importer unchanged.
- No schema migration, no new NuGet package.

Explicitly **not** in Phase 0: any admin action handler. The menu shell exists, the buttons say what
they will do only once the handler behind them is real — no dead controls.

## 5. Phase 1 — admin panel (W-036)

Screens: Library (search/paginate, open track, delete with confirmation, restore), Albums (create,
add/remove tracks), Users (list, create, open, edit quota, ban/unban, reset usage), Status
(worker heartbeat, import counts, queue depth).

Open decision **D-1 — how the bot reaches the domain**: **resolved as option B**.

| Option | Cost | Risk |
| --- | --- | --- |
| A. Bot calls the public API over loopback with a dedicated service identity | needs a token/credential mechanism for a non-human admin | admin credential lives in the Worker process |
| B. Extract the write operations into application services used by both the HTTP endpoints and the bot | real refactor across `MusicHub.Application`/`MusicHub.Api` (backend-engineer ownership) | touches working code |
| C. Bot performs EF operations directly | smallest diff | **rejected**: duplicates CR-004 authorization rules and will diverge |

Option **B** is implemented for the first Phase 1 feature. `AlbumService`
(`src/server/MusicHub.Infrastructure/Library/AlbumService.cs`) is now the single implementation of
"create an album" and "put a track in an album". `POST /api/albums` and
`POST /api/albums/{id}/tracks` in `LibraryEndpoints` call it, and so does the bot. The service is
authorization-agnostic on purpose: the endpoints authorize with the `Admin` policy, the bot with
`TelegramBotAdminGate`, and audit rows and realtime notifications stay with the caller because they
are transport concerns.

### 5.1 Per-file album filing (implemented)

After a successful import the bot asks, as its own message, **"which album should this track be filed
under?"** and offers: an existing album (paged picker), *create a new album*, or *no album*.

The requirement is that this happens **separately for each file**. The chat session
(`TelegramBotStateStore`) holds exactly one state per chat, which would mean answering the second
file silently retargeted the first file's buttons. The fix is a separate keyed store,
`TelegramAlbumPromptStore`, which keys each pending question by **the id of the bot message that asks
it** under `telegram.bot.album.<chatId>.<messageId>` in `AppSettings`. A callback arrives with the id
of the message it was tapped on, so the question it belongs to is unambiguous no matter how many are
open. Rows are deleted when answered, skipped, or expired (15 minutes, matching the session lifetime),
and no migration is needed because `AppSettings` already exists.

Behaviour notes:

- A typed album name is intercepted in `TelegramIngestionService.TryHandleAlbumNameAsync` **before**
  the frozen audio-only rejection, but only while a prompt is `AwaitingName`. Commands are excluded,
  so `/status` is never stored as an album name.
- Typing a name that already exists reuses that album (case-insensitive `NormalizedTitle`) instead of
  creating a near-duplicate.
- `TelegramBotClient.SendMessageReturningIdAsync` exists because the older `SendMessageAsync` does not
  return the message id the prompt store requires. A zero id is treated as "no question was asked",
  and the operator still gets the plain acceptance reply rather than an unanswerable prompt.
- The picker's *next*/*previous* buttons only render directions that exist, and `AlbumService.ListAsync`
  takes a `skip` so page 2 shows the next page instead of repeating page 1.

### 5.2 Reports, album editing and the artist report (implemented)

The panel is now a real menu (three rows: reports / albums, artists / storage, refresh) and every
button leads to a screen that exists. Three commands open the same screens directly:
`/reports`, `/albums`, `/artists`.

**Library overview** (`/reports`, `v1:reports`) shows albums, tracks, artists, users, active users,
inactive users and the total playing time. Every number is read from the database on the same
render, so the refresh button genuinely re-reads state.

| Value | Source |
| --- | --- |
| Albums | `Albums` where `not IsDeleted` |
| Tracks | `Tracks` where `not IsDeleted` |
| Artists | artists credited on at least one live track (same set the artist report lists) |
| Users | `Users` row count |
| Active / inactive users | `User.IsActive` |
| Total music time | sum of `Track.DurationSeconds` over live tracks |

`User.IsActive` is not an approximation: `UserEntitlementService` is the component that clears it
when an access period, storage allowance or listening allowance runs out, so the report shows
exactly the state the server enforces. `LibraryReportService`
(`src/server/MusicHub.Infrastructure/Library/LibraryReportService.cs`) owns these aggregates instead
of the handler, for the same reason D-1 put album writes in `AlbumService`.

**Album editing** (`/albums`, `v1:amalbs:<page>`) lists albums newest first with their live track
count. Opening one (`v1:amopen:<albumId>`) shows the album's report — album artist, year, live track
count, duration, per-artist share — and one `❌ <track>` button per track on the page.

- Removing a track is **two taps**: the first tap (`v1:amrm:<albumId>:<row>`) renders a confirmation
  naming the track and the album, and only the confirmation (`v1:amrmdo:<albumId>`) writes.
- What is removed is the **album membership only**. The `Track` row, the `AudioAsset` and the stored
  blob all survive, and `AlbumDisplay` is cleared when this was the track's last album and it still
  pointed at this one.
- `Add track` (`v1:amaddlist:<albumId>:<page>`) lists live tracks that are not in the album yet,
  newest first, paged. Adding (`v1:amadd:<albumId>:<row>`) re-renders the same shortlist so several
  tracks can be added without walking back into the album.
- `AlbumService.RemoveTrackAsync` is shared with `DELETE /api/albums/{id}/tracks/{trackId}`, so both
  transports apply the same display-clearing rule instead of one of them forgetting it.

Every write this feature performs through the bot is followed by an audit row via
`TelegramAudit.RecordAsync` (`src/server/MusicHub.Infrastructure/Telegram/TelegramAudit.cs`) with
actions `album.create`, `album.track.assign`, `album.track.remove` and `album.track.add`; the
callback-based flows record the Telegram sender and chat id in the details JSON.

**The 64-byte callback problem.** `callback_data` is capped at 64 bytes and a versioned `v1:` action
plus a 32-character album id leaves no room for a second 32-character track id. Album screens
therefore carry the album id plus the **row index**, and `TelegramAlbumEditStore` keeps the
row-to-track-id mapping for each rendered message under
`telegram.bot.albumedit.<chatId>.<messageId>` in `AppSettings` (15-minute lifetime, no migration).
It is a separate store from `TelegramBotStateStore` for the same reason `TelegramAlbumPromptStore`
is: several album screens can be open at once, and a callback arrives with the id of the message it
was tapped on. The mapping is rewritten on every render, so a tap can never resolve to a track from
an older page — and an index outside the stored mapping rebuilds the screen and removes nothing.

**Artist report** (`/artists`, `v1:artists:<page>`) lists artists with at least one live track,
busiest first. Opening one (`v1:artopen:<artistId>`) shows track count, album count, total duration
and the albums the artist appears on, each of which opens the album screen above. Credits come from
`TrackArtist`, which is the authoritative credit; a track's `ArtistDisplay` string is only a display
hint and is never counted.

Complete `callback_data` vocabulary added by this feature:

| Payload | Meaning |
| --- | --- |
| `v1:reports` | library overview (also the refresh) |
| `v1:amalbs:<page>` | album management list |
| `v1:amopen:<albumId>` | one album's report and track list |
| `v1:ampgt:<albumId>:<page>` | album track page |
| `v1:amrm:<albumId>:<row>` | ask to remove a track |
| `v1:amrmdo:<albumId>` | confirm that removal |
| `v1:amaddlist:<albumId>:<page>` | tracks that could be added |
| `v1:amadd:<albumId>:<row>` | add that track |
| `v1:artists:<page>` | artist report list |
| `v1:artopen:<artistId>` | one artist's report |

Tests: `tests/server/MusicHub.Tests/Integration/TelegramReportAndAlbumEditingTests.cs` (8 cases,
overview counts read back from the database, artist list to detail, album list to detail, page 2
differs from page 1, removal needs the confirmation and keeps the audio, add-from-bot, stale index
removes nothing, HTTP delete clears `AlbumDisplay`), plus unit tests for the button encoding (≤ 64
bytes), the absence of dead paging buttons, long-title clipping, `FormatDuration`, the overview text
and the album report text.

Still open in Phase 1: the Library screen (search/paginate/open/delete with confirmation/restore)
and the Users screen (list, create, edit quota, ban/unban, reset usage).

## 6. Phase 2 — personal features

File-id cache (`file_id` is only reusable by the bot that sent the file, so cache per bot plus track
plus format), personal playlists and favourites (already modelled), audio tools through the existing
ffmpeg dependency (trim, speed, pitch, 8D, format conversion), force-subscribe via `getChatMember`,
rate-limited broadcast, per-user language and default quality in user preferences.

Sending audio above the Bot API's 50 MB limit requires the local Bot API server; `LocalBotApiEndpoint`
is already threaded through both the ingestion and publish services, so this is a deployment task.

## 7. Threat notes

- The bot is a remote admin surface. Anyone who can message the bot reaches the authorization gate;
  the gate must be the first thing that runs, and its denial must not leak whether a given chat id is
  configured.
- `callback_data` arrives from the client and can be replayed by the user who owns the message.
  Stale confirmations must be rejected by comparing the session generation, not just the id.
- Buttons must never encode a token, a password or a file path.
- A deleted track is soft-deleted (`IsDeleted`), so the storage file must not be removed by a bot
  action; only the existing integrity/maintenance path may delete blobs.
- Bot actions must appear in the audit log with the Telegram user id so an operator can tell a bot
  action from a dashboard action.
