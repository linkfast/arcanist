# Arcanist

Arcanist is a self-hosted music server: your own personal and family "Spotify". It is an open source product of LinkFast. Point it at the folders where your music lives, and listen from every device at home or on the go — web, desktop, mobile, TV, and DLNA players.

> **Status:** early development. Nothing here is ready for use yet.

## Goals

- **Libraries** built from local or network folders, scanned and organized automatically (tags, artwork, artists, albums), without duplicate tracks in the catalog.
- **Users and permissions** — an administrator account, regular users, and per-user access to each library.
- **Open APIs** — GraphQL as the primary API and an equivalent OpenAPI (REST) contract, documented so anyone can build a player.
- **Replaceable web player** — Ultimate Web Player ships by default; alternative players can be installed from the admin panel.
- **Runs anywhere you do** — Windows (installer, service, tray icon), macOS (a menu bar app), Linux, Docker and NAS (Synology, QNAP, OpenMediaVault, TrueNAS SCALE, Unraid and Rockstor).
- **Reach your music without a public IP** through a peer-to-peer network based on [iroh](https://github.com/n0-computer/iroh), and share libraries between Arcanist instances.
- **mStream compatibility** — mStream clients can use Arcanist as their server, and existing mStream installations can be imported.
- **Optional Ultimate Cloud integration** — use music stored in Ultimate Cloud, and with a paid plan sync libraries to Ultimate Cloud or S3-compatible storage.

## Installing

Releases are published on [GitHub Releases](https://github.com/linkfast/arcanist/releases) and as a Docker image
at `ghcr.io/linkfast/arcanist`. Every installation listens on port **8484**: open `http://<host>:8484/` and follow
the setup wizard. From another device it asks for a one-time setup code, shown where each section below says.

### Windows

Run `Arcanist-win-Setup.exe`. It installs for the current user without administrator rights and starts the
Arcanist tray icon, which runs the server in the background while you are signed in, starts with Windows and
installs updates. Data lives in `%ProgramData%\Arcanist`.

To keep Arcanist running when nobody is signed in, choose **Run as a Windows service…** in the tray menu. The
service runs as the dedicated account `NT SERVICE\Arcanist`, which can only read the music folders you choose; for
music on a NAS share, keep the app mode or change the service account in `services.msc`. The tray app keeps
controlling and updating the service.

The setup code is in `%ProgramData%\Arcanist\setup-code.txt` (you do not need it on the machine itself).

### macOS

macOS 14 or later, Apple Silicon or Intel. Open the `.dmg` and drag **Arcanist.app** to Applications (or run the
`.pkg`), or install it with Homebrew (`brew install --cask linkfast/arcanist/arcanist`) or MacPorts. The app lives
in the menu bar: it runs the server, opens at login and installs updates. Data lives in
`~/Library/Application Support/Arcanist`. It is not notarized yet: the first time, confirm it in **System Settings →
Privacy & Security → Open Anyway**. Details, MacPorts and running the server alone: see
[packaging/macos](packaging/macos/README.md).

### Linux

Self-contained tarballs for x64 and arm64 with a systemd unit: see [packaging/linux](packaging/linux/README.md).
The setup code is in `journalctl -u arcanist`.

### Docker

```bash
docker run -d --name arcanist --restart unless-stopped -p 8484:8484 -v arcanist-data:/data -v /path/to/music:/music:ro ghcr.io/linkfast/arcanist
```

The image supports linux/amd64 and linux/arm64. The setup code is in `docker logs arcanist`. Add `/music` as a
library. Keep `/data` on a local disk: with SQLite, the database must not live on a network share (the music can).
For server audio (playing through the host's sound card), add `--device /dev/snd --group-add audio`.

With Docker Compose (SQLite or PostgreSQL): see [packaging/docker](packaging/docker/README.md).

### Synology

A package for DSM 7.2+ is prepared for SynoCommunity ([packaging/synology](packaging/synology/README.md)). Its install
wizard shows the setup code.

### Other NAS

- **QNAP** (QTS and QuTS hero 5+): the release's `.qpkg`, in App Center → Install Manually
  ([packaging/qnap](packaging/qnap/README.md)).
- **OpenMediaVault** 7+: the `.deb` and the `openmediavault-arcanist` plugin, which adds Services → Arcanist
  ([packaging/openmediavault](packaging/openmediavault/README.md)).
- **TrueNAS SCALE** 24.10+: an app for its community catalog, and a YAML for Install via YAML
  ([packaging/truenas](packaging/truenas/README.md)).
- **Unraid**: a Community Applications template ([packaging/unraid](packaging/unraid/README.md)).
- **Rockstor** 5.5+: a Rock-on ([packaging/rockstor](packaging/rockstor/README.md)).

## Repository layout

| Path | Contents |
|---|---|
| `src/Arcanist.Core` | Server (ASP.NET Core, .NET 10), executable `arcanist` |
| `src/Arcanist.Web` | Admin panel (Angular + spartan-ng), served at `/admin` |
| `src/Arcanist.Tray` | The Windows tray app and macOS menu bar app (Avalonia), installed with Velopack |
| `native/arcanist-audio` | Audio analysis in Rust (Symphonia): loudness, fingerprints, waveform, tempo and key |
| `tests/` | Server and tray tests |
| `packaging/` | Windows installer, macOS app (.dmg, .pkg, MacPorts), Linux tarballs, .deb and systemd unit, NAS packages (Synology, QNAP, OpenMediaVault, TrueNAS, Unraid, Rockstor) |
| `.github/workflows` | CI, and releases on `v*` tags |

## Building

Requirements: .NET SDK 10, Node.js 24 (pinned in `mise.toml`) and Rust 1.85+ for the audio analysis library in
`native/arcanist-audio` (the server builds without it, but then analyzes nothing). ffmpeg is optional at runtime:
it converts streams and decodes the formats the built-in decoder does not read, and the admin panel can download it.
[docs/ffmpeg.md](docs/ffmpeg.md) explains which ffmpeg Arcanist uses and how to install one that encodes HE-AAC.

```bash
dotnet build Arcanist.slnx
dotnet test
dotnet run --project src/Arcanist.Core          # http://localhost:5263
```

The admin panel (Angular) is developed with its own dev server, which proxies API calls to the running server:

```bash
cd src/Arcanist.Web
npm ci
npm start                                        # http://localhost:4200/admin/
```

Packages, each including the admin panel:

```bash
docker build -t arcanist .                       # or: docker buildx build --platform linux/amd64,linux/arm64 .
packaging/linux/pack.sh 0.2.0                    # artifacts/linux/*.tar.gz
pwsh packaging/windows/pack.ps1 -Version 0.2.0   # artifacts/windows/releases/Arcanist-win-Setup.exe
```

Pushing a `v*` tag runs the same builds in GitHub Actions and publishes the release and the Docker image.

## Building clients

The GraphQL and REST APIs are documented for player developers in [docs/developers](docs/developers/README.md).

## Contributing

Contributions are welcome. Every commit must be signed off under the [Developer Certificate of Origin](https://developercertificate.org/) — see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Copyright (C) 2026 LinkFast S.A. and Arcanist contributors. Arcanist is developed by LinkFast as open source; contributions keep their authors' copyright.

Arcanist is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE).

If you run a modified version of Arcanist and let other people use it over a network, the AGPL requires you to offer them the corresponding source code.

Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
