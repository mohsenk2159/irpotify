<#
.SYNOPSIS
    Build the self-contained MusicHub Enterprise Windows server package.

.DESCRIPTION
    Produces artifacts/installers/MusicHubServer-win-x64-<version>.zip: a directory tree that can be
    extracted anywhere and installed with one command, without a .NET SDK, without a network
    connection, and without the repository.

    The package deliberately mirrors the repository layout (src\server, deploy\scripts, tools) because
    the existing install and start scripts resolve every path relative to the repository root. That
    means the shipped scripts are the same ones the tests exercise, not a parallel reimplementation
    that could drift.

    What goes in:

      src\server\MusicHub.Api\bin\Debug\net10.0\      published API (framework-dependent) + wwwroot
      src\server\MusicHub.Worker\bin\Debug\net10.0\  published Worker
      src\server\MusicHub.Install\bin\Debug\net10.0\ the installer CLI (credential + recovery verbs)
      src\server\MusicHub.Infrastructure\...          migrations, so start-dev can compare versions
      src\web\admin\dist\                             dashboard source for the build step
      deploy\scripts\                                 install, start, stop, verify, backup, restore
      deploy\windows\                                 supervisor + install entry point
      tools\pgsql\bin, lib, share                     portable PostgreSQL 17 (no pgAdmin, no docs)
      tools\ffmpeg\...\bin                            ffmpeg + ffprobe
      runtime\                                        private .NET 10 runtime (no SDK, no admin install)

    What is deliberately left out: the .NET SDK (~1 GB), pgAdmin 4 (673 MB, a GUI the server never
    uses), PostgreSQL documentation/include trees, ffplay (an SDL player the server never starts), the
    test projects, node_modules, and every secret. The API and Worker are published Release and
    Debug-Configuration-free: the scripts look under bin\Debug\net10.0, so the publish output is
    placed there deliberately.

.PARAMETER Version
    Version stamped into the artifact name and the package manifest.

.PARAMETER OutputDirectory
    Where the .zip is written. Default: <repo>\artifacts\installers.

.PARAMETER KeepStaging
    Leave the staging directory in place for inspection.

.EXAMPLE
    .\deploy\windows\build-package.ps1

.EXAMPLE
    .\deploy\windows\build-package.ps1 -Version 1.0.1 -KeepStaging
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$OutputDirectory = '',
    [switch]$KeepStaging,
    [switch]$SkipWebBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\common.ps1')

$repoRoot = Get-MusicHubRepoRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\installers' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$stagingName = "MusicHubServer-win-x64-$Version"
$staging = Join-Path ([System.IO.Path]::GetTempPath()) $stagingName
$zipPath = Join-Path $OutputDirectory "$stagingName.zip"

function Write-Phase([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function New-Dir([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
    return $Path
}

function Get-TreeSize([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $sum = (Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if (-not $sum) { return 0 }
    return $sum
}

Write-Host ''
Write-Host "MusicHub Enterprise - Windows server package $Version" -ForegroundColor Cyan

$dotnet = Initialize-MusicHubDotnet

# ---------------------------------------------------------------------------------------------
# 1. Clean staging
# ---------------------------------------------------------------------------------------------
Write-Phase '1/8 Staging'
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
    Write-MusicHubInfo "Removed the previous staging directory."
}
New-Dir $staging | Out-Null
New-Dir $OutputDirectory | Out-Null
Write-MusicHubInfo "Staging: $staging"

# ---------------------------------------------------------------------------------------------
# 2. Publish the server hosts
# ---------------------------------------------------------------------------------------------
# The scripts resolve bin\Debug\net10.0 regardless of configuration, so publishing into that path
# keeps the package compatible with deploy/scripts without editing them. Debug symbols are excluded
# because they add hundreds of megabytes and the release package ships no source to match them.
Write-Phase '2/8 Publish API, Worker and Install CLI'

$publishTargets = @(
    @{ Project = 'src\server\MusicHub.Api\MusicHub.Api.csproj';         Out = 'src\server\MusicHub.Api\bin\Debug\net10.0';         Dll = 'MusicHub.Api.dll' },
    @{ Project = 'src\server\MusicHub.Worker\MusicHub.Worker.csproj';   Out = 'src\server\MusicHub.Worker\bin\Debug\net10.0';   Dll = 'MusicHub.Worker.dll' },
    @{ Project = 'src\server\MusicHub.Install\MusicHub.Install.csproj'; Out = 'src\server\MusicHub.Install\bin\Debug\net10.0'; Dll = 'MusicHub.Install.dll' }
)

foreach ($target in $publishTargets) {
    $projectPath = Join-Path $repoRoot $target.Project
    $outPath = Join-Path $staging $target.Out
    New-Dir $outPath | Out-Null

    Write-MusicHubInfo "Publishing $($target.Project)"
    $publishArgs = @(
        'publish', $projectPath,
        '-c', 'Debug',
        '-o', $outPath,
        '--nologo',
        '-v', 'minimal',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:UseAppHost=false'
    )
    $output = $null
    $publishExit = 0
    # Same stderr trap as npm: dotnet writes warnings and progress to stderr, and a terminating
    # NativeCommandError would abort packaging on a successful publish.
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $dotnet @publishArgs 2>&1
        $publishExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    if ($publishExit -ne 0) {
        throw "dotnet publish failed for $($target.Project) (exit $publishExit).`n$((@($output) | ForEach-Object { "$_" }) -join "`n")"
    }

    $dllPath = Join-Path $outPath $target.Dll
    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw "Publish did not produce $dllPath."
    }
    Write-MusicHubInfo "  $($target.Dll) OK"
}

# The start script compares the migrations on disk with the ones applied to the database. Ship the
# migration sources so that comparison works without the repository.
$migrationsSource = Join-Path $repoRoot 'src\server\MusicHub.Infrastructure\Persistence\Migrations'
$migrationsTarget = Join-Path $staging 'src\server\MusicHub.Infrastructure\Persistence\Migrations'
New-Dir $migrationsTarget | Out-Null
if (Test-Path -LiteralPath $migrationsSource) {
    Get-ChildItem -LiteralPath $migrationsSource -Filter '*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $migrationsTarget -Force }
    $count = @(Get-ChildItem -LiteralPath $migrationsTarget -Filter '*.cs').Count
    Write-MusicHubInfo "  migrations: $count file(s)"
}

# ---------------------------------------------------------------------------------------------
# 3. Dashboard
# ---------------------------------------------------------------------------------------------
Write-Phase '3/8 Dashboard'

$distSource = Join-Path $repoRoot 'src\web\admin\dist'
$webSource = Join-Path $repoRoot 'src\web\admin'
# The API is started with its working directory set to the project directory, which is therefore its
# content root, and ASP.NET Core serves static files from <content root>\wwwroot. Copying the bundle
# into the publish folder would produce an install that serves the API but returns 404 for the
# dashboard. The project directory is the authoritative location; the publish folder gets a copy too
# so the assembly also works when it is started from there.
$wwwroot = Join-Path $staging 'src\server\MusicHub.Api\wwwroot'
$wwwrootPublish = Join-Path $staging 'src\server\MusicHub.Api\bin\Debug\net10.0\wwwroot'

if (-not $SkipWebBuild) {
    $npm = (Get-Command npm.cmd -ErrorAction SilentlyContinue).Source
    if (-not $npm) { $npm = (Get-Command npm -ErrorAction SilentlyContinue).Source }
    if ($npm) {
        Write-MusicHubInfo 'Building the dashboard (npm run build)'
        Push-Location $webSource
        # npm writes progress to stderr. Under $ErrorActionPreference='Stop' (set at the top of this
        # script) PowerShell converts the first stderr line into a terminating NativeCommandError, so a
        # perfectly good build would abort the packaging run. Judge by the exit code instead.
        $previousErrorAction = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $npmOut = & $npm run build 2>&1
            $npmExit = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorAction
            Pop-Location
        }
        if ($npmExit -ne 0) {
            throw "The dashboard build failed (exit $npmExit).`n$((@($npmOut) | ForEach-Object { "$_" }) -join "`n")"
        }
        Write-MusicHubInfo '  dashboard built'
    }
    else {
        Write-MusicHubWarning 'npm was not found; using the existing dist\ if present.'
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $distSource 'index.html'))) {
    throw "The dashboard bundle is missing at $distSource (no index.html). Run 'npm --prefix src/web/admin run build' first, or omit -SkipWebBuild."
}

New-Dir $wwwroot | Out-Null
New-Dir $wwwrootPublish | Out-Null
Copy-Item -Path (Join-Path $distSource '*') -Destination $wwwroot -Recurse -Force
Copy-Item -Path (Join-Path $distSource '*') -Destination $wwwrootPublish -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $wwwroot 'index.html'))) {
    throw "Copying the dashboard to wwwroot did not produce index.html."
}
Write-MusicHubInfo "  wwwroot: $((Get-ChildItem -LiteralPath $wwwroot -Recurse -File).Count) file(s) (project + publish)"

# The hosts are started with their working directory set to the project directory, so that directory
# is the content root. appsettings.json and appsettings.<Environment>.json are read from there, not
# from the publish folder, so copy the project's own settings next to the assemblies.
foreach ($project in 'MusicHub.Api', 'MusicHub.Worker') {
    $projectDir = Join-Path $repoRoot "src\server\$project"
    $stagedProjectDir = New-Dir (Join-Path $staging "src\server\$project")
    Get-ChildItem -LiteralPath $projectDir -File -Filter 'appsettings*.json' -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stagedProjectDir -Force }
}
Write-MusicHubInfo '  appsettings.json staged beside each host project'

# ---------------------------------------------------------------------------------------------
# 4. Private .NET runtime
# ---------------------------------------------------------------------------------------------
# The hosts are published framework-dependent: they need a runtime, not an SDK. Shipping the runtime
# privately means the operator does not install .NET system-wide and the package cannot be broken by
# a later SDK upgrade or removal.
Write-Phase '4/8 Private .NET runtime'

$runtimeDir = New-Dir (Join-Path $staging 'runtime')
$dotnetHome = Split-Path -Parent $dotnet
$sharedSource = Join-Path $dotnetHome 'shared'
$hostSource = Join-Path $dotnetHome 'host'

if (-not (Test-Path -LiteralPath $sharedSource)) {
    throw "The .NET shared runtime was not found at $sharedSource. Install the .NET 10 runtime on this build machine."
}

Copy-Item -LiteralPath (Join-Path $dotnetHome 'dotnet.exe') -Destination $runtimeDir -Force
foreach ($package in 'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App') {
    $source = Join-Path $sharedSource $package
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing runtime package: $source" }
    $latest = Get-ChildItem -LiteralPath $source -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $target = New-Dir (Join-Path $runtimeDir "shared\$package\$($latest.Name)")
    Copy-Item -Path (Join-Path $latest.FullName '*') -Destination $target -Recurse -Force
    Write-MusicHubInfo "  $package $($latest.Name)"
}

# host\fxr is what makes `dotnet <dll>` resolve the framework; host\fxr and the shared folders must
# agree on a version or the host refuses to start.
$fxrSource = Join-Path $hostSource 'fxr'
if (Test-Path -LiteralPath $fxrSource) {
    $latestFxr = Get-ChildItem -LiteralPath $fxrSource -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $target = New-Dir (Join-Path $runtimeDir "host\fxr\$($latestFxr.Name)")
    Copy-Item -Path (Join-Path $latestFxr.FullName '*') -Destination $target -Recurse -Force
    Write-MusicHubInfo "  host\fxr $($latestFxr.Name)"
}
else {
    throw "host\fxr was not found at $fxrSource; the private runtime would not start."
}

Write-MusicHubInfo ("  runtime total: {0:N1} MB" -f ((Get-TreeSize $runtimeDir) / 1MB))

# ---------------------------------------------------------------------------------------------
# 5. PostgreSQL and ffmpeg
# ---------------------------------------------------------------------------------------------
Write-Phase '5/8 PostgreSQL 17 and ffmpeg'

$pgSource = Join-Path $repoRoot 'tools\pgsql'
if (-not (Test-Path -LiteralPath (Join-Path $pgSource 'bin\pg_ctl.exe'))) {
    throw "Portable PostgreSQL 17 was not found at $pgSource."
}
$pgTarget = New-Dir (Join-Path $staging 'tools\pgsql')
foreach ($part in 'bin', 'lib', 'share') {
    $source = Join-Path $pgSource $part
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing PostgreSQL directory: $source" }
    Copy-Item -LiteralPath $source -Destination $pgTarget -Recurse -Force
}
# pgAdmin 4, StackBuilder, the documentation and the C headers are not needed by a server install.
Write-MusicHubInfo ("  pgsql: {0:N1} MB (bin, lib, share only)" -f ((Get-TreeSize $pgTarget) / 1MB))

$ffprobe = Get-ChildItem -Path (Join-Path $repoRoot 'tools\ffmpeg') -Recurse -Filter 'ffprobe.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $ffprobe) { throw "ffprobe.exe was not found under tools\ffmpeg." }
$ffBinSource = $ffprobe.DirectoryName
$ffTarget = New-Dir (Join-Path $staging 'tools\ffmpeg\bin')
foreach ($exe in 'ffmpeg.exe', 'ffprobe.exe') {
    $source = Join-Path $ffBinSource $exe
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source" }
    Copy-Item -LiteralPath $source -Destination $ffTarget -Force
}
# ffplay is an SDL media player; the server never starts it, and it is 102 MB.
Write-MusicHubInfo ("  ffmpeg: {0:N1} MB (ffmpeg + ffprobe only)" -f ((Get-TreeSize $ffTarget) / 1MB))

# ---------------------------------------------------------------------------------------------
# 6. Scripts
# ---------------------------------------------------------------------------------------------
Write-Phase '6/8 Scripts'

$scriptsTarget = New-Dir (Join-Path $staging 'deploy\scripts')
Copy-Item -Path (Join-Path $repoRoot 'deploy\scripts\*.ps1') -Destination $scriptsTarget -Force
Write-MusicHubInfo "  deploy\scripts: $((Get-ChildItem -LiteralPath $scriptsTarget -Filter '*.ps1').Count) file(s)"

$windowsTarget = New-Dir (Join-Path $staging 'deploy\windows')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'supervisor.ps1') -Destination $windowsTarget -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $windowsTarget -Force
Write-MusicHubInfo '  deploy\windows: supervisor + install entry point'

# The supervisor expects storage\run; the scripts expect a storage root. Create them so a fresh
# extraction has the expected shape before the installer runs.
New-Dir (Join-Path $staging 'storage\run') | Out-Null
New-Dir (Join-Path $staging 'storage\logs') | Out-Null

# ---------------------------------------------------------------------------------------------
# 7. Manifest
# ---------------------------------------------------------------------------------------------
Write-Phase '7/8 Manifest'

$manifest = [ordered]@{
    product          = 'MusicHub Enterprise'
    component        = 'server'
    version          = $Version
    platform         = 'win-x64'
    builtAtUtc       = [DateTimeOffset]::UtcNow.ToString('o')
    runtime          = 'Microsoft.NETCore.App 10 / Microsoft.AspNetCore.App 10 (private, framework-dependent hosts)'
    postgres         = '17 (portable, trimmed to bin/lib/share)'
    nodeModules      = 'excluded'
    dotnetSdk        = 'excluded'
    installCommand   = 'powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1'
    uninstallCommand = 'powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Uninstall'
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'package.json') -Encoding UTF8
Write-MusicHubInfo '  package.json written'

$readme = @"
MusicHub Enterprise - Windows server package $Version
=====================================================

Install (no .NET SDK, no Docker, no network required). Run this from the folder this file is in:

    powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1

The installer detects this machine's LAN address, asks which port the API should listen on, creates
the PostgreSQL database, generates the secrets, starts PostgreSQL + the API + the Worker (which runs
the Telegram bot), verifies the stack over HTTP, registers the "MusicHub Server" Scheduled Task so the
stack starts at logon, and prints the dashboard URL.

Useful afterwards:

    powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Status
    powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Start
    powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Stop
    powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Uninstall

Everything mutable lives under .\storage : the database cluster, uploaded music, artwork, the data
protection key ring, logs, and the generated secrets (storage\dev.secrets.ps1). Back up that folder.

The package ships a private .NET runtime under .\runtime and portable PostgreSQL under
.\tools\pgsql, so nothing has to be installed system-wide. It also does not need the network: only
the Telegram bot needs outbound HTTPS, and only once you provision a bot token.

Do not move or rename this directory after installing: the Scheduled Task and the database cluster
both record absolute paths.
"@
Set-Content -LiteralPath (Join-Path $staging 'README.txt') -Value $readme -Encoding UTF8
Write-MusicHubInfo '  README.txt written'

# ---------------------------------------------------------------------------------------------
# 8. Zip
# ---------------------------------------------------------------------------------------------
Write-Phase '8/8 Package'

$totalBytes = Get-TreeSize $staging
Write-MusicHubInfo ("Staged size: {0:N1} MB" -f ($totalBytes / 1MB))

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

# Compress-Archive is deliberately avoided for a payload this size: it buffers in memory and is slow
# for hundreds of megabytes. ZipFile streams to disk.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $staging,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

$zipInfo = Get-Item -LiteralPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zipPath.sha256" -Value $hash -Encoding ASCII

Write-Host ''
Write-Host ("Package: {0}" -f $zipPath) -ForegroundColor Green
Write-Host ("Size:    {0:N1} MB" -f ($zipInfo.Length / 1MB))
Write-Host ("SHA256:  {0}" -f $hash)

if (-not $KeepStaging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
    Write-MusicHubInfo 'Staging directory removed.'
}
else {
    Write-MusicHubInfo "Staging kept at $staging"
}

exit 0
