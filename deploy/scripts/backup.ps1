<#
.SYNOPSIS
    Online logical backup of the MusicHub Enterprise database with a verified manifest.

.DESCRIPTION
    Creates a PostgreSQL custom-format dump (`pg_dump -Fc`) while the API and worker keep
    running, plus a JSON manifest containing timestamp, size, SHA-256, the schema migration
    id and the application version.

    Every dump is verified immediately with `pg_restore --list`; the result is recorded in
    the manifest. Backups default to <data root>\backups (gitignored storage). Writing a dump
    inside the repository outside the gitignored storage directory is refused.

    Retention: -KeepDays (default 14) and -KeepCount (default 30) remove only backup pairs
    that this script created (*.dump + *.dump.manifest.json) after a successful backup.

.PARAMETER BackupDirectory
    Target directory. Default: <MUSICHUB_DATA_ROOT>\backups.

.PARAMETER Database
    Database to dump. Default: the configured MusicHub database.

.PARAMETER Verify
    Do not create a backup. Re-verify existing dump/manifest pairs (SHA-256 + pg_restore --list).

.PARAMETER BackupFile
    With -Verify: verify a single dump file instead of the directory.

.PARAMETER KeepDays
    Delete backups older than this many days (0 disables age-based pruning).

.PARAMETER KeepCount
    Always keep at least this many of the newest backups (0 disables count-based pruning).

.PARAMETER Label
    Free-form label stored in the manifest (for example "pre-upgrade-1.1.0").

.EXAMPLE
    .\deploy\scripts\backup.ps1

.EXAMPLE
    .\deploy\scripts\backup.ps1 -Label "pre-upgrade" -KeepDays 30 -KeepCount 60

.EXAMPLE
    .\deploy\scripts\backup.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string]$BackupDirectory = '',
    [string]$Database = '',
    [switch]$Verify,
    [string]$BackupFile = '',
    [int]$KeepDays = 14,
    [int]$KeepCount = 30,
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

function Assert-MusicHubBackupLocation {
    <# Refuse to place backups inside the repository source tree outside the gitignored storage directory. #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot
    )
    $full = [System.IO.Path]::GetFullPath($Path)
    $repoFull = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    $storageFull = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'storage')).TrimEnd('\')
    if ($full.StartsWith($repoFull + '\', [StringComparison]::OrdinalIgnoreCase) -and
        -not $full.StartsWith($storageFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write backups to '$full': inside the repository but outside the gitignored storage directory. Use storage\\backups or an external volume."
    }
    return $full
}

function Get-MusicHubAppVersion {
    param([Parameter(Mandatory)]$Config)
    $version = Invoke-MusicHubHttp -Url "$($Config.ApiBaseUrl)/api/version" -TimeoutSec 10
    if ($version.StatusCode -eq 200 -and $version.Json -and $version.Json.version) {
        return [pscustomobject]@{ Version = "$($version.Json.version)"; Environment = "$($version.Json.environment)"; Source = 'api' }
    }
    return [pscustomobject]@{ Version = 'unknown'; Environment = 'unknown'; Source = 'unavailable' }
}

function Get-MusicHubRepositoryRevision {
    param([Parameter(Mandatory)][string]$RepoRoot)
    try {
        $previousErrorAction = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $output = & git -C $RepoRoot rev-parse --short HEAD 2>$null
        $exit = $LASTEXITCODE
        return $(if ($exit -eq 0 -and $output) { ("$output").Trim() } else { '' })
    }
    catch { return '' }
    finally { $ErrorActionPreference = $previousErrorAction }
}

function Write-MusicHubBackupManifest {
    param(
        [Parameter(Mandatory)][string]$DumpPath,
        [Parameter(Mandatory)]$Manifest
    )
    $manifestPath = "$DumpPath.manifest.json"
    $Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    return $manifestPath
}

function Test-MusicHubDumpIntegrity {
    <# SHA-256 vs manifest plus `pg_restore --list` structural check. #>
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$DumpPath
    )
    $result = [ordered]@{
        dumpPath        = $DumpPath
        exists          = $false
        sizeBytes       = 0
        sha256          = ''
        manifestPath    = "$DumpPath.manifest.json"
        manifestPresent = $false
        manifestSha256  = ''
        sha256Match     = $false
        restoreListOk   = $false
        tocEntries      = 0
        error           = ''
    }
    if (-not (Test-Path -LiteralPath $DumpPath)) {
        $result.error = 'dump file not found'
        return [pscustomobject]$result
    }
    $result.exists = $true
    $file = Get-Item -LiteralPath $DumpPath
    $result.sizeBytes = $file.Length
    $result.sha256 = (Get-FileHash -LiteralPath $DumpPath -Algorithm SHA256).Hash.ToLowerInvariant()

    if (Test-Path -LiteralPath $result.manifestPath) {
        $result.manifestPresent = $true
        try {
            $manifest = Get-Content -LiteralPath $result.manifestPath -Raw | ConvertFrom-Json
            if ($manifest.sha256) { $result.manifestSha256 = "$($manifest.sha256)".ToLowerInvariant() }
            $result.sha256Match = ($result.manifestSha256 -eq $result.sha256)
        }
        catch { $result.error = "manifest unreadable: $($_.Exception.Message)" }
    }

    if (-not $Config.PgRestore) {
        $result.error = 'pg_restore not available; cannot verify the archive'
        return [pscustomobject]$result
    }
    $listArgs = @('-l', $DumpPath)
    $list = Invoke-MusicHubNative -FilePath $Config.PgRestore -Arguments $listArgs -Environment @{ PGPASSWORD = $Config.Password } -AllowFailure
    if ($list.ExitCode -eq 0) {
        $result.restoreListOk = $true
        $entries = @($list.Lines | Where-Object { "$_".Trim() -ne '' -and -not "$_".TrimStart().StartsWith(';') })
        $result.tocEntries = $entries.Count
    }
    else {
        $result.error = "pg_restore --list failed: $($list.Text)"
    }
    return [pscustomobject]$result
}

$config = $null
$exitCode = 0
try {
    $config = Get-MusicHubConfig
    if ([string]::IsNullOrWhiteSpace($BackupDirectory)) { $BackupDirectory = $config.BackupDir }
    $BackupDirectory = Assert-MusicHubBackupLocation -Path $BackupDirectory -RepoRoot $config.RepoRoot
    if ([string]::IsNullOrWhiteSpace($Database)) { $Database = $config.Database }

    Write-Host ''
    Write-Host 'MusicHub Enterprise - database backup' -ForegroundColor Cyan
    Write-MusicHubInfo "Source:   $($config.Host):$($config.Port)/$Database (user $($config.Username))"
    Write-MusicHubInfo "Target:   $BackupDirectory"

    if (-not $config.PgDump) { throw 'pg_dump was not found. Expected tools\pgsql\bin\pg_dump.exe.' }
    if (-not $config.PgRestore) { throw 'pg_restore was not found. Expected tools\pgsql\bin\pg_restore.exe.' }

    if ($Verify) {
        $targets = @()
        if ($BackupFile) {
            $resolved = (Resolve-Path -LiteralPath $BackupFile -ErrorAction Stop).Path
            Assert-MusicHubBackupLocation -Path $resolved -RepoRoot $config.RepoRoot | Out-Null
            $targets = @($resolved)
        }
        else {
            if (-not (Test-Path -LiteralPath $BackupDirectory)) { throw "Backup directory does not exist: $BackupDirectory" }
            $targets = @(Get-ChildItem -LiteralPath $BackupDirectory -Filter '*.dump' | Sort-Object LastWriteTime -Descending | ForEach-Object { $_.FullName })
        }
        if ($targets.Count -eq 0) { throw "No .dump files found in $BackupDirectory." }
        $checks = New-MusicHubCheckList
        foreach ($target in $targets) {
            $verifyResult = Test-MusicHubDumpIntegrity -Config $config -DumpPath $target
            $status = if ($verifyResult.exists -and $verifyResult.sha256Match -and $verifyResult.restoreListOk) { 'PASS' } else { 'FAIL' }
            $detail = "$([Math]::Round($verifyResult.sizeBytes / 1MB, 2)) MiB sha256=$($verifyResult.sha256.Substring(0, [Math]::Min(16, $verifyResult.sha256.Length)))... toc=$($verifyResult.tocEntries)"
            if (-not $verifyResult.manifestPresent) { $detail += ' (manifest missing)' }
            if ($verifyResult.error) { $detail += " error=$($verifyResult.error)" }
            Add-MusicHubCheck -List $checks -Name (Split-Path -Leaf $target) -Status $status -Detail $detail
        }
        $failures = Write-MusicHubCheckSummary -List $checks
        if ($failures -gt 0) { exit 1 }
        exit 0
    }

    if (-not (Test-MusicHubPostgresReady -Config $config -Quiet)) {
        throw "PostgreSQL is not reachable at $($config.Host):$($config.Port). Start it (deploy/scripts/start-dev.ps1) and retry."
    }
    New-MusicHubDirectory -Path $BackupDirectory | Out-Null

    $timestamp = [DateTimeOffset]::UtcNow
    $stamp = $timestamp.ToString('yyyyMMdd''T''HHmmss''Z''')
    $safeDatabase = ($Database -replace '[^A-Za-z0-9_.-]', '_')
    $dumpPath = Join-Path $BackupDirectory ("musichub-$safeDatabase-$stamp.dump")

    $serverVersion = Get-MusicHubPsqlScalar -Config $config -Database $Database -Sql 'show server_version;' -AllowFailure
    $latestMigration = Get-MusicHubPsqlScalar -Config $config -Database $Database -Sql 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1;' -AllowFailure
    $appVersion = Get-MusicHubAppVersion -Config $config
    $revision = Get-MusicHubRepositoryRevision -RepoRoot $config.RepoRoot
    $pgDumpVersionOutput = Invoke-MusicHubNative -FilePath $config.PgDump -Arguments @('--version') -AllowFailure
    $pgDumpVersion = (($pgDumpVersionOutput.Text -split "`n")[0]).Trim()

    Write-MusicHubStep 'Running pg_dump -Fc'
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $dumpArgs = @(
        '-h', $config.Host,
        '-p', "$($config.Port)",
        '-U', $config.Username,
        '-d', $Database,
        '-Fc',
        '--no-password',
        '--verbose',
        '-f', $dumpPath
    )
    $dump = Invoke-MusicHubNative -FilePath $config.PgDump -Arguments $dumpArgs -Environment @{ PGPASSWORD = $config.Password } -AllowFailure
    $stopwatch.Stop()
    if ($dump.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $dumpPath) { Remove-Item -LiteralPath $dumpPath -Force -ErrorAction SilentlyContinue }
        throw "pg_dump failed with exit code $($dump.ExitCode). $($dump.Text)"
    }
    if (-not (Test-Path -LiteralPath $dumpPath)) { throw "pg_dump reported success but $dumpPath does not exist." }
    $dumpInfo = Get-Item -LiteralPath $dumpPath
    Write-MusicHubInfo ("Dump written: {0} ({1:N2} MiB) in {2:N1}s" -f $dumpInfo.FullName, ($dumpInfo.Length / 1MB), $stopwatch.Elapsed.TotalSeconds)

    Write-MusicHubStep 'Verifying dump with pg_restore --list'
    $integrity = Test-MusicHubDumpIntegrity -Config $config -DumpPath $dumpPath
    if (-not $integrity.restoreListOk) { throw "Dump verification failed: $($integrity.error)" }
    Write-MusicHubInfo "pg_restore --list OK ($($integrity.tocEntries) table-of-contents entries)"

    $manifest = [ordered]@{
        manifestVersion  = 1
        tool             = 'deploy/scripts/backup.ps1'
        createdAtUtc     = $timestamp.ToString('o')
        label            = $Label
        host             = $config.Host
        port             = $config.Port
        database         = $Database
        username         = $config.Username
        serverVersion    = $serverVersion
        latestMigration  = $latestMigration
        appVersion       = $appVersion.Version
        appEnvironment   = $appVersion.Environment
        repositoryRevision = $revision
        pgDumpVersion    = $pgDumpVersion
        format           = 'custom (pg_dump -Fc)'
        fileName         = $dumpInfo.Name
        filePath         = $dumpInfo.FullName
        sizeBytes        = $dumpInfo.Length
        sha256           = $integrity.sha256
        restoreListVerified = $integrity.restoreListOk
        tocEntries       = $integrity.tocEntries
        durationMs       = [int]$stopwatch.Elapsed.TotalMilliseconds
        machine          = $env:COMPUTERNAME
        operator         = $env:USERNAME
    }
    $manifestPath = Write-MusicHubBackupManifest -DumpPath $dumpPath -Manifest $manifest
    Write-MusicHubInfo "Manifest: $manifestPath"

    Write-MusicHubStep 'Retention'
    $pruned = 0
    if ($KeepDays -gt 0 -or $KeepCount -gt 0) {
        $pairs = @(Get-ChildItem -LiteralPath $BackupDirectory -Filter '*.dump' | Sort-Object LastWriteTime -Descending)
        $cutoff = (Get-Date).AddDays(-[Math]::Abs($KeepDays))
        for ($index = 0; $index -lt $pairs.Count; $index++) {
            $candidate = $pairs[$index]
            if ($candidate.FullName -eq $dumpInfo.FullName) { continue }
            $tooOld = ($KeepDays -gt 0 -and $candidate.LastWriteTime -lt $cutoff)
            $tooMany = ($KeepCount -gt 0 -and $index -ge $KeepCount)
            if ($tooOld -or $tooMany) {
                Remove-Item -LiteralPath $candidate.FullName -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath "$($candidate.FullName).manifest.json" -Force -ErrorAction SilentlyContinue
                $pruned++
                Write-MusicHubInfo "Pruned $($candidate.Name)"
            }
        }
    }
    else {
        Write-MusicHubInfo 'Retention pruning disabled (KeepDays=0, KeepCount=0).'
    }

    Write-MusicHubStep 'Summary'
    Write-MusicHubInfo "Backup:    $($dumpInfo.FullName)"
    Write-MusicHubInfo ("Size:      {0:N2} MiB ({1} bytes)" -f ($dumpInfo.Length / 1MB), $dumpInfo.Length)
    Write-MusicHubInfo "SHA-256:   $($integrity.sha256)"
    Write-MusicHubInfo "Migration: $latestMigration"
    Write-MusicHubInfo "App:       $($appVersion.Version) ($($appVersion.Environment))"
    Write-MusicHubInfo "Verified:  pg_restore --list OK, $($integrity.tocEntries) TOC entries"
    Write-MusicHubInfo "Pruned:    $pruned old backup(s)"
    Write-MusicHubInfo 'Copy this directory to off-host storage; a backup on the same disk is not a disaster-recovery plan.'
    Write-Host ''
    Write-Host 'Backup completed and verified.' -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-MusicHubError $_.Exception.Message
}
finally {
    if ($config) { [Environment]::SetEnvironmentVariable('PGPASSWORD', $null, 'Process') }
}

exit $exitCode
