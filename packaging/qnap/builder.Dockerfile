# Builds Arcanist's QNAP packages (packaging/qnap/qpkg.sh): QNAP's QDK, plus what qpkg.sh uses to take Debian 12's
# runtime libraries for both architectures (apt-get download, no emulation) and to draw the App Center icons.
FROM debian:12

ARG QDK_VERSION=2.5.3
ARG QDK_SHA256=17b3841b7d4590a4ee025844ba583304b5e3c497d9fa8934d5175131d3908022

RUN dpkg --add-architecture arm64 \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl binutils file imagemagick rsync xz-utils tzdata \
    && curl -fsSL -o /tmp/qdk.deb "https://github.com/qnap-dev/QDK/releases/download/v${QDK_VERSION}/qdk_${QDK_VERSION}_amd64.deb" \
    && echo "${QDK_SHA256}  /tmp/qdk.deb" | sha256sum -c - \
    && apt-get install -y --no-install-recommends /tmp/qdk.deb \
    && rm -rf /tmp/qdk.deb /var/lib/apt/lists/*

ENV PATH="/usr/share/QDK/bin:${PATH}"
