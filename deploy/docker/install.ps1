<#
.SYNOPSIS
    MusicHub Enterprise container install: prepare .env, detect the LAN address, choose the
    listening port, and start the stack.

.DESCRIPTION
    Runs the container path end to end so an operator does not have to hand-edit .env or work out
    which address the phone should connect to:

      1. Verifies Docker and the Compose plugin, and that the daemon answers.
      2. Creates .env from .env.example when it does not exist, and fills every CHANGE_ME value
         with a fresh cryptographic secret (never printed, never on a command line).
      3. Detects this host's LAN address and asks the operator which port the dashboard should be
         published on. Both are written to .env, so nothing machine-specific lives in the
         repository and a second install can pick a different port.
      4. Builds the dashboard, then starts postgres + api + worker + caddy with the LAN overlay.
      5. Waits for health and prints the exact URL to open.

    Safe to run repeatedly. An existing .env is never overwritten: only the endpoint variables are
    updated, so real secrets survive a re-run.

.PARAMETER Port
    Port to publish the dashboard on. Default: the port recorded by an earlier run, else the first
    free port from 8090 upward.

.PARAMETER LanAddress
    LAN address to publish on. Default: detected and confirmed with the operator.

.PARAMETER NonInteractive
    Accept the detected address and the first free port without asking (CI and automation).

.PARAMETER PublicHostname
    Public DNS name for the Caddy TLS edge. Default: keep whatever .env already has.

.PARAMETER AcmeEmail
    ACME contact address. Default: keep whatever .env already has.

.PARAMETER SkipBuild
    Do not rebuild images (use the images already present locally).

.PARAMETER SkipDashboard
    Do not rebuild the dashboard bundle into wwwroot.

.PARAMETER MinimumPort
    Lowest port the operator may choose. Default 1024 (unprivileged).

.EXAMPLE
    .\deploy\docker\install.ps1

.EXAMPLE
    .\deploy\docker\install.ps1 -Port 8090 -NonInteractive

.EXAMPLE
    .\deploy\docker\install.ps1 -PublicHostname music.example.com -AcmeEmail admin@example.com
#>
[CmdletBinding()]
param(
    [int]$Port = 0,
    [string]$LanAddress = '',
    [switch]$NonInteractive,
    [string]$PublicHostname = '',
    [string]$AcmeEmail = '',
    [switch]$SkipBuild,
    [switch]$SkipDashboard,
    [int]$MinimumPort = 1024,
    [int]$HealthTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot '..\scripts') 'common.ps1')

$repoRoot = Get-MusicHubRepoRoot
$envPath = Join-Path $repoRoot '.env'
$composeArgs = @('compose', '--env-file', '.env', '-f', 'deploy/docker/docker-compose.yml', '-f', 'deploy/docker/docker-compose.lan.yml')

function New-MusicHubHexSecret {
    <# Lowercase hex: URL-safe, no ';' or '"' to corrupt an Npgsql string, no '$' for compose. #>
    param([int]$Bytes = 32)
    $buffer = [byte[]]::new($Bytes)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return ($buffer | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Set-MusicHubEnvValue {
    <#
        Set KEY=value in .env, preserving every other line and its order. A missing key is appended.
        Values are written verbatim; nothing here is interpolated by PowerShell.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )
    $lines = @()
    if (Test-Path -LiteralPath $Path) { $lines = @(Get-Content -LiteralPath $Path) }

    $pattern = '^\s*' + [regex]::Escape($Key) + '\s*='
    $replaced = $false
    $output = @()
    foreach ($line in $lines) {
        if ($line -match $pattern) {
            $output += "$Key=$Value"
            $replaced = $true
        }
        else {
            $output += $line
        }
    }
    if (-not $replaced) { $output += "$Key=$Value" }

    Set-Content -LiteralPath $Path -Value $output -Encoding UTF8
}

function Get-MusicHubEnvValue {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Key)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $match = Get-Content -LiteralPath $Path | Where-Object { $_ -match ('^\s*' + [regex]::Escape($Key) + '\s*=') } | Select-Object -First 1
    if (-not $match) { return '' }
    return ($match -split '=', 2)[1].Trim()
}

$exitCode = 0
try {
    Write-Host ''
    Write-Host 'MusicHub Enterprise - container install' -ForegroundColor Cyan
    Write-MusicHubInfo "Repository: $repoRoot"

    Write-MusicHubStep '1/6 Docker'
    $docker = (Get-Command docker -ErrorAction SilentlyContinue).Source
    if (-not $docker) { throw 'docker was not found on PATH. Install Docker Engine/Desktop first.' }
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $composeVersion = (& docker compose version 2>&1) -join ' '
        $composeExit = $LASTEXITCODE
        $infoExit = 0
        & docker info *> $null
        $infoExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }

    if ($composeExit -ne 0) { throw "The Docker Compose v2 plugin is not available. $composeVersion" }
    Write-MusicHubInfo "Compose: $composeVersion"
    if ($infoExit -ne 0) { throw 'The Docker daemon is not answering. Start Docker and re-run.' }
    Write-MusicHubInfo 'Docker daemon is answering.'

    Write-MusicHubStep '2/6 Environment file'
    if (-not (Test-Path -LiteralPath $envPath)) {
        $template = Join-Path $repoRoot '.env.example'
        if (-not (Test-Path -LiteralPath $template)) { throw ".env.example is missing; cannot create $envPath." }
        Copy-Item -LiteralPath $template -Destination $envPath -Force
        Write-MusicHubInfo 'Created .env from .env.example.'
    }
    else {
        Write-MusicHubInfo '.env already exists; keeping its values.'
    }

    # Generate only the values that are still placeholders. A real secret that is already present is
    # left exactly as it is, so re-running the installer never rotates a working deployment.
    $generated = @()
    foreach ($spec in @(
        @{ Key = 'POSTGRES_PASSWORD'; Bytes = 32 },
        @{ Key = 'MUSICHUB_JWT_SECRET'; Bytes = 64 },
        @{ Key = 'MUSICHUB_BOOTSTRAP_TOKEN'; Bytes = 32 }
    )) {
        $current = Get-MusicHubEnvValue -Path $envPath -Key $spec.Key
        if ([string]::IsNullOrWhiteSpace($current) -or $current -like 'CHANGE_ME*') {
            Set-MusicHubEnvValue -Path $envPath -Key $spec.Key -Value (New-MusicHubHexSecret -Bytes $spec.Bytes)
            $generated += $spec.Key
        }
    }
    if ($generated.Count -gt 0) {
        Write-MusicHubInfo ("Generated fresh values for: {0}" -f ($generated -join ', '))
        Write-MusicHubInfo 'They are in .env only and were not printed. Back that file up and keep it private.'
    }
    else {
        Write-MusicHubInfo 'All required secrets are already set in .env.'
    }

    if ($PublicHostname) { Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_PUBLIC_HOSTNAME' -Value $PublicHostname }
    if ($AcmeEmail) { Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_ACME_EMAIL' -Value $AcmeEmail }

    # The template ships music.example.com. If it is still there and the operator did not pass
    # -PublicHostname, Caddy would attempt ACME for a domain this machine does not control, which
    # fails loudly and repeatedly. The LAN path bypasses Caddy entirely, so point the edge at
    # localhost (Caddy then uses its own local CA) and tell the operator how to switch to a real
    # domain later. Without this warning the failure only shows up in the Caddy logs.
    $currentHostname = Get-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_PUBLIC_HOSTNAME'
    if (-not $PublicHostname -and $currentHostname -eq 'music.example.com') {
        Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_PUBLIC_HOSTNAME' -Value 'localhost'
        Write-MusicHubInfo 'MUSICHUB_PUBLIC_HOSTNAME was still the template value; set it to localhost.'
        Write-MusicHubWarning 'The HTTPS edge is not configured for a real domain. LAN access works now.'
        Write-MusicHubWarning 'For public HTTPS, re-run with -PublicHostname <your-domain> -AcmeEmail <you@domain>.'
    }

    Write-MusicHubStep '3/6 LAN address and dashboard port'
    # The dashboard is reached by IP on the LAN. Detect the address and let the operator choose the
    # port; both go into .env so the compose overlay needs no literal and no default that collides.
    $endpoint = Resolve-MusicHubEndpoint `
        -RepoRoot $repoRoot `
        -Port $Port `
        -LanAddress $LanAddress `
        -PreferredPort 8090 `
        -MinimumPort $MinimumPort `
        -Mode 'docker' `
        -NonInteractive:$NonInteractive `
        -Persist

    Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_LAN_ADDRESS' -Value $endpoint.LanAddress
    Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_LAN_PORT' -Value ([string]$endpoint.Port)
    Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_DASHBOARD_PORT' -Value ([string]$endpoint.Port)
    $playerPort = [int]$endpoint.Port + 1
    Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_PLAYER_PORT' -Value ([string]$playerPort)

    # The pairing payload must name the URL clients actually reach, or the QR code is useless.
    $lanUrl = "http://$($endpoint.LanAddress):$($endpoint.Port)"
    $currentPublicUrl = Get-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_PUBLIC_URL'
    if ([string]::IsNullOrWhiteSpace($currentPublicUrl) -or $currentPublicUrl -eq 'https://music.example.com') {
        Set-MusicHubEnvValue -Path $envPath -Key 'MUSICHUB_PUBLIC_URL' -Value $lanUrl
        Write-MusicHubInfo "MUSICHUB_PUBLIC_URL set to $lanUrl"
    }
    else {
        Write-MusicHubInfo "Keeping MUSICHUB_PUBLIC_URL=$currentPublicUrl (already configured)."
    }

    # The API trusts forwarded headers only from the edge network. A LAN publication is a direct
    # connection, so the overlay does not need to widen that list.

    Write-MusicHubStep '4/6 Configuration check'
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $configOutput = & docker @composeArgs config --quiet 2>&1
        $configExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    if ($configExit -ne 0) {
        throw "docker compose config rejected the configuration (exit $configExit). $((@($configOutput) | ForEach-Object { "$_" }) -join ' ')"
    }
    Write-MusicHubInfo 'Compose configuration is valid.'

    Write-MusicHubStep '5/6 Dashboard and images'
    if (-not $SkipDashboard) {
        $npm = (Get-Command npm -ErrorAction SilentlyContinue).Source
        if ($npm) {
            $previousErrorAction = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & npm --prefix src/web/admin ci *> $null
                $npmExit = $LASTEXITCODE
                if ($npmExit -ne 0) { & npm --prefix src/web/admin install *> $null; $npmExit = $LASTEXITCODE }
                if ($npmExit -eq 0) {
                    & npm --prefix src/web/admin run build *> $null
                    $npmExit = $LASTEXITCODE
                }
            }
            finally { $ErrorActionPreference = $previousErrorAction }

            if ($npmExit -eq 0) {
                $adminDist = Join-Path $repoRoot 'src\web\admin\dist'
                $playerDist = Join-Path $repoRoot 'src\web\player\dist'
                $wwwrootAdmin = Join-Path $repoRoot 'src\server\MusicHub.Api\wwwroot\admin'
                $wwwrootPlayer = Join-Path $repoRoot 'src\server\MusicHub.Api\wwwroot\player'
                if (Test-Path -LiteralPath $adminDist) {
                    New-MusicHubDirectory -Path $wwwrootAdmin | Out-Null
                    Copy-Item -Path (Join-Path $adminDist '*') -Destination $wwwrootAdmin -Recurse -Force
                    Write-MusicHubInfo 'Admin dashboard built into src/server/MusicHub.Api/wwwroot/admin.'
                }
                if (Test-Path -LiteralPath $playerDist) {
                    New-MusicHubDirectory -Path $wwwrootPlayer | Out-Null
                    Copy-Item -Path (Join-Path $playerDist '*') -Destination $wwwrootPlayer -Recurse -Force
                    Write-MusicHubInfo 'Player web app built into src/server/MusicHub.Api/wwwroot/player.'
                }
            }
            else {
                Write-MusicHubWarning 'npm build failed; the image will serve the dashboard already in wwwroot (if any).'
            }
        }
        else {
            Write-MusicHubWarning 'npm was not found; skipping the dashboard build and using the existing wwwroot.'
        }
    }
    else {
        Write-MusicHubInfo 'Skipped (-SkipDashboard).'
    }

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($SkipBuild) {
            & docker @composeArgs up -d 2>&1 | ForEach-Object { Write-MusicHubInfo "$_" }
        }
        else {
            & docker @composeArgs up -d --build 2>&1 | ForEach-Object { Write-MusicHubInfo "$_" }
        }
        $upExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    if ($upExit -ne 0) { throw "docker compose up failed (exit $upExit)." }

    Write-MusicHubStep '6/6 Health'
    $healthUrl = "http://127.0.0.1:$($endpoint.Port)/healthz"
    if (Wait-MusicHubHttp -Url $healthUrl -TimeoutSeconds $HealthTimeoutSeconds) {
        Write-MusicHubInfo "API healthy: $healthUrl"
    }
    else {
        Write-MusicHubWarning "The API did not answer $healthUrl within $HealthTimeoutSeconds seconds."
        Write-MusicHubWarning 'Inspect the containers with: docker compose --env-file .env -f deploy/docker/docker-compose.yml logs api'
    }

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & docker @composeArgs ps 2>&1 | ForEach-Object { Write-MusicHubInfo "$_" } }
    finally { $ErrorActionPreference = $previousErrorAction }

    Write-MusicHubAccessSummary -Port $endpoint.Port -LanAddress $endpoint.LanAddress -DashboardPath '/index.html' -IsDocker
    Write-MusicHubInfo 'First administrator: open the dashboard, or POST /api/setup/initialize with MUSICHUB_BOOTSTRAP_TOKEN from .env.'
    Write-MusicHubInfo 'The Telegram bot starts with the worker; provision the token and the admin Telegram id to activate it.'
    Write-Host 'Container stack is running.' -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-MusicHubError $_.Exception.Message
}

exit $exitCode
