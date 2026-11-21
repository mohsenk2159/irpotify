<#
.SYNOPSIS
    External monitoring probe for MusicHub Enterprise.

.DESCRIPTION
    Lightweight health probe intended for a scheduler or monitoring agent (Task Scheduler,
    Zabbix/Nagios user parameter, Uptime Kuma push, etc.).

    Checks:
      * GET /healthz (process liveness)
      * GET /api/health (database + storage write probe reported by the API)
      * Worker heartbeat age (AppSettings 'worker.heartbeat'), read either through
        GET /api/admin/metrics (admin credentials required) or directly from PostgreSQL
        when local psql and a connection string are available.
      * Optional ffprobe state as reported by the API.

    Exit codes:
      0  healthy (all checks passed, warnings allowed only with -AllowWarnings)
      1  unhealthy (any check failed)
      2  configuration/usage error (API unreachable at the transport level, bad URL)
      3  degraded/unknown (probe could not evaluate the worker heartbeat)

    This script never prints secrets or access tokens.

.PARAMETER BaseUrl
    API base URL (default MUSICHUB_BASE_URL or http://127.0.0.1:5080).

.PARAMETER MaxHeartbeatAgeSeconds
    Maximum acceptable worker heartbeat age (default 180).

.PARAMETER SkipWorker
    Do not evaluate the worker heartbeat (useful for API-only probes).

.PARAMETER Json
    Emit a single JSON object instead of human-readable lines.

.EXAMPLE
    .\deploy\scripts\healthcheck.ps1

.EXAMPLE
    .\deploy\scripts\healthcheck.ps1 -Json -MaxHeartbeatAgeSeconds 300
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = '',
    [int]$MaxHeartbeatAgeSeconds = 180,
    [int]$TimeoutSeconds = 10,
    [switch]$SkipWorker,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$results = New-MusicHubCheckList
$exitCode = 0
$config = $null
try {
    $config = Get-MusicHubConfig
    if ([string]::IsNullOrWhiteSpace($BaseUrl)) { $BaseUrl = $config.ApiBaseUrl }
    $BaseUrl = $BaseUrl.TrimEnd('/')
    if ($BaseUrl -notmatch '^https?://') { Write-MusicHubError "BaseUrl must be an absolute http(s) URL: $BaseUrl"; exit 2 }

    $healthz = Invoke-MusicHubHttp -Url "$BaseUrl/healthz" -TimeoutSec $TimeoutSeconds
    if ($healthz.StatusCode -eq 200) { Add-MusicHubCheck -List $results -Name 'api.liveness' -Status 'PASS' -Detail 'HTTP 200 /healthz' }
    elseif ($healthz.StatusCode -eq 0) {
        Add-MusicHubCheck -List $results -Name 'api.liveness' -Status 'FAIL' -Detail "unreachable: $($healthz.Error)"
        $exitCode = 2
    }
    else { Add-MusicHubCheck -List $results -Name 'api.liveness' -Status 'FAIL' -Detail "HTTP $($healthz.StatusCode)" }

    if ($exitCode -ne 2) {
        $health = Invoke-MusicHubHttp -Url "$BaseUrl/api/health" -TimeoutSec $TimeoutSeconds
        if ($health.StatusCode -eq 200 -and $health.Json) {
            $checks = $health.Json.checks
            Add-MusicHubCheck -List $results -Name 'api.readiness' -Status 'PASS' -Detail "status=$($health.Json.status)"
            if ($checks.database -eq $true) { Add-MusicHubCheck -List $results -Name 'database' -Status 'PASS' -Detail 'API reports database=true' }
            else { Add-MusicHubCheck -List $results -Name 'database' -Status 'FAIL' -Detail 'API reports database=false' }
            if ($checks.storageWritable -eq $true) { Add-MusicHubCheck -List $results -Name 'storage' -Status 'PASS' -Detail 'API storage write probe ok' }
            else { Add-MusicHubCheck -List $results -Name 'storage' -Status 'FAIL' -Detail 'API reports storageWritable=false' }
            if ($checks.ffprobe -eq $true) { Add-MusicHubCheck -List $results -Name 'ffprobe' -Status 'PASS' -Detail 'API reports ffprobe available' }
            else { Add-MusicHubCheck -List $results -Name 'ffprobe' -Status 'WARN' -Detail 'API reports ffprobe unavailable; uploads will fail validation' }
        }
        elseif ($health.StatusCode -eq 503 -and $health.Json) {
            Add-MusicHubCheck -List $results -Name 'api.readiness' -Status 'FAIL' -Detail "HTTP 503 status=$($health.Json.status)"
        }
        else {
            Add-MusicHubCheck -List $results -Name 'api.readiness' -Status 'FAIL' -Detail "HTTP $($health.StatusCode) $($health.Error)"
        }

        if (-not $SkipWorker) {
            $heartbeatAge = -1
            $source = ''
            $adminEmail = $env:MUSICHUB_ADMIN_EMAIL
            $adminPassword = $env:MUSICHUB_ADMIN_PASSWORD
            if ($adminEmail -and $adminPassword) {
                $loginBody = @{ email = $adminEmail; password = $adminPassword; deviceName = 'deploy-healthcheck' } | ConvertTo-Json -Compress
                $login = Invoke-MusicHubHttp -Method POST -Url "$BaseUrl/api/auth/login" -Body $loginBody -TimeoutSec $TimeoutSeconds
                if ($login.StatusCode -eq 200 -and $login.Json -and $login.Json.accessToken) {
                    $metrics = Invoke-MusicHubHttp -Url "$BaseUrl/api/admin/metrics" -Headers @{ Authorization = "Bearer $($login.Json.accessToken)" } -TimeoutSec 30
                    if ($metrics.StatusCode -eq 200 -and $metrics.Json -and $metrics.Json.workerHeartbeat) {
                        $parsedHeartbeat = [DateTimeOffset]::MinValue
                        if ([DateTimeOffset]::TryParse("$($metrics.Json.workerHeartbeat)", [ref]$parsedHeartbeat)) {
                            $heartbeatAge = [int][Math]::Round(([DateTimeOffset]::UtcNow - $parsedHeartbeat.ToUniversalTime()).TotalSeconds)
                            $source = 'api.metrics'
                        }
                    }
                }
            }
            if ($heartbeatAge -lt 0 -and $config.Psql -and (Test-MusicHubPostgresReady -Config $config -Quiet)) {
                $heartbeat = Get-MusicHubWorkerHeartbeat -Config $config
                if ($heartbeat.Present) {
                    $heartbeatAge = $heartbeat.AgeSeconds
                    $source = 'database'
                }
            }
            if ($heartbeatAge -lt 0) {
                Add-MusicHubCheck -List $results -Name 'worker.heartbeat' -Status 'WARN' -Detail 'unavailable (no admin credentials and no reachable local database)'
                if ($exitCode -eq 0) { $exitCode = 3 }
            }
            elseif ($heartbeatAge -gt $MaxHeartbeatAgeSeconds) {
                Add-MusicHubCheck -List $results -Name 'worker.heartbeat' -Status 'FAIL' -Detail "$heartbeatAge s old via $source (limit ${MaxHeartbeatAgeSeconds}s)"
            }
            else {
                Add-MusicHubCheck -List $results -Name 'worker.heartbeat' -Status 'PASS' -Detail "$heartbeatAge s old via $source (limit ${MaxHeartbeatAgeSeconds}s)"
            }
        }
    }

    $failures = Get-MusicHubCheckFailureCount -List $results
    if ($failures -gt 0) {
        $exitCode = 1
    }
    elseif ($exitCode -eq 0) {
        $warnings = @($results | Where-Object { $_.Status -eq 'WARN' }).Count
        if ($warnings -gt 0) { $exitCode = 3 }
    }

    if ($Json) {
        $payload = [ordered]@{
            status     = switch ($exitCode) { 0 { 'healthy' } 3 { 'degraded' } 2 { 'unknown' } default { 'unhealthy' } }
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            baseUrl    = $BaseUrl
            exitCode   = $exitCode
            checks     = $results
        }
        $payload | ConvertTo-Json -Depth 5 -Compress | Write-Output
    }
    else {
        Write-Host ''
        $results | Format-Table -AutoSize Check, Status, Detail | Out-String -Width 200 | Write-Host
        $label = switch ($exitCode) { 0 { 'HEALTHY' } 3 { 'DEGRADED' } 2 { 'UNKNOWN/CONFIG ERROR' } default { 'UNHEALTHY' } }
        $color = switch ($exitCode) { 0 { 'Green' } 3 { 'Yellow' } default { 'Red' } }
        Write-Host "Result: $label (exit $exitCode)" -ForegroundColor $color
    }
}
catch {
    Write-MusicHubError $_.Exception.Message
    if ($Json) { Write-Output (@{ status = 'unknown'; exitCode = 2; error = $_.Exception.Message } | ConvertTo-Json -Compress) }
    exit 2
}
finally {
    if ($config) { [Environment]::SetEnvironmentVariable('PGPASSWORD', $null, 'Process') }
}

exit $exitCode
