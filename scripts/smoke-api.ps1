param(
    [string]$BaseUrl = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot "storage\dev.secrets.ps1")

$ffmpeg = $env:MUSICHUB_FFMPEG_PATH
$check = [System.Collections.Generic.List[object]]::new()
$failures = 0

function Record([string]$name, [bool]$ok, [string]$detail) {
    $check.Add([pscustomobject]@{ Check = $name; Passed = $ok; Detail = $detail })
    if (-not $ok) { $script:failures++ }
    $label = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("[{0}] {1} - {2}" -f $label, $name, $detail)
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = [Uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromSeconds(120)

function Send([string]$method, [string]$path, $body, [string]$token, [hashtable]$headers) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), $path)
    if ($token) { $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token) }
    if ($headers) { foreach ($k in $headers.Keys) { [void]$request.Headers.TryAddWithoutValidation($k, $headers[$k]) } }
    if ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Depth 10 }
        $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { } }
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $parsed; Raw = $text; Response = $response }
}

# 1. Health
$health = Send "GET" "/api/healthz" $null $null $null
Record "health" ($health.Status -eq 200) "HTTP $($health.Status)"

# 2. Setup status / bootstrap
$status = Send "GET" "/api/setup/status" $null $null $null
Record "setup.status" ($status.Status -eq 200) "HTTP $($status.Status) initialized=$($status.Body.initialized)"
if (-not $status.Body.initialized) {
    $setup = Send "POST" "/api/setup/initialize" @{
        bootstrapToken = $env:MUSICHUB_BOOTSTRAP_TOKEN
        email = $env:MUSICHUB_ADMIN_EMAIL
        password = $env:MUSICHUB_ADMIN_PASSWORD
        displayName = "MusicHub Administrator"
    } $null $null
    Record "setup.initialize" ($setup.Status -eq 201) "HTTP $($setup.Status)"
    $again = Send "POST" "/api/setup/initialize" @{
        bootstrapToken = $env:MUSICHUB_BOOTSTRAP_TOKEN
        email = "second@musichub.local"
        password = $env:MUSICHUB_ADMIN_PASSWORD
        displayName = "Second"
    } $null $null
    Record "setup.initialize.rejected-after-first" ($again.Status -eq 409) "HTTP $($again.Status)"
}

# 3. Login failures and success
$badLogin = Send "POST" "/api/auth/login" @{ email = $env:MUSICHUB_ADMIN_EMAIL; password = "definitely-wrong-password"; deviceName = "smoke" } $null $null
Record "auth.login.rejects-wrong-password" ($badLogin.Status -eq 401) "HTTP $($badLogin.Status)"

$login = Send "POST" "/api/auth/login" @{ email = $env:MUSICHUB_ADMIN_EMAIL; password = $env:MUSICHUB_ADMIN_PASSWORD; deviceName = "smoke" } $null $null
$token = $login.Body.accessToken
$refresh = $login.Body.refreshToken
Record "auth.login" ($login.Status -eq 200 -and -not [string]::IsNullOrWhiteSpace($token)) "HTTP $($login.Status) tokenLength=$($token.Length)"

$me = Send "GET" "/api/auth/me" $null $token $null
Record "auth.me" ($me.Status -eq 200) "HTTP $($me.Status) user=$($me.Body.email)"
$anon = Send "GET" "/api/auth/me" $null $null $null
Record "auth.me.requires-token" ($anon.Status -eq 401) "HTTP $($anon.Status)"

# 4. Refresh rotation and reuse detection
$rotated = Send "POST" "/api/auth/refresh" @{ refreshToken = $refresh } $null $null
Record "auth.refresh.rotates" ($rotated.Status -eq 200 -and $rotated.Body.refreshToken -ne $refresh) "HTTP $($rotated.Status)"
$reuse = Send "POST" "/api/auth/refresh" @{ refreshToken = $refresh } $null $null
Record "auth.refresh.detects-reuse" ($reuse.Status -eq 401) "HTTP $($reuse.Status)"
# Re-login after family revocation
$login = Send "POST" "/api/auth/login" @{ email = $env:MUSICHUB_ADMIN_EMAIL; password = $env:MUSICHUB_ADMIN_PASSWORD; deviceName = "smoke-2" } $null $null
$token = $login.Body.accessToken
Record "auth.login.after-revocation" ($login.Status -eq 200) "HTTP $($login.Status)"

# 5. Generate fixture and upload
$fixtureRoot = Join-Path $repoRoot "storage\smoke"
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
$wav = Join-Path $fixtureRoot "smoke-tone.wav"
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=440:duration=3" -metadata title="Smoke Tone" -metadata artist="MusicHub QA" -metadata album="Smoke Album" -ar 44100 -ac 2 $wav
Record "fixture.ffmpeg" (Test-Path $wav) "size=$((Get-Item $wav).Length)"

function Upload-Audio([string]$filePath, [string]$token) {
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    $bytes = [System.IO.File]::ReadAllBytes($filePath)
    $fileContent = [System.Net.Http.ByteArrayContent]::new($bytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("audio/wav")
    $form.Add($fileContent, "files", [System.IO.Path]::GetFileName($filePath))
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "/api/uploads")
    $request.Content = $form
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = ($text | ConvertFrom-Json); Raw = $text }
}

$upload = Upload-Audio $wav $token
$trackId = $upload.Body.results[0].track.id
Record "upload.accepted" ($upload.Status -eq 201 -and $upload.Body.results[0].success) "HTTP $($upload.Status) trackId=$trackId"
Record "upload.metadata" ($upload.Body.results[0].track.title -eq "Smoke Tone" -and $upload.Body.results[0].track.artist -eq "MusicHub QA") "title=$($upload.Body.results[0].track.title) artist=$($upload.Body.results[0].track.artist)"
Record "upload.sha256" ($upload.Body.results[0].track.sha256 -match '^[0-9a-f]{64}$') "sha256=$($upload.Body.results[0].track.sha256)"
Record "upload.no-storage-path-leak" (-not ($upload.Raw -match 'storage|quarantine|music/')) "response has no storage path"

$duplicate = Upload-Audio $wav $token
Record "upload.duplicate" ($duplicate.Status -eq 201 -and $duplicate.Body.results[0].duplicate -eq $true) "HTTP $($duplicate.Status) duplicate=$($duplicate.Body.results[0].duplicate)"

$garbage = Join-Path $fixtureRoot "not-audio.mp3"
[System.IO.File]::WriteAllBytes($garbage, [byte[]](1..64))
$badUpload = Upload-Audio $garbage $token
Record "upload.rejects-invalid-audio" ($badUpload.Status -eq 422 -or $badUpload.Status -eq 207) "HTTP $($badUpload.Status) error=$($badUpload.Body.results[0].error)"

$anonUpload = Upload-Audio $wav $null
Record "upload.requires-auth" ($anonUpload.Status -eq 401) "HTTP $($anonUpload.Status)"

# 6. Library reads
$tracks = Send "GET" "/api/tracks?search=smoke&pageSize=10" $null $token $null
Record "library.tracks" ($tracks.Status -eq 200 -and $tracks.Body.total -ge 1) "HTTP $($tracks.Status) total=$($tracks.Body.total)"
$albums = Send "GET" "/api/albums" $null $token $null
Record "library.albums" ($albums.Status -eq 200) "HTTP $($albums.Status)"
$homeFeed = Send "GET" "/api/home" $null $token $null
Record "library.home" ($homeFeed.Status -eq 200) "HTTP $($homeFeed.Status)"
$search = Send "GET" "/api/search?q=MusicHub" $null $token $null
Record "library.search" ($search.Status -eq 200) "HTTP $($search.Status)"
$stats = Send "GET" "/api/dashboard/stats" $null $token $null
Record "dashboard.stats" ($stats.Status -eq 200) "HTTP $($stats.Status)"

# 7. Streaming
$streamToken = Send "POST" "/api/tracks/$trackId/stream-token" $null $token $null
$streamUrl = $streamToken.Body.streamUrl
Record "stream.token" ($streamToken.Status -eq 200 -and $streamUrl) "HTTP $($streamToken.Status)"

$full = $client.GetAsync($streamUrl).GetAwaiter().GetResult()
$fullBytes = $full.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
Record "stream.full" ($full.StatusCode -eq 200 -and $fullBytes.Length -eq (Get-Item $wav).Length) "HTTP $([int]$full.StatusCode) bytes=$($fullBytes.Length) etag=$($full.Headers.ETag)"

$rangeRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $streamUrl)
$rangeRequest.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new(0, 99)
$range = $client.SendAsync($rangeRequest).GetAwaiter().GetResult()
$rangeBytes = $range.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
Record "stream.range" ($range.StatusCode -eq 206 -and $rangeBytes.Length -eq 100) "HTTP $([int]$range.StatusCode) bytes=$($rangeBytes.Length) contentRange=$($range.Content.Headers.ContentRange)"

$rangeRequest2 = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $streamUrl)
$rangeRequest2.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new((Get-Item $wav).Length, (Get-Item $wav).Length + 10)
$range2 = $client.SendAsync($rangeRequest2).GetAwaiter().GetResult()
Record "stream.unsatisfiable-range" ($range2.StatusCode -eq 416) "HTTP $([int]$range2.StatusCode)"

$anonStream = Send "GET" "/api/tracks/$trackId/stream" $null $null $null
Record "stream.requires-auth" ($anonStream.Status -eq 401) "HTTP $($anonStream.Status)"

$badTokenStream = Send "GET" "/api/tracks/$trackId/stream?token=invalid.invalid" $null $null $null
Record "stream.rejects-invalid-token" ($badTokenStream.Status -eq 401) "HTTP $($badTokenStream.Status)"

# 8. Role authorization
$listener = Send "POST" "/api/users" @{ email = "listener-smoke@musichub.local"; password = "ListenerPassw0rd!123"; displayName = "Smoke Listener"; role = "Listener" } $token $null
if ($listener.Status -eq 409) { $listenerStatus = "already exists" } else { $listenerStatus = "created" }
Record "users.admin-creates-listener" ($listener.Status -eq 201 -or $listener.Status -eq 409) "HTTP $($listener.Status) $listenerStatus"

$listenerLogin = Send "POST" "/api/auth/login" @{ email = "listener-smoke@musichub.local"; password = "ListenerPassw0rd!123"; deviceName = "smoke-listener" } $null $null
$listenerToken = $listenerLogin.Body.accessToken
$listenerUpload = Upload-Audio $wav $listenerToken
Record "authz.listener-cannot-upload" ($listenerUpload.Status -eq 403) "HTTP $($listenerUpload.Status)"
$listenerUsers = Send "GET" "/api/users" $null $listenerToken $null
Record "authz.listener-cannot-list-users" ($listenerUsers.Status -eq 403) "HTTP $($listenerUsers.Status)"
$listenerTelegram = Send "GET" "/api/admin/telegram" $null $listenerToken $null
Record "authz.listener-cannot-read-telegram" ($listenerTelegram.Status -eq 403) "HTTP $($listenerTelegram.Status)"

# 9. Telegram configuration safety
$telegram = Send "GET" "/api/admin/telegram" $null $token $null
Record "telegram.config.read" ($telegram.Status -eq 200) "HTTP $($telegram.Status) configured=$($telegram.Body.configured)"
$badToken = Send "PUT" "/api/admin/telegram" @{ botToken = "not-a-token" } $token $null
Record "telegram.rejects-invalid-token-format" ($badToken.Status -eq 400) "HTTP $($badToken.Status)"
$fakeToken = "123456789:AAFakeTokenValueForTestingOnly_1234567890"
$saveToken = Send "PUT" "/api/admin/telegram" @{ botToken = $fakeToken; adminTelegramUserId = 1000001; adminChatId = 1000001; ingestionEnabled = $false; destinationGroupId = -1001234567890; autoPublishEnabled = $false } $token $null
Record "telegram.token.saved" ($saveToken.Status -eq 200 -and $saveToken.Body.tokenConfigured -eq $true) "HTTP $($saveToken.Status)"
Record "telegram.token.not-echoed" (-not ($saveToken.Raw -match [regex]::Escape($fakeToken))) "response does not contain token"
$telegramAgain = Send "GET" "/api/admin/telegram" $null $token $null
Record "telegram.token.not-readable" (-not ($telegramAgain.Raw -match [regex]::Escape($fakeToken))) "GET does not expose token"
$ingestDisabled = Send "POST" "/api/admin/telegram/test-ingest" $null $token $null
Record "telegram.test-ingest.blocked-when-disabled" ($ingestDisabled.Status -eq 415 -or $ingestDisabled.Status -eq 400) "HTTP $($ingestDisabled.Status)"

# 10. Audit and metrics
$audit = Send "GET" "/api/admin/audit?pageSize=5" $null $token $null
Record "admin.audit" ($audit.Status -eq 200) "HTTP $($audit.Status)"
$metrics = Send "GET" "/api/admin/metrics" $null $token $null
Record "admin.metrics" ($metrics.Status -eq 200) "HTTP $($metrics.Status)"
$events = Send "GET" "/api/admin/system-events" $null $token $null
Record "admin.system-events" ($events.Status -eq 200) "HTTP $($events.Status)"

# 11. OpenAPI document
$openapi = Send "GET" "/openapi/v1.json" $null $null $null
Record "openapi.document" ($openapi.Status -eq 200) "HTTP $($openapi.Status)"

$client.Dispose()
Write-Host ""
Write-Host ("Smoke summary: {0} passed, {1} failed" -f ($check.Count - $failures), $failures)
$check | Format-Table -AutoSize | Out-String | Write-Host

$reportPath = Join-Path $repoRoot "artifacts\smoke\smoke-api-result.json"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $reportPath) | Out-Null
$check | ConvertTo-Json -Depth 5 | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "Report: $reportPath"

if ($failures -gt 0) { exit 1 }
