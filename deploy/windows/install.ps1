<#
.SYNOPSIS
    Install MusicHub Enterprise server on Windows from this package.

.DESCRIPTION
    One-command install for the self-contained Server for Windows package. No .NET SDK, no Docker and no
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
    [int]$DashboardPort = 0,
    [int]$PlayerPort = 0,
    [int]$ApiPort = 0,
    [string]$LanAddress = '',
    [switch]$NonInteractive,
    [switch]$Start,
    [switch]$Launch,
    [switch]$Stop,
    [switch]$Status,
    [switch]$Credentials,
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

function Get-BootstrapToken {
    $secretsPath = Join-Path $installRoot 'storage\dev.secrets.ps1'
    if (-not (Test-Path -LiteralPath $secretsPath)) { return $null }
    $content = Get-Content -LiteralPath $secretsPath -Raw -ErrorAction SilentlyContinue
    if ($content -match '\$env:MUSICHUB_BOOTSTRAP_TOKEN\s*=\s*[''"]([^''"]+)[''"]') {
        return $matches[1]
    }
    return $null
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
    param(
        [Parameter(Mandatory)][int]$DashboardPort,
        [Parameter(Mandatory)][int]$PlayerPort,
        [Parameter(Mandatory)][string]$PublicHost
    )

    $secretsPath = Join-Path $installRoot 'storage\dev.secrets.ps1'
    if (Test-Path -LiteralPath $secretsPath) {
        # Check if secrets file already has dual ports
        $existing = Get-Content -LiteralPath $secretsPath -Raw -ErrorAction SilentlyContinue
        if ($existing -match 'MUSICHUB_DASHBOARD_PORT' -and $existing -match 'MUSICHUB_PLAYER_PORT') {
            Write-Info 'storage\dev.secrets.ps1 already has dual-port configuration; keeping secrets.'
            return $secretsPath
        }
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
        ("`$env:MUSICHUB_DASHBOARD_PORT = '" + $DashboardPort + "'")
        ("`$env:MUSICHUB_PLAYER_PORT = '" + $PlayerPort + "'")
        ("`$env:MUSICHUB_ADMIN_URL = 'http://" + $PublicHost + ':' + $DashboardPort + "'")
        ("`$env:MUSICHUB_PUBLIC_URL = 'http://" + $PublicHost + ':' + $PlayerPort + "'")
        "# Production applies the security headers (CSP, HSTS). A package install is a real deployment,"
        "# not a development host, so it must not run with the developer relaxations."
        ("`$env:ASPNETCORE_ENVIRONMENT = 'Production'")
        ("`$env:ASPNETCORE_URLS = 'http://0.0.0.0:" + $DashboardPort + ";http://0.0.0.0:" + $PlayerPort + "'")
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
    Write-Info "Secrets written to $secretsPath."
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
    $dPort = if ($endpoint -and $endpoint.DashboardPort) { [int]$endpoint.DashboardPort } elseif ($endpoint -and $endpoint.Port) { [int]$endpoint.Port } else { 5080 }
    $pPort = if ($endpoint -and $endpoint.PlayerPort) { [int]$endpoint.PlayerPort } else { 5081 }
    $lan = if ($endpoint -and $endpoint.LanAddress) { $endpoint.LanAddress } else { '127.0.0.1' }
    $dListening = [bool](Get-NetTCPConnection -LocalPort $dPort -State Listen -ErrorAction SilentlyContinue)
    $pListening = [bool](Get-NetTCPConnection -LocalPort $pPort -State Listen -ErrorAction SilentlyContinue)
    $pgListening = [bool](Get-NetTCPConnection -LocalPort 5433 -State Listen -ErrorAction SilentlyContinue)
    $workerRunning = [bool](Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like '*MusicHub.Worker.dll*' })
    return [pscustomobject]@{
        DashboardPort = $dPort; PlayerPort = $pPort; LanAddress = $lan
        DashboardListening = $dListening; PlayerListening = $pListening; PostgresListening = $pgListening; WorkerRunning = $workerRunning
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
Write-Host 'irpotify Server for Windows - install' -ForegroundColor Cyan

# -------------------------------------------------------------------------------------------
# Read-only verbs
# -------------------------------------------------------------------------------------------
if ($Credentials) {
    $state = Get-StackState
    $dPort = $state.DashboardPort
    $pPort = $state.PlayerPort
    $lan = $state.LanAddress
    $token = Get-BootstrapToken

    Write-Host ''
    Write-Host '========================================================================' -ForegroundColor Cyan
    Write-Host '                    irpotify Server Credentials                         ' -ForegroundColor Cyan
    Write-Host '========================================================================' -ForegroundColor Cyan
    Write-Host ''
    if ($token) {
        Write-Host '  Bootstrap Token (Master Setup Key):' -ForegroundColor Yellow
        Write-Host "  $token" -ForegroundColor Green
        Write-Host ''
        Write-Host '  Use this Bootstrap Token on the first-run setup page at /setup' -ForegroundColor White
        Write-Host '  or in the login dialog to recover / reset account passwords.' -ForegroundColor White
    } else {
        Write-Host '  Bootstrap Token: (Not found in storage\dev.secrets.ps1)' -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "  Admin Dashboard (Local)  : http://127.0.0.1:$dPort/index.html (مدیران)" -ForegroundColor Yellow
    Write-Host "  Admin Dashboard (LAN)    : http://${lan}:$dPort/index.html" -ForegroundColor Yellow
    Write-Host "  Player Web App (Local)   : http://127.0.0.1:$pPort/index.html (کاربران و پخش)" -ForegroundColor Green
    Write-Host "  Player Web App (LAN)     : http://${lan}:$pPort/index.html" -ForegroundColor Green
    Write-Host "  Setup Page               : http://127.0.0.1:$dPort/setup" -ForegroundColor White
    Write-Host '========================================================================' -ForegroundColor Cyan
    Write-Host ''
    exit 0
}

if ($Status) {
    $manifest = Get-PackageManifest
    $state = Get-StackState
    $token = Get-BootstrapToken
    Write-Host ''
    Write-Host "Install root : $installRoot"
    if ($manifest) { Write-Host "Version      : $($manifest.version) ($($manifest.platform))" }
    Write-Host "Dashboard port : $($state.DashboardPort)"
    Write-Host "Player port    : $($state.PlayerPort)"
    Write-Host "LAN address    : $($state.LanAddress)"
    Write-Host ("PostgreSQL     : " + $(if ($state.PostgresListening) { 'listening on 5433' } else { 'not running' }))
    Write-Host ("Dashboard API  : " + $(if ($state.DashboardListening) { "listening on $($state.DashboardPort)" } else { 'not running' }))
    Write-Host ("Player API     : " + $(if ($state.PlayerListening) { "listening on $($state.PlayerPort)" } else { 'not running' }))
    Write-Host ("Worker         : " + $(if ($state.WorkerRunning) { 'running' } else { 'not running' }))
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Write-Host ("Logon task     : " + $(if ($task) { $task.State } else { 'not registered' }))
    if ($token) {
        Write-Host ''
        Write-Host "Bootstrap Token : $token" -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host "Admin Dashboard: http://$($state.LanAddress):$($state.DashboardPort)/index.html" -ForegroundColor Yellow
    Write-Host "Player Web App : http://$($state.LanAddress):$($state.PlayerPort)/index.html" -ForegroundColor Green
    exit 0
}

if ($Stop) {
    Write-Step 'Stopping the stack'
    
    # 1. Stop scheduled task so it doesn't immediately respawn processes
    try {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    } catch {}

    # 2. Stop supervisor processes if running
    $supervisors = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like '*supervisor.ps1*' }
    foreach ($s in $supervisors) {
        Stop-Process -Id $s.ProcessId -Force -ErrorAction SilentlyContinue
    }

    # 3. Stop API, Worker, and Database
    $stopScript = Join-Path $installRoot 'deploy\scripts\stop-dev.ps1'
    if (Test-Path -LiteralPath $stopScript) {
        & $stopScript -StopDatabase
    }

    # 4. Clean PID files
    $runDir = Join-Path $installRoot 'storage\run'
    if (Test-Path -LiteralPath $runDir) {
        Get-ChildItem -LiteralPath $runDir -Filter '*.pid' -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    $postmasterPid = Join-Path $installRoot 'storage\postgres-data\postmaster.pid'
    if (Test-Path -LiteralPath $postmasterPid) {
        Remove-Item -LiteralPath $postmasterPid -Force -ErrorAction SilentlyContinue
    }

    Write-Host ''
    Write-Host 'irpotify Server and all services (PostgreSQL, API, Worker) stopped.' -ForegroundColor Green
    exit 0
}

if ($Launch -or $Start) {
    $endpoint = Get-Endpoint
    $dPort = if ($endpoint -and $endpoint.DashboardPort) { [int]$endpoint.DashboardPort } elseif ($endpoint -and $endpoint.Port) { [int]$endpoint.Port } else { 5080 }
    $pPort = if ($endpoint -and $endpoint.PlayerPort) { [int]$endpoint.PlayerPort } else { 5081 }
    $lan = if ($endpoint -and $endpoint.LanAddress) { $endpoint.LanAddress } else { '127.0.0.1' }
    $dashboardUrl = "http://127.0.0.1:$dPort/index.html"
    $playerUrl = "http://127.0.0.1:$pPort/index.html"

    # Fast path: Is the API already answering and healthy?
    $alreadyRunning = $false
    try {
        $res = Invoke-WebRequest -Uri "http://127.0.0.1:$dPort/healthz" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($res.StatusCode -eq 200) { $alreadyRunning = $true }
    } catch {}

    if ($alreadyRunning) {
        Write-Host "irpotify Server is already running." -ForegroundColor Green
        Write-Host "  Admin Dashboard : $dashboardUrl (مدیران)" -ForegroundColor Yellow
        Write-Host "  Player Web App  : $playerUrl (کاربران و پخش)" -ForegroundColor Green
        Start-Process $playerUrl
        Start-Sleep -Seconds 1
        exit 0
    }

    Write-Host ''
    Write-Host '========================================================================' -ForegroundColor Cyan
    Write-Host '                     Starting irpotify Server...                        ' -ForegroundColor Cyan
    Write-Host '========================================================================' -ForegroundColor Cyan
    Write-Host ''

    Use-PrivateRuntime | Out-Null

    # Clean any stale postgres postmaster.pid lock file
    $postmasterPid = Join-Path $installRoot 'storage\postgres-data\postmaster.pid'
    if (Test-Path -LiteralPath $postmasterPid) {
        try {
            $firstLine = (Get-Content -LiteralPath $postmasterPid -TotalCount 1).Trim()
            $stalePid = 0
            if ([int]::TryParse($firstLine, [ref]$stalePid)) {
                if (-not (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) {
                    Remove-Item -LiteralPath $postmasterPid -Force -ErrorAction SilentlyContinue
                    Write-Info 'Cleaned stale postmaster.pid lock file.'
                }
            }
        } catch {}
    }

    # Ensure log files are accessible
    $logDir = Join-Path $installRoot 'storage\logs'
    if (Test-Path -LiteralPath $logDir) {
        try {
            if (Get-Command icacls.exe -ErrorAction SilentlyContinue) {
                & icacls.exe $logDir /grant "$($env:USERNAME):(OI)(CI)F" /T /Q 2>&1 | Out-Null
            }
        } catch {}
    }

    # Start the stack
    & (Join-Path $installRoot 'deploy\scripts\start-dev.ps1') -BindLan -SkipBuild -NonInteractive -Force
    if ($LASTEXITCODE -ne 0) {
        Write-Err 'Server startup encountered an issue. Check storage\logs for details.'
        exit 1
    }

    Write-Info 'Verifying server responsiveness...'
    if (Wait-ForApi -Port $dPort -Seconds 60) {
        Write-Host ''
        Write-Host '========================================================================' -ForegroundColor Green
        Write-Host '                   irpotify Server is now ONLINE!                       ' -ForegroundColor Green
        Write-Host '========================================================================' -ForegroundColor Green
        Write-Host "  1. Admin Dashboard (Local) : $dashboardUrl (مدیران)" -ForegroundColor Yellow
        Write-Host "     Admin Dashboard (LAN)   : http://${lan}:$dPort/index.html" -ForegroundColor Yellow
        Write-Host "  2. Player Web App  (Local) : $playerUrl (کاربران و پخش)" -ForegroundColor Green
        Write-Host "     Player Web App  (LAN)   : http://${lan}:$pPort/index.html" -ForegroundColor Green
        Write-Host "  Mobile Apps (Android & iOS): http://${lan}:$pPort" -ForegroundColor Cyan
        Write-Host ''
        Write-Host '  Opening Player Web App in your browser...' -ForegroundColor Cyan
        Start-Process $playerUrl
        Start-Sleep -Seconds 2
        exit 0
    }
    else {
        Write-Err "API did not answer /healthz on port $dPort within 60 seconds."
        exit 1
    }
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
    # Interactive Dual-Port Selection if not provided as argument
    if ($ApiPort -gt 0 -and $DashboardPort -le 0) { $DashboardPort = $ApiPort }
    if ($DashboardPort -le 0 -and -not $NonInteractive -and [Environment]::UserInteractive) {
        $suggestedDPort = 5080
        $record = Get-Endpoint
        if ($record -and $record.DashboardPort) {
            $suggestedDPort = [int]$record.DashboardPort
        } elseif ($record -and $record.Port) {
            $suggestedDPort = [int]$record.Port
        } else {
            for ($p = 5080; $p -le 65535; $p++) {
                if (-not (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) -and $p -ne 5433) {
                    $suggestedDPort = $p
                    break
                }
            }
        }

        $suggestedPPort = 5081
        if ($record -and $record.PlayerPort) {
            $suggestedPPort = [int]$record.PlayerPort
        } else {
            for ($p = 5081; $p -le 65535; $p++) {
                if ($p -ne $suggestedDPort -and -not (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) -and $p -ne 5433) {
                    $suggestedPPort = $p
                    break
                }
            }
        }

        Write-Host ''
        Write-Host '========================================================================' -ForegroundColor Cyan
        Write-Host '               Dual Web-App Network & Port Configuration                ' -ForegroundColor Cyan
        Write-Host '========================================================================' -ForegroundColor Cyan
        Write-Host ' irpotify operates 2 independent Web Applications:' -ForegroundColor White
        Write-Host '   1. Admin Management PWA (وب‌اپلیکیشن مدیریتی - مخصوص مدیران سیستم)' -ForegroundColor Yellow
        Write-Host '   2. Player Web App (وب‌اپلیکیشن پخش موسیقی - شنوندگان، اندروید، iOS)' -ForegroundColor Green
        Write-Host ''
        Write-Host ' [Port 1] Admin Dashboard Port:' -ForegroundColor Yellow
        Write-Host " Default / Suggested: $suggestedDPort" -ForegroundColor Gray
        $validDPort = $false
        while (-not $validDPort) {
            $entered = Read-Host " Enter Admin Dashboard Port [$suggestedDPort]"
            if ([string]::IsNullOrWhiteSpace($entered)) {
                $DashboardPort = $suggestedDPort
                $validDPort = $true
            } else {
                $parsed = 0
                if ([int]::TryParse($entered.Trim(), [ref]$parsed) -and $parsed -ge 1024 -and $parsed -le 65535 -and $parsed -ne 5433) {
                    $inUse = Get-NetTCPConnection -LocalPort $parsed -State Listen -ErrorAction SilentlyContinue
                    if ($inUse) {
                        Write-Host " Warning: Port $parsed is currently in use." -ForegroundColor Yellow
                        $c = Read-Host " Use port $parsed anyway? (y/N)"
                        if ($c -eq 'y' -or $c -eq 'Y') { $DashboardPort = $parsed; $validDPort = $true }
                    } else {
                        $DashboardPort = $parsed
                        $validDPort = $true
                    }
                } else {
                    Write-Host " Invalid port. Must be 1024-65535 and not 5433." -ForegroundColor Red
                }
            }
        }

        Write-Host ''
        Write-Host ' [Port 2] Player Web App Port:' -ForegroundColor Green
        Write-Host " Default / Suggested: $suggestedPPort" -ForegroundColor Gray
        $validPPort = $false
        while (-not $validPPort) {
            $entered = Read-Host " Enter Player Web App Port [$suggestedPPort]"
            if ([string]::IsNullOrWhiteSpace($entered)) {
                if ($suggestedPPort -eq $DashboardPort) { $suggestedPPort = $DashboardPort + 1 }
                $PlayerPort = $suggestedPPort
                $validPPort = $true
            } else {
                $parsed = 0
                if ([int]::TryParse($entered.Trim(), [ref]$parsed) -and $parsed -ge 1024 -and $parsed -le 65535 -and $parsed -ne 5433) {
                    if ($parsed -eq $DashboardPort) {
                        Write-Host " Port $parsed is already assigned to Admin Dashboard. Choose a different port." -ForegroundColor Red
                    } else {
                        $inUse = Get-NetTCPConnection -LocalPort $parsed -State Listen -ErrorAction SilentlyContinue
                        if ($inUse) {
                            Write-Host " Warning: Port $parsed is currently in use." -ForegroundColor Yellow
                            $c = Read-Host " Use port $parsed anyway? (y/N)"
                            if ($c -eq 'y' -or $c -eq 'Y') { $PlayerPort = $parsed; $validPPort = $true }
                        } else {
                            $PlayerPort = $parsed
                            $validPPort = $true
                        }
                    }
                } else {
                    Write-Host " Invalid port. Must be 1024-65535 and not 5433." -ForegroundColor Red
                }
            }
        }

        Write-Host ''
        Write-Host " Selected Admin Dashboard Port: $DashboardPort" -ForegroundColor Yellow
        Write-Host " Selected Player Web App Port:  $PlayerPort" -ForegroundColor Green
        Write-Host '========================================================================' -ForegroundColor Cyan
        Write-Host ''
    }

    if ($DashboardPort -le 0) { $DashboardPort = if ($ApiPort -gt 0) { $ApiPort } else { 5080 } }
    if ($PlayerPort -le 0) { $PlayerPort = if ($DashboardPort -eq 5080) { 5081 } else { $DashboardPort + 1 } }

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
    $serverArgs['ApiPort'] = $DashboardPort
    if ($LanAddress) { $serverArgs['LanAddress'] = $LanAddress }
    if ($NonInteractive) { $serverArgs['NonInteractive'] = $true }
    & $installServer @serverArgs
    if ($LASTEXITCODE -ne 0) { throw "install-server.ps1 failed (exit $LASTEXITCODE). See the messages above." }

    $endpoint = Get-Endpoint
    if (-not $endpoint) { throw 'The installer did not record storage\install.endpoint.json.' }
    Write-Info "Chosen endpoint: $($endpoint.LanAddress) - Dashboard: $DashboardPort, Player: $PlayerPort"

    # Persist both ports in install.endpoint.json
    try {
        $epPath = Join-Path $installRoot 'storage\install.endpoint.json'
        $epObj = Get-Content -LiteralPath $epPath -Raw | ConvertFrom-Json
        $epObj | Add-Member -NotePropertyName 'DashboardPort' -NotePropertyValue $DashboardPort -Force
        $epObj | Add-Member -NotePropertyName 'PlayerPort' -NotePropertyValue $PlayerPort -Force
        $epObj | ConvertTo-Json | Set-Content -LiteralPath $epPath -Encoding UTF8
    } catch {}

    Write-Step '4/7 Secrets'
    Write-ServerSecrets -DashboardPort $DashboardPort -PlayerPort $PlayerPort -PublicHost ([string]$endpoint.LanAddress) | Out-Null

    Write-Step '5/7 Start the stack'
    $startDev = Join-Path $installRoot 'deploy\scripts\start-dev.ps1'
    # -SkipDatabase: install-server.ps1 has already initialised the cluster, created the application
    # role and written the encrypted credential. start-dev.ps1 would otherwise try to provision the
    # credential a second time, which is exactly the double-install path that used to fail.
    & $startDev -BindLan -SkipBuild -SkipDatabase -NonInteractive -Force
    if ($LASTEXITCODE -ne 0) { throw "start-dev.ps1 failed (exit $LASTEXITCODE). See the messages above." }

    Write-Step '6/7 Verify'
    if (-not (Wait-ForApi -Port $DashboardPort -Seconds 120)) {
        throw "The API did not answer /healthz on Dashboard port $DashboardPort within 120 seconds. Check storage\logs\api.err.log."
    }
    Write-Info "API answered http://127.0.0.1:$DashboardPort/healthz"

    # Verify Admin Dashboard
    try {
        $adminIndex = Invoke-WebRequest -Uri "http://127.0.0.1:$DashboardPort/index.html" -UseBasicParsing -TimeoutSec 8 -ErrorAction Stop
        Write-Info "Admin Dashboard served on port ${DashboardPort}: /index.html ($($adminIndex.RawContentLength) bytes)"
    } catch {
        Write-Warn "Admin dashboard /index.html check returned: $($_.Exception.Message)"
    }

    # Verify Player Web App
    try {
        $playerIndex = Invoke-WebRequest -Uri "http://127.0.0.1:$PlayerPort/index.html" -UseBasicParsing -TimeoutSec 8 -ErrorAction Stop
        Write-Info "Player Web App served on port ${PlayerPort}: /index.html ($($playerIndex.RawContentLength) bytes)"
    } catch {
        Write-Warn "Player Web App /index.html check returned: $($_.Exception.Message)"
    }

    # /api/health is the strongest evidence available here: the API performs a real query against
    # PostgreSQL and a real enumeration of the music root, so database=true proves the encrypted
    # credential works end to end.
    $health = $null
    try { $health = Invoke-RestMethod -Uri "http://127.0.0.1:$DashboardPort/api/health" -TimeoutSec 10 -ErrorAction Stop }
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
        $setup = Invoke-RestMethod -Uri "http://127.0.0.1:$DashboardPort/api/setup/status" -TimeoutSec 10 -ErrorAction Stop
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

    # Configure Firewall rules for BOTH ports (Dashboard & Player)
    foreach ($p in @($DashboardPort, $PlayerPort)) {
        $ruleName = "irpotify Server TCP $p"
        try {
            $devRule = Get-NetFirewallRule -DisplayName "*$p*" -ErrorAction SilentlyContinue |
                Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' -and $_.Action -eq 'Allow' }
            if ($devRule) {
                Write-Info "Firewall already allows TCP $p ($($devRule[0].DisplayName))."
            }
            else {
                New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP `
                    -LocalPort $p -RemoteAddress LocalSubnet -Action Allow -ErrorAction Stop | Out-Null
                Write-Info "Firewall rule '$ruleName' created (TCP $p from LocalSubnet)."
            }
        }
        catch {
            Write-Warn "Could not create firewall rule for port $p (needs elevation): $($_.Exception.Message)"
        }
    }

    $token = Get-BootstrapToken

    Write-Host ''
    Write-Host '========================================================================' -ForegroundColor Green
    Write-Host '                irpotify Server Installation Completed                  ' -ForegroundColor Green
    Write-Host '========================================================================' -ForegroundColor Green
    Write-Host ''
    Write-Host '  1. ADMIN DASHBOARD (وب‌اپلیکیشن مدیریتی - مخصوص مدیران سیستم):' -ForegroundColor Yellow
    Write-Host "     Local : http://127.0.0.1:$DashboardPort/index.html" -ForegroundColor White
    Write-Host "     LAN   : http://$($endpoint.LanAddress):$DashboardPort/index.html" -ForegroundColor White
    Write-Host ''
    Write-Host '  2. PLAYER WEB APP (وب‌اپلیکیشن پخش موسیقی - کاربران، شنوندگان و موبایل):' -ForegroundColor Green
    Write-Host "     Local : http://127.0.0.1:$PlayerPort/index.html" -ForegroundColor White
    Write-Host "     LAN   : http://$($endpoint.LanAddress):$PlayerPort/index.html" -ForegroundColor White
    Write-Host ''
    Write-Host "  Mobile Apps (Android & iOS) Server URL: http://$($endpoint.LanAddress):$PlayerPort" -ForegroundColor Cyan
    Write-Host ''
    Write-Host '========================================================================' -ForegroundColor Yellow
    Write-Host '                 ONE-TIME SETUP TOKEN (BOOTSTRAP TOKEN)                 ' -ForegroundColor Yellow
    Write-Host '========================================================================' -ForegroundColor Yellow
    Write-Host ''
    if ($token) {
        Write-Host "  $token" -ForegroundColor Cyan
        Write-Host ''
        Write-Host '  IMPORTANT SETUP INSTRUCTIONS:' -ForegroundColor Yellow
        Write-Host "  1. Copy the Bootstrap Token shown above." -ForegroundColor White
        Write-Host "  2. Open the Admin Dashboard: http://127.0.0.1:$DashboardPort/setup" -ForegroundColor White
        Write-Host "  3. Paste this Bootstrap Token into the setup form to create the primary administrator." -ForegroundColor White
        Write-Host ''
        Write-Host '  (This token is also stored in storage\dev.secrets.ps1 and can be viewed' -ForegroundColor Gray
        Write-Host '   at any time by running credentials.bat or status.bat)' -ForegroundColor Gray
    } else {
        Write-Host '  Warning: Bootstrap token could not be read from storage\dev.secrets.ps1' -ForegroundColor Red
    }
    Write-Host '========================================================================' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Next: open the admin dashboard and complete the first-run setup to create the administrator.'
    Write-Host '  Then sign in to the Player Web App or install it on your mobile device.'
    Write-Host ''
    exit 0
}
catch {
    Write-Err $_.Exception.Message
    Write-Host ''
    Write-Host 'Install did not complete. Logs are under storage\logs.' -ForegroundColor Red
    exit 1
}
