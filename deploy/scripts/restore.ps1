<#
.SYNOPSIS
    Restore a MusicHub Enterprise database dump with explicit safety gates.

.DESCRIPTION
    Default behaviour is a side-by-side restore into a NEW database (musichub_restore) so the
    live database is never touched. The script:

      1. Requires -BackupFile and -ConfirmRestore.
      2. Verifies the dump against its manifest SHA-256 (and pg_restore --list).
      3. Creates/recreates the target database (asked for -ReplaceTargetDatabase when it exists).
      4. Restores with `pg_restore --no-owner --no-privileges --exit-on-error`.
      5. Reports table counts and the applied migration in the target database.
      6. Prints the exact next steps (it never switches application configuration for you).

    Restoring over the live database additionally requires BOTH -AllowLiveRestore and
    -ConfirmDestructiveLiveRestore, and always stops MusicHub.Api/MusicHub.Worker first.

.PARAMETER BackupFile
    Path to the .dump file produced by deploy/scripts/backup.ps1.

.PARAMETER ConfirmRestore
    Required acknowledgement that this creates/overwrites the target database.

.PARAMETER TargetDatabase
    Restore target. Default: musichub_restore. The live database name is refused without the
    destructive live-restore switches.

.PARAMETER ReplaceTargetDatabase
    Drop and recreate the target when it already contains tables.

.PARAMETER AllowLiveRestore
    Permit the live database as the target (still requires -ConfirmDestructiveLiveRestore).

.PARAMETER ConfirmDestructiveLiveRestore
    Second, explicit acknowledgement for overwriting the live database.

.PARAMETER SkipManifestVerification
    Allow restoring a dump that has no manifest file. A SHA-256 mismatch still fails.

.PARAMETER StopServices
    Also stop API/Worker for a non-live restore (they are always stopped for a live restore).

.PARAMETER KeepServicesStopped
    Do not print restart instructions (useful inside a maintenance window script).

.EXAMPLE
    .\deploy\scripts\restore.ps1 -BackupFile storage\backups\musichub-musichub-20261116T010000Z.dump -ConfirmRestore

.EXAMPLE
    # Disaster recovery over the live database (maintenance window, API/Worker stopped by the script)
    .\deploy\scripts\restore.ps1 -BackupFile <dump> -ConfirmRestore -TargetDatabase musichub `
        -AllowLiveRestore -ConfirmDestructiveLiveRestore
#>
[CmdletBinding()]
param(
    [string]$BackupFile = '',
    [switch]$ConfirmRestore,
    [string]$TargetDatabase = 'musichub_restore',
    [switch]$ReplaceTargetDatabase,
    [switch]$AllowLiveRestore,
    [switch]$ConfirmDestructiveLiveRestore,
    [switch]$SkipManifestVerification,
    [switch]$StopServices,
    [switch]$KeepServicesStopped
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

function Get-MusicHubDatabaseExists {
    param([Parameter(Mandatory)]$Config, [Parameter(Mandatory)][string]$Name)
    $value = Get-MusicHubPsqlScalar -Config $Config -Database 'postgres' -Sql "select count(*) from pg_database where datname = '$Name';" -AllowFailure
    return ($value -eq '1')
}

function Get-MusicHubDatabaseTableCount {
    param([Parameter(Mandatory)]$Config, [Parameter(Mandatory)][string]$Name)
    $value = Get-MusicHubPsqlScalar -Config $Config -Database $Name -Sql "select count(*) from information_schema.tables where table_schema = 'public';" -AllowFailure
    if (-not $value) { return 0 }
    return [int]$value
}

$config = $null
$exitCode = 0
try {
    $config = Get-MusicHubConfig
    Write-Host ''
    Write-Host 'MusicHub Enterprise - database restore' -ForegroundColor Cyan

    # ---- Gate 1: explicit acknowledgement ---------------------------------------------------
    if ([string]::IsNullOrWhiteSpace($BackupFile)) { throw 'Missing -BackupFile. Pass the .dump file to restore.' }
    if (-not $ConfirmRestore) { throw 'Refusing to run without -ConfirmRestore. This operation creates or replaces a database.' }
    if (-not (Test-Path -LiteralPath $BackupFile)) { throw "Backup file not found: $BackupFile" }
    $dumpPath = (Resolve-Path -LiteralPath $BackupFile).Path
    if (-not $config.PgRestore) { throw 'pg_restore was not found. Expected tools\pgsql\bin\pg_restore.exe.' }
    if (-not $config.Psql) { throw 'psql was not found. Expected tools\pgsql\bin\psql.exe.' }

    if ($TargetDatabase -notmatch '^[A-Za-z0-9_]+$') { throw "TargetDatabase must match ^[A-Za-z0-9_]+$ : $TargetDatabase" }
    if ($TargetDatabase -in @('postgres', 'template0', 'template1')) { throw "TargetDatabase '$TargetDatabase' is a PostgreSQL system database and cannot be used." }

    $liveDatabase = $config.Database
    $isLive = ($TargetDatabase -eq $liveDatabase)
    if ($isLive -and -not ($AllowLiveRestore -and $ConfirmDestructiveLiveRestore)) {
        throw "Refusing to restore over the live database '$liveDatabase'. Supply BOTH -AllowLiveRestore and -ConfirmDestructiveLiveRestore, or restore into a side database (default musichub_restore)."
    }

    Write-MusicHubInfo "Dump:     $dumpPath"
    Write-MusicHubInfo "Target:   $($config.Host):$($config.Port)/$TargetDatabase$(if ($isLive) { '  [LIVE DATABASE]' } else { '' })"
    Write-MusicHubInfo "Live DB:  $liveDatabase"

    # ---- Gate 2: manifest + SHA-256 ---------------------------------------------------------
    $manifestPath = "$dumpPath.manifest.json"
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $expected = "$($manifest.sha256)".ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $dumpPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $expected) { throw "Manifest $manifestPath has no sha256 value; refusing to continue." }
        if ($expected -ne $actual) {
            throw "SHA-256 mismatch. Manifest=$expected actual=$actual. The dump is corrupt or was modified; do not restore it."
        }
        $size = (Get-Item -LiteralPath $dumpPath).Length
        if ($manifest.sizeBytes -and [long]$manifest.sizeBytes -ne $size) {
            throw "Size mismatch against manifest ($($manifest.sizeBytes) vs $size bytes). Refusing to restore."
        }
        Write-MusicHubInfo "Manifest verified: SHA-256 $actual"
        Write-MusicHubInfo "Dump metadata: migration=$($manifest.latestMigration) appVersion=$($manifest.appVersion) createdUtc=$($manifest.createdAtUtc)"
    }
    else {
        if (-not $SkipManifestVerification) {
            throw "No manifest found at $manifestPath. Re-create the backup with deploy/scripts/backup.ps1, or pass -SkipManifestVerification to accept an unverified dump."
        }
        Write-MusicHubWarning 'Restoring without a manifest (-SkipManifestVerification). Integrity cannot be proven.'
    }

    # ---- Gate 3: archive structure ----------------------------------------------------------
    $list = Invoke-MusicHubNative -FilePath $config.PgRestore -Arguments @('-l', $dumpPath) -AllowFailure
    if ($list.ExitCode -ne 0) { throw "pg_restore --list failed; the archive is not restorable. $($list.Text)" }
    $tocEntries = @($list.Lines | Where-Object { "$_".Trim() -ne '' -and -not "$_".TrimStart().StartsWith(';') }).Count
    Write-MusicHubInfo "Archive structure verified: $tocEntries TOC entries."

    if (-not (Test-MusicHubPostgresReady -Config $config -Quiet)) {
        throw "PostgreSQL is not reachable at $($config.Host):$($config.Port)."
    }

    # ---- Stop the application hosts ---------------------------------------------------------
    $stoppedHosts = New-Object System.Collections.Generic.List[string]
    if ($isLive -or $StopServices) {
        Write-MusicHubStep 'Stopping MusicHub.Api and MusicHub.Worker'
        foreach ($kind in @('Api', 'Worker')) {
            $serviceName = "MusicHub.$kind"
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($service -and $service.Status -ne 'Stopped') {
                Stop-Service -Name $serviceName -Force -ErrorAction Stop
                Write-MusicHubInfo "Stopped Windows service $serviceName."
                $stoppedHosts.Add("service:$serviceName")
                continue
            }
            foreach ($process in @(Get-MusicHubProcesses -Kind $kind)) {
                $result = Stop-MusicHubHostProcess -ProcessId $process.ProcessId
                if ($result.Stopped) {
                    Write-MusicHubInfo "Stopped $kind process $($process.ProcessId)$(if ($result.Forced) { ' (forced)' })."
                    $stoppedHosts.Add("process:$($process.ProcessId)")
                }
                else {
                    throw "Could not stop $kind process $($process.ProcessId): $($result.Note)"
                }
            }
            Remove-MusicHubPidFile -Config $config -Kind $kind
        }
        if ($stoppedHosts.Count -eq 0) { Write-MusicHubInfo 'No running API/Worker processes or services found.' }
    }
    else {
        Write-MusicHubInfo 'API/Worker left running (side restore into a different database). Use -StopServices to stop them.'
    }

    # ---- Prepare target database ------------------------------------------------------------
    Write-MusicHubStep "Preparing target database '$TargetDatabase'"
    $exists = Get-MusicHubDatabaseExists -Config $config -Name $TargetDatabase
    if ($exists) {
        $tableCount = Get-MusicHubDatabaseTableCount -Config $config -Name $TargetDatabase
        if ($tableCount -gt 0 -and -not $ReplaceTargetDatabase -and -not $isLive) {
            throw "Target database '$TargetDatabase' already contains $tableCount tables. Pass -ReplaceTargetDatabase to drop and recreate it."
        }
        $connections = Get-MusicHubPsqlScalar -Config $config -Database 'postgres' -Sql "select count(*) from pg_stat_activity where datname = '$TargetDatabase';" -AllowFailure
        Write-MusicHubInfo "Target database exists ($tableCount tables, $connections connections). Dropping and recreating."
        if (-not $config.DropDb) { throw 'dropdb was not found; cannot recreate the target database.' }
        $drop = Invoke-MusicHubNative -FilePath $config.DropDb -Arguments @('-h', $config.Host, '-p', "$($config.Port)", '-U', $config.Username, '--force', '--if-exists', $TargetDatabase) -Environment @{ PGPASSWORD = $config.Password } -AllowFailure
        if ($drop.ExitCode -ne 0) { throw "dropdb failed (exit $($drop.ExitCode)). $($drop.Text)" }
    }
    if (-not $config.Createdb) { throw 'createdb was not found; cannot create the target database.' }
    $create = Invoke-MusicHubNative -FilePath $config.Createdb -Arguments @('-h', $config.Host, '-p', "$($config.Port)", '-U', $config.Username, '-E', 'UTF8', $TargetDatabase) -Environment @{ PGPASSWORD = $config.Password } -AllowFailure
    if ($create.ExitCode -ne 0) { throw "createdb failed (exit $($create.ExitCode)). $($create.Text)" }
    Write-MusicHubInfo "Created database '$TargetDatabase'."

    # ---- Restore ----------------------------------------------------------------------------
    Write-MusicHubStep 'Restoring with pg_restore'
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $restoreArgs = @(
        '-h', $config.Host,
        '-p', "$($config.Port)",
        '-U', $config.Username,
        '-d', $TargetDatabase,
        '--no-owner',
        '--no-privileges',
        '--exit-on-error',
        '--verbose',
        $dumpPath
    )
    $restore = Invoke-MusicHubNative -FilePath $config.PgRestore -Arguments $restoreArgs -Environment @{ PGPASSWORD = $config.Password } -AllowFailure
    $stopwatch.Stop()
    if ($restore.ExitCode -ne 0) { throw "pg_restore failed (exit $($restore.ExitCode)) after $([Math]::Round($stopwatch.Elapsed.TotalSeconds,1))s. $($restore.Text)" }
    Write-MusicHubInfo ("Restore finished in {0:N1}s." -f $stopwatch.Elapsed.TotalSeconds)

    # ---- Post-restore verification ----------------------------------------------------------
    Write-MusicHubStep 'Post-restore verification'
    $restoredTables = Get-MusicHubDatabaseTableCount -Config $config -Name $TargetDatabase
    $restoredMigration = Get-MusicHubPsqlScalar -Config $config -Database $TargetDatabase -Sql 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1;' -AllowFailure
    $users = Get-MusicHubPsqlScalar -Config $config -Database $TargetDatabase -Sql 'select count(*) from "Users";' -AllowFailure
    $tracks = Get-MusicHubPsqlScalar -Config $config -Database $TargetDatabase -Sql 'select count(*) from "Tracks";' -AllowFailure
    $assets = Get-MusicHubPsqlScalar -Config $config -Database $TargetDatabase -Sql 'select count(*) from "AudioAssets";' -AllowFailure
    Write-MusicHubInfo "Tables:     $restoredTables"
    Write-MusicHubInfo "Migration:  $restoredMigration"
    Write-MusicHubInfo "Rows:       users=$users tracks=$tracks audioAssets=$assets"

    Write-MusicHubStep 'Next steps'
    if ($isLive) {
        Write-MusicHubInfo "1. Start the hosts again:"
        if ($stoppedHosts -contains 'service:MusicHub.Api') { Write-MusicHubInfo '     Start-Service MusicHub.Api; Start-Service MusicHub.Worker' }
        else { Write-MusicHubInfo '     .\deploy\scripts\start-dev.ps1' }
        Write-MusicHubInfo "2. Verify:   .\deploy\scripts\verify.ps1"
        Write-MusicHubInfo "3. Confirm the restored migration ($restoredMigration) matches the deployed binaries before serving traffic."
    }
    else {
        Write-MusicHubInfo "1. Validate data in '$TargetDatabase' (application binaries and migrations are unchanged)."
        Write-MusicHubInfo "2. To promote it, stop API/Worker, back up the live database, then set MUSICHUB_CONNECTION_STRING to:"
        Write-MusicHubInfo "     Host=$($config.Host);Port=$($config.Port);Database=$TargetDatabase;Username=$($config.Username);Password=<from your secret store>"
        Write-MusicHubInfo "   or restore over the live database using -AllowLiveRestore -ConfirmDestructiveLiveRestore."
        Write-MusicHubInfo "3. Verify:   .\deploy\scripts\verify.ps1 -BaseUrl $($config.ApiBaseUrl)"
        Write-MusicHubInfo "4. Drop the rehearsal database when finished:"
        Write-MusicHubInfo ("     tools\pgsql\bin\dropdb.exe -h {0} -p {1} -U {2} {3}" -f $config.Host, $config.Port, $config.Username, $TargetDatabase)
    }
    if (-not $KeepServicesStopped -and $stoppedHosts.Count -gt 0) {
        Write-MusicHubInfo 'Restart the hosts before accepting traffic.'
    }
    Write-Host ''
    Write-Host 'Restore completed and verified.' -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-MusicHubError $_.Exception.Message
    if ($config) {
        Write-MusicHubWarning 'Restore did not complete. The target database may be incomplete; inspect it before use.'
    }
}
finally {
    if ($config) { [Environment]::SetEnvironmentVariable('PGPASSWORD', $null, 'Process') }
}

exit $exitCode
