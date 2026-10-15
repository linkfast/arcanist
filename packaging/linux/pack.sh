#!/usr/bin/env sh
# Builds self-contained tarballs: artifacts/linux/arcanist-<version>-linux-<arch>.tar.gz for x64 and arm64.
#
#   packaging/linux/pack.sh [version] [--skip-panel]
#
# Each one has the server (no .NET install needed), the audio analysis library, the admin panel, the systemd unit
# and the license.
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
    name="arcanist-$version-linux-$arch"
    dir="$out/$name"
    rm -rf "$dir"
    # Native libraries: audio analysis and P2P (need Rust and cargo-zigbuild, see packaging/native/build.sh).
    [ -f "artifacts/native/linux-$arch/libarcanist_audio.so" ] && [ -f "artifacts/native/linux-$arch/libarcanist_p2p.so" ] \
        || sh packaging/native/build.sh "linux-$arch"
    dotnet publish src/Arcanist.Core -c Release -r "linux-$arch" --self-contained -p:Version="$version" \
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
