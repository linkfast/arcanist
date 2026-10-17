#!/usr/bin/env sh
# Builds Synology packages from the local tarballs, to try them before the release that SynoCommunity's build
# downloads exists:
#
#   packaging/linux/pack.sh               # artifacts/linux/arcanist-<version>-linux-{x64,arm64}-dotnet.tar.gz
#   packaging/synology/build-local.sh     # artifacts/synology/arcanist_<arch>-7.2_<version>-<rev>.spk
#   SPK_REV=2 packaging/synology/build-local.sh   # the same version again, to install over the first
#
# Needs Docker (spksrc's build image); clones spksrc into artifacts/spksrc the first time.
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)"
spksrc="$root/artifacts/spksrc"
for arch in x64 arm64; do
    [ -f "artifacts/linux/arcanist-$version-linux-$arch-dotnet.tar.gz" ] \
        || { echo "Missing artifacts/linux/arcanist-$version-linux-$arch-dotnet.tar.gz: run packaging/linux/pack.sh first" >&2; exit 1; }
done

# Without line ending conversion: on Windows, spksrc's checksum files would not be read.
[ -d "$spksrc/.git" ] || git -c core.autocrlf=false clone --depth 1 https://github.com/SynoCommunity/spksrc.git "$spksrc"
rm -rf "$spksrc/cross/arcanist" "$spksrc/spk/arcanist" "$spksrc/work-"*/arcanist* 2>/dev/null || true
cp -R packaging/synology/spksrc/cross/arcanist "$spksrc/cross/"
cp -R packaging/synology/spksrc/spk/arcanist "$spksrc/spk/"

# This version, from the local tarballs instead of the release's. Package versions have no pre-release suffix.
sed -i "s/^PKG_VERS = .*/PKG_VERS = $version/" "$spksrc/cross/arcanist/Makefile"
sed -i "s/^SPK_VERS = .*/SPK_VERS = ${version%%-*}/" "$spksrc/spk/arcanist/Makefile"
# SPK_REV=2 (and so on) builds a package DSM installs over an earlier one of the same version.
[ -z "${SPK_REV:-}" ] || sed -i "s/^SPK_REV = .*/SPK_REV = $SPK_REV/" "$spksrc/spk/arcanist/Makefile"
mkdir -p "$spksrc/distrib"
for arch in x64 arm64; do
    cp "artifacts/linux/arcanist-$version-linux-$arch-dotnet.tar.gz" "$spksrc/distrib/"
done
rm -f "$spksrc/cross/arcanist/digests"

# Docker on Windows (Git Bash) wants the folder as a Windows path, and no path conversion of the rest.
mount="$spksrc"
command -v cygpath >/dev/null && mount="$(cygpath -w "$spksrc")"
MSYS_NO_PATHCONV=1 docker run --rm --platform linux/amd64 -v "$mount:/spksrc" -w /spksrc ghcr.io/synocommunity/spksrc \
    sh -c 'make -C cross/arcanist digests && make -C spk/arcanist arch-x64-7.2 arch-aarch64-7.2'

mkdir -p artifacts/synology
cp "$spksrc"/packages/arcanist_*.spk artifacts/synology/
ls -l artifacts/synology
