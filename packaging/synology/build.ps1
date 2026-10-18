<#
.SYNOPSIS
    Builds Arcanist's Synology package (.spk) from this checkout, with Docker as the only requirement.

.DESCRIPTION
    Every step runs in a container, so the machine needs Docker (Docker Desktop with Linux containers on Windows) and
    nothing else: no Rust, Node, .NET or Git Bash. Caches live in Docker volumes (arcanist-*), so later builds are
    much faster, and nothing touches the checkout's node_modules, bin or obj.

      1. Native libraries for linux-<arch> (Rust, cargo-zigbuild; image arcanist-native-builder, built once).
      2. The admin panel (Node 24).
      3. The server for systems with .NET (artifacts/linux/arcanist-<version>-linux-<arch>-dotnet.tar.gz).
      4. The package with SynoCommunity's spksrc (cloned once into artifacts/spksrc; its first build downloads the
         DSM toolchain, several GB).

    The result is artifacts/synology/arcanist_<x64|aarch64>-7.2_<version>-<revision>.spk: install it in Package
    Center with "Manual Install". Each build gets the next revision, so DSM installs it over the one before.

.EXAMPLE
    pwsh packaging/synology/build.ps1                 # x64 (Intel/AMD NAS)
.EXAMPLE
    pwsh packaging/synology/build.ps1 -Arch all       # x64 and ARMv8
.EXAMPLE
    pwsh packaging/synology/build.ps1 -SkipNative -SkipPanel   # reuse what the last build made
#>
[CmdletBinding()]
param(
    # The NAS's processor: x64 (Intel or AMD), arm64 (ARMv8, such as the Realtek RTD1619B), or all.
    [ValidateSet('x64', 'arm64', 'all')]
    [string]$Arch = 'x64',

    # The package revision; by default, one more than the last one built here for this version.
    [int]$Revision = 0,

    # The version; by default, the one in Directory.Build.props.
    [string]$Version = '',

    # Reuse the native libraries already in artifacts/native.
    [switch]$SkipNative,

    # Reuse the admin panel already built in src/Arcanist.Web/dist.
    [switch]$SkipPanel,

    # Reuse the server tarballs already in artifacts/linux.
    [switch]$SkipServer
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$started = Get-Date

function Step([string]$text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed (exit code $LASTEXITCODE)." }
}

# Docker, running, with Linux containers.
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is needed: install Docker Desktop (https://www.docker.com/products/docker-desktop/) and start it.'
}
$os = & docker info --format '{{.OSType}}' 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Docker is not running: start Docker Desktop and try again.' }
if ($os -ne 'linux') { throw 'Docker uses Windows containers: switch Docker Desktop to Linux containers.' }

if (-not $Version) {
    $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    if ($props -notmatch '<Version>([^<]+)</Version>') { throw 'No <Version> in Directory.Build.props.' }
    $Version = $Matches[1]
}
$arches = if ($Arch -eq 'all') { @('x64', 'arm64') } else { @($Arch) }
$spkArch = @{ x64 = 'x64'; arm64 = 'aarch64' }
# Package versions have no pre-release suffix.
$spkVersion = ($Version -split '-')[0]

$synology = Join-Path $root 'artifacts/synology'
New-Item -ItemType Directory -Force $synology | Out-Null
if ($Revision -le 0) {
    $last = Get-ChildItem $synology -Filter "arcanist_*-7.2_$spkVersion-*.spk" -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Name -match '-(\d+)\.spk$') { [int]$Matches[1] } } |
        Measure-Object -Maximum
    $Revision = if ($last.Count -gt 0) { [int]$last.Maximum + 1 } else { 1 }
}

Write-Host "Arcanist $Version for Synology ($($arches -join ', ')), package revision $Revision" -ForegroundColor Green
$mount = "${root}:/src"

# 1. Native libraries.
if (-not $SkipNative) {
    Step 'Native libraries (Rust)'
    Invoke-Docker build --quiet -t arcanist-native-builder -f (Join-Path $root 'packaging/native/builder.Dockerfile') (Join-Path $root 'packaging/native') | Out-Null
    foreach ($a in $arches) {
        Invoke-Docker run --rm -v $mount -w /src `
            -v arcanist-cargo-registry:/usr/local/cargo/registry `
            -v "arcanist-native-target:/target" -e CARGO_TARGET_DIR=/target `
            arcanist-native-builder sh packaging/native/build.sh "linux-$a"
    }
}
foreach ($a in $arches) {
    foreach ($lib in 'libarcanist_audio.so', 'libarcanist_p2p.so', 'libarcanist_player.so') {
        if (-not (Test-Path (Join-Path $root "artifacts/native/linux-$a/$lib"))) { throw "Missing artifacts/native/linux-$a/${lib}: build without -SkipNative." }
    }
}

# 2. The admin panel. Its node_modules and Angular's cache stay in volumes: the checkout's are Windows' own.
if (-not $SkipPanel) {
    Step 'Admin panel (Node 24)'
    Invoke-Docker run --rm -v $mount -w /src/src/Arcanist.Web `
        -v arcanist-web-node-modules:/src/src/Arcanist.Web/node_modules `
        -v arcanist-web-angular-cache:/src/src/Arcanist.Web/.angular `
        -v arcanist-npm-cache:/root/.npm `
        node:24.15.0-bookworm sh -c 'npm ci --no-audit --no-fund && npm run build'
}
if (-not (Test-Path (Join-Path $root 'src/Arcanist.Web/dist/arcanist-web/browser/index.html'))) {
    throw 'The admin panel is not built: build without -SkipPanel.'
}

# 3. The server for systems with .NET (what the package runs with SynoCommunity's dotnet10-runtime).
if (-not $SkipServer) {
    Step 'Server (.NET 10)'
    Invoke-Docker run --rm -v $mount -w /src `
        -v arcanist-dotnet-build:/build -v arcanist-nuget:/root/.nuget/packages `
        -e "ARCHES=$($arches -join ' ')" -e FLAVORS=dotnet -e ARTIFACTS_PATH=/build -e DOTNET_CLI_TELEMETRY_OPTOUT=1 `
        mcr.microsoft.com/dotnet/sdk:10.0 sh packaging/linux/pack.sh $Version --skip-panel
}
foreach ($a in $arches) {
    if (-not (Test-Path (Join-Path $root "artifacts/linux/arcanist-$Version-linux-$a-dotnet.tar.gz"))) {
        throw "Missing artifacts/linux/arcanist-$Version-linux-$a-dotnet.tar.gz: build without -SkipServer."
    }
}

# 4. The package, with spksrc.
Step 'Synology package (spksrc)'
$spksrc = Join-Path $root 'artifacts/spksrc'
if (-not (Test-Path (Join-Path $spksrc '.git'))) {
    # Without line ending conversion: spksrc's checksum files would not be read.
    Invoke-Docker run --rm -v "$(Join-Path $root 'artifacts'):/a" alpine/git -c core.autocrlf=false clone --depth 1 https://github.com/SynoCommunity/spksrc.git /a/spksrc
}
foreach ($path in 'cross/arcanist', 'spk/arcanist', 'cross/arcanist/digests') {
    Remove-Item -Recurse -Force (Join-Path $spksrc $path) -ErrorAction SilentlyContinue
}
Get-ChildItem $spksrc -Directory -Filter 'work-*' -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter 'arcanist*' } | Remove-Item -Recurse -Force
Remove-Item (Join-Path $spksrc 'packages/arcanist_*') -Force -ErrorAction SilentlyContinue
Copy-Item -Recurse (Join-Path $root 'packaging/synology/spksrc/cross/arcanist') (Join-Path $spksrc 'cross/arcanist')
Copy-Item -Recurse (Join-Path $root 'packaging/synology/spksrc/spk/arcanist') (Join-Path $spksrc 'spk/arcanist')

# This version and revision, from the local tarballs instead of the release's (LF line endings, as make wants).
function Set-MakeVariable([string]$file, [string]$name, [string]$value) {
    $text = [IO.File]::ReadAllText($file) -replace "(?m)^$name = .*$", "$name = $value"
    [IO.File]::WriteAllText($file, $text.Replace("`r`n", "`n"))
}
Set-MakeVariable (Join-Path $spksrc 'cross/arcanist/Makefile') 'PKG_VERS' $Version
Set-MakeVariable (Join-Path $spksrc 'spk/arcanist/Makefile') 'SPK_VERS' $spkVersion
Set-MakeVariable (Join-Path $spksrc 'spk/arcanist/Makefile') 'SPK_REV' "$Revision"
New-Item -ItemType Directory -Force (Join-Path $spksrc 'distrib') | Out-Null
foreach ($a in $arches) {
    Copy-Item (Join-Path $root "artifacts/linux/arcanist-$Version-linux-$a-dotnet.tar.gz") (Join-Path $spksrc 'distrib') -Force
}

$targets = ($arches | ForEach-Object { "arch-$($spkArch[$_])-7.2" }) -join ' '
Invoke-Docker run --rm --platform linux/amd64 -v "${spksrc}:/spksrc" -w /spksrc ghcr.io/synocommunity/spksrc `
    sh -c "make -C cross/arcanist digests && make -C spk/arcanist $targets"

$built = Get-ChildItem (Join-Path $spksrc 'packages') -Filter 'arcanist_*.spk'
if (-not $built) { throw 'spksrc built no package: see its output above.' }
foreach ($package in $built) { Copy-Item $package.FullName $synology -Force }

$elapsed = (Get-Date) - $started
Write-Host ''
Write-Host "Done in $([int]$elapsed.TotalMinutes) min $($elapsed.Seconds) s:" -ForegroundColor Green
$built | ForEach-Object { Write-Host "  artifacts/synology/$($_.Name)  ($([math]::Round($_.Length / 1MB, 1)) MB)" }
Write-Host 'Install it in Package Center > Manual Install.'
