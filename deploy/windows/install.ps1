<#
.SYNOPSIS
    Install MusicHub Enterprise server on Windows from this package.

.DESCRIPTION
    One-command install for the self-contained Windows server package. No .NET SDK, no Docker and no
    network connection are required: the package carries a private .NET runtime, portable PostgreSQL,
    ffmpeg/ffprobe and the published hosts.

    What it does, in order:

      1. Verifies the package is intact (manifest, runtime, PostgreSQL, ffmpeg, hosts, dashboard).
      2. Puts the private runtime first on PATH and sets DOTNET_ROOT, so nothing depends on a
         system-wide .NET install and a later SDK removal cannot break the server.
      3. Runs deploy\scripts\install-server.ps1, which detects this machine's LAN address, asks which
         port the API should listen on, initialises the PostgreSQL cluster and writes the encrypted
         database credential.
      4. Writes storage\dev.secrets.ps1 with freshly generated JWT and bootstrap secrets, Production
         as the environment, and Database:MigrateOnStartup enabled so the API applies migrations on
         first start (a package install has no SDK, so `dotnet ef` is not available).
      5. Runs deploy\scripts\start-dev.ps1 -BindLan -SkipBuild to start PostgreSQL, the API and the
         Worker (which starts the Telegram bot).
      6. Verifies the stack over HTTP before reporting success.
      7. Registers the "MusicHub Server" Scheduled Task so the stack comes back after a reboot.

    The scripts it calls are the same ones the repository ships and the test suite exercises; this file
    orchestrates them for an end user rather than reimplementing the install.

.PARAMETER ApiPort
    Port for the API. When omitted, the installer proposes the first free port at or above 5080.

.PARAMETER LanAddress
    Address other devices should use. When omitted, it is detected from this host's adapters.

.PARAMETER NonInteractive
    Accept the detected address and the proposed port without prompting.

.PARAMETER Start
    Start an already-installed stack and exit.

.PARAMETER Stop
    Stop the running stack and exit.

.PARAMETER Status
    Report what is installed and what is running, then exit.

.PARAMETER Uninstall
    Remove the Scheduled Task and stop the stack. Data under storage\ is preserved unless -PurgeData
    is also supplied.

.PARAMETER PurgeData
    With -Uninstall, also delete storage\ (database cluster, uploaded music, keys, logs). This is
    irreversible and requires an explicit confirmation.

.PARAMETER NoScheduledTask
    Do not register the logon task (useful for a test install that should not survive a reboot).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -NonInteractive -ApiPort 8080

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Status
#>
[CmdletBinding()]
param(
    [int]$ApiPort = 0,
    [string]$LanAddress = '',
    [switch]$NonInteractive,
    [switch]$Start,
    [switch]$Stop,
    [switch]$Status,
    [switch]$Uninstall,
    [switch]$PurgeData,
    [switch]$NoScheduledTask
)

$ErrorActionPreference = 'Stop'

# The install root is the directory that contains this script's parent (deploy\windows\install.ps1).
$installRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$taskName = 'MusicHub Server'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}
function Write-Info([string]$Text) { Write-Host "    $Text" }
function Write-Warn([string]$Text) { Write-Host "WARN: $Text" -ForegroundColor Yellow }
function Write-Err([string]$Text) { Write-Host "ERROR: $Text" -ForegroundColor Red }

function Get-PackageManifest {
    $path = Join-Path $installRoot 'package.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Use-PrivateRuntime {
    <#
        Put the packaged runtime on PATH and point DOTNET_ROOT at it. This is what lets the shipped
        scripts find a `dotnet` without a system-wide SDK: Resolve-MusicHubDotnet checks DOTNET_ROOT
        first, so the private runtime wins even when the machine has no .NET at all.
    #>
    $runtime = Join-Path $installRoot 'runtime'
    if (-not (Test-Path -LiteralPath (Join-Path $runtime 'dotnet.exe'))) {
        throw "The packaged .NET runtime is missing at $runtime. The package is incomplete; re-extract it."
    }
    $env:DOTNET_ROOT = $runtime
    if (($env:PATH -split ';') -notcontains $runtime) { $env:PATH = "$runtime;$env:PATH" }
    return (Join-Path $runtime 'dotnet.exe')
}

function Test-PackagePayload {
    <# Fail before touching anything when the extracted package is incomplete. #>
    $required = @(
        'runtime\dotnet.exe',
        'runtime\host\fxr',
        'tools\pgsql\bin\pg_ctl.exe',
        'tools\pgsql\bin\initdb.exe',
        'tools\pgsql\bin\psql.exe',
        'tools\ffmpeg\bin\ffmpeg.exe',
        'tools\ffmpeg\bin\ffprobe.exe',
        'src\server\MusicHub.Api\bin\Debug\net10.0\MusicHub.Api.dll',
        'src\server\MusicHub.Worker\bin\Debug\net10.0\MusicHub.Worker.dll',
        'src\server\MusicHub.Install\bin\Debug\net10.0\MusicHub.Install.dll',
        # The API's content root is its working directory (the project folder), and that is where
        # ASP.NET Core looks for wwwroot. Checking the publish folder here would pass while the
        # dashboard 404s at runtime.
        'src\server\MusicHub.Api\wwwroot\index.html',
        'src\server\MusicHub.Api\appsettings.json',
        'deploy\scripts\install-server.ps1',
        'deploy\scripts\start-dev.ps1',
        'deploy\scripts\stop-dev.ps1',
        'deploy\scripts\common.ps1',
        'deploy\windows\supervisor.ps1'
    )
    $missing = @()
    foreach ($item in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $installRoot $item))) { $missing += $item }
    }
    if ($missing.Count -gt 0) {
        throw ("The package is incomplete. Missing:`n  " + ($missing -join "`n  "))
    }
    Write-Info "Package payload verified ($($required.Count) required entries)."
}

function Get-Endpoint {
    $path = Join-Path $installRoot 'storage\install.endpoint.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Write-ServerSecrets {
    <#
        Generate storage\dev.secrets.ps1 for a package install.

        This runs before start-dev.ps1 so that script finds the file already present and does not
        regenerate it (it only writes when the file is absent or -RegenerateSecrets is passed). The
        values differ from the developer defaults on purpose:

          * ASPNETCORE_ENVIRONMENT is Production, not Development, so the API applies its production
            security headers (CSP, HSTS, no-scope-validation) instead of the developer relaxations.
          * Database__MigrateOnStartup is true, because a package install has no SDK and therefore no
            `dotnet ef`; the API is what applies migrations.
    #>
    param([Parameter(Mandatory)][int]$Port, [Parameter(Mandatory)][string]$PublicHost)

    $secretsPath = Join-Path $installRoot 'storage\dev.secrets.ps1'
    if (Test-Path -LiteralPath $secretsPath) {
        Write-Info 'storage\dev.secrets.ps1 already exists; keeping it (delete it to regenerate).'
        return $secretsPath
    }

    function New-Secret([int]$Bytes) {
        $buffer = New-Object byte[] $Bytes
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
        return ([Convert]::ToBase64String($buffer) -replace '\+', '-' -replace '/', '_').TrimEnd('=')
    }
    $escape = { param($v) ("$v") -replace "'", "''" }

    $jwt = New-Secret 64
    $bootstrap = New-Secret 32
    $storageRoot = Join-Path $installRoot 'storage'
    $ffBin = Join-Path $installRoot 'tools\ffmpeg\bin'
    $pgBin = Join-Path $installRoot 'tools\pgsql\bin'

    $lines = @(
        '# MusicHub Enterprise server secrets.'
        ('# Generated by deploy\windows\install.ps1 at ' + [DateTimeOffset]::UtcNow.ToString('o') + '.')
        '# This file is gitignored. Never commit it, never paste its contents into logs, tickets, reports or chat.'
        '# The database password is NOT here: it lives encrypted in storage\keys\db-credential.protected.'
        ''
        ("`$env:MUSICHUB_JWT_SECRET = '" + (& $escape $jwt) + "'")
        ("`$env:MUSICHUB_BOOTSTRAP_TOKEN = '" + (& $escape $bootstrap) + "'")
        ("`$env:MUSICHUB_DATA_ROOT = '" + (& $escape $storageRoot) + "'")
        ("`$env:MUSICHUB_PG_DATA_DIR = '" + (& $escape (Join-Path $storageRoot 'postgres-data')) + "'")
        ("`$env:MUSICHUB_PUBLIC_URL = 'http://" + $PublicHost + ':' + $Port + "'")
        "# Production applies the security headers (CSP, HSTS). A package install is a real deployment,"
        "# not a development host, so it must not run with the developer relaxations."
        ("`$env:ASPNETCORE_ENVIRONMENT = 'Production'")
        # Bind every interface, not the LAN address alone. Kestrel treats a hostname in
        # ASPNETCORE_URLS as an exact bind address, so 'http://192.168.x.y:PORT' refuses 127.0.0.1:
        # the supervisor and -Status would both report the server dead while it was serving the LAN.
        # MUSICHUB_PUBLIC_URL still carries the LAN address, because that is what clients must dial.
        ("`$env:ASPNETCORE_URLS = 'http://0.0.0.0:" + $Port + "'")
        '# No .NET SDK ships with this package, so `dotnet ef` cannot run. The API applies pending'
        '# migrations itself on startup, which is what makes a first start work offline.'
        ("`$env:Database__MigrateOnStartup = 'true'")
        '# Version 1 contract: installing the stack starts the Telegram bot with it.'
        ("`$env:Telegram__WorkerEnabled = 'true'")
        ''
        '# Toolchain'
        ('$env:PATH = "$env:PATH;' + $pgBin + ';' + $ffBin + '"')
        ("`$env:MUSICHUB_FFPROBE_PATH = '" + (& $escape (Join-Path $ffBin 'ffprobe.exe')) + "'")
        ("`$env:MUSICHUB_FFMPEG_PATH = '" + (& $escape (Join-Path $ffBin 'ffmpeg.exe')) + "'")
        ''
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $secretsPath) -Force | Out-Null
    Set-Content -LiteralPath $secretsPath -Value $lines -Encoding UTF8
    try { & icacls.exe $secretsPath /inheritance:r /grant:r "$($env:USERNAME):(R,W)" 2>&1 | Out-Null }
    catch { Write-Warn "Could not tighten the ACL on $secretsPath; restrict it to your account manually." }
    Write-Info "Secrets written to $secretsPath (not printed)."
    return $secretsPath
}

function Register-ServerTask {
    <# Register the logon task that keeps the stack alive across reboots. #>
    $supervisor = Join-Path $installRoot 'deploy\windows\supervisor.ps1'
    if (-not (Test-Path -LiteralPath $supervisor)) { throw "Missing supervisor: $supervisor" }

    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Info "Replacing the existing '$taskName' task."
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    }

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $supervisor + '"') `
        -WorkingDirectory $installRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    # A logon task that must survive a crash of the supervisor itself.
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
        -Settings $settings -Principal $principal `
        -Description 'Keeps the MusicHub Enterprise server (PostgreSQL, API, Worker) running.' | Out-Null
    Write-Info "Scheduled Task '$taskName' registered: it starts the stack at logon."
}

function Get-StackState {
    $endpoint = Get-Endpoint
    $port = if ($endpoint -and $endpoint.Port) { [int]$endpoint.Port } else { 5080 }
    $lan = if ($endpoint -and $endpoint.LanAddress) { $endpoint.LanAddress } else { '127.0.0.1' }
    $apiListening = [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    $pgListening = [bool](Get-NetTCPConnection -LocalPort 5433 -State Listen -ErrorAction SilentlyContinue)
    $workerRunning = [bool](Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like '*MusicHub.Worker.dll*' })
    return [pscustomobject]@{
        Port = $port; LanAddress = $lan
        ApiListening = $apiListening; PostgresListening = $pgListening; WorkerRunning = $workerRunning
    }
}

function Wait-ForApi([int]$Port, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/healthz" -UseBasicParsing -TimeoutSec 4 -ErrorAction Stop
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

Write-Host ''
Write-Host 'MusicHub Enterprise - Windows server install' -ForegroundColor Cyan

# -------------------------------------------------------------------------------------------
# Read-only verbs
# -------------------------------------------------------------------------------------------
if ($Status) {
    $manifest = Get-PackageManifest
    $state = Get-StackState
    Write-Host ''
    Write-Host "Install root : $installRoot"
    if ($manifest) { Write-Host "Version      : $($manifest.version) ($($manifest.platform))" }
    Write-Host "API port     : $($state.Port)"
    Write-Host "LAN address  : $($state.LanAddress)"
    Write-Host ("PostgreSQL   : " + $(if ($state.PostgresListening) { 'listening on 5433' } else { 'not running' }))
    Write-Host ("API          : " + $(if ($state.ApiListening) { "listening on $($state.Port)" } else { 'not running' }))
    Write-Host ("Worker       : " + $(if ($state.WorkerRunning) { 'running' } else { 'not running' }))
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Write-Host ("Logon task   : " + $(if ($task) { $task.State } else { 'not registered' }))
    Write-Host ''
    Write-Host "Dashboard    : http://$($state.LanAddress):$($state.Port)/index.html"
    exit 0
}

if ($Stop) {
    Write-Step 'Stopping the stack'
    & (Join-Path $installRoot 'deploy\scripts\stop-dev.ps1') -StopDatabase
    exit $LASTEXITCODE
}

if ($Start) {
    Use-PrivateRuntime | Out-Null
    Write-Step 'Starting the stack'
    # No -SkipDatabase here: this verb must also bring PostgreSQL back up if it is down. start-dev
    # reuses the existing verified credential instead of re-provisioning it.
    & (Join-Path $installRoot 'deploy\scripts\start-dev.ps1') -BindLan -SkipBuild -NonInteractive
    exit $LASTEXITCODE
}

if ($Uninstall) {
    Write-Step 'Uninstalling'
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Info "Removed the '$taskName' task."
    }
    else { Write-Info 'No logon task was registered.' }

    $stopScript = Join-Path $installRoot 'deploy\scripts\stop-dev.ps1'
    if (Test-Path -LiteralPath $stopScript) {
        & $stopScript -StopDatabase
    }

    if ($PurgeData) {
        $storage = Join-Path $installRoot 'storage'
        if (Test-Path -LiteralPath $storage) {
            Write-Warn 'Purging storage deletes the database cluster, uploaded music, keys and logs.'
            if (-not $NonInteractive) {
                $answer = Read-Host 'Type DELETE to confirm permanent data loss'
                if ($answer -ne 'DELETE') { Write-Info 'Purge cancelled; data preserved.'; exit 1 }
            }
            Remove-Item -LiteralPath $storage -Recurse -Force
            Write-Info 'storage\ removed.'
        }
    }
    else {
        Write-Info 'Data preserved under storage\. Re-run with -Uninstall -PurgeData to delete it.'
    }
    Write-Host 'Uninstall complete.' -ForegroundColor Green
    exit 0
}

# -------------------------------------------------------------------------------------------
# Full install
# -------------------------------------------------------------------------------------------
try {
    Write-Step '1/7 Verify the package'
    $manifest = Get-PackageManifest
    if ($manifest) { Write-Info "Package: $($manifest.product) $($manifest.version) ($($manifest.platform))" }
    Test-PackagePayload

    Write-Step '2/7 Private .NET runtime'
    $dotnetExe = Use-PrivateRuntime
    $runtimeVersion = (& $dotnetExe --list-runtimes 2>&1 | Select-Object -First 2) -join '; '
    Write-Info "DOTNET_ROOT = $(Join-Path $installRoot 'runtime')"
    Write-Info $runtimeVersion

    # install-server.ps1 dot-sources common.ps1, which resolves every path from the repository root.
    # In this package that root is the install directory, so the shipped scripts work unchanged.
    Write-Step '3/7 Database and endpoint'
    $installServer = Join-Path $installRoot 'deploy\scripts\install-server.ps1'
    $serverArgs = @{}
    if ($ApiPort -gt 0) { $serverArgs['ApiPort'] = $ApiPort }
    if ($LanAddress) { $serverArgs['LanAddress'] = $LanAddress }
    if ($NonInteractive) { $serverArgs['NonInteractive'] = $true }
    & $installServer @serverArgs
    if ($LASTEXITCODE -ne 0) { throw "install-server.ps1 failed (exit $LASTEXITCODE). See the messages above." }

    $endpoint = Get-Endpoint
    if (-not $endpoint) { throw 'The installer did not record storage\install.endpoint.json.' }
    Write-Info "Chosen endpoint: $($endpoint.LanAddress):$($endpoint.Port)"

    Write-Step '4/7 Secrets'
    Write-ServerSecrets -Port ([int]$endpoint.Port) -PublicHost ([string]$endpoint.LanAddress) | Out-Null

    Write-Step '5/7 Start the stack'
    $startDev = Join-Path $installRoot 'deploy\scripts\start-dev.ps1'
    # -SkipDatabase: install-server.ps1 has already initialised the cluster, created the application
    # role and written the encrypted credential. start-dev.ps1 would otherwise try to provision the
    # credential a second time, which is exactly the double-install path that used to fail.
    & $startDev -BindLan -SkipBuild -SkipDatabase -NonInteractive -Force
    if ($LASTEXITCODE -ne 0) { throw "start-dev.ps1 failed (exit $LASTEXITCODE). See the messages above." }

    Write-Step '6/7 Verify'
    $port = [int]$endpoint.Port
    if (-not (Wait-ForApi -Port $port -Seconds 120)) {
        throw "The API did not answer /healthz on port $port within 120 seconds. Check storage\logs\api.err.log."
    }
    Write-Info "API answered http://127.0.0.1:$port/healthz"

    # The dashboard is part of the contract: a package install must serve the UI, not just the API.
    try {
        $index = Invoke-WebRequest -Uri "http://127.0.0.1:$port/index.html" -UseBasicParsing -TimeoutSec 8 -ErrorAction Stop
        Write-Info "Dashboard served: /index.html ($($index.RawContentLength) bytes, HTTP $($index.StatusCode))"
    }
    catch {
        throw "The dashboard did not answer /index.html: $($_.Exception.Message). The package is built wrongly: the API serves wwwroot from its content root, which is its project directory."
    }

    # /api/health is the strongest evidence available here: the API performs a real query against
    # PostgreSQL and a real enumeration of the music root, so database=true proves the encrypted
    # credential works end to end.
    #
    # deploy/scripts/verify.ps1 is deliberately NOT used for the pass/fail decision. It inspects the
    # database directly with psql and needs the plaintext role password, which a package install does
    # not have: the password exists only inside keys\db-credential.protected. On this install its
    # database.migrations and worker.heartbeat checks would therefore FAIL on a healthy server. Run
    # it only with MUSICHUB_CONNECTION_STRING set (see docs/INSTALL_AND_RUN.md).
    $health = $null
    try { $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/health" -TimeoutSec 10 -ErrorAction Stop }
    catch { throw "Could not read /api/health: $($_.Exception.Message)" }
    Write-Info "Health: status=$($health.status) database=$($health.checks.database) storageReadable=$($health.checks.storageReadable)"
    if ($health.checks.database -ne $true) {
        throw 'The API cannot reach PostgreSQL (checks.database=false). See storage\logs\api.err.log.'
    }
    if ($health.checks.storageReadable -ne $true) {
        throw "The API cannot read its music root (checks.storageReadable=false). See storage\logs\api.err.log."
    }

    # Migrations are applied by the API on startup (Database:MigrateOnStartup). The setup endpoint
    # queries the Users table, so a 200 there proves the schema exists.
    try {
        $setup = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/setup/status" -TimeoutSec 10 -ErrorAction Stop
        Write-Info "Schema reachable: /api/setup/status initialized=$($setup.initialized) bootstrapTokenConfigured=$($setup.bootstrapTokenConfigured)"
    }
    catch {
        throw "The database schema is not usable yet: /api/setup/status failed ($($_.Exception.Message)). Migrations may still be running; check storage\logs\api.log."
    }

    # The Worker is what runs the Telegram bot, so its absence is a real failure, not a warning.
    $workerLog = Join-Path $installRoot 'storage\logs\worker.log'
    $workerDeadline = (Get-Date).AddSeconds(60)
    $workerUp = $false
    while ((Get-Date) -lt $workerDeadline) {
        $state = Get-StackState
        if ($state.WorkerRunning) { $workerUp = $true; break }
        Start-Sleep -Seconds 3
    }
    if (-not $workerUp) {
        throw "MusicHub.Worker is not running; the Telegram bot will not poll. See storage\logs\worker.err.log."
    }
    Write-Info 'Worker is running (the Telegram bot starts with it).'

    Write-Step '7/7 Autostart'
    if ($NoScheduledTask) {
        Write-Info 'Skipped (-NoScheduledTask). The stack will not restart after a reboot.'
    }
    else {
        Register-ServerTask
    }

    # The firewall rule is what lets a phone or another PC reach the API. It needs elevation, so a
    # failure here is reported rather than fatal: the stack is already running on this machine.
    $ruleName = "MusicHub Server $port"
    $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if (-not $existingRule) {
        try {
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP `
                -LocalPort $port -RemoteAddress LocalSubnet -Action Allow -ErrorAction Stop | Out-Null
            Write-Info "Firewall rule '$ruleName' created (TCP $port from LocalSubnet)."
        }
        catch {
            Write-Warn "Could not create the firewall rule (needs an elevated shell): $($_.Exception.Message)"
            Write-Warn "Other devices will not reach the server until you allow TCP $port from your LAN."
        }
    }
    else { Write-Info "Firewall rule '$ruleName' already exists." }

    Write-Host ''
    Write-Host 'Server install completed.' -ForegroundColor Green
    Write-Host ''
    Write-Host "  Dashboard (this machine) : http://127.0.0.1:$port/index.html"
    Write-Host "  Dashboard (other devices): http://$($endpoint.LanAddress):$port/index.html"
    Write-Host "  Server address for the Android and Windows apps: http://$($endpoint.LanAddress):$port"
    Write-Host ''
    Write-Host '  Next: open the dashboard and complete the first-run setup to create the administrator.'
    Write-Host '  Then provision the Telegram bot token and admin id in the dashboard (Admin -> Telegram).'
    Write-Host ''
    exit 0
}
catch {
    Write-Err $_.Exception.Message
    Write-Host ''
    Write-Host 'Install did not complete. Logs are under storage\logs.' -ForegroundColor Red
    exit 1
}
