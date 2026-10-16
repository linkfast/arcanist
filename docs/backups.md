# Backups: the format in the destination

Arcanist backs up into a folder of the server, an S3 bucket or another Arcanist server. Everything a restore needs is
in the destination, so a backup can be restored by another server, by a fresh installation (the setup wizard's
"Restore from a backup") or, if it ever comes to that, by hand: the music is plain files.

This is format 1. A server refuses backups of a newer format (`backup_format_unsupported`).

## Layout

```
<destination>/<backup name>-<id>/
  arcanist-backup.json                      the manifest
  current/<library>/<path>                  the mirror: every file of the latest run
  versions/<snapshot>/<library>/<path>      what that run replaced or removed from current/
  snapshots/<snapshot>/index.json.gz        every file of that run
  snapshots/<snapshot>/catalog.json.gz      its tracks, to recognize them when restoring
  snapshots/<snapshot>/userdata.json.gz     ratings, play counts, history and playlists (when the backup takes them)
  snapshots/<snapshot>/playlists/<user>/<playlist>.m3u8
  snapshots/<snapshot>/server.arcb          the server itself, encrypted (when the backup takes it)
  artwork/<sha256>.<ext>                    album artwork that is not in the music folders
  in-progress/<snapshot>/<n>.json           the journal of a run that has not finished
```

- **A snapshot** is one run, named after its start in UTC: `20261016T104500Z`.
- **`<library>`** is the library's name when it was first backed up (made safe for every file system, and unique:
  `Music`, `Music (2)`…). It never changes afterwards, even if the library is renamed.
- **Paths below it** are the files' paths in the library, exactly as they are on disk.

## How runs work

Each run mirrors the libraries into `current/`:

- A file whose size and modification time did not change is not read.
- A file with the same size but another time is hashed: if its SHA-256 is the same, nothing is copied.
- A changed file's previous copy moves to `versions/<snapshot>/` before the new one is copied into `current/`.
- A file that disappeared from the library moves to `versions/<snapshot>/`.
- Nothing is overwritten: files are written under a hidden name (`.arcanist-partial-*`) and renamed when complete.
- A library folder that cannot be read (a disconnected drive or share) keeps its last copy as it is: nothing is marked
  as removed.

A run that stops halfway (a restart, a cancellation, an error) leaves its journal in `in-progress/`. The next run
finishes that same snapshot instead of starting another, so nothing is copied or versioned twice.

**Retention:** after the configured number of days (0 keeps everything), `versions/<snapshot>/` folders go. A snapshot
needs the versions of every later run to be restored, so the snapshots older than the newest one whose versions went
are removed from the manifest and from `snapshots/`.

## Restoring snapshot D by hand

1. Read `snapshots/D/index.json.gz` (gzipped JSON): its `files` are every file of D, with their path relative to
   `current/` (`p`), size (`s`), modification time (`m`) and SHA-256 (`h`).
2. For each path, find the first later snapshot S (in the manifest's order) whose index lists the path in `moved`:
   the file is `versions/S/<path>`. If no later snapshot moved it, it is `current/<path>`.
3. Check the file's SHA-256 against `h`.

For the latest snapshot every file is simply in `current/`.

## The files

### `arcanist-backup.json`

```json
{
  "format": 1,
  "id": "…",
  "name": "Everything",
  "scope": "SERVER",
  "arcanistVersion": "1.0.0",
  "machine": "NAS",
  "createdAt": "…",
  "updatedAt": "…",
  "sources": [{ "sourceId": "…", "libraryId": "…", "library": "Music", "folder": "Music" }],
  "snapshots": [{ "name": "20261016T104500Z", "createdAt": "…", "files": 1234, "bytes": 5678, "userData": true, "server": true }]
}
```

The manifest is replaced at the end of each run: the new one is written as `arcanist-backup.json.next`, the old one
deleted and the new one renamed. A `.next` file without the manifest is a run that stopped right there, and is read
instead.

### `catalog.json.gz`

The snapshot's tracks, and those its user data mentions:

- **Tracks:** id on the server that made the backup, title, artists, album, disc and number, duration, MusicBrainz
  recording id, AcoustID, the audio hashes of their copies, their files in the snapshot (best first) and lyrics found
  online.
- **Albums:** title, artist, year, MusicBrainz release id, and the name of their artwork in `artwork/`.

A restore matches each track with the restoring server's catalog by audio hash, then MusicBrainz recording, then
AcoustID, then title, artist and album (and duration, within three seconds).

### `userdata.json.gz`

Per user:

- ratings, with their dates;
- play counts;
- every play, with its original id so restoring twice adds it once;
- playlists, with their entries and collaborators (by user name).

Tracks are referred to by their id in `catalog.json.gz`.

The `.m3u8` files play the snapshot's own copies: their paths are relative (`../../../../current/<library>/<path>`).

### `server.arcb`

A zip encrypted with the backup's password. It holds:

- `arcanist.db`: the database as SQLite, also when the server used PostgreSQL;
- `arcanist.json`: its settings, pointed at that SQLite file;
- `keys/`: the keys that protect S3 secrets, the Ultimate Cloud link, app passwords and the P2P node's identity.

The encryption:

- **Header:** `ARCB1`, then the PBKDF2 iterations (int32, little endian) and a 16-byte salt.
- **Key:** PBKDF2-SHA256 of the password (UTF-8), 32 bytes.
- **Records:** up to 1 MiB of plaintext each, as AES-256-GCM. Each record is:
  - a flag byte (1 for the last record);
  - the length (int32, little endian);
  - the ciphertext;
  - its 16-byte tag.
- **Nonce:** 4 zero bytes, then the record's number (int64, big endian).
- **Associated data:** the header, the record's number (int64, big endian) and the flag byte.

Without the password the server part cannot be restored; Arcanist does not keep a way around it.
