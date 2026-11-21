<#
.SYNOPSIS
    Shared helpers for MusicHub Enterprise deployment scripts.

.DESCRIPTION
    Dot-source this file from scripts in deploy/scripts:

        . (Join-Path $PSScriptRoot 'common.ps1')

    Design rules:
      * Secrets are never written to the console, logs, process command lines or reports.
        Database credentials travel to PostgreSQL clients via PGPASSWORD in the child
        process environment, never as command-line arguments.
      * Every helper is idempotent or explicitly read-only.
      * Nothing here installs services, opens firewall ports or deletes data.

.NOTES
    Owner: devops-release-engineer. Compatible with Windows PowerShell 5.1 and PowerShell 7+.
#>

$ErrorActionPreference = 'Stop'

# Network discovery and port selection live in their own file because both install paths (native
# Windows and container) need them and neither should duplicate socket probing or address ranking.
. (Join-Path $PSScriptRoot 'network.ps1')

function Get-MusicHubRepoRoot {
    <# Resolve the repository root from deploy/scripts. #>
    $resolved = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
    return (Resolve-Path -LiteralPath $resolved).Path
}

function Write-MusicHubStep {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-MusicHubInfo {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "    $Message"
}

function Write-MusicHubWarning {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "WARN: $Message" -ForegroundColor Yellow
}

function Write-MusicHubError {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "ERROR: $Message" -ForegroundColor Red
}

function New-MusicHubRandomSecret {
    <# Cryptographically secure, URL-safe random secret. #>
    param([int]$Bytes = 48)
    if ($Bytes -lt 16) { throw "Secret length must be at least 16 bytes." }
    $buffer = New-Object byte[] $Bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($buffer) -replace '\+', '-' -replace '/', '_').TrimEnd('=')
}

function Resolve-MusicHubDotnet {
    <#
        Locate the .NET host. The development host uses a portable SDK under
        %LOCALAPPDATA%\Microsoft\dotnet, which requires DOTNET_ROOT for global tools
        (dotnet-ef) to resolve hostfxr. Returns the full path to dotnet.exe or $null.
    #>
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($env:DOTNET_ROOT) { $candidates.Add((Join-Path $env:DOTNET_ROOT 'dotnet.exe')) }
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'))
    $candidates.Add('C:\Program Files\dotnet\dotnet.exe')
    $onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($onPath) { $candidates.Insert(0, $onPath.Source) }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return [System.IO.Path]::GetFullPath($candidate) }
    }
    return $null
}

function Initialize-MusicHubDotnet {
    <# Put dotnet and global tool shims on PATH and set DOTNET_ROOT for portable SDKs. #>
    param([switch]$Quiet)
    $dotnet = Resolve-MusicHubDotnet
    if (-not $dotnet) { throw "The .NET SDK was not found. Install .NET 10 or add dotnet.exe to PATH." }
    $dotnetHome = Split-Path -Parent $dotnet
    $portableSdk = Join-Path $dotnetHome 'sdk'
    if ((Test-Path -LiteralPath $portableSdk) -and -not (Test-Path -LiteralPath (Join-Path $dotnetHome 'host\fxr'))) {
        Write-MusicHubWarning "dotnet.exe at $dotnetHome has no host\fxr; the SDK is probably incomplete."
    }
    if ($dotnetHome -ne 'C:\Program Files\dotnet') {
        $env:DOTNET_ROOT = $dotnetHome
    }
    $toolShims = Join-Path $env:USERPROFILE '.dotnet\tools'
    $pathParts = $env:PATH -split ';'
    foreach ($part in @($dotnetHome, $toolShims)) {
        if ($part -and ($pathParts -notcontains $part)) { $env:PATH = "$part;$env:PATH" }
    }
    if (-not $Quiet) { Write-MusicHubInfo "dotnet: $dotnet" }
    return $dotnet
}

function ConvertFrom-MusicHubConnectionString {
    <#
        Parse an Npgsql connection string or a postgres:// URL into components.
        Supports Host/Server, Port, Database/Initial Catalog, Username/User ID/User, Password/Pwd.
        Returns an object; the Password property must never be printed.
    #>
    param([Parameter(Mandatory)][string]$ConnectionString)
    $hostName = '127.0.0.1'
    $port = 5432
    $database = 'musichub'
    $username = 'musichub'
    $password = ''

    $text = $ConnectionString.Trim()
    if ($text -match '^(postgres|postgresql|npgsql)://') {
        $uri = [Uri]$text
        if ($uri.Host) { $hostName = $uri.Host }
        if ($uri.Port -gt 0) { $port = $uri.Port }
        $userInfo = $uri.UserInfo
        if ($userInfo) {
            $parts = $userInfo.Split(':', 2)
            $username = [Uri]::UnescapeDataString($parts[0])
            if ($parts.Count -gt 1) { $password = [Uri]::UnescapeDataString($parts[1]) }
        }
        $dbPath = $uri.AbsolutePath.Trim('/')
        if ($dbPath) { $database = [Uri]::UnescapeDataString($dbPath) }
    }
    else {
        foreach ($segment in $text.Split(';')) {
            $trimmed = $segment.Trim()
            if (-not $trimmed) { continue }
            $index = $trimmed.IndexOf('=')
            if ($index -lt 1) { continue }
            $key = ($trimmed.Substring(0, $index) -replace '\s', '').ToLowerInvariant()
            $value = $trimmed.Substring($index + 1).Trim()
            switch ($key) {
                'host' { $hostName = $value }
                'server' { $hostName = $value }
                'port' { $parsed = 0; if ([int]::TryParse($value, [ref]$parsed)) { $port = $parsed } }
                'database' { $database = $value }
                'initialcatalog' { $database = $value }
                'username' { $username = $value }
                'userid' { $username = $value }
                'user' { $username = $value }
                'password' { $password = $value }
                'pwd' { $password = $value }
            }
        }
    }

    return [pscustomobject]@{
        Host     = $hostName
        Port     = [int]$port
        Database = $database
        Username = $username
        Password = $password
    }
}

function Find-MusicHubTool {
    <# Return the first existing path from the candidates, or '' when none exists. #>
    param([AllowEmptyString()][AllowNull()][string[]]$Candidates)
    if (-not $Candidates) { return '' }
    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    return ''
}

function Get-MusicHubConfig {
    <#
        Build the deployment configuration for this host.

        -NoSecrets : do not dot-source storage\dev.secrets.ps1 (read-only tools only).
        Secrets precedence: process environment > storage\dev.secrets.ps1 (gitignored) > development defaults.
    #>
    param(
        [string]$RepoRoot,
        [switch]$NoSecrets
    )
    if (-not $RepoRoot) { $RepoRoot = Get-MusicHubRepoRoot }
    $secretsPath = Join-Path $RepoRoot 'storage\dev.secrets.ps1'
    $secretsLoaded = $false
    if (-not $NoSecrets -and (Test-Path -LiteralPath $secretsPath)) {
        . $secretsPath
        $secretsLoaded = $true
    }

    $connectionString = $env:MUSICHUB_CONNECTION_STRING
    if ([string]::IsNullOrWhiteSpace($connectionString)) { $connectionString = $env:ConnectionStrings__Default }
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        $connectionString = 'Host=127.0.0.1;Port=5433;Database=musichub;Username=musichub;Password=musichub'
    }
    $db = ConvertFrom-MusicHubConnectionString -ConnectionString $connectionString

    $pgBin = Join-Path $RepoRoot 'tools\pgsql\bin'
    if (-not (Test-Path -LiteralPath (Join-Path $pgBin 'psql.exe'))) { $pgBin = '' }
    $psql = if ($pgBin) { Join-Path $pgBin 'psql.exe' } else { (Get-Command psql.exe -ErrorAction SilentlyContinue).Source }
    $pgDump = if ($pgBin) { Join-Path $pgBin 'pg_dump.exe' } else { (Get-Command pg_dump.exe -ErrorAction SilentlyContinue).Source }
    $pgRestore = if ($pgBin) { Join-Path $pgBin 'pg_restore.exe' } else { (Get-Command pg_restore.exe -ErrorAction SilentlyContinue).Source }
    $pgCtl = if ($pgBin) { Join-Path $pgBin 'pg_ctl.exe' } else { (Get-Command pg_ctl.exe -ErrorAction SilentlyContinue).Source }
    $pgIsReady = if ($pgBin) { Join-Path $pgBin 'pg_isready.exe' } else { (Get-Command pg_isready.exe -ErrorAction SilentlyContinue).Source }
    $createdb = if ($pgBin) { Join-Path $pgBin 'createdb.exe' } else { (Get-Command createdb.exe -ErrorAction SilentlyContinue).Source }
    $dropdb = if ($pgBin) { Join-Path $pgBin 'dropdb.exe' } else { (Get-Command dropdb.exe -ErrorAction SilentlyContinue).Source }

    $ffprobeCandidate = Get-ChildItem -Path (Join-Path $RepoRoot 'tools\ffmpeg') -Recurse -Filter 'ffprobe.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    $ffmpegCandidate = Get-ChildItem -Path (Join-Path $RepoRoot 'tools\ffmpeg') -Recurse -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    $ffprobe = $env:MUSICHUB_FFPROBE_PATH
    if ([string]::IsNullOrWhiteSpace($ffprobe)) {
        $ffprobe = Find-MusicHubTool -Candidates @($ffprobeCandidate, '', (Get-Command ffprobe.exe -ErrorAction SilentlyContinue).Source)
    }
    $ffmpeg = $env:MUSICHUB_FFMPEG_PATH
    if ([string]::IsNullOrWhiteSpace($ffmpeg)) {
        $ffmpeg = Find-MusicHubTool -Candidates @($ffmpegCandidate, '', (Get-Command ffmpeg.exe -ErrorAction SilentlyContinue).Source)
    }

    $dataRoot = $env:MUSICHUB_DATA_ROOT
    if ([string]::IsNullOrWhiteSpace($dataRoot)) { $dataRoot = Join-Path $RepoRoot 'storage' }
    $dataRoot = [System.IO.Path]::GetFullPath($dataRoot)

    $pgDataDir = $env:MUSICHUB_PG_DATA_DIR
    if ([string]::IsNullOrWhiteSpace($pgDataDir)) { $pgDataDir = Join-Path $RepoRoot 'storage\postgres-data' }
    $pgDataDir = [System.IO.Path]::GetFullPath($pgDataDir)

    $baseUrl = $env:MUSICHUB_BASE_URL
    if ([string]::IsNullOrWhiteSpace($baseUrl) -and $env:ASPNETCORE_URLS) {
        $firstUrl = ($env:ASPNETCORE_URLS -split ';')[0].Trim()
        if ($firstUrl) {
            $baseUrl = $firstUrl -replace '://\*', '://127.0.0.1' -replace '://0\.0\.0\.0', '://127.0.0.1' -replace '://\+', '://127.0.0.1'
        }
    }
    if ([string]::IsNullOrWhiteSpace($baseUrl)) { $baseUrl = 'http://127.0.0.1:5080' }
    $baseUrl = $baseUrl.TrimEnd('/')

    $apiProjectDir = Join-Path $RepoRoot 'src\server\MusicHub.Api'
    $workerProjectDir = Join-Path $RepoRoot 'src\server\MusicHub.Worker'

    return [pscustomobject]@{
        RepoRoot          = $RepoRoot
        SecretsPath       = $secretsPath
        SecretsLoaded     = $secretsLoaded
        ConnectionString  = $connectionString
        Host              = $db.Host
        Port              = $db.Port
        Database          = $db.Database
        Username          = $db.Username
        Password          = $db.Password
        DataRoot          = $dataRoot
        PgDataDir         = $pgDataDir
        LogDir            = Join-Path $dataRoot 'logs'
        RunDir            = Join-Path $dataRoot 'run'
        BackupDir         = Join-Path $dataRoot 'backups'
        MusicRoot         = Join-Path $dataRoot 'music'
        ArtworkRoot       = Join-Path $dataRoot 'artwork'
        QuarantineRoot    = Join-Path $dataRoot 'quarantine'
        PgBin             = $pgBin
        Psql              = $psql
        PgDump            = $pgDump
        PgRestore         = $pgRestore
        PgCtl             = $pgCtl
        PgIsReady         = $pgIsReady
        Createdb          = $createdb
        DropDb            = $dropdb
        Ffprobe           = $ffprobe
        Ffmpeg            = $ffmpeg
        ApiBaseUrl        = $baseUrl
        ApiProjectDir     = $apiProjectDir
        WorkerProjectDir  = $workerProjectDir
        ApiDll            = Join-Path $apiProjectDir 'bin\Debug\net10.0\MusicHub.Api.dll'
        WorkerDll         = Join-Path $workerProjectDir 'bin\Debug\net10.0\MusicHub.Worker.dll'
        Solution          = Join-Path $RepoRoot 'MusicHub.slnx'
    }
}

function New-MusicHubDirectory {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Test-MusicHubTcpPort {
    param(
        [string]$ComputerName = '127.0.0.1',
        [Parameter(Mandatory)][int]$Port,
        [int]$TimeoutMs = 1500
    )
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($ComputerName, $Port)
        if ($task.Wait($TimeoutMs)) { return $client.Connected }
        return $false
    }
    catch { return $false }
    finally { $client.Close() }
}

function Wait-MusicHubHttp {
    param(
        [Parameter(Mandatory)][string]$Url,
        [int]$TimeoutSeconds = 90,
        [int]$IntervalMilliseconds = 1000
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -eq 200) { return $true }
        }
        catch { }
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
    return $false
}

function Invoke-MusicHubHttp {
    <# HTTP helper that never throws; returns StatusCode/Content/Json/Error. #>
    param(
        [string]$Method = 'GET',
        [Parameter(Mandatory)][string]$Url,
        [hashtable]$Headers,
        [string]$Body,
        [int]$TimeoutSec = 30
    )
    $request = @{ Uri = $Url; Method = $Method; UseBasicParsing = $true; TimeoutSec = $TimeoutSec }
    if ($Headers) { $request.Headers = $Headers }
    if ($PSBoundParameters.ContainsKey('Body') -and $null -ne $Body) {
        $request.Body = $Body
        $request.ContentType = 'application/json'
    }
    try {
        $response = Invoke-WebRequest @request
        $content = $response.Content
        $json = $null
        if ($content) { try { $json = $content | ConvertFrom-Json } catch { } }
        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Content = $content; Json = $json; Error = $null }
    }
    catch [System.Net.WebException] {
        $status = 0
        $content = ''
        if ($_.Exception.Response) {
            try {
                $status = [int]$_.Exception.Response.StatusCode
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $content = $reader.ReadToEnd()
                    $reader.Dispose()
                }
            }
            catch { }
        }
        $json = $null
        if ($content) { try { $json = $content | ConvertFrom-Json } catch { } }
        return [pscustomobject]@{ StatusCode = $status; Content = $content; Json = $json; Error = $_.Exception.Message }
    }
    catch {
        return [pscustomobject]@{ StatusCode = 0; Content = ''; Json = $null; Error = $_.Exception.Message }
    }
}

function Invoke-MusicHubPsql {
    <#
        Run psql with ON_ERROR_STOP. The password is passed through PGPASSWORD in the child
        environment only and is restored afterwards. Never pass SQL containing secrets.
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [string]$Sql,
        [string]$SqlFile,
        [string]$Database,
        [switch]$AllowFailure,
        [switch]$NoRowCount
    )
    if (-not $Config.Psql) { throw "psql was not found. Set up tools\pgsql or add psql to PATH." }
    $targetDatabase = if ($Database) { $Database } else { $Config.Database }
    $previousPassword = $env:PGPASSWORD
    $previousTimeout = $env:PGCONNECT_TIMEOUT
    $previousErrorAction = $ErrorActionPreference
    $env:PGPASSWORD = $Config.Password
    $env:PGCONNECT_TIMEOUT = '10'
    $ErrorActionPreference = 'Continue'
    $psqlArgs = @(
        '-h', $Config.Host,
        '-p', "$($Config.Port)",
        '-U', $Config.Username,
        '-d', $targetDatabase,
        '-X',
        '--no-password',
        '-v', 'ON_ERROR_STOP=1',
        '-A', '-t'
    )
    if ($NoRowCount) { $psqlArgs += @('-q') }
    # Windows PowerShell passes embedded double quotes to native processes incorrectly
    # (they are dropped), so SQL containing quoted identifiers like "AppSettings"
    # would reach psql as appsettings. Route such SQL through a temporary file instead.
    $temporarySqlFile = $null
    if (-not $SqlFile -and $Sql -and $Sql.Contains('"')) {
        $temporarySqlFile = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($temporarySqlFile, $Sql, [System.Text.UTF8Encoding]::new($false))
        $SqlFile = $temporarySqlFile
    }
    if ($SqlFile) { $psqlArgs += @('-f', $SqlFile) }
    else { $psqlArgs += @('-c', $Sql) }
    try {
        $output = & $Config.Psql @psqlArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $env:PGPASSWORD = $previousPassword
        $env:PGCONNECT_TIMEOUT = $previousTimeout
        $ErrorActionPreference = $previousErrorAction
        if ($temporarySqlFile) { Remove-Item -LiteralPath $temporarySqlFile -Force -ErrorAction SilentlyContinue }
    }
    $text = (@($output) | ForEach-Object { "$_" }) -join "`n"
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "psql failed with exit code $exitCode against $($Config.Host):$($Config.Port)/$targetDatabase. $text"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output); Text = $text }
}

function Invoke-MusicHubNative {
    <#
        Run a native tool, applying temporary process environment variables (for example
        PGPASSWORD) and always restoring them. Never echoes the environment values.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [hashtable]$Environment = @{},
        [switch]$AllowFailure
    )
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process')
    }
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        foreach ($key in $Environment.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
    }
    $text = ((@($output) | ForEach-Object { "$_" }) -join "`n")
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "$([System.IO.Path]::GetFileName($FilePath)) failed with exit code $exitCode. $text"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Text = $text; Lines = @($output) }
}

function Get-MusicHubPsqlScalar {    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$Sql,
        [string]$Database,
        [switch]$AllowFailure
    )
    $result = Invoke-MusicHubPsql -Config $Config -Sql $Sql -Database $Database -AllowFailure:$AllowFailure -NoRowCount
    if ($result.ExitCode -ne 0) { return $null }
    $line = @($result.Output | Where-Object { "$_".Trim() -ne '' } | Select-Object -First 1)
    if ($line.Count -eq 0) { return $null }
    return "$($line[0])".Trim()
}

function Test-MusicHubPostgresReady {
    param([Parameter(Mandatory)]$Config, [switch]$Quiet)
    if (-not $Config.PgIsReady) {
        if (-not $Quiet) { Write-MusicHubWarning "pg_isready was not found; falling back to a TCP probe on port $($Config.Port)." }
        return (Test-MusicHubTcpPort -ComputerName $Config.Host -Port $Config.Port)
    }
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Config.PgIsReady -h $Config.Host -p "$($Config.Port)" -q | Out-Null; $ready = ($LASTEXITCODE -eq 0) }
    finally { $ErrorActionPreference = $previousErrorAction }
    return $ready
}

function Get-MusicHubMigrationIds {
    <# EF migration ids from source files (excludes .Designer.cs and the model snapshot). #>
    param([Parameter(Mandatory)][string]$RepoRoot)
    $migrationsDir = Join-Path $RepoRoot 'src\server\MusicHub.Infrastructure\Persistence\Migrations'
    if (-not (Test-Path -LiteralPath $migrationsDir)) { return ,@() }
    # Keep the array intact: a bare `return @(...)` unrolls to a scalar for single items,
    # which makes callers index characters (e.g. '..._InitialCreate'[-1] -eq 'e').
    return ,@(Get-ChildItem -LiteralPath $migrationsDir -Filter '*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -ne 'MusicHubDbContextModelSnapshot.cs' } |
        ForEach-Object { $_.BaseName } |
        Sort-Object)
}

function Get-MusicHubAppliedMigrations {
    <# Applied migration ids from the live database, oldest first. #>
    param([Parameter(Mandatory)]$Config, [string]$Database)
    if (-not (Test-MusicHubPostgresReady -Config $Config -Quiet)) { return ,@() }
    $result = Invoke-MusicHubPsql -Config $Config -Database $Database -AllowFailure -NoRowCount -Sql 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";'
    if ($result.ExitCode -ne 0) { return ,@() }
    return ,@($result.Output | ForEach-Object { "$_".Trim() } | Where-Object { $_ -ne '' })
}

function Get-MusicHubWorkerHeartbeat {
    <#
        Worker heartbeat written by MusicHub.Worker (AppSettings key 'worker.heartbeat').
        Returns object with Present, EpochSeconds, AgeSeconds, UpdatedAtUtc.
    #>
    param([Parameter(Mandatory)]$Config)
    $sql = @'
select coalesce(extract(epoch from "UpdatedAt")::bigint, 0) from "AppSettings" where "Key" = 'worker.heartbeat';
'@
    $raw = Get-MusicHubPsqlScalar -Config $Config -Sql $sql -AllowFailure
    $epoch = 0
    if ($raw -and [long]::TryParse($raw, [ref]$epoch)) {
        $updated = [DateTimeOffset]::FromUnixTimeSeconds($epoch)
        return [pscustomobject]@{
            Present      = $true
            EpochSeconds = $epoch
            AgeSeconds   = [int][Math]::Round(([DateTimeOffset]::UtcNow - $updated).TotalSeconds)
            UpdatedAtUtc = $updated.ToString('o')
        }
    }
    return [pscustomobject]@{ Present = $false; EpochSeconds = 0; AgeSeconds = [int]::MaxValue; UpdatedAtUtc = '' }
}

function Get-MusicHubProcesses {
    <# Locate MusicHub API/Worker host processes (dotnet MusicHub.Api.dll or published exe). #>
    param([Parameter(Mandatory)][ValidateSet('Api', 'Worker')][string]$Kind)
    $marker = "MusicHub.$Kind"
    $processes = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='MusicHub.Api.exe' OR Name='MusicHub.Worker.exe'" -ErrorAction SilentlyContinue
    foreach ($process in @($processes)) {
        if (-not $process.CommandLine) { continue }
        if ($process.CommandLine -notlike "*$marker*") { continue }
        if ($process.CommandLine -like '*dotnet test*') { continue }
        if ($process.CommandLine -like '*vstest*') { continue }
        [pscustomobject]@{
            ProcessId   = [int]$process.ProcessId
            Name        = $process.Name
            CommandLine = $process.CommandLine
            StartedAt   = ''
        }
    }
}

function Get-MusicHubPidFile {
    param([Parameter(Mandatory)]$Config, [Parameter(Mandatory)][ValidateSet('Api', 'Worker')][string]$Kind)
    return (Join-Path $Config.RunDir ("{0}.pid" -f $Kind.ToLowerInvariant()))
}

function Save-MusicHubPidFile {
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][ValidateSet('Api', 'Worker')][string]$Kind,
        [Parameter(Mandatory)][int]$ProcessId
    )
    New-MusicHubDirectory -Path $Config.RunDir | Out-Null
    $payload = [ordered]@{
        kind        = $Kind
        processId   = $ProcessId
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue).CommandLine
    }
    $payload | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Get-MusicHubPidFile -Config $Config -Kind $Kind) -Encoding UTF8
}

function Stop-MusicHubHostProcess {
    <#
        Stop one host process. Windows console hosts do not receive WM_CLOSE, so this makes a
        best-effort graceful attempt (taskkill without /F, then a 15 s drain window) and then a
        forced termination. Service deployments use Stop-Service for a true graceful shutdown.
    #>
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [int]$GraceSeconds = 15
    )
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { return [pscustomobject]@{ ProcessId = $ProcessId; Stopped = $true; Forced = $false; Note = 'process was not running' } }
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & taskkill.exe /PID $ProcessId /T 2>&1 | Out-Null
        $deadline = (Get-Date).AddSeconds($GraceSeconds)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
                return [pscustomobject]@{ ProcessId = $ProcessId; Stopped = $true; Forced = $false; Note = 'graceful signal accepted' }
            }
            Start-Sleep -Milliseconds 500
        }
        & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
                return [pscustomobject]@{ ProcessId = $ProcessId; Stopped = $true; Forced = $true; Note = 'force-terminated after grace period' }
            }
            Start-Sleep -Milliseconds 300
        }
        return [pscustomobject]@{ ProcessId = $ProcessId; Stopped = $false; Forced = $true; Note = 'process did not exit' }
    }
    finally { $ErrorActionPreference = $previousErrorAction }
}

function Start-MusicHubHostProcess {
    <#
        Start a MusicHub host as a detached process that survives the calling shell.
        Environment variables are inherited (secrets are never placed on the command line).
        Returns the ProcessId.
    #>
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][ValidateSet('Api', 'Worker')][string]$Kind,
        [Parameter(Mandatory)][string]$DllPath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [string]$LogFile
    )
    if (-not (Test-Path -LiteralPath $DllPath)) {
        throw "Host assembly not found: $DllPath. Build the solution first (dotnet build MusicHub.slnx)."
    }
    New-MusicHubDirectory -Path $Config.LogDir | Out-Null
    New-MusicHubDirectory -Path $Config.RunDir | Out-Null
    if (-not $LogFile) { $LogFile = Join-Path $Config.LogDir ("{0}.log" -f $Kind.ToLowerInvariant()) }
    $errorLog = [System.IO.Path]::ChangeExtension($LogFile, '.err.log')
    $dotnet = Resolve-MusicHubDotnet
    if (-not $dotnet) { throw "dotnet.exe was not found." }
    $process = Start-Process -FilePath $dotnet -ArgumentList @("`"$DllPath`"") -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -RedirectStandardOutput $LogFile -RedirectStandardError $errorLog -PassThru
    Save-MusicHubPidFile -Config $Config -Kind $Kind -ProcessId $process.Id
    return $process.Id
}

function Remove-MusicHubPidFile {
    param([Parameter(Mandatory)]$Config, [Parameter(Mandatory)][ValidateSet('Api', 'Worker')][string]$Kind)
    $path = Get-MusicHubPidFile -Config $Config -Kind $Kind
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
}

function New-MusicHubCheckList {
    <#
      Return an empty List[object] without letting PowerShell unroll it.
      A plain `return $list` on an empty collection emits nothing, so the
      caller receives $null and Add-MusicHubCheck fails with
      "Cannot bind argument to parameter 'List' because it is null."
    #>
    Write-Output -NoEnumerate ([System.Collections.Generic.List[object]]::new())
}

function Add-MusicHubCheck {
    <# Record a PASS/FAIL/WARN/SKIP row and print it immediately. #>
    param(
        [Parameter(Mandatory)]$List,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('PASS', 'FAIL', 'WARN', 'SKIP')][string]$Status,
        [string]$Detail = ''
    )
    $List.Add([pscustomobject]@{ Check = $Name; Status = $Status; Detail = $Detail })
    $color = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        default { 'Gray' }
    }
    Write-Host ("[{0}] {1} - {2}" -f $Status, $Name, $Detail) -ForegroundColor $color
}

function Get-MusicHubCheckFailureCount {
    param([Parameter(Mandatory)]$List)
    return @($List | Where-Object { $_.Status -eq 'FAIL' }).Count
}

function Write-MusicHubCheckSummary {
    param([Parameter(Mandatory)]$List)
    Write-Host ''
    $List | Format-Table -AutoSize Check, Status, Detail | Out-String -Width 200 | Write-Host
    $failures = Get-MusicHubCheckFailureCount -List $List
    $warnings = @($List | Where-Object { $_.Status -eq 'WARN' }).Count
    $passed = @($List | Where-Object { $_.Status -eq 'PASS' }).Count
    $skipped = @($List | Where-Object { $_.Status -eq 'SKIP' }).Count
    $color = if ($failures -gt 0) { 'Red' } else { 'Green' }
    Write-Host ("Summary: {0} passed, {1} failed, {2} warnings, {3} skipped" -f $passed, $failures, $warnings, $skipped) -ForegroundColor $color
    return $failures
}

function Stop-MusicHubDatabase {
    param(
        [Parameter(Mandatory)]$Config,
        [ValidateSet('smart', 'fast', 'immediate')][string]$Mode = 'fast'
    )
    if (-not $Config.PgCtl) { throw "pg_ctl was not found; cannot stop PostgreSQL." }
    if (-not (Test-MusicHubPostgresReady -Config $Config -Quiet)) {
        return [pscustomobject]@{ Stopped = $true; Note = 'PostgreSQL was not running' }
    }
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Config.PgCtl -D $Config.PgDataDir stop -m $Mode -w 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    $text = (@($output) | ForEach-Object { "$_" }) -join ' '
    if ($exitCode -ne 0) { throw "pg_ctl stop failed with exit code $exitCode. $text" }
    return [pscustomobject]@{ Stopped = $true; Note = $text.Trim() }
}
