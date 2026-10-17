#!/usr/bin/env sh
# Builds the native libraries (native/arcanist-audio, native/arcanist-p2p and native/arcanist-player) for a .NET
# runtime identifier into artifacts/native/<rid>/, where the server project picks them up when publishing for that RID.
#
#   packaging/native/build.sh linux-x64 | linux-arm64 | win-x64 | win-arm64 | osx-x64 | osx-arm64 [crates…]
#
# Linux builds go through cargo-zigbuild and target glibc 2.27, the floor of .NET 10 (and of Synology DSM 7.2),
# whatever the build machine has. Install it with `pip install cargo-zigbuild ziglang` or `cargo install
# cargo-zigbuild` plus Zig. The player links ALSA: the target's libasound comes from Debian's packages, pinned by
# date and checksum (needs curl, pkg-config and dpkg-deb or ar).
set -eu
rid="$1"
shift
crates="${*:-arcanist-audio arcanist-p2p arcanist-player}"
case "$rid" in
    linux-x64) target=x86_64-unknown-linux-gnu; glibc=.2.27; prefix=lib; ext=so; deb=amd64; triplet=x86_64-linux-gnu ;;
    linux-arm64) target=aarch64-unknown-linux-gnu; glibc=.2.27; prefix=lib; ext=so; deb=arm64; triplet=aarch64-linux-gnu ;;
    win-x64) target=x86_64-pc-windows-msvc; prefix=; ext=dll ;;
    win-arm64) target=aarch64-pc-windows-msvc; prefix=; ext=dll ;;
    osx-x64) target=x86_64-apple-darwin; prefix=lib; ext=dylib ;;
    osx-arm64) target=aarch64-apple-darwin; prefix=lib; ext=dylib ;;
    *) echo "unsupported runtime identifier: $rid" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
rustup target add "$target" >/dev/null 2>&1 || true
mkdir -p "$root/artifacts/native/$rid"

# The target's ALSA (libasound and its development files) in a sysroot of its own: artifacts/native/alsa-<arch>/.
alsa_sysroot() {
    sysroot="$root/artifacts/native/alsa-$deb"
    if [ ! -f "$sysroot/.done" ]; then
        rm -rf "$sysroot"
        mkdir -p "$sysroot"
        snapshot=https://snapshot.debian.org/archive/debian/20230701T000000Z/pool/main/a/alsa-lib
        while read -r package sum; do
            [ -n "$package" ] || continue
            file="$sysroot/$package.deb"
            curl -fsSL --retry 3 -o "$file" "$snapshot/$package.deb"
            echo "$sum  $file" | sha256sum -c - >/dev/null
            if command -v dpkg-deb >/dev/null; then
                dpkg-deb -x "$file" "$sysroot"
            else
                (cd "$sysroot" && ar x "$file" && tar -xf data.tar.* && rm -f data.tar.* control.tar.* debian-binary)
            fi
            rm -f "$file"
        done <<EOF
$(case "$deb" in
    amd64) echo "libasound2_1.2.8-1+b1_amd64 44c77b076a7b11ae99712439022d822245b1994c435da564ebd320bb676faf4c
libasound2-dev_1.2.8-1+b1_amd64 6eddd5b43c03cdd769f6b6f4506abcbc84c19d4bada2b3036d1fd921a9875d7a" ;;
    arm64) echo "libasound2_1.2.8-1+b1_arm64 9fa889400fcee4b92c8f4a2fafbb7f2cd33444d9ec1665a71002ab67c06114bb
libasound2-dev_1.2.8-1+b1_arm64 263668e5ce831d3a8426bd342beba7e94925e893a4a5a285252b8f3ed6b3f5ee" ;;
esac)
libasound2-data_1.2.8-1_all fe0780d2d3674b2977e0acb0d48b448ad72ba1642564b7dc537f55e839984c2d
EOF
        touch "$sysroot/.done"
    fi
    export PKG_CONFIG_ALLOW_CROSS=1 PKG_CONFIG_SYSROOT_DIR="$sysroot" PKG_CONFIG_LIBDIR="$sysroot/usr/lib/$triplet/pkgconfig"
}

for crate in $crates; do
    manifest="$root/native/$crate/Cargo.toml"
    case "$rid" in
        linux-*)
            if [ "$crate" = arcanist-player ]; then
                alsa_sysroot
                # Looks for libasound next to itself first: the Docker image ships one there; elsewhere the system's.
                RUSTFLAGS='-C link-arg=-Wl,-rpath,$ORIGIN' cargo zigbuild --release --locked --target "$target${glibc}" --manifest-path "$manifest"
            else
                cargo zigbuild --release --locked --target "$target${glibc}" --manifest-path "$manifest"
            fi
            ;;
        *) cargo build --release --locked --target "$target" --manifest-path "$manifest" ;;
    esac
    lib="$prefix$(echo "$crate" | tr - _).$ext"
    cp "${CARGO_TARGET_DIR:-$root/native/$crate/target}/$target/release/$lib" "$root/artifacts/native/$rid/"
    echo "$root/artifacts/native/$rid/$lib"
done
