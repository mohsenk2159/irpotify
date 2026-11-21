<#
.SYNOPSIS
    Network discovery and listening-port selection for MusicHub Enterprise installs.

.DESCRIPTION
    Shared by the Windows native install (deploy/scripts/install-server.ps1 and
    deploy/scripts/start-dev.ps1) and the container install (deploy/docker/install.ps1).
    Dot-sourced through common.ps1.

    Two problems are solved here:

      1. The address other devices must use is the machine's own LAN address. It is discovered at
         install time instead of being hard-coded, so the same installer works on any host and no
         operator-specific address ends up in the repository.

      2. The listening port is chosen by the operator from the allowed range after the installer
         reports which ports are already taken. A fixed default port collides with whatever else
         the host happens to run; asking removes the guesswork.

    The chosen endpoint is persisted to <repo>/storage/install.endpoint.json (gitignored) so a
    second script in the same install session reuses the operator's answer instead of asking
    again. The file is JSON, never PowerShell: reading it cannot execute code.

    Nothing here opens a firewall port, changes a network adapter or binds a listener beyond the
    instant it takes to prove a port is free.

.NOTES
    Compatible with Windows PowerShell 5.1 and PowerShell 7+ (Windows and Linux).
#>

# common.ps1 normally provides the writers and dot-sources this file. Standalone use (tests, ad-hoc
# diagnostics) must not fail on a missing function, so define minimal fallbacks when absent.
if (-not (Get-Command Write-MusicHubWarning -CommandType Function -ErrorAction SilentlyContinue)) {
    function Write-MusicHubWarning { param([Parameter(Mandatory)][string]$Message) Write-Host "WARN  $Message" -ForegroundColor Yellow }
}
if (-not (Get-Command Write-MusicHubInfo -CommandType Function -ErrorAction SilentlyContinue)) {
    function Write-MusicHubInfo { param([Parameter(Mandatory)][string]$Message) Write-Host "      $Message" -ForegroundColor Gray }
}

function Get-MusicHubLanAddress {
    <#
        Return the host's IPv4 LAN addresses, best candidate first.

        Loopback, APIPA (169.254/16) and virtual adapter ranges are excluded by default because the
        operator cannot reach the server on them from another device: Docker/WSL/Hyper-V vEthernet
        switches and VPN tunnels are private to this machine.
    #>
    [CmdletBinding()]
    param([switch]$IncludeVirtual)

    $virtualPattern = 'vEthernet|Loopback|Bluetooth|Docker|WSL|Hyper-V|VMware|VirtualBox|Teredo|isatap|Tailscale|ZeroTier|WireGuard|OpenVPN|br-|veth'

    $addresses = @()

    if ($IsWindows -ne $false -and (Get-Command Get-NetIPAddress -ErrorAction SilentlyContinue)) {
        # Windows: the adapter table is the only place that also tells us which interface carries
        # the default route, so the recommendation is the network peers actually share.
        $candidates = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object {
                $_.IPAddress -notlike '127.*' -and
                $_.IPAddress -notlike '169.254.*' -and
                $_.AddressState -eq 'Preferred'
            })

        $defaultRouteIndexes = @()
        try {
            $defaultRouteIndexes = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty InterfaceIndex)
        }
        catch { }

        foreach ($candidate in $candidates) {
            $alias = ''
            try {
                $adapter = Get-NetAdapter -InterfaceIndex $candidate.InterfaceIndex -ErrorAction SilentlyContinue
                if ($adapter) { $alias = [string]$adapter.InterfaceAlias }
            }
            catch { }

            $addresses += [pscustomobject]@{
                IPAddress       = $candidate.IPAddress
                InterfaceIndex  = [int]$candidate.InterfaceIndex
                InterfaceAlias  = $alias
                PrefixLength    = [int]$candidate.PrefixLength
                IsPrivate       = ($candidate.IPAddress -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)')
                IsVirtual       = ($alias -match $virtualPattern)
                HasDefaultRoute = ($defaultRouteIndexes -contains [int]$candidate.InterfaceIndex)
            }
        }
    }
    else {
        # Linux/macOS: no adapter metadata through a portable cmdlet, so resolve the host name and
        # report the addresses it answers with. Ordering falls back to private-range preference.
        $resolved = @()
        try { $resolved = @([System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName())) } catch { }
        if ($resolved.Count -eq 0) {
            try { $resolved = @([System.Net.Dns]::GetHostAddresses('')) } catch { }
        }

        $index = 0
        foreach ($candidate in $resolved) {
            if ($candidate.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { continue }
            $text = $candidate.ToString()
            if ($text -like '127.*' -or $text -like '169.254.*') { continue }
            $index++
            $addresses += [pscustomobject]@{
                IPAddress       = $text
                InterfaceIndex  = $index
                InterfaceAlias  = ''
                PrefixLength    = 0
                IsPrivate       = ($text -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)')
                IsVirtual       = $false
                HasDefaultRoute = $true
            }
        }
    }

    if (-not $IncludeVirtual) {
        $filtered = @($addresses | Where-Object { -not $_.IsVirtual })
        if ($filtered.Count -gt 0) { $addresses = $filtered }
    }

    # Order: the adapter that carries the default route first (that is the network peers share),
    # then private ranges, then everything else.
    return @($addresses | Sort-Object `
        @{ Expression = { if ($_.HasDefaultRoute) { 0 } else { 1 } } }, `
        @{ Expression = { if ($_.IsPrivate) { 0 } else { 1 } } }, `
        @{ Expression = { $_.InterfaceAlias } })
}

function Get-MusicHubPrimaryLanAddress {
    <#
        Return the single best LAN address as a string, or $null when the host has none.
        The caller decides whether that is fatal; a loopback-only install is still valid.
    #>
    [CmdletBinding()]
    param([switch]$Quiet)

    $all = @(Get-MusicHubLanAddress)
    if ($all.Count -eq 0) {
        if (-not $Quiet) {
            Write-MusicHubWarning 'No LAN IPv4 address was detected. Other devices will not be able to reach this server by IP.'
        }
        return $null
    }
    return $all[0].IPAddress
}

function Test-MusicHubPortAvailable {
    <#
        True when the port can be bound on the given address right now.

        The probe binds and immediately releases the socket, so it answers "is something already
        listening" without reserving the port. An address of 0.0.0.0 tests every interface, which
        is what a wildcard listen needs.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Port,
        [string]$Address = '0.0.0.0'
    )

    if ($Port -lt 1 -or $Port -gt 65535) { return $false }

    $listener = $null
    try {
        $ipAddress = if ($Address -eq '0.0.0.0' -or $Address -eq '*' -or [string]::IsNullOrWhiteSpace($Address)) {
            [System.Net.IPAddress]::Any
        }
        else {
            [System.Net.IPAddress]::Parse($Address)
        }
        $listener = [System.Net.Sockets.TcpListener]::new($ipAddress, $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener) {
            try { $listener.Stop() } catch { }
        }
    }
}

function Find-MusicHubFreePort {
    <#
        First free port at or above -PreferredPort, wrapping to -MinimumPort once the upper end is
        exhausted. Returns $null when nothing in the range is free.
    #>
    [CmdletBinding()]
    param(
        [int]$PreferredPort = 5080,
        [int]$MinimumPort = 1024,
        [int]$MaximumPort = 65535,
        [string]$Address = '0.0.0.0'
    )

    $start = [Math]::Max($MinimumPort, [Math]::Min($PreferredPort, $MaximumPort))

    for ($port = $start; $port -le $MaximumPort; $port++) {
        if (Test-MusicHubPortAvailable -Port $port -Address $Address) { return $port }
    }
    for ($port = $MinimumPort; $port -lt $start; $port++) {
        if (Test-MusicHubPortAvailable -Port $port -Address $Address) { return $port }
    }
    return $null
}

function Select-MusicHubListenPort {
    <#
        Ask the operator which port the server should listen on.

        The suggested value is the first free port from -PreferredPort upward, so pressing Enter
        always yields a working install. A typed value is validated against the allowed range and
        against the live socket table, and the question is repeated rather than accepting a port
        that would fail at startup.
    #>
    [CmdletBinding()]
    param(
        [int]$PreferredPort = 5080,
        [int]$MinimumPort = 1024,
        [int]$MaximumPort = 65535,
        [string]$Address = '0.0.0.0',
        [switch]$NonInteractive,
        [int]$MaxAttempts = 5
    )

    if ($MinimumPort -lt 1 -or $MaximumPort -gt 65535 -or $MinimumPort -ge $MaximumPort) {
        throw "Invalid port range $MinimumPort-$MaximumPort. Use 1-65535 with MinimumPort < MaximumPort."
    }

    $suggested = Find-MusicHubFreePort -PreferredPort $PreferredPort -MinimumPort $MinimumPort -MaximumPort $MaximumPort -Address $Address
    if (-not $suggested) {
        throw "No free TCP port was found between $MinimumPort and $MaximumPort on $Address."
    }

    if ($NonInteractive -or -not [Environment]::UserInteractive) {
        return $suggested
    }

    Write-Host ''
    Write-Host ("Port {0} is {1}." -f $PreferredPort, $(if (Test-MusicHubPortAvailable -Port $PreferredPort -Address $Address) { 'free' } else { 'already in use' })) -ForegroundColor DarkGray
    Write-Host ("Suggested port: {0} (free). Press Enter to accept, or type another port in {1}-{2}." -f $suggested, $MinimumPort, $MaximumPort) -ForegroundColor Cyan

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $answer = Read-Host "Listening port [$suggested]"
        if ([string]::IsNullOrWhiteSpace($answer)) { return $suggested }

        $parsed = 0
        if (-not [int]::TryParse($answer.Trim(), [ref]$parsed)) {
            Write-MusicHubWarning "'$($answer.Trim())' is not a number."
            continue
        }
        if ($parsed -lt $MinimumPort -or $parsed -gt $MaximumPort) {
            Write-MusicHubWarning "$parsed is outside the allowed range $MinimumPort-$MaximumPort."
            continue
        }
        if (-not (Test-MusicHubPortAvailable -Port $parsed -Address $Address)) {
            Write-MusicHubWarning "Port $parsed is already in use on this host. Choose another."
            continue
        }
        return $parsed
    }

    throw "No valid port was chosen after $MaxAttempts attempts. Re-run the installer and enter a free port."
}

function Select-MusicHubLanAddress {
    <#
        Ask which detected address should be published when the host has more than one.
        A single address is returned without prompting.
    #>
    [CmdletBinding()]
    param([switch]$NonInteractive)

    $all = @(Get-MusicHubLanAddress)
    if ($all.Count -eq 0) { return $null }
    if ($all.Count -eq 1 -or $NonInteractive -or -not [Environment]::UserInteractive) { return $all[0].IPAddress }

    Write-Host ''
    Write-Host 'Detected network addresses for other devices:' -ForegroundColor Cyan
    for ($i = 0; $i -lt $all.Count; $i++) {
        $entry = $all[$i]
        $marker = if ($i -eq 0) { ' (recommended)' } else { '' }
        $alias = if ($entry.InterfaceAlias) { $entry.InterfaceAlias } else { 'interface' }
        Write-Host ("  [{0}] {1}  {2}/{3}{4}" -f ($i + 1), $entry.IPAddress, $alias, $entry.PrefixLength, $marker)
    }

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $answer = Read-Host "Address other devices should use [1]"
        if ([string]::IsNullOrWhiteSpace($answer)) { return $all[0].IPAddress }
        $parsed = 0
        if ([int]::TryParse($answer.Trim(), [ref]$parsed) -and $parsed -ge 1 -and $parsed -le $all.Count) {
            return $all[$parsed - 1].IPAddress
        }
        Write-MusicHubWarning "Enter a number between 1 and $($all.Count)."
    }
    return $all[0].IPAddress
}

function Get-MusicHubAccessUrls {
    <#
        Build the URLs the operator and client apps should use for a given bind address/port.
        Loopback is always included: it is what the installer itself verifies against.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Port,
        [string]$LanAddress = '',
        [switch]$Https
    )

    $scheme = if ($Https) { 'https' } else { 'http' }
    $urls = [ordered]@{ Loopback = "${scheme}://127.0.0.1:$Port" }
    if ($LanAddress) { $urls.Lan = "${scheme}://${LanAddress}:$Port" }
    return [pscustomobject]$urls
}

function Write-MusicHubAccessSummary {
    <#
        Print exactly what the operator has to type into a browser or a client app.
        Called at the end of both install paths so the answer is never "look it up yourself".
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Port,
        [string]$LanAddress = '',
        [switch]$Https,
        [string]$DashboardPath = '/index.html',
        [switch]$IsDocker
    )

    $urls = Get-MusicHubAccessUrls -Port $Port -LanAddress $LanAddress -Https:$Https

    Write-Host ''
    Write-Host '  Access this server' -ForegroundColor Cyan
    Write-Host ("    This machine:      {0}{1}" -f $urls.Loopback, $DashboardPath)
    if ($urls.Lan) {
        Write-Host ("    Other devices:     {0}{1}" -f $urls.Lan, $DashboardPath) -ForegroundColor Green
        Write-Host '    Server address for the Android and Windows apps:' -ForegroundColor DarkGray
        Write-Host ("      {0}" -f $urls.Lan) -ForegroundColor Green
    }
    else {
        Write-Host '    Other devices:     no LAN address detected; this server is reachable from this machine only.' -ForegroundColor Yellow
    }
    Write-Host ("    Listening port:    {0}" -f $Port)
    if ($IsDocker) {
        Write-Host '    Container:         the api service publishes this port on the LAN address above.' -ForegroundColor DarkGray
    }
    Write-Host ''
}

function Get-MusicHubEndpointFilePath {
    <# Absolute path of the persisted endpoint record inside the data root. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)
    return (Join-Path (Join-Path $RepoRoot 'storage') 'install.endpoint.json')
}

function Import-MusicHubEndpoint {
    <#
        Read the endpoint persisted by an earlier install step. Returns $null when no record exists
        or the record is unreadable, so the caller simply prompts again.

        JSON is used on purpose: reading a data file must never execute anything.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Get-MusicHubEndpointFilePath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $path)) { return $null }

    try {
        $record = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Write-MusicHubWarning "Ignoring unreadable endpoint record at ${path}: $($_.Exception.Message)"
        return $null
    }

    $port = 0
    if (-not [int]::TryParse([string]$record.Port, [ref]$port) -or $port -lt 1 -or $port -gt 65535) { return $null }

    return [pscustomobject]@{
        LanAddress = [string]$record.LanAddress
        Port       = $port
        Mode       = [string]$record.Mode
        UpdatedAt  = [string]$record.UpdatedAt
    }
}

function Save-MusicHubEndpoint {
    <# Persist the operator's choice so the next install step does not ask the same questions. #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][int]$Port,
        [string]$LanAddress = '',
        [string]$Mode = 'windows'
    )

    $path = Get-MusicHubEndpointFilePath -RepoRoot $RepoRoot
    $directory = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $record = [ordered]@{
        LanAddress = $LanAddress
        Port       = $Port
        Mode       = $Mode
        UpdatedAt  = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Set-Content -LiteralPath $path -Value ($record | ConvertTo-Json) -Encoding UTF8

    try {
        if (Get-Command icacls.exe -ErrorAction SilentlyContinue) {
            & icacls.exe $path /inheritance:r /grant:r "$($env:USERNAME):(R,W)" 2>&1 | Out-Null
        }
        elseif (Get-Command chmod -ErrorAction SilentlyContinue) {
            & chmod 600 $path 2>&1 | Out-Null
        }
    }
    catch { }

    return $path
}

function Test-MusicHubEndpointAlive {
    <#
        True when something already serves MusicHub's dependency-free liveness endpoint on this
        port. Used so a re-run recognises its own running stack instead of treating the occupied
        port as a conflict and proposing a different one.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Port,
        [string]$Address = '127.0.0.1',
        [int]$TimeoutMs = 1500
    )

    $previousProgress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        $response = Invoke-WebRequest -Uri "http://${Address}:$Port/healthz" -UseBasicParsing -TimeoutSec ([Math]::Max(1, [int]($TimeoutMs / 1000)))
        return ([int]$response.StatusCode -eq 200)
    }
    catch {
        return $false
    }
    finally {
        $ProgressPreference = $previousProgress
    }
}

function Resolve-MusicHubEndpoint {
    <#
        Decide the LAN address and listening port for this install.

        Precedence, highest first:
          1. Explicit -Port / -LanAddress (automation, tests, re-runs).
          2. The endpoint recorded by an earlier step of the same install, if its port is still free.
          3. Discovery plus an interactive question (skipped with -NonInteractive).

        Returns an object with LanAddress, Port, Source and Mode. -Persist writes the answer to
        storage/install.endpoint.json so the following install step reuses it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [int]$Port = 0,
        [string]$LanAddress = '',
        [int]$PreferredPort = 5080,
        [int]$MinimumPort = 1024,
        [int]$MaximumPort = 65535,
        [string]$Mode = 'windows',
        [switch]$NonInteractive,
        [switch]$Persist
    )

    $source = 'discovered'
    $resolvedPort = 0
    $resolvedAddress = $LanAddress

    if ($Port -gt 0) {
        if ($Port -lt 1 -or $Port -gt 65535) { throw "Port $Port is outside 1-65535." }
        if (-not (Test-MusicHubPortAvailable -Port $Port) -and -not (Test-MusicHubEndpointAlive -Port $Port)) {
            Write-MusicHubWarning "Port $Port is already in use on this host by something that is not MusicHub; the server will fail to bind it."
        }
        $resolvedPort = $Port
        $source = 'parameter'
    }
    else {
        $record = Import-MusicHubEndpoint -RepoRoot $RepoRoot
        # A recorded port is still correct when it is free, or when the process holding it is this
        # very stack (a re-run must not migrate itself to a new port and orphan the old listener).
        if ($record -and ((Test-MusicHubPortAvailable -Port $record.Port) -or (Test-MusicHubEndpointAlive -Port $record.Port))) {
            $resolvedPort = $record.Port
            if (-not $resolvedAddress) { $resolvedAddress = $record.LanAddress }
            $source = 'saved'
            Write-MusicHubInfo "Reusing the listening port $resolvedPort recorded by the previous install step."
        }
    }

    if (-not $resolvedAddress) {
        $resolvedAddress = Select-MusicHubLanAddress -NonInteractive:$NonInteractive
    }

    if ($resolvedPort -le 0) {
        if ($resolvedAddress) {
            Write-Host ''
            Write-Host ("This machine's LAN address is {0}." -f $resolvedAddress) -ForegroundColor Green
        }
        # An already-running MusicHub on the preferred port is not a conflict: reuse it so the
        # re-run attaches to the existing listener instead of spawning a second instance.
        if (Test-MusicHubEndpointAlive -Port $PreferredPort) {
            $resolvedPort = $PreferredPort
            $source = 'running'
            Write-MusicHubInfo "MusicHub is already answering on port $PreferredPort."
        }
        else {
            $resolvedPort = Select-MusicHubListenPort `
                -PreferredPort $PreferredPort `
                -MinimumPort $MinimumPort `
                -MaximumPort $MaximumPort `
                -NonInteractive:$NonInteractive
        }
    }

    if ($Persist) {
        Save-MusicHubEndpoint -RepoRoot $RepoRoot -Port $resolvedPort -LanAddress $resolvedAddress -Mode $Mode | Out-Null
    }

    return [pscustomobject]@{
        LanAddress = [string]$resolvedAddress
        Port       = [int]$resolvedPort
        Source     = $source
        Mode       = $Mode
    }
}
