<#
.SYNOPSIS
    MusicHub Enterprise server install: create the database role with a generated password and
    store it encrypted for the application.

.DESCRIPTION
    Implements the install-time database credential policy:

      1. PostgreSQL runs from the portable distribution (or an existing cluster).
      2. If the cluster does not exist yet, it is initialised by initdb with a freshly generated
         256-bit random password, using a transient password file that is deleted immediately.
      3. If the cluster already exists, the role password is rotated with the same generated
         password through an administrator connection read from the environment.
      4. The application copy of the connection string is written to
         <data root>/keys/db-credential.protected, encrypted with the application's
         data-protection keyring (DPAPI-protected for the installing account on Windows). The
         file is readable only by that account.
      5. The plaintext password is never written to disk anywhere else, never printed, never
         placed on a command line and never stored in the repository.

    The API and the Worker read the credential from that file; they do not need the password in
    their environment. MUSICHUB_CONNECTION_STRING still works as an explicit override.

.PARAMETER DataRoot
    Data root that will hold keys/db-credential.protected. Default: <repo>\storage.

.PARAMETER Database
    Database name. Default: musichub.

.PARAMETER Username
    Application role. Default: musichub.

.PARAMETER Superuser
    Superuser that initdb creates on a fresh cluster. Default: postgres. It is separate from the
    application role so the API never connects as a superuser.

.PARAMETER Port
    PostgreSQL port. Default: 5433 (the developer stack port). The installer checks that it is free
    before initialising a cluster, because a cluster that cannot bind its port fails later with a
    message that does not mention the real cause.

.PARAMETER ApiPort
    Port the API will listen on. Default: the port recorded by an earlier install step, else the
    first free port from 5080 upward. The operator is asked to confirm it.

.PARAMETER LanAddress
    Address other devices should use. Default: detected from this host's adapters.

.PARAMETER NonInteractive
    Accept the detected address and the first free port without asking.

.PARAMETER AdminConnection
    Administrator connection string used to rotate the password of an existing role. When
    omitted, the local superuser is used through the cluster's local trust authentication.

.PARAMETER SkipDatabase
    Do not touch PostgreSQL; only (re)write the credential file from a connection string that
    already exists. Requires -AdminConnection or an existing credential.

.EXAMPLE
    .\deploy\scripts\install-server.ps1

.EXAMPLE
    .\deploy\scripts\install-server.ps1 -Port 5432 -Database musichub -Username musichub

.EXAMPLE
    .\deploy\scripts\install-server.ps1 -NonInteractive -ApiPort 8080
#>
[CmdletBinding()]
param(
    [string]$DataRoot = '',
    [string]$Database = 'musichub',
    [string]$Username = 'musichub',
    [string]$Superuser = 'postgres',
    [int]$Port = 5433,
    [int]$ApiPort = 0,
    [string]$LanAddress = '',
    [switch]$NonInteractive,
    [string]$AdminConnection = '',
    [switch]$SkipDatabase
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-MusicHubRepoRoot
if (-not $DataRoot) { $DataRoot = Join-Path $repoRoot 'storage' }
$DataRoot = [System.IO.Path]::GetFullPath($DataRoot)
$pgDataDir = Join-Path $DataRoot 'postgres-data'
$pgBin = Join-Path $repoRoot 'tools\pgsql\bin'
$logDir = Join-Path $DataRoot 'logs'
$runDir = Join-Path $DataRoot 'run'

$exitCode = 0
try {
    Write-Host ''
    Write-Host 'MusicHub Enterprise - server install' -ForegroundColor Cyan

    Write-MusicHubStep '1/5 Requirements'
    $dotnet = Initialize-MusicHubDotnet
    $installDll = Join-Path $repoRoot 'src\server\MusicHub.Install\bin\Debug\net10.0\MusicHub.Install.dll'
    if (-not (Test-Path -LiteralPath $installDll)) {
        Write-MusicHubInfo 'Building MusicHub.Install'
        $buildOutput = & $dotnet build (Join-Path $repoRoot 'src\server\MusicHub.Install\MusicHub.Install.csproj') --nologo -v minimal 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "MusicHub.Install failed to build.`n$((@($buildOutput) | ForEach-Object { "$_" }) -join "`n")"
        }
    }
    Write-MusicHubInfo "Installer: $installDll"
    New-MusicHubDirectory -Path $DataRoot | Out-Null
    New-MusicHubDirectory -Path $logDir | Out-Null
    New-MusicHubDirectory -Path $runDir | Out-Null

    # Ask for the endpoint here, before PostgreSQL is touched: the operator learns the address other
    # devices will use and picks the API port up front, and start-dev reuses the same answer.
    $endpoint = Resolve-MusicHubEndpoint `
        -RepoRoot $repoRoot `
        -Port $ApiPort `
        -LanAddress $LanAddress `
        -PreferredPort 5080 `
        -Mode 'windows' `
        -NonInteractive:$NonInteractive `
        -Persist
    $ApiPort = $endpoint.Port
    Write-MusicHubInfo "LAN address for clients: $($endpoint.LanAddress)"
    Write-MusicHubInfo "API listening port: $ApiPort"

    # PostgreSQL's port is a separate socket. Report a conflict now instead of letting initdb build a
    # cluster that then fails to start.
    if (-not $SkipDatabase -and -not (Test-MusicHubPortAvailable -Port $Port)) {
        Write-MusicHubWarning "Port $Port is already in use; PostgreSQL will not be able to bind it."
    }

    if ($SkipDatabase) {
        Write-MusicHubStep '2/5 Database (skipped)'
        Write-MusicHubWarning '-SkipDatabase: PostgreSQL is left untouched; the credential file is written from an existing connection string.'
    }
    else {
        Write-MusicHubStep '2/5 PostgreSQL'
        if (-not (Test-Path -LiteralPath (Join-Path $pgBin 'pg_ctl.exe'))) {
            throw "PostgreSQL 17 was not found under tools\pgsql. Install it there or start an external server and re-run with -AdminConnection."
        }
        $postmasterPid = Join-Path $pgDataDir 'postmaster.pid'
        if (Test-Path -LiteralPath $postmasterPid) {
            $stalePid = 0
            $firstLine = (Get-Content -LiteralPath $postmasterPid -TotalCount 1).Trim()
            if ([int]::TryParse($firstLine, [ref]$stalePid) -and -not (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) {
                Remove-Item -LiteralPath $postmasterPid -Force
            }
        }

        $freshCluster = -not (Test-Path -LiteralPath (Join-Path $pgDataDir 'PG_VERSION'))
        $pwFile = Join-Path $runDir 'install-db.pw'
        try {
            if ($freshCluster) {
                Write-MusicHubInfo 'No cluster found: generating a random superuser password and initialising PostgreSQL.'
                New-MusicHubDirectory -Path $pgDataDir | Out-Null
                $existing = Get-ChildItem -LiteralPath $pgDataDir -Force -ErrorAction SilentlyContinue
                if ($existing) { throw "The data directory $pgDataDir is not empty but has no PG_VERSION. Refusing to initialise over unknown files." }

                $seedOutput = & $dotnet $installDll create-credential --data-root $DataRoot --host 127.0.0.1 --port $Port --database $Database --username $Username --seed-only --pwfile-out $pwFile --admin-username $Superuser 2>&1
                $seedExit = $LASTEXITCODE
                if ($seedExit -ne 0) {
                    throw "create-credential --seed-only failed (exit $seedExit). $((@($seedOutput) | ForEach-Object { "$_" }) -join ' ')"
                }
                if (-not (Test-Path -LiteralPath $pwFile)) { throw "The installer did not produce $pwFile." }

                $initdb = Join-Path $pgBin 'initdb.exe'
                # initdb spawns children that inherit its stdout/stderr handles, so invoking it
                # directly blocks the pipeline. Redirect to files and wait on the process.
                $initOut = Join-Path $logDir 'initdb.out.log'
                $initErr = Join-Path $logDir 'initdb.err.log'
                $initArgs = '-D "' + $pgDataDir + '" -U "' + $Superuser + '" --pwfile="' + $pwFile + '" --auth-local=trust --auth-host=scram-sha-256 -E UTF8 --locale=C'
                $initProcess = Start-Process -FilePath $initdb -ArgumentList $initArgs `
                    -RedirectStandardOutput $initOut -RedirectStandardError $initErr -WindowStyle Hidden -PassThru
                $initProcess.WaitForExit(300000) | Out-Null
                $initProcess.Refresh()
                if (-not (Test-Path -LiteralPath (Join-Path $pgDataDir 'PG_VERSION'))) {
                    $detail = ''
                    foreach ($p in @($initErr, $initOut)) {
                        if (Test-Path -LiteralPath $p) { $detail += (Get-Content -LiteralPath $p -Raw) }
                    }
                    throw "initdb failed (exit $($initProcess.ExitCode)). $detail"
                }
                Write-MusicHubInfo "PostgreSQL cluster initialised at $pgDataDir with superuser '$Superuser'."
            }
            else {
                Write-MusicHubInfo "Existing cluster at ${pgDataDir}: no credential file rewrite of the superuser password."
            }
        }
        finally {
            if (Test-Path -LiteralPath $pwFile) {
                Remove-Item -LiteralPath $pwFile -Force -ErrorAction SilentlyContinue
                Write-MusicHubInfo 'Transient initdb password file removed.'
            }
        }

        Write-MusicHubInfo 'Starting PostgreSQL'
        # pg_ctl hands its stdout/stderr to the postmaster, which keeps them open; redirecting to
        # files and waiting on the pg_ctl process avoids blocking the pipeline indefinitely.
        # Every path is quoted because Start-Process joins the list with spaces.
        $pgStartOut = Join-Path $logDir 'pg-start.out.log'
        $pgStartErr = Join-Path $logDir 'pg-start.err.log'
        $pgArgs = '-D "' + $pgDataDir + '" -l "' + (Join-Path $logDir 'postgres.log') + '" -o "-p ' + $Port + ' -c listen_addresses=127.0.0.1" -w -t 90 start'
        $pgCtl = Start-Process -FilePath (Join-Path $pgBin 'pg_ctl.exe') -ArgumentList $pgArgs `
            -RedirectStandardOutput $pgStartOut -RedirectStandardError $pgStartErr -WindowStyle Hidden -PassThru
        $pgCtl.WaitForExit(120000) | Out-Null
        # Start-Process -PassThru does not refresh ExitCode, so re-read it before judging the result,
        # and confirm the outcome on the port either way.
        $pgCtl.Refresh()
        $started = Test-MusicHubTcpPort -Port $Port
        if (-not $started) {
            $detail = if (Test-Path -LiteralPath $pgStartErr) { (Get-Content -LiteralPath $pgStartErr -Raw) } else { '' }
            throw "pg_ctl start failed (exit $($pgCtl.ExitCode)). $detail See $(Join-Path $logDir 'postgres.log')."
        }
        Write-MusicHubInfo "PostgreSQL is listening on 127.0.0.1:$Port."

        # Now that the cluster is running, create (or rotate) the unprivileged application role
        # and give it the database plus the public schema. On a fresh install this authenticates
        # with the encrypted superuser credential, so the superuser password is never passed on a
        # command line or through this process's environment.
        Write-MusicHubInfo "Creating or rotating the application role '$Username'."
        if ($AdminConnection) {
            $env:MUSICHUB_PG_ADMIN_CONNECTION = $AdminConnection
        }
        try {
            if ($AdminConnection) {
                $roleOutput = & $dotnet $installDll create-credential --data-root $DataRoot --host 127.0.0.1 --port $Port --database $Database --username $Username --create-role --create-database 2>&1
            }
            else {
                $roleOutput = & $dotnet $installDll create-credential --data-root $DataRoot --host 127.0.0.1 --port $Port --database $Database --username $Username --create-role --create-database --admin-from-file 2>&1
            }
            $roleExit = $LASTEXITCODE
        }
        finally {
            [Environment]::SetEnvironmentVariable('MUSICHUB_PG_ADMIN_CONNECTION', $null, 'Process')
        }
        (@($roleOutput) | ForEach-Object { "$_" }) | Where-Object { $_ -match 'Wrote|Rotated|Created|Granted|ERROR' } | ForEach-Object { Write-MusicHubInfo "$_" }
        if ($roleExit -ne 0) {
            throw "create-credential failed (exit $roleExit). $((@($roleOutput) | ForEach-Object { "$_" }) -join ' ')"
        }
    }

    Write-MusicHubStep '3/5 Credential'
    $credPath = Join-Path $DataRoot 'keys\db-credential.protected'
    if (-not (Test-Path -LiteralPath $credPath)) { throw "The credential file was not written at $credPath." }
    $acl = (& icacls.exe $credPath 2>&1) -join ' '
    Write-MusicHubInfo "Credential: $credPath"
    Write-MusicHubInfo 'Only the installing account can read it, and its content is encrypted for that account.'
    if ($acl -match 'BUILTIN\\Users|Everyone|Authenticated Users') {
        Write-MusicHubWarning 'The credential file ACL still grants a broad group. Re-run as an administrator or tighten the ACL manually.'
    }

    Write-MusicHubStep '4/5 Verification'
    $verifyOutput = & $dotnet $installDll verify-credential --data-root $DataRoot --probe 2>&1
    $verifyExit = $LASTEXITCODE
    (@($verifyOutput) | ForEach-Object { "$_" }) | ForEach-Object { Write-MusicHubInfo $_ }
    if ($verifyExit -ne 0) { throw "The stored credential could not be verified (exit $verifyExit)." }

    Write-MusicHubStep '5/5 Next steps'
    Write-MusicHubInfo 'The API and Worker now read the credential from the data root; they need no password in their environment.'
    Write-MusicHubInfo "Start the stack:  .\deploy\scripts\start-dev.ps1 -BindLan"
    Write-MusicHubInfo "Verify it:        .\deploy\scripts\verify.ps1"
    Write-MusicHubInfo 'The Worker the stack starts also runs the Telegram bot (Telegram__WorkerEnabled defaults to true); the bot polls while a valid token is configured.'
    Write-MusicHubInfo 'Provision the bot token and the admin Telegram user id before expecting the bot to answer (see docs/INSTALL_AND_RUN.md, section Server and Telegram bot).'

    Write-MusicHubAccessSummary -Port $ApiPort -LanAddress $endpoint.LanAddress -DashboardPath '/index.html'
    Write-Host 'Server install completed.' -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-MusicHubError $_.Exception.Message
}
finally {
    [Environment]::SetEnvironmentVariable('MUSICHUB_PG_ADMIN_CONNECTION', $null, 'Process')
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $null, 'Process')
}

exit $exitCode
