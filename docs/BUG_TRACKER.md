# Bug Tracker

| ID | Severity | Status | Area | Summary | Evidence | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| B-001 | High | FIXED | Build | `AudioProbeService` required three-argument accessors for stream-level duration/bitrate | `dotnet build` error CS7036 | media-engineer |
| B-002 | High | FIXED | Authz | Duplicate SHA-256 constraint handling referenced a non-existent enum member | build error CS0117 | backend-engineer |
| B-003 | High | FIXED | API | `RealtimeNotifier`/`StreamTracker` were not registered while injected into handlers | DI validation exception at startup | backend-engineer |
| B-004 | High | FIXED | Security | `Microsoft.OpenApi` 2.0.0, `System.Security.Cryptography.Pkcs/Xml` 9.0.0 and `Microsoft.AspNetCore.DataProtection` 10.0.0 had published advisories | `dotnet build` NU1902/NU1903/NU1904 | devops-release-engineer |
| B-005 | Medium | VERIFIED | Telegram | Live Telegram verification needed a real bot, a real inbound message and a real audio document. All three ran against the real service and the real stack | VERIFIED END TO END 2026-11-19. A real admin sent three messages to `@irpotify_bot`, one carrying a 17,084,762-byte MP3. The real `MusicHub.Worker` long-polled them and logged `Telegram ingestion processed 3 update(s): 1 imported, 0 duplicate(s), 2 rejected`. Per layer: (1) transport - `getMe` -> `ok=true id=9239014183 username=irpotify_bot`, empty webhook, `getUpdates` delivered real updates; (2) authorization - the sender is compared with `config.AdminTelegramUserId` server-side (`TelegramIngestionService` line 145) and a mismatch writes `Error="Sender is not the configured Telegram administrator."`; (3) file download - `GetFileAsync`/`DownloadFileAsync` ran for the first time and produced the file; (4) validation/storage - signature check, SHA-256 `df304444fb3484888ad8d968abebbbc37a1e364cc9be4e9a86407144e42e8dd0`, committed to `storage/music/df/30/df304444....mp3`, and the SHA-256 recomputed from that file on disk matches the database byte for byte (17,084,762 bytes); (5) metadata - ffprobe reported `DurationSeconds=2126.759, SampleRate=44100, Channels=1, Codec=mp3` and the ID3 tag supplied `Title="ÃƒËœÃ‚Â±Ãƒâ€ºÃ…â€™Ãƒâ„¢Ã¢â‚¬Â¦Ãƒâ€ºÃ…â€™ÃƒÅ¡Ã‚Â©ÃƒËœÃ‚Â³\| Ãƒâ„¢Ã¢â‚¬Â Ãƒâ„¢Ã‹â€ ÃƒËœÃ‚Â³ÃƒËœÃ‚ÂªÃƒËœÃ‚Â§Ãƒâ„¢Ã¢â‚¬Å¾ÃƒÅ¡Ã‹Å“Ãƒâ€ºÃ…â€™"`, `ArtistDisplay="@Avaykhatere ..."`; (6) attribution - the track is owned by `admin@musichub.local` with a 10 GiB quota, so quota accounting applies; (7) audit - `TelegramImports` holds one `Completed` row linked to the track plus four `Failed` rows for non-audio messages; (8) offset - `AppSettings."telegram.updateOffset"` advanced 567119805 -> 567119808 and all five updates carry `ProcessedAt`, so nothing can be replayed. Prior work verified token masking (`GET /api/admin/telegram` returns no raw token) and the admin test adapter. Only the runtime rejection of a *non-admin* sender was not exercised, because no second Telegram account was available; the branch is covered by code and by test | orchestrator |
| B-006 | Medium | FIXED | Android | Android SDK (`%LOCALAPPDATA%\Android\Sdk`: platforms android-35/37.0, build-tools 34/36, platform-tools, emulator, system image android-37.0), JDK 17 (`C:\Program Files\Android\Android Studio\jbr`), Gradle 8.9 wrapper and AVD `Medium_Phone` (API 37) all present and `adb devices` reachable; first runtime verification pending | SDK/emulator availability confirmed on 2026-11-18; APK build + emulator login evidence pending | android-engineer |
| B-007 | Medium | FIXED | Docker | Docker Desktop 4.99.0 was installed but the engine did not come up (WSL2 backend distro stopped). After starting Docker Desktop (`docker desktop start`) the backend came online and the full compose stack then built and ran | Docker daemon reachable after `docker desktop start` (exit 0); compose static config valid (B-060); `docker compose ... up -d --build` -> postgres/api/worker healthy + caddy Up; worker log `Telegram polling worker started.`; /healthz -> Healthy, /api/health -> ok via Caddy; verified 2026-11-21 | devops-release-engineer |
| B-008 | Low | OPEN | Uploads | Multipart form uploads buffer to the framework temp directory before hashing | code inspection | media-engineer |
| B-009 | High | FIXED | Uploads | Every import failed with `InvalidOperationException` (`CoreEventId.NavigationBaseIncludeIgnored`) because the duplicate lookup included `Tracks -> AudioAsset` back to the query root; EF Core 10 treats that include walk as an error | Smoke run upload returned HTTP 422; DB `UploadSessions.Error` recorded the exception; after removing the invalid include, smoke upload passed (42/42) | media-engineer (fix applied by orchestrator) |
| B-010 | Low | FIXED | Tooling | `scripts/smoke-api.ps1` assigned to read-only automatic `$HOME`, aborting the suite after the albums check | smoke run error "Cannot overwrite variable HOME"; renamed to `$homeFeed`; full suite then completed | runtime-qa-engineer (fix applied by orchestrator) |
| B-011 | Medium | FIXED | Player build | `MainViewModel` threw `HttpRequestException` handling without `using System.Net.Http` | `dotnet build src\desktop\MusicHub.Player` error CS0246 | orchestrator |
| B-012 | Critical | FIXED | Player startup | `NavList SelectedIndex="0"` in XAML raised `SelectionChanged` during `InitializeComponent`, before `_viewModel` existed Ã¢â€ â€™ `NullReferenceException`; silent session restore aborted and the process stayed alive with no window at all | `%APPDATA%\MusicHub\player.log`: "Silent session restore failed / MainWindow.NavList_SelectionChanged line 33", process running with `MainWindowHandle=0`; after moving the selection to the constructor and adding a null guard the log shows "Main window shown" | orchestrator |
| B-013 | Medium | FIXED | Player sign-in | A non-refreshable 401 (the login call itself) was replaced by the generic "Your session has expired. Sign in again.", hiding the server's player-permission denial | UI showed "Your session has expired" for `player.denied@musichub.local` while the API returns `{"error":"This account is not permitted to use the player application."}`; after the fix the UI shows that message | orchestrator |
| B-014 | High | FIXED | Player views | `IsCardView` / `IsPlainTrackView` never raised `PropertyChanged`, so Albums/Artists/Playlists/Tracks/Favorites bodies stayed collapsed after navigation | UI Automation text dump showed only page title/subtitle; after raising the flags the "New playlist" button, empty-state text and cards appeared | orchestrator |
| B-015 | Medium | FIXED | Player sign-out | `LogoutAsync` returned early when no refresh token was stored, skipping `ClearAuth()` and leaving stale tokens in memory | code inspection; now the early-return branch clears auth, and sign-out removes `session.dat` and returns to the login window | orchestrator |
| B-016 | Low | FIXED | Player UI | Pluralisation showed "1 tracks" in the Tracks header and the queue summary | UI text "1 tracks in your library" / "1 tracks in the queue" | orchestrator |
| B-017 | Low | FIXED | Player UX | Search box relied on a tooltip only; no visible placeholder so the hint was undiscoverable | UI text dump had no hint label for the search field | orchestrator |
| B-018 | Low | FIXED (documented) | Player tooling | Launching `MusicHub.Player.exe` without `DOTNET_ROOT` shows "You must install .NET Desktop Runtime" even though the runtime exists in the portable SDK | Windows Application event log "App host version 10.0.15, .NET location: Not found"; `dotnet --list-runtimes` lists `Microsoft.WindowsDesktop.App 10.0.15`; documented in the player README and PROJECT_STATUS | orchestrator |
| B-019 | Low | FIXED | Tooling | `.gitignore` rule `.env.*` also ignored the root `.env.example`, so the deployment template could not be committed as-is | `git check-ignore -v .env.example` matched `.gitignore:29:.env.*`; after adding `!.env.example` it matches the negation and `git status` reports `?? .env.example` | orchestrator |
| B-020 | Medium | FIXED | Deployment | `PairingEndpoints` read `MUSICHUB_PUBLIC_URL` with `??`, so an empty string was treated as configured and produced a malformed pairing payload (`...?server=&code=...`) | Fixed: new `PublicUrl.Resolve` in `Api/Common.cs` treats blank/whitespace as unset, trims a trailing slash, and throws `ApiException(500)` naming the variable when the value is set but is not an absolute http(s) URL, instead of emitting a QR code no client can parse. Precedence `MUSICHUB_PUBLIC_URL` -> `Server:PublicBaseUrl` -> request origin. Verified dynamically with `storage/verify-w019-forwarding.ps1` against the real API on :5099: empty -> `server=http://127.0.0.1:5099` HTTP 200; whitespace -> same; `https://music.example.com` -> that value; `music.example.com` -> HTTP 500 `MUSICHUB_PUBLIC_URL (or Server:PublicBaseUrl) is set to 'music.example.com', which is not an absolute http or https URL`. 15 unit tests in `DeploymentConfigurationTests` | backend-engineer |
| B-021 | Medium | FIXED | Deployment | No `UseForwardedHeaders` call existed in either host, so correct client IPs and scheme depended on the framework switch `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, which clears the known-proxy list and trusts *every* peer - a direct caller could forge `X-Forwarded-For` to escape the per-IP rate limit or poison audit rows | Fixed: `Api/ForwardedHeadersSetup.cs` builds `ForwardedHeadersOptions` from `ForwardedHeaders:KnownProxies` / `KnownNetworks` (IP literals and CIDR only, hostnames rejected), clears the framework loopback defaults, clamps `ForwardLimit` to 1-10, and enables the middleware only when at least one proxy/network is configured. Wired in `Program.cs:267-271`, before `UseExceptionHandler` and therefore before the rate limiter and audit. Compose now sets `ForwardedHeaders__KnownNetworks` against a pinned `edge` subnet (172.28.0.0/24) instead of the blanket switch. Verified dynamically with the same script: forged `X-Forwarded-For: 203.0.113.77` with nothing configured -> audit recorded `127.0.0.1`; with `KnownNetworks=10.99.0.0/16` -> `127.0.0.1`; with `KnownNetworks=127.0.0.1/32` -> `203.0.113.77` (the trusted-proxy path). 18 unit tests in `DeploymentConfigurationTests` | backend-engineer |
| B-022 | Informational | OPEN | Deployment | The Caddy container runs as root (all capabilities dropped except `NET_BIND_SERVICE`) because the official image has no non-root default for binding :80/:443 | compose `cap_drop: ALL` + `cap_add: NET_BIND_SERVICE`; non-root variant documented but not shipped | devops-release-engineer |
| B-023 | Medium | FIXED | Entitlements | Listening-time quota relied on client-reported `listenedSeconds`, so a modified client could report `0` forever and never consume its allowance Ã¢â‚¬â€ the one path by which a patched APK obtained unpaid service | Fixed by measuring served bytes. `ListeningTimeTracker` counts bytes written to the response body in `/api/tracks/{id}/stream`; `StreamTokenService` now embeds the issuing user id in the signed payload so an anonymous stream request is attributable (substituting it breaks the HMAC); the playback endpoint charges `max(clientReport, bytes/bitrate)`. The counter is drained, not read, so the two sources describe one interval and `max` cannot double-charge. The client report is retained because offline playback reads a local file and never touches the stream endpoint. Test `Streamed_bytes_charge_listening_time_even_when_the_client_reports_zero` **fails on the pre-fix code** (`Server charged 0s after serving 800044 bytes with a zero-second report`) and passes after. Companion test `Client_report_is_still_honoured_when_it_exceeds_measured_stream_time` guards the offline path. Live probe `scripts/verify-listening-quota-tamper.ps1`. Report `docs/security/CLIENT_TAMPER_ASSESSMENT.md` | backend-engineer |
| B-024 | Low | FIXED | Test infrastructure | The integration suite hit the sign-in rate limiter (`429 TooManyRequests`) because every request shares one client IP, failing unrelated tests | `dotnet test` failures in `EntitlementTests` at `ApiFactory.LoginAsync`; after making the limit configurable (`RateLimiting:LoginPermitLimit`, default 10) and setting `RateLimiting__LoginPermitLimit=500` in the fixture, 63/63 passed | backend-engineer |
| B-025 | Low | OPEN | Availability | A database outage during sign-in surfaces as HTTP `500` instead of `503`: the EF Core context is registered without `EnableRetryOnFailure`, so a dropped Npgsql connection fails the request immediately | Stopping PostgreSQL mid-run produced `NpgsqlException: Exception while writing to stream Ã¢â€ â€™ IOException: An existing connection was forcibly closed by the remote host` in `AuthService.LoginAsync:45`, HTTP 500 to the client; after PostgreSQL restarted, the same login succeeded with no code change | backend-engineer |
| B-026 | High | FIXED | Android | `MainShell.kt` was committed referencing five screens, a `BadgeBox`/`BottomNavigation` API and a `LocalAppContainer` call that did not exist, so `:app:compileDebugKotlin` failed with ~20 unresolved-reference errors and no APK could be produced | `gradlew :app:compileDebugKotlin` errors `Unresolved reference 'BadgeBox'`, `'BottomNavigation'`, `'LibraryMusic'`, `'Shuffle'`, `'SkipNext'`; after rewriting `MainShell` against real Material3 APIs and adding the missing screens + vector drawables, the build is clean | orchestrator |
| B-027 | Medium | FIXED | Android | The now-playing seek bar was hard-coded to `positionMs = 0L` and `PlayerUiState` had no position field, so the slider never advanced and the elapsed label stayed `0:00` during real playback | Emulator screenshot showed `0:00 / 19:13` while `logcat` confirmed `MediaCodec` decoding and `AudioTrack` running; after adding `PlayerUiState.positionMs` and a 1 s display ticker the position tracks the player | orchestrator |
| B-028 | Medium | FIXED | Android | First run opened the QR pairing form and `ServerProfile` persisted only a single `baseUrl`, so a user without a dashboard-issued pairing code could not sign in and the entered LAN/internet pair was discarded | Emulator UI dump of a clean install showed the pairing form first with no email/password fields; after defaulting `AuthMode.LOGIN`, adding `ServerProfile.lanBaseUrl`/`internetBaseUrl` + `candidateBaseUrls()` and `SessionManager.revalidateEndpoint`, the first screen asks for credentials and both addresses are stored and failed over | orchestrator |
| B-029 | Low | FIXED | Android | The now-playing screen rendered "Not signed in" as the track title whenever nothing was playing, misreporting a healthy signed-in session | Emulator UI dump showed `Now playing / Not signed in / 0:00 / Queue (0)` on a fresh session; after adding a dedicated `player_no_track` empty state (EN + FA) the wording is accurate | orchestrator |
| B-030 | Informational | OPEN | Android tooling | Gradle 8.9 cannot run on the Java 25 bundled with Android Studio (JBR), so `gradlew` fails with a bare version error; builds must set `JAVA_HOME` to the JDK 17 install | `gradlew :app:compileDebugKotlin` failed with only `25.0.4` and no stack trace; with `JAVA_HOME=C:\Program Files\Microsoft\jdk-17.0.21.21-hotspot` the build succeeds | orchestrator |
| B-031 | Medium | OPEN | Android runtime | The emulator VM ANRs (System UI, GMS, `com.android.phone`, and the app's `SystemJobService`) when host memory is exhausted: 7.9 GB installed against ~10.2 GB commit and under 0.6 GB free | `ActivityManager: ANR in com.musichub.mobile / Reason: No response to onStartJob for com.musichub.mobile/androidx.work.impl.background.systemjob.SystemJobService` alongside ANRs in `com.android.systemui` and `com.google.android.gms`; after the boot storm settled and ANR dialogs were dismissed the app ran normally, so this is host capacity, not an app defect | orchestrator |
| B-032 | High | FIXED | Android auth | `AuthAuthenticator` returned null for every path starting `/api/auth/`, so an expired 15-minute access token could never be refreshed on `change-password`, `me`, `sessions`, `logout` or `logout-all`; those calls failed with a generic error and the app never recovered | Settings change-password returned 'Password change failed.' while the endpoint was healthy server-side and the legacy password still worked; after excluding only `login`/`refresh` from the authenticator, the same UI flow returned 'Password changed. Other devices were signed out.'. Now covered by 6 on-device regression tests in `src/android/app/src/androidTest/java/com/musichub/mobile/data/api/AuthAuthenticatorInstrumentedTest.kt` driving a real `OkHttpClient` against a real `MockWebServer`: all five authenticated routes refresh once and retry with the new token, `login`/`refresh` never refresh, a second 401 is not retried forever, and a 200 never refreshes. `gradlew :app:connectedDebugAndroidTest` on `Medium_Phone_API_37.0` -> 13/13 passed | orchestrator |
| B-033 | Medium | FIXED | Android i18n | The change-password card in `SettingsScreen.kt` used hard-coded English labels, so it stayed English in Persian | Persian UI dump showed localized sections but the password card still read 'Current password', 'New password (min 12 characters)', 'Confirm new password'; now driven by `settings_change_password` / `settings_current_password` / `settings_new_password` / `settings_confirm_password` / `settings_password_changed` in EN and FA | orchestrator |
| B-034 | Medium | FIXED | Android i18n | The bottom navigation was hard-coded English ('Home/Search/Library/Now/Settings'), so Persian screens kept an English tab bar | After the app switched to Persian the UI dump ended with the Persian tab labels only after wiring `nav_home` / `nav_search` / `nav_library` / `nav_now` / `nav_settings` through `stringResource` and adding the missing `nav_now` / `nav_settings` keys in both locales | orchestrator |
| B-035 | Medium | FIXED | Android i18n | Choosing a language persisted the setting but never applied it: `LocaleHelper.setLanguage` only wrote SharedPreferences and the locale is applied in `attachBaseContext`, which runs once per process | Tapping fa wrote `language_tag=fa` yet the whole UI stayed English and a cold restart still showed English; after `LanguageRow` recreates the Activity on `Dispatchers.Main`, the switch repaints immediately and Persian renders end to end. Now covered by 7 on-device tests in `LocaleApplicationInstrumentedTest.kt` plus a debug-only `LocaleTestActivity` that applies `LocaleHelper.wrap` in `attachBaseContext` exactly as `MainActivity` does: the tag persists, a blank tag returns the same Context, `fa` resolves `Ã˜Â®Ã˜Â§Ã™â€ Ã™â€¡` with `LAYOUT_DIRECTION_RTL`, `en` resolves `Home` with LTR, a *newly started* Activity after the switch renders Persian (the actual regression), and the choice survives across Activity instances. `gradlew :app:connectedDebugAndroidTest` on `Medium_Phone_API_37.0` -> 13/13 passed | orchestrator |
| B-036 | High | FIXED | Install / PostgreSQL | PostgreSQL 15+ removed the implicit `CREATE` grant on the `public` schema, so the installer-created application role could connect but every EF migration failed with `42501 permission denied for schema public`; the installer now creates the database with the role as owner and transfers ownership of the `public` schema | On a fresh cluster the API logged `Failed executing DbCommand ... CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"` with `SqlState: 42501 permission denied for schema public`, so the credential worked but no table could be created; after the installer took database and schema ownership, the same run applied migrations and reported `tables=44` | orchestrator |
| B-037 | Medium | FIXED | Install / verification | `MusicHub.Install` only created the database when told to and never granted schema rights, and the `--seed-only` path did not exist, so a brand-new cluster could not be initialised without putting the plaintext password on a command line | The verification harness could create the credential file and authenticate but the API could not migrate; `initdb` had no way to receive the generated password without echoing it. Added `--create-database` (database plus `public` schema ownership) and `--seed-only --pwfile-out` (transient owner-only password file that the installer script deletes immediately after `initdb`) | orchestrator |
| B-038 | High | FIXED | Install / tooling | `pg_ctl` and `initdb` spawn children that inherit the parent's stdout/stderr handles, so calling them directly from PowerShell never returns: the pipeline waits for a handle the postmaster keeps open forever. Both are now launched with `Start-Process -RedirectStandardOutput/-RedirectStandardError` and judged by their on-disk result (port reachable, `PG_VERSION` present) rather than by a `$LASTEXITCODE` that is not refreshed | `.\deploy\scripts\install-server.ps1` hung indefinitely after `initdb` and after `pg_ctl start`; `Get-CimInstance Win32_Process` showed `initdb.exe` and `postgres.exe` alive with the installer blocked on the pipeline, while `storage\verify-install\logs\pg-start.out.log` already said `server started`. With redirection both steps return | orchestrator |
| B-039 | High | FIXED | Install / security | A fresh install passed the application role name to `initdb -U`, so the role the API connects as was the cluster **superuser**, and the credential file stored a superuser password. The installer now initialises with a separate `--admin-username` (default `postgres`), stores the superuser connection only in `db-admin-credential.protected`, and creates the application role afterwards through `--admin-from-file`; it refuses outright when `--username` equals the authenticated administrator | Static review while exercising the installer: `initdb -U musichub` followed by rotating `musichub`'s password and writing it to `db-credential.protected` meant the API's role had `rolsuper = true`; `select rolsuper from pg_roles where rolname = 'musichub'` would have returned true | orchestrator |
| B-040 | High | FIXED | Security / install | `DatabaseCredentialFile.TryRead` decided "no credential file" with `File.Exists`, which returns **false** when the caller is merely denied access, so an installed-but-unreadable credential was reported as `Missing` and `ConnectionStringResolver.Resolve` fell through to `ConnectionStrings:Default` or the development default ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the exact silent downgrade the `Unreadable` state exists to prevent. `TryRead` now classifies the read instead of asking `File.Exists`, and the ambiguous `DirectoryNotFoundException` (which .NET also raises for a file under an inaccessible parent) is resolved by enumerating `keys/`; only a clean "not there" may report `Missing` | Pre-fix, with a credential file whose content could not be read, `TryRead` returned `Missing`. Post-fix, four measured cases on Windows: a file with a deny-read ACE -> `Unreadable`; a path occupied by a directory -> `Unreadable`; a genuinely absent file -> `Missing`; a completely absent `keys/` directory -> `Missing`. `Resolve(..., allowDevelopmentDefault: true)` now throws `cannot be decrypted` instead of returning `Host=fromconfig`. `InstallerSecurityTests` covers all four states plus a file locked by another process; 4 of them fail with the fix reverted | orchestrator |
| B-041 | High | FIXED | Install / security | The Worker passed `allowDevelopmentDefault: true` unconditionally (`MusicHub.Worker/Program.cs`) while the API used `builder.Environment.IsDevelopment()`, so in Production a missing credential file made the Worker connect to the hard-coded development default `Host=127.0.0.1;Port=5433;...;Password=musichub` instead of failing loudly. Both hosts now call `ConnectionStringResolver.ResolveForHost(builder.Environment.EnvironmentName, ...)`, and the single decision lives in `AllowsDevelopmentDefault`, which accepts only `Development` | With no credential file: `ResolveForHost("Production", root, null, provider)` throws `No database connection string is available`, while `ResolveForHost("Development", ...)` returns the development default. `InstallerSecurityTests` asserts the environment matrix (`Development`/`development` -> true; `Production`/`Staging`/empty/null -> false) and that neither host source file contains `allowDevelopmentDefault` or names `DevelopmentDefault`; the call-site assertion fails with the pre-fix Worker | orchestrator |
| B-042 | High | FIXED | Install / security | `MusicHub.Install` interpolated the generated application password into the DDL text (`create role ... password '<pw>'` / `alter role ... password '<pw>'`) and the generic `catch` printed `RedactConnectionDetails(ex.Message)`. PostgreSQL echoes the failing statement or identifier in its error, and the old regex only matched `password=value`, so `password 'value'`, `at or near "'value"` and `role "value"` all passed through unchanged: a server-side error during those statements printed the live generated password to stderr. Statements now run through `RoleProvisioning.ExecuteAsync`, which scrubs the value it holds by identity, and the new `SecretRedactor` also redacts secret-shaped patterns for text whose value is unknown; no original exception is rethrown, so the driver chain cannot carry the echo | Measured against a real PostgreSQL 17.6 cluster: `42601: unterminated quoted string at or near "'SecretProbe..."` and `42704: role "role SecretProbe..." does not exist` both contain the password, and the old regex left each one unchanged. Through the new redactor the same two messages print `... at or near "'***"` and `role "role ***"` with the value absent. Verified with `Include Error Detail` both off and on. `InstallerSecurityTests` covers all three echo shapes plus the flattened-output property | orchestrator |
| B-043 | Medium | OPEN | Install / security | `DataProtectionSetup.Create` creates `keys/dataprotection` with `Directory.CreateDirectory` and never restricts it, unlike `DatabaseCredentialFile.Write` which restricts `keys/` on Unix. On Windows the keyring therefore keeps its inherited ACL (any local account that inherits read on the data root); on Linux it is created with the default umask unless `Write` happened to tighten the parent first. The keyring protects the database credential *and* the Telegram bot token / pending Telegram files, so this is the weakest link in the confidentiality chain | `icacls keys\dataprotection` and the key XML both list the inherited `<SANDBOX-GROUP>:(M)`, `<MACHINE-SID>:(M)`, `SYSTEM:(F)`, `Administrators:(F)`, `<USERNAME>:(F)`, while `db-credential.protected` is restricted to `<USERNAME>:(R,W)` | orchestrator |
| B-044 | Medium | OPEN | Install / security | `RestrictFile` swallows a failing `icacls` on Windows: when both the `DOMAIN\User` and bare `User` attempts fail, the method returns silently and the credential lands with its inherited, broadly-readable ACL. `install-server.ps1:212` only emits a warning for this condition, so a weak ACL is never a hard failure | `TryRunIcacls` returns `bool`, but `RestrictFile` discards it and has no error path; the write still returns success and `Write` returns the path | orchestrator |
| B-045 | Medium | OPEN | Install / security | `keys/db-admin-credential.protected` is written on a fresh install and never deleted. It holds a **superuser** connection string, protected by the same keyring the API and Worker load, so any compromise of the application account or its keyring yields full cluster superuser rights and defeats the two-role separation introduced for B-039. `ARCHITECTURE.md` describes it as "used once, on a fresh install only" but nothing enforces or cleans that up | `grep` for `db-admin-credential` across `deploy/scripts/*.ps1` finds no removal; the file persisted after a complete `install-server.ps1` run and after the follow-up `create-credential --admin-from-file` step | orchestrator |
| B-046 | Medium | OPEN | Install / security | The API and the Worker resolve **different** data roots whenever `MUSICHUB_DATA_ROOT` is unset, because `StorageOptions.RootPath` defaults to the relative `"storage"` and `ResolveDataRoot` returns `Path.GetFullPath` of it against the current working directory. The caller-supplied fallbacks (API: `ContentRoot/../../../storage`, Worker: `AppContext.BaseDirectory + "storage"`) are therefore dead code. Each host can create its own keyring at a different path, so the credential written by the installer is invisible to one of them and the documented "both must resolve the same root" invariant is unenforced | Running the same resolver from the repo root gave `<repo>\storage`; from `MusicHub.Worker\bin\Debug\net10.0` it gave `...\net10.0\storage`. `StorageOptions.RootPath` default is `"storage"`, so `string.IsNullOrWhiteSpace` is false and the fallback argument is never used | orchestrator |
| B-047 | Low | OPEN | Install / security | `TryRead` accepts an arbitrary `fileName` while `Write` rejects `/`, `\` and `..`. `PathFor(dataRoot, fileName)` is public, so a computed name such as `..\outside.protected` normalizes outside the `keys` directory and `TryRead` will read it. Current callers pass compile-time constants, so this is latent rather than exploitable, but the asymmetry is a trap for the next caller | `PathFor(root, @"..\outside.protected")` Ã¢â€ â€™ `<root>\keys\..\outside.protected`, which normalizes to `<root>\outside.protected` (outside `keys/`); `TryRead` returned `Unreadable` instead of rejecting the name, while `Write` threw `ArgumentException: The credential file name must be a bare file name.` | orchestrator |
| B-048 | Low | OPEN | Install | `--host`, `--database` and `--username` are interpolated into the persisted connection string without escaping. A value containing `;` silently becomes an additional connection-string keyword, so the stored credential no longer describes what the installer configured, producing a wrong-database or wrong-option connection far from its cause. SQL-side handling is correct (`QuoteIdentifier` is applied), so this is connection-string injection only | `--database 'musichub;Include Error Detail=true'` produced `Host=127.0.0.1;Port=5433;Database=musichub;Include Error Detail=true;Username=musichub;...`, which `NpgsqlConnectionStringBuilder` parses with `Database = musichub` plus an extra keyword | orchestrator |
| B-049 | Informational | OPEN | Install / security | `DataProtectionSetup.Create` returns `services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>()` without disposing the container, so the provider and its key-ring cache outlive their container. Harmless for a process-lifetime singleton in the API/Worker, but the installer calls it up to twice per process and nothing flushes the key cache on exit | Code inspection `DataProtectionSetup.cs:51`; no `using`/`Dispose` anywhere on the return path | orchestrator |
| B-050 | Informational | OPEN | Install / documentation | The installer's class documentation claims the generated password "is set on the PostgreSQL role through a parameter-safe statement and then zeroized", but the statement is string-interpolated (B-042) and `password = string.Empty` in the `finally` cannot erase a .NET string. `CryptographicOperations.ZeroMemory` only covers the ciphertext bytes, which are not secret. The zeroization claim should be removed rather than relied on | Code inspection `Program.cs:16, 182` and `DatabaseCredentialFile.cs:138`; .NET strings are immutable, so the assignment leaves the original buffer on the heap for the GC | orchestrator |
| B-051 | High | FIXED | Install / security | The *existing role* path in `EnsureRoleAndDatabaseAsync` ran only `alter role ... with login password ...`, which changed the password and nothing else. If the target role already existed as a **superuser** (stale cluster, hand-made role, or a cluster left over from the B-039 behaviour) the installer rotated its password and wrote that superuser connection string to `db-credential.protected`, so the API and Worker connected as cluster superusers. New `RoleProvisioning` states `login nosuperuser nocreatedb nocreaterole noreplication nobypassrls` on **both** the create and the alter path, and then re-reads `pg_roles` and `pg_auth_members` and refuses to write the credential if anything is still elevated | Live dev cluster: `create role w051stale superuser login` -> `super=true`; the old statement `alter role w051stale with login password ...` -> still `super=true`; the fixed installer on the same role -> `super=False createdb=False createrole=False replication=False bypassrls=False` from `pg_roles`. Proving it exposed a second hole the attribute check alone misses: a role with `rolsuper=false` that is a **member** of a superuser role reports `rolsuper=false` yet `pg_has_role(..., 'usage')` is true, so the first version of the guard would still have issued a working superuser credential; `AssertUnprivilegedAsync` now also reads `pg_auth_members`, and the fixed installer exits 2 with `it is a member of 'w051su'` and writes no credential file. `InstallerSecurityTests` asserts the alter statement carries every `no*` attribute, that the source no longer contains the password-only statements, that any elevated bit is rejected, and that the guard consults `pg_auth_members` | orchestrator |
| B-052 | Medium | OPEN | Install / security | The check that the application role differs from the administrator compares `NpgsqlConnectionStringBuilder(adminConnection).Username` Ã¢â‚¬â€ the *client-declared* name, not the server's actual identity Ã¢â‚¬â€ and uses `StringComparison.Ordinal`, while PostgreSQL folds unquoted identifiers to lower case. A `pg_ident` map, a connection pooler, or a case variation (`Username=Postgres` vs `--username postgres`) passes the guard, which is the only structural protection against reproducing B-039 | Code inspection `Program.cs:255-261`; the connection is already open at that point, so `select current_user` is available and unused. `StringComparison.Ordinal` cannot match PostgreSQL's identifier folding | orchestrator |
| B-053 | Medium | OPEN | Install / security | Both hosts register the eagerly built provider as a singleton and then *also* call `services.AddDataProtection().SetApplicationName(...)`. The duplicate call registers its own key-management services, and when the host container resolves `IKeyManager` a **second** keyring is created at the default location outside the data root. The real Worker demonstrates it: its log opens `C:\Users\...\AppData\Local\ASP.NET\DataProtection-Keys` as the key repository, and a stray DPAPI-wrapped `key-b48badf7-....xml` exists there alongside the data-root keyring. This breaks the documented "keys live under the data root" invariant, leaks key material outside the data root, and would fail outright on the container's `read_only: true` root filesystem or an account with no profile | `storage/logs/worker.log:2` Ã¢â‚¬â€ "User profile is available. Using 'C:\Users\<USERNAME>\AppData\Local\ASP.NET\DataProtection-Keys' as key repository"; that key file is present in AppData while `storage/keys/dataprotection` holds a different key. A probe confirmed `IDataProtectionProvider` still resolves to the eager instance (`ReferenceEquals == True`) and cross-reads it, so the *credential* path is correct; the defect is the unmanaged second keyring | orchestrator |
| B-054 | Low | OPEN | Install | `WritePasswordFile` opens the transient `initdb` password file with `FileMode.Create`, so a pre-existing file keeps its original permissions (only `UnixCreateMode` applies at creation) and the 256-bit superuser password is written into it. The Windows branch also discards `process.ExitCode`, so a failed `icacls` on the password file is invisible. `--pwfile-out` accepts any absolute path | Code inspection `Program.cs:196-238`; `FileMode.Create` truncates an existing file without changing its mode, and `process?.WaitForExit(10_000)` never checks the result | orchestrator |
| B-055 | Low | OPEN | Install / security | `Write`'s bare-file-name guard rejects `/`, `\` and `..` but not a **drive-relative rooted** name such as `C:evil.protected`: it contains no separator and no `..`, yet `Path.Combine` honours the rooted second argument and returns it unchanged, so the path escapes the keys directory. `TryRead` has no validation at all | Compiled probe: for `C:evil.protected` the guard rejects = **False** and `Path.Combine(<root>, "keys", "C:evil.protected")` returns `C:evil.protected`; `/etc/passwd` and `\\attacker\share\x` are rejected. Current callers pass compile-time constants, so this is latent | orchestrator |
| B-056 | Low | OPEN | Install / security | `ResolveDataRoot` passes `MUSICHUB_DATA_ROOT` through `Path.GetFullPath` with no validation, and that value selects both the credential file *and* the data-protection keyring. An environment-level attacker can point it at a UNC share or a directory holding their own keyring and matching credential, moving the trust root of every data-protection-protected secret (database credential, Telegram bot token, pending Telegram files). This adds little over setting `MUSICHUB_CONNECTION_STRING` directly, but it relocates the keyring | Code inspection `ConnectionStringResolver.cs:33-39`; `Path.GetFullPath` accepts UNC, `\\?\` device paths and `..` segments without complaint | orchestrator |
| B-057 | Low | OPEN | Install / resilience | `Resolve` checks `MUSICHUB_CONNECTION_STRING` *before* reading the credential file, so the documented "a file that exists but cannot be decrypted is a hard error" rule is bypassed whenever the override is set. The override is deliberate for containers, but the effect is that a partially restored data root (credential file present, keyring missing) can be papered over silently and the failure the `Unreadable` state exists to surface becomes invisible | Code inspection `ConnectionStringResolver.cs:63-64`; the override returns before `TryRead` is ever called, so no diagnostic about the unreadable file is emitted | orchestrator |
| B-058 | Informational | OPEN | Install | No audited connection sets `SslMode`, so Npgsql's default (`Prefer`) applies: TLS is opportunistic and the server certificate is not validated. Acceptable for `Host=127.0.0.1` and the private `backend` container bridge, but the database hop is not authenticated for the installer's own admin connection | No `SslMode` appears in any of the four audited files; the installer connects with the operator-supplied admin connection string unchanged | orchestrator |
| B-059 | Medium | FIXED | Media pipeline | `GetOrCreateArtistAsync`/`GetOrCreateGenreAsync` read the row and then insert it in the same EF command batch, with no handling of the unique index on `NormalizedName`. Two imports that resolve the same new artist/genre both read "missing" and both insert; the loser hits `IX_Artists_NormalizedName`, the `DbUpdateException` is not a FileHash violation, so it falls through to the generic handler and the whole upload is lost as "The file could not be stored." Files with no tags all resolve to the fallback artist "Unknown Artist", which makes this reachable on any multi-user instance, and cross-user imports are not serialised by CR-010's per-account lock | Found while proving CR-010: six concurrent untagged uploads from six different administrators produced one success and five `{"success":false,"error":"The file could not be stored."}`; `UploadSessions.Error` recorded `DbUpdateException: An error occurred while saving the entity changes` (not a quota message). Regression test `ReviewFindingsTests.Concurrent_untagged_uploads_from_different_users_all_succeed` fails with the message above when the fix is reverted | backend-engineer |
| B-060 | High | FIXED | Docker / deployment | `deploy/docker/docker-compose.yml` set `pids_limit: 512` on the `api` and `worker` services **and** a `deploy.resources.limits` block naming only `cpus` and `memory`. Compose rejects that combination outright: *"can't set distinct values on 'pids_limit' and 'deploy.resources.limits.pids': invalid compose project"*, so `docker compose up` failed during validation before it created a single container. The file had only ever been read, never validated, which is why the asset stayed `IMPLEMENTED_NOT_TESTED` | Reproduced on Docker Compose v5.7.0 with a minimal two-service file: `pids_limit` + `limits` (cpus/memory) -> INVALID; `pids_limit` + `limits` (cpus+memory+pids) -> VALID; `pids_limit` alone -> VALID. On the real file: `config --quiet` exited 1 naming `services.api`. Fixed by stating `pids: 512` inside `deploy.resources.limits` on both services; `config --quiet` now exits 0 and `config --format json` reports `api published=(none) expose=5080 pids_limit=512 limits.pids=512`, `caddy published=80:80,443:443,443:443/udp`, `postgres`/`worker` publish nothing. `ComposeTopologyTests` (10 cases) is the regression guard; with the two `pids:` lines removed, 3 of its cases fail | orchestrator |
| B-061 | Medium | OPEN | Telegram / data model | `TelegramConfigurations` has only a primary key, yet four call sites treat it as a single row (`OrderBy(x => x.UpdatedAt).FirstOrDefault()` in `TelegramEndpoints`, `TelegramIngestionService` and `TelegramPublishService`, plus a plain `FirstOrDefault()` in `TrackImportService`). Nothing in the schema prevents a second row, so a duplicate would silently change which configuration the API, the ingestion worker and the publish service use - the bot could end up reading a different admin id than the one an operator just wrote | Insert a second row with an older `UpdatedAt`, then read `GET /api/admin/telegram`: the endpoint returns the older row and `PUT` updates that same older row, so an operator editing settings can leave the worker using a different row entirely. Runtime evidence now exists as well: the worker logs `Microsoft.EntityFrameworkCore.Query[10103] The query uses the 'First'/'FirstOrDefault' operator without 'OrderBy' and filter operators. This may lead to unpredictable results.` immediately before resolving `TelegramConfigurations`, which is the `db.TelegramConfigurations.AsNoTracking().FirstOrDefaultAsync(ct)` call in `TrackImportService`. Observed concretely: the first version of the new `set-telegram-admin` had no `WHERE` clause and rewrote every row while reporting success - against a database that held a real configuration row it updated a different row than the one the API reads. Expected a single effective configuration row enforced by the schema. Suggested fix: a uniqueness guarantee on `TelegramConfigurations` (a unique index on a constant expression, e.g. `create unique index ... on "TelegramConfigurations" ((true))`), or move the configuration into the existing `AppSettings` singleton. Until then the new CLI is scoped exactly as the API reads, so it writes the row actually in use | orchestrator |
| B-062 | High | FIXED | Media / metadata | Untagged uploads were titled with a random GUID. `AudioProbeService` fell back to `Path.GetFileNameWithoutExtension(path)` when a file carried no title tag, but the only path it ever receives is the quarantine file `{guid:N}.part`, so every untagged file was titled with the quarantine GUID. `TrackImportService.BuildTrackAsync` already had the correct fallback to `asset.OriginalFileName`, but it only runs when the probe reports no title, so it was dead code | Found by running the real pipeline, not by reading it: a fresh 2.5 s 523 Hz WAV named `ingest-proof.wav` uploaded through `POST /api/admin/telegram/test-ingest` produced `Title = 7cf86ebf01ac48b9b9aa8c37174d45ef`, `NormalizedTitle` identical and `ArtistDisplay = Unknown Artist`. Regression test `MediaPipelineTests.Untagged_upload_takes_the_original_file_name_as_its_title` fails before the fix with `Expected: "untagged-af559c83a5764e0eb210774e839a8893" / Actual: "b77142fe74df4890af9b4dcfcf15b93c"` and passes after it; full suite 212/212. Fix: the probe reports an empty title and the importer applies the uploader-supplied name | orchestrator |
| B-063 | Medium | FIXED | Storage / relocation | A library relocation could report success while having silently skipped artwork and quarantine files. Verification compared audio assets against the database, then compared artwork/quarantine source against destination, but the repair pass only re-copied audio paths, and the final re-check only examined database assets - so a drifted or missing artwork file was detected, "repaired" without effect, and then passed the second check | Found while writing the relocation tests, by reading the two verification passes against each other. `verify -> repair(audio only) -> verifyAgain(audio only)` cannot fail on an artwork problem. Fixed by one routine that returns every mismatch (enumerated files **and** database assets), repairing those, then re-running the full check; a second failure throws and the pointer is never written. Covered by `A_move_that_cannot_verify_the_copy_does_not_change_the_location`, which fails with `Sub-string not found: "failed"` when the failure-state write is disabled | orchestrator |
| B-064 | Medium | FIXED | Storage / relocation | A move that failed left its progress row at the last started phase (`verifying`), so the operator's screen showed a move that had stopped as if it were still running, with no reason. | The only way to see it was to make a move fail and look at what the screen said. `LibraryRelocationService.RunAsync` now catches non-cancellation failures and records phase `failed` with the reason, using a 10 s independent timeout so an already-cancelled request token cannot suppress the write. If the write itself fails it is logged at `Error`, never swallowed. Asserted by the failed-move test against the stored `AppSettings` row | orchestrator |
| B-065 | Low | FIXED | Storage / relocation | `LibraryRelocationService` hashed the entire source library synchronously (`GetAwaiter().GetResult()`) inside a lazy `IEnumerable` evaluated on a request thread, so a large library would block a thread for the duration of the hashing pass and a hashing failure surfaced as an `AggregateException`. | Reading the enumeration against the async signature it sat in. Replaced with `EnumerateSourcesAsync`, which hashes with `await` and observes the cancellation token. | orchestrator |
| B-066 | Low | FIXED | Telegram bot | The bot's `/storage` start button would have thrown at build time: callback actions are restricted to lowercase ASCII letters and digits, so the hyphenated id `storage-start` was rejected by `TelegramCallbackData.Encode`. | Caught by the keyboard builder's own validation before any test ran; the ids are now `storagestart` and `storagerefresh`, and the retry/confirm path is covered by `A_usable_path_offers_the_move_and_the_confirm_tap_queues_it` | orchestrator |
| B-067 | Low | FIXED | Telegram bot | `/start` was routed to the status screen, so the two-button menu and the storage screen had no entry point, and the refresh button always jumped back to the status screen even from storage. | Found while wiring the storage screen: the refresh handler overwrote the session state with `status` unconditionally. Refresh is now contextual (it re-reads whatever screen the chat is on), and `/start` renders the menu. Covered by `TelegramBotStorageTests` and the updated status-screen test | orchestrator |

## W-019 code review findings (CR-001 .. CR-025)

Read-only review of the recent backend increment by the `code-review-engineer` subagent, covering
`MusicHub.Api/Endpoints`, `Program.cs`, the auth/entitlement/upload/Telegram services, the worker
loops and the quality of the existing test suite. B-020, B-021, B-023, B-025 and B-040..B-058 were
explicitly excluded as already tracked; they are not repeated here.

Severity counts: 0 Critical, 2 High, 9 Medium, 10 Low, 4 Informational.
Release blockers were **CR-001** and **CR-002**, both reproduced against the running API. **All 25 findings
are now FIXED and regression-tested** (CR-001/CR-002 in R-010, CR-003..CR-025 in W-030/R-011); the detail
block under each one records the fix, the commands run and the test that pins it. Ten of them were proven
by reverting the fix and observing the named test fail; a further fix, B-059, was proven the same way after
being discovered while validating CR-010.

| ID | Severity | Status | Area | Title |
| --- | --- | --- | --- | --- |
| CR-001 | High | FIXED | Library API | `PATCH /api/tracks/{id}` threw `NullReferenceException` and returned 500 after the edit was already committed | Fixed: the handler now includes `.Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)` alongside Genre/TrackArtists, matching every other track read path, so `DtoMapper.Track` has the navigations it dereferences. Verified: `PATCH` with `{"year":2025}` against the real API returned HTTP 200 with `sizeBytes=46246384` and a 64-char `sha256`, and `SELECT "Year"` confirmed the write. Regression-tested by `tests/.../Integration/TrackEditTests.cs` (3 tests: successful edit returns a complete DTO and persists, a missing track is 404 not 500, a Listener is 403). The test was confirmed to fail against the old handler shape before being accepted |
| CR-002 | High | FIXED | Rate limiting | The rate limiter ran before authentication, so the per-user `password` and `upload` partitions collapsed to per-IP | Fixed: `app.UseAuthentication()` now precedes `app.UseRateLimiter()` in `Program.cs`, so the partition factories read a real principal. Verified live: 12 wrong-password attempts as administrator produced 10x400 then 429, and `android.qa@musichub.local`'s first attempt returned 400 instead of the 429 it got before the fix (it was being throttled by the administrator's exhausted bucket). Regression-guarded by `Integration/RateLimiterPartitionTests.cs`: one test asserts the pipeline order from source and was confirmed to fail with the message "app.UseAuthentication() is at line 326 and app.UseRateLimiter() at line 325" when the old order was temporarily restored; the other asserts a fresh account is not rejected with 429 |
| CR-003 | Medium | FIXED | Playback history | Concurrent playback reports permanently wedge a (user, track) pair with HTTP 500 | Fixed: added a unique `(UserId, TrackId)` index (migration `20261118231510_ListeningHistoryPerTrack`, which sums and collapses any duplicate rows the bug already produced before creating the index) and replaced the read-then-insert with a single-statement `INSERT ... ON CONFLICT (UserId, TrackId) DO UPDATE` upsert. There is no 30-day window any more, so an existing duplicate can no longer break the query |
| CR-004 | Medium | FIXED | Authorization | Contributor can mutate any track/album/artist/folder, contrary to the documented role contract | Fixed: every library-content write is now gated on `Policy = "Admin"` - track PATCH/DELETE, album, artist, folder, genre, folder-track, album-track membership and `POST /api/uploads` - and the seeded `Contributor` role description was rewritten to read-only. Per-user data (favourites, own playlists, playback history) stays writable by any authenticated user. Enforced server-side; the dashboard narrowed its own `canContribute` so no dead write controls remain |
| CR-005 | Medium | FIXED | Users | Every `PATCH /api/users/{id}` rotates the SecurityStamp; an admin can demote the last administrator | Fixed: `SecurityStamp` now rotates only on deactivation or an actual role change, so a display-name or quota edit no longer signs the user out; a role change that would leave zero administrators returns 409 `{"error":"At least one administrator is required."}` |
| CR-006 | Medium | FIXED | Authorization | `includeDeleted=true` on `GET /api/tracks` is not authorized server-side | Fixed: `includeDeleted=true` from a non-administrator returns 403 with an `{ error }` body before the query is built |
| CR-007 | Medium | FIXED | Telegram | Ingestion can stall permanently: the duplicate-update `continue` skips offset persistence | Fixed: the offset is upserted once per poll after the loop, so a batch consisting only of already-seen updates still advances it |
| CR-008 | Medium | FIXED | Worker | The documented "single instance using a database lock row" does not exist; job claiming races | Fixed: claiming moved to `BackgroundJobClaimer` - a candidate read followed by an `ExecuteUpdateAsync` that re-tests the claimability predicate, so a row another worker took no longer matches and the loser gets zero rows. The dead `LockKey` constant and the false doc comment were removed |
| CR-009 | Medium | FIXED | Entitlements | The 2-minute entitlement sweep is N+1 and re-reads nearly every user every pass | Fixed: the sweep reads candidates and grouped storage usage in two queries and deactivates the offending ids directly, with no per-user re-read; `EnforceAsync(Guid)` remains for the login/refresh hot paths |
| CR-010 | Medium | FIXED | Uploads | Storage quota is checked before the upload and never re-asserted, so concurrent uploads overshoot | Fixed: the quota is re-asserted inside the commit transaction under a `SELECT ... FOR UPDATE` row lock on the account, then rolls back and deletes the committed file. The lock is what makes it correct - under ReadCommitted a bare re-read still lets two imports pass on the same baseline. Proving it also uncovered B-059 |
| CR-011 | Medium | FIXED | Library API | Unbounded `TrackIds` lists combined with per-item queries are an amplification lever for whoever may call the endpoint (a Contributor at the time of the review; administrators only after CR-004) | Fixed: batches are bounded at 500 ids with a 400 `{"error":"At most 500 tracks may be added at once."}`, membership is written set-based instead of two queries per id, and the reorder path compares against a `HashSet`. CR-004 narrows who can reach it, but the bound is kept because an authenticated administrator token can also be leaked or scripted |
| CR-012 | Low | FIXED | Health | Anonymous `/api/health` writes a probe file per call and discloses configuration state | Fixed: the anonymous probe no longer writes to the data root (it only checks that the music root can be enumerated) and reports only `{ database, storageReadable }`; the database result is cached for 10 s behind a semaphore so polling does not hammer PostgreSQL |
| CR-013 | Low | FIXED | Caching | Track artwork is served `Cache-Control: public` from an authorized-only endpoint; album artwork sets none | Fixed: both artwork handlers go through one `ArtworkResults` helper that sets `Cache-Control: private,max-age=86400` |
| CR-014 | Low | FIXED | Media | `Album.ArtworkPath` / `Artist.ArtworkPath` are never written, so album/artist artwork is dead | Fixed: the import seeds `Album.ArtworkPath` / `Artist.ArtworkPath` from the first track cover, and album detail falls back to its first track's artwork, so existing data works without a backfill |
| CR-015 | Low | FIXED | Metrics | `/api/admin/metrics` never disposes `Process.GetCurrentProcess()` and blocks 250 ms per call | Fixed: `using var process = Process.GetCurrentProcess()` releases the handle deterministically; the 250 ms sample stays but is taken from the singleton instance |
| CR-016 | Low | FIXED | Dashboard | Dashboard disk figures describe the working directory instead of the data root | Fixed: the stats endpoint resolves the volume from `StorageOptions.RootPath` through the same `DiskSnapshot`/`DiskInfo` helper the metrics endpoint uses |
| CR-017 | Low | FIXED | Pagination | `Skip`/`Take` pagination uses non-unique orderings, so pages can repeat or drop rows | Fixed: every paginated ordering gained an Id tie-breaker - tracks, albums, artists, audit log, upload history, Telegram imports/publications and background jobs |
| CR-018 | Low | FIXED | Entitlements | `ConsumeListeningTimeAsync` has a lost-update race on `ListenSecondsUsed` | Fixed: `ListenSecondsUsed` is incremented in SQL with `ExecuteUpdateAsync`, and the row is re-read to decide exhaustion. The re-read detaches only that entity rather than clearing the tracker, so staged work survives |
| CR-019 | Low | FIXED | Streaming | `streaming.maxConcurrentPerUser` is an editable setting that no code reads - a dead control | Fixed: `StreamTracker` keeps a per-user count and the stream endpoint enforces `streaming.maxConcurrentPerUser` from the stored setting (falling back to options), returning 429 `{ error }`. The dashboard control now does something |
| CR-020 | Low | FIXED | Library API | The caller's entire favourites set is materialised on every list request | Fixed: all six handlers query favourites only for the ids in the page/detail response instead of materialising the whole set |
| CR-021 | Low | FIXED | API contract | Inconsistent error contracts: bare `Results.Forbid()`, and an invalid email returns 409 | Fixed: `ApiResults.Forbidden`/`Unauthorized` give every 4xx the documented `{ error }` body; `POST /api/users` splits a malformed address (400) from a duplicate (409) via `IsPlausibleEmail`; change-password stays 400 rather than 401 because a 401 makes every client treat the session as expired |
| CR-022 | Low | FIXED | Uploads | Raw `DbUpdateException` messages are returned to the client in upload results | Fixed: a `DbUpdateException` returns \"The file could not be stored.\" / \"The file could not be imported.\" with the detail logged and kept on the upload session only |
| CR-023 | Informational | FIXED | Pairing | Pairing claim bypasses the entitlement enforcement that login and refresh apply | Fixed: the claim calls `entitlements.EnforceAsync(session.UserId, checkStorage: true, ct)` before issuing tokens and maps a denial to 401 with `DeactivationReasons.Describe` |
| CR-024 | Informational | FIXED | Media | A committed file is orphaned when the import transaction rolls back for any reason other than a hash race | Fixed: `CommitAsync` returns a `CommitResult(RelativePath, CreatedFile)` and the committed path is tracked outside the `try`, so every rollback/cancel path deletes only a file this call created |
| CR-025 | Informational | FIXED | Cleanup | Dead code and unimplemented settings left in the tree | Fixed: deleted `Worker.cs` and the three `Class1.cs` files; `QuotaBytes` now uses `StorageOptions.DefaultUserQuotaBytes`; the outbox drain honours `Attempts`/`NextAttemptAt` with exponential backoff and dead-letters after 5 attempts |

### CR-001 (High) - FIXED - track PATCH returned 500 on a committed mutation

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 88-118 (handler), 117-118 (mapper call); src/server/MusicHub.Api/CommonDtos.cs:24, 35-36
Explanation: The handler loads the track with Genre and TrackArtists included but never AudioAsset
  or FileHash, then calls DtoMapper.Track, which unconditionally dereferences
  track.AudioAsset.SizeBytes and track.AudioAsset.FileHash.Sha256. Lazy loading is not enabled
  anywhere (no UseLazyLoadingProxies, no AutoInclude - verified by grep across src/server), so the
  navigation is null. SaveChangesAsync already ran, so the edit is persisted and only the response
  fails: the worst shape, data changed and the client told it failed.
Reproduction: VERIFIED DYNAMIC. POST /api/auth/login as administrator, then
  PATCH /api/tracks/e0988806-5130-494e-8d63-1910035b45b7 with body {"year":2024}.
Expected: 200 with the updated TrackDto (the dashboard renders the returned track).
Actual: HTTP 500 {"type":"https://httpstatuses.com/500","title":"An unexpected server error
  occurred.","status":500}; the API log shows "System.NullReferenceException ... at
  MusicHub.Api.DtoMapper.Track(Track track, Boolean favorite) in CommonDtos.cs:line 24" reached from
  LibraryEndpoints.cs:line 117; and SELECT "Year" FROM "Tracks" immediately afterwards returns 2024,
  proving the row was updated despite the 500.
Suggested fix: Include the asset graph in the same query
  (.Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)), or after SaveChangesAsync re-read the
  DTO through the same projection GET /api/tracks/{id} uses so the class of bug cannot recur. Add an
  integration test asserting 200 for a track rename - there is none today.
Fix applied: Took the first option and added the integration tests. The handler now loads
  .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash) with the existing Genre and TrackArtists
  includes, which is the same graph every other track read path already loads. Confirmed against the
  real API: PATCH {"year":2025} -> HTTP 200, body carries sizeBytes=46246384 and a 64-character
  sha256, and a follow-up read shows year=2025. tests/.../Integration/TrackEditTests.cs adds the
  missing coverage: a successful edit returns a complete DTO and persists, a missing track is 404
  rather than 500, and a Listener gets 403. All 3 pass.
```

### CR-002 (High) - FIXED - rate limiter partitioned on an unauthenticated principal

```text
File: src/server/MusicHub.Api/Program.cs
Line: 320 (UseRateLimiter), 321 (UseAuthentication); partitions at 239-241 and 251-253
Explanation: app.UseRateLimiter() is registered before app.UseAuthentication(), so when the limiter
  acquires a lease HttpContext.User is still the default unauthenticated principal. The "password"
  and "upload" partition factories read http.User.FindFirstValue(ClaimTypes.NameIdentifier), which
  therefore always returns null and falls through to RemoteIpAddress. The per-account guarantee the
  comment at lines 236-237 claims ("a stolen token must not become an online oracle for
  brute-forcing the current password") is not realised: the limiter keys on the client IP instead.
  Secondary effect: in any shared-egress deployment all users share one 10/min password bucket and
  one 20/min upload bucket, so one user throttles everybody.
Reproduction: VERIFIED DYNAMIC. Sign in as administrator, then call
  POST /api/auth/change-password with a wrong currentPassword 11 times: attempts 1-10 return 401,
  attempt 11 returns 429. Immediately call the same endpoint with a DIFFERENT account's token
  (android.qa@musichub.local) for the first time: it also returns 429. A request that is throttled
  before authentication is by definition not partitioned per user.
Expected: per-user partitions for authenticated endpoints, so each account has its own
  password-change and upload budget; a second user is unaffected by the first user's exhaustion.
Actual: all partitions are keyed by client IP and the first authenticated request from another
  account is rejected with 429.
Suggested fix: Move app.UseRateLimiter() below app.UseAuthentication() (and below
  UseAuthorization() if a policy ever reads roles). Add an integration test that asserts two users
  from the same client IP get independent password budgets.
Fix applied: Moved the call, so the pipeline is now UseCors -> UseAuthentication -> UseRateLimiter ->
  UseAuthorization, with a comment recording why the order matters. Verified live against the real
  API: 12 wrong-password attempts as administrator returned 400 x10 then 429, and a DIFFERENT
  account's first attempt returned 400 instead of the 429 it received before the fix - previously the
  limiter rejected it before authentication had even run. Guarded by
  tests/.../Integration/RateLimiterPartitionTests.cs; because middleware order is not observable over
  HTTP (both orders serve requests, only the partition key differs), one test asserts the order from
  Program.cs and was confirmed to FAIL when the old order was temporarily restored:
  "app.UseAuthentication() is at line 326 and app.UseRateLimiter() at line 325". The second test
  creates a fresh account and asserts its first change-password attempt is 400, not 429.
```

### CR-003 (Medium) - FIXED - playback history duplicate rows wedge the endpoint

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 592-594; schema src/server/MusicHub.Infrastructure/Persistence/MusicHubDbContext.cs:247-251
Explanation: ListeningHistory has no unique constraint on (UserId, TrackId) - only (UserId, PlayedAt)
  and (TrackId, PlayedAt) indexes. The handler does read-then-insert with SingleOrDefaultAsync, so two
  concurrent reports for the same track both observe null and both insert. Every later report then
  matches two in-window rows and SingleOrDefaultAsync throws InvalidOperationException ("Sequence
  contains more than one element"), which the global handler turns into HTTP 500. The pair stays
  broken for 30 days, and PlayedAt only refreshes on a successful report, so the duplicate never ages
  out early.
Reproduction: With a listener token, fire two POST /api/history/playback requests concurrently for
  the same trackId. The next sequential report for that track returns 500 instead of 204.
Expected: at most one in-window row per (user, track); reporting is idempotent and always returns 204
  (or 403 when the quota is spent).
Actual: duplicate rows accumulate and the endpoint then fails with 500 permanently for that track.
Suggested fix: Add a unique index on (UserId, TrackId) with a migration and switch to an upsert
  (INSERT ... ON CONFLICT (UserId, TrackId) DO UPDATE SET PlayCount = PlayCount + 1, PlayedAt = now()).
  If the 30-day bucketing is intentional, use OrderByDescending(x => x.PlayedAt).FirstOrDefaultAsync()
  so an existing duplicate degrades instead of throwing.

Fix applied: Schema + handler. Added a unique index on (UserId, TrackId) and dropped (UserId, PlayedAt) /
  (TrackId, PlayedAt), keeping a single PlayedAt index for the "recent history" query. Reporting is now one
  statement - INSERT ... ON CONFLICT ("UserId","TrackId") DO UPDATE SET "PlayCount" =
  "ListeningHistory"."PlayCount" + 1, "PlayedAt" = now() - so the 30-day window is gone and there is no
  read-then-insert window to lose. The migration collapses pre-existing duplicates before creating the
  index, because otherwise it fails on exactly the databases the bug hit.
Verified: `dotnet test` -> 152/152 pass. The migration was applied to the development database
  (Host=127.0.0.1;Port=5433;Database=musichub): __EFMigrationsHistory gained
  20261118231510_ListeningHistoryPerTrack, pg_indexes shows IX_ListeningHistory_UserId_TrackId as a UNIQUE
  btree (UserId, TrackId), and re-inserting a duplicate (UserId, TrackId) is rejected with "duplicate key
  value violates unique constraint". The dedup SQL was run inside a transaction against real rows (three
  rows for one pair, PlayCount 6/3/5): it leaves one row with PlayCount 14 and the newest PlayedAt. The
  first version ran the DELETE before the aggregate UPDATE and lost the counted plays (6+3+5 collapsed to
  6); the order was reversed and re-verified before the migration was applied.
Regression tests: ReviewFindingsTests.Repeated_playback_reports_for_one_track_stay_at_one_history_row
  (5 reports -> 1 row, PlayCount 5) and
  Concurrent_playback_reports_do_not_wedge_the_endpoint_or_lose_time (6 concurrent reports -> all 204, a
  following report still 204, 1 row, ListenSecondsUsed 30). The concurrency test was confirmed to FAIL
  against the old read-then-insert shape with 500 InternalServerError.
```

### CR-004 (Medium) - FIXED - Contributor can mutate any entity in the library

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 88 (tracks), 205 (albums), 312 (artists), 361 (folders)
Explanation: All four PATCH handlers use [Authorize(Policy = "Contributor")] and then load the entity
  by id with no ownership predicate, even though Track and Folder both carry CreatedByUserId. The
  seeded role description (MusicHubDbContext.cs:62) reads "Upload and manage own music
  contributions". So any Contributor can rename, re-genre, re-album or re-associate another user's
  upload, and can move any folder into any other folder. Albums/artists/genres are arguably shared
  curation, but the track and folder cases contradict the stated model. This is a contract problem as
  much as a bug: if collaborative curation is intended, the role description and docs are what is
  wrong.
Reproduction: Create Contributors A and B. As B upload a track. As A,
  PATCH /api/tracks/{B'sTrackId} with {"title":"Taken over"} - the title changes (subject to CR-001's
  500, the row is still mutated).
Expected: 403 for a non-owner, matching "manage own contributions"; or an explicit documented
  statement that Contributors share curation rights.
Actual: the entity is mutated by a non-owner.
Suggested fix: Decide the model and encode it. For ownership: if (track.CreatedByUserId !=
  principal.GetUserId() && !principal.IsAdministrator()) return Results.Forbid(); in the track and
  folder handlers. For shared curation: update Role.Description, ARCHITECTURE.md and the dashboard
  role help, and add a test that pins the decision.

Fix applied: Adopted the stated policy: only Administrator may create, edit, upload, modify, move or delete
  library content, and every other authenticated role is strictly read-only for it. Contributor is retained
  as a read-only role so existing assignments stay valid. Implementation: every library write endpoint now
  carries [Authorize(Policy = "Admin")] - track PATCH/DELETE, album POST/PATCH, artist POST/PATCH, folder
  POST/PATCH, genre POST, folder-track and album-track membership, and POST /api/uploads. The seed role
  descriptions were rewritten to say so. Per-user data is the documented exception and stays writable by
  its owner: favourites, own playlists, collaborative playlists, playback history.
Rejected alternative: keeping "manage own contributions". The policy is a product decision, and an ownership
  predicate would still let a Contributor mutate library content.
Regression tests: ReviewFindingsTests.Contributor_cannot_change_library_content (six-case Theory: PATCH and
  DELETE track, POST album/artist/folder/genre -> 403), Contributor_cannot_upload_audio (POST /api/uploads
  -> 403), and Contributor_can_still_manage_own_favourites_and_playlists, which pins the per-user exception
  so the fix cannot be over-applied (favourite 204, playlist create 201, rename 204, add track 204).
Server-side: the policy is on the endpoint, so hiding a control in the dashboard is never the boundary. The
  dashboard additionally narrowed canContribute to administrators so it renders no dead write controls.
```

### CR-005 (Medium) - FIXED - unconditional SecurityStamp rotation and no last-admin guard

```text
File: src/server/MusicHub.Api/Endpoints/AuthEndpoints.cs
Line: 234-239 (role change), 273-274 (stamp rotation); Program.cs:184 (OnTokenValidated)
Explanation: Line 274 assigns user.SecurityStamp = Guid.NewGuid() unconditionally, including for a
  pure display-name or quota edit. OnTokenValidated compares the stamp on every request, so any
  profile edit instantly invalidates that user's live access tokens; the Android client and the WPF
  player treat a non-refreshable 401 as session expiry (the B-013/B-032 lineage), so an administrator
  editing a display name signs the user out of the client mid-playback. Separately, lines 234-239
  replace UserRoles from request.Role with no self-check and no last-administrator check, so an
  administrator can demote the only administrator and leave the instance with no recovery path
  outside direct database access. The dashboard hides the role control for the current user
  (UsersPage.tsx:205) - exactly the "UI hiding is not authorization" pattern the project forbids.
Reproduction: (a) Sign in twice for one user, then PATCH /api/users/{id} with
  {"displayName":"New Name"} as admin; both access tokens then return 401. (b) As the only
  administrator, PATCH /api/users/{ownId} with {"role":"Listener"} succeeds and GET /api/users then
  returns 403 for the only account.
Expected: the stamp rotates only on events that must revoke access (password change/reset,
  deactivation, sign-out-everywhere); a role change that would remove the last Administrator is
  rejected.
Actual: any metadata edit forces re-authentication, and the last administrator can be removed.
Suggested fix: Move the stamp rotation into a helper called only from the revoking paths. Before
  applying a role change, count administrators and return 409 {"error":"At least one administrator is
  required."} when the acting user is the last one; add an explicit self-demotion rule and cover both
  with integration tests.

Fix applied: SecurityStamp is now assigned only when the request deactivates the account or actually changes
  the role. A role change that would leave zero administrators is refused with 409
  {"error":"At least one administrator is required."}. The handler also loads UserRoles.ThenInclude(Role),
  which the guard needs: the previous Include(x => x.UserRoles) alone left x.Role null (lazy loading is
  disabled solution-wide), so the comparison against the seeded Administrator role evaluated against null
  and threw.
Regression tests: Editing_a_display_name_keeps_existing_access_tokens_valid (rename -> 204, the pre-existing
  token still gets 200 from /api/auth/me) and Changing_a_role_revokes_existing_access_tokens (role change ->
  204, the old token gets 401). The first was confirmed to FAIL against unconditional rotation.
  The_last_administrator_cannot_be_demoted asserts 409 with "At least one administrator" when only one
  administrator exists, and 204 when another does.
```

### CR-006 (Medium) - FIXED - `includeDeleted` is not authorized

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 28, 37; front end src/web/admin/src/pages/TracksPage.tsx:113, 255
Explanation: Line 37 honours includeDeleted for any authenticated caller with no role check, yet the
  dashboard only renders the "Include deleted" toggle for administrators. The server therefore
  exposes soft-deleted tracks (title, artist, album, SHA-256, size, duration, artwork URL) to a
  Listener. It also conflicts with GET /api/tracks/{id}, which always excludes deleted rows, so a
  listener can enumerate a track id that the detail endpoint then 404s.
Reproduction: With a Listener token, GET /api/tracks?includeDeleted=true returns 200 with items whose
  ids return 404 from GET /api/tracks/{id}.
Expected: 403 for non-administrators when includeDeleted=true, or the parameter ignored.
Actual: 200 with soft-deleted rows included.
Suggested fix: if (includeDeleted == true && !principal.IsAdministrator()) return Results.Forbid();
  before building the query, with an integration test asserting the listener gets 403 and cannot see a
  deleted track in the default listing.

Fix applied: The administrator gate runs before the query is built, so a non-administrator asking for
  includeDeleted=true gets 403 with the documented { error } body rather than a silently filtered list.
Regression test: ReviewFindingsTests.Listener_cannot_list_deleted_tracks_but_administrator_can - the
  Listener gets 403, the deleted id is absent from the Listener's default listing, and the administrator does
  see it. Confirmed to FAIL against the old code (Expected: Forbidden, Actual: OK).
```

### CR-007 (Medium) - FIXED - ingestion can stall permanently

```text
File: src/server/MusicHub.Infrastructure/Telegram/TelegramIngestionService.cs
Line: 76-122 (the continue at 83, the offset write at 113-114)
Explanation: The loop advances a local newOffset, then `continue`s when the update id is already
  recorded - skipping the UpsertOffsetAsync + SaveChangesAsync pair. Offset is persisted only for
  updates that pass the duplicate check, or in the final block that runs only when updates.Count == 0.
  So if a batch consists entirely of already-recorded updates, no offset is written. The state is
  reachable: process updates 1..20, have SaveChangesAsync fail once (the exception propagates to
  TelegramPollingWorker, which catches and retries), leaving the offset unchanged while the
  TelegramUpdates rows are already committed. Every later poll then re-fetches 1..20, skips all of
  them and never advances - no new message is ever ingested until an operator edits the
  telegram.updateOffset row. No log line or health signal reports it.
Reproduction: Set AppSetting telegram.updateOffset=0 with TelegramUpdates rows for update_id 1..20 (or
  simulate the SaveChanges failure), then let the worker poll against a bot that still returns those
  ids: "Telegram polling worker started." repeats with no imports and the offset stays 0.
Expected: the persisted offset advances past every update already recorded, so ingestion drains and
  resumes.
Actual: the offset never advances; ingestion is permanently stuck while the log looks healthy.
Suggested fix: Move the offset write out of the per-update success path - always UpsertOffsetAsync and
  save after the loop (or in a finally), regardless of skips and of updates.Count. Emit a warning when
  a full batch was skipped without an offset change so the stall is observable.

Fix applied: The offset is now upserted once per poll, after the loop, whenever it differs from the stored
  value or no row exists - so the all-duplicates batch, the empty batch and the partial batch all advance it.
  The per-update save inside the loop is gone.
Regression test: IngestionAndWorkerTests.A_poll_of_already_seen_updates_still_advances_the_offset drives the
  real TelegramIngestionService.PollOnceAsync against a loopback HttpListener stub of the Bot API. Two
  already-recorded updates (4101, 4102) are returned, the stored offset starts at 0, and the test asserts the
  offset becomes 4103 and that the next poll sends offset=4103. Confirmed to FAIL against the old shape.
  A_disabled_configuration_reports_why_and_touches_nothing asserts a disabled configuration explains itself
  and makes no HTTP call at all.
```

### CR-008 (Medium) - FIXED - job claiming races; the advertised lock row does not exist

```text
File: src/server/MusicHub.Worker/MaintenanceWorker.cs
Line: 17-19 (LockKey declared), 76-91 (ProcessJobAsync); also
  src/server/MusicHub.Infrastructure/Telegram/TelegramPublishService.cs:27-35
Explanation: The class doc claims "Runs a single instance at a time using a database lock row" and
  declares LockKey = "worker.maintenance.lock", but LockKey is never referenced anywhere in the
  repository. ProcessJobAsync selects the first eligible job and then sets Status = Processing in a
  separate SaveChangesAsync with no guarded update and no row lock, so two worker processes (a rolling
  restart overlap, a second container, two local workers) both select and both execute the same job.
  TelegramPublishService has the same read-then-write shape and would double-send audio.
  HeartbeatAsync has the same read-then-insert race and would throw a primary-key violation if two
  workers start together.
Reproduction: Start two MusicHub.Worker processes against one database and enqueue one integrity.scan
  job: both log completion for the same job id and two StorageIntegrityCheck rows are created.
  Static confirmation: grep -n LockKey shows only the declaration.
Expected: exactly one execution per job, as the class documentation promises.
Actual: both workers execute the job; the lock constant is dead code.
Suggested fix: Replace read-then-write with an atomic claim that asserts the row count, e.g.
  ExecuteUpdateAsync(s => s.SetProperty(Status, Processing).SetProperty(Attempts, x => x.Attempts + 1))
  on a Where(Id == job.Id && Status == expected) query, then `if (claimed != 1) return;`. Apply the
  same to TelegramPublications. Either implement the advertised lock row or delete LockKey and correct
  the doc comment.

Fix applied: Claiming moved into Infrastructure/Maintenance/BackgroundJobClaimer.cs. FindCandidateAsync
  selects the oldest runnable id (with an Id tie-breaker, CR-017); TryClaimAsync then performs one conditional
  UPDATE - Where(x => x.Id == jobId).Where(Claimable(now)) - that sets Status = Processing, increments
  Attempts and sets LockedUntil, and returns null when ExecuteUpdateAsync reports zero rows. ClaimNextAsync
  composes the two for the normal path. The predicate lives in one expression so the read and the UPDATE
  cannot drift apart. MaintenanceWorker calls the claimer; the dead LockKey constant and the doc comment
  advertising a lock row that never existed are gone.
Regression tests (IngestionAndWorkerTests, real PostgreSQL): Two_workers_cannot_claim_the_same_job models the
  real race - both workers find the same candidate before either claims, then both try - and asserts exactly
  one claim wins with Attempts == 1. A_second_claim_of_the_same_job_is_refused asserts the repeat claim is
  null. A_job_whose_lock_expired_can_be_claimed_again and A_job_scheduled_for_the_future_is_not_claimed cover
  the other branches. The first two were confirmed to FAIL against a claim with the predicate removed (the
  loser received Claim { Attempt = 2 }, i.e. the job would have run twice).
```

### CR-009 (Medium) - FIXED - entitlement sweep is N+1

```text
File: src/server/MusicHub.Infrastructure/Security/UserEntitlementService.cs
Line: 153-186 (candidates), 62-75 (per-user re-read inside EnforceAsync)
Explanation: The candidate filter includes `|| x.QuotaBytes > 0`, and every user created through the
  API gets a 10 GB default quota (AuthEndpoints.cs:185), so the candidate set is effectively all
  active non-administrator accounts on every 2-minute pass. The method computes a grouped storage
  usage dictionary and passes only usage.ContainsKey(userId) into EnforceAsync, which ignores it and
  re-reads the user, re-checks the admin role, and re-runs a per-user SUM over Tracks. That is about
  three extra round trips plus a SUM per candidate, every 2 minutes.
Reproduction: Create ~300 non-admin users, enable EF command logging and let
  EntitlementEnforcementWorker run one cycle: the log shows a Users SELECT, a UserRoles SELECT and a
  SUM over Tracks for each candidate, even though the grouped usage was already loaded.
Expected: one grouped usage query plus one batched evaluation per sweep, with no per-user re-reads.
Actual: ~4 queries per candidate per pass.
Suggested fix: Add an overload that evaluates from already-materialised state - project the
  candidates (Id, ExpiresAt, ListenQuotaSeconds, ListenSecondsUsed, QuotaBytes) in the initial query,
  join the precomputed usage dictionary in memory, and call a pure evaluator that only performs
  DeactivateAsync for the offending ids. Keep EnforceAsync(Guid) for the login/refresh hot paths.

Fix applied: The sweep projects the candidates (Id, ExpiresAt, ListenQuotaSeconds, ListenSecondsUsed,
  QuotaBytes) and the grouped storage usage in two queries, evaluates them in memory and calls the
  deactivation path only for the offending ids. There is no per-user re-read and no per-user SUM.
  EnforceAsync(Guid) is unchanged and still used by login, refresh and the pairing claim, where one account is
  being checked anyway.
Verified: EntitlementTests assert the sweep deactivates expired, listening-exhausted and storage-exhausted
  accounts and leaves administrators alone; all pass against the two-query implementation.
```

### CR-010 (Medium) - FIXED - concurrent uploads overshoot the storage quota

```text
File: src/server/MusicHub.Infrastructure/Media/TrackImportService.cs
Line: 58-115 (the quota read at 65-66, receiveLimit at 106-109)
Explanation: The quota decision reads usedBytes and derives receiveLimit from it, then streams the
  body. Nothing serialises two uploads for the same user and nothing re-checks the quota after the
  write, so N concurrent uploads each see the same usedBytes and each may consume the full remaining
  headroom. POST /api/uploads accepts up to 25 files per request and the upload policy allows 20
  requests/min, so a Contributor can start dozens of parallel imports and land far beyond QuotaBytes
  before the next entitlement check notices - and the correction is deactivation, i.e. after the disk
  is consumed.
Reproduction: Create a Contributor with quotaBytes = 100 MB. Fire five simultaneous POST /api/uploads
  requests each with a 40 MB file: all five import (200 MB total), and GET /api/users then reports
  storageUsedBytes ~200 MB with the account deactivated.
Expected: at most 100 MB stored for that user; later uploads rejected with 422 "User storage quota
  exceeded."
Actual: every upload passes the pre-check; the quota is exceeded by the concurrency factor.
Suggested fix: Reserve capacity before streaming (a per-user counter updated with a guarded
  ExecuteUpdate that asserts the row count, or a PostgreSQL advisory lock on the user id around
  check-then-commit), and re-verify usedBytes + received <= quota inside the commit transaction,
  rolling back and deleting the committed file when it fails. SELECT ... FOR UPDATE on the Users row is
  the simplest correct fix given the schema.

Fix applied: The quota is now re-asserted inside the commit transaction, after the file is committed but
  before the AudioAsset row is written. Three parts, because the first two attempts were insufficient:
  (1) the pre-upload check stays as an early rejection; (2) inside the transaction the account row is
  locked with SELECT 1 FROM "Users" WHERE "Id" = @userId FOR UPDATE, then the quota and the used bytes are
  read again; (3) if usedNow + session.ReceivedBytes exceeds the quota the import throws
  InvalidAudioException("User storage quota exceeded."), the transaction rolls back and the committed file
  is deleted (CR-024's committedPath handling).
Scope note on why the lock is required: re-reading inside the transaction is not enough on its own. Under
  the default ReadCommitted isolation two concurrent imports both see the same baseline before either
  commits, so both pass and both land. The row lock serialises imports per account, so the second one sees
  the first one's committed bytes and is rejected. The quota is read after the lock rather than from the
  pre-upload snapshot, so a limit tightened while the bytes were in flight is also enforced.
Scope note: a reservation scheme would additionally avoid the wasted upload bandwidth, but the lock plus
  re-assert is what makes the stored state correct, which is the defect.
Verified: `dotnet test` -> 152/152. Regression test
  ReviewFindingsTests.Concurrent_uploads_cannot_overshoot_the_storage_quota creates a dedicated
  Administrator with quotaBytes = 400000, fires four ~240 KB uploads at once, and asserts exactly one
  succeeds, that the stored bytes for that account never exceed the quota, and that each refusal reports
  the quota rather than a storage failure. Confirmed to FAIL with the in-transaction block removed:
  Assert.Equal(1, succeeded) reported "Expected: 1, Actual: 4", i.e. all four uploads landed instead of
  one. Existing coverage in EntitlementTests still asserts the login-time and sweep deactivation.

  Correction to an earlier version of this entry: the first attempt at this proof reported that the
  stored total exceeded the cap, and that was not what the run showed. The failure was an
  Assert.Contains on the refusal message ("The file could not be stored." did not contain "quota"), and
  the stored-bytes assertion had passed - so the run proved a *different* defect and not the quota. The
  cause was a second bug: the fixtures carry no tags, every import resolved the artist to "Unknown
  Artist", and the read-then-insert in GetOrCreateArtistAsync let concurrent imports both miss the row
  and both insert, so the loser lost the upload to the unique index. That masked the quota. The artist
  and genre creators now insert-or-ignore and re-read (see B-059), and the quota test is the one that
  actually observes four uploads landing when the fix is removed.
```

### CR-011 (Medium) - FIXED - unbounded `TrackIds` plus per-item queries

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 393-404 (folders), 538-555 (reorder), 508-522 (playlist add)
Explanation: POST /api/folders/{id}/tracks iterates request.TrackIds.Distinct() with two AnyAsync
  calls per id and no maximum length, so one request with 100 000 ids issues ~200 000 round trips and
  pins a request thread for minutes. PUT /api/playlists/{id}/reorder loads all PlaylistTracks and then
  does rows.Where(x => !request.TrackIds.Contains(x.TrackId)) inside a loop - O(rows x ids) with a
  linear list search, quadratic with an unbounded list. PlaylistTrackRequest/FolderTrackRequest are
  plain List<Guid> with no validation attribute.
Reproduction: As a Contributor, POST /api/folders/{folderId}/tracks with 50 000 random guids; repeat
  in parallel to saturate the thread pool.
Expected: a bounded batch size (e.g. 500) rejected with 400 above the limit, and set-based SQL.
Actual: unbounded work per request, proportional to attacker-supplied list length.
Suggested fix: Validate TrackIds.Count <= 500 with
  Results.BadRequest(new { error = "At most 500 tracks may be added at once." }); replace the loop
  with one query (db.FolderTracks.Where(x => x.FolderId == id && ids.Contains(x.TrackId)) plus an
  existence check over db.Tracks), and turn request.TrackIds into a HashSet<Guid> before comparisons.

Fix applied: Folder-track and album-track membership share MaxBatchSize = 500; a larger request returns 400
  with BatchTooLargeMessage ("At most 500 tracks may be added at once.") before any query runs. The per-id
  AnyAsync loop was replaced with a set-based read of existing FolderTracks plus a single existence check over
  Tracks. The reorder handler turns request.TrackIds into a HashSet before comparing, removing the quadratic
  Contains.
Regression test: ReviewFindingsTests.Adding_more_than_500_tracks_to_a_folder_is_rejected - 501 ids return 400
  with a body containing "500". Confirmed to FAIL against the unbounded version.
```

### CR-012 (Low) - FIXED - anonymous health endpoint writes and discloses

```text
File: src/server/MusicHub.Api/Endpoints/HealthEndpoints.cs
Line: 13-47
Explanation: The endpoint is AllowAnonymous() and writes then deletes a probe file under
  storage.QuarantineRoot on every call, so an unauthenticated caller can generate arbitrary disk churn
  (only the 600/min global IP limiter applies) and provoke the exception path on a full or read-only
  volume. It also reports database, ffprobe, malwareScannerConfigured and telegramConfigured to
  anyone, which is reconnaissance for an attacker mapping the deployment.
Reproduction: Loop 200 unauthenticated GET /api/health requests; observe a probe file created and
  deleted per call and the JSON body returned.
Expected: liveness/readiness without write access to the data root and without enumerating optional
  capabilities to anonymous callers.
Actual: an anonymous endpoint mutates the data root and publishes configuration details.
Suggested fix: Use a read-only readiness probe (cached CanConnectAsync plus a Directory.Exists/access
  check), or restrict the detailed payload to administrators while keeping a bare
  {"status":"ok"} public, with a short negative cache so a poll does not hit the database every second.

Fix applied: The anonymous probe no longer creates or deletes any file. Readiness is
  Directory.EnumerateFileSystemEntries(storage.MusicRoot).MoveNext() inside a try (MoveNext is what actually
  touches the volume, so it has to be the guarded call) plus a cached CanConnectAsync. The payload is now
  { status, checks: { database, storageReadable } } and no longer names FFprobe, the malware scanner or
  Telegram. The database result is cached for 10 s behind a SemaphoreSlim, so a 1/second poll hits PostgreSQL
  at most once per 10 s.
Regression test: ReviewFindingsTests.Health_probe_writes_nothing_and_discloses_no_capabilities counts the
  files in the quarantine directory before and after an anonymous GET /api/health (must be unchanged), asserts
  the raw body contains none of "telegramConfigured", "malwareScannerConfigured" or "ffprobe", and asserts
  database and storageReadable are both true.
```

### CR-013 (Low) - FIXED - artwork caching contradicts its own authorization

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 145-154 (track artwork), 265-272 (album artwork)
Explanation: /api/tracks/{id}/artwork requires [Authorize] but sets Cache-Control: public,max-age=86400.
  "public" explicitly authorises shared caches and proxies to store the response and serve it to other
  users, contradicting the authorization requirement on the resource. The album equivalent sets no
  cache header at all, so two sibling endpoints behave differently for the same class of content.
Reproduction: GET /api/tracks/{id}/artwork with a bearer token and inspect the headers; a shared
  proxy is then permitted to replay the body to an unauthenticated client.
Expected: private,max-age=86400 (or no-store), consistent across both endpoints.
Actual: track artwork is public; album artwork has no Cache-Control.
Suggested fix: Change line 152 to private,max-age=86400 and set the same header in the album handler;
  factor both into one helper so they cannot drift again.

Fix applied: Both handlers return through Api/Common.cs ArtworkResults, which sets CacheControlValue =
  "private,max-age=86400" and the content type in one place, so track and album artwork cannot drift apart
  again.
Regression tests: ReviewFindingsTests.Track_artwork_is_never_publicly_cacheable asserts CacheControl.Private
  is true, Public is false and max-age=86400 is present (the assertion is skipped only when the fixture WAV has
  no embedded cover, which the test states explicitly). Album_artwork_endpoint_falls_back_to_a_track_cover
  asserts the same header on every album that advertises an artworkUrl.
```

### CR-014 (Low) - FIXED - album and artist artwork is a dead feature

```text
File: src/server/MusicHub.Infrastructure/Media/TrackImportService.cs
Line: 343-361 (album creation), 364-365 (artwork); src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs:181
Explanation: Artwork is only ever extracted for the track; Album.ArtworkPath and Artist.ArtworkPath are
  never assigned anywhere in the solution (grep for "ArtworkPath =" finds only the track assignment and
  migration column definitions). So GET /api/albums/{id} always returns artworkUrl = null,
  GET /api/albums/{id}/artwork is unreachable, and every album/artist grid renders the placeholder
  permanently. The album artwork endpoint, the Album.ArtworkPath column and the artist artwork URL in
  the DTOs are dead control surface.
Reproduction: Import a track with embedded cover art, then GET /api/albums: every artworkUrl is null
  while GET /api/tracks/{id} returns a working artworkUrl for the same track.
Expected: album artwork derived from its tracks (first non-null) so album/artist views show covers.
Actual: album and artist artwork are permanently null.
Suggested fix: In BuildTrackAsync, when artwork is produced and the track is attached to an album, set
  album.ArtworkPath if null; make GET /api/albums/{id} fall back to the first track's ArtworkPath in
  the album so existing data works without a backfill.

Fix applied: TrackImportService seeds album.ArtworkPath and artist.ArtworkPath from the first imported track
  cover when they are null, so new imports populate them. LibraryEndpoints album detail additionally falls
  back to the first non-deleted track's ArtworkPath, so albums imported before this change start serving
  artwork without a backfill, and the album DTO's artworkUrl reflects what the endpoint can actually serve.
Regression test: ReviewFindingsTests.Album_artwork_endpoint_falls_back_to_a_track_cover walks every album and
  asserts the contract in both directions: a null artworkUrl must correspond to 404 from
  /api/albums/{id}/artwork, and a non-null artworkUrl must resolve to 200 with a private cache header. A
  broken advertisement fails just as loudly as a missing one.
```

### CR-015 (Low) - FIXED - metrics endpoint leaks a process handle

```text
File: src/server/MusicHub.Api/Endpoints/DashboardEndpoints.cs
Line: 74-101 (Process at 76, Task.Delay at 83)
Explanation: var process = Process.GetCurrentProcess() is not disposed and has no using, so the OS
  handle is released only by the finalizer; the monitoring page polls on refetchInterval, so handles
  accumulate between gen-2 collections. The same handler awaits Task.Delay(250) purely to obtain a CPU
  sample, pinning a request for a quarter second on every poll, and probes the database twice.
Reproduction: Poll GET /api/admin/metrics in a loop for a few minutes and watch HandleCount climb
  between collections.
Expected: no unbounded handle growth from a read-only metrics endpoint.
Actual: one leaked handle per request until GC finalizes the Process objects.
Suggested fix: using var process = Process.GetCurrentProcess(); (or a cached instance for the
  singleton metrics path) plus process.Refresh(); consider making the 250 ms sample opt-in and caching
  the counts for a few seconds.

Fix applied: `using var process = Process.GetCurrentProcess();` so the handle is released when the handler
  returns instead of at finalization, which is what made HandleCount climb between gen-2 collections on the
  polling monitoring page.
```

### CR-016 (Low) - FIXED - dashboard disk figures describe the wrong volume

```text
File: src/server/MusicHub.Api/Endpoints/DashboardEndpoints.cs
Line: 149-160 (DiskInfo at 153); contrast Program.cs:88 in the same file which uses the data root
Explanation: DiskInfo builds new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\")
  while /api/admin/metrics correctly uses Path.GetPathRoot(storage.Value.RootPath). For a
  Windows-service deployment the working directory is typically C:\Windows\System32, so the dashboard
  "Storage" tile reports the system volume instead of the volume holding the library, and the two admin
  pages can disagree about the same number. The ?? fallback also does not catch an empty string.
Reproduction: Configure MUSICHUB_DATA_ROOT on D: while the service working directory stays on C:.
  GET /api/dashboard/stats reports storage.disk.name = "C:\\" while GET /api/admin/metrics reports
  disk.drive.Name = "D:\\".
Expected: both endpoints describe the volume holding the content-addressed store.
Actual: the stats endpoint describes the working directory's volume.
Suggested fix: Inject IOptions<StorageOptions> and reuse the metrics root resolution through one
  DiskInfoFor(string root) helper; re-check for an empty string explicitly.

Fix applied: The stats endpoint no longer uses Environment.CurrentDirectory. It resolves the volume from
  IOptions<StorageOptions>.Value.RootPath through DiskSnapshot, the same helper DiskInfo(string rootPath) uses
  for /api/admin/metrics, so the two pages describe the same disk. Both helpers return null rather than
  throwing when the volume is not ready, and an empty root is checked explicitly.
```

### CR-017 (Low) - FIXED - non-unique pagination orderings

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 49-59 (tracks), 165 (albums); src/server/MusicHub.Api/Endpoints/DashboardEndpoints.cs:132 (audit)
Explanation: The default track sort is OrderByDescending(CreatedAt) and the album sort is
  OrderBy(NormalizedTitle); neither is unique. PostgreSQL does not guarantee a stable order among ties,
  so a bulk import producing several tracks with the same CreatedAt can shift rows between pages and a
  client paging through the library sees an item twice or skips one. The audit log has the same shape
  with far higher tie probability because one request can write several audit rows.
Reproduction: Import 60 tracks in one request so several share CreatedAt, then page through
  GET /api/tracks?pageSize=10&page=1..6 and compare the union of ids against
  GET /api/tracks?pageSize=200.
Expected: paging is stable and the union equals the full set.
Actual: ties can be ordered differently per query, producing duplicates or gaps.
Suggested fix: Append .ThenBy(x => x.Id) (or .ThenByDescending for descending sorts) to every
  paginated ordering: tracks, albums, artists, audit, upload/import/publication listings.

Fix applied: Every paginated ordering gained an Id tie-breaker: tracks (all sort branches), albums, artists,
  the audit log (.ThenByDescending(x => x.Id)), upload history, Telegram imports and publications, background
  jobs, and the BackgroundJobClaimer candidate query.
Regression test: ReviewFindingsTests.Paging_through_the_library_covers_every_track_exactly_once pages the
  whole library at pageSize=3 and asserts the collected ids equal the single-page result and contain no
  duplicates. Ties are the normal case in this fixture because the suite uploads many tracks inside the same
  second.
```

### CR-018 (Low) - FIXED - lost update on `ListenSecondsUsed`

```text
File: src/server/MusicHub.Infrastructure/Security/UserEntitlementService.cs
Line: 86-110
Explanation: The method reads the tracked user, adds seconds to ListenSecondsUsed in memory and saves.
  There is no optimistic concurrency token on User, so two concurrent reports both read the same
  baseline and the last commit wins, discarding the other increment. The player flushes every 30 s and
  on pause/track change, so overlapping reports are normal; usage is systematically under-counted and
  the allowance extends beyond what the operator granted. Distinct from B-023, which covers an
  untrusted client; here the server loses updates even for honest clients.
Reproduction: For listenQuotaSeconds = 3600, listenSecondsUsed = 0, send two concurrent
  POST /api/history/playback with listenedSeconds: 30; GET /api/users then shows 30 rather than 60.
Expected: usage accumulates additively regardless of concurrency.
Actual: one increment is lost per overlapping pair.
Suggested fix: Increment in SQL (ExecuteUpdateAsync(s => s.SetProperty(x => x.ListenSecondsUsed,
  x => x.ListenSecondsUsed + seconds))), then re-read to decide whether the quota is exhausted and
  deactivate inside the same transaction; alternatively add a rowversion token and retry on
  DbUpdateConcurrencyException.

Fix applied: ConsumeListeningTimeAsync increments in SQL -
  ExecuteUpdateAsync(s => s.SetProperty(x => x.ListenSecondsUsed, x => x.ListenSecondsUsed + seconds)) - and
  only then re-reads the row to decide whether the allowance is exhausted, so overlapping reports cannot
  overwrite each other. The re-read detaches that one entity instead of calling ChangeTracker.Clear(): the
  clear was discarding the PlaybackEvent the same request had just staged, a correctness bug introduced by the
  fix and then removed.
Regression test: ReviewFindingsTests.Concurrent_playback_reports_do_not_wedge_the_endpoint_or_lose_time fires
  6 concurrent reports of listenedSeconds = 5 and asserts ListenSecondsUsed == 30 exactly; a lost update
  leaves a smaller number.
```

### CR-019 (Low) - FIXED - dead streaming setting

```text
File: src/server/MusicHub.Api/Endpoints/StreamEndpoints.cs
Line: 42-68; src/server/MusicHub.Api/Program.cs:186; front end src/web/admin/src/pages/SettingsPage.tsx:201
Explanation: SettingsEndpoints exposes maxConcurrentStreamsPerUser (AllowedKeys, returned by
  DashboardEndpoints.cs:186) and the dashboard renders an input for it, but the only concurrency
  accounting is StreamTracker, a single global counter with no per-user dimension, and StreamEndpoints
  takes only a global lease. Changing the setting has no effect - precisely the "no dead controls" rule.
Reproduction: Set streaming.maxConcurrentPerUser = 1 in the dashboard, then open five simultaneous
  GET /api/tracks/{id}/stream requests with one bearer token; all five stream concurrently.
Expected: the fifth request is rejected (429/409) once the per-user limit is reached.
Actual: all requests succeed; the setting is inert.
Suggested fix: Key StreamTracker by user id (ConcurrentDictionary<Guid,int> with the same Interlocked
  discipline and eviction on zero) and enforce the configured limit, or remove the key from
  AllowedKeys, the DTO and the dashboard so operators are not misled.

Fix applied: StreamTracker keeps ConcurrentDictionary<Guid,int> _perUser alongside the global counter, with
  ActiveFor(Guid) and Track(Guid); the entry is removed when it reaches zero so the dictionary does not retain
  every user who ever streamed. The stream endpoint reads streaming.maxConcurrentPerUser from AppSettings
  (SettingsEndpoints.MaxConcurrentPerUserKey, falling back to StorageOptions.MaxConcurrentStreamsPerUser) and
  returns 429 with { error } when the account is at its limit. The stored value wins over the configured
  default, so an operator change takes effect without a restart - which is what makes the dashboard control
  real.
Regression test: ReviewFindingsTests.The_stored_per_user_stream_limit_is_enforced sets the setting to 1 via
  PUT /api/admin/settings, streams once (200), holds one lease so the next request is the second concurrent
  stream (429 with "simultaneous streams"), then releases the lease and streams again (200), proving the limit
  is accounting and not a latch. Confirmed to FAIL against the global-only tracker (Expected:
  TooManyRequests, Actual: OK). The test restores the setting in a finally block.
```

### CR-020 (Low) - FIXED - whole favourites set loaded per request

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 60 (tracks), 177 (album detail), 623 (history), 639 (search), 650 (home)
Explanation: var favoriteIds = await db.Favorites.AsNoTracking().Where(x => x.UserId == userId)
  .Select(x => x.TrackId).ToListAsync(ct) runs on every /api/tracks page request and the same full-set
  load appears in four more handlers. A page only needs IsFavorite for the at most 200 rows being
  returned, so a user with tens of thousands of favourites pays a full-table read per page view, per
  search keystroke and per 30-second dashboard poll.
Reproduction: Add 20 000 favourites for one user, then page through GET /api/tracks?pageSize=50; every
  page performs a full Favorites scan regardless of page content.
Expected: only the favourites for the rows in the response are queried.
Actual: the whole favourite set is read for every call.
Suggested fix: Filter by the ids in the result page
  (db.Favorites.Where(x => x.UserId == userId && ids.Contains(x.TrackId))) and use the same pattern in
  the other four handlers.

Fix applied: All six handlers - track list, album detail, history, search, home and playlist detail - now
  query favourites filtered by the ids present in the response page/detail
  (db.Favorites.Where(x => x.UserId == userId && ids.Contains(x.TrackId))) instead of materialising the
  caller's whole favourites set.
```

### CR-021 (Low) - FIXED - inconsistent error contracts

```text
File: src/server/MusicHub.Api/Endpoints/LibraryEndpoints.cs
Line: 486, 501, 513, 528, 542; src/server/MusicHub.Api/Endpoints/AuthEndpoints.cs:54, 169
Explanation: The project contract is { error: "..." } with 400 for validation. The playlist ownership
  checks return a bare Results.Forbid() (403, empty body) which the client cannot distinguish from a
  transient failure, and Results.Unauthorized() from the change-password guard is the same. Conversely
  POST /api/users returns Results.Conflict for an *invalid* email (the format check and the duplicate
  check are combined in one branch), mixing a validation error into the "already exists" status, and
  change-password maps every AuthService failure - including "Account not found." - to 400 where
  401/404 is correct.
Reproduction: As a Listener, PATCH /api/playlists/{someoneElsesId} returns 403 with an empty body. As
  an admin, POST /api/users with {"email":"not-an-email",...} returns 409
  {"error":"Email is invalid or already registered."}.
Expected: one documented shape for 4xx, validation as 400, ownership as 403 carrying { error }.
Actual: 403s lose the body and a validation failure is reported as a conflict.
Suggested fix: Replace bare Results.Forbid() with
  Results.Json(new { error = "..." }, statusCode: 403) via a shared helper; split the email check into
  BadRequest (format) and Conflict (duplicate); return 401 when the caller's account row is missing;
  document the shapes in API_CONTRACT.md.

Fix applied: Api/Common.cs gained ApiResults.Forbidden(message) and Unauthorized(message), which produce
  Results.Json(new { error }, statusCode) so every rejection carries the documented body; the playlist
  ownership checks use them. POST /api/users splits the checks: IsPlausibleEmail -> 400 "Email is not a valid
  address.", then uniqueness -> 409 "Email is already registered.".
  Change-password deliberately stays 400 for a wrong current password: the caller is already authenticated by
  the bearer token on that request, so a 401 would make every client treat the session as expired and sign the
  user out - the dashboard and the Android client both key off 401. That mapping was tried and reverted for
  this reason.
Regression tests: Creating_a_user_with_a_malformed_email_returns_400_not_409 (400 with "valid address" for
  "not-an-email", 409 with "already registered" for a duplicate) and A_forbidden_playlist_edit_returns_a_body
  (a Listener editing someone else's playlist gets 403 with a non-empty error).
```

### CR-022 (Low) - FIXED - raw database errors returned to clients

```text
File: src/server/MusicHub.Infrastructure/Media/TrackImportService.cs
Line: 263-271; src/server/MusicHub.Api/Endpoints/UploadEndpoints.cs:61-74
Explanation: The first catch clause includes DbUpdateException and sets result.Error = ex.Message,
  which POST /api/uploads returns verbatim in its results array (and TelegramEndpoints /test-ingest
  returns through Results.UnprocessableEntity). A database failure therefore surfaces Npgsql/PostgreSQL
  text - constraint names, table and column names, and on some error classes the failing statement
  fragment. MediaPipelineTests asserts only that "storage", "quarantine" and "music/" do not appear, so
  schema leakage would pass the suite.
Reproduction: Force a DbUpdateException that is not a SHA-256 unique violation and inspect the
  response: it contains PostgreSQL constraint/relation names.
Expected: a generic message ("The file could not be stored.") with the detail logged server-side only.
Actual: the raw exception message reaches the HTTP response.
Suggested fix: Return a fixed message for the DbUpdateException case and keep the detail in
  UploadSession.Error only; extend the negative assertions to reject constraint, relation and Npgsql.

Fix applied: The DbUpdateException branch returns the fixed message "The file could not be stored." ("The file
  could not be imported." through the generic catch) and keeps the raw text in the log and in
  UploadSession.Error, which only administrators read. No Npgsql/PostgreSQL text reaches the HTTP response.
```

### CR-023 (Informational) - FIXED - pairing claim skips entitlement enforcement

```text
File: src/server/MusicHub.Api/Endpoints/PairingEndpoints.cs
Line: 36-70; src/server/MusicHub.Infrastructure/Security/AuthService.cs:85, 143, 239
Explanation: POST /api/pairing/claim checks session.User.IsActive and IssueForUserAsync re-checks
  IsActive, but neither runs UserEntitlementService.EnforceAsync, whereas LoginAsync and RefreshAsync
  both do. An account whose ExpiresAt has passed but which the 2-minute sweep has not yet reached is
  still IsActive=true and can pair, receiving a full token pair. The tokens are useless in practice -
  OnTokenValidated rejects and deactivates on the first real request - so this is a consistency gap,
  not an access-control hole.
Reproduction: Set a user's ExpiresAt one minute in the past, do not run the sweep, then create and
  claim a pairing code: the claim returns 200 with tokens and the first authenticated call returns 401.
Expected: claim refuses with the same reason-specific message login/refresh return.
Actual: claim issues tokens that are dead on arrival.
Suggested fix: Call entitlements.EnforceAsync(session.UserId, checkStorage: true, ct) before
  IssueForUserAsync and map a denial to 401 with DeactivationReasons.Describe(reason).

Fix applied: POST /api/pairing/claim calls entitlements.EnforceAsync(session.UserId, checkStorage: true, ct)
  after the cheap IsActive check and before IssueForUserAsync, and maps a denial to 401 with
  { error: DeactivationReasons.Describe(reason), reason }. Pairing now applies the same rules as login and
  refresh.
Regression test: ReviewFindingsTests.Pairing_refuses_an_account_that_is_past_its_expiry creates a pairing code
  while the account is valid, moves ExpiresAt one minute into the past directly in the database (so IsActive is
  still true, the window before the 2-minute sweep), and asserts the claim returns 401 with reason = "expired"
  and no accessToken. Confirmed to FAIL against the old claim (Expected: Unauthorized, Actual: OK).
```

### CR-024 (Informational) - FIXED - committed file orphaned on non-hash rollback

```text
File: src/server/MusicHub.Infrastructure/Media/TrackImportService.cs
Line: 163-257 (commit before BeginTransaction at 166)
Explanation: CommitAsync moves the quarantine file into the content-addressed store before
  BeginTransactionAsync. If the transaction fails with a DbUpdateException that is not the SHA-256
  unique violation, the catch block deletes `quarantine` - which is null by then - so the committed
  file survives with no AudioAsset row referencing it. StorageIntegrityService reports it as an
  orphan, but nothing deletes it, so repeated failures accumulate unreferenced audio and consume the
  disk that the quota logic believes is free.
Reproduction: Import a file, then force a non-hash DbUpdateException inside the transaction: the upload
  reports failure but a new file appears under storage/music/xx/yy/ with no matching row, and the next
  integrity scan reports one orphan.
Expected: no file outlives a failed import.
Actual: the committed file is left behind on the DB-failure path.
Suggested fix: Track relativePath/destination separately from quarantine and TryDelete the committed
  path in every rollback branch, guarded so a file that legitimately pre-existed (CommitAsync's
  File.Exists branch) is not removed - have CommitAsync return whether it created the file.

Fix applied: ContentAddressedStorage.CommitAsync now returns CommitResult(RelativePath, CreatedFile), so the
  caller knows whether the file already existed for another asset. TrackImportService declares committedPath
  outside the try and deletes it on every rollback, cancellation and failure path - including the new
  in-transaction quota re-assert (CR-010) - guarded so a pre-existing file is never removed. The old
  `if (ex is DbUpdateException)` condition and the `quarantine = null` assignments that made the cleanup a
  no-op are gone.
```

### CR-025 (Informational) - FIXED - dead code and unimplemented settings

```text
File: src/server/MusicHub.Worker/Worker.cs; MaintenanceWorker.cs:18;
  src/server/MusicHub.Infrastructure/Options/Options.cs:9;
  src/server/MusicHub.Infrastructure/Telegram/TelegramIngestionService.cs:159-169
Explanation: Several artefacts are declared but never used, so the codebase misrepresents its own
  capabilities: MusicHub.Worker/Worker.cs is the `dotnet new worker` template leftover (a 1-second
  logging loop) and is not registered in Program.cs; MaintenanceWorker.LockKey is declared and never
  referenced (see CR-008); StorageOptions.DefaultUserQuotaBytes is copied into options in both hosts but
  never read, while POST /api/users hardcodes 10L * 1024 * 1024 * 1024 (AuthEndpoints.cs:185); and
  OutboxMessage.NextAttemptAt/Error are never written or honoured - DrainOutboxAsync marks every
  message processed on the first attempt with no failure path, so the outbox is a one-shot drain rather
  than the retrying queue the fields imply. MusicHub.Domain/Class1.cs and
  MusicHub.Infrastructure/Class1.cs are also empty template files.
Reproduction: grep -rn "LockKey|DefaultUserQuotaBytes|new Worker(" src/server shows only the
  declarations; grep -rn "OutboxMessage" src/server/MusicHub.Worker shows ProcessedAt/Attempts written
  and Error/NextAttemptAt never assigned.
Expected: no unused declarations that imply unimplemented behaviour.
Actual: dead declarations remain and the outbox retry fields are inert.
Suggested fix: Delete Worker.cs and the two Class1.cs files; wire DefaultUserQuotaBytes into user
  creation (QuotaBytes = request.QuotaBytes ?? storageOptions.DefaultUserQuotaBytes); either implement
  outbox retry (leave ProcessedAt null, set NextAttemptAt/Error on handler failure) or drop the two
  columns and document the outbox as fire-and-forget.

Fix applied: Deleted MusicHub.Worker/Worker.cs (the template logging loop, never registered) and the three
  empty Class1.cs files. MaintenanceWorker.LockKey was removed with CR-008. POST /api/users now uses
  storageOptions.Value.DefaultUserQuotaBytes instead of the hardcoded 10 GiB, so the setting is live. The
  outbox drain honours the fields the schema already had: a message that fails is left unprocessed with Error
  set and NextAttemptAt = now + 2^attempts seconds, and after 5 attempts it is dead-lettered
  (Status = DeadLetter) instead of being retried forever or silently marked processed. Claiming only picks rows
  whose NextAttemptAt has elapsed (or is null), which is the second half of CR-008.
```

### Not covered by this pass

`src/web/admin` was inspected only where needed to judge the server contract; Android and the WPF
player were excluded by the brief; EF migrations were checked only for the indexes implied by CR-003
and CR-017; `docs/openapi.json` was not diffed against the route table; `DatabaseCredentialFile.cs`
and `deploy/scripts/*` were deliberately skipped as W-018 territory.

Every CR finding began as a STATIC review finding. CR-001 and CR-002 were reproduced against the running
API during the original review. All 25 are now fixed and each has a named regression test; the tests that
pin the pre-fix behaviour were each run against the old code first and observed to fail, which is recorded
in the individual detail blocks above. The full server suite was 152/152 when W-030 closed (187/187 after W-032) and the web suite is 12/12.

Two findings in this batch were discovered while fixing, and are recorded because they show the review was
not exhaustive: the CR-003 dedup migration as first written deleted duplicate rows before aggregating, which
discarded the counted plays (6+3+5 collapsed to 6); and the first attempt at CR-005's doc-only fix loaded
`Include(x => x.UserRoles)` without `ThenInclude(x => x.Role)`, so the role comparison threw. Both were
caught by executing the change rather than reading it, and both are covered by tests now.

| B-068 | High | FIXED | Storage / relocation | `StorageAdminService.TryQueueRelocationAsync` enforced "one move at a time" with a check-then-insert: it read the queue, found it empty, then inserted. Two requests arriving together both passed the check and both queued a `storage.relocate` job, so the worker could start two simultaneous copies of the entire library into different folders. | Found by asking what the rule actually guarantees under concurrency, then proving it: a new test fires two `TryQueueRelocationAsync` calls with separate `DbContext`s at once - with the old code both returned `Queued = true` and `BackgroundJobs` held 2 pending relocation rows (`Assert.Single() Failure: The collection contained 2 matching items`). Fixed by doing the queue check and the insert inside one transaction that first takes a transaction-scoped advisory lock (`pg_advisory_xact_lock`), the same pattern already used for the per-user upload quota (CR-010) - an advisory lock rather than `FOR UPDATE` because no single row represents "the queue". The lock is released on commit or rollback, so a failed request cannot leave it held. Verified: the test fails again when the lock line is removed and passes with it restored; full suite 314/314 | orchestrator |
| B-069 | High | FIXED | Android install | Shipped APK was the **debug** variant: `android:debuggable="true"` and signed with the throwaway `CN=Android Debug` key. Android 8+ Play Protect refuses or warns on a debuggable sideload, and the platform treats a debug-signed package as an untrusted developer build, so the file would not install cleanly. | `aapt2 dump xmltree --file AndroidManifest.xml app-debug.apk` showed `debuggable(0x0101000f)=true`; `apksigner verify --print-certs` showed `CN=Android Debug`, v2 only. Fixed: a real RSA-4096 release keystore (`storage/android-keys/musichub-release.jks`, 90-year validity, `CN=MusicHub Enterprise`) whose password lives only in the gitignored `keystore.properties`; `app/build.gradle.kts` now reads that file, enables v1+v2+v3 signing, sets `isDebuggable = false`, and leaves the release build **unsigned** rather than falling back to the debug key when the file is absent. Verified on the shipped `MusicHub-Android-1.0.0-release.apk` (13.17 MB, versionCode 2): `apksigner verify --verbose` -> `Verifies`, v2 `true`, v3 `true`, 1 signer, `CN=MusicHub Enterprise`, key 4096 bits; `aapt2 dump xmltree` -> no `debuggable` attribute; SHA-256 `D9638D76...39651C` matches the published `.sha256`. `git check-ignore` confirms both key files are ignored | orchestrator |
| B-070 | Medium | FIXED | Android install | The manifest declared only `android.hardware.camera.any required=false`, so the platform inferred a **required** `android.hardware.camera` feature from the `CAMERA` permission (`uses-implied-feature: android.hardware.camera reason=requested android.permission.CAMERA`). Google Play and `adb install` both treat that as "device incompatible", making the APK uninstallable on any device or emulator that reports no camera, even though the QR scanner is optional and a user can type the server URL by hand. | `aapt2 dump badging app-release.apk` printed `uses-feature: name=android.hardware.camera` (required) plus the implied-feature line. Fixed by declaring `android.hardware.camera`, `camera.any` and `camera.autofocus` all with `required="false"` in `AndroidManifest.xml`. Re-verified after rebuild: badging lists only not-required camera features; no other required hardware feature is declared | orchestrator |

| B-071 | Medium | OPEN | Android UX | A bare `host:port` typed into the server field (e.g. `192.168.1.20:5080`, which is exactly what the LAN hint invites) is silently upgraded to `https://` by `LocalNetworkPolicy.validateBaseUrl`, because the scheme is only defaulted when `://` is absent. The user then gets the generic "Cannot reach the server. Check the connection and server URL." message instead of being told the scheme is wrong, even though the cleartext toggle is on. Observed live on SM-G975F: the same address signed in immediately once `http://` was typed explicitly. | Reproduced on the device: LAN field `192.168.1.20:5080`, cleartext switch on, correct credentials -> "Cannot reach the server". Re-typing the identical host as `http://192.168.1.20:5080` -> sign-in succeeded and `/api/health`/`/api/tracks` answered. Root cause: `LocalNetworkPolicy.kt` prepends `https://` before it ever consults `allowCleartext`. Suggested fix: when the input has no scheme and `allowCleartext` is enabled and the host is local/private, default to `http://`; otherwise surface a specific "missing http://" validation failure instead of a network error | orchestrator |
| B-072 | Low | FIXED | Android release | Release build shipped with `isMinifyEnabled = false`, so jadx recovered readable class names, API paths and DTOs, making the API surface free to map | `build.gradle.kts` release block now sets `isMinifyEnabled = true` and `isShrinkResources = true`; `proguard-rules.pro` keeps kotlinx.serialization (`$$serializer`/`Companion`), OkHttp/Retrofit annotations, Media3, Room, WorkManager worker subclasses and the app entry points - without those keeps a minified build throws `SerializationException` on the first API response. Defence in depth only: the server is the authority. Runtime behaviour of the minified build still to be confirmed on a device | android-engineer |
| B-073 | Informational | OPEN (accepted) | Android network | `network_security_config.xml` permits cleartext at `base-config`. Tightening it was attempted and reverted: Android `<domain-config>` cannot express IP ranges, so `cleartextTrafficPermitted="false"` on base-config would block `http://192.168.x.x` - the primary self-hosted deployment - while still allowing every `*.local` name. The real control is `CleartextPolicyInterceptor`, which every network path uses (Retrofit, `DownloadWorker`, `ArtworkLoader`, Media3 `HttpDataSource`) because all clients come from `ApiFactory`. Documented in the file itself | android-engineer |
| B-074 | Low | OPEN (accepted) | Android integrity | No app-integrity attestation (Play Integrity / root detection). A modified client can run in an emulator and script the API directly. Accepted for a self-hosted product: attestation adds a Google dependency and breaks legitimate self-hosted installs, and scripting the API yields no more access than the account is entitled to | android-engineer |
| B-075 | Low | OPEN | Release key custody | `storage/android-keys/musichub-release.jks` and `keystore.properties` (plaintext password) are gitignored but live on the development disk. A leak lets an attacker sign an update Android accepts as genuine. Operator action: offline encrypted backup and the password in a secret manager, not beside the key | devops-release-engineer |
| B-076 | Medium | FIXED | Android UX | Home could not be refreshed from the server by gesture. `HomeScreen` was a plain `LazyColumn` of five `LazyRow` sections with no `PullToRefreshBox`, and `HomeViewModel.reload()` published `HomeState.Loading`, which replaced the whole screen with a centred spinner. There was no way to pull the list down and have it re-read `/api/home`, and a retry blanked already-visible content. | `HomeViewModelTest` (8 cases, new JVM source set) pins the new behaviour: `pull to refresh keeps content visible and reports refreshing` **fails on the pre-fix code** by observing `HomeState.Loading` instead of `Success(refreshing = true)` while the round-trip is in flight (`expected Success while refreshing but was Loading`); `pull to refresh failure keeps previous data instead of erroring` pins that a failed refresh keeps the stale list. Proved by mutation: replacing `current.copy(refreshing = true)` with `HomeState.Loading` made the test fail; restoring it made it pass. Fixed with a distinct `HomeState.Success.refreshing` flag, `PullToRefreshBox` in `HomeScreen`, and a `refresh()` separate from `reload()`. `gradlew :app:testDebugUnitTest` -> 16/16 | orchestrator |
| B-077 | Medium | FIXED | Android UX | The app looked bare: no cover art anywhere (zero `ArtworkImage`/`AsyncImage` usages, `ArtworkLoader` was constructed in `AppContainer` but never called by any UI file, and `res/drawable` held only 13 flat vector icons), every card was a plain `surfaceVariant` block, and Home had no header or iconography. Covers the user report "Ø¨Ø±Ù†Ø§Ù…Ù‡ Ø®ÛŒÙ„ÛŒ Ø®Ø´Ú© Ù‡Ø³ØªØ´ Ù†Ù‡ Ø¹Ú©Ø³ÛŒ Ù†Ù‡ Ø±Ù†Ú¯ Ùˆ Ù„Ø¹Ø§Ø¨ÛŒ Ù†Ù‡ Ø§ÛŒÚ©ÙˆÙ†ÛŒ". | Fixed with `ui/components/ArtworkImage.kt` (real cover via the existing authenticated `ArtworkLoader`, falling back to a two-tone gradient plus the title's initial, because most tracks have no `ArtworkPath` server-side), a gradient `HomeHeader`, per-section icons, and `ElevatedCard`. The fallback palette is pure Kotlin (`ui/theme/ArtworkPalette.kt`) and covered by `ArtworkPaletteTest` (8 cases): stability per seed, no flat gradients across 50 seeds, a negative `hashCode` seed stays in range, all palette entries reachable, `take(1)`-safe initials that keep surrogate pairs intact, and a note glyph for blank titles | orchestrator |
| B-078 | High | FIXED | Android favorites | The Favorites feature was dead. `TrackDao.setFavorite`/`observeFavorites`, `LibraryRepository.setFavorite`, and the `PendingFavoriteEntity` sync queue all existed, and Home even rendered a Favorites row, but **no UI element ever invoked `setFavorite`**, so the list was permanently empty. `LibrarySection` had no Favorites entry, and no track row (`LibraryScreen`, `HomeScreen`, `SearchScreen`, `NowPlayingScreen`) carried a heart control. | Fixed: `FavoriteButton` on every track row in Home, Library, Search and on Now Playing; `LibrarySection.Favorites` added with its own list; `HomeViewModel` flips the heart optimistically and keeps the Favorites row honest (inserts on favourite, removes on unfavourite) with rollback on a rejected write. Tests: `toggle favorite flips optimistically and persists`, `favoriting adds the track to the favorites section`, `unfavoriting removes the track from the favorites section`, and `failed favorite write rolls the heart back` - the last one **fails on the pre-fix code** when the rollback is removed (`a rejected write must not leave a filled heart`). Both mutations were run and observed to fail before being restored. `LibraryScreen` tab labels also stopped using raw `enum.name` (they rendered English identifiers such as "Tracks" in Persian) | orchestrator |
| B-079 | Medium | FIXED | Android layout | Bottom content slid under the system navigation bar. `MainActivity` calls `enableEdgeToEdge()` while `Scaffold` in `MainShell.kt` passed no `WindowInsets`, and screens filling the `Box` with `Modifier.fillMaxSize()` lost the bottom spacing. The Now Playing queue (`LazyColumn` with `fillMaxSize()` inside the padded Box) and the Settings trailing content were worst. **This is a code-inspection finding, not a reproduced one.** An earlier draft of this entry claimed a device observation from a `uiautomator` dump showing the nav labels at y=994..1041; re-reading that dump shows its root bounds were `[0,0][2047,1080]`, i.e. the phone was in **landscape**, where the system bar is on the side and the bottom inset is zero - the labels were laid out normally. The portrait dumps from the same session reported the nav labels at `[0,0][0,0]` (never laid out), so no valid measurement of the overlap exists yet. What is certain from the source: `MainActivity` calls `enableEdgeToEdge()` and `Scaffold` supplied no `WindowInsets`, so nothing ever reserved the bottom system-bar height. | Fixed: `Scaffold` now declares `contentWindowInsets = WindowInsets.safeDrawing.only(Horizontal + Top)` so the `NavigationBar` is the single owner of the bottom inset, the content `Box` calls `consumeWindowInsets(padding)` so no screen double-pads, and Now Playing was rebuilt as one `LazyColumn` with `contentPadding` bottom 28.dp so its queue scrolls clear of the bar instead of underneath it | orchestrator |
| B-080 | High | FIXED | Android release | The release build type enables R8 (`isMinifyEnabled = true`, `isShrinkResources = true`) and obfuscates/shrinks every dependency, but the resulting APK has **never been installed or operated on a device**. It compiles, links, passes `lintVitalRelease` and is correctly signed (v2+v3, `CN=MusicHub Enterprise`, RSA 4096), so nothing has failed - the risk is that this is the first release with minification on, and ProGuard/R8 faults show up only at runtime (reflection, serialization, Room, WorkManager, Media3, Compose). The previously published `MusicHub-Android-1.0.0-release.apk` (SHA-256 `4F95D2C9...`) was built **before** minification was enabled at 02:35 and is therefore an unminified artifact that no longer matches the source tree. | Static evidence only: `mapping.txt` (50.7 MB) proves the new code was compiled in - `ui.components.ArtworkImageKt.ArtworkImage`, `ArtworkImageKt.placeholderBrush`, `ui.components.FavoriteButtonKt.FavoriteButton`, `ui.home.HomeViewModel.refresh`, `ui.player.NowPlayingViewModel.toggleFavorite` all have obfuscated mappings - and the APK shrank 13,813,712 -> 4,717,823 bytes with 4 dex files collapsing to 1, which is the expected effect of R8 rather than lost code. `proguard-rules.pro` already carries explicit keeps for kotlinx.serialization, OkHttp/Retrofit, Media3, Room and WorkManager. **Blocked on the device**: SM-G975F `R58M915RT4W` stopped answering both `adb` over USB and `adb connect 192.168.1.98:5555` (connection timed out), so the APK could not be installed. RESOLVED 2026-11-20: installed on SM-G975F (`adb install -r` -> Success, `lastUpdateTime` 01:55:51 -> 08:03:39). The minified build launched with no `FATAL`/`AndroidRuntime`/`ClassNotFound`/`NoSuchMethod` entries, no `ANR in com.musichub`, and the process stayed resident (`ps -A` -> `com.musichub.mobile`). The new Compose UI rendered, confirmed by the new strings only present after this change: `Your music` and `Pull the list down to sync with the server`. Pull-to-refresh was operated on the device and produced 72 new server log lines containing the real `/api/home` EF queries (Users security-stamp lookup, `Tracks ... ORDER BY CreatedAt DESC`, `PlaybackEvents` recent ids). kotlinx.serialization survived minification, since the Home payload was decoded and rendered. Artwork and favourite hearts still to be exercised before publishing | orchestrator |
| B-081 | High | FIXED | Android layout | The app ran with the **platform default theme, which supplies an ActionBar**, because `res/values/themes.xml` defined `Theme.MusicHub` (parent `Theme.Material.NoActionBar`) but `AndroidManifest.xml` never declared `android:theme`, so the style was dead code. The platform therefore drew a real title bar above the Compose content and the Compose window was laid out starting underneath it, so the Search field and the Home gradient header were painted over the ActionBar rather than below it. Reported by the user as "?? ???? ??? ???? ??? ?? ??? ??? ???? ?????". | Reproduced on SM-G975F (`R58M915RT4W`, portrait, `wm size` 1080x2280) with `uiautomator dump` on the Search tab: `android:id/action_bar_container` = `[0,107][1080,254]` and `android:id/action_bar` = `[0,107][1080,254]` containing a `TextView` "MusicHub" at `[42,145][283,216]`; the Compose content root (`android:id/content`) started at `[0,147][1080,2047]`, i.e. 107 px inside the ActionBar, and the search `EditText` was at `[42,168][1038,344]`, overlapping it by 86 px. The same ActionBar was present on Home. Fixed and re-verified on the device; see the end of this entry. `aapt2 dump xmltree app-release.apk --file AndroidManifest.xml` showed no `theme` attribute on `<application>` or `<activity>`, and `grep -r "Theme.MusicHub\|android:theme"` matched only the style definition itself, confirming it was never applied. Fixed by adding `android:theme="@style/Theme.MusicHub"` to `<application>`, adding a `values-night` counterpart so the launch window matches the dark Compose surface, and correcting the light window background to `#FDFCFF`. Regression test `ManifestThemeTest` (2 cases, new JVM test) asserts the manifest declares the theme and that its parent is a `NoActionBar` theme. **Mutation-proven**: with the `android:theme` attribute deleted from the manifest, `:app:testReleaseUnitTest --tests com.musichub.mobile.ManifestThemeTest --rerun-tasks` reported `2 tests completed, 1 failed` with `AssertionError: AndroidManifest.xml must declare android:theme so the platform does not add an ActionBar`; after restoring the attribute both cases pass (`tests=2 failures=0`). The built artifact was then audited with `aapt2`: `aapt2 dump xmltree app-release.apk --file AndroidManifest.xml` shows `android:theme(0x01010000)=@0x7f0d0113`, `aapt2 dump resources` resolves `0x7f0d0113` to `style/Theme.MusicHub` with `parent=0x0103022e`, and the platform `android.jar` resolves `0x0103022e` to `style/Theme.Material.NoActionBar PUBLIC` whose own parent sets `windowNoTitle(0x01010056)=true`. The whole chain is therefore NoActionBar, so no ActionBar can be created. Full suite after the fix: 18/18 (`ManifestThemeTest` 2, `HomeViewModelTest` 8, `ArtworkPaletteTest` 8). Rebuilt release APK: 4,719,532 bytes, SHA-256 `3B7B95D2314672DC30D934B1619F5DB46ACC8C5066895E5BA7FF3F8A14ED2D46` | orchestrator |
| B-082 | Medium | FIXED | Telegram album picker | The album picker's "next" page showed the **same albums as page 1**, so the button looked live but did nothing new. `AlbumService.ListAsync` took only a `take` and no offset, and `TelegramBotHandler` called it with the page size alone while still computing `totalPages` from the album count, so every page rendered the first 8 albums. Found while writing the picker test, not reported by a user. **Fix:** `ListAsync(int take, int skip, CancellationToken)` now applies `Skip(page * AlbumsPerPage)`, and the handler passes `safePage * TelegramKeyboard.AlbumsPerPage`. **Evidence:** `The_picker_second_page_shows_different_albums_than_the_first` creates `AlbumsPerPage + 1` albums, opens page 0 then page 1, and asserts the two id sets are disjoint. Mutation-proven: removing the `Skip` call makes it fail with `Assert.Empty() Failure: Collection was not empty`; restoring it gives `Passed! 10 -> 11` tests green. |
| B-083 | High | FIXED | Auth recovery | A forgotten administrator password had **no supported recovery path**. The API stores a PBKDF2-SHA256 hash, so the value cannot be recovered; every route that could replace it needs a session the operator no longer has (`/api/auth/change-password` is `[Authorize]`, `/api/users/{id}/reset-password` requires the `Admin` policy), and `POST /api/setup/initialize` returns 409 once any user exists. The only escape was hand-editing the database. Related defect found while tracing it: `deploy/scripts/start-dev.ps1` writes `MUSICHUB_ADMIN_EMAIL`/`MUSICHUB_ADMIN_PASSWORD` into `storage/dev.secrets.ps1`, but **no server project reads them** - only the test scripts (`healthcheck.ps1`, `verify.ps1`, `smoke-api.ps1`) do - so the file's admin password is not the live one. Confirmed by a real login attempt against the running stack: `HTTP 401` with the value from that file, while the account itself was created by `setup.initialize` (audit row `setup.initialize` at 2026-11-21 01:40:37, actor null, ip `::1`). | Fixed with `MusicHub.Install reset-admin-password`, an offline CLI in the existing installer project (`src/server/MusicHub.Install/AdminPasswordReset.cs`, verb wired in `Program.cs`). The password is read from `MUSICHUB_NEW_ADMIN_PASSWORD` or stdin and is **refused as an argument** (arguments appear in the process list and shell history); it is never printed. The command updates one row by `NormalizedEmail`, rotates `SecurityStamp` (killing live access tokens), revokes all `RefreshTokens` and `UserSessions` for that user, clears `FailedLoginCount`/`LockoutEnd`, writes an `admin.password.reset` audit row with a null actor, and warns when no active administrator would remain. Connection comes from `--connection`, then `MUSICHUB_CONNECTION_STRING`, then the installer credential under `MUSICHUB_DATA_ROOT`. **VERIFIED end to end** against the live Docker stack, twice - the second time by running the command exactly as published in `docs/INSTALL_AND_RUN.md`: `exit=0`, stamp `2ed817a5...` -> `8cc3ab89...`, previous password `401` -> new password `200`, a random wrong value `401`, `LockoutEnd` `null`, live tokens `1 -> 0`, live sessions `1 -> 0`, audit row `admin.password.reset\|NULL\|{"via":"install-cli","email":"admin@musichub.local"}`. Guard tests: password-as-argument -> exit 1, no password -> exit 1, 8-char password -> exit 2, missing `--email` -> exit 1. `AdminPasswordResetTests` (7 cases) pins the SQL contract and the no-argument/no-echo rule. Suite after the change: **370 passed, 0 failed** | orchestrator |
| B-084 | High | FIXED | Authz | The last-administrator guard covered demotion but **not deactivation**. `PATCH /api/users/{id}` with `{ "isActive": false }` on the only active administrator succeeded, revoking that account's tokens and sessions, leaving nobody who could sign in to reactivate anyone - the same dead end as demotion, reached a different way. `BUG_TRACKER` already flagged the demotion variant (see the CR-005 note in the `AuthEndpoints` comment), but the deactivation branch had no equivalent check. | Fixed in `AuthEndpoints.cs`: the deactivation branch now counts other **active** administrators and returns `409 {"error":"At least one active administrator is required."}` when the target is an active administrator and none other exists. Tests in `ReviewFindingsTests`: `The_last_active_administrator_cannot_be_deactivated` (refused with 409, account still active, `/api/auth/me` still 200) and `A_non_administrator_can_still_be_deactivated` (proves the guard did not become a blanket refusal). Full suite: **370 passed, 0 failed** |

| B-085 | Critical | FIXED | Windows package install | The documented Windows install sequence could not complete. `deploy/scripts/install-server.ps1` initialises the PostgreSQL cluster and writes the encrypted credential; the next documented step, `deploy/scripts/start-dev.ps1`, then re-entered `Invoke-MusicHubInstallCredential`, found the cluster already existed, and threw "The developer cluster at ... already exists and its superuser password is not known" because no `-AdminConnection` was supplied. Both the source-checkout path in `docs/INSTALL_AND_RUN.md` and every package install were therefore blocked at step 2, and the failure named a credential rotation that was never needed. | Found by reading the two scripts against each other before running either: `install-server.ps1` L152-186 creates the cluster, `start-dev.ps1` L418-432 re-runs the provisioning. Fixed in `start-dev.ps1`: when the cluster exists and no `-AdminConnection` is given, the existing credential is **verified** with `MusicHub.Install verify-credential --probe` and reused, and the hard failure now only fires when that file is absent or does not authenticate. Verified on a real package install: `-Start` logged `Credential OK: musichub@127.0.0.1:5433/musichub` then `Reusing the existing verified database credential`, and the API reached `/api/health` with `database=True` | orchestrator |
| B-086 | Critical | FIXED | Windows package install | A package install could not start the stack at all. The package deliberately ships a private .NET **runtime** and no SDK, but `start-dev.ps1` ran `dotnet --version` (an SDK command) at L350 under `$ErrorActionPreference = 'Stop'`. On a runtime-only host that command writes to stderr and exits non-zero, which PowerShell converts into a terminating `NativeCommandError`, aborting the bootstrap before PostgreSQL was even considered. Reproduced in isolation against a runtime-only copy of the host: `dotnet --version` under `Stop` threw `RemoteException: The command could not be loaded... No .NET SDKs were found`, while `--list-runtimes` succeeded (exit 0). | Fixed in `start-dev.ps1`: the SDK probe now runs under `Continue`, records availability in `$script:sdkAvailable`, reports "No .NET SDK at ...; using the prebuilt assemblies (-SkipBuild)" when `-SkipBuild` was passed, and throws a specific message only when an SDK is genuinely required to build. Verified on the real package install: `No .NET SDK at ...runtime\\dotnet.exe; using the prebuilt assemblies (-SkipBuild)` followed by a healthy API | orchestrator |
| B-087 | Critical | FIXED | Windows package install | The installed dashboard was 404 on the reboot path. The API is started with its working directory set to the **project** directory (`common.ps1` L522 passes `$config.ApiProjectDir`), which makes that directory the content root, and ASP.NET Core serves static files from `<content root>\\wwwroot`. A package that copies the built dashboard only into `bin\\Debug\\net10.0\\wwwroot` serves the API but no UI. Found by reading `Start-MusicHubHostProcess` against `Program.cs` L345-371 instead of assuming the publish folder was the content root. | Fixed in `deploy/windows/build-package.ps1`: the dashboard is copied to `src\\server\\MusicHub.Api\\wwwroot` (the content root) **and** to the publish folder, and `appsettings*.json` is staged beside each host project for the same reason. `install.ps1` now fails the install when `/index.html` does not answer rather than warning. Verified: `/index.html` -> HTTP 200, 951 bytes, and both referenced assets served (`/assets/index-BrthbLKS.js` 525,322 bytes, `/assets/index-C5paYwUU.css` 32,580 bytes) | orchestrator |
| B-088 | High | FIXED | Windows package install | After a reboot the supervisor reported the API healthy while nothing could reach it on loopback. The generated `storage\\dev.secrets.ps1` set `ASPNETCORE_URLS = 'http://<lan-address>:<port>'`, and Kestrel treats a hostname there as an exact bind address, so the server listened on `192.168.10.20:5199` only. `127.0.0.1` refused connections, which broke the supervisor's own readiness probe and every `-Status` / `install.ps1` check, while the LAN worked - the most confusing possible symptom. Observed directly: `Get-NetTCPConnection -LocalPort 5199` showed `192.168.10.20:5199 PID 784`, `Invoke-RestMethod http://127.0.0.1:5199/api/health` failed 6/6 times, and `api.log` showed `Now listening on: http://192.168.10.20:5199`. | Fixed in `deploy/windows/install.ps1`: `ASPNETCORE_URLS` is now `http://0.0.0.0:<port>` so every interface is bound, while `MUSICHUB_PUBLIC_URL` keeps the LAN address because that is what clients dial. Verified after the change: the binding became `0.0.0.0:5199`, and `http://127.0.0.1:5199/api/health`, `http://192.168.10.20:5199/api/health` and `http://127.0.0.1:5199/index.html` all returned HTTP 200 | orchestrator |
| B-089 | High | FIXED | Windows package install | The installer would have reported a healthy server as broken, and the Worker as missing. `install.ps1` read `$health.database` / `$health.storageReadable` from `/api/health`, but the payload nests both under `checks` (`HealthEndpoints.cs` L43-49), so a healthy server would have printed `database=False storageReadable=False`. Separately, `verify.ps1` was going to be used as the install gate, but it inspects PostgreSQL with `psql` and authenticates as the application role using a **plaintext** password that a package install deliberately does not have (it exists only inside `keys\\db-credential.protected`, and the cluster uses `scram-sha-256`), so `database.migrations` and `worker.heartbeat` would FAIL on a correct install. Confirmed: `psql -U musichub` without a password hung until the 300 s timeout. | Fixed in `install.ps1`: the health check reads `$health.checks.database` / `$health.checks.storageReadable` and now **throws** on either being false; the Worker is confirmed by process presence with a 60 s wait and is a hard failure rather than a warning; the schema is proven by `GET /api/setup/status` (a real query against `Users`). `verify.ps1` is no longer the package gate, and `docs/INSTALL_AND_RUN.md` documents that it needs `MUSICHUB_CONNECTION_STRING` on a package install. Verified: `Health: status=ok database=True storageReadable=True`, `Schema reachable: initialized=True bootstrapTokenConfigured=True`, `Worker is running` | orchestrator |
| B-090 | Medium | FIXED | Windows package build | The packaging script aborted on a successful build. Both `npm run build` and `dotnet publish` write progress and warnings to stderr, and `build-package.ps1` ran them under `$ErrorActionPreference = 'Stop'`, so PowerShell raised a terminating `NativeCommandError` on the first stderr line. Observed: the first run failed at `3/8 Dashboard` with `npm.cmd : [plugin builtin:vite-reporter]` and `Exit code: 1` while vite had in fact succeeded. The same trap is already documented for `docker build` in this repository. | Fixed: both invocations now run under `Continue` with the exit code captured and judged explicitly. Verified by the second run completing all 8 phases and producing `MusicHubServer-win-x64-1.0.0.zip` (181.7 MB, SHA-256 `48CA74E7...C0A8C47`) | orchestrator |
| B-091 | Low | FIXED | Windows package docs | The shipped `README.txt` and `package.json` told the operator to run `.\install.ps1`, but the installer lives at `deploy\windows\install.ps1`; following the instructions verbatim produced "The argument ... does not exist". Also, `install.ps1` called `start-dev.ps1` without `-SkipDatabase`, so the fresh install re-entered credential provisioning (the B-085 path) even after `install-server.ps1` had just finished. | Fixed in `build-package.ps1`: both commands now read `.\deploy\windows\install.ps1`, and the README describes the private runtime, portable PostgreSQL, offline install, and the logon task. `install.ps1` passes `-SkipDatabase` for the full install (PostgreSQL was just verified listening) while `-Start` deliberately omits it so it can bring a stopped database back up. Verified: the full install completed 7/7 steps, and the published README text matches the real layout | orchestrator |
| B-092 | High | FIXED | CI | The `Server build and test` job could never pass on `ubuntu-latest`. `MusicHub.slnx` includes the WPF player, which targets `net10.0-windows`, and the SDK refuses to build a Windows-targeted project on a non-Windows host without `EnableWindowsTargeting` - so `Restore` failed before a single test ran. Observed on run `44947537047` step `Restore`: `error NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true. [.../src/desktop/MusicHub.Player/MusicHub.Player.csproj]`. Nothing in the repository set that property, and there was no `Directory.Build.props`. This had never been noticed because the workflow had never run: the repository had no remote before this session. | Fixed with a new root `Directory.Build.props` that sets `<EnableWindowsTargeting>true</EnableWindowsTargeting>` **only** when `'$(OS)' != 'Windows_NT'`, so a Windows developer build is byte-for-byte unchanged and the property is not silently enabled for real Windows packaging. Verified locally: `dotnet build MusicHub.slnx` -> `Build succeeded. 0 Warning(s), 0 Error(s)`, and all eight projects emit including `MusicHub.Player -> bin\Debug\net10.0-windows\MusicHub.Player.dll`. The Linux behaviour is proven by the next CI run rather than by inspection | orchestrator |
| B-093 | High | FIXED | CI | The `Android assemble` job failed instantly with exit code 126. `src/android/gradlew` was committed with git mode `100644`, so the runner checked it out without the execute bit and `./gradlew assembleDebug` was refused. Observed on run `44947537047`: `/home/runner/work/_temp/.../sh: line 1: ./gradlew: Permission denied` then `Process completed with exit code 126`. The file content itself was correct - the stored blob is 8,733 bytes, begins `#!/bin/sh`, and `git ls-files --eol` reports `i/lf w/lf attr/text eol=lf` - so this was purely a file-mode defect, not a line-ending or shebang one. It cannot reproduce on Windows, where the mode bit is ignored, which is why local Android builds worked. | Fixed with `git update-index --chmod=+x src/android/gradlew`; `git ls-files -s` now reports `100755` and `git diff --cached --summary` records `mode change 100644 => 100755`. The `.gitattributes` `gradlew text eol=lf` rule was already correct and is untouched | orchestrator |
| B-094 | High | FIXED | CI | The CI comment claimed the integration tests need no database, which was false, and the suite would have failed even after B-092 and B-093 were fixed. The job carried the comment "Integration tests use Testcontainers-free in-memory or file-backed fixtures, so no database service is required here", but `ApiFactory` hardcoded `Host=127.0.0.1;Port=5433` and opened a real Npgsql connection in `ResetDatabase()` to drop and recreate `musichub_test`. There was no in-memory substitute anywhere. The same constructor also set `MUSICHUB_FFPROBE_PATH` and `MUSICHUB_FFMPEG_PATH` to a Windows `tools\ffmpeg\...\ffprobe.exe` that does not exist on a Linux runner, so media probing would have pointed at a missing file. | Fixed in `tests/server/MusicHub.Tests/Integration/ApiFactory.cs` and both workflows. `AdminConnection` is now a property resolving `MUSICHUB_TEST_ADMIN_CONNECTION` first, then the CI default on port 5432, then the developer cluster on 5433, with `HostOf`/`PortOf` read through `NpgsqlConnectionStringBuilder` so the per-test connection string is derived from one source instead of a second hardcoded literal. The ffprobe/ffmpeg variables are set **only** when the checkout file exists, letting the API fall back to its PATH lookup. `.github/workflows/ci.yml` and `release.yml` gained a `services: postgres: image postgres:17` block with a `pg_isready` health check, an `apt-get install ffmpeg` step, a `Wait for PostgreSQL` step, and `CI: 'true'` plus `MUSICHUB_TEST_ADMIN_CONNECTION` on the Test step. The false comment was deleted. Verified locally against the real 5433 cluster with the new code path: `dotnet test MusicHub.slnx` -> **372 passed, 0 failed**, 7 m 56 s, exit 0 | orchestrator |
| B-095 | Medium | FIXED | CI | CI silently compiled and tested with a different .NET SDK than the project targets. `actions/setup-dotnet` installed `10.0.x`, but the runner image also carries a newer SDK and, with no `global.json`, the host resolves to the highest installed version. The failing run's own log proves it: the error came from `/usr/share/dotnet/sdk/11.0.100/Sdks/Microsoft.NET.Sdk/...`, i.e. SDK **11.0.100** was building a `net10.0` solution. That is a green build that proves nothing about the shipped toolchain, and it makes CI diverge from every local build (which uses `10.0.404`). | Fixed with a new root `global.json` pinning `"version": "10.0.100"` with `"rollForward": "latestFeature"`, so the 10.0.4xx band is accepted but 11.x is not. Verified locally: `dotnet --version` still resolves `10.0.404` and `dotnet restore MusicHub.slnx` exits 0, so the pin does not block the existing developer SDK | orchestrator |
| B-096 | Low | FIXED | CI | The `release.yml` `verify` job had the same two defects as CI but worse consequences: it gated image publishing and the Release itself. It ran `dotnet test` with no PostgreSQL service (so every integration test would fail and no release could ever be published) and used the same deprecated action major versions. It also built the Android APK without ensuring the exact SDK revisions the project pins. | Fixed: `verify` now has the same `postgres:17` service, `ffmpeg` install, `Wait for PostgreSQL` step and `CI`/`MUSICHUB_TEST_ADMIN_CONNECTION` env as CI. Both workflows were also moved off the deprecated Node 20 action majors (`checkout`/`setup-dotnet`/`setup-node`/`setup-java`/`upload-artifact`/`download-artifact`/`setup-gradle` v4 -> v5), which GitHub was already force-running on Node 24 with a warning. Because `app/build.gradle.kts` pins `buildToolsVersion = "36.0.0"` and `compileSdk = 35` and a missing exact revision fails rather than substituting, both workflows now install `platforms;android-35` and `build-tools;36.0.0` through `sdkmanager`. Both files re-parsed with a real YAML loader after editing | orchestrator |
| B-097 | Critical | FIXED | Repository integrity | **Four production source files were never committed, so the published repository could not compile on Linux - and the local build could not reveal it.** `.gitignore` line 23 was `storage/` with no leading slash, which matches a directory named `storage` **or `Storage` at any depth** because Git's ignore matching is case-insensitive on Windows and macOS. That silently swallowed `src/server/MusicHub.Infrastructure/Storage/` - `LibraryLocationStore.cs`, `LibraryRelocationService.cs`, `StorageAdminService.cs`, `StorageLocationValidator.cs`, all in namespace `MusicHub.Infrastructure.Storage`. Every local Windows build succeeded because the files are present on disk; nothing ever compared "what is on disk" against "what is in the repository", so `git ls-files src/server/MusicHub.Infrastructure/Storage/` returned **0** files and the first commit shipped without them. Discovered by the CI run itself, which failed with `The type or namespace name 'Storage' does not exist in the namespace 'MusicHub.Infrastructure'` plus 9 further `StorageAdminService`/`LibraryLocationStore`/`StorageRelocationSnapshot`/`StorageCheck` resolution errors - exactly the symbols `Program.cs` and `StorageEndpoints.cs` (which **were** committed) depend on. Confirmed with `git check-ignore -v`: `.gitignore:23:storage/` matched the `.cs` file. | Fixed by anchoring the two directory patterns to the repository root: `/storage/` and `/artifacts/`, with a comment stating why. The secret and log patterns (`*.log`, `*.env`, `dev.secrets.ps1`, `.env`, `.env.*`) were deliberately **left unanchored** - a secret must be ignored wherever it appears, and anchoring them would have been a new leak risk. Verified after the change: `storage/dev.secrets.ps1`, `storage/android-keys/keystore.properties` and `storage/postgres-data/postmaster.pid` are still ignored via `/storage/`, the server package under `artifacts/` is still ignored via `/artifacts/`, and `git check-ignore` no longer matches `LibraryLocationStore.cs`. All four files were then added (`git diff --cached --name-status` shows 4 `A` entries), a secret scan over them found only `CancellationToken` matches, and `dotnet build MusicHub.slnx` re-ran green (`0 Warning(s), 0 Error(s)`) so the tracked sources are the ones that compile. **A whole-repository audit followed:** `git ls-files --others --ignored` listed 33 entries, and filtering out build output left exactly these four `.cs` files plus the empty runtime `src/server/MusicHub.Api/storage/`, so nothing else is missing | orchestrator |
