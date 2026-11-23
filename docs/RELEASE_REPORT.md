# MusicHub Enterprise — Release Report (Phase 12)

**Date:** 2026-11-21
**Version:** 1.0.0
**Status:** `IMPLEMENTED_NOT_TESTED` for publication · `VERIFIED` for build, tests and container runtime

---

## 1. Truthful status summary

| Area | Status | Evidence |
| --- | --- | --- |
| Server build | `VERIFIED` | `dotnet test MusicHub.slnx` → 372 passed, 0 failed |
| Dashboard build + tests | `VERIFIED` | `npm run test -- --run` → 12 passed |
| Dashboard served from image | `VERIFIED` | `/index.html` 200 (951 B), asset bundle 200 (525,322 B) |
| Docker API image | `VERIFIED` | `musichub/api:verify` built, `linux/amd64`, user `1654:1654` |
| Docker worker image | `VERIFIED` | `musichub/worker:verify` built, healthcheck `pgrep` present |
| Container startup | `VERIFIED` | 4 containers healthy; `/healthz` 200 `Healthy` |
| Port exposure | `VERIFIED` | API only on `192.168.10.20:8090`; worker + postgres publish nothing |
| Persistent storage | `VERIFIED` | Sentinel survived `restart api worker` |
| LAN overlay fail-closed | `VERIFIED` | `config` exits 1 with `required variable MUSICHUB_LAN_ADDRESS is missing` |
| GHCR pull path | `VERIFIED` | Local `1.0.0` tags reused by exact image ID, no rebuild |
| GitHub Actions YAML | `VERIFIED` (syntax) | Both parse; all `needs:` targets resolve |
| Release publishing | `NOT_IMPLEMENTED` | No remote configured; zero commits |
| Android Play Store listing | `BLOCKED` | Needs operator account, payment, login |
| Telegram bot live traffic | `BLOCKED` | Real token must be provisioned; leaked token must be revoked |

---

## 2. Architecture

```
                        ┌──────────────────────────────┐
   internet / LAN  ───► │  caddy :80/:443 (TLS, ACME)  │
                        └──────────────┬───────────────┘
                                       │ edge network 172.28.0.0/24
                        ┌──────────────▼───────────────┐
                        │  api  :5080 (no host publish)│──┐
                        │  read_only, cap_drop ALL     │  │
                        └──────────────┬───────────────┘  │
                                       │ backend network  │  musichub_data
                        ┌──────────────▼───────────────┐  │  (music, artwork,
                        │  worker  (no port, no JWT)   │──┤   quarantine,
                        │  Telegram polling + outbox   │  │   DataProtection keys)
                        └──────────────┬───────────────┘  │
                        ┌──────────────▼───────────────┐  │
                        │  postgres :5432 (private)    │  │
                        └──────────────────────────────┘  │
                                                          │
   Optional LAN overlay: api additionally published on ───┘
   ${MUSICHUB_LAN_ADDRESS}:${MUSICHUB_LAN_PORT} → 5080
```

**Repositories of record**

| Component | Path | Runtime |
| --- | --- | --- |
| Backend | `src/server/MusicHub.Api` | .NET 10, ASP.NET Core Minimal API |
| Worker | `src/server/MusicHub.Worker` | .NET 10, no HTTP listener |
| Dashboard | `src/web/admin` | React + TS + Vite, baked into API `wwwroot` |
| Windows player | `src/desktop/MusicHub.Player` | WPF `net10.0-windows` + LibVLCSharp |
| Android | `src/android` | Kotlin + Compose + Media3 + Room |

---

## 3. Docker image configuration

| Property | API | Worker |
| --- | --- | --- |
| Dockerfile | `deploy/docker/Dockerfile.api` | `deploy/docker/Dockerfile.worker` |
| Base | `mcr.microsoft.com/dotnet/aspnet:10.0.9-noble` | same |
| Build SDK | `mcr.microsoft.com/dotnet/sdk:10.0.404-noble` | same |
| Dashboard stage | `node:22-alpine` → `COPY --from=web /web/dist/` into `wwwroot` | — |
| Arch built locally | `linux/amd64` | `linux/amd64` |
| Arch in CI | `linux/amd64`, `linux/arm64` | `linux/amd64`, `linux/arm64` |
| User | `1654:1654` (non-root) | `1654:1654` |
| Healthcheck | `GET /healthz` via curl | `pgrep -f MusicHub.Worker.dll` |
| Root FS | read-only + tmpfs `/tmp` | read-only + tmpfs `/tmp` |
| Capabilities | `cap_drop: ALL` | `cap_drop: ALL` |
| Ports | `expose 5080`, never published in base file | none |

**Why the dashboard lives in the image:** a pulled GHCR image must serve the UI without the host
having Node or a pre-built `dist/`. `REQUIRE_DASHBOARD=true` makes the build fail if the dashboard
was silently skipped.

---

## 4. Files created or modified in this phase

**Created**

| File | Purpose |
| --- | --- |
| `docs/ICON_ASSET_INVENTORY.md` | 101 coded `MH-*` assets with ready-to-send prompts |
| `docs/RELEASE_REPORT.md` | This report |

**Modified**

| File | Change |
| --- | --- |
| `src/server/MusicHub.Install/AdminPasswordReset.cs` | New: offline administrator password recovery (B-083) |
| `src/server/MusicHub.Install/Program.cs` | New `reset-admin-password` verb; honours `MUSICHUB_CONNECTION_STRING`; usage text |
| `src/server/MusicHub.Api/Endpoints/AuthEndpoints.cs` | Deactivation branch now refuses to remove the last active administrator (B-084) |
| `tests/server/MusicHub.Tests/Unit/AdminPasswordResetTests.cs` | New: 9 tests pinning the SQL contract, the no-argument/no-echo rule, and the published Docker recipe |
| `tests/server/MusicHub.Tests/Integration/ReviewFindingsTests.cs` | Two tests for the deactivation guard |
| `docs/INSTALL_AND_RUN.md` | "Recovering a forgotten administrator password" section, including the Docker form |
| `docs/PROJECT_MAP.md` | Registered `AdminPasswordReset.cs`; corrected the stale admin-login note |
| `docs/BUG_TRACKER.md` | B-083, B-084 |
| `deploy/docker/install.ps1` | Rewrites the template `MUSICHUB_PUBLIC_HOSTNAME` to `localhost` and warns, so Caddy never requests a certificate for an unowned domain |
| `deploy/docker/README.md` | Added the "always pass the same `-f` files" warning + `mh` helper, discovered by runtime test |
| `.env.example` | Documented the hostname rewrite next to `MUSICHUB_PUBLIC_HOSTNAME` |
| `docs/ICON_ASSET_INVENTORY.md` | 101 coded `MH-*` assets |
| `docs/RELEASE_REPORT.md` | This report |

Earlier phases created: `deploy/scripts/network.ps1`, `deploy/docker/install.ps1`,
`deploy/docker/docker-compose.lan.yml`, `.github/workflows/ci.yml`,
`.github/workflows/release.yml`, `README.md`, `README.fa.md`, `SECURITY.md`, `LICENSE`,
`.editorconfig`, `.gitattributes`, `.dockerignore`, `tests/.../InstallEndpointTests.cs`.

---

## 5. Commands executed (exact) and observed results

```powershell
# 1. Server tests - 372 passed
dotnet test MusicHub.slnx --nologo
# → Passed! - Failed: 0, Passed: 372, Skipped: 0, Total: 372, Duration: 6 m 31 s

# 2. Dashboard tests — 12 passed
npm --prefix src/web/admin run test -- --run
# → Test Files 1 passed (1) · Tests 12 passed (12)

# 3. API image build
docker build -f deploy/docker/Dockerfile.api --build-arg REQUIRE_DASHBOARD=true -t musichub/api:verify .
# → DONE; /app/wwwroot/index.html = 951 B, assets/ present

# 4. Worker image build
docker build -f deploy/docker/Dockerfile.worker -t musichub/worker:verify .
# → DONE; healthcheck present, /var/lib/musichub owned by app

# 5. Image metadata
docker image inspect musichub/api:verify --format 'arch={{.Architecture}} user={{.Config.User}}'
# → arch=amd64 user=1654:1654

# 6. Compose validation (base only)
docker compose --env-file .env -f deploy/docker/docker-compose.yml config --quiet
# → exit 0

# 7. LAN overlay must fail closed
docker compose --env-file .env -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.lan.yml config --quiet
# → exit 1: required variable MUSICHUB_LAN_ADDRESS is missing a value

# 8. LAN overlay with values
docker compose --env-file .env -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.lan.yml config --quiet
# → exit 0

# 9. Runtime on GHCR-equivalent tags
docker tag musichub/api:verify musichub/api:1.0.0
docker tag musichub/worker:verify musichub/worker:1.0.0
docker compose --env-file .env -f ...yml -f ...lan.yml up -d
# → recreated; running image ID == tagged image ID (no rebuild)

# 10. Endpoint sweep
curl.exe -s -o NUL -w "%{http_code}" http://192.168.10.20:8090/healthz      # 200
curl.exe -s -o NUL -w "%{http_code}" http://192.168.10.20:8090/             # 302 → /index.html
curl.exe -s -o NUL -w "%{http_code}" http://192.168.10.20:8090/index.html   # 200 (951 B)
curl.exe -s -o NUL -w "%{http_code}" http://192.168.10.20:8090/api/version  # 200
curl.exe -s -o NUL -w "%{http_code}" http://192.168.10.20:8090/api/health   # 200
curl.exe -s -o NUL -w "%{http_code}" http://192.168.10.20:8090/api/users    # 401

# 11. Persistence across restart
docker run --rm -v musichub_data:/d alpine sh -c 'echo persistence-probe-2026 > /d/.persistence-probe'
docker compose ... restart api worker
docker run --rm -v musichub_data:/d:ro alpine sh -c 'cat /d/.persistence-probe'
# → persistence-probe-2026

# 12. YAML validation
python -m pip install pyyaml && python yamlcheck2.py
# → ci.yml parsed OK, release.yml parsed OK, ALL OK

# 13. .env writer isolation test
powershell -NoProfile -ExecutionPolicy Bypass -File envwriter-test.ps1
# → in-place replace, order preserved, no duplicates, append works, '=' in value safe

# 14. TLS hostname guard (install.ps1) — 5 behaviour cases
powershell -NoProfile -ExecutionPolicy Bypass -File hostname-branch-test.ps1
# → Case 1 template → localhost + 2 warnings · Case 2 -PublicHostname preserved
#   Case 3 already localhost untouched · Case 4 real domain untouched · Case 5 key absent
# → ALL CASES PASS

# 15. Regression test for the guard
dotnet test MusicHub.slnx --filter "FullyQualifiedName~InstallEndpointTests"
# → Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7

# 16. Administrator password recovery, end to end against the live Docker stack
#     (run inside the compose network because the database is not published to the host)
#     .env has no MUSICHUB_CONNECTION_STRING key - compose builds it from POSTGRES_* - so the
#     running API container is the source of truth for the string the stack already uses.
dotnet build src/server/MusicHub.Install
$env:MUSICHUB_CONNECTION_STRING   = (docker exec musichub-api-1 printenv MUSICHUB_CONNECTION_STRING).Trim()
$env:MUSICHUB_NEW_ADMIN_PASSWORD  = 'a-long-new-password-here'
docker run --rm --network musichub_backend -v "${PWD}:/repo:ro" `
  -e MUSICHUB_CONNECTION_STRING -e MUSICHUB_NEW_ADMIN_PASSWORD `
  mcr.microsoft.com/dotnet/aspnet:10.0.9-noble `
  sh -c 'exec dotnet /repo/src/server/MusicHub.Install/bin/Debug/net10.0/MusicHub.Install.dll reset-admin-password --email admin@musichub.local'
Remove-Item Env:MUSICHUB_CONNECTION_STRING, Env:MUSICHUB_NEW_ADMIN_PASSWORD
# → Password replaced for 'admin@musichub.local'.
#   Sessions invalidated: 2 refresh token(s), 2 device session(s).
#   exit=0
#   stamp 2ed817a5... -> 8cc3ab89...   previous password 401   new password 200
#   LockoutEnd -> null   live tokens 1 -> 0   live sessions 1 -> 0
#   audit: admin.password.reset | actor=NULL | {"via":"install-cli","email":"admin@musichub.local"}
#   Re-run verbatim from docs/INSTALL_AND_RUN.md to prove the published instructions work.

# 17. Recovery command guards
# → password as argument  -> exit 1 ("Passwords are not accepted as arguments")
#   no password supplied  -> exit 1
#   8-character password  -> exit 2 ("must be at least 12 characters")
#   missing --email       -> exit 1
```

`/api/health` body: `{"status":"ok","version":"1.0.0","checks":{"database":true,"storageReadable":true}}`

---

## 6. Security audit result

**No critical secret blocks publication.**

| Check | Result |
| --- | --- |
| Commits in history | 0 → nothing to scrub |
| `.opencode/`, `.agents/` | gitignored; trackable count 333 → 315 |
| Real secrets in tracked files | none (all 4 verified absent) |
| PII in docs | sanitized (`<USERNAME>`, `<HOSTNAME>`, `<SANDBOX-GROUP>`, `<MACHINE-SID>`, `192.168.1.x`) |
| TLS validation | never weakened anywhere |
| Authorization | server-side; `/api/users` → 401 unauthenticated (verified) |

**Residual, Low:** `docs/AGENT_REPORTS.md` and `docs/BUG_TRACKER.md` contain the dev-default
`Password=musichub` inside log excerpts. It is the local dev default, not a production credential.

---

## 7. Defects found during this phase

| ID | Severity | Finding | Status |
| --- | --- | --- | --- |
| R-01 | Medium | Recreating the stack **without** the LAN overlay silently drops the port mapping. Containers stay `healthy`, `/healthz` is unreachable from the host, and the dashboard disappears from the LAN. Root cause: Compose treats the `-f` file set as part of the project definition. | Fixed in `deploy/docker/README.md` with a warning and an `mh` helper function |
| R-02 | Medium | A default install left `MUSICHUB_PUBLIC_HOSTNAME=music.example.com` in `.env`. The installer already treated the template *public URL* as unconfigured but not the template *hostname*, so Caddy kept attempting ACME for a domain the operator does not own and failed repeatedly. LAN access was unaffected (it bypasses Caddy), so the failure was only visible in the Caddy logs. | Fixed in `deploy/docker/install.ps1`: the template hostname is rewritten to `localhost` (Caddy's local CA) with an explicit warning and the `-PublicHostname` hint. Covered by a new regression test; 5 behaviour cases verified |
| B-083 | High | A forgotten administrator password had no supported recovery path: the hash cannot be reversed, every route that could replace it needs a session the operator lost, and `/api/setup/initialize` returns 409 once a user exists. `MUSICHUB_ADMIN_PASSWORD` in `storage/dev.secrets.ps1` is written but read by no server project, so it is not the live password (a real login returns 401). | Fixed: `MusicHub.Install reset-admin-password` (offline CLI). Verified end to end: stamp rotated, stale password 401, new password 200, lockout cleared, 3 tokens and 3 sessions revoked, audit row written |
| B-084 | High | The last-administrator guard covered demotion but not deactivation, so `{"isActive": false}` on the only active administrator left the instance with nobody who could sign in. | Fixed in `AuthEndpoints.cs` with a 409 on the deactivation branch; two tests added |

Reproduction: start with both files, then run
`docker compose -f deploy/docker/docker-compose.yml restart api` — the container is recreated from
the base file only and `HostConfig.PortBindings` becomes `{}`.

---

## 8. Remaining manual steps

### 8.1 Git — initialise, remote, push, tag

```powershell
cd "C:\Users\MohsenKarami\Documents\Default Project\irmusic"

git init -b main
git add -A
git status --short          # review: no .env, no storage/, no artifacts/, no keystore
git commit -m "MusicHub Enterprise 1.0.0"
```

Verify nothing secret is staged before the first push:

```powershell
git ls-files | Select-String -Pattern '\.env$|keystore|\.jks$|secrets\.ps1|/storage/'
# expected: no output
```

Create the empty repository on GitHub (**do not** add a README or licence there), then:

```powershell
git remote add origin https://github.com/<owner>/<repo>.git
git push -u origin main
```

Tag and release:

```powershell
git tag -a v1.0.0 -m "MusicHub Enterprise 1.0.0"
git push origin v1.0.0
```

Pushing the tag triggers `.github/workflows/release.yml` automatically.

### 8.2 Required GitHub repository settings

| Setting | Value | Why |
| --- | --- | --- |
| Actions → General → Workflow permissions | Read and write | `GITHUB_TOKEN` must push to GHCR and create the release |
| Packages visibility | Public | So `docker pull` works without `docker login` |
| Secrets → `ANDROID_KEYSTORE_BASE64` | base64 of the release keystore | APK signing |
| Secrets → `ANDROID_KEYSTORE_PASSWORD` | keystore password | APK signing |
| Secrets → `ANDROID_KEY_ALIAS` | key alias | APK signing |
| Secrets → `ANDROID_KEY_PASSWORD` | key password | APK signing |

If the Android secrets are absent, run the workflow manually with `skip_android: true`.

### 8.3 Publish the release assets

The install artifacts are gitignored by design and must be attached to the GitHub Release:

| File | Size | Contents |
| --- | --- | --- |
| `artifacts/installers/MusicHub-Android-1.0.0-release.apk` | 13.17 MB | Android client (minified, signed) |
| `artifacts/installers/MusicHubPlayer-win-x64.zip` | 104.89 MB | WPF player (self-contained win-x64) |
| `artifacts/installers/MusicHubServer-win-x64-1.0.0.zip` | 181.7 MB | **Server for Windows package (Host)**: API + Worker + dashboard + private .NET runtime + trimmed portable PostgreSQL 17 + ffmpeg/ffprobe |

The server package is self-contained: no .NET SDK, no Docker and no network connection are needed at
install time. Install it with:

```powershell
# From the extracted MusicHubServer-win-x64-1.0.0\ folder:
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1
```

Build it with `.\deploy\windows\build-package.ps1` (add `-Version`, `-OutputDirectory`,
`-KeepStaging`, `-SkipWebBuild` as needed). The script writes the `.zip` and a `.sha256` beside it.

```powershell
gh release upload v1.0.0 `
  artifacts/installers/MusicHub-Android-1.0.0-release.apk `
  artifacts/installers/MusicHubPlayer-win-x64.zip `
  artifacts/installers/MusicHubServer-win-x64-1.0.0.zip `
  artifacts/installers/MusicHubServer-win-x64-1.0.0.zip.sha256 --clobber
```

### 8.4 After the first release, verify the public paths

```powershell
# Anonymous pull must work (packages set to public)
docker pull ghcr.io/<owner>/musichub-api:1.0.0
docker pull ghcr.io/<owner>/musichub-worker:1.0.0
```

### 8.5 Telegram

1. Open BotFather → `/revoke` for the bot whose token leaked. The old token is compromised.
2. `/token` to obtain a fresh one. Do **not** paste it into chat or a file in the repository.
3. Dashboard → Telegram → paste the token, set admin user id `920615095`, save.
4. Test `/reports`, `/albums`, `/artists` from that Telegram account.

### 8.6 Android Play Store (optional)

Requires an operator Google account, a one-time registration fee, and a signed AAB. Not required
for the GitHub-release APK path, which is already functional.

---

## 9. Update procedure for users

```bash
# Pull a newer tag and restart in place; data lives in named volumes
docker compose --env-file .env -f deploy/docker/docker-compose.yml down
docker pull ghcr.io/<owner>/musichub-api:1.0.1
docker pull ghcr.io/<owner>/musichub-worker:1.0.1
# edit MUSICHUB_IMAGE_TAG=1.0.1 in .env
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
```

Migrations are applied by the API before it starts listening, and the worker waits for the API to
report healthy, so no manual migration step is needed.

---

## 10. Acceptance checklist

- [x] Server tests pass (372)
- [x] Dashboard tests pass (12)
- [x] API image builds with the dashboard inside
- [x] Worker image builds
- [x] Containers start and report healthy
- [x] Only intended ports are published
- [x] Data survives a restart
- [x] LAN overlay fails closed without its variables
- [x] GHCR pull path reuses the image without rebuilding
- [x] Workflows parse and `needs:` resolve
- [ ] Git remote configured and first push performed
- [ ] Release published with APK and Windows ZIP attached
- [ ] Telegram token revoked and re-provisioned
- [ ] Icon artwork delivered and integrated
