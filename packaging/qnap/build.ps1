<#
.SYNOPSIS
    Builds Arcanist's QNAP packages (.qpkg, QTS and QuTS hero 5.0 and later) from this checkout, with Docker as the
    only requirement.

.DESCRIPTION
    Every step runs in a container (see packaging/build-steps.ps1): native libraries, admin panel and the
    self-contained server for Linux, then packaging/qnap/qpkg.sh in the image of builder.Dockerfile (QNAP's QDK), which
    adds Debian 12's C library (QTS's is too old for .NET) and leaves artifacts/qnap/Arcanist_<version>_<arch>.qpkg.

    Install it in App Center → Install Manually (it is not signed by QNAP: accept the warning).

.EXAMPLE
    pwsh packaging/qnap/build.ps1                    # x86_64
.EXAMPLE
    pwsh packaging/qnap/build.ps1 -Arch all -SkipNative -SkipPanel
#>
[CmdletBinding()]
param(
    # x64 (x86_64), arm64 (arm_64) or all.
    [ValidateSet('x64', 'arm64', 'all')]
    [string]$Arch = 'x64',

    # The version; by default, the one in Directory.Build.props.
    [string]$Version = '',

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
Write-Host "Arcanist $Version for QNAP ($($arches -join ', '))" -ForegroundColor Green

Build-Native $root $arches -Skip:$SkipNative
Build-Panel $root -Skip:$SkipPanel
$bundled = ''
if ($LocalWebamp) {
    if ($SkipServer) { throw '-LocalWebamp needs the server built (leave out -SkipServer).' }
    $bundled = Build-LocalWebamp $root $WebampPath
}
Build-Server $root $Version $arches @('self-contained') $bundled -Skip:$SkipServer

Write-Step 'QNAP packages'
Invoke-Docker build -q -t arcanist-qnap-builder -f (Join-Path $root 'packaging/qnap/builder.Dockerfile') (Join-Path $root 'packaging/qnap')
Invoke-Docker run --rm -v "${root}:/src" -w /src -e "ARCHES=$($arches -join ' ')" arcanist-qnap-builder sh packaging/qnap/qpkg.sh $Version

$elapsed = (Get-Date) - $started
Write-Host ''
Write-Host "Done in $([int]$elapsed.TotalMinutes) min $($elapsed.Seconds) s:" -ForegroundColor Green
Get-ChildItem (Join-Path $root 'artifacts/qnap') -Filter '*.qpkg' | ForEach-Object {
    Write-Host "  artifacts/qnap/$($_.Name)  ($([math]::Round($_.Length / 1MB, 1)) MB)"
}
Write-Host 'On the NAS: App Center → Install Manually, then open Arcanist from App Center.'
