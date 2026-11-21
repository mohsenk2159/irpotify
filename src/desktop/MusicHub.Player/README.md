# MusicHub Player (Windows)

Native WPF desktop player for MusicHub Enterprise. It signs in with an account created in
the admin dashboard, browses the server library, and streams audio directly from the
MusicHub API over LAN, VPN or an HTTPS tunnel.

## Requirements

- Windows 10 (1809+) or Windows 11, x64.
- .NET 10 Desktop Runtime (the development host already has the portable SDK at
  `%LOCALAPPDATA%\Microsoft\dotnet`).
- Network access to the MusicHub API (default `http://127.0.0.1:5080`).

## Build and run

```powershell
$env:PATH="$env:PATH;$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet build src\desktop\MusicHub.Player\MusicHub.Player.csproj

# Launch with the portable runtime resolved explicitly
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"
.\src\desktop\MusicHub.Player\bin\Debug\net10.0-windows\MusicHub.Player.exe
```

Publish a self-contained build for machines without the .NET runtime:

```powershell
dotnet publish src\desktop\MusicHub.Player\MusicHub.Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\player\win-x64
```

`LibVLCSharp` + `VideoLAN.LibVLC.Windows` ship the native `libvlc` binaries into a
`libvlc\win-x64` folder next to the executable; keep that folder beside the app.

## Signing in

1. Enter the server address. Accepted forms:
   - LAN: `http://192.168.1.20:5080`
   - Domain with TLS: `https://music.example.com`
   - Local development: `http://127.0.0.1:5080`
2. Enter the email and password of a dashboard account.
3. Leave “Remember this device” checked to keep the session across restarts.

The player sends `clientType: "player"` on login. If the account has **Allow player
application** turned off in the dashboard (`canUsePlayer = false`), the server answers
`401` with *“This account is not permitted to use the player application.”* The same
account can still sign in to the web dashboard. An administrator can enable player access
from **Administration → Users → Edit user**.

An account can also carry an **access expiry date**, a **storage quota** and a
**listening-time allowance**. When the date passes or a limit is used up, the server
deactivates the account, revokes the session and the player returns to the sign-in window
with the server’s message. Signing in again is refused until an administrator extends the
date or raises/resets the allowance.

## Features

- Home feed (recently added, recently played, most played, albums), all tracks, albums,
  artists, playlists, favorites and search.
- Album, artist and playlist detail pages with “Play all”.
- Playback: play/pause, previous/next, seek, volume, mute, shuffle, repeat off/all/one,
  queue panel, double-click to play.
- Favorites toggle and playlist management (create, delete, add the current track).
- Keyboard shortcuts: `Space` play/pause, `Ctrl+←/→` previous/next, `Ctrl+F` focus
  search, `Ctrl+Q` toggle queue.
- Formats decoded by LibVLC: FLAC, MP3, M4A/AAC, OGG, OPUS, WAV.
- Listening-time accounting: real played wall-clock seconds are reported to
  `/api/history/playback` every 30 s and on pause, track change, sign-out and exit, so a
  listening allowance is charged for actual playback rather than for seeks or idle time.

## Security notes

- Tokens are stored only in memory and, when “Remember this device” is used, in
  `%APPDATA%\MusicHub\session.dat` encrypted with Windows DPAPI (`CurrentUser` scope).
  There is no plaintext token file and no token in the registry.
- Streaming uses the short-lived `/api/tracks/{id}/stream-token` URL; the player builds
  the URL against the address you typed so an internal proxy host returned by the server
  cannot break playback.
- Access tokens refresh transparently once per request after a `401`; a failed refresh
  clears the session and returns to the sign-in window.
- A `403` from playback reporting means the account lost access (expired, or a quota was
  used up): playback stops immediately and the server message is shown.
- HTTPS certificates are validated by the normal .NET rules. Nothing in the player
  disables or weakens TLS validation.

## Diagnostics

| Path | Contents |
| --- | --- |
| `%APPDATA%\MusicHub\player.log` | Startup, sign-in and crash diagnostics |
| `%APPDATA%\MusicHub\session.dat` | DPAPI-encrypted session (delete to force sign-in) |
| `%APPDATA%\MusicHub\player-settings.json` | Volume, shuffle, repeat, remember-device preference |

The log never contains the password, the access token or the refresh token.

## Connecting to a containerised / remote server

The desktop player only needs an HTTPS (or LAN HTTP) URL to the API. Recommended setups,
with files under `deploy/docker/`:

- **LAN with port forwarding** – publish the API port and connect to
  `http://<public-ip>:<port>`. Plain HTTP exposes credentials, so prefer the TLS proxy.
- **Caddy/nginx reverse proxy on 443** – connect to `https://music.example.com`; the
  player validates the public certificate normally.
- **Cloudflare Tunnel** – terminate TLS at Cloudflare and connect to
  `https://music.example.com`; the tunnel points at the internal API container.
- **Tailscale** – connect to the machine’s tailnet IP or MagicDNS name; no public port
  needs to be open.

Do not disable certificate validation to make a tunnel work — fix the certificate chain
or use the tunnel’s own domain instead.

## Known limitations

- Windows only; there is no macOS/Linux build.
- No offline cache or downloads; playback requires a live connection to the API.
- One playback session per player window; there is no cross-device “remote control”.
- Lyrics, equaliser and gapless album playback are not implemented.
- Gapless queueing is approximate (LibVLC handles track transitions).
