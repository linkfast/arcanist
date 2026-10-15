#!/usr/bin/env sh
# Builds the native libraries (native/arcanist-audio and native/arcanist-p2p) for a .NET runtime identifier into
# artifacts/native/<rid>/, where the server project picks them up when publishing for that RID.
#
#   packaging/native/build.sh linux-x64 | linux-arm64 | win-x64 | win-arm64 | osx-x64 | osx-arm64
#
# Linux builds go through cargo-zigbuild and target glibc 2.27, the floor of .NET 10 (and of Synology DSM 7.2),
# whatever the build machine has. Install it with `pip install cargo-zigbuild ziglang` or `cargo install
# cargo-zigbuild` plus Zig.
set -eu
rid="$1"
case "$rid" in
    linux-x64) target=x86_64-unknown-linux-gnu; glibc=.2.27; prefix=lib; ext=so ;;
    linux-arm64) target=aarch64-unknown-linux-gnu; glibc=.2.27; prefix=lib; ext=so ;;
    win-x64) target=x86_64-pc-windows-msvc; prefix=; ext=dll ;;
    win-arm64) target=aarch64-pc-windows-msvc; prefix=; ext=dll ;;
    osx-x64) target=x86_64-apple-darwin; prefix=lib; ext=dylib ;;
    osx-arm64) target=aarch64-apple-darwin; prefix=lib; ext=dylib ;;
    *) echo "unsupported runtime identifier: $rid" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
rustup target add "$target" >/dev/null 2>&1 || true
mkdir -p "$root/artifacts/native/$rid"

for crate in arcanist-audio arcanist-p2p; do
    manifest="$root/native/$crate/Cargo.toml"
    case "$rid" in
        linux-*) cargo zigbuild --release --locked --target "$target${glibc}" --manifest-path "$manifest" ;;
        *) cargo build --release --locked --target "$target" --manifest-path "$manifest" ;;
    esac
    lib="$prefix$(echo "$crate" | tr - _).$ext"
    cp "$root/native/$crate/target/$target/release/$lib" "$root/artifacts/native/$rid/"
    echo "$root/artifacts/native/$rid/$lib"
done
