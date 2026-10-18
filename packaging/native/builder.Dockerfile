# The environment packaging/native/build.sh needs for Linux: Rust, cargo-zigbuild with Zig (to target glibc 2.27)
# and what fetching ALSA's sysroot uses (curl, dpkg-deb). packaging/synology/build.ps1 builds it once and runs the
# native build in it, so a Windows machine with Docker needs nothing else.
#
#   docker build -t arcanist-native-builder -f packaging/native/builder.Dockerfile packaging/native
FROM rust:1.93-bookworm

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3-pip pkg-config curl \
    && rm -rf /var/lib/apt/lists/* \
    && pip3 install --no-cache-dir --break-system-packages cargo-zigbuild ziglang \
    && rustup target add x86_64-unknown-linux-gnu aarch64-unknown-linux-gnu
