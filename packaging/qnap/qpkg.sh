#!/usr/bin/env sh
# Builds Arcanist's QNAP packages (QTS and QuTS hero 5.0 and later) from the self-contained Linux builds:
# artifacts/qnap/Arcanist_<version>_<x86_64|arm_64>.qpkg
#
#   packaging/qnap/qpkg.sh [version]          # ARCHES="x64 arm64" by default
#
# Runs in the image of packaging/qnap/builder.Dockerfile (QDK, and Debian 12 to take its libraries from); build.ps1
# does all of it with Docker. Takes artifacts/linux/arcanist-<version>-linux-<arch>/ or its .tar.gz (pack.sh).
#
# QTS's C library is glibc 2.21 and Arcanist needs 2.27, so each package brings Debian 12's (2.36), its loader and the
# libraries the server loads (ICU, OpenSSL, ALSA), with time zones and CA certificates: bin/arcanist-run runs the
# server through that loader. About 40 MB more than the tarball.
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)}"
# QDK takes at most 10 characters: 0.1.0-alpha.6 is 0.1.0a6, 0.1.0-edge.123 is 0.1.0e123.
qver="$(echo "$version" | sed -E 's/^([0-9]+\.[0-9]+\.[0-9]+)-([a-z])[a-z]*\.?([0-9]*).*$/\1\2\3/')"
[ "${#qver}" -le 10 ] || { echo "Version $qver is longer than QDK allows (10)" >&2; exit 1; }

out="$root/artifacts/qnap"
work="$out/work"
rm -rf "$work"
mkdir -p "$work" "$out"
cp -R packaging/qnap/Arcanist "$work/Arcanist"
pkg="$work/Arcanist"
sed -i "s/@VERSION@/$qver/" "$pkg/qpkg.cfg"
chmod 0755 "$pkg/shared/Arcanist.sh" "$pkg/shared/bin/"* "$pkg/shared/ui/index.cgi"

# App Center's icons: 64 px enabled and disabled, 80 px for its dialogs (GIF names, PNG content works).
mkdir -p "$pkg/icons"
icon=src/Arcanist.Web/public/arcanist.png
convert "$icon" -resize 64x64 "gif:$pkg/icons/Arcanist.gif"
convert "$icon" -resize 64x64 -colorspace Gray "gif:$pkg/icons/Arcanist_gray.gif"
convert "$icon" -resize 80x80 "gif:$pkg/icons/Arcanist_80.gif"

# Debian 12's runtime libraries for one architecture, without emulation: apt-get download and dpkg-deb -x.
sysroot() {
    debarch=$1
    dir="$work/sysroot-$debarch"
    mkdir -p "$dir/debs"
    (cd "$dir/debs" && apt-get download -q \
        "libc6:$debarch" "libstdc++6:$debarch" "libgcc-s1:$debarch" "libicu72:$debarch" "libssl3:$debarch" \
        "libasound2:$debarch" libasound2-data "gcc-12-base:$debarch" >/dev/null)
    for deb in "$dir"/debs/*.deb; do dpkg-deb -x "$deb" "$dir"; done
    echo "$dir"
}

[ -d /var/lib/apt/lists ] && [ -n "$(ls /var/lib/apt/lists 2>/dev/null)" ] || apt-get update -q >/dev/null

qarches=""
for arch in ${ARCHES:-x64 arm64}; do
    case "$arch" in
        x64) debarch=amd64; qarch=x86_64; triplet=x86_64-linux-gnu; loader=ld-linux-x86-64.so.2 ;;
        arm64) debarch=arm64; qarch=arm_64; triplet=aarch64-linux-gnu; loader=ld-linux-aarch64.so.1 ;;
        *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
    esac
    build="artifacts/linux/arcanist-$version-linux-$arch"
    if [ ! -x "$build/arcanist" ]; then
        [ -f "$build.tar.gz" ] || { echo "Missing $build (self-contained): run packaging/linux/pack.sh first" >&2; exit 1; }
        tar -xzf "$build.tar.gz" -C artifacts/linux
    fi

    app="$pkg/$qarch/app"
    mkdir -p "$app/lib"
    cp -R "$build"/. "$app/"
    # systemd's unit is not for QTS, and LTTng tracing is not there either.
    rm -f "$app/arcanist.service" "$app/libcoreclrtraceptprovider.so"

    sys="$(sysroot "$debarch")"
    libdir="$sys/lib/$triplet"
    [ -d "$libdir" ] || libdir="$sys/usr/lib/$triplet"
    # The loader sits next to the server: under an explicit loader /proc/self/exe is the loader, and .NET finds the
    # application from that folder.
    cp -L "$sys/lib/$triplet/$loader" "$app/$loader"
    # libmvec: the ffmpeg Arcanist downloads (BtbN's) needs it too.
    for name in libc.so.6 libm.so.6 libmvec.so.1 libdl.so.2 libpthread.so.0 librt.so.1 libresolv.so.2 \
                libnss_files.so.2 libnss_dns.so.2; do
        cp -L "$sys/lib/$triplet/$name" "$app/lib/" 2>/dev/null || cp -L "$sys/usr/lib/$triplet/$name" "$app/lib/" 2>/dev/null || :
    done
    for name in libstdc++.so.6 libgcc_s.so.1 libicuuc.so.72 libicui18n.so.72 libicudata.so.72 \
                libssl.so.3 libcrypto.so.3 libasound.so.2; do
        found="$(find "$sys" -name "$name" | head -n 1)"
        [ -n "$found" ] || { echo "Missing $name for $debarch" >&2; exit 1; }
        cp -L "$found" "$app/lib/$name"
    done
    mkdir -p "$app/alsa" "$app/certs"
    cp -R "$sys/usr/share/alsa/." "$app/alsa/"
    cp -R /usr/share/zoneinfo "$app/zoneinfo"
    cp -L /etc/ssl/certs/ca-certificates.crt "$app/certs/ca-certificates.crt"
    # Their licenses, as Debian ships them (the sources are Debian 12's, at sources.debian.org).
    mkdir -p "$app/licenses"
    for doc in "$sys"/usr/share/doc/*/copyright; do
        cp "$doc" "$app/licenses/$(basename "$(dirname "$doc")").copyright"
    done
    cp /usr/share/doc/tzdata/copyright "$app/licenses/tzdata.copyright"
    cp /usr/share/doc/ca-certificates/copyright "$app/licenses/ca-certificates.copyright"
    strip --strip-unneeded "$app"/lib/*.so* 2>/dev/null || :
    chmod 0755 "$app/arcanist" "$app/$loader" "$app"/lib/*

    # Every library the server and its native parts need must be in the package.
    missing=""
    for elf in "$app/arcanist" "$app"/*.so "$app"/lib/*; do
        for needed in $(readelf -d "$elf" 2>/dev/null | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p'); do
            [ -e "$app/lib/$needed" ] || [ -e "$app/$needed" ] || missing="$missing $needed($(basename "$elf"))"
        done
    done
    [ -z "$missing" ] || { echo "Libraries missing for $qarch:$missing" >&2; exit 1; }
    qarches="$qarches --build-arch $qarch"
done

(cd "$pkg" && qbuild --root "$pkg" $qarches --build-dir "$out" >/dev/null)
rm -rf "$work"
ls "$out"/*.qpkg
