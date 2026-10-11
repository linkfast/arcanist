#!/usr/bin/env sh
# Builds the admin panel once for every package: src/Arcanist.Web/dist/arcanist-web/browser.
# Uses mise (mise.toml pins Node) when available, otherwise whatever node/npm is on the PATH.
set -eu
cd "$(dirname "$0")/../src/Arcanist.Web"
run() { if command -v mise >/dev/null 2>&1; then mise exec -- "$@"; else "$@"; fi; }
run npm ci
run npm run build
