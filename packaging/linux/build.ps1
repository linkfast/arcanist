<#
.SYNOPSIS
    Builds Arcanist for Linux from this checkout: the Debian/Ubuntu package (.deb) and the tarballs, with Docker as
    the only requirement.

.DESCRIPTION
    Every step runs in a container (see packaging/build-steps.ps1): native libraries, admin panel, the self-contained
    server (artifacts/linux/arcanist-<version>-linux-<arch>.tar.gz) and the package for Ubuntu 24.04 and later and
    Debian 12 and later (artifacts/linux/arcanist_<version>-1_<amd64|arm64>.deb).

    Install the .deb on the machine with:  sudo apt install ./arcanist_<version>-1_amd64.deb

.EXAMPLE
    pwsh packaging/linux/build.ps1                     # amd64
.EXAMPLE
    pwsh packaging/linux/build.ps1 -Arch all -Dotnet   # amd64 and arm64, also the tarballs for systems with .NET
#>
[CmdletBinding()]
param(
    # x64 (amd64), arm64 or all.
    [ValidateSet('x64', 'arm64', 'all')]
    [string]$Arch = 'x64',

    # The version; by default, the one in Directory.Build.props.
    [string]$Version = '',

    # Also build the tarballs for systems with the ASP.NET Core 10 runtime (…-dotnet.tar.gz).
    [switch]$Dotnet,

    # Reuse what the last build left.
    [switch]$SkipNative,
    [switch]$SkipPanel,
    [switch]$SkipServer,

    # Bundle Webamp built from a local checkout (../ArcanistWebPlayers/webamp, or -WebampPath) instead of its release.
    [switch]$LocalWebamp,
    [string]$WebampPath = ''
)

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'packaging/build-steps.ps1')
$started = Get-Date
Assert-Docker

if (-not $Version) { $Version = Get-ArcanistVersion $root }
$arches = if ($Arch -eq 'all') { @('x64', 'arm64') } else { @($Arch) }
$flavors = @('self-contained') + $(if ($Dotnet) { @('dotnet') } else { @() })
Write-Host "Arcanist $Version for Linux ($($arches -join ', '))" -ForegroundColor Green

Build-Native $root $arches -Skip:$SkipNative
Build-Panel $root -Skip:$SkipPanel
$bundled = ''
if ($LocalWebamp) {
    if ($SkipServer) { throw '-LocalWebamp needs the server built (leave out -SkipServer).' }
    $bundled = Build-LocalWebamp $root $WebampPath
}
Build-Server $root $Version $arches $flavors $bundled -Skip:$SkipServer

Write-Step 'Debian package'
Invoke-Docker run --rm -v "${root}:/src" -w /src -e "ARCHES=$($arches -join ' ')" debian:12-slim sh packaging/linux/deb.sh $Version

$elapsed = (Get-Date) - $started
Write-Host ''
Write-Host "Done in $([int]$elapsed.TotalMinutes) min $($elapsed.Seconds) s:" -ForegroundColor Green
$debVersion = ($Version -replace '-', '~') + '-1'
foreach ($a in $arches) {
    $debArch = if ($a -eq 'x64') { 'amd64' } else { 'arm64' }
    $deb = Get-Item (Join-Path $root "artifacts/linux/arcanist_${debVersion}_$debArch.deb")
    Write-Host "  artifacts/linux/$($deb.Name)  ($([math]::Round($deb.Length / 1MB, 1)) MB)"
    Write-Host "  artifacts/linux/arcanist-$Version-linux-$a.tar.gz"
}
Write-Host 'On the server: sudo apt install ./<the .deb>, then open http://<server>:8484/'
