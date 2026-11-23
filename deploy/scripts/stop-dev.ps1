<#
.SYNOPSIS
    Stop the MusicHub Enterprise developer stack.

.DESCRIPTION
    Stops MusicHub.Api and MusicHub.Worker started by deploy/scripts/start-dev.ps1.
    When Windows services with those names are installed, Stop-Service is used instead
    (that path is a real graceful host shutdown). Console hosts do not receive WM_CLOSE,
    so a best-effort taskkill is attempted, followed by a grace period and a forced stop.

    PostgreSQL is left running unless -StopDatabase is supplied; it is then stopped with
    `pg_ctl stop -m fast` (checkpointed, no data loss).

    Idempotent: stopping an already-stopped component reports success.

.PARAMETER StopDatabase
    Also stop PostgreSQL 17 (pg_ctl stop -m fast).

.PARAMETER SkipWorker
    Leave MusicHub.Worker running.

.PARAMETER SkipApi
    Leave MusicHub.Api running.

.PARAMETER GraceSeconds
    Seconds to wait for a graceful exit before forcing termination (default 15).

.EXAMPLE
    .\deploy\scripts\stop-dev.ps1

.EXAMPLE
    .\deploy\scripts\stop-dev.ps1 -StopDatabase
#>
[CmdletBinding()]
param(
    [switch]$StopDatabase,
    [switch]$SkipApi,
    [switch]$SkipWorker,
    [int]$GraceSeconds = 15
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$config = $null
$failures = 0
try {
    $config = Get-MusicHubConfig
    Write-Host ''
    Write-Host 'MusicHub Enterprise - stopping developer stack' -ForegroundColor Cyan

    foreach ($kind in @('Api', 'Worker')) {
        if ($kind -eq 'Api' -and $SkipApi) { Write-MusicHubInfo 'API stop skipped (-SkipApi).'; continue }
        if ($kind -eq 'Worker' -and $SkipWorker) { Write-MusicHubInfo 'Worker stop skipped (-SkipWorker).'; continue }

        $serviceName = "MusicHub.$kind"
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service) {
            if ($service.Status -eq 'Stopped') {
                Write-MusicHubInfo "$serviceName service is already stopped."
                Remove-MusicHubPidFile -Config $config -Kind $kind
                continue
            }
            Write-MusicHubInfo "Stopping Windows service $serviceName (graceful via SCM)."
            try {
                Stop-Service -Name $serviceName -Force -ErrorAction Stop
                Write-MusicHubInfo "$serviceName service stopped."
            }
            catch {
                $failures++
                Write-MusicHubError "Failed to stop service $serviceName`: $($_.Exception.Message)"
            }
            Remove-MusicHubPidFile -Config $config -Kind $kind
            continue
        }

        $targets = New-Object System.Collections.Generic.List[object]
        $pidFile = Get-MusicHubPidFile -Config $config -Kind $kind
        if (Test-Path -LiteralPath $pidFile) {
            try {
                $record = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
                $trackedPid = 0
                if ([int]::TryParse("$($record.processId)", [ref]$trackedPid) -and $trackedPid -gt 0) {
                    $live = Get-Process -Id $trackedPid -ErrorAction SilentlyContinue
                    if ($live) { $targets.Add([pscustomobject]@{ ProcessId = $trackedPid; Source = 'pid file' }) }
                }
            }
            catch { Write-MusicHubWarning "Ignoring unreadable PID file $pidFile." }
        }
        foreach ($process in @(Get-MusicHubProcesses -Kind $kind)) {
            if (-not ($targets | Where-Object { $_.ProcessId -eq $process.ProcessId })) {
                $targets.Add([pscustomobject]@{ ProcessId = $process.ProcessId; Source = 'process scan' })
            }
        }

        if ($targets.Count -eq 0) {
            Write-MusicHubInfo "$kind is not running."
            Remove-MusicHubPidFile -Config $config -Kind $kind
            continue
        }
        foreach ($target in $targets) {
            Write-MusicHubInfo "Stopping $kind process $($target.ProcessId) (source: $($target.Source))."
            $result = Stop-MusicHubHostProcess -ProcessId $target.ProcessId -GraceSeconds $GraceSeconds
            if ($result.Stopped) {
                $suffix = if ($result.Forced) { ' (forced after grace period)' } else { '' }
                Write-MusicHubInfo "Stopped PID $($result.ProcessId)$suffix."
            }
            else {
                $failures++
                Write-MusicHubError "PID $($result.ProcessId) did not exit: $($result.Note)"
            }
        }
        Remove-MusicHubPidFile -Config $config -Kind $kind
    }

    if ($StopDatabase) {
        Write-MusicHubInfo 'Stopping PostgreSQL (pg_ctl stop -m fast).'
        try {
            $result = Stop-MusicHubDatabase -Config $config -Mode fast
            Write-MusicHubInfo $result.Note
        }
        catch {
            $failures++
            Write-MusicHubError "PostgreSQL stop failed: $($_.Exception.Message)"
        }

        # Terminate any lingering postgres processes for this data directory
        $lingering = Get-CimInstance Win32_Process -Filter "Name='postgres.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine -like "*$($config.PgDataDir)*" }
        foreach ($proc in $lingering) {
            Write-MusicHubInfo "Stopping lingering postgres.exe process (PID $($proc.ProcessId))."
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        }

        $pidFile = Join-Path $config.PgDataDir 'postmaster.pid'
        if (Test-Path -LiteralPath $pidFile) {
            Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-MusicHubInfo 'PostgreSQL left running (use -StopDatabase to stop it).'
    }

    Write-Host ''
    if ($failures -gt 0) {
        Write-Host "Stop completed with $failures failure(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host 'Stop completed.' -ForegroundColor Green
    exit 0
}
catch {
    Write-MusicHubError $_.Exception.Message
    exit 1
}
finally {
    if ($config) { [Environment]::SetEnvironmentVariable('PGPASSWORD', $null, 'Process') }
}
