# MusicHub Enterprise — Google Play listing prerequisites

Status owner: music-orchestrator (docs) · devops-release-engineer (artifacts) · web-dashboard-engineer
(delivery). Last verified: 2026-11-21 (R-019 / W-041).

This file is the source of truth for the **version 1** Google-side install requirements of
`com.musichub.mobile`. It records what is verified today and what still has to be supplied by the
operator before the app can be submitted to the Play Console.

## 1. Verified install-time security facts (2026-11-21, R-019)

| Check | Result | Evidence |
| --- | --- | --- |
| APK signature | `apksigner verify --verbose --print-certs` -> `Verifies`; v2+v3 = true; v3.1/v4 = false; 1 signer; RSA 4096; `CN=MusicHub Enterprise, OU=MusicHub, O=MusicHub, L=Tehran, C=IR` | shipped artifact `artifacts/installers/MusicHub-Android-1.0.0-release.apk` |
| APK SHA-256 | `4F95D2C94A0F9235F6AE0C067DEBC7036EDA095DF6C3CD507FD30F829DD5FA74` (matches `MusicHub-Android-1.0.0-release.apk.sha256`) | artifacts |
| Debuggability | release manifest has no `android:debuggable`; `build.gradle.kts` sets `isDebuggable = false` for the release build type | B-069 fix verified |
| Install state on device `R58M915RT4W` (SM-G975F) | `installed=true`, `stopped=false`, `hidden=false`, `suspended=false`, `enabled=true`, `notLaunched=false`; versionCode 2 / versionName 1.0.0; minSdk 26, targetSdk 35 | `adb shell dumpsys package com.musichub.mobile` |
| Cold start | `adb shell am start -W -n com.musichub.mobile/.MainActivity` -> `Status: ok`, `LaunchState: COLD` | R-019 |
| Runtime crash | `adb logcat -d --pid=<musichub_pid>` -> clean startup log, no FATAL/CRASH | R-019 |
| Play Protect | scan executed from Play Store on the phone; user reported **green check / no threat** | R-019 follow-up, 2026-11-21 |
| Data-minimisation manifest | `allowBackup=false`, camera `required=false`, foreground service types `mediaPlayback` + `dataSync`, no `debuggable` | shipped manifest |

Conclusion for version 1: the shipped artifact is a **release-signed, non-debuggable, Play Protect
clean** build. Install-time secure recognition on Android 8+ is satisfied by this artifact.

## 2. Play Console listing requirements that are still open (operator actions)

These cannot be completed from the engineering host; they need the operator's Play Console account
and legal/hosting decisions.

1. **Play App Signing** — required for any new Play listing. The app must be uploaded as an **Android
   App Bundle (`.aab`)**; Google Play then manages the store signing key. The repository currently
   ships the signed **APK** (`artifacts/installers/MusicHub-Android-1.0.0-release.apk`, versionCode 2);
   `.gitignore` excludes `*.aab`, so an AAB build step still has to be produced for the listing.
2. **Privacy policy** — Play requires a publicly reachable privacy policy URL when the app handles
   user data (accounts, library content, playback statistics). No privacy policy exists in the
   repository yet; a draft is prepared under `docs/PLAY_STORE_LISTING.md` §4 before submission.
3. **Content rating (IARC)** — questionnaire to be completed in the console; draft answers prepared
   in §3.
4. **Data safety form** — questionnaire to be completed in the console; draft answers prepared in §3.
5. **Target API / API level** — already compliant: minSdk 26, targetSdk 35 (compileSdk 35).

## 3. Draft questionnaire answers (IARC + Data safety)

Basis: the app is a self-hosted, music-library streaming server client. Account (e-mail + password),
user-uploaded audio content (content-addressed storage on the server), per-user playback/listening
statistics, and admin-only Telegram ingestion.

- Content rating (draft): Music / audio app with user-provided content; no ads; no in-app purchases;
  music content is supplied by the user/library operator, not a proprietary catalogue.
- Data safety (draft):
  - Data collected: app info (e-mail), user content (audio files), library metadata (tracks,
    albums, artists), listening/playback statistics per user.
  - Purpose: library management and access control; playback usage tracking.
  - Third-party sharing: no (Google Play services as platform only).
  - Security: TLS in transit; server-side hashing (SHA-256) of stored content; password hashing.

These are **drafts** and must be reviewed (and, where required, legally reviewed) before submission.

## 4. Draft privacy policy outline

Draft template (operator must host and point the Play Console field at the final URL):

1. Controller identity and contact (support e-mail — **to be provided**).
2. Data collected: account data (e-mail, credentials), user content (audio library files),
   playback/listening data.
3. Purpose and legal bases (contract, legitimate interest).
4. Storage & security: hashed credentials, content-addressed storage, TLS.
5. Retention and deletion (user-driven library removal; server retention policy).
6. Third parties: none beyond Google Play services.
7. Children / permissions (camera optional, QR scanner optional).
8. Changes and contact.

A final, hostable privacy policy text must replace this outline before the listing is submitted.

## 5. Submission checklist (version 1)

- [x] Release-signed, non-debuggable APK verified (`apksigner`), Play Protect clean.
- [x] Install state + cold start verified on the connected device.
- [ ] Play App Signing enabled; AAB built for the listing.
- [ ] Privacy policy published at a public URL and linked in the console.
- [ ] Content rating completed (IARC).
- [ ] Data safety form completed.
- [ ] Listing submitted; rollout to production track.

## 6. Next actions

1. Operator: publish/host the final privacy policy and provide the support e-mail.
2. Operator: finish the IARC questionnaire and the data safety form in the Play Console.
3. Operator: enable Play App Signing and build the release AAB for submission.
4. Operator: submit the listing.
5. Orchestrator: record the submission state in `docs/AGENT_REPORTS.md` (R-020) and `docs/WORK_QUEUE.md` (W-042).
