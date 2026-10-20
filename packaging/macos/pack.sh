#!/usr/bin/env sh
# Builds Arcanist for macOS (on a Mac, with Xcode's command line tools, .NET 10 and Rust):
#
#   packaging/macos/pack.sh <version> <arm64|x64> [channel] [--skip-panel]
#
# Into artifacts/macos/<arch>/:
# - Arcanist-<version>-macos-<arch>.dmg: Arcanist.app to drag to Applications (or run from anywhere: portable).
# - Arcanist-<version>-macos-<arch>.pkg: installs Arcanist.app in /Applications and opens it.
# - Arcanist-<version>-macos-<arch>.zip: the same Arcanist.app, zipped (MacPorts uses it).
# - arcanist-<version>-macos-<arch>.tar.gz: the server alone, for Macs without a session (see README).
# - releases/: Velopack's update packages for the channel (osx-<arch> by default; edge builds use edge-osx-<arch>).
#
# Arcanist.app is the menu bar app (src/Arcanist.Tray for net10.0): it runs the bundled server, opens at login and
# updates itself through Velopack. Without an Apple Developer ID the app is signed ad hoc, so Gatekeeper asks once
# before opening it (System Settings → Privacy & Security → Open Anyway).
set -eu
version="$1"
arch="$2"
channel="${3:-osx-$arch}"
skip_panel="${4:-}"
case "$arch" in
    arm64 | x64) rid="osx-$arch" ;;
    *) echo "arch must be arm64 or x64" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
out="artifacts/macos/$arch"
app="$out/app"
rm -rf "$out"
mkdir -p "$out"

if [ "$skip_panel" != "--skip-panel" ]; then
    sh packaging/build-panel.sh
fi
panel="src/Arcanist.Web/dist/arcanist-web/browser"
[ -f "$panel/index.html" ] || { echo "The admin panel is not built ($panel)" >&2; exit 1; }

# The native libraries for this architecture (cross-built for x64 on Apple Silicon), picked up by the publish.
sh packaging/native/build.sh "$rid"

dotnet publish src/Arcanist.Core -c Release -r "$rid" --self-contained -p:Version="$version" -p:SkipNativeBuild=true -o "$app/server"
for library in libarcanist_audio.dylib libarcanist_p2p.dylib; do
    [ -f "$app/server/$library" ] || { echo "$library was not built" >&2; exit 1; }
done
cp -R "$panel" "$app/server/wwwroot/admin"

dotnet publish src/Arcanist.Tray -f net10.0 -c Release -r "$rid" --self-contained -p:Version="$version" -o "$app"
find "$app" -name '*.pdb' -delete

# The icon, from the tray's 256 px PNG (macOS scales it up for the largest sizes).
iconset="$out/arcanist.iconset"
mkdir -p "$iconset"
for size in 16 32 64 128 256 512; do
    sips -z "$size" "$size" src/Arcanist.Tray/Assets/arcanist.png --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" src/Arcanist.Tray/Assets/arcanist.png --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$out/arcanist.icns"

# Velopack: the update packages, and the app bundle (in its portable zip) that the rest is made of.
dotnet tool restore
dotnet vpk pack \
    --packId Arcanist \
    --packVersion "$version" \
    --packDir "$app" \
    --mainExe Arcanist.Tray \
    --runtime "$rid" \
    --channel "$channel" \
    --packTitle Arcanist \
    --packAuthors "LinkFast S.A." \
    --bundleId net.linkfast.arcanist \
    --icon "$out/arcanist.icns" \
    --noInst \
    --outputDir "$out/releases"

bundle="$out/bundle"
mkdir -p "$bundle"
ditto -x -k "$out/releases/Arcanist-$channel-Portable.zip" "$bundle"
rm "$out/releases/Arcanist-$channel-Portable.zip"
[ -d "$bundle/Arcanist.app" ] || { echo "Velopack's portable zip has no Arcanist.app" >&2; exit 1; }
# Ad hoc, with no certificate: every binary (the server's included, outside the standard folders that --deep reaches,
# and Rust's x86_64 libraries, which the linker leaves unsigned), then the bundle, so Gatekeeper can ask instead of
# calling it damaged.
find "$bundle/Arcanist.app/Contents" -type f | while IFS= read -r file; do
    case "$(file -b "$file")" in
        *Mach-O*) codesign --force --sign - "$file" ;;
    esac
done
codesign --force --sign - "$bundle/Arcanist.app"
codesign --verify --deep "$bundle/Arcanist.app"

name="Arcanist-$version-macos-$arch"
ditto -c -k --keepParent "$bundle/Arcanist.app" "$out/$name.zip"

# The disk image: the app and a link to Applications, to drag it there.
staging="$out/dmg"
mkdir -p "$staging"
ditto "$bundle/Arcanist.app" "$staging/Arcanist.app"
ln -s /Applications "$staging/Applications"
hdiutil create -volname Arcanist -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$out/$name.dmg"

# The installer: always to /Applications, then it opens the app for whoever is signed in.
pkgroot="$out/pkgroot"
mkdir -p "$pkgroot" "$out/scripts"
ditto "$bundle/Arcanist.app" "$pkgroot/Arcanist.app"
pkgbuild --analyze --root "$pkgroot" "$out/components.plist"
plutil -replace 0.BundleIsRelocatable -bool NO "$out/components.plist"
cat > "$out/scripts/postinstall" <<'EOF'
#!/bin/sh
user="$(stat -f %Su /dev/console)"
if [ -n "$user" ] && [ "$user" != root ]; then
    sudo -u "$user" open /Applications/Arcanist.app || true
fi
exit 0
EOF
chmod +x "$out/scripts/postinstall"
pkgbuild --root "$pkgroot" --component-plist "$out/components.plist" --install-location /Applications \
    --identifier net.linkfast.arcanist --version "$version" --scripts "$out/scripts" "$out/$name.pkg"

# The server alone, for a Mac without a signed-in session (a LaunchDaemon, see packaging/macos/README.md).
tar -czf "$out/arcanist-$version-macos-$arch.tar.gz" -C "$app" server

rm -rf "$app" "$bundle" "$staging" "$pkgroot" "$iconset" "$out/scripts" "$out/components.plist" "$out/arcanist.icns"
ls -l "$out" "$out/releases"
