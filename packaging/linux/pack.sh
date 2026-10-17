#!/usr/bin/env sh
# Builds tarballs for x64 and arm64 in artifacts/linux:
#
#   arcanist-<version>-linux-<arch>.tar.gz          self-contained: no .NET install needed;
#   arcanist-<version>-linux-<arch>-dotnet.tar.gz   for systems with the ASP.NET Core 10 runtime, smaller (run it with
#                                                   `dotnet arcanist.dll`); the Synology package uses it, with
#                                                   SynoCommunity's dotnet10-runtime package.
#
#   packaging/linux/pack.sh [version] [--skip-panel]
#
# Each one has the server, its native libraries, the admin panel, the systemd unit and the license.
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)}"
[ "${2:-}" = "--skip-panel" ] || sh packaging/build-panel.sh
panel="src/Arcanist.Web/dist/arcanist-web/browser"
[ -f "$panel/index.html" ] || { echo "The admin panel is not built ($panel)" >&2; exit 1; }

out="artifacts/linux"
mkdir -p "$out"
for arch in x64 arm64; do
    # Native libraries: audio analysis, P2P and the server audio player (need Rust and cargo-zigbuild, see
    # packaging/native/build.sh). The player uses the system's libasound (ALSA).
    [ -f "artifacts/native/linux-$arch/libarcanist_audio.so" ] && [ -f "artifacts/native/linux-$arch/libarcanist_p2p.so" ] \
        && [ -f "artifacts/native/linux-$arch/libarcanist_player.so" ] || sh packaging/native/build.sh "linux-$arch"

    for flavor in self-contained dotnet; do
        if [ "$flavor" = dotnet ]; then
            name="arcanist-$version-linux-$arch-dotnet"
            contained=false
        else
            name="arcanist-$version-linux-$arch"
            contained=true
        fi
        dir="$out/$name"
        rm -rf "$dir"
        dotnet publish src/Arcanist.Core -c Release -r "linux-$arch" --self-contained "$contained" -p:Version="$version" \
            -p:SkipNativeBuild=true -o "$dir"
        # Debug symbols, mostly native ones from SkiaSharp, would double the download.
        find "$dir" -name '*.pdb' -delete
        mkdir -p "$dir/wwwroot"
        cp -R "$panel" "$dir/wwwroot/admin"
        cp packaging/linux/arcanist.service LICENSE THIRD-PARTY-NOTICES.md "$dir/"
        chmod +x "$dir/arcanist"
        tar -czf "$out/$name.tar.gz" -C "$out" "$name"
        echo "$out/$name.tar.gz"
    done
done
