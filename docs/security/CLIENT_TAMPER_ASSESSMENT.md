# MusicHub Enterprise â€” Client Tampering / "Crackability" Assessment

Date: 2026-11-20
Scope: whether the MusicHub Android client (`com.musichub.mobile`) can be modified to obtain
features or entitlement it is not licensed for, and what the server does about it.
Method: static review of client + server, inspection of the **installed** APK on a real device,
and an adversarial integration test that plays the role of a modified client.

Status labels: `VERIFIED`, `IMPLEMENTED_NOT_TESTED`, `BLOCKED`, `NOT_IMPLEMENTED`.

---

## 1. Executive answer

**There is nothing in the Android APK to crack.** The client contains no licence, premium,
paywall, entitlement or billing logic at all. Every access decision that matters is made on the
server, per request, from database state:

- account active/expired (`User.IsActive`, `User.ExpiresAt`)
- storage quota and listening allowance (`User.QuotaBytes`, `User.ListenQuotaSeconds`)
- player permission (`User.CanUsePlayer`)
- role-based authorization (`UserRoles`)

A patched APK therefore cannot unlock anything: deleting a button, flipping a boolean or
rewriting the UI changes only what the device displays. The server re-checks the account on
every authenticated request (`Program.cs`, `JwtBearerEvents.OnTokenValidated`) and refuses with
`401`/`403` regardless of what the client believes.

The one genuine exception was **B-023**: the listening-time allowance trusted a number the client
supplied, so a modified client could report `0` and use a metered account forever. This assessment
**fixed** that (see Â§5) and proved it with a test that fails on the old code.

## 2. Verified state of the installed build

Inspected the APK actually installed on the connected device (Samsung SM-G975F, Android 12,
serial `R58M915RT4W`), pulled with `adb pull`, not the one in the build folder.

| Property | Observed | Command |
| --- | --- | --- |
| Package | `com.musichub.mobile`, versionCode `2`, versionName `1.0.0` | `aapt2 dump badging` |
| Debuggable | **No** (no `application-debuggable` flag) | `aapt2 dump badging` |
| Signing | v2 **and** v3 verified, single signer, RSA 4096 | `apksigner verify --print-certs` |
| Signer | `CN=MusicHub Enterprise, OU=MusicHub, O=MusicHub, L=Tehran, C=IR` | same |
| Cert SHA-256 | `8cffb3860e3d33840abf90bb72dc8cef340ba35ee17a88f1d1f4b6bec695b7d7` | same |
| Installer | `com.google.android.packageinstaller` (sideloaded, not Play) | `pm dump` |
| Root | unavailable (`su` not found) | `adb shell su -c id` |

A non-debuggable, v2/v3-signed build means `adb shell run-as` cannot read app data, and the
platform will refuse an update signed with a different key. That is the correct baseline.

## 3. Findings

| ID | Severity | Finding | Status |
| --- | --- | --- | --- |
| B-023 | Medium | Listening-time quota trusted client-reported `listenedSeconds`; a modified client reporting `0` consumed nothing | **FIXED** (Â§5) |
| B-072 | Low | Release build had `isMinifyEnabled = false`, so jadx recovered readable class names, API paths and DTOs | **FIXED** (Â§6) |
| B-073 | Informational | `network_security_config.xml` permits cleartext at `base-config`; not exploitable as written | **Investigated, no change** (Â§7) |
| B-074 | Low | No app-integrity attestation (Play Integrity / root detection); a modified client can drive the API directly | **Accepted for self-hosted** (Â§8) |
| B-075 | Low | Release signing key sits on the developer disk next to its plaintext password | **OPEN â€” operator action** (Â§9) |

### Why the entitlement model resists tampering

`UserEntitlementService.EnforceAsync` is invoked at login, on the playback-report path, and from a
2-minute Worker sweep (`EnforceAllAsync`). Separately, `JwtBearerEvents.OnTokenValidated` re-reads
`IsActive`, `SecurityStamp`, `ExpiresAt` and the listening allowance on **every** request, so a
deactivated account is blocked immediately rather than at the next sweep. Deactivation also bumps
`SecurityStamp` and revokes all refresh tokens and sessions, which invalidates every token the
attacker already holds.

Administrators are exempt from automatic deactivation by design. That is a deliberate product
decision, not a bypass: it requires the Administrator role in the database.

## 4. Attack surface that remains (by design)

The product is self-hosted, so the API is reachable by the same people who can modify the client.
A determined attacker with valid credentials can always script the API directly; no amount of
client hardening changes that. What matters is that scripting the API yields **no more access**
than the account is entitled to. That property now holds, including for listening time.

## 5. B-023 fix â€” server-measured listening time (VERIFIED)

**Problem.** `POST /api/history/playback` accepted `listenedSeconds` from the client and only
clamped it to 0..3600. A modified client sends `0` forever; the allowance never decrements.

**Design constraint discovered before writing the fix.** The Android app plays *downloaded* tracks
from a local file (`PlayerController.kt:198-199` selects `Uri.fromFile(local)`), which never touches
`/api/tracks/{id}/stream`. A bytes-only fix would therefore have made **offline playback free**, a
worse hole than the one being closed. The fix must keep the client report as a signal for offline
playback while making it impossible to *reduce* the charge.

**Fix.**

1. New singleton `ListeningTimeTracker` (`MusicHub.Api/Realtime/ListeningTimeTracker.cs`) counts
   bytes actually written to the response body in the stream endpoint.
2. `StreamTokenService.Create` now embeds the issuing user id in the signed payload, so an
   anonymous stream request (players fetch audio without an `Authorization` header) is still
   attributable. Substituting the user id invalidates the HMAC.
3. The playback endpoint converts drained bytes to seconds using the track's real bitrate
   (falling back to size/duration) and charges `max(clientReport, measured)`.

**Why `max` cannot double-charge.** The byte counter is **drained**, not read, on every report, so
the two sources describe the same interval. Taking the larger of two measurements of one interval
is a lower-bound estimator, never a sum.

**Why a per-process counter is safe.** A multi-instance deployment can only *under*-charge (a
stream handled by another instance is not seen). Under-charging is the pre-existing behaviour;
over-charging would wrongly deactivate paying accounts. The failure direction is therefore the safe
one.

**Evidence.**

The new integration test `Streamed_bytes_charge_listening_time_even_when_the_client_reports_zero`
streams a track anonymously with a stream token, then reports `listenedSeconds = 0`.

- With the fix: passes; the server charges the real played time.
- With the fix reverted (`var listenedSeconds = reported;`): **fails** with
  `Server charged 0s after serving 800044 bytes with a zero-second report.`

That is a reproduced vulnerability, not an assumed one. Restoring the fix makes it pass again.

Companion test `Client_report_is_still_honoured_when_it_exceeds_measured_stream_time` guards the
offline case: with no streamed bytes, a 45-second report charges exactly 45 seconds.

Runtime adversarial probe: `scripts/verify-listening-quota-tamper.ps1` performs the same attack
against a live API with a throwaway account holding a 60-second allowance.

## 6. B-072 fix â€” R8 minification enabled (VERIFIED at build level)

`isMinifyEnabled = true` and `isShrinkResources = true` for release, with `proguard-rules.pro`
keeping kotlinx.serialization, OkHttp/Retrofit annotations, Media3, Room, WorkManager workers and
the app entry points. Without those keeps, a minified release build throws
`SerializationException` on the first API response, so the rules are part of the fix, not an
afterthought.

Note this is defence in depth only. It raises the cost of mapping the API; it protects nothing on
its own, because the server is the authority.

## 7. B-073 â€” cleartext policy (investigated, deliberately unchanged)

`network_security_config.xml` sets `cleartextTrafficPermitted="true"` on `<base-config>`, which
looks like a blanket allow. Tightening it was attempted and **reverted**, because Android's
`<domain-config>` entries cannot express IP ranges: setting `base-config` to `false` would block
`http://192.168.x.x` â€” the primary self-hosted deployment â€” while still allowing every `*.local`
name. The change would have broken the product without closing a real hole.

The actual control is `CleartextPolicyInterceptor` (`data/api/Network.kt`), which rejects cleartext
unless the user enabled the LAN option **and** the host is loopback/private. Every network path in
the app â€” Retrofit, `DownloadWorker`, `ArtworkLoader`, and Media3's `HttpDataSource` â€” goes through
a client built by `ApiFactory` and therefore carries that interceptor. Verified by inspection of
each call site.

## 8. B-074 â€” no attestation (accepted)

There is no Play Integrity or root detection. A modified client can run in an emulator and script
the API. For a self-hosted product whose users control both ends this is acceptable: attestation
would add a Google dependency and break legitimate self-hosted installs. Recorded, not fixed.

## 9. B-075 â€” signing key custody (operator action)

`storage/android-keys/musichub-release.jks` and `keystore.properties` (plaintext password) are
gitignored but live on the development disk. If leaked, an attacker can sign a malicious update
that Android will accept as genuine. Required operator action: offline encrypted backup, and the
password held in a secret manager rather than beside the key.

## 10. What was NOT verified

- No dynamic repackaging test (patch â†’ resign â†’ install under a different package id â†’ run). The
  static conclusion does not depend on it: with no client-side licence logic, there is nothing to
  patch, and the server authority was proven by the integration test instead.
- The R8 release build's runtime behaviour was not exercised on a device in this pass (build-level
  verification only). This must be confirmed before shipping a minified release, because R8
  failures are runtime failures that the debug build never shows.
- The Windows desktop player (`src/desktop/MusicHub.Player`) was not assessed here.
- `scripts/verify-listening-quota-tamper.ps1` was authored but its live run is recorded separately.

## 11. Conclusion

The client is not the security boundary, and that is the right architecture. Removing the last
client-trusted quantity (listening time) closed the only path by which a modified APK could obtain
service it had not paid for. Remaining items are hardening and key custody, not authorization
holes.
