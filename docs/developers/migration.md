# Migrating between Arcanist servers

One Arcanist can bring another one over (a server moving to new hardware, or two servers merged into one): its
users, libraries and music, the users' ratings, plays, playlists and wish lists, its settings, extensions and web
players. The server that is brought over is **not changed**, and reading it never counts as plays there.

In the panel this is the **Import** page (*From another Arcanist*, and *Migrate this server to another* on the
server being brought over) and the setup wizard (*Where do you come from?* → *Another Arcanist*, also offered next to
*Restore from a backup*).

## Migration codes

The server being brought over makes a **migration code** (`createMigrationCode`, administrators with a recent
authentication): 20 letters and digits in groups of four (`XXXX-XXXX-XXXX-XXXX-XXXX`, no `0`, `O`, `1` or `I`),
valid for 24 hours or until revoked (`revokeMigrationCode`). Only its SHA-256 is kept. Whoever has it can read the
whole server, password hashes included: it is shown once.

With the P2P network on, the code comes with a link that carries the code and the server's node, so the other
server reaches it without opening ports:

```
arcanist://p2p?kind=migrate&node=<ticket, base64url>&token=<code>&name=<server name>
```

Using the link turns the P2P network of the server that reads on (during setup, only until setup ends; it is then
stored as on).

## Finding servers

`arcanistServers` lists the Arcanist servers on the local networks (the DLNA media servers whose manufacturer is
`LinkFast S.A.` and model `Arcanist`: their address is the description's host and port) and those paired through the
P2P network. Discovery is only for finding them: everything else goes through the export API.

## The export API

Server to server, outside the public contract (like the peer API), under `/rest/v1/export`. Every request carries
the code in `X-Arcanist-Migration` (letters and digits count; dashes, spaces and case do not) and, optionally, the
reading server's name in `X-Arcanist-Server` (shown next to the code as *used by*). A wrong, expired or revoked code
is `403 migration_code_invalid`; failures are rate limited by address (`429 too_many_requests`). Through the P2P
network any node may call it (the code is the authorization).

| Method | Path | Answer |
|---|---|---|
| GET | `/manifest` | `ExportManifest`: format (1), server id and name, version, libraries (files, bytes, tracks), users (role, permissions, library access, whether they have a password or a second factor), extensions, players and the default player |
| GET | `/credentials` | Each user's password hash (ASP.NET Core Identity's, which the other server verifies as it is) |
| GET | `/libraries/{id}/files` | Every file of the library's folders (audio and the rest: covers, lyrics, cue sheets), with size and date |
| GET | `/libraries/{id}/file?path=` | One file as stored, with `Range` |
| GET | `/userdata?users=<id>,<id>` | The users' data in the backups' format (`docs/backups.md`: catalog with hashes, MusicBrainz and AcoustID ids and tags; ratings, play counts, plays, playlists) and their wishes |
| GET | `/settings` | The portable settings, as stored |
| GET | `/extensions/{id}/package`, `/players/{id}/package` | An installed (not built-in) extension or player, zipped |

Only libraries with this server's own folders (local or S3) are exported: not personal libraries, nor those kept by
an extension, nor those of other servers or Ultimate Cloud.

## What the reading server does

`analyzeArcanistServer` reads the manifest to choose; `startArcanistImport` (administrators with a recent
authentication) creates or maps the users and creates the new libraries at once, then a background job
(`arcanistImports`, resumable after a restart) goes through these steps:

1. **Preparing**: the settings (with the other server's library and user ids changed to this one's) and the
   extensions and players: built-in ones on or off as there, the others from the marketplace when it has them,
   else the package from the other server, and its default player.
2. **Copying**: each file to the same path in the chosen library's folder. A file of the same size already there
   counts as present; another file there makes the copy `name (2).ext`. Nothing is overwritten.
3. **Waiting** for the libraries' scans.
4. **Applying** the users' data, matched by audio hash, MusicBrainz, AcoustID and tags: ratings fill gaps, play
   counts keep the highest, plays come once (`arcanist:<server id>:<play id>`), playlists by name, wishes by title
   and artist.

Users whose name exists here are not changed (only their data comes); one named like the setup wizard's new
administrator is that administrator. New users keep their password when asked (never app passwords, sessions,
devices or second factors) and, with permissions, their role, permissions and library access.
