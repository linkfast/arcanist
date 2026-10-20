<#
.SYNOPSIS
    Builds the Windows installer: Velopack Setup.exe plus the update packages for GitHub Releases.

.DESCRIPTION
    The package's main executable is the tray app (Arcanist.Tray.exe); the server is published self-contained next
    to it in server\, with the admin panel in server\wwwroot\admin. Both are self-contained (win-x64 or win-arm64)
    and need no installed runtime.

    Velopack installs per user into %LocalAppData%\Arcanist without elevation. The data directory is
    %ProgramData%\Arcanist, shared with the optional Windows service.

.PARAMETER Version
    SemVer 2 version; defaults to <Version> in Directory.Build.props.

.PARAMETER SkipPanel
    Reuse an admin panel already built in src\Arcanist.Web\dist (packaging/build-panel.sh).

.PARAMETER Runtime
    win-x64 (the default) or win-arm64. For win-arm64 the native libraries must be built first, on any Windows
    machine with Rust: `packaging/native/build.sh win-arm64` (artifacts\native\win-arm64).

.PARAMETER Channel
    Velopack's update channel: installs only update from packages of their own channel. By default `win` for x64
    (what the first releases used) and `win-arm64`; edge builds use their own (`edge`, `edge-arm64`).

.EXAMPLE
    ./packaging/windows/pack.ps1 -Version 0.2.0
    ./packaging/windows/pack.ps1 -Version 0.2.0 -Runtime win-arm64
#>
param(
    [string] $Version,
    [string] $Output = "artifacts",
    [switch] $SkipPanel,
    [ValidateSet("win-x64", "win-arm64")] [string] $Runtime = "win-x64",
    [string] $Channel
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$root = Resolve-Path "$PSScriptRoot/../.."
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

if (-not $Channel) { $Channel = if ($Runtime -eq "win-x64") { "win" } else { $Runtime } }

$app = Join-Path $Output "windows/app-$Runtime"
$releases = Join-Path $Output "windows/releases"
Remove-Item $app -Recurse -Force -ErrorAction SilentlyContinue

if (-not $SkipPanel) {
    # Same as packaging/build-panel.sh: mise pins Node when available.
    Push-Location src/Arcanist.Web
    try {
        if (Get-Command mise -ErrorAction SilentlyContinue) { mise exec -- npm ci; mise exec -- npm run build }
        else { npm ci; npm run build }
    }
    finally { Pop-Location }
}
$panel = "src/Arcanist.Web/dist/arcanist-web/browser"
if (-not (Test-Path "$panel/index.html")) { throw "The admin panel is not built ($panel)" }

# On the build machine's own platform this also builds the native libraries with cargo; for another one they come
# from artifacts/native/<runtime> (packaging/native/build.sh).
dotnet publish src/Arcanist.Core -c Release -r $Runtime --self-contained -p:Version=$Version -o "$app/server"
foreach ($library in "arcanist_audio.dll", "arcanist_p2p.dll") {
    if (-not (Test-Path "$app/server/$library")) { throw "$library was not built (is Rust installed?)" }
}
Copy-Item $panel "$app/server/wwwroot/admin" -Recurse

# The tray also builds for macOS (net10.0): this is its Windows build.
dotnet publish src/Arcanist.Tray -f net10.0-windows -c Release -r $Runtime --self-contained -p:Version=$Version -o $app
# Debug symbols, mostly native ones from SkiaSharp and HarfBuzz, would make up half of the download.
Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item

dotnet tool restore
dotnet vpk pack `
    --packId Arcanist `
    --packVersion $Version `
    --packDir $app `
    --mainExe Arcanist.Tray.exe `
    --runtime $Runtime `
    --channel $Channel `
    --packTitle Arcanist `
    --packAuthors "LinkFast S.A." `
    --icon src/Arcanist.Tray/Assets/arcanist.ico `
    --shortcuts "Desktop,StartMenuRoot" `
    --outputDir $releases

Write-Host "Installer and update packages in $releases"
