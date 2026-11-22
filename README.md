<div dir="rtl">

**فارسی:** [README.fa.md](README.fa.md)

</div>

# MusicHub Enterprise

**Self-hosted, multi-user music library and streaming platform.**

MusicHub Enterprise is a self-hosted, multi-user music library and streaming platform. It is
independent of Spotify and of any third-party catalogue: your music lives on your own disk, on your
own server, under your own accounts.

| Component | What it is |
| --- | --- |
| **Server / API** | ASP.NET Core (.NET 10) minimal API, EF Core, PostgreSQL, JWT auth, SignalR |
| **Worker** | Telegram ingestion and publishing, integrity scans, queued jobs, retention pruning |
| **Admin dashboard** | React + TypeScript + Vite, Persian (RTL) and English (LTR), light and dark |
| **Windows player** | Native WPF player, LibVLC playback, queue, favourites, playlists |
| **Android client** | Kotlin + Compose + Media3, QR pairing, offline downloads |
| **Deployment** | Native Windows scripts and Docker Compose, with backups, restores and runbooks |

## Download

Grab the latest release from the [Releases page](../../releases/latest).

| Platform | File | Notes |
| --- | --- | --- |
| Android 8.0+ | `MusicHub-Android-<version>-release.apk` | Signed release build. Enable "install from unknown sources" for your file manager. |
| Windows 10/11 x64 | `MusicHubPlayer-win-x64-<version>.zip` | Self-contained: no .NET runtime needed. Extract and run `MusicHub.Player.exe`. |
| **Windows server** | `MusicHubServer-win-x64-<version>.zip` | **Self-contained server stack**: PostgreSQL, API, Worker (Telegram bot) and the dashboard. No .NET SDK, no Docker and no network connection needed. |
| Docker (server) | `ghcr.io/<owner>/musichub-api:<version>` and `ghcr.io/<owner>/musichub-worker:<version>` | `linux/amd64` and `linux/arm64`. |

Every release also publishes `SHA256SUMS.txt` and a per-file `.sha256`, so a download can be
verified before it is installed.

> The signed APK, the player zip and the server package are **release assets**, not repository
> files. Build output is never committed.

## Quick start

### 🚀 One-Line Linux Quick Install (Recommended)

Install all prerequisites (Docker, PostgreSQL 17, audio tools, SSL certificate, and system manager) with a single command:

```bash
curl -fsSL https://raw.githubusercontent.com/mohsenk2159/irpotify/main/install.sh | sudo bash
```

> **Instant Management:** After installation, manage the server, inspect live logs, restart, or update anytime simply by running:
> ```bash
> irpotify
> ```

---

### Windows server package (recommended for a Windows server)

Download `MusicHubServer-win-x64-<version>.zip`, extract it, and run the installer from the extracted
folder. It needs nothing installed beforehand:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1
```

The installer detects this machine's LAN address, asks which port the API should listen on,
initialises PostgreSQL, generates the secrets, starts PostgreSQL + the API + the Worker (which runs
the Telegram bot), verifies the stack over HTTP, and registers a logon task so it comes back after a
reboot. It prints the dashboard URL when it finishes.

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Status
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Start
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Stop
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Uninstall
```

### Docker (recommended for the server on Linux)

```bash
git clone <repository-url> musichub
cd musichub
cp .env.example .env
```

Edit `.env` and replace every `CHANGE_ME` value. Generate the secrets with a cryptographic
generator, not by hand:

```bash
openssl rand -hex 32   # POSTGRES_PASSWORD
openssl rand -hex 64   # MUSICHUB_JWT_SECRET
openssl rand -hex 32   # MUSICHUB_BOOTSTRAP_TOKEN
```

Then start the stack:

```bash
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
```

Or let the installer do all of the above — it generates the secrets, detects this machine's LAN
address, asks which port to publish the dashboard on, and prints the URL to open:

```powershell
.\deploy\docker\install.ps1
```

To use the prebuilt public images instead of building locally, set the tag in `.env`:

```dotenv
MUSICHUB_IMAGE_TAG=1.0.0
```

and point compose at GHCR by overriding the image names:

```bash
docker pull ghcr.io/<owner>/musichub-api:1.0.0
docker pull ghcr.io/<owner>/musichub-worker:1.0.0
docker tag ghcr.io/<owner>/musichub-api:1.0.0 musichub/api:1.0.0
docker tag ghcr.io/<owner>/musichub-worker:1.0.0 musichub/worker:1.0.0
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
```

### Windows from a source checkout

This path is for development: it needs the .NET 10 SDK and the `tools\pgsql` and `tools\ffmpeg`
payloads, both gitignored, so a fresh clone must provide them. To install on a Windows server without
any toolchain, use the server package above instead.

```powershell
# 1. Database credential. Detects the LAN address and asks which port the API should use.
.\deploy\scripts\install-server.ps1

# 2. Start PostgreSQL, the API and the worker (the worker also starts the Telegram bot).
.\deploy\scripts\start-dev.ps1 -BindLan

# 3. Verify
.\deploy\scripts\verify.ps1
```

The installer prints the exact URLs at the end:

```text
  Access this server
    This machine:      http://127.0.0.1:5080/index.html
    Other devices:     http://192.168.1.20:5080/index.html
    Server address for the Android and Windows apps:
      http://192.168.1.20:5080
```

Use the **Other devices** address for the phone and the Windows player. Both the address and the
port are discovered at install time, so the same scripts work on any machine and no port conflict
is assumed away.

### First administrator

`MUSICHUB_BOOTSTRAP_TOKEN` must be set. Then:

```bash
curl -X POST http://127.0.0.1:5080/api/setup/initialize \
  -H "Content-Type: application/json" \
  -d '{"bootstrapToken":"<your token>","email":"admin@example.com","password":"<12+ characters>","displayName":"Administrator"}'
```

After the account exists, clear the token from `.env` and recreate the API so bootstrap is
permanently disabled.

## How it works

```text
                    :80 / :443
                        │
                    ┌───▼────┐   TLS terminates here (Caddy, real certificates)
                    │ caddy  │
                    └───┬────┘
                        │ http://api:5080
                    ┌───▼────┐
                    │  api   │   applies migrations, serves the API and the dashboard
                    └───┬────┘
          ┌─────────────┴──────────────┐
    ┌─────▼─────┐                ┌─────▼─────┐
    │ postgres  │                │  worker   │   private network only
    └───────────┘                └───────────┘
```

* Only the TLS edge publishes a host port. PostgreSQL, the API and the worker are reachable only
  inside the compose network.
* `musichub_data` is mounted into **both** the API and the worker. It holds the music, the artwork,
  the quarantine area and the DataProtection key ring. The shared key ring is mandatory: the API
  encrypts the Telegram bot token and the worker must decrypt it.
* The LAN overlay (`deploy/docker/docker-compose.lan.yml`) optionally publishes the dashboard on a
  private address and an operator-chosen port, so a phone on the same network can reach it without
  a public DNS name and without weakening TLS.

## Repository layout

```text
src/server/MusicHub.Api            HTTP API, auth, endpoints, SignalR
src/server/MusicHub.Worker         background worker (Telegram, jobs, retention)
src/server/MusicHub.Domain         entities and enums
src/server/MusicHub.Application    application layer
src/server/MusicHub.Infrastructure EF Core, storage, FFprobe, security, Telegram
src/web/admin                      admin dashboard (React + Vite)
src/desktop/MusicHub.Player        Windows desktop player (WPF + LibVLC)
src/android                        native Android client
tests/server/MusicHub.Tests        backend unit and integration tests
deploy/scripts                     Windows install, start, verify, backup, restore
deploy/docker                      container topology, Dockerfiles, install script
docs/                              architecture, contracts, status, runbooks
tools/                             local portable toolchain (gitignored)
storage/                           runtime data root (gitignored)
```

## Configuration

| Variable | Required | Purpose |
| --- | --- | --- |
| `MUSICHUB_JWT_SECRET` | Yes | HMAC signing key, at least 32 characters |
| `MUSICHUB_BOOTSTRAP_TOKEN` | First run only | One-time token to create the first administrator |
| `POSTGRES_PASSWORD` | Docker | Database role password |
| `MUSICHUB_CONNECTION_STRING` | Native | PostgreSQL connection string (Docker builds it from the parts) |
| `MUSICHUB_PUBLIC_URL` | Recommended | Absolute origin used in Android/desktop pairing payloads |
| `MUSICHUB_PUBLIC_HOSTNAME` / `MUSICHUB_ACME_EMAIL` | Docker | Caddy certificate hostname and ACME contact |
| `MUSICHUB_DATA_ROOT` | Recommended | Absolute path for music, artwork, quarantine, keys |
| `MUSICHUB_LAN_ADDRESS` / `MUSICHUB_LAN_PORT` | Optional | Written by `deploy/docker/install.ps1`; publishes the dashboard on the LAN |
| `MUSICHUB_FFPROBE_PATH` / `MUSICHUB_FFMPEG_PATH` | Recommended | Media tool binaries |
| `MUSICHUB_REQUIRE_MALWARE_SCAN` / `MUSICHUB_CLAMAV_HOST` | Optional | ClamAV enforcement |

Never commit secrets. `.env`, `storage/` (including the generated `dev.secrets.ps1` and the
database credential file) and the Android signing key are gitignored. See
[`deploy/docker/README.md`](deploy/docker/README.md) §7 for the full table and the code that reads
each variable.

## Build from source

```powershell
$env:PATH="$env:PATH;$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet build MusicHub.slnx
dotnet test MusicHub.slnx

npm --prefix src/web/admin install
npm --prefix src/web/admin run build
```

The API image compiles the dashboard itself, so a locally built image already serves the UI. For a
native run, publish the bundle into the static root:

```powershell
Copy-Item src\web\admin\dist\* src\server\MusicHub.Api\wwwroot\ -Recurse -Force
```

## Security

Server-side authorization for every endpoint and for media streaming; JWT access tokens with
rotating refresh tokens and reuse detection; bootstrap requires an explicit token and no default
credentials exist; uploads are validated by extension, magic bytes and FFprobe parsing, with an
optional malware scan; music is stored content-addressed and its paths are never returned to
clients; the Telegram bot token is encrypted at rest and never returned by the API.

See [`SECURITY.md`](SECURITY.md) for the full model and the operator responsibilities, and
[`docs/security/CLIENT_TAMPER_ASSESSMENT.md`](docs/security/CLIENT_TAMPER_ASSESSMENT.md) for the
client-side analysis.

## Documentation

| Document | Contents |
| --- | --- |
| [`docs/INSTALL_AND_RUN.md`](docs/INSTALL_AND_RUN.md) | Version 1 install on Windows, Android and Docker |
| [`deploy/docker/README.md`](deploy/docker/README.md) | Container runbook: exposure options, backup, restore, upgrades |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Component design and data flow |
| [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md) | Endpoint contract, with `docs/openapi.json` |
| [`docs/DATABASE_SCHEMA.md`](docs/DATABASE_SCHEMA.md) | Tables, relationships, indexes |
| [`docs/TELEGRAM_BOT.md`](docs/TELEGRAM_BOT.md) | Bot setup, commands, ingestion and publishing |
| [`docs/PROJECT_STATUS.md`](docs/PROJECT_STATUS.md) | Evidence-based status of every feature |

## Contributing

Issues and pull requests are welcome. Read [`SECURITY.md`](SECURITY.md) before reporting anything
that looks like a vulnerability, and do not open a public issue for a security defect.

## License

[MIT](LICENSE)
