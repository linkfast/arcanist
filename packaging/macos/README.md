# Arcanist on macOS

macOS 14 (Sonoma) or later, on Apple Silicon (`arm64`) or Intel (`x64`). Every release has, for each:

| File | What it is |
|---|---|
| `Arcanist-<version>-macos-<arch>.dmg` | **Arcanist.app** to drag to Applications. It also runs from anywhere (portable). |
| `Arcanist-<version>-macos-<arch>.pkg` | An installer: puts Arcanist.app in /Applications and opens it. |
| `Arcanist-<version>-macos-<arch>.zip` | The same Arcanist.app, zipped. |
| `arcanist-macports-<version>.tar.gz` | The MacPorts port (both architectures). |
| `arcanist-<version>-macos-<arch>.tar.gz` | The server alone, without the app (see below). |

**Homebrew:** `brew install --cask linkfast/arcanist/arcanist` (the cask is in
[linkfast/homebrew-arcanist](https://github.com/linkfast/homebrew-arcanist), updated with every release).

**MacPorts:** download `arcanist-macports-<version>.tar.gz` from the release, then either

```sh
tar -xzf arcanist-macports-<version>.tar.gz && cd aqua/arcanist && sudo port install
```

or keep the tree as a source of ports: extract it in a folder of your own, add `file:///that/folder` above the
`rsync://` line of `/opt/local/etc/macports/sources.conf`, run `portindex` in it, and `sudo port install arcanist`.

## Arcanist.app

The app lives in the menu bar (not in the Dock): it starts the server, opens the player and the admin panel, starts,
stops and restarts the server, opens its logs, and **opens at login** (a LaunchAgent in
`~/Library/LaunchAgents/net.linkfast.arcanist.plist`; the menu turns it off). It updates itself through Velopack,
from the releases of its own channel (`osx-arm64`, `osx-x64`; edge builds use `edge-osx-…`), like the Windows app.

The data (database, settings, caches, logs) is in `~/Library/Application Support/Arcanist`, the server's default on
macOS; the music stays where it is. The admin panel is at http://localhost:8484/admin.

**It is not notarized** (that needs an Apple Developer ID). The app is signed ad hoc, so the first time macOS asks:
open it, then **System Settings → Privacy & Security → Open Anyway** (and the same for the `.pkg`). Installed through
Homebrew or MacPorts it is the same app.

To remove it: quit it from the menu bar, delete Arcanist.app, and, if you want, the LaunchAgent and
`~/Library/Application Support/Arcanist`.

## The server alone

For a Mac that serves music with nobody signed in, the `.tar.gz` holds the server without the app:

```sh
sudo mkdir -p /usr/local/arcanist && sudo tar -xzf arcanist-<version>-macos-<arch>.tar.gz -C /usr/local/arcanist --strip-components 1
/usr/local/arcanist/arcanist --data-dir "/Library/Application Support/Arcanist"
```

Run it at boot with a LaunchDaemon (`/Library/LaunchDaemons/net.linkfast.arcanist.server.plist`), with
`ProgramArguments` like the command above, `RunAtLoad`, and `KeepAlive` with `SuccessfulExit` false (the server asks
to be restarted by exiting with code 75). It runs as root unless `UserName` names another account, which must be able
to read the music folders.

## Building

On a Mac with Xcode's command line tools, .NET 10 and Rust: `sh packaging/macos/pack.sh <version> <arm64|x64>`
(`packaging/macos/portfile.sh` writes the port from the two zips). The release workflow does both on GitHub's macOS
runners (`.github/workflows/packages.yml`).
