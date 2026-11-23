# MusicHub — install and run on this machine

Everything below was executed on this host and observed, not assumed.

## Version 1 install — overview and required files

Version 1 installs one cooperating set of components and one client:

| Platform | Installs | Required install file(s) |
| --- | --- | --- |
| Android | MusicHub client (`com.musichub.mobile` 1.0.0 / versionCode 2) | `artifacts\installers\MusicHub-Android-1.0.0-release.apk` + `MusicHub-Android-1.0.0-release.apk.sha256` |
| Windows | MusicHub player (self-contained win-x64) + **self-contained server package** (PostgreSQL/API/Worker/dashboard) | `artifacts\installers\MusicHubPlayer-win-x64.zip`, `artifacts\installers\MusicHubServer-win-x64-1.0.0.zip` (or a source checkout: `deploy\scripts\install-server.ps1`, `deploy\scripts\start-dev.ps1`) |
| Docker | Full server stack (postgres + api + worker + caddy) | `deploy\docker\docker-compose.yml`, `deploy\docker\Caddyfile`, `deploy\docker\Dockerfile.api`, `deploy\docker\Dockerfile.worker`, runtime env (`.env`) |

The Telegram bot server is part of the installed stack on every platform (version 1 install
contract, see below); installing and starting means the bot starts with it.

## Artifacts

| File | Size | State |
| --- | --- | --- |
| `artifacts\installers\MusicHub-Android-1.0.0-release.apk` | 13.17 MB | Signed release build, versionCode 2 |
| `artifacts\installers\MusicHub-Android-1.0.0-release.apk.sha256` | — | SHA-256 of the APK above |
| `artifacts\installers\MusicHubPlayer-win-x64.zip` | 104.9 MB | Self-contained win-x64 player |
| `artifacts\installers\MusicHubServer-win-x64-1.0.0.zip` | 181.7 MB | Self-contained Server for Windows package (Host): API + Worker + dashboard + private .NET runtime + trimmed portable PostgreSQL 17 + ffmpeg/ffprobe |
| `deploy\docker\docker-compose.yml` + `Caddyfile` + `Dockerfile.api` + `Dockerfile.worker` | - | Version 1 container install (postgres + api + worker + caddy); built images `musichub/api:local`, `musichub/worker:local`, `postgres:17.11-bookworm`, `caddy:2.11.9-alpine` |

## Install artifact readiness by platform (version 1)

- **Android:** install file ready - `artifacts\installers\MusicHub-Android-1.0.0-release.apk` (release-signed, v2+v3, RSA 4096, Play Protect clean, cold start verified on the connected device).
- **Windows:** install files ready - `artifacts\installers\MusicHubPlayer-win-x64.zip` (self-contained player) and the server install path (`deploy\scripts\install-server.ps1`, `start-dev.ps1`, `verify.ps1`).
- **Docker:** install files ready - `deploy\docker\docker-compose.yml` + runtime env; full stack built and running during the version 1 install verification (postgres/api/worker healthy, caddy up, bot auto-start verified in the worker container).

## Android

`minSdk 26` (Android 8.0), `targetSdk 35`, ABIs `arm64-v8a, armeabi-v7a, x86, x86_64`.

Signature facts, from `apksigner verify --verbose --print-certs`:

- `Verifies`, 1 signer
- v2 scheme: true, v3 scheme: true
- certificate `CN=MusicHub Enterprise, OU=MusicHub, O=MusicHub, L=Tehran, C=IR`
- RSA 4096-bit key, valid until 2116
- certificate SHA-256 `8C:FF:B3:86:0E:3D:33:84:0A:BF:90:BB:72:DC:8C:EF:34:0B:A3:5E:E1:7A:88:F1:D1:F4:B6:BE:C6:95:B7:D7`

No `debuggable` attribute is present in the release manifest, and no camera feature is marked
required, so the APK installs on devices and emulators that report no camera.

### Signing key

`storage\android-keys\musichub-release.jks` plus `storage\android-keys\keystore.properties`.
Both are gitignored (`storage/` is ignored), so neither can be committed. The password exists only
in that properties file; it is not in this document, in source, or in any log.

Back these two files up before rebuilding. Losing them means the app can never be updated in place
again — every future build would need a different `applicationId` or a full uninstall on each
device.

`src/android/app/build.gradle.kts` reads the properties file at configuration time. If it is absent
(a fresh clone, or CI without secrets) the release build is left **unsigned** rather than silently
falling back to the debug key, which is what made the old APK uninstallable.

### Install on the phone

1. Copy `MusicHub-Android-1.0.0-release.apk` to the device.
2. Allow installing from unknown sources for the file manager or browser used.
3. Install and open it.

The server URL is entered in the app. The installer prints the address to use: on the server host
run `.\deploy\scripts\start-dev.ps1 -BindLan` and read the **Other devices** line of the summary
(for example `http://192.168.1.20:5080`). Both the address and the port are chosen at install time,
so use the values the installer printed for your machine rather than a fixed address.
Cleartext must be allowed in the app's connection settings — `LocalNetworkPolicy` rejects
`http://` unless it is enabled and the host is loopback or private.

## Windows player

1. Extract `MusicHubPlayer-win-x64.zip` to a folder, for example `C:\MusicHubPlayer`.
2. Run `MusicHub.Player.exe`.

Self-contained: the .NET runtime does not need to be installed. The zip is not an MSI; the
repository has no WiX/Inno/MSIX project.

## iOS client (iPhone & iPad)

The native iOS client is built in **Swift and SwiftUI** (`src/ios`) and targets iOS 16.0+:

### Features
- Native audio engine (`AVPlayer`) with continuous background playback (`UIBackgroundModes: audio`).
- System Lock Screen & Control Center controls with real-time seeking, duration, and cover art (`MPRemoteCommandCenter` & `MPNowPlayingInfoCenter`).
- Instant QR code pairing via camera scanner (`AVCaptureSession`).
- Home, Search, Library (with 0-9 / A-Z alphabetical sort), Album, Artist, Folder, and Settings views matching Spotify's dark aesthetic.

### Installation
1. Obtain the `irpotify-ios.ipa` binary from the [Releases](https://github.com/mohsenk2159/irpotify/releases) page or the GitHub Actions `iOS` workflow artifact.
2. Sideload onto iPhone or iPad using:
   - **Sideloadly** (Windows / Mac) with a free personal Apple ID.
   - **AltStore** / **AltServer**.
   - **TrollStore** (for devices on iOS 14.0–17.0) for permanent installation.
3. Trust the developer profile under iOS **Settings > General > VPN & Device Management**.
4. Open **irpotify** and scan the pairing QR code from the admin dashboard to start streaming!

## Server and Telegram bot

Three processes must be running. They are started and kept alive by the Scheduled Task
**MusicHub Dev Supervisor**, which runs `storage\run\supervisor.ps1` at logon and every 30 seconds
checks that PostgreSQL, the API and the worker are up, restarting any that died.

```
PostgreSQL 17   127.0.0.1:5433     tools\pgsql
MusicHub.Api    <chosen port>      serves the LAN and the dashboard
MusicHub.Worker                   long-polls Telegram: ingestion, publishing, maintenance
```

The API port is whatever the installer selected (default: the first free port at or above 5080).
`storage\install.endpoint.json` records the chosen address and port; `deploy/scripts/start-dev.ps1`
prints both in its closing summary.

### Version 1 install contract: the Telegram bot starts automatically

From version 1 the Telegram bot is part of the installed stack on both platforms. Installing and
starting the stack means the Telegram bot server starts with it; there is no separate manual step
that has to be done after install except provisioning the bot token and the admin id (below).

- **Windows (native / developer install).** The Worker the supervisor task starts runs
  `TelegramPollingWorker`, which starts polling while `Telegram:WorkerEnabled` is true (the default
  the generated `storage\dev.secrets.ps1` now sets to `true`; the previous generated value was `false`).
  The whole install path is: `.\deploy\scripts\install-server.ps1` (database credential) then
  `.\deploy\scripts\start-dev.ps1` (starts PostgreSQL + API + Worker) or the
  **MusicHub Dev Supervisor** Scheduled Task. After a fresh install the stack comes up with the bot
  polling automatically.
- **Docker (container install).** `deploy/docker/docker-compose.yml` starts `postgres`, `api`,
  `worker` and `caddy`; the worker service sets `Telegram__WorkerEnabled: ${MUSICHUB_TELEGRAM_WORKER_ENABLED:-true}`. After
  `docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d --build` the Telegram
  bot starts automatically inside the worker container.

### Prerequisites for the bot on either platform (version 1)

1. A Telegram bot token (obtain from BotFather, then store it encrypted through the API:
   `PUT /api/admin/telegram`, or through the operator CLI
   `dotnet src/server/MusicHub.Install/bin/Debug/net10.0/MusicHub.Install.dll set-telegram-admin`
   and the token endpoints documented in `docs/TELEGRAM_BOT.md`). The token lives in the database
   under `TelegramConfigurations.BotTokenProtected` and is decrypted with the shared DataProtection
   key ring; the API and the Worker must share the data volume / key directory.
2. The admin Telegram user id (`AdminTelegramUserId` + `AdminChatId`) in the same row. The bot
   answers only that sender; enforcement is server-side.
3. ffmpeg + ffprobe for the Worker (already inside the Docker worker image; on Windows from
   `tools\ffmpeg\...`). The bot uses ffprobe for metadata and ffmpeg for artwork.
4. Outbound HTTPS to `api.telegram.org` (and, when configured, a local Bot API server for uploads
   above the 50 MB Bot API limit).

### Install commands (version 1)

**Windows, from the package (recommended).** `artifacts\installers\MusicHubServer-win-x64-<version>.zip`
is self-contained: a private .NET runtime, portable PostgreSQL 17, ffmpeg/ffprobe and the published
hosts. It needs no .NET SDK, no Docker and no network connection.

```powershell
# Extract, then from the extracted folder (the zip contains MusicHubServer-win-x64-<version>\):
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1
```

The installer verifies the payload, points `DOTNET_ROOT` at the bundled runtime, detects this
machine's LAN address, asks which port the API should listen on, initialises the PostgreSQL cluster,
writes the encrypted database credential, generates `storage\dev.secrets.ps1` (with
`ASPNETCORE_ENVIRONMENT=Production` and `Database__MigrateOnStartup=true`), starts PostgreSQL + the
API + the Worker, verifies the stack over HTTP, and registers the **MusicHub Server** logon task.
It prints the dashboard URL when it finishes.

Afterwards:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Status
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Start
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Stop
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Uninstall          # keeps storage\
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Uninstall -PurgeData # deletes storage\
```

`-NoScheduledTask` skips the logon task (useful for a throwaway test install). `-ApiPort` and
`-LanAddress` pre-answer the two install questions, and `-NonInteractive` accepts the detected values.

`verify.ps1` inspects the database directly with `psql` and therefore needs the plaintext role
password, which a package install deliberately does not have (it exists only inside
`storage\keys\db-credential.protected`). On a package install its `database.migrations` and
`worker.heartbeat` checks would FAIL on a healthy server. Verify a package install through
`-Status` and the API instead:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Status
Invoke-RestMethod http://127.0.0.1:<port>/api/health     # checks.database / checks.storageReadable
```

**Windows, from a source checkout (development).** This path needs the .NET 10 SDK and the
`tools\pgsql` + `tools\ffmpeg` payload, both gitignored, so a fresh clone must provide them.

```powershell
# 1. Database credential (fresh cluster: generates the encrypted credential).
#    Detects this machine's LAN address and asks which port the API should listen on.
.\deploy\scripts\install-server.ps1

# 2. Start the whole stack: PostgreSQL, API, Worker (the Worker starts the Telegram bot).
#    -BindLan listens on the LAN so phones and other devices can connect.
.\deploy\scripts\start-dev.ps1 -BindLan

# 3. Or start it through the supervisor task
Start-ScheduledTask -TaskName 'MusicHub Dev Supervisor'

# 4. Verify
.\deploy\scripts\verify.ps1
```

The address and the port are not fixed values. `install-server.ps1` and `start-dev.ps1` both call
`deploy/scripts/network.ps1`, which:

1. lists this host's IPv4 LAN addresses and recommends the adapter that carries the default route;
2. asks which address other devices should use when there is more than one;
3. proposes the first free port at or above 5080 and lets the operator type another one in the
   allowed range, refusing a port that is already in use;
4. records the answer in `storage/install.endpoint.json` (gitignored) so the next script in the same
   session reuses it instead of asking again;
5. prints a summary with the loopback URL, the LAN URL, and the exact server address to type into
   the Android and Windows apps.

Automation can skip the questions with `-NonInteractive`, or pin the values with `-ApiPort` and
`-LanAddress`.

Docker:

```powershell
# Detects the LAN address, asks for the dashboard port, generates the secrets,
# builds the dashboard, and starts the stack.
.\deploy\docker\install.ps1
```

`install.ps1` writes `MUSICHUB_LAN_ADDRESS` and `MUSICHUB_LAN_PORT` into `.env`;
`deploy/docker/docker-compose.lan.yml` reads them and fails loudly when either is missing, so the
repository never contains one machine's address. To run compose by hand instead:

```bash
cp .env.example .env        # then fill every CHANGE_ME value, MUSICHUB_PUBLIC_URL, MUSICHUB_PUBLIC_HOSTNAME
docker compose --env-file .env -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.lan.yml up -d --build
```

After the stack is up, the bot answers only after the token is provisioned and the worker is polling;
verify with the worker log (`storage\logs\worker-task.log` / `worker-task.err.log` on Windows,
`docker compose logs worker` on Docker) for `Telegram polling worker started.`

State and logs:

- `storage\logs\supervisor.log` — what the supervisor started and when
- `storage\logs\api-task.log`, `worker-task.log` — host output
- `storage\logs\postgres-task.err.log` — PostgreSQL output

Manual control:

```powershell
Start-ScheduledTask  -TaskName 'MusicHub Dev Supervisor'
Stop-ScheduledTask   -TaskName 'MusicHub Dev Supervisor'
Get-Content .\storage\logs\supervisor.log -Tail 20
```

The supervisor never starts the API until `pg_isready` succeeds. That ordering matters: PostgreSQL
opens its socket while still replaying WAL, and an API that connects in that window exits with
`57P03: the database system is starting up` and does not retry.

### Firewall

The inbound rule **MusicHub Dev 5080** allows TCP 5080 from `LocalSubnet` only. It was created for
the phone to reach the API. If the installer chose a different port, create the rule for that port
instead:

```powershell
New-NetFirewallRule -DisplayName "MusicHub Dev <port>" -Direction Inbound -Protocol TCP `
  -LocalPort <port> -RemoteAddress LocalSubnet -Action Allow
```

Remove it with:

```powershell
Remove-NetFirewallRule -DisplayName 'MusicHub Dev 5080'
```

## User Password Recovery & Admin Notifications

1. **Self-Service Password Reset Request:**
   Users on the Player Web App (`/player/` or port 5081) can click **"فراموشی رمز عبور؟" (Forgot Password?)** on the login page, enter their registered email, and submit.
   The system securely records a `SystemNotification` (type `PasswordReset`) and displays:
   `"درخواست تغییر رمز عبور شما برای مدیریت ارسال شد."`

2. **Admin Notification Center:**
   In the Admin Management Dashboard (`/index.html` or port 5080), administrators receive a visual red alert badge in the left sidebar under **"اعلانات" (Notifications)** and in the top navigation bar.
   Inside the Notifications page:
   - Notifications can be filtered by All, Unread, or Read.
   - Notifications can be sorted by Newest, Oldest, or Unread first.
   - Batch selection and deletion are supported.
   - Clicking **"تغییر رمز عبور کاربر"** opens the password change modal with that user's email pre-filled, allowing the administrator to generate or set a new password and immediately resolve the request.

## Recovering a forgotten administrator password

There is no "forgot password" email. The server stores a PBKDF2-SHA256 hash, so a password can only be
**replaced**, never recovered — and every HTTP route that could replace it (`/api/auth/change-password`,
`/api/users/{id}/reset-password`) requires a session you no longer have. `POST /api/setup/initialize`
refuses once any user exists (409).

The supported recovery path is a command that must be run **on the server**, because the only
credential that may replace an administrator password is access to the host:

```powershell
$env:MUSICHUB_NEW_ADMIN_PASSWORD = 'a-long-new-password-here'
dotnet run --project src/server/MusicHub.Install -- reset-admin-password --email admin@musichub.local
```

The command:

- reads the password from `MUSICHUB_NEW_ADMIN_PASSWORD` or stdin — **never** from an argument, because
  arguments are visible in the process list and retained in shell history;
- never prints the password back;
- rotates `SecurityStamp`, so every live access token stops working;
- revokes all refresh tokens and device sessions for that account;
- clears `FailedLoginCount` and `LockoutEnd`, so a lockout does not survive the reset;
- writes an `admin.password.reset` audit row with a `null` actor (an operator at a shell did this, not a
  signed-in user);
- warns if no active administrator would remain.

The minimum length is 12 characters, the same as the API enforces.

### Docker install

The database is **not** published to the host — that is deliberate, and it means the command above
cannot reach it from the host shell. Run the command inside the compose network instead.

`.env` has no `MUSICHUB_CONNECTION_STRING` key: compose builds it from `POSTGRES_DB`, `POSTGRES_USER`
and `POSTGRES_PASSWORD` and injects it into the containers. Read the string the running stack already
uses, then forward it without ever putting it on a command line:

```powershell
# 1. Build the CLI once, so the DLL exists on disk.
dotnet build src/server/MusicHub.Install

# 2. Take the connection string from the running API container (it is not printed).
$env:MUSICHUB_CONNECTION_STRING = (docker exec musichub-api-1 printenv MUSICHUB_CONNECTION_STRING).Trim()

# 3. Set the new password. It is read from the environment, never from an argument.
$env:MUSICHUB_NEW_ADMIN_PASSWORD = 'a-long-new-password-here'

# 4. Run the CLI in a throwaway container on the compose network, mounting the repo read-only.
#    `-e NAME` with no value forwards the variable from the host, so neither secret
#    appears in the docker command line.
docker run --rm --network musichub_backend `
  -v "${PWD}:/repo:ro" `
  -e MUSICHUB_CONNECTION_STRING `
  -e MUSICHUB_NEW_ADMIN_PASSWORD `
  mcr.microsoft.com/dotnet/aspnet:10.0.9-noble `
  sh -c 'exec dotnet /repo/src/server/MusicHub.Install/bin/Debug/net10.0/MusicHub.Install.dll reset-admin-password --email admin@musichub.local'

# 5. Clear both secrets from the shell.
Remove-Item Env:MUSICHUB_CONNECTION_STRING, Env:MUSICHUB_NEW_ADMIN_PASSWORD
```

Replace `musichub-api-1` if your compose project uses a different name (`docker ps` shows it).

If the network name differs, list it with `docker network ls` — the project's default is
`<project>_backend`.

> `libgssapi_krb5.so.2: cannot open shared object file` on the slim runtime image is a harmless probe
> warning from Npgsql's GSSAPI detection. The connection still succeeds; ignore it.

### Alternative: an explicit connection string

For a **native** install, or any case where the database is reachable from where you run the command,
pass the connection string directly. This also bypasses the data-root credential lookup:

```powershell
$env:MUSICHUB_NEW_ADMIN_PASSWORD = 'a-long-new-password-here'
dotnet run --project src/server/MusicHub.Install -- reset-admin-password `
  --email admin@musichub.local `
  --connection 'Host=...;Port=5432;Database=musichub;Username=...;Password=...'
```

Resolution order is `--connection`, then `MUSICHUB_CONNECTION_STRING`, then the installer-written
credential under `MUSICHUB_DATA_ROOT`.

### What this does not fix

A password reset cannot help if **no active administrator exists**. The API refuses to deactivate or
demote the last active administrator for exactly this reason (`409 At least one active administrator is
required`). If you reach that state anyway, reset the password of an existing account first, then
reactivate an administrator role directly in the database.

## Reporting a bug in this setup

If the phone cannot connect:

1. `Get-NetTCPConnection -State Listen -LocalPort <port>` must show the chosen port listening.
2. `Get-NetFirewallRule -DisplayName "MusicHub Dev <port>"` must be `Enabled`.
3. The network profile must be one the rule covers; the rule is `LocalSubnet` on any profile.
4. `Invoke-RestMethod http://<lan-ip>:<port>/api/health` must return `status: ok`, using the LAN
   address and port the installer printed.
