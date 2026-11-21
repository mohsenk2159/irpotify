# Library Location and Relocation

Status: implemented. Owner: music-orchestrator (server core shared with media-engineer).

## Why the data root cannot be the thing the admin edits

The server resolves its **data root** before it can talk to PostgreSQL:

- `<data root>/keys/db-credential.protected` is the database credential.
- `<data root>/keys/dataprotection/` is the keyring that unprotects it and every other
  protected value (bot token, password-reset tokens).

`Program.cs` therefore reads the data root from `MUSICHUB_DATA_ROOT`, then
`Storage:RootPath`, then a per-environment fallback, and only afterwards connects to the
database. A value stored in the database cannot influence that order, and letting an admin
rewrite the environment from the product would turn one account compromise into control of
the host.

## The split

| Root | What lives there | Who may change it |
| --- | --- | --- |
| **Data root** | `keys/`, `dataprotection/`, database credential, `backups/`, logs | The operator, through `MUSICHUB_DATA_ROOT` or `Storage:RootPath`. Shown read-only in the product. |
| **Library root** | `music/`, `artwork/`, `quarantine/` | The administrator, from the Windows player and from the Telegram bot. |

The library root defaults to the data root, so existing installations keep working with no
migration and no configuration change.

### Pointer file

`<data root>/library-location.json`:

```json
{ "version": 1, "libraryRoot": "D:\\MusicHub\\library", "updatedAt": "2026-11-19T00:00:00Z" }
```

- The data root never moves, so the pointer is always findable.
- Absent file -> library root = data root, source `default`.
- The file is written only after a relocation has been verified, by writing
  `library-location.json.tmp` and then atomically replacing the real file.
- A corrupt/unreadable pointer is reported as `pointerError` instead of silently pretending the
  library is at the data root; the store falls back to the data root so the API still boots.

Every process re-reads the pointer when its timestamp/length changes, so the API picks the new
root up without a restart once the worker has flipped the pointer. The old tree is left in
place, so requests already streaming from it keep working.

## Moving the library

`POST /api/admin/storage/relocate` queues one `storage.relocate` `BackgroundJob`; the Worker
executes it. The move is deliberately not done inside the HTTP request.

Order of operations (all of it is re-done in the worker; the queued payload is untrusted):

1. Re-validate the target with the same validator the API used. Never probe a path that already
   failed a safety check.
2. Refuse a target that is not empty unless it carries `.musichub-library.json` from an earlier
   attempt of the same move (this makes a failed move resumable without ever overwriting a
   directory the administrator pointed at by mistake).
3. Copy `music/`, `artwork/` and `quarantine/`, preserving relative paths. The copy is resumable:
   a destination file that already matches the source's length and SHA-256 is skipped.
4. Verify: every `FileHashes` row must exist under `<target>` with the recorded size, and the
   SHA-256 recomputed from the destination must equal the recorded hash. Artwork and quarantine
   files are verified source-against-destination. If verification fails, copy again (bounded) and
   re-verify; never flip the pointer on a failed verification.
5. Write the pointer atomically. The old tree is **not** deleted: the operator removes it after
   confirming the new one works. No code path deletes user files during a move.
6. Record progress and the final result in
   `AppSettings["storage.relocation.state"]`; the HTTP surface only reads it.

A move that fails leaves the pointer untouched, so the library keeps working from the old root.
`MaxAttempts` is 1: a failure is reported, not retried in the background, because a retry against
an unknown target is exactly the kind of action an admin should trigger again deliberately.

### Concurrency

Starting a move is refused while another move is pending or running, and while an upload session
is in progress. The verifier reads the database after the copy, so a track committed during the
copy either appears in the copied set or is caught by the verification pass. The UI states that
uploads should not be started during a move.

## Path safety

A candidate is rejected - with a reason for each failed check - when it is relative, contains
`..`, names a file, is a drive root, is under the Windows/System/Program Files directories, is
under the application directory, is the current library root, is the data root or inside its
`keys/` directory, or cannot be created/written. A candidate that fails an earlier check is never
probed: writing into `C:\Windows` is the action the check exists to prevent.

## API contract

### `GET /api/admin/storage/location`

```json
{
  "dataRoot": "C:\\MusicHub\\storage",
  "dataRootFrom": "environment",
  "library": {
    "path": "D:\\MusicHub\\library",
    "source": "relocated",
    "pointerError": null,
    "writable": true,
    "areas": [ { "name": "music", "path": "...", "exists": true, "files": 12, "bytes": 12345, "error": null } ],
    "disk": { "name": "D:\\", "totalBytes": 0, "freeBytes": 0 }
  },
  "assets": { "count": 12, "bytes": 12345 },
  "relocation": { "active": false, "job": null, "progress": null }
}
```

### `POST /api/admin/storage/validate`

Request `{ "path": "D:\\MusicHub\\library" }`. Response:

```json
{
  "valid": true,
  "requestedPath": "D:\\MusicHub\\library",
  "resolvedPath": "D:\\MusicHub\\library",
  "checks": [ { "id": "path.not.system", "ok": true, "message": null } ],
  "disk": { "name": "D:\\", "totalBytes": 0, "freeBytes": 0 }
}
```

### `POST /api/admin/storage/relocate`

Request `{ "path": "D:\\MusicHub\\library", "confirmation": "relocate" }`.

- `202` -> `{ "jobId": "...", "target": "...", "stateUrl": "/api/admin/storage/relocation" }`
- `400` -> invalid target, with the same `checks` array
- `409` -> a move is already queued/running, or an upload is in progress
- `403` -> not an administrator (server-side; the UI hiding the screen is not authorization)

### `GET /api/admin/storage/relocation`

```json
{
  "active": true,
  "job": { "id": "...", "status": "Processing", "attempts": 0, "createdAt": "...", "completedAt": null, "error": null },
  "progress": { "phase": "copying", "filesTotal": 12, "filesCopied": 3, "bytesTotal": 12345, "bytesCopied": 4000, "target": "...", "startedAt": "...", "error": null }
}
```

`progress.phase` is one of `copying`, `verifying`, `switching`, `completed`, `failed`.

A failure is written as `failed` with the reason, and the pointer is not touched, so the library
stays where it was. A move to a folder that already failed a safety check is refused before any
file or folder is created.

## Telegram bot

The bot is the second front end for the same feature (`src/server/MusicHub.Infrastructure/Telegram`):

- `/storage` renders the current location, the data root, per-folder usage and the last move.
- `/storage <path>` runs the **same** `StorageLocationValidator` the API runs and, only when every
  check passes, shows a Confirm button. The start button does not exist otherwise.
- Tapping Confirm calls `StorageAdminService.TryQueueRelocationAsync` - the same service the HTTP
  endpoint calls - so the one-move-at-a-time rule, the upload guard and the `MaxAttempts = 1`
  payload are not duplicated in the bot.
- Both roots are named on the screen so the data root is never mistaken for the movable library.

`StorageAdminService` exists for exactly this reason: two front ends must not be two
implementations of an authorization-adjacent decision.
