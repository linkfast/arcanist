# Steps the packaging scripts share (synology/build.ps1, linux/build.ps1): each runs in a Docker container, so a
# machine with Docker Desktop needs nothing else. Caches live in Docker volumes (arcanist-*), so later builds are
# faster and nothing touches the checkout's node_modules, bin or obj. Dot-source it: . "$PSScriptRoot/../build-steps.ps1"

$ErrorActionPreference = 'Stop'

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed (exit code $LASTEXITCODE)." }
}

function Assert-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is needed: install Docker Desktop (https://www.docker.com/products/docker-desktop/) and start it.'
    }
    $os = & docker info --format '{{.OSType}}' 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Docker is not running: start Docker Desktop and try again.' }
    if ($os -ne 'linux') { throw 'Docker uses Windows containers: switch Docker Desktop to Linux containers.' }
}

function Get-ArcanistVersion([string]$root) {
    $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    if ($props -notmatch '<Version>([^<]+)</Version>') { throw 'No <Version> in Directory.Build.props.' }
    return $Matches[1]
}

# The native libraries for linux-<arch> into artifacts/native (Rust and cargo-zigbuild, in arcanist-native-builder).
function Build-Native([string]$root, [string[]]$arches, [switch]$Skip) {
    if (-not $Skip) {
        Write-Step 'Native libraries (Rust)'
        Invoke-Docker build --quiet -t arcanist-native-builder -f (Join-Path $root 'packaging/native/builder.Dockerfile') (Join-Path $root 'packaging/native') | Out-Null
        foreach ($a in $arches) {
            Invoke-Docker run --rm -v "${root}:/src" -w /src `
                -v arcanist-cargo-registry:/usr/local/cargo/registry `
                -v arcanist-native-target:/target -e CARGO_TARGET_DIR=/target `
                arcanist-native-builder sh packaging/native/build.sh "linux-$a"
        }
    }
    foreach ($a in $arches) {
        foreach ($lib in 'libarcanist_audio.so', 'libarcanist_p2p.so', 'libarcanist_player.so') {
            if (-not (Test-Path (Join-Path $root "artifacts/native/linux-$a/$lib"))) { throw "Missing artifacts/native/linux-$a/${lib}: build without -SkipNative." }
        }
    }
}

# The admin panel into src/Arcanist.Web/dist (Node 24; its node_modules and Angular's cache stay in volumes).
function Build-Panel([string]$root, [switch]$Skip) {
    if (-not $Skip) {
        Write-Step 'Admin panel (Node 24)'
        Invoke-Docker run --rm -v "${root}:/src" -w /src/src/Arcanist.Web `
            -v arcanist-web-node-modules:/src/src/Arcanist.Web/node_modules `
            -v arcanist-web-angular-cache:/src/src/Arcanist.Web/.angular `
            -v arcanist-npm-cache:/root/.npm `
            node:24.15.0-bookworm sh -c 'npm ci --no-audit --no-fund && npm run build'
    }
    if (-not (Test-Path (Join-Path $root 'src/Arcanist.Web/dist/arcanist-web/browser/index.html'))) {
        throw 'The admin panel is not built: build without -SkipPanel.'
    }
}

# Webamp built from a local checkout (its own scripts/pack.ps1), unpacked into artifacts/players-local/webamp.
# Returns the folder as the server's build container sees it, for Build-Server.
function Build-LocalWebamp([string]$root, [string]$webampPath) {
    if (-not $webampPath) { $webampPath = Join-Path $root '../ArcanistWebPlayers/webamp' }
    $packScript = Join-Path $webampPath 'packages/arcanist-webamp/scripts/pack.ps1'
    if (-not (Test-Path $packScript)) { throw "No Webamp checkout at $webampPath (or no scripts/pack.ps1 in it): pass -WebampPath." }
    Write-Step "Webamp ($((Resolve-Path $webampPath).Path))"
    & $packScript | Out-Host
    $version = (Get-Content (Join-Path $webampPath 'packages/arcanist-webamp/package.json') -Raw | ConvertFrom-Json).version
    $players = Join-Path $root 'artifacts/players-local'
    Remove-Item -Recurse -Force $players -ErrorAction SilentlyContinue
    Expand-Archive (Join-Path $webampPath "packages/arcanist-webamp/release/arcanist-webamp-$version.zip") -DestinationPath (Join-Path $players 'webamp')
    return '/src/artifacts/players-local'
}

# The server builds into artifacts/linux (pack.sh in .NET's SDK): flavors "self-contained" and/or "dotnet".
function Build-Server([string]$root, [string]$version, [string[]]$arches, [string[]]$flavors, [string]$bundled, [switch]$Skip) {
    if (-not $Skip) {
        Write-Step "Server (.NET 10, $($flavors -join ' and '))"
        Invoke-Docker run --rm -v "${root}:/src" -w /src `
            -v arcanist-dotnet-build:/build -v arcanist-nuget:/root/.nuget/packages `
            -e "ARCHES=$($arches -join ' ')" -e "FLAVORS=$($flavors -join ' ')" -e ARTIFACTS_PATH=/build `
            -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e "BUNDLED_PLAYERS_DIRECTORY=$bundled" `
            mcr.microsoft.com/dotnet/sdk:10.0 sh packaging/linux/pack.sh $version --skip-panel
    }
    foreach ($a in $arches) {
        foreach ($flavor in $flavors) {
            $name = if ($flavor -eq 'dotnet') { "arcanist-$version-linux-$a-dotnet.tar.gz" } else { "arcanist-$version-linux-$a.tar.gz" }
            if (-not (Test-Path (Join-Path $root "artifacts/linux/$name"))) { throw "Missing artifacts/linux/${name}: build without -SkipServer." }
        }
    }
}
