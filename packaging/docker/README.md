# Arcanist with Docker

## Try it in two minutes

With Docker (Docker Desktop on Windows or macOS), the published image, nothing else. Builds for testing between
releases are tagged **`edge`** (each change to `main`); releases are `preview` (the newest, pre-releases included),
`latest` (the newest stable one) and their version.

```bash
docker run -d --name arcanist -p 8484:8484 -v arcanist-data:/data -v /path/to/music:/music:ro ghcr.io/linkfast/arcanist:edge
```

On Windows (PowerShell), the music folder as Windows writes it:

```powershell
docker run -d --name arcanist -p 8484:8484 -v arcanist-data:/data -v "D:\Music:/music:ro" ghcr.io/linkfast/arcanist:edge
```

Open `http://localhost:8484/`, follow the setup wizard and add `/music` as a library. From another machine the
wizard asks for the setup code: `docker logs arcanist`. To update: `docker pull ghcr.io/linkfast/arcanist:edge`, then
remove the container (`docker rm -f arcanist`; the data stays in the volume) and run it again.

To keep it running, use Compose (below) with `ARCANIST_TAG=edge` in `.env`.

## Build the image yourself

From a checkout, with Docker as the only requirement (the Dockerfile builds the panel, the native libraries and the
server). In PowerShell:

```powershell
pwsh packaging/docker/build.ps1                                # image arcanist:local
pwsh packaging/docker/build.ps1 -Up -MusicDir D:\Music         # ...and start it (http://localhost:8484)
pwsh packaging/docker/build.ps1 -LocalWebamp -Up -MusicDir D:\Music   # with Webamp from ../ArcanistWebPlayers/webamp
```

Or by hand: `docker build -t arcanist:local .`, then Compose with `ARCANIST_IMAGE=arcanist` and `ARCANIST_TAG=local`.
The first build takes a while (Rust); later ones reuse Docker's cache. `-Port` changes the port on this machine.

## Publish it for testers

- **Automatically:** every change to `main` publishes `ghcr.io/linkfast/arcanist:edge` and `:edge-<commit>`
  (`.github/workflows/edge.yml`, amd64 and arm64), along with the `edge` pre-release on GitHub (Windows, .deb and
  Synology packages of the same build). Run that workflow by hand (Actions → *Edge* → *Run workflow*) to publish
  another tag, such as one for a particular test.
- **From your machine:** `docker login ghcr.io` (user: your GitHub user; password: a token with `write:packages`),
  then `pwsh packaging/docker/build.ps1 -Push -Tag <tag>`: amd64 and arm64, straight to the registry.
- **Releases** (a `v*` tag) publish `<version>`, `preview` and, for stable versions, `latest` (`release.yml`).

The first time GitHub creates the package it is **private**: in github.com/orgs/linkfast/packages → *arcanist* →
*Package settings*, set *Change visibility* to **Public** (and link it to the repository) so anyone can pull it
without signing in.

## Compose

Two ready Compose files, for Linux, macOS or Windows with Docker:

| File | Database | For |
|---|---|---|
| [`compose.yaml`](compose.yaml) | SQLite, in the data volume | Most homes: up to about 150,000–200,000 songs and around ten people listening at once. |
| [`compose.postgres.yaml`](compose.postgres.yaml) | PostgreSQL 17, in a container next to it | Large libraries (more than about 250,000 songs) or many people listening at once. |

A server on SQLite can move to PostgreSQL later from the panel (Database page), without these files.

### Install

1. Put the Compose file you chose and [`.env.example`](.env.example) in a folder, and copy `.env.example` to `.env`.
2. In `.env`, set `MUSIC_DIR` to your music folder. For PostgreSQL, also set `POSTGRES_PASSWORD` to a long random
   text of letters and digits.
3. Start it:

   ```bash
   docker compose up -d
   ```

   With PostgreSQL: `docker compose -f compose.postgres.yaml up -d`.
4. Open `http://<host>:8484/`. From another machine, the setup wizard asks for the setup code, which is in the log:

   ```bash
   docker compose logs arcanist | grep -i "setup code"
   ```

5. In the wizard, add `/music` as a library.

### Settings

All in `.env`:

- `MUSIC_DIR`: the folder of your music on the host (required).
- `ARCANIST_PORT`: the port on the host (8484).
- `ARCANIST_TAG`: the image's version. `preview` follows the newest release (there are only pre-releases for now),
  `latest` the newest stable one, `edge` the newest build of `main` (for testing), and a version such as
  `0.1.0-alpha.1` stays on it.
- `ARCANIST_IMAGE`: another image, such as `arcanist` for one built here (`ghcr.io/linkfast/arcanist` by default).
- `TZ`: your time zone, such as `Europe/Madrid`.
- `POSTGRES_PASSWORD`: only for `compose.postgres.yaml`.

Everything else is set up in the panel.

### Update

```bash
docker compose pull
docker compose up -d
```

The database is migrated when Arcanist starts. Pin `ARCANIST_TAG` to a version to choose when to update.

### Folders and permissions

The container runs as the user `app` (uid 1654).

- **Music is mounted read-only** (`:ro`): enough to play it. Drop `:ro` to let Arcanist organize files, import
  from drop folders, copy shared libraries or receive uploads; the folder must then be writable by uid 1654
  (`sudo chown -R 1654:1654 /srv/music`, or give that uid access with ACLs).
- **The music folder must be readable by uid 1654** (readable by everyone is enough).
- **Data** lives in the `arcanist-data` volume. Keep it on a local disk: SQLite must not live on a network share
  (the music can).
- To run as another user, add `user: "1000:1000"` to the service and mount a folder of the host you own on
  `/data` instead of the volume.

The Compose files have commented lines for a drop folder (`/drop`) and a backup folder (`/backups`).

### Optional features

- **Server audio** (playing through the host's sound card): uncomment `devices` and `group_add`. Linux only.
- **DLNA** finds TVs and speakers with multicast on the local network, which a container with its own network does
  not see. For DLNA, use `network_mode: host` instead of `ports` (Linux only).
- **Behind a reverse proxy** (HTTPS): add its address or network to `environment`, so Arcanist sees the clients'
  real addresses: `Arcanist__Network__TrustedProxies__0: 172.18.0.0/16` (one entry per number: `__1`, `__2`…).

### Back up

The panel's Backups page copies the music, the users' data and, encrypted, the server itself, to a folder, S3 or
another Arcanist. To copy the volumes by hand, stop the server first:

```bash
docker compose stop arcanist
docker run --rm -v arcanist_arcanist-data:/data -v "$PWD":/backup alpine tar czf /backup/arcanist-data.tgz -C /data .
docker compose start arcanist
```

With PostgreSQL, add `docker compose exec postgres pg_dump -U arcanist arcanist > arcanist.sql`.
