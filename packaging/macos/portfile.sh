#!/usr/bin/env sh
# Writes the MacPorts port of a version, from its two app zips (packaging/macos/pack.sh):
#
#   packaging/macos/portfile.sh <version> <arm64 zip> <x64 zip>
#
# into artifacts/macos/arcanist-macports-<version>.tar.gz, a ports tree with aqua/arcanist/Portfile. Install it with
# `sudo port install` inside aqua/arcanist, or add the tree to /opt/local/etc/macports/sources.conf (see
# packaging/macos/README.md).
set -eu
version="$1"
arm64="$2"
x64="$3"

root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$root/artifacts/macos/ports"
rm -rf "$out"
mkdir -p "$out/aqua/arcanist"

sha() { (command -v sha256sum >/dev/null && sha256sum "$1" || shasum -a 256 "$1") | cut -d' ' -f1; }
size() { wc -c < "$1" | tr -d ' '; }

# MacPorts versions cannot hold a hyphen: 0.1.0-alpha.3 is 0.1.0.alpha.3 there; the files keep the real one.
cat > "$out/aqua/arcanist/Portfile" <<EOF
# -*- coding: utf-8; mode: tcl; tab-width: 4; indent-tabs-mode: nil; c-basic-offset: 4 -*- vim:fenc=utf-8:ft=tcl:et:sw=4:ts=4:sts=4

PortSystem          1.0

name                arcanist
version             $(echo "$version" | tr '-' '.')
set upstream        $version
revision            0
categories          aqua audio net
platforms           {darwin >= 23}
supported_archs     arm64 x86_64
license             AGPL-3+
maintainers         nomaintainer
description         Self-hosted music server
long_description    Arcanist is a self-hosted music server: libraries from your folders, for all your \\
                    devices. Arcanist.app runs it from the menu bar and opens at login.
homepage            https://github.com/linkfast/arcanist

master_sites        https://github.com/linkfast/arcanist/releases/download/v\${upstream}/
use_zip             yes
extract.mkdir       yes

if {\${configure.build_arch} eq "arm64"} {
    distname        Arcanist-\${upstream}-macos-arm64
    checksums       sha256  $(sha "$arm64") \\
                    size    $(size "$arm64")
} else {
    distname        Arcanist-\${upstream}-macos-x64
    checksums       sha256  $(sha "$x64") \\
                    size    $(size "$x64")
}

use_configure       no
build {}

destroot {
    copy \${worksrcpath}/Arcanist.app \${destroot}\${applications_dir}
}

notes "
Arcanist.app is in \${applications_dir}: open it and Arcanist runs from the menu bar (it opens at login\\
too). The admin panel is at http://localhost:8484/admin. The app is not notarized: the first time, macOS\\
asks to confirm it in System Settings → Privacy & Security → Open Anyway.
"
EOF

tar -czf "$root/artifacts/macos/arcanist-macports-$version.tar.gz" -C "$out" aqua
echo "$root/artifacts/macos/arcanist-macports-$version.tar.gz"
