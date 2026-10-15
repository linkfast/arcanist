<#
.SYNOPSIS
    Builds the Windows installer: Velopack Setup.exe plus the update packages for GitHub Releases.

.DESCRIPTION
    The package's main executable is the tray app (Arcanist.Tray.exe); the server is published self-contained next
    to it in server\, with the admin panel in server\wwwroot\admin. Both are win-x64 and need no installed runtime.

    Velopack installs per user into %LocalAppData%\Arcanist without elevation. The data directory is
    %ProgramData%\Arcanist, shared with the optional Windows service.

.PARAMETER Version
    SemVer 2 version; defaults to <Version> in Directory.Build.props.

.PARAMETER SkipPanel
    Reuse an admin panel already built in src\Arcanist.Web\dist (packaging/build-panel.sh).

.EXAMPLE
    ./packaging/windows/pack.ps1 -Version 0.2.0
#>
param(
    [string] $Version,
    [string] $Output = "artifacts",
    [switch] $SkipPanel
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$root = Resolve-Path "$PSScriptRoot/../.."
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

$app = Join-Path $Output "windows/app"
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

# Also builds the native libraries with cargo (win-x64 is the build machine's own platform).
dotnet publish src/Arcanist.Core -c Release -r win-x64 --self-contained -p:Version=$Version -o "$app/server"
foreach ($library in "arcanist_audio.dll", "arcanist_p2p.dll") {
    if (-not (Test-Path "$app/server/$library")) { throw "$library was not built (is Rust installed?)" }
}
Copy-Item $panel "$app/server/wwwroot/admin" -Recurse

dotnet publish src/Arcanist.Tray -c Release -r win-x64 --self-contained -p:Version=$Version -o $app
# Debug symbols, mostly native ones from SkiaSharp and HarfBuzz, would make up half of the download.
Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item

dotnet tool restore
dotnet vpk pack `
    --packId Arcanist `
    --packVersion $Version `
    --packDir $app `
    --mainExe Arcanist.Tray.exe `
    --runtime win-x64 `
    --packTitle Arcanist `
    --packAuthors "Arcanist Contributors" `
    --icon src/Arcanist.Tray/Assets/arcanist.ico `
    --shortcuts "Desktop,StartMenuRoot" `
    --outputDir $releases

Write-Host "Installer and update packages in $releases"
