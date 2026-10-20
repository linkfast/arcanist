<#
.SYNOPSIS
    Builds Arcanist's Docker image from this checkout, and optionally starts it or publishes it.

.DESCRIPTION
    Everything (admin panel, native libraries, server) is built inside Docker by the repository's Dockerfile, so the
    machine needs Docker Desktop (Linux containers) and nothing else.

      pwsh packaging/docker/build.ps1                          # image arcanist:local, for this machine
      pwsh packaging/docker/build.ps1 -Up -MusicDir D:\Music   # ...and start it with Compose (http://localhost:8484)
      pwsh packaging/docker/build.ps1 -Push -Tag test-ana      # ghcr.io/linkfast/arcanist:test-ana, amd64 and arm64

    Publishing needs a `docker login ghcr.io` first, with a GitHub token that can write packages (write:packages).

.EXAMPLE
    pwsh packaging/docker/build.ps1 -LocalWebamp -Up -MusicDir D:\Music
#>
[CmdletBinding()]
param(
    # The image's tag.
    [string]$Tag = 'local',

    # Publish to GitHub's registry (ghcr.io/linkfast/arcanist:<Tag>, or -Repository), for amd64 and arm64.
    [switch]$Push,

    # The image's name when published.
    [string]$Repository = 'ghcr.io/linkfast/arcanist',

    # Architectures to build without -Push (one, loaded into this machine's Docker): linux/amd64 or linux/arm64.
    [ValidateSet('linux/amd64', 'linux/arm64')]
    [string]$Platform = 'linux/amd64',

    # Bundle Webamp built from a local checkout (../ArcanistWebPlayers/webamp, or -WebampPath) instead of its release.
    [switch]$LocalWebamp,
    [string]$WebampPath = '',

    # Start it afterwards with packaging/docker/compose.yaml.
    [switch]$Up,

    # With -Up: the music folder to give it (as /music, read-only).
    [string]$MusicDir = '',

    # With -Up: the port on this machine.
    [int]$Port = 8484
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$started = Get-Date

function Invoke-Checked {
    $command, $rest = $args
    & $command @rest
    if ($LASTEXITCODE -ne 0) { throw "$($args -join ' ') failed (exit code $LASTEXITCODE)." }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is needed: install Docker Desktop (https://www.docker.com/products/docker-desktop/) and start it.'
}
$os = & docker info --format '{{.OSType}}' 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Docker is not running: start Docker Desktop and try again.' }
if ($os -ne 'linux') { throw 'Docker uses Windows containers: switch Docker Desktop to Linux containers.' }
if ($Up -and $Push) { throw '-Up starts a local image: leave out -Push (or start the published one with Compose).' }
if ($Up -and -not $MusicDir) { throw '-Up needs -MusicDir, the folder of your music.' }

$image = if ($Push) { "${Repository}:$Tag" } else { "arcanist:$Tag" }
$arguments = @('buildx', 'build', '-t', $image, '-f', (Join-Path $root 'Dockerfile'))

# The version the server reports: the project's, marked as a local build.
$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = if ($props -match '<Version>([^<]+)</Version>') { $Matches[1] } else { '0.0.0' }
$commit = (& git -C $root rev-parse --short HEAD 2>$null)
$suffix = if ($commit) { "local.$commit" } else { 'local' }
$arguments += @('--build-arg', "VERSION=$(if ($version.Contains('-')) { "$version.$suffix" } else { "$version-$suffix" })")

if ($LocalWebamp) {
    if (-not $WebampPath) { $WebampPath = Join-Path $root '../ArcanistWebPlayers/webamp' }
    $packScript = Join-Path $WebampPath 'packages/arcanist-webamp/scripts/pack.ps1'
    if (-not (Test-Path $packScript)) { throw "No Webamp checkout at $WebampPath (or no scripts/pack.ps1 in it): pass -WebampPath." }
    Write-Host "==> Webamp ($((Resolve-Path $WebampPath).Path))" -ForegroundColor Cyan
    & $packScript
    $webampVersion = (Get-Content (Join-Path $WebampPath 'packages/arcanist-webamp/package.json') -Raw | ConvertFrom-Json).version
    $players = Join-Path $root 'artifacts/players-local'
    Remove-Item -Recurse -Force $players -ErrorAction SilentlyContinue
    Expand-Archive (Join-Path $WebampPath "packages/arcanist-webamp/release/arcanist-webamp-$webampVersion.zip") -DestinationPath (Join-Path $players 'webamp')
    $arguments += @('--build-context', "players=$players")
}

if ($Push) {
    # Both architectures, straight to the registry (a multi-architecture image cannot be loaded locally).
    $arguments += @('--platform', 'linux/amd64,linux/arm64', '--push')
}
else {
    $arguments += @('--platform', $Platform, '--load')
}
$arguments += $root

Write-Host "==> Image $image" -ForegroundColor Cyan
Invoke-Checked docker @arguments

if ($Up) {
    Write-Host '==> Starting it' -ForegroundColor Cyan
    $env:ARCANIST_IMAGE = 'arcanist'
    $env:ARCANIST_TAG = $Tag
    $env:MUSIC_DIR = (Resolve-Path $MusicDir).Path
    $env:ARCANIST_PORT = "$Port"
    Invoke-Checked docker compose -f (Join-Path $PSScriptRoot 'compose.yaml') up -d --force-recreate
    # The setup code, for setting it up from another machine (from this one, the wizard does not ask for it).
    $code = $null
    for ($i = 0; $i -lt 30 -and -not $code; $i++) {
        Start-Sleep -Seconds 1
        $code = & docker logs arcanist 2>&1 | Select-String -Pattern 'setup code' | Select-Object -Last 1
    }
    Write-Host ''
    Write-Host "Arcanist runs at http://localhost:$Port/" -ForegroundColor Green
    if ($code) { Write-Host "  $($code.Line.Trim())" }
    Write-Host '  Logs: docker logs -f arcanist    Stop: docker compose -f packaging/docker/compose.yaml down'
}

$elapsed = (Get-Date) - $started
Write-Host ''
Write-Host "Done in $([int]$elapsed.TotalMinutes) min $($elapsed.Seconds) s: $image" -ForegroundColor Green
if ($Push) { Write-Host "  Others pull it with: docker pull $image" }
