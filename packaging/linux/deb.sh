#!/usr/bin/env sh
# Builds Debian packages for Ubuntu 24.04 and later and Debian 12 and later from the self-contained builds that
# pack.sh leaves in artifacts/linux (arcanist-<version>-linux-<arch>/): artifacts/linux/arcanist_<version>_<arch>.deb
#
#   packaging/linux/deb.sh [version]          # ARCHES="x64 arm64" by default, as pack.sh
#
# Needs dpkg-deb (packaging/linux/build.ps1 runs it in a Debian container); DEB_MAINTAINER sets the Maintainer field.
# The package puts the server in /opt/arcanist and its systemd unit in /usr/lib/systemd/system, starts it when installed and restarts it when
# upgraded. Its data stays in /var/lib/arcanist, also when the package is purged.
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)}"
# Debian sorts "~" before anything: 0.1.0~alpha.1 comes before 0.1.0, as the pre-release before the release.
debversion="$(echo "$version" | sed 's/-/~/')-1"

for arch in ${ARCHES:-x64 arm64}; do
    case "$arch" in
        x64) debarch=amd64 ;;
        arm64) debarch=arm64 ;;
        *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
    esac
    build="artifacts/linux/arcanist-$version-linux-$arch"
    [ -x "$build/arcanist" ] || { echo "Missing $build (self-contained): run pack.sh first" >&2; exit 1; }

    staging="artifacts/linux/deb-$debarch"
    rm -rf "$staging"
    mkdir -p "$staging/DEBIAN" "$staging/opt" "$staging/usr/lib/systemd/system" "$staging/usr/share/doc/arcanist"
    cp -R "$build" "$staging/opt/arcanist"
    rm -f "$staging/opt/arcanist/arcanist.service"
    cp packaging/linux/arcanist.service "$staging/usr/lib/systemd/system/arcanist.service"
    sed -i 's|^# systemd unit for an Arcanist tarball extracted to /opt/arcanist.*|# systemd unit of the arcanist package. Change it with `sudo systemctl edit arcanist`, not here.|' \
        "$staging/usr/lib/systemd/system/arcanist.service"
    cat > "$staging/usr/share/doc/arcanist/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Arcanist
Source: https://github.com/linkfast/arcanist

Files: *
Copyright: 2026 LinkFast S.A. and Arcanist contributors
License: AGPL-3.0-or-later
 The full license is in /opt/arcanist/LICENSE, and the licenses of the components it
 includes in /opt/arcanist/THIRD-PARTY-NOTICES.md.
EOF
    find "$staging" -type d -exec chmod 0755 {} +
    find "$staging/opt" -type f -exec chmod 0644 {} +
    chmod 0755 "$staging/opt/arcanist/arcanist"
    find "$staging/opt/arcanist" -name '*.so' -exec chmod 0755 {} +

    size="$(du -sk "$staging" --exclude=DEBIAN | cut -f1)"
    cat > "$staging/DEBIAN/control" <<EOF
Package: arcanist
Version: $debversion
Architecture: $debarch
Maintainer: ${DEB_MAINTAINER:-LinkFast S.A. <https://github.com/linkfast/arcanist/issues>}
Installed-Size: $size
Depends: libc6 (>= 2.27), libgcc-s1, libstdc++6, zlib1g, ca-certificates, tzdata,
 libssl3t64 | libssl3,
 libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu72
Recommends: ffmpeg, libasound2t64 | libasound2
Section: sound
Priority: optional
Homepage: https://github.com/linkfast/arcanist
Description: self-hosted music server
 Arcanist turns your music folders into your own streaming service, for your
 whole household: libraries, playlists, ratings and listening statistics, a web
 player and admin panel, Subsonic and mStream apps, DLNA, a P2P network to
 listen from anywhere, and backups.
 .
 It runs as the systemd service "arcanist" on port 8484; open
 http://<this machine>:8484/ to set it up. ffmpeg (recommended) converts audio.
EOF

    cat > "$staging/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = configure ] && [ -d /run/systemd/system ]; then
    systemctl daemon-reload
    if [ -z "$2" ]; then
        # First install: start it, and with the system from now on.
        systemctl enable --now arcanist.service >/dev/null 2>&1 || true
        cat <<'MSG'

Arcanist is running: open http://<this machine>:8484/ and follow the setup wizard.
From another machine it asks for the setup code:
    sudo cat /var/lib/arcanist/setup-code.txt
Your music folders must be readable by everyone (or see "systemctl edit arcanist"
in https://github.com/linkfast/arcanist/blob/main/packaging/linux/README.md).

MSG
    else
        # An upgrade: the new version takes over (the database migrates when it starts).
        systemctl try-restart arcanist.service >/dev/null 2>&1 || true
    fi
fi
exit 0
EOF
    cat > "$staging/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = remove ] && [ -d /run/systemd/system ]; then
    systemctl disable --now arcanist.service >/dev/null 2>&1 || true
fi
exit 0
EOF
    cat > "$staging/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload >/dev/null 2>&1 || true
fi
if [ "$1" = purge ]; then
    # The data (database, keys, settings) is never deleted by the package.
    echo "Arcanist's data is still in /var/lib/arcanist (remove it with: sudo rm -rf /var/lib/private/arcanist /var/lib/arcanist)."
fi
exit 0
EOF
    chmod 0755 "$staging/DEBIAN/postinst" "$staging/DEBIAN/prerm" "$staging/DEBIAN/postrm"

    out="artifacts/linux/arcanist_${debversion}_${debarch}.deb"
    dpkg-deb --root-owner-group -Zxz --build "$staging" "$out" >/dev/null
    rm -rf "$staging"
    echo "$out"
done
