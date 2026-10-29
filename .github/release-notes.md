## Which file do I need?

Every package includes the admin panel and listens on port **8484**. Once it is running, open
http://localhost:8484/admin (or `http://<server-ip>:8484/admin` from another device) and the setup wizard walks you
through the rest.

### Windows 10/11

| File | Choose it if |
|---|---|
| `Arcanist-win-Setup.exe` | **Most people** (Intel/AMD PCs). Installs for your user without admin rights, puts Arcanist in the tray, starts with Windows and updates itself. It can also run as a Windows service (from the tray menu). |
| `Arcanist-win-arm64-Setup.exe` | The same, for Windows on ARM (Snapdragon, Surface Pro X…). |
| `Arcanist-win-Portable.zip` / `Arcanist-win-arm64-Portable.zip` | You don't want to install anything: unzip and run. |

Windows SmartScreen may warn the first time (the installer is not signed yet): **More info → Run anyway**.

### macOS 14 or later

| File | Choose it if |
|---|---|
| `Arcanist-…-macos-arm64.dmg` | **Apple Silicon (M1 and later)**: drag Arcanist.app to Applications. |
| `Arcanist-…-macos-x64.dmg` | Intel Macs. |
| `….pkg` | You prefer an installer that puts the app in /Applications for you. |
| `….zip` | The same app, zipped. |
| `arcanist-…-macos-<arch>.tar.gz` | Only the server, without the menu bar app (advanced). |
| `arcanist-macports-….tar.gz` | The MacPorts port. |

Or with Homebrew: `brew install --cask linkfast/arcanist/arcanist`. The app is not notarized: the first time, open
it and go to **System Settings → Privacy & Security → Open Anyway**.

### Linux

| File | Choose it if |
|---|---|
| `arcanist_…_amd64.deb` / `…_arm64.deb` | **Ubuntu 24.04+ or Debian 12+** (also Raspberry Pi OS 64-bit): `sudo apt install ./arcanist_…deb`. Runs as a systemd service and starts on its own. |
| `arcanist-…-linux-x64.tar.gz` / `…-linux-arm64.tar.gz` | Other distributions: self-contained (no .NET needed), with a systemd unit inside. |
| `arcanist-…-linux-<arch>-dotnet.tar.gz` | You already have the ASP.NET Core 10 runtime and want a smaller download. |

Use `amd64`/`x64` for Intel/AMD and `arm64` for 64-bit ARM.

### Docker

```sh
docker pull ghcr.io/linkfast/arcanist:{{VERSION}}
```

`preview` always points to the newest release, pre-releases included. Ready-made Compose files (SQLite or
PostgreSQL) are in [`packaging/docker`](https://github.com/linkfast/arcanist/tree/main/packaging/docker).

### NAS

| NAS | File |
|---|---|
| **Synology** (DSM 7.2+) | `arcanist_x64-7.2_….spk` for Intel/AMD models, `arcanist_aarch64-7.2_….spk` for ARMv8 ones. Package Center → **Manual Install**. Needs the **.NET 10 Runtime** and **FFmpeg 8** packages from [SynoCommunity](https://synocommunity.com). |
| **QNAP** (QTS / QuTS hero) | `Arcanist_…_x86_64.qpkg` for Intel/AMD, `Arcanist_…_arm_64.qpkg` for ARM64. App Center → **Install Manually** (allow apps without a valid signature). |
| **OpenMediaVault 7/8** | Install `arcanist_…_<arch>.deb` and then the plugin `openmediavault-arcanist_…_all.deb`; Arcanist appears under Services. |
| **TrueNAS SCALE, Unraid, Rockstor** | They use the Docker image: see [`packaging/truenas`](https://github.com/linkfast/arcanist/tree/main/packaging/truenas), [`packaging/unraid`](https://github.com/linkfast/arcanist/tree/main/packaging/unraid) and [`packaging/rockstor`](https://github.com/linkfast/arcanist/tree/main/packaging/rockstor). |

Not sure whether your NAS is Intel/AMD or ARM? Look up its model on the maker's site (the CPU is listed in the specs).

### Files you don't need to download

`*.nupkg`, `RELEASES*`, `releases.*.json` and `assets.*.json` are what the Windows and macOS apps use to update
themselves.
