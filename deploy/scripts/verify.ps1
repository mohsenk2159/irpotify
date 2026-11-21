<#
.SYNOPSIS
    Verify the running MusicHub Enterprise developer stack with real checks.

.DESCRIPTION
    Runs against the real API and the real PostgreSQL instance:
      1. PostgreSQL readiness (pg_isready).
      2. EF Core migrations applied (source latest vs __EFMigrationsHistory).
      3. API /healthz and /api/health (database + storage probes reported by the API).
      4. Worker heartbeat freshness (AppSettings key 'worker.heartbeat').
      5. FFprobe availability (local binary + API-reported probe state).
      6. Storage root and subdirectory writability.
      7. Admin login smoke test when MUSICHUB_ADMIN_EMAIL / MUSICHUB_ADMIN_PASSWORD are set.

    Prints a PASS/FAIL/WARN/SKIP table and exits non-zero when any check FAILs.
    Never prints secrets or tokens.

.PARAMETER BaseUrl
    API base URL. Default: MUSICHUB_BASE_URL or http://127.0.0.1:5080.

.PARAMETER HeartbeatMaxAgeSeconds
    Maximum acceptable worker heartbeat age (default 120).

.PARAMETER SkipLogin
    Skip the login smoke test even when credentials are available.

.PARAMETER NoReport
    Do not write storage\logs\verify-last.json.

.EXAMPLE
    .\deploy\scripts\verify.ps1

.EXAMPLE
    .\deploy\scripts\verify.ps1 -BaseUrl http://127.0.0.1:5080 -HeartbeatMaxAgeSeconds 180
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = '',
    [int]$HeartbeatMaxAgeSeconds = 120,
    [switch]$SkipLogin,
    [switch]$NoReport
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$config = $null
$checks = New-MusicHubCheckList
try {
    $config = Get-MusicHubConfig
    if ([string]::IsNullOrWhiteSpace($BaseUrl)) { $BaseUrl = $config.ApiBaseUrl }
    $BaseUrl = $BaseUrl.TrimEnd('/')

    Write-Host ''
    Write-Host 'MusicHub Enterprise - verification' -ForegroundColor Cyan
    Write-MusicHubInfo "API:      $BaseUrl"
    Write-MusicHubInfo "Database: $($config.Host):$($config.Port)/$($config.Database) (user $($config.Username))"
    Write-MusicHubInfo "Data root: $($config.DataRoot)"
    Write-Host ''

    # 1. PostgreSQL readiness -----------------------------------------------------------------
    if (-not $config.PgIsReady) {
        Add-MusicHubCheck -List $checks -Name 'postgres.tooling' -Status 'WARN' -Detail 'pg_isready not found; used a TCP probe'
    }
    $pgReady = Test-MusicHubPostgresReady -Config $config -Quiet
    if ($pgReady) {
        $pgVersion = Get-MusicHubPsqlScalar -Config $config -Sql 'show server_version;' -AllowFailure
        Add-MusicHubCheck -List $checks -Name 'postgres.ready' -Status 'PASS' -Detail "$($config.Host):$($config.Port) server $pgVersion"
    }
    else {
        Add-MusicHubCheck -List $checks -Name 'postgres.ready' -Status 'FAIL' -Detail "No response on $($config.Host):$($config.Port). Start it with deploy/scripts/start-dev.ps1."
    }

    # 2. Migrations applied -------------------------------------------------------------------
    $sourceMigrations = Get-MusicHubMigrationIds -RepoRoot $config.RepoRoot
    $sourceLatest = if ($sourceMigrations.Count -gt 0) { $sourceMigrations[-1] } else { '' }
    if (-not $pgReady) {
        Add-MusicHubCheck -List $checks -Name 'database.migrations' -Status 'FAIL' -Detail 'Cannot inspect migrations: database is not reachable'
    }
    else {
        $applied = Get-MusicHubAppliedMigrations -Config $config
        $appliedLatest = if ($applied.Count -gt 0) { $applied[-1] } else { '' }
        if (-not $sourceLatest) {
            Add-MusicHubCheck -List $checks -Name 'database.migrations' -Status 'FAIL' -Detail 'No EF migration files found under src/server/MusicHub.Infrastructure/Persistence/Migrations'
        }
        elseif ($appliedLatest -eq $sourceLatest) {
            Add-MusicHubCheck -List $checks -Name 'database.migrations' -Status 'PASS' -Detail "$appliedLatest (applied $($applied.Count))"
        }
        else {
            Add-MusicHubCheck -List $checks -Name 'database.migrations' -Status 'FAIL' -Detail "Applied latest '$appliedLatest' != source latest '$sourceLatest'. Run dotnet ef database update (or start-dev.ps1)."
        }
    }

    # 3. API health ---------------------------------------------------------------------------
    $healthz = Invoke-MusicHubHttp -Url "$BaseUrl/healthz"
    if ($healthz.StatusCode -eq 200) {
        Add-MusicHubCheck -List $checks -Name 'api.healthz' -Status 'PASS' -Detail "HTTP 200 $BaseUrl/healthz"
    }
    else {
        Add-MusicHubCheck -List $checks -Name 'api.healthz' -Status 'FAIL' -Detail "HTTP $($healthz.StatusCode) $($healthz.Error)"
    }

    $health = Invoke-MusicHubHttp -Url "$BaseUrl/api/health"
    $apiChecks = $null
    if ($health.StatusCode -eq 200 -and $health.Json) {
        $apiChecks = $health.Json.checks
        Add-MusicHubCheck -List $checks -Name 'api.health' -Status 'PASS' -Detail "status=$($health.Json.status) version=$($health.Json.version)"
    }
    elseif ($health.StatusCode -eq 503 -and $health.Json) {
        $apiChecks = $health.Json.checks
        Add-MusicHubCheck -List $checks -Name 'api.health' -Status 'FAIL' -Detail "HTTP 503 status=$($health.Json.status)"
    }
    else {
        Add-MusicHubCheck -List $checks -Name 'api.health' -Status 'FAIL' -Detail "HTTP $($health.StatusCode) $($health.Error)"
    }

    if ($apiChecks) {
        if ($apiChecks.database -eq $true) {
            Add-MusicHubCheck -List $checks -Name 'api.database' -Status 'PASS' -Detail 'API reports a working database connection'
        }
        else {
            Add-MusicHubCheck -List $checks -Name 'api.database' -Status 'FAIL' -Detail 'API reports database=false'
        }
    }

    # 4. Worker heartbeat --------------------------------------------------------------------
    $heartbeat = Get-MusicHubWorkerHeartbeat -Config $config
    if (-not $pgReady) {
        Add-MusicHubCheck -List $checks -Name 'worker.heartbeat' -Status 'FAIL' -Detail 'Cannot read heartbeat: database is not reachable'
    }
    elseif (-not $heartbeat.Present) {
        Add-MusicHubCheck -List $checks -Name 'worker.heartbeat' -Status 'FAIL' -Detail 'AppSettings key worker.heartbeat is missing. Start MusicHub.Worker.'
    }
    elseif ($heartbeat.AgeSeconds -gt $HeartbeatMaxAgeSeconds) {
        Add-MusicHubCheck -List $checks -Name 'worker.heartbeat' -Status 'FAIL' -Detail "Heartbeat is $($heartbeat.AgeSeconds)s old (limit ${HeartbeatMaxAgeSeconds}s, last $($heartbeat.UpdatedAtUtc))"
    }
    else {
        Add-MusicHubCheck -List $checks -Name 'worker.heartbeat' -Status 'PASS' -Detail "Age $($heartbeat.AgeSeconds)s (limit ${HeartbeatMaxAgeSeconds}s, last $($heartbeat.UpdatedAtUtc))"
    }

    # 5. FFprobe ------------------------------------------------------------------------------
    if (-not $config.Ffprobe) {
        Add-MusicHubCheck -List $checks -Name 'ffprobe.available' -Status 'FAIL' -Detail 'ffprobe.exe not found. Set MUSICHUB_FFPROBE_PATH or install tools\ffmpeg.'
    }
    elseif (-not (Test-Path -LiteralPath $config.Ffprobe)) {
        Add-MusicHubCheck -List $checks -Name 'ffprobe.available' -Status 'FAIL' -Detail "Configured ffprobe not found: $($config.Ffprobe)"
    }
    else {
        $previousErrorAction = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $versionOutput = (& $config.Ffprobe -version 2>&1) -join ' '; $ffExit = $LASTEXITCODE }
        finally { $ErrorActionPreference = $previousErrorAction }
        if ($ffExit -eq 0) {
            $versionLine = (($versionOutput -split "`n")[0]).Trim()
            Add-MusicHubCheck -List $checks -Name 'ffprobe.available' -Status 'PASS' -Detail $versionLine
        }
        else {
            Add-MusicHubCheck -List $checks -Name 'ffprobe.available' -Status 'FAIL' -Detail "ffprobe -version exited $ffExit"
        }
    }
    if ($apiChecks) {
        if ($apiChecks.ffprobe -eq $true) {
            Add-MusicHubCheck -List $checks -Name 'api.ffprobe' -Status 'PASS' -Detail 'API probe reports ffprobe available'
        }
        else {
            Add-MusicHubCheck -List $checks -Name 'api.ffprobe' -Status 'FAIL' -Detail 'API probe reports ffprobe unavailable'
        }
    }

    # 6. Storage writability ------------------------------------------------------------------
    $storageOk = $true
    $storageDetail = ''
    try {
        New-MusicHubDirectory -Path $config.DataRoot | Out-Null
        $probeFile = Join-Path $config.DataRoot ('.verify-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        Set-Content -LiteralPath $probeFile -Value 'ok' -Encoding ASCII -NoNewline
        $written = Test-Path -LiteralPath $probeFile
        Remove-Item -LiteralPath $probeFile -Force -ErrorAction SilentlyContinue
        if (-not $written) { $storageOk = $false; $storageDetail = 'Probe file was not created' }
    }
    catch {
        $storageOk = $false
        $storageDetail = $_.Exception.Message
    }
    if ($storageOk) {
        $missingDirs = @()
        foreach ($directory in @($config.MusicRoot, $config.ArtworkRoot, $config.QuarantineRoot)) {
            if (-not (Test-Path -LiteralPath $directory)) { $missingDirs += $directory }
        }
        if ($missingDirs.Count -gt 0) {
            Add-MusicHubCheck -List $checks -Name 'storage.writable' -Status 'WARN' -Detail "Root writable but missing subdirectories: $($missingDirs -join ', ')"
        }
        else {
            Add-MusicHubCheck -List $checks -Name 'storage.writable' -Status 'PASS' -Detail "$($config.DataRoot) (music/artwork/quarantine present)"
        }
    }
    else {
        Add-MusicHubCheck -List $checks -Name 'storage.writable' -Status 'FAIL' -Detail "$($config.DataRoot): $storageDetail"
    }
    if ($apiChecks) {
        if ($apiChecks.storageWritable -eq $true) {
            Add-MusicHubCheck -List $checks -Name 'api.storage' -Status 'PASS' -Detail 'API quarantine write probe succeeded'
        }
        else {
            Add-MusicHubCheck -List $checks -Name 'api.storage' -Status 'FAIL' -Detail 'API storageWritable=false'
        }
    }

    # 7. Login smoke --------------------------------------------------------------------------
    $adminEmail = $env:MUSICHUB_ADMIN_EMAIL
    $adminPassword = $env:MUSICHUB_ADMIN_PASSWORD
    if ($SkipLogin) {
        Add-MusicHubCheck -List $checks -Name 'auth.login' -Status 'SKIP' -Detail 'Skipped (-SkipLogin)'
    }
    elseif ([string]::IsNullOrWhiteSpace($adminEmail) -or [string]::IsNullOrWhiteSpace($adminPassword)) {
        Add-MusicHubCheck -List $checks -Name 'auth.login' -Status 'SKIP' -Detail 'MUSICHUB_ADMIN_EMAIL / MUSICHUB_ADMIN_PASSWORD not set'
    }
    else {
        $body = @{ email = $adminEmail; password = $adminPassword; deviceName = 'deploy-verify' } | ConvertTo-Json -Compress
        $login = Invoke-MusicHubHttp -Method POST -Url "$BaseUrl/api/auth/login" -Body $body
        if ($login.StatusCode -eq 200 -and $login.Json -and $login.Json.accessToken) {
            Add-MusicHubCheck -List $checks -Name 'auth.login' -Status 'PASS' -Detail "HTTP 200 for $adminEmail (token received, not printed)"
            $headers = @{ Authorization = "Bearer $($login.Json.accessToken)" }
            $me = Invoke-MusicHubHttp -Url "$BaseUrl/api/auth/me" -Headers $headers
            if ($me.StatusCode -eq 200 -and $me.Json -and "$($me.Json.email)".ToLowerInvariant() -eq $adminEmail.ToLowerInvariant()) {
                Add-MusicHubCheck -List $checks -Name 'auth.me' -Status 'PASS' -Detail "HTTP 200 user=$($me.Json.email) roles=$(@($me.Json.roles) -join ',')"
            }
            else {
                Add-MusicHubCheck -List $checks -Name 'auth.me' -Status 'FAIL' -Detail "HTTP $($me.StatusCode) $($me.Error)"
            }
        }
        elseif ($login.StatusCode -eq 423) {
            Add-MusicHubCheck -List $checks -Name 'auth.login' -Status 'FAIL' -Detail 'HTTP 423 account locked out (too many failed logins)'
        }
        elseif ($login.StatusCode -eq 429) {
            Add-MusicHubCheck -List $checks -Name 'auth.login' -Status 'FAIL' -Detail 'HTTP 429 rate limited; wait a minute and retry'
        }
        else {
            $detail = "HTTP $($login.StatusCode)"
            if ($login.Json -and $login.Json.error) { $detail = "$detail $($login.Json.error)" }
            elseif ($login.Error) { $detail = "$detail $($login.Error)" }
            Add-MusicHubCheck -List $checks -Name 'auth.login' -Status 'FAIL' -Detail $detail
        }
    }

    $failures = Write-MusicHubCheckSummary -List $checks

    if (-not $NoReport) {
        New-MusicHubDirectory -Path $config.LogDir | Out-Null
        $reportPath = Join-Path $config.LogDir 'verify-last.json'
        $report = [ordered]@{
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            baseUrl        = $BaseUrl
            database       = "$($config.Host):$($config.Port)/$($config.Database)"
            dataRoot       = $config.DataRoot
            failures       = $failures
            checks         = $checks
        }
        $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
        Write-MusicHubInfo "Report: $reportPath"
    }

    if ($failures -gt 0) { exit 1 }
    exit 0
}
catch {
    Write-MusicHubError $_.Exception.Message
    exit 1
}
finally {
    if ($config) { [Environment]::SetEnvironmentVariable('PGPASSWORD', $null, 'Process') }
}
