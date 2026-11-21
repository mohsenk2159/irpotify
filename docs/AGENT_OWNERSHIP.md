# Agent Ownership

| Area | Owner agent | Owned production paths |
| --- | --- | --- |
| Orchestration and shared contracts | `music-orchestrator` | `AGENTS.md`, `MusicHub.slnx`, `opencode.jsonc`, `docs/`, `.opencode/` |
| Backend domain/API/authz | `backend-engineer` | `src/server/MusicHub.Domain/`, `src/server/MusicHub.Application/`, `src/server/MusicHub.Api/` (except Telegram + media files) |
| Media pipeline | `media-engineer` | `src/server/MusicHub.Infrastructure/Media/`, `src/server/MusicHub.Infrastructure/Options/`, storage/streaming endpoints inside `MusicHub.Api/Endpoints/StreamEndpoints.cs`, `UploadEndpoints.cs` |
| Telegram | `telegram-engineer` | `src/server/MusicHub.Infrastructure/Telegram/`, `src/server/MusicHub.Api/Endpoints/TelegramEndpoints.cs`, `src/server/MusicHub.Worker/` Telegram workers |
| Database schema/migrations | `solution-architect` (design) + `backend-engineer` (implementation) | `src/server/MusicHub.Infrastructure/MusicHubDbContext.cs`, `src/server/MusicHub.Infrastructure/Persistence/` |
| Admin dashboard | `web-dashboard-engineer` | `src/web/admin/` |
| Windows desktop player | `music-orchestrator` (interim; no dedicated desktop agent yet) | `src/desktop/MusicHub.Player/` |
| Android client | `android-engineer` | `src/android/` |
| Deployment, backups, observability | `devops-release-engineer` | `deploy/`, `Dockerfile*`, `docker-compose*.yml`, `docs/deployment/`, `docs/runbooks/` |
| Automated tests | `qa-automation-engineer` | `tests/` (except `tests/e2e/` owned by runtime QA) |
| Runtime QA | `runtime-qa-engineer` | `tests/e2e/`, `artifacts/qa/` |
| Reviews/audits | `code-review-engineer`, `security-engineer` | Read-only; report findings to orchestrator |

## Shared files (orchestrator serialization required)

- `MusicHub.slnx`
- `src/server/MusicHub.Api/Program.cs`
- `src/server/MusicHub.Api/Common.cs`, `CommonDtos.cs`
- `src/server/MusicHub.Infrastructure/MusicHubDbContext.cs`
- `docs/API_CONTRACT.md`, `docs/DATABASE_SCHEMA.md`, `docs/ARCHITECTURE.md`
- Root configuration and deploy manifests

No agent may edit another owner's path without orchestrator approval recorded in `docs/AGENT_REPORTS.md`.
