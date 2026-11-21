# MusicHub Enterprise — Docker deployment runbook

Production container topology for the MusicHub API, background worker, PostgreSQL and a TLS
edge. Everything in this directory is declarative: no script here installs a service, opens a
firewall port, deletes data or publishes a container port on its own.

| File | Purpose |
| --- | --- |
| `Dockerfile.api` | Multi-stage build (SDK 10 → ASP.NET 10 runtime) of the API plus the prebuilt dashboard; ships `ffmpeg`/`ffprobe`; runs as UID 1654. |
| `Dockerfile.worker` | Multi-stage build of the background worker; ships `ffmpeg`/`ffprobe`; runs as UID 1654; no listening socket. |
| `docker-compose.yml` | `postgres`, `api`, `worker`, `caddy`; named volumes; health checks; resource limits; private networks. |
| `docker-compose.lan.yml` | Optional overlay that publishes the dashboard on this host's LAN address and an operator-chosen port. Both come from `.env`; there is no literal address in the file. |
| `install.ps1` | One-command install: verifies Docker, generates the secrets in `.env`, detects the LAN address, asks for the dashboard port, builds the dashboard, and starts the stack. |
| `Caddyfile` | Automatic-HTTPS reverse proxy for `api:5080`. No TLS validation weakening of any kind. |
| `Dockerfile.api.dockerignore`, `Dockerfile.worker.dockerignore` | Build-context filters (BuildKit reads `<Dockerfile>.dockerignore`). |
| `../../.env.example` | Template for every variable the compose file consumes. |

> Status: `VERIFIED` on the authoring host for the parts listed in
> [Verification](#verification-and-known-gaps). The images build, the stack starts, and the
> dashboard answers on a LAN address. Re-verify on your own host with `docker compose config`
> before opening a port.

---

## 1. Topology

```
                    :80 / :443 (+ :443/udp)
                            │
                        ┌───▼────┐   edge network
                        │ caddy  │   (TLS terminates here)
                        └───┬────┘
                            │ http://api:5080
                        ┌───▼────┐   edge + backend
                        │  api   │   applies EF migrations, serves API + dashboard
                        └───┬────┘
              ┌─────────────┴──────────────┐
        ┌─────▼─────┐                ┌─────▼─────┐
        │ postgres  │                │  worker   │   backend network only
        └───────────┘                └───────────┘
```

* Only Caddy publishes host ports. `api:5080`, PostgreSQL 5432 and the worker are reachable only
  inside the compose network.
* `musichub_data` is mounted into **both** `api` and `worker` at `/var/lib/musichub`. That volume
  holds `music/`, `artwork/`, `quarantine/`, `tmp/` and `keys/dataprotection/`. The shared key
  ring is mandatory: the API encrypts the Telegram bot token with DataProtection and the worker
  must decrypt it. Splitting the volumes breaks Telegram ingestion.
* The backend network is **not** `internal: true`, because the worker needs outbound HTTPS to
  `api.telegram.org`.

---

## 2. Prerequisites

* Docker Engine 24+ with the Compose v2 plugin (`docker compose version`). BuildKit is the default
  builder and is required for the per-Dockerfile `.dockerignore` files.
* ~4 GB free for images (the .NET SDK and the ffmpeg layers dominate), plus the size of the music
  library for `musichub_data`.
* A DNS name you control for TLS (skip for the Cloudflare Tunnel and Tailscale paths).
* Inbound TCP 80 and 443 (and UDP 443 for HTTP/3) if you terminate TLS on this host.
* No firewall rule for 5080 or 5432. Nothing in this directory opens them.

---

## 3. Generate secrets

Never commit real values, never paste them into logs, tickets or chat. Use lowercase hex: it is
URL-safe, has no `;`/`"` (which would corrupt the Npgsql connection string) and no `$` (which
compose would interpolate).

Linux / macOS:

```bash
openssl rand -hex 64    # MUSICHUB_JWT_SECRET      (minimum 32 characters, 128 here)
openssl rand -hex 32    # POSTGRES_PASSWORD
openssl rand -hex 32    # MUSICHUB_BOOTSTRAP_TOKEN (one-time, delete after first run)
```

Windows PowerShell (works in 5.1 and 7+):

```powershell
function New-MusicHubHexSecret {
    param([int]$Bytes = 32)
    $buffer = [byte[]]::new($Bytes)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return ($buffer | ForEach-Object { $_.ToString('x2') }) -join ''
}

New-MusicHubHexSecret -Bytes 64   # MUSICHUB_JWT_SECRET
New-MusicHubHexSecret -Bytes 32   # POSTGRES_PASSWORD
New-MusicHubHexSecret -Bytes 32   # MUSICHUB_BOOTSTRAP_TOKEN
```

Create the environment file (from the repository root):

```powershell
Copy-Item .env.example .env
# edit .env: fill every CHANGE_ME value, set MUSICHUB_PUBLIC_URL and MUSICHUB_PUBLIC_HOSTNAME
```

Restrict it: `icacls .env /inheritance:r /grant:r "$($env:USERNAME):(R,W)"`, and on Linux
`chmod 600 .env`. The repository `.gitignore` already excludes `.env`.

> Compose resolves `.env` relative to the compose file's directory (`deploy/docker/`), so always
> pass `--env-file .env` explicitly while working from the repository root. Omitting it fails
> fast with `POSTGRES_PASSWORD must be set in .env` rather than silently starting with a weak
> password.

---

## 4. Build the dashboard bundle

The API image publishes whatever `src/server/MusicHub.Api/wwwroot` contains. `Program.cs` serves
it through `UseDefaultFiles`/`UseStaticFiles` and its `/` endpoint falls back to a JSON banner
when `index.html` is missing, so an image without the dashboard is a working API but an empty UI.

**Mode A — prebuild on the host (what the release pipeline does):**

```powershell
npm --prefix src/web/admin ci
npm --prefix src/web/admin run build
New-Item -ItemType Directory -Force src/server/MusicHub.Api/wwwroot | Out-Null
Copy-Item -Recurse -Force src/web/admin/dist/* src/server/MusicHub.Api/wwwroot/
```

**Mode B — build inside Docker and materialize the bundle:**

```powershell
docker build -f deploy/docker/Dockerfile.api --target web-export `
    --output type=local,dest=src/server/MusicHub.Api/wwwroot .
```

Both modes work with `MUSICHUB_REQUIRE_DASHBOARD=true`, which makes the image build fail when
`wwwroot/index.html` is absent. Leave it `false` for an API-only image.

The dashboard bundle is *not* baked in from `src/web/admin/dist` directly: only
`src/server/MusicHub.Api/wwwroot` ends up in the image, so a stale host `dist/` can never leak in
through the build context (it is also excluded by the `.dockerignore`).

---

## 5. First run

### 5a. One command (recommended)

```powershell
.\deploy\docker\install.ps1
```

The script:

1. Verifies Docker and the Compose v2 plugin, and that the daemon answers.
2. Creates `.env` from `.env.example` and fills `POSTGRES_PASSWORD`, `MUSICHUB_JWT_SECRET` and
   `MUSICHUB_BOOTSTRAP_TOKEN` with fresh cryptographic values. They are written to `.env` only and
   never printed. An existing `.env` is never overwritten, so a re-run keeps working secrets.
3. Detects this host's IPv4 LAN addresses and recommends the adapter that carries the default
   route. When there is more than one, it asks which address other devices should use.
4. Proposes the first free port at or above 8090 for the dashboard and lets the operator type a
   different one; a port already in use is rejected instead of being accepted and failing later.
5. Writes `MUSICHUB_LAN_ADDRESS` and `MUSICHUB_LAN_PORT` into `.env`, and points
   `MUSICHUB_PUBLIC_URL` at the resulting LAN URL when it was still the template value.
6. Validates the compose configuration, builds the dashboard into `wwwroot`, and starts
   `postgres`, `api`, `worker` and `caddy` with the LAN overlay.
7. Waits for `/healthz` and prints the exact URLs to open.

Useful flags:

```powershell
.\deploy\docker\install.ps1 -NonInteractive          # accept the detected address and first free port
.\deploy\docker\install.ps1 -Port 8090               # pin the dashboard port
.\deploy\docker\install.ps1 -LanAddress 192.168.1.10 # pin the address other devices use
.\deploy\docker\install.ps1 -PublicHostname music.example.com -AcmeEmail admin@example.com
.\deploy\docker\install.ps1 -SkipBuild               # reuse the images already present
```

### 5b. By hand

From the repository root:

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml config      # static validation
docker compose --env-file .env -f deploy/docker/docker-compose.yml build
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
docker compose --env-file .env -f deploy/docker/docker-compose.yml ps
```

Add `-f deploy/docker/docker-compose.lan.yml` to any of those commands to publish the dashboard on
the LAN. That overlay requires `MUSICHUB_LAN_ADDRESS` and `MUSICHUB_LAN_PORT` in `.env` and refuses
to start without them, so the API can never be published on an unintended interface.

> **Always pass the same `-f` files on every later command.** Compose treats the file set as part of
> the project definition. If you started the stack *with* the LAN overlay and then run
> `docker compose -f deploy/docker/docker-compose.yml restart api` (or `up -d`) **without** it,
> Compose recreates the container from the base file alone and the LAN port mapping silently
> disappears: the API still reports healthy, but nothing is published on the host and the dashboard
> becomes unreachable from other devices. Define a shell function or keep the full command in a
> script so the two files can never drift apart:

```powershell
# Put this at the top of your session, then use `mh up -d`, `mh restart api`, `mh logs -f worker`.
function mh {
  docker compose --env-file .env `
    -f deploy/docker/docker-compose.yml `
    -f deploy/docker/docker-compose.lan.yml @args
}
```

Startup order is enforced: `postgres` must pass `pg_isready`, the API applies pending EF Core
migrations *before* it starts listening, and `worker` starts only after `api` is healthy.

Verify the edge:

```powershell
curl.exe -fsS https://music.example.com/healthz      # 200 once Kestrel is up
curl.exe -fsS https://music.example.com/api/health   # 200 = DB reachable; 503 = degraded
```

`/api/health` also reports `ffprobe` availability, storage writability, malware-scanner
configuration and whether Telegram is configured. A `ffprobe: false` result means the image or the
volume is wrong, not the network.

### The Telegram bot starts automatically in the container

The `worker` service is part of the same compose stack and starts the Telegram polling loop
(`Telegram__WorkerEnabled` defaults to `true`; see `deploy/docker/README.md` section 7 for the
configuration keys). Installing and starting the stack therefore starts the Telegram bot server
with it:

```bash
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d --build
docker compose --env-file .env -f deploy/docker/docker-compose.yml ps    # worker should be Up
docker compose --env-file .env -f deploy/docker/docker-compose.yml logs worker | Select-String "Telegram polling worker started"
```

Before expecting the bot to answer, provision the bot token and the admin Telegram user id (see
`docs/INSTALL_AND_RUN.md`, section Server and Telegram bot). The worker then polls
`api.telegram.org` and answers only the configured admin sender.

### Bootstrap the first administrator

`MUSICHUB_BOOTSTRAP_TOKEN` is required; without it `POST /api/setup/initialize` answers `503`.
The endpoint is rate limited to 5 requests/minute per client IP and self-disables once any user
exists.

```powershell
curl.exe -fsS https://music.example.com/api/setup/status
# {"initialized":false,"bootstrapTokenConfigured":true}

curl.exe -fsS -X POST https://music.example.com/api/setup/initialize `
  -H "Content-Type: application/json" `
  -d '{\"bootstrapToken\":\"<your token>\",\"email\":\"admin@example.com\",\"password\":\"<at least 12 characters>\",\"displayName\":\"Administrator\"}'
```

Then remove the token from `.env` (`MUSICHUB_BOOTSTRAP_TOKEN=`) and recreate the API so bootstrap
is permanently disabled:

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d api
```

Sign in at `https://music.example.com/` (or `POST /api/auth/login` with `{ "email", "password" }`).

---

## 6. Public exposure options

### 6a. Caddy with a real domain (default)

1. Point an `A`/`AAAA` record for `MUSICHUB_PUBLIC_HOSTNAME` at the host.
2. Make sure inbound TCP 80 (ACME HTTP-01), TCP 443 and optionally UDP 443 reach the host.
3. Start the stack as in §5. Caddy obtains a Let's Encrypt certificate automatically and redirects
   HTTP to HTTPS.

Keep the edge limits consistent with the application: Kestrel caps request bodies at
`min(Storage.MaxUploadBytes, 4 GiB)` (default 2 GiB) and answers `413` beyond that; the Caddyfile
allows 4 GB so the application, not the proxy, produces the error.

### 6b. Cloudflare Tunnel (no inbound ports, TLS terminates at Cloudflare)

The tunnel connects out to Cloudflare, so no port forwarding and no certificate on this host. TLS
is terminated by Cloudflare's edge and the tunnel itself is already encrypted — do **not** weaken
anything with `--no-tls-verify`/`--insecure-skip-verify`, and do not set Cloudflare's SSL/TLS mode
to *Flexible* if you ever put a certificate-bearing origin behind it.

In the Cloudflare dashboard create a tunnel, add a public hostname whose **service is
`http://api:5080`**, and copy the tunnel token into `.env` (never into a compose file):

```dotenv
CLOUDFLARE_TUNNEL_TOKEN=CHANGE_ME_tunnel_token
MUSICHUB_PUBLIC_HOSTNAME=music.example.com
MUSICHUB_PUBLIC_URL=https://music.example.com
```

Add the sidecar in an override file (`deploy/docker/docker-compose.cloudflare.yml`):

```yaml
services:
  cloudflared:
    image: cloudflare/cloudflared:latest
    restart: unless-stopped
    command: ["tunnel", "--no-autoupdate", "run", "--token", "${CLOUDFLARE_TUNNEL_TOKEN:?set CLOUDFLARE_TUNNEL_TOKEN}"]
    networks: [edge]
    depends_on:
      api:
        condition: service_healthy
    security_opt: [no-new-privileges:true]
    cap_drop: [ALL]
    logging:
      driver: json-file
      options: { max-size: "10m", max-file: "3" }
```

Run without Caddy and without publishing a port:

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml `
  -f deploy/docker/docker-compose.cloudflare.yml up -d postgres api worker cloudflared
```

Notes:
* `cloudflared` sets `X-Forwarded-*`/`CF-Connecting-IP`. The sidecar joins the `edge` network, so
  it is inside the trusted `ForwardedHeaders__KnownNetworks` range and client IPs plus the HTTPS
  scheme survive to ASP.NET. If you run the tunnel outside compose, add its address or subnet to
  `MUSICHUB_TRUSTED_PROXY_NETWORKS` (or `MUSICHUB_TRUSTED_PROXY_ADDRESSES`), otherwise every request
  will look like it came from the sidecar.
* The sidecar needs the `edge` network to resolve `api`. It must not be put on `backend`.
* If you prefer Caddy to keep its own certificates, point the tunnel at `https://caddy:443` and set
  Cloudflare's mode to **Full (strict)**; never `No verification`.

### 6c. Tailscale

Tailscale terminates TLS with a certificate for the tailnet DNS name (`*.ts.net`), so no ACME and
no port forwarding are needed. The tunnel/relay is encrypted end to end; no verification flags
are used anywhere.

Sidecar pattern (userspace networking, no host network, no docker socket):

```yaml
services:
  tailscale:
    image: tailscale/tailscale:latest
    restart: unless-stopped
    environment:
      TS_AUTHKEY: ${TS_AUTHKEY:?set TS_AUTHKEY in .env}
      TS_STATE_DIR: /var/lib/tailscale
      TS_USERSPACE: "true"
      TS_HOSTNAME: ${TS_HOSTNAME:-musichub}
    volumes:
      - tailscale_state:/var/lib/tailscale
    networks: [edge]
    cap_drop: [ALL]
    cap_add: [NET_ADMIN]
    security_opt: [no-new-privileges:true]

volumes:
  tailscale_state:
    name: musichub_tailscale
```

Then serve the API over the tailnet (from the sidecar, which can resolve `api`):

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml `
  -f deploy/docker/docker-compose.tailscale.yml exec tailscale `
  tailscale serve --bg http://api:5080
```

`tailscale serve` is tailnet-only. `tailscale funnel --bg 5080` exposes it publicly through
Tailscale's edge (TLS still terminates at Tailscale). Set `MUSICHUB_PUBLIC_URL` to the resulting
`https://<hostname>.<tailnet>.ts.net` URL so pairing QR codes are correct.

`TS_AUTHKEY` is a credential: keep it in `.env` only, prefer a short-lived/expiring key, and
revoke it in the Tailscale admin console after the node authorizes.

### 6d. Plain port-forward, no domain

For a LAN-only installation you do not need Caddy or certificates: publish the API on a private
address instead and keep everything else internal. The provided overlay does exactly that, with the
address and port taken from `.env` rather than written into the file:

```yaml
# deploy/docker/docker-compose.lan.yml (already in the repository)
services:
  api:
    ports:
      - "${MUSICHUB_LAN_ADDRESS:?...}:${MUSICHUB_LAN_PORT:?...}:5080"
```

`deploy/docker/install.ps1` fills both variables. To do it by hand, add them to `.env`:

```dotenv
MUSICHUB_LAN_ADDRESS=192.168.1.10
MUSICHUB_LAN_PORT=8090
MUSICHUB_PUBLIC_URL=http://192.168.1.10:8090
```

Then start with both compose files:

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml `
  -f deploy/docker/docker-compose.lan.yml up -d
```

For a loopback-only publication instead (reachable through an SSH tunnel or VPN), replace the host
side with `127.0.0.1`:

```yaml
services:
  api:
    ports:
      - "127.0.0.1:5080:5080"
```

Do **not** map `5432` and do not bind `0.0.0.0` on an untrusted network: the API has no TLS
listener of its own and expects a trustworthy transport in front of it. Set
`MUSICHUB_PUBLIC_URL` to the URL clients actually use; Android clients need a reachable address, so
combine plain HTTP with a VPN rather than exposing it over the internet.

---

## 7. Configuration keys (verified against the code)

| Compose variable | Consumed as | Code reference |
| --- | --- | --- |
| `MUSICHUB_CONNECTION_STRING` | Npgsql connection string | `MusicHub.Api/Program.cs:29`, `MusicHub.Worker/Program.cs:12` |
| `MUSICHUB_JWT_SECRET` | `Auth:JwtSecret`, must be ≥ 32 chars | `Program.cs:34-38` |
| `MUSICHUB_DATA_ROOT` | `Storage:RootPath` | `Program.cs:41-48`, worker `Program.cs:17-22` |
| `MUSICHUB_FFPROBE_PATH` | `Storage:FfprobePath` | `Program.cs:51` |
| `MUSICHUB_FFMPEG_PATH` | `Storage:FfmpegPath` (artwork extraction) | `Program.cs:52` |
| `MUSICHUB_REQUIRE_MALWARE_SCAN` | `Storage:RequireMalwareScan` | `Program.cs:49` |
| `MUSICHUB_CLAMAV_HOST` | `Storage:ClamAvHost` (port 3310 default) | `Program.cs:50` |
| `MUSICHUB_BOOTSTRAP_TOKEN` | one-time setup token | `Endpoints/SetupEndpoints.cs:19,30` |
| `MUSICHUB_PUBLIC_URL` | pairing payload origin | `Api/Common.cs` (`PublicUrl.Resolve`), used by `Endpoints/PairingEndpoints.cs:31` |
| `Database__MigrateOnStartup` | run EF migrations before listening | `Program.cs:323` |
| `ASPNETCORE_URLS` | Kestrel listen address | `Program.cs:77` |
| `ForwardedHeaders__KnownProxies` | exact proxy IPs whose `X-Forwarded-*` are believed | `Api/ForwardedHeadersSetup.cs`, wired at `Program.cs:267-271` |
| `ForwardedHeaders__KnownNetworks` | proxy CIDR ranges whose `X-Forwarded-*` are believed | `Api/ForwardedHeadersSetup.cs`, wired at `Program.cs:267-271` |
| `ForwardedHeaders__ForwardLimit` | proxy hops to walk (clamped 1-10, default 1) | `Api/ForwardedHeadersSetup.cs` |
| `Telegram__WorkerEnabled` | enables polling/publishing loops | `Worker/TelegramWorkers.cs:18` |
| `Cors__AllowedOrigins__N` | dashboard CORS allow-list | `Program.cs:123` |

Defaults worth knowing: uploads are capped at 2 GiB (`Storage:MaxUploadBytes`); the ClamAV port is
3310 and is not published; `/healthz` is dependency-free liveness, `/api/health` is readiness.
`MUSICHUB_ADMIN_EMAIL`/`MUSICHUB_ADMIN_PASSWORD` are only used by the native dev scripts in
`deploy/scripts`; no server code reads them, and they are not part of this compose file.

---

## 8. Migrations and upgrades

* The API runs `Database.MigrateAsync()` on startup when `Database__MigrateOnStartup=true`
  (set by default in compose, because the containers run `Production`). Migrations therefore block
  the API from listening until the schema is current; `worker` waits for `api` to be healthy.
* Run exactly **one** API instance while migrations are enabled. For scale-out, migrate once with a
  single replica and then start the rest with `Database__MigrateOnStartup=false`.
* Upgrade procedure:

```powershell
# 1. back up first (§10)
docker compose --env-file .env -f deploy/docker/docker-compose.yml build
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
curl.exe -fsS https://music.example.com/api/health
```

* Rollback: image tags can be rolled back, but migrations are effectively forward-only. Restore the
  pre-upgrade dump instead of hand-reverting schema changes. Never run an older image against a
  newer schema.

---

## 9. Operations

```powershell
$c = @('compose','--env-file','.env','-f','deploy/docker/docker-compose.yml')
docker @c ps
docker @c logs -f --tail=200 api worker
docker @c exec api curl -fsS http://127.0.0.1:5080/api/health
docker @c restart api
docker @c down            # stops and removes containers; keeps the volumes (never add -v)
```

* **Graceful shutdown.** Containers get `stop_grace_period` 45 s (api) / 60 s (worker) so ASP.NET
  Core can drain requests and the worker can finish the current Telegram iteration and release its
  database scope instead of being SIGKILLed at Docker's 10 s default.
* **Logging.** Logs go to stdout only, captured by the `json-file` driver with rotation
  (`max-size: 10m`, `max-file: 5`) so a noisy container cannot fill the host disk.
* **Worker health.** The container health check is only process liveness. The authoritative signal
  is the heartbeat row the worker writes:

```powershell
docker @c exec -T postgres psql -U musichub -d musichub -c `
  'select "Key", "UpdatedAt" from "AppSettings" where "Key" = $q$worker.heartbeat$q$;'
```

  A heartbeat older than ~2 minutes means the worker is wedged even if the container is "healthy".
* **Do not** mount `/var/run/docker.sock` into any service, and do not add a `ports:` entry to
  `postgres`, `api` or `worker`.

---

## 10. Backup, restore and retention

### 10.1 Online logical backup (preferred; no downtime)

`docker compose cp` is used instead of shell redirection because PowerShell's `>` corrupts binary
output.

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force backups | Out-Null
$c = @('compose','--env-file','.env','-f','deploy/docker/docker-compose.yml')

docker @c exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/musichub-$STAMP.dump'
docker @c cp "postgres:/tmp/musichub-$stamp.dump" "./backups/musichub-$stamp.dump"
docker @c exec -T postgres rm -f "/tmp/musichub-$stamp.dump"

Get-FileHash "./backups/musichub-$stamp.dump" -Algorithm SHA256 | Format-List
```

### 10.2 Integrity check (run on every backup)

```powershell
docker @c exec -T postgres pg_restore -l /dev/stdin < ./backups/musichub-$stamp.dump   # Linux/macOS
docker run --rm -v "${PWD}/backups:/b:ro" postgres:17.11-bookworm pg_restore -l "/b/musichub-$stamp.dump" | Select-Object -First 20
```

A truncated dump fails `pg_restore -l`; always check before rotating older backups out.

### 10.3 Cold volume backup (whole PostgreSQL cluster)

Only while the database is stopped, otherwise the copy is inconsistent:

```powershell
docker @c stop api worker postgres
docker run --rm -v musichub_pgdata:/data:ro -v "${PWD}/backups:/backup" alpine:3.20 `
    tar czf "/backup/pgdata-$stamp.tgz" -C /data .
docker @c start postgres api worker
```

### 10.4 Music/data volume backup

The music library, artwork, quarantine and the DataProtection key ring live on
`musichub_data`. A database dump alone does **not** contain the audio files.

```powershell
docker run --rm -v musichub_data:/data:ro -v "${PWD}/backups:/backup" alpine:3.20 `
    tar czf "/backup/musichub_data-$stamp.tgz" -C /data music artwork keys
```

`keys/` is a secret: it decrypts the stored Telegram bot token. Treat that archive like a
credential — encrypted at rest, restricted ACLs, never in the repository, never in chat.

### 10.5 Restore

> Destructive and irreversible. Confirm you have a verified dump and the operator's approval
> before dropping a database or emptying a volume.

```powershell
$c = @('compose','--env-file','.env','-f','deploy/docker/docker-compose.yml')
docker @c down                     # keeps volumes

# 1. database (recreate empty, then restore)
docker @c up -d postgres
docker @c exec -T postgres psql -U musichub -d postgres -c 'DROP DATABASE IF EXISTS musichub WITH (FORCE);'
docker @c exec -T postgres psql -U musichub -d postgres -c 'CREATE DATABASE musichub OWNER musichub;'
docker @c cp "./backups/musichub-$stamp.dump" postgres:/tmp/restore.dump
docker @c exec -T postgres pg_restore -U musichub -d musichub --no-owner --exit-on-error /tmp/restore.dump
docker @c exec -T postgres rm -f /tmp/restore.dump

# 2. data volume, only if it was lost (with api/worker stopped)
docker run --rm -v musichub_data:/data -v "${PWD}/backups:/backup" alpine:3.20 `
    sh -c "tar xzf /backup/musichub_data-$stamp.tgz -C /data"

# 3. start and verify
docker @c up -d
curl.exe -fsS https://music.example.com/api/health
```

Post-restore checks: `\dt` shows the full schema, `"__EFMigrationsHistory"` ends with
`20261116113423_AddUserCanUsePlayer` (the current source migration), `"Tracks"` row count matches
expectation, and `/api/health` reports `database:true, storageWritable:true, ffprobe:true`.

If the volume was recreated from scratch after a restore, re-check the volume ownership: the image
pre-creates `/var/lib/musichub` owned by 1654:1654 so a *new* named volume inherits it. For a host
bind mount you must do it yourself: `sudo chown -R 1654:1654 /srv/musichub`.

### 10.6 Retention

Suggested baseline for a personal library: 7 daily dumps, 4 weekly, 6 monthly — but every backup
must be validated with `pg_restore -l` and stored off-host. Delete by age only after a newer dump
has passed the integrity check. `storage/`, `artifacts/` and `backups/`-style paths are already
ignored by the repository `.gitignore`; never commit a dump or key-ring archive.

---

## 11. Security posture

| Control | Implementation |
| --- | --- |
| Non-root application containers | Images `USER 1654:1654` (`APP_UID`/`APP_GID`); `/app` stays root-owned and read-only. |
| Read-only root filesystem | `read_only: true` plus a small `noexec,nosuid` `/tmp` tmpfs for api, worker, caddy. |
| No capabilities | `cap_drop: [ALL]` for api/worker; Caddy keeps only `NET_BIND_SERVICE` to bind 80/443. |
| No privilege escalation | `security_opt: [no-new-privileges:true]` on every service. |
| Process bounds | `pids_limit: 512` on api/worker (bounds ffmpeg fan-out). |
| Database isolation | `postgres` publishes no port and sits only on the backend network. |
| Worker isolation | no port, backend network only, no JWT secret in its environment. |
| Secrets | runtime-only via `.env` → environment; nothing is copied into an image or layer; `${VAR:?}` fails the deployment instead of starting with a placeholder. |
| Music storage | `musichub_data` named volume at `/var/lib/musichub`, never inside the image or a writable layer. |
| TLS | Caddy automatic certificates only. No `tls internal`, `skip_verify`, `insecure_skip_verify`, `auto_https off`, `-k`, or `NODE_TLS_REJECT_UNAUTHORIZED`. |
| Access logs | deliberately disabled: stream/pairing URLs can carry bearer material in query strings. |

Known caveats, stated rather than hidden:

* Forwarded headers are trusted only from `ForwardedHeaders__KnownNetworks` (defaults to the pinned
  `edge` subnet `172.28.0.0/24`) and any address in `ForwardedHeaders__KnownProxies`. The framework
  switch `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is deliberately **not** used: it clears the
  known-proxy list and believes `X-Forwarded-For` from any source, so a direct caller or a rogue
  container could forge the client IP, escape the per-IP rate limit on `login`/`upload`/`setup`,
  and poison the IP recorded in audit and session rows. Keep the trusted set narrow and never
  publish port 5080.
* The DataProtection key ring is stored unencrypted on Linux (`ProtectKeysWithDpapi()` is
  Windows-only, see `Program.cs:62-65`). Protect the volume: full-disk encryption on the host,
  restrictive permissions, and treat the `keys/` backup as a secret.
* Caddy runs as root inside its container (official image default) with all capabilities dropped
  except `NET_BIND_SERVICE`. A non-root UID cannot bind :80/:443 and cannot own a freshly created
  named volume without a manual `chown`; if you need that, set `user: "1000:1000"` on the `caddy`
  service, add `sysctls: { net.ipv4.ip_unprivileged_port_start: "0" }`, and pre-chown
  `musichub_caddy_data`/`musichub_caddy_config`. That variant is untested here.

---

## 12. Verification and known gaps

**Verified on the authoring host (Docker Desktop, linux/amd64):**

* `docker compose config` exits `0` for the base file and for the base + LAN overlay (with
  `MUSICHUB_LAN_ADDRESS` / `MUSICHUB_LAN_PORT` set). Without them the overlay fails with
  `required variable MUSICHUB_LAN_ADDRESS is missing a value`, which is the intended behaviour.
* `docker build` produced `musichub/api:local` (943 MB) and `musichub/worker:local` (940 MB), both
  `linux/amd64`, both `USER 1654:1654`.
* `docker compose up -d` brought up `postgres`, `api`, `worker` and `caddy`; `postgres`, `api` and
  `worker` report `healthy`, `caddy` is `Up`.
* The published mapping resolves to the intended socket: `host_ip: <detected LAN address>`,
  `published: <chosen port>`, `target: 5080`, protocol `tcp`.
* End-to-end through the LAN publication: `GET /` → `302` → `/index.html` (`200`, 951 B), the JS
  bundle (`200`, 525,355 B), `POST /api/auth/login` → `200`, `GET /api/dashboard/stats` → `200`,
  and `GET /api/users` without a token → `401`.
* `dotnet test MusicHub.slnx` → 354 passed, 0 failed, including the compose-topology tests that
  assert the overlay's shape.

**Not verified here:**

* No image scan (`docker scout`, `trivy`) was run.
* Multi-arch (`linux/arm64`) was not built. The base images publish arm64 and buildx lists it as
  available, but no `--platform` pin exists yet.
* The Cloudflare Tunnel and Tailscale overlays in §6b/§6c are documented patterns, not tested
  deployments on this host.

**Discovered mismatches / follow-ups for the orchestrator:**

1. `.gitignore` line 29 (`.env.*`) also ignores `.env.example`, so the template cannot be
   committed without a `!.env.example` exception (or `git add -f .env.example`).
2. Client IPs and the HTTPS scheme behind a proxy now come from `ForwardedHeadersSetup`
   (`ForwardedHeaders__KnownNetworks` / `KnownProxies`), which is installed before the rate limiter
   and the exception handler. `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is no longer set by compose,
   because it trusts every peer. If you add a proxy that is not on the `edge` network, list it
   explicitly or client IPs will be the proxy's address.
3. `MUSICHUB_PUBLIC_URL` accepts an empty value as "unset" and falls back to the request origin; a
   value that is set but is not an absolute `http(s)` URL fails the pairing request with a 500 that
   names the variable, instead of emitting a QR code no client can parse. The compose file still
   marks it required so the payload matches the public hostname.
4. The API — not just the worker — needs `ffmpeg`/`ffprobe` for uploads (`TrackImportService`) and
   for `/api/health`; both images install them.
5. Worker and API must share the DataProtection key ring, or Telegram publishing cannot decrypt the
   stored bot token. This is why both mount `musichub_data`.
6. Upload buffering: `ReadFormAsync()` spills bodies to temp storage, so images set
   `TMPDIR=/var/lib/musichub/tmp` instead of relying on a tmpfs sized for multi-gigabyte uploads.
7. The LAN overlay publishes the API directly over HTTP, so requests bypass Caddy. That is
   intentional for a trusted LAN (the alternative is a certificate warning on every device), and it
   is why the mapping is bound to a private address and documented as LAN-only. Do not reuse it for
   internet exposure.

---

## 13. Troubleshooting

| Symptom | Likely cause / action |
| --- | --- |
| `MUSICHUB_JWT_SECRET must be set` at startup | `.env` missing/placeholder; run compose with `--env-file .env`. |
| API healthy, `/api/health` reports `database:false` | `POSTGRES_PASSWORD` changed after the volume was initialized; the stored role password no longer matches. Restore the password or reinitialize the volume (destructive). |
| `ffprobe:false` in `/api/health` | Wrong image or `MUSICHUB_FFPROBE_PATH`; both images install ffmpeg at `/usr/bin`. |
| Uploads fail with 413 | Body larger than Kestrel's `min(MaxUploadBytes, 4 GiB)`; raise `Storage__MaxUploadBytes` (and keep Caddy's 4 GB ceiling above it). |
| Uploads fail with 500 after a large upload | `/var/lib/musichub` full (temp spill) or not writable by UID 1654 — check the volume ownership. |
| Edge shows `522`/`525`/`526` with Cloudflare | Origin TLS mismatch caused by Cloudflare's SSL mode; use Full (strict) with a real origin certificate, never "No verification". |
| `caddy` restarts in a loop | Hostname/email not set, or ports 80/443 already in use; `docker compose logs caddy`. |
| `worker` never starts | It waits for `api` to become healthy, which means migrations failed; check `api` logs for the EF error. |
| SignalR disconnects behind the proxy | Keep the request path identical (no path rewriting) and do not add an upstream response timeout. |
