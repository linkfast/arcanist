# Arcanist with Docker Compose

Two ready Compose files, for Linux, macOS or Windows with Docker:

| File | Database | For |
|---|---|---|
| [`compose.yaml`](compose.yaml) | SQLite, in the data volume | Most homes: up to about 150,000–200,000 songs and around ten people listening at once. |
| [`compose.postgres.yaml`](compose.postgres.yaml) | PostgreSQL 17, in a container next to it | Large libraries (more than about 250,000 songs) or many people listening at once. |

A server on SQLite can move to PostgreSQL later from the panel (Database page), without these files.

## Install

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

## Settings

All in `.env`:

- `MUSIC_DIR`: the folder of your music on the host (required).
- `ARCANIST_PORT`: the port on the host (8484).
- `ARCANIST_TAG`: the image's version. `preview` follows the newest release (there are only pre-releases for now),
  `latest` the newest stable one, and a version such as `0.1.0-alpha.1` stays on it.
- `TZ`: your time zone, such as `Europe/Madrid`.
- `POSTGRES_PASSWORD`: only for `compose.postgres.yaml`.

Everything else is set up in the panel.

## Update

```bash
docker compose pull
docker compose up -d
```

The database is migrated when Arcanist starts. Pin `ARCANIST_TAG` to a version to choose when to update.

## Folders and permissions

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

## Optional features

- **Server audio** (playing through the host's sound card): uncomment `devices` and `group_add`. Linux only.
- **DLNA** finds TVs and speakers with multicast on the local network, which a container with its own network does
  not see. For DLNA, use `network_mode: host` instead of `ports` (Linux only).
- **Behind a reverse proxy** (HTTPS): add its address or network to `environment`, so Arcanist sees the clients'
  real addresses: `Arcanist__Network__TrustedProxies__0: 172.18.0.0/16` (one entry per number: `__1`, `__2`…).

## Back up

The panel's Backups page copies the music, the users' data and, encrypted, the server itself, to a folder, S3 or
another Arcanist. To copy the volumes by hand, stop the server first:

```bash
docker compose stop arcanist
docker run --rm -v arcanist_arcanist-data:/data -v "$PWD":/backup alpine tar czf /backup/arcanist-data.tgz -C /data .
docker compose start arcanist
```

With PostgreSQL, add `docker compose exec postgres pg_dump -U arcanist arcanist > arcanist.sql`.
