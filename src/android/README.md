# MusicHub Android client

Kotlin + Jetpack Compose client that signs in against the MusicHub server API, streams the
library with Media3/ExoPlayer, and keeps the session on the device until the user signs out.

The app has **no administrative surface**. Library uploads, user management and server
settings belong to the Windows player and the admin dashboard; the Android client only
consumes the public JSON API as a signed-in listener.

## Sign-in

The first screen asks for:

- account email and password (authenticated server-side against `POST /api/auth/login`);
- a **LAN server address** and a **Internet server address**, each with its own port.

At least one address is required. Every filled address is validated and probed with
`GET /api/healthz`; the first healthy candidate is used, with LAN tried before Internet so a
device on the home network prefers the local server. Both values are saved on the server profile
(`ServerProfile.lanBaseUrl` / `internetBaseUrl`), and **Settings → server health check** re-probes
them, moving the active URL to whichever endpoint currently answers. The app therefore reconnects
without asking again and survives moving between the home network and the internet.

Successful sign-in stores the refresh token in `EncryptedSharedPreferences`
(Android Keystore-backed) and keeps the access token in memory only. The password itself is
never persisted. The session survives app restarts and process death until the user taps
**Sign out**.

Cleartext HTTP is allowed only when the local-network switch is on **and** the host is a
loopback/private address; TLS verification is never disabled.

## Screens

| Screen | Contents |
| --- | --- |
| Home | Recently added, recently played, most played, favorites, albums |
| Search | Debounced search across tracks, albums, artists and folders |
| Library | Tracks, albums (with album detail and "play all"), artists, playlists |
| Now playing | Artwork metadata, seek bar, previous/play-pause/next, shuffle, repeat, queue |
| Settings | Server address and health check, change password, HTTP/Wi-Fi toggles, language, theme, sign out |

Language (English/Persian) and theme (system/light/dark) are stored in DataStore and applied
at startup; Persian switches the layout to RTL.

## Change password

Settings → **Change password** posts to `POST /api/auth/change-password`. The server verifies
the current password, rehashes with PBKDF2-SHA256 (210 000 iterations), rotates the security
stamp, revokes every refresh token, and returns a fresh token pair for this device — so the
user stays signed in here while other devices are signed out.

## Offline behaviour

The library, favorites, history and queue are cached in Room. Downloaded tracks play from
local files through the same Media3 session. Pending favorite and playback reports are queued
and replayed by WorkManager when connectivity returns.

## Build

Gradle 8.9 cannot run on the Java 25 bundled with recent Android Studio releases (it fails with
a bare version error and no stack trace), so build with a JDK 17.

```powershell
$env:JAVA_HOME = "C:\Program Files\Microsoft\jdk-17.0.21.21-hotspot"
cd src\android
.\gradlew.bat :app:assembleDebug
```

Output: `src\android\app\build\outputs\apk\debug\app-debug.apk` (~19.6 MB, debug-signed).

Install and launch on an emulator:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb install -r app\build\outputs\apk\debug\app-debug.apk
& $adb shell am start -n com.musichub.mobile/.MainActivity
```

The emulator reaches an API bound to the host loopback at `http://10.0.2.2:5080`; enable
**Allow HTTP on a local/private network** on the sign-in screen for that to be permitted.

`local.properties` points at the machine's Android SDK and is gitignored.
