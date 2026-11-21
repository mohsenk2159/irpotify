# Security policy

## Reporting a vulnerability

Report suspected vulnerabilities privately. Do **not** open a public issue for a security defect.

Use GitHub's **Report a vulnerability** button on the repository's Security tab (Private
Vulnerability Reporting), or contact the maintainers through the address published on the
repository profile.

Include:

- the affected component (server API, worker, dashboard, Windows player, Android client, deployment
  scripts, container image) and version or commit;
- the impact you believe it has, and who can trigger it (anonymous, authenticated user, listener,
  administrator, local operator);
- reproduction steps or a proof of concept;
- any evidence (logs, requests) with secrets redacted.

You will get an acknowledgement and an assessment. Please allow time for a fix before publishing
details.

## Supported versions

The project is pre-1.1. Security fixes are applied to the current `main` and the latest release
tag. Older tags are not maintained.

## Security model in one page

MusicHub is self-hosted and multi-user. The server is the security boundary; clients are not
trusted.

- **Authorization is enforced server-side.** Hiding a control in the dashboard or a client app is a
  usability choice, never a permission. Every endpoint checks the caller's role and ownership.
- **Two roles.** `Administrator` and `User`. Entitlements (`CanUsePlayer`, `CanUseTelegram`,
  per-user quotas) are checked on the server for the action they gate.
- **Authentication** is JWT access + refresh tokens. The signing secret must be at least 32
  characters; the API refuses to start without one.
- **Uploads are untrusted.** Audio is parsed with ffprobe for metadata only. Archives are never
  extracted, and uploaded content is never executed. Optional ClamAV scanning is available
  (`Storage:RequireMalwareScan`).
- **Music lives outside the code and outside container layers.** A content-addressed store under a
  configurable data root; the container keeps it on a named volume.
- **TLS is never weakened.** No `skip_verify`, `insecure_skip_verify`, `tls internal`,
  `auto_https off`, `-k` or `NODE_TLS_REJECT_UNAUTHORIZED` anywhere. Caddy uses real ACME
  certificates; a LAN-only install publishes a private address over HTTP deliberately, bound to that
  private address.
- **Secrets stay out of the repository.** `.env`, `storage/` (including the generated
  `dev.secrets.ps1`, the database credential file and the Android signing key), `artifacts/` and
  `tools/` are gitignored. The database password is stored encrypted under the data root
  (`keys/db-credential.protected`), not in a file the API reads as plain text.
- **Containers** run as a non-root UID with a read-only root filesystem, all capabilities dropped,
  `no-new-privileges`, bounded PIDs, and no published port except the TLS edge (and the optional
  LAN publication bound to a private address).
- **Telegram bot is admin-only.** It answers only the configured admin sender; the check is on the
  server, not in the message handler's convenience branch. The bot token is encrypted with the
  application DataProtection key ring, which the API and the worker must share.

## Operator responsibilities

The defaults are safe, but these are yours to get right:

1. **Generate real secrets.** Every `CHANGE_ME` in `.env` must be replaced with a cryptographic
   value. `deploy/docker/install.ps1` does this automatically; a manual install must not skip it.
2. **Protect `.env` and the data root.** Restrict them to the service account. Treat a backup of
   `keys/` as a credential: it decrypts the stored Telegram bot token.
3. **Rotate what leaked.** If a token, password or key was ever pasted into a chat, an issue or a
   log, rotate it at the source. Deleting the line from a file does not revoke it.
4. **Do not expose the API directly.** It has no TLS listener. Put Caddy (or a tunnel) in front, or
   bind a private LAN address as the provided overlay does.
5. **Keep the edge narrow.** `ForwardedHeaders__KnownNetworks` / `KnownProxies` must list only your
   real proxies. Trusting every peer lets a direct caller forge its client IP and escape per-IP
   rate limits.
6. **Update.** Pull the new image tag and re-run migrations with a single API instance.

## Known limitations

- The DataProtection key ring is unencrypted on Linux (`ProtectKeysWithDpapi()` is Windows-only).
  Protect the volume with disk encryption and restrictive permissions.
- The Android client allows cleartext HTTP only for loopback and private addresses, and only when
  the operator enables it. Do not enable it for a public host.
- Multi-arch container images (`linux/arm64`) are not built by default yet; build with
  `--platform` if you run on ARM.

See `docs/security/CLIENT_TAMPER_ASSESSMENT.md` for the client-side tamper analysis, and
`deploy/docker/README.md` §11 for the container security posture.
