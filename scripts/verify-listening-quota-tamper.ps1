<#
.SYNOPSIS
  Adversarial verification of the listening-time entitlement (B-023).

.DESCRIPTION
  This script plays the role of a *modified client* - the exact threat that makes an APK
  "crackable". It does not patch any binary; it calls the real API the way a reverse-engineered
  client would, and measures what the server is willing to give away for free.

  The attack it models:
    1. Authenticate normally (a cracked client still needs valid credentials).
    2. Stream audio from /api/tracks/{id}/stream.
    3. Send POST /api/history/playback with listenedSeconds = 0 every time.

  Before the B-023 fix, step 3 made the server charge nothing, so an account with a listening
  allowance could be used forever. After the fix the server derives listening time from the bytes
  it actually served, so the same forged report still consumes the allowance.

  Requires a running API and the dev secrets file (admin credentials).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-listening-quota-tamper.ps1
#>
param(
    [string]$BaseUrl = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$repoRoot = Split-Path -Parent $PSScriptRoot

# Honour credentials supplied by the caller. Otherwise fall back to the development secrets file.
# (Sourcing it unconditionally would silently replace a caller-provided admin login, which is what
# made an earlier run of this probe fail with a misleading 401.)
if (-not $env:MUSICHUB_ADMIN_EMAIL -or -not $env:MUSICHUB_ADMIN_PASSWORD) {
    . (Join-Path $repoRoot "storage\dev.secrets.ps1")
}

$checks = [System.Collections.Generic.List[object]]::new()
$failures = 0

function Record([string]$name, [bool]$ok, [string]$detail) {
    $checks.Add([pscustomobject]@{ Check = $name; Passed = $ok; Detail = $detail })
    if (-not $ok) { $script:failures++ }
    $label = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("[{0}] {1} - {2}" -f $label, $name, $detail)
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = [Uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromSeconds(120)

function Send([string]$method, [string]$path, $body, [string]$token) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), $path)
    if ($token) {
        $request.Headers.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)
    }
    if ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 10 }
        $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    # Read bytes first: audio would be mangled by a text decode, and the byte count is what the
    # server charges listening time against.
    $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $text = if ($bytes.Length -gt 0) { [System.Text.Encoding]::UTF8.GetString($bytes) } else { "" }
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { } }
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $parsed; Raw = $text; Bytes = $bytes.Length }
}

# --- 1. Admin session ------------------------------------------------------
$login = Send "POST" "/api/auth/login" @{
    email = $env:MUSICHUB_ADMIN_EMAIL
    password = $env:MUSICHUB_ADMIN_PASSWORD
    deviceName = "quota-tamper"
    clientType = "dashboard"
} $null
if ($login.Status -ne 200) {
    Write-Host "Cannot authenticate as the administrator: HTTP $($login.Status) $($login.Raw)"
    exit 2
}
$adminToken = $login.Body.accessToken
Record "admin.login" $true "HTTP 200"

# --- 2. Create a throwaway listener with a 60-second listening allowance ----
$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
$email = "tamper.$suffix@musichub.local"
$password = "TamperPassw0rd!123"

$created = Send "POST" "/api/users" @{
    email = $email
    password = $password
    displayName = "Tamper Probe $suffix"
    role = "Listener"
    canUsePlayer = $true
    listenQuotaSeconds = 60
} $adminToken
if ($created.Status -notin 200, 201) {
    Write-Host "Cannot create the probe user: HTTP $($created.Status) $($created.Raw)"
    exit 2
}
$probeUserId = $created.Body.id
Record "probe.user.created" $true "quota=60s user=$email"

$probeLogin = Send "POST" "/api/auth/login" @{
    email = $email
    password = $password
    deviceName = "tamper-client"
    clientType = "player"
} $null
if ($probeLogin.Status -ne 200) {
    Write-Host "Probe user cannot sign in: HTTP $($probeLogin.Status) $($probeLogin.Raw)"
    exit 2
}
$probeToken = $probeLogin.Body.accessToken
Record "probe.login" $true "HTTP 200"

# --- 3. Find a track to stream --------------------------------------------
$tracks = Send "GET" "/api/tracks?page=1&pageSize=5" $null $probeToken
$trackId = $null
if ($tracks.Body -and $tracks.Body.items -and $tracks.Body.items.Count -gt 0) {
    $trackId = $tracks.Body.items[0].id
}
if (-not $trackId) {
    Write-Host "No tracks in the library; upload at least one before running this probe."
    exit 3
}
Record "track.selected" $true "$trackId"

# --- 4. The attack: stream, then lie about how long we listened ------------
# A 128 kbit/s MP3 is ~16 000 bytes/s, so pulling the whole file is far more than the 60 s quota.
$streamToken = Send "POST" "/api/tracks/$trackId/stream-token" $null $probeToken
if ($streamToken.Status -ne 200) {
    Write-Host "Cannot obtain a stream token: HTTP $($streamToken.Status) $($streamToken.Raw)"
    exit 2
}
$url = $streamToken.Body.streamUrl
Record "stream.token.issued" $true "expiresAt=$($streamToken.Body.expiresAt)"

$streamedBytes = 0
$stream = Send "GET" $url $null $null
if ($stream.Status -in 200, 206) {
    $streamedBytes = $stream.Bytes
    Record "stream.served" $true "HTTP $($stream.Status) $streamedBytes bytes"
} else {
    Record "stream.served" $false "HTTP $($stream.Status)"
}

# The forged report: claim we listened for zero seconds.
$forged = Send "POST" "/api/history/playback" @{
    trackId = $trackId
    positionSeconds = 0
    completed = $false
    listenedSeconds = 0
} $probeToken
Record "forged-report.accepted-or-blocked" ($forged.Status -in 204, 403) "HTTP $($forged.Status)"

# --- 5. Read back how much listening time the server actually charged -------
$usage = $null
$users = Send "GET" "/api/users" $null $adminToken
if ($users.Status -eq 200 -and $users.Body) {
    # /api/users returns a bare array, not a paged envelope.
    $row = $users.Body | Where-Object { $_.id -eq $probeUserId }
    if ($row) { $usage = $row.listenSecondsUsed }
}

if ($null -eq $usage) {
    Record "server.charged-listening-time" $false "could not read listenSecondsUsed"
} else {
    # The decisive assertion. A tampered client that reports 0 must still be charged, because the
    # server measured the bytes it served. 0 here means the hole is still open.
    Record "server.charged-listening-time" ($usage -gt 0) "listenSecondsUsed=$usage (forged report said 0)"
}

# --- 6. Prove the quota actually bites -------------------------------------
# The forged report above already exhausted the 60-second allowance, so the account must be dead
# by now: further playback reports are rejected and every other authenticated call is refused.
$blockedAt = if ($forged.Status -eq 403) { 1 } else { $null }
if (-not $blockedAt) {
    for ($i = 0; $i -lt 6; $i++) {
        $r = Send "POST" "/api/history/playback" @{
            trackId = $trackId
            positionSeconds = 0
            completed = $false
            listenedSeconds = 0
        } $probeToken
        if ($r.Status -eq 403) { $blockedAt = $i + 2; break }
    }
}

$afterBlock = Send "GET" "/api/tracks?page=1&pageSize=1" $null $probeToken
if ($blockedAt) {
    Record "quota.deactivated-account" ($afterBlock.Status -in 401, 403) `
        "first forged report blocked (#$blockedAt); subsequent GET -> HTTP $($afterBlock.Status)"
} else {
    Record "quota.deactivated-account" $false "60 s allowance never exhausted by zero-second reports"
}

# --- Summary ---------------------------------------------------------------
$passed = $checks.Count - $failures
Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $passed, $failures)

$artifactDir = Join-Path $repoRoot "artifacts\smoke"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$artifactPath = Join-Path $artifactDir "verify-listening-quota-tamper.json"
[pscustomobject]@{
    GeneratedAt = (Get-Date).ToString("o")
    BaseUrl     = $BaseUrl
    ProbeUser   = $email
    StreamedBytes = $streamedBytes
    ChargedSeconds = $usage
    Passed      = $passed
    Failed      = $failures
    Checks      = $checks
} | ConvertTo-Json -Depth 6 | Set-Content -Path $artifactPath -Encoding UTF8
Write-Host "Evidence written to $artifactPath"

exit $(if ($failures -gt 0) { 1 } else { 0 })
