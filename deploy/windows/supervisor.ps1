<#
.SYNOPSIS
    MusicHub Enterprise server supervisor: keeps PostgreSQL, the API and the Worker running.

.DESCRIPTION
    Started by the "MusicHub Server" Scheduled Task at logon. It launches each host as a hidden
    detached process and then watches them, restarting anything that dies.

    Every path is derived from $PSScriptRoot, so this file works from any installation directory.
    The development supervisor in storage\run hardcodes the developer's checkout path; this one is
    the portable equivalent that ships inside the Windows install package. It is shipped to
    <root>\deploy\windows, so $PSScriptRoot\..\.. is the installation root.

    The data root is <install root>\storage, which holds dev.secrets.ps1 (generated at install
    time), postgres-data and the logs.
#>
$ErrorActionPreference = 'Continue'

# $PSScriptRoot is <root>\deploy\windows
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo

$secretsPath = Join-Path $repo 'storage\dev.secrets.ps1'
if (Test-Path -LiteralPath $secretsPath) {
    . $secretsPath
}

# A Scheduled Task does not inherit the installer's session environment, so DOTNET_ROOT is not set
# here. Point it at the packaged runtime explicitly: without this the hosts would be launched with a
# system dotnet, which a package install is not allowed to depend on.
$privateRuntime = Join-Path $repo 'runtime'
if (Test-Path -LiteralPath (Join-Path $privateRuntime 'dotnet.exe')) {
    $env:DOTNET_ROOT = $privateRuntime
    if (($env:PATH -split ';') -notcontains $privateRuntime) { $env:PATH = "$privateRuntime;$env:PATH" }
}

$log = Join-Path $repo 'storage\logs'
$run = Join-Path $repo 'storage\run'
New-Item -ItemType Directory -Path $log -Force | Out-Null
New-Item -ItemType Directory -Path $run -Force | Out-Null
$supervisorLog = Join-Path $log 'supervisor.log'

function Write-Log([string]$Message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Add-Content -Path $supervisorLog -Value $line -Encoding UTF8
}

function Get-ApiPort {
    # The port is whatever the installer recorded, so the supervisor never guesses.
    $endpointFile = Join-Path $repo 'storage\install.endpoint.json'
    if (Test-Path -LiteralPath $endpointFile) {
        try {
            $endpoint = Get-Content -LiteralPath $endpointFile -Raw | ConvertFrom-Json
            if ($endpoint.Port) { return [int]$endpoint.Port }
        }
        catch { Write-Log "could not read install.endpoint.json: $($_.Exception.Message)" }
    }
    return 5080
}

function Test-Port([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

# A listening port is not the same as a database that accepts queries. PostgreSQL opens the socket
# while it is still replaying WAL, and an API that connects in that window dies with
# "57P03: the database system is starting up" and never retries.
function Test-PostgresReady {
    $isReady = Join-Path $repo 'tools\pgsql\bin\pg_isready.exe'
    if (-not (Test-Path -LiteralPath $isReady)) { return (Test-Port $script:PgPort) }
    & $isReady -h 127.0.0.1 -p $script:PgPort -t 3 *> $null
    return ($LASTEXITCODE -eq 0)
}

function Test-PostgresRunning {
    if (Test-PostgresReady) { return $true }
    $stateFile = Join-Path $run 'postgres.pid'
    if (-not (Test-Path -LiteralPath $stateFile)) { return $false }
    $owner = (Get-Content -LiteralPath $stateFile -TotalCount 1).Trim()
    return [bool](Get-Process -Id $owner -ErrorAction SilentlyContinue)
}

function Wait-For([scriptblock]$Condition, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not (& $Condition) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    return [bool](& $Condition)
}

function Get-MusicHubDotnetProcess([string]$DllName) {
    # Match the full DLL name so an unrelated MusicHub install on the same machine is not counted.
    return Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$DllName*" }
}

function Start-Postgres {
    $pidFile = Join-Path $repo 'storage\postgres-data\postmaster.pid'
    if (Test-Path -LiteralPath $pidFile) {
        $owner = (Get-Content -LiteralPath $pidFile -TotalCount 1).Trim()
        if (-not (Get-Process -Id $owner -ErrorAction SilentlyContinue)) {
            Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
        }
    }
    $pg = Join-Path $repo 'tools\pgsql\bin\postgres.exe'
    $data = Join-Path $repo 'storage\postgres-data'
    $p = Start-Process -FilePath $pg `
        -ArgumentList @('-D', "`"$data`"", '-p', "$script:PgPort", '-c', 'listen_addresses=127.0.0.1') `
        -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $log 'postgres-task.log') `
        -RedirectStandardError (Join-Path $log 'postgres-task.err.log')
    $p.Id | Out-File -LiteralPath (Join-Path $run 'postgres.pid') -Encoding ASCII -NoNewline
    Write-Log "started PostgreSQL pid=$($p.Id)"
}

function Start-Host([string]$Kind, [string]$DllPath, [string]$WorkDir) {
    $dotnet = $env:DOTNET_ROOT
    if (-not $dotnet) { $dotnet = $repo }
    $dotnetExe = Join-Path $dotnet 'dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = 'dotnet' }
    $p = Start-Process -FilePath $dotnetExe -ArgumentList @("`"$DllPath`"") `
        -WorkingDirectory $WorkDir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $log ("{0}.log" -f $Kind.ToLowerInvariant())) `
        -RedirectStandardError (Join-Path $log ("{0}.err.log" -f $Kind.ToLowerInvariant()))
    Write-Log "started $Kind pid=$($p.Id)"
}

$script:PgPort = 5433
if ($env:MUSICHUB_PG_PORT) { $script:PgPort = [int]$env:MUSICHUB_PG_PORT }

$apiDll    = Join-Path $repo 'src\server\MusicHub.Api\bin\Debug\net10.0\MusicHub.Api.dll'
$workerDll = Join-Path $repo 'src\server\MusicHub.Worker\bin\Debug\net10.0\MusicHub.Worker.dll'
$apiDir    = Join-Path $repo 'src\server\MusicHub.Api'
$workerDir = Join-Path $repo 'src\server\MusicHub.Worker'

Write-Log 'supervisor starting'

while ($true) {
    try {
        if (-not (Test-PostgresRunning)) {
            Write-Log 'PostgreSQL is not running; starting it.'
            Start-Postgres
            if (Wait-For { Test-PostgresReady } 90) {
                Write-Log 'PostgreSQL is ready.'
            }
            else {
                Write-Log 'PostgreSQL did not become ready within 90s.'
            }
        }

        $apiPort = Get-ApiPort
        # Only start the API once the database really answers, otherwise it exits during startup.
        if ((Test-PostgresReady) -and (-not (Test-Port $apiPort))) {
            Write-Log "API is down on $apiPort; starting it."
            Start-Host -Kind 'Api' -DllPath $apiDll -WorkDir $apiDir
            if (Wait-For { Test-Port $apiPort } 90) {
                Write-Log 'API is up.'
            }
            else {
                Write-Log 'API did not come up within 90s.'
            }
        }

        if ((Test-PostgresReady) -and (-not (Get-MusicHubDotnetProcess 'MusicHub.Worker.dll'))) {
            Write-Log 'Worker is not running; starting it.'
            Start-Host -Kind 'Worker' -DllPath $workerDll -WorkDir $workerDir
        }
    }
    catch {
        Write-Log "supervisor error: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds 30
}
