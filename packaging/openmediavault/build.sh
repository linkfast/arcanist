#!/usr/bin/env sh
# Builds openmediavault's plugin for Arcanist from src/: artifacts/linux/openmediavault-arcanist_<version>_all.deb
#
#   packaging/openmediavault/build.sh [arcanist version] [plugin version]
#
# The plugin depends on the arcanist package of the same release (artifacts/linux/arcanist_<version>-1_<arch>.deb,
# from packaging/linux/deb.sh): install both together. Its own version is 1.<number of commits>, which only grows:
# openmediavault reads plugin versions with Python's packaging (no "~"), so it cannot follow Arcanist's pre-releases.
# Needs dpkg-deb (packaging/openmediavault/build.ps1 runs it in a Debian container).
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)}"
debversion="$(echo "$version" | sed 's/-/~/')-1"
plugin="${2:-1.$(git rev-list --count HEAD)}"

staging="artifacts/linux/omv-plugin"
rm -rf "$staging"
mkdir -p "$staging/DEBIAN" "$staging/usr/share/doc/openmediavault-arcanist"
cp -R packaging/openmediavault/src/usr packaging/openmediavault/src/srv "$staging/"
find "$staging" -type d -exec chmod 0755 {} +
find "$staging" -type f -exec chmod 0644 {} +
# omv-confdbadm only runs executable scripts.
chmod 0755 "$staging"/usr/share/openmediavault/confdb/create.d/*.sh

cat > "$staging/usr/share/doc/openmediavault-arcanist/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: openmediavault-arcanist
Source: https://github.com/linkfast/arcanist

Files: *
Copyright: 2026 LinkFast S.A. and Arcanist contributors
License: AGPL-3.0-or-later
 The full license is in /opt/arcanist/LICENSE (from the arcanist package).
EOF

size="$(du -sk "$staging" --exclude=DEBIAN | cut -f1)"
cat > "$staging/DEBIAN/control" <<EOF
Package: openmediavault-arcanist
Version: $plugin
Architecture: all
Maintainer: ${DEB_MAINTAINER:-LinkFast S.A. <https://github.com/linkfast/arcanist/issues>}
Installed-Size: $size
Depends: openmediavault (>= 7.0), arcanist (>= $debversion)
Section: net
Priority: optional
Homepage: https://github.com/linkfast/arcanist
XB-Plugin-Section: multimedia
XB-Plugin-Architecture: amd64, arm64
Description: openmediavault plugin for Arcanist, the self-hosted music server
 Runs Arcanist (from the arcanist package) as a service of openmediavault:
 its port, the shared folders it may read or write, server audio, its status
 with the setup code, and its log in the System Logs. Arcanist itself is set
 up in its own web interface.
EOF

# Rebuilds the workbench and restarts the engine when the plugin is installed, upgraded or removed.
cat > "$staging/DEBIAN/triggers" <<'EOF'
activate-noawait update-workbench
activate-noawait restart-engined
EOF

cat > "$staging/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e

. /etc/default/openmediavault
. /usr/share/openmediavault/scripts/helper-functions

case "$1" in
    configure)
        echo "Updating configuration database ..."
        omv-confdbadm create "conf.service.arcanist"
        if [ -n "$2" ]; then
            omv-confdbadm migrate "conf.service.arcanist" "${2}"
        fi
        # Applies the settings now: the port and shared folders in the arcanist service's drop-in.
        omv-salt deploy run --quiet arcanist || :
    ;;
    abort-upgrade|abort-remove|abort-deconfigure)
    ;;
    *)
        echo "postinst called with unknown argument '$1'" >&2
        exit 1
    ;;
esac

exit 0
EOF

cat > "$staging/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e

remove_action() {
    # Arcanist goes back to its package's settings (port 8484); its data is never touched.
    rm -f /etc/systemd/system/arcanist.service.d/openmediavault.conf
    rmdir /etc/systemd/system/arcanist.service.d 2>/dev/null || :
    if [ -d /run/systemd/system ]; then
        systemctl daemon-reload || :
        systemctl try-restart arcanist.service || :
    fi
}

case "$1" in
    purge)
        remove_action
        if [ -f /usr/share/openmediavault/scripts/helper-functions ]; then
            . /usr/share/openmediavault/scripts/helper-functions
            omv_config_delete "/config/services/arcanist" || :
        fi
    ;;
    remove)
        remove_action
    ;;
    upgrade|failed-upgrade|abort-install|abort-upgrade|disappear)
    ;;
    *)
        echo "postrm called with unknown argument '$1'" >&2
        exit 1
    ;;
esac

exit 0
EOF
chmod 0755 "$staging/DEBIAN/postinst" "$staging/DEBIAN/postrm"

out="artifacts/linux/openmediavault-arcanist_${plugin}_all.deb"
dpkg-deb --root-owner-group -Zxz --build "$staging" "$out" >/dev/null
rm -rf "$staging"
echo "$out"
