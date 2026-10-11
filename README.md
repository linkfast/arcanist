# Arcanist

Arcanist is a self-hosted music server: your own personal and family "Spotify". Point it at the folders where your music lives, and listen from every device at home or on the go — web, desktop, mobile, TV, and DLNA players.

> **Status:** early development. Nothing here is ready for use yet.

## Goals

- **Libraries** built from local or network folders, scanned and organized automatically (tags, artwork, artists, albums), without duplicate tracks in the catalog.
- **Users and permissions** — an administrator account, regular users, and per-user access to each library.
- **Open APIs** — GraphQL as the primary API and an equivalent OpenAPI (REST) contract, documented so anyone can build a player.
- **Replaceable web player** — Ultimate Web Player ships by default; alternative players can be installed from the admin panel.
- **Runs anywhere you do** — Windows (installer, service, tray icon), Linux, Docker and NAS (Synology first).
- **Reach your music without a public IP** through a peer-to-peer network based on [iroh](https://github.com/n0-computer/iroh), and share libraries between Arcanist instances.
- **mStream compatibility** — mStream clients can use Arcanist as their server, and existing mStream installations can be imported.
- **Optional Ultimate Cloud integration** — use music stored in Ultimate Cloud, and with a paid plan sync libraries to Ultimate Cloud or S3-compatible storage.

## Repository layout

| Path | Contents |
|---|---|
| `src/Arcanist.Core` | Server (ASP.NET Core, .NET 10) |
| `src/Arcanist.Web` | Admin panel (Angular + spartan-ng), served at `/admin` |

## Running with Docker

```bash
docker build -t arcanist .
docker run -d -p 8484:8484 -v arcanist-data:/data -v /path/to/music:/music:ro arcanist
```

Open `http://<host>:8484/` and follow the setup wizard. From another device it asks for a one-time setup code,
printed in `docker logs <container>`. Add `/music` as a library. Keep `/data` on a local disk: with SQLite, the
database must not live on a network share (the music can).

## Building

Requirements: .NET SDK 10 and Node.js 24 (pinned in `mise.toml`).

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

`dotnet publish src/Arcanist.Core -c Release -p:IncludeAdminPanel=true` produces a self-contained folder with the
panel included.

## Building clients

The GraphQL and REST APIs are documented for player developers in [docs/developers](docs/developers/README.md).

## Contributing

Contributions are welcome. Every commit must be signed off under the [Developer Certificate of Origin](https://developercertificate.org/) — see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Copyright (C) 2026 Arcanist Contributors.

Arcanist is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE).

If you run a modified version of Arcanist and let other people use it over a network, the AGPL requires you to offer them the corresponding source code.

Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
