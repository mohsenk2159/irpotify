# MusicHub Enterprise — Project Instructions

## Start Here

Every session MUST begin by reading:

1. `docs/PROJECT_STATUS.md`
2. `docs/WORK_QUEUE.md`
3. `docs/BUG_TRACKER.md`
4. `docs/AGENT_OWNERSHIP.md`
5. `docs/AGENT_OPERATING_PROTOCOL.md`
6. `docs/PROJECT_MAP.md` — file index; use it to locate code instead of scanning the tree

Do not assume a task succeeded because it was started. Verify current files, builds, tests, and runtime state before continuing.

## Mission

Build and operate MusicHub Enterprise: a secure, self-hosted, multi-user music library, streaming server, admin dashboard, Android client, and Telegram ingestion/publishing module. The product is independent of Spotify and third-party music catalogues.

## Non-negotiable Engineering Rules

- Real implementations only. No mock production paths, fabricated metrics, or dead controls.
- Verify with commands and evidence. Never claim a feature works without a passing test or runtime observation.
- Preserve existing user work. Inspect before editing or deleting.
- Never commit or log secrets, passwords, access tokens, authorization headers, or full Telegram bot tokens.
- Enforce authorization on the server. UI hiding is not authorization.
- Keep uploaded music outside source and disposable container layers.
- Never execute uploaded content or extract arbitrary archives.
- Never weaken TLS validation to make a connection work.
- Keep changes within assigned ownership. Escalate shared-file changes to the orchestrator.
- A failed test may not be deleted or skipped to obtain a green build.
- Critical bugs and unresolved high-severity bugs block release.

## Architecture Snapshot

- Backend: .NET 10, ASP.NET Core Minimal API, EF Core, PostgreSQL, JWT, SignalR.
- Dashboard: React + TypeScript + Vite.
- Windows player: WPF `net10.0-windows` + LibVLCSharp, DPAPI session storage.
- Android: Kotlin + Jetpack Compose + Media3 + Room + WorkManager.
- Telegram: first-class server module, durable outbox and background worker.
- Storage: content-addressed local filesystem under a configurable data root.
- Deployment: native Windows and Docker Compose using identical application binaries/configuration.

See `docs/ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, and `docs/API_CONTRACT.md` for current details.

## Repository Ownership

See `docs/AGENT_OWNERSHIP.md`. Do not edit another agent's owned production paths without orchestrator approval.

## Standard Commands

Set the local portable SDK path when needed:

```powershell
$env:PATH="$env:PATH;$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet build MusicHub.slnx
dotnet test MusicHub.slnx
```

```powershell
npm --prefix src/web/admin install
npm --prefix src/web/admin run build
npm --prefix src/web/admin run test
```

```powershell
.\deploy\scripts\start-dev.ps1
.\deploy\scripts\verify.ps1
```

## Evidence and Reporting

Every delegated task must return the report format in `docs/AGENT_OPERATING_PROTOCOL.md`, including commands executed and actual results. The orchestrator records accepted reports in `docs/AGENT_REPORTS.md` and updates the work queue and bug tracker.

## Truthful Status Labels

Use exactly one of: `VERIFIED`, `IMPLEMENTED_NOT_TESTED`, `BLOCKED`, `NOT_IMPLEMENTED`. Evidence is mandatory for `VERIFIED`.

## Safety

Ask the user before destructive, irreversible, or high-impact actions (production restore, data deletion, credential use, public exposure, git push, service installation). Ordinary local development decisions do not require approval.
