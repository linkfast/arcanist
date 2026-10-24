# Using a server as an app's only library

How an app with a library of its own (Ultimate Desktop in "migrated" mode, for one) uses an Arcanist server as its
**only** library and does there everything it does with local files: edit tags and covers, delete, show properties,
rename libraries, show the storage used, choose where uploads go, refresh incrementally, copy music to its own disk
and administer backups.

**GraphQL (`POST /graphql`, subscriptions over WebSocket on `/graphql`) is the primary API**; every operation below
has a REST mirror under `/rest/v1` with the same name in PascalCase as its `operationId` (see `docs/api/openapi.json`),
listed for reference. Errors are codes (`extensions.code` in GraphQL, `code` in REST problem details; validation:
`validation_failed` with per-field codes in `extensions.errors`). See [README.md](README.md) for signing in, paging
and the rest of the API.

## Detecting support

`serverInfo { features }` (`GET /rest/v1/server-info`, no sign-in) lists what the server has. The ones this page
uses:

| Feature | What it means |
|---|---|
| `TRACK_EDITING` | `editTrack`, `editTracks`, `Track.canEdit`, permission `EDIT_METADATA` |
| `TRACK_DELETION` | `deleteTrack`, `deleteTracks`, `Track.canDelete`, permission `DELETE_TRACKS` |
| `ARTWORK_EDITING` | `setTrackArtwork`, `removeTrackArtwork`, `setAlbumArtwork`, `removeAlbumArtwork` |
| `TRACK_PROPERTIES` | the extra fields of `Track` and `TrackFile` below |
| `STORAGE_USAGE` | `myStorage` |
| `UPLOAD_TARGET` | `libraryId` on `POST /rest/v1/uploads`, `myUploadSettings` / `setMyUploadSettings` |
| `CHANGES_SINCE` | `changedSince` on `tracks` and `playlists`, `TrackPage.serverTime`, `trackIds`, `catalogChanged` |
| `LIBRARY_RENAME` | `renameLibrary` |
| `VIRTUAL_LIBRARIES` | "All" (`libraries(includeVirtual: true)`), the whole server as one library |

`myExportFiles` / `myExportData` arrived with the same version as these (check `TRACK_PROPERTIES`).

## Permissions

The user's `permissions` (`me { permissions }`) say what to offer; administrators have all of them.

| Permission | Needed for | Refused with |
|---|---|---|
| `EDIT_METADATA` | editing tags and covers | `forbidden` (403) |
| `DELETE_TRACKS` | deleting tracks | `forbidden` (403) |
| `UPLOAD` | uploads, choosing an upload library | `forbidden`; `libraryId` → `not_found` in `setMyUploadSettings` |
| `DOWNLOAD` | `myExportFiles` and downloading originals | `forbidden` (403) |
| administrator (role `ADMIN`) | backups and restores, migration codes | `forbidden` (403) |

Besides the permission, a library where the user **only listens** (`readOnlyLibraryIds` of their account,
`Library.canUpload` false) refuses changes with `library_read_only` (403). Sensitive administrator operations (creating
a backup, its destination, restoring, a migration code) also need a recent password: `reauthentication_required` (403)
→ `POST /rest/v1/auth/reauthenticate` with the password, then retry.

## All libraries as one

- `libraries(includeVirtual: true, language: "en")` puts "All" first (`virtual: true`, id
  `a11a11a1-0000-4000-8000-000000000001`). As a `libraryId` (or with no `libraryId` at all) `tracks`, `albums`,
  `artists`, `matchTracks`, `trackIds` cover every library the user can access, **including ones added later**.
- Nothing is uploaded to "All": uploads name a real library or use the user's default (below).

## Track properties

```graphql
query Track($id: ID!) {
  track(id: $id) {
    id title discNumber trackNumber durationMs year genres comment
    album { id title } albumArtist { id name }
    artists { artistId name role }            # PRIMARY, FEATURED, COMPOSER, LYRICIST, CONDUCTOR…
    bpm taggedBpm key { notation camelot confidence }
    hasLyrics artworkId addedAt updatedAt
    rating starred playCount lastPlayedAt
    musicBrainzRecordingId musicBrainzReleaseId acoustId
    libraryIds canEdit canDelete
    files {
      id libraryId path size format bitRate sampleRate bitDepth channels durationMs
      contentHash addedAt modifiedAt cueTrack
      replayGainTrackGain replayGainAlbumGain
      loudness { integrated range truePeak trackGain albumGain }
      streamUrl
    }
  }
}
```

- `files` are the copies the user can reach, best quality first. `path` is relative to the library's folder (for
  libraries of other servers and Ultimate Cloud, the name the server gives the copy). `contentHash` is Arcanist's hash
  of the audio only (tags excluded): equal on every Arcanist for the same audio.
- `bpm`, `key` and `loudness` come from the server's analysis (null until done); `taggedBpm` is what the tags say.
- `comment` and `taggedBpm` of files catalogued before this version appear after the next full scan or an edit.
- `canEdit` / `canDelete` are hints for menus (permission, library the user adds to, a writable kind of source, not a
  cue-sheet track); the operation may still answer `read_only` (a read-only folder).

REST: `GET /rest/v1/tracks/{id}`.

## Editing tags

```graphql
mutation Edit($id: ID!, $input: TrackEditInput!) {
  editTrack(id: $id, input: $input) { id title album { id title } artists { name role } year genres updatedAt }
}
```

```json
{
  "id": "01a16d36-ca1b-7471-80b9-cef41ca7ad3e",
  "input": {
    "title": "Riff (Live)",
    "artists": ["The Band", "Guest"],
    "albumArtist": "The Band",
    "album": "Loud Live",
    "genres": ["Rock", "Live"],
    "year": 2001,
    "trackNumber": 3,
    "discNumber": 1,
    "composers": ["Writer"],
    "lyricists": [],
    "conductor": "",
    "comment": "Recorded live",
    "bpm": 128.5,
    "lyrics": "[00:12.00]First line\n[00:15.50]Second line",
    "musicBrainzRecordingId": null,
    "musicBrainzReleaseId": null
  }
}
```

- **Every field is optional**: left out or `null` = unchanged. To clear: `""` for text, `[]` for lists, `0` for numbers,
  the nil UUID `00000000-0000-0000-0000-000000000000` for ids. `title` cannot be cleared. Lists cannot contain `;`
  (it separates values in tags). `"A feat. B"` in `artists` credits B as featured. Lyrics may be LRC (then they are
  synchronized); a `.lrc` file next to the audio still wins over them.
- Changing `album` or `albumArtist` without a `musicBrainzReleaseId` clears the release id of the files (otherwise the
  release would keep the track in its old album).
- The tags are written **into every copy's file** (each is copied, tagged, read back and put in place of the original,
  which goes to the folder's `.arcanist-trash/<date>/`, kept 30 days), then read into the catalog as a scan would. The
  answer is the track as it is now. **Its id stays**, with its ratings, plays and playlist entries; it changes only when
  the new tags make it the same song as another track of its album (same disc, number and title, or MusicBrainz
  recording), which it joins with its data. Files keep their names and folders ("Organize" in the panel moves them).
- Copies in libraries of other servers or Ultimate Cloud keep their own tags (they are not files of this server).

Several tracks at once (multi-edit, an album):

```graphql
mutation EditMany($input: EditTracksInput!) { editTracks(input: $input) { id album { id title } } }
```

```json
{ "input": { "trackIds": ["…", "…"], "changes": { "album": "Loud (Remaster)", "year": 2020 } } }
```

Up to 1000; returns them in the order asked.

Errors: `forbidden` (no `EDIT_METADATA`), `library_read_only`, `not_found` (a track the user cannot see),
`read_only` (no copy the server can write: other servers, Ultimate Cloud, a read-only folder), `cue_sheet_track` (part
of an album in one file with a cue sheet), `tags_unsupported` (a format without tags), `source_unavailable` (the
folder or bucket does not answer), `write_failed` (the file could not be put back; the original returns), `busy` (a
scan or import kept the library for 30 s: try again), `validation_failed`.

REST: `PATCH /rest/v1/tracks/{id}` (body: the input), `POST /rest/v1/tracks/edit`.

## Covers

```graphql
mutation Cover($id: ID!, $image: String!) { setTrackArtwork(id: $id, image: $image) { id artworkId } }
mutation AlbumCover($id: ID!, $image: String!) { setAlbumArtwork(id: $id, image: $image) { id artworkId } }
mutation NoCover($id: ID!) { removeTrackArtwork(id: $id) { id artworkId } }
mutation NoAlbumCover($id: ID!) { removeAlbumArtwork(id: $id) { id artworkId } }
```

```json
{ "id": "…", "image": "data:image/jpeg;base64,/9j/4AAQSkZJRg…" }
```

- `image` is base64 or a data URL; JPEG or PNG are embedded as they are, other formats (WebP, GIF…) as JPEG. At most
  20 MB (`image`: `too_many`); not an image: `image`: `invalid_format`.
- `setTrackArtwork` embeds it as the front cover of the track's files (replacing their pictures) and makes it the
  track's own image; an album without a cover takes it too. `removeTrackArtwork` removes the pictures and the track's
  own image: it then shows its album's cover, if any.
- `setAlbumArtwork` / `removeAlbumArtwork` do it for every file of the album's tracks and the album's cover. A cover
  file in the album's folder (`cover.jpg`…) may bring a removed cover back at the next full scan.
- Images: `GET /rest/v1/artwork/{artworkId}?size=512` (public, cache forever: a new cover has a new id).
- Same errors as editing.

REST: `PUT /rest/v1/tracks/{id}/artwork` and `PUT /rest/v1/albums/{id}/artwork` with the image bytes as the body
(`image/jpeg`, `image/png`…); `DELETE` the same paths.

## Deleting

```graphql
mutation Delete($input: DeleteTracksInput!) { deleteTracks(input: $input) { trackId status code } }
```

```json
{ "input": { "trackIds": ["…", "…"], "libraryId": null } }
```

```json
{ "data": { "deleteTracks": [
  { "trackId": "…", "status": "DELETED", "code": null },
  { "trackId": "…", "status": "NOT_DELETABLE", "code": "read_only" }
] } }
```

- The files go to the trash of their folder (`.arcanist-trash/<date>/`, purged after 30 days; with a `.lrc` next to
  them) and leave the catalog at once. A track left without copies leaves with its ratings, StarTracks, plays and
  playlist entries. With `libraryId`, only the copies in that library go; the track stays with the others.
- There is no "remove from the library but keep the file": the next scan would bring it back.
- One outcome per track: `DELETED`, `NOT_FOUND`, `FORBIDDEN` (`library_read_only`), `NOT_DELETABLE` (`read_only`,
  `cue_sheet_track`), `FAILED` (`write_failed`). Up to 1000 per call. Without `DELETE_TRACKS`, the whole call is
  `forbidden`.
- One track: `deleteTrack(id: ID!, libraryId: ID): Boolean!`, which fails with the outcome's code.

REST: `POST /rest/v1/tracks/delete`, `DELETE /rest/v1/tracks/{id}[?libraryId=]` (204).

## Renaming a library

```graphql
mutation Rename($id: ID!) { renameLibrary(id: $id, input: { name: "Rock & Roll" }) { id name } }
```

Needs `MANAGE_LIBRARIES` (on a library the user can access) or to own the library (a personal one: the user's own
Ultimate Cloud). `name`: `duplicate` when another library has it (ignoring case and accents), `required`,
`invalid_format`; `library_virtual` for "All", `library_managed` for libraries kept by an extension. (`updateLibrary` still
changes everything at once, for managers.) REST: `PUT /rest/v1/libraries/{id}/name`.

## Storage used

```graphql
query { myStorage {
  files bytes uploadLibraryId uploadFreeBytes
  libraries { libraryId name kind files tracks bytes freeBytes totalBytes canUpload }
} }
```

- One entry per library the user can access, in the order of `libraries`. `bytes` is what its files take (an album in
  one file with a cue sheet counts once); `freeBytes` / `totalBytes` are those of the disk of its folder (null for S3,
  other servers, Ultimate Cloud).
- `uploadLibraryId` is where the user's uploads that name no library go now, and `uploadFreeBytes` its free space.

REST: `GET /rest/v1/me/storage`.

## Where uploads go

- `POST /rest/v1/uploads?fileName=…&libraryId=…` (uploads are REST only: the body is the file) puts it in that
  library. Without `libraryId`: the target of `wishId`, else the **user's own default**, else the server's default
  (`uploadSettings`), else `upload_library_required` (400). `POST /rest/v1/libraries/{id}/uploads` still works.
- The user's default, for the whole server as one library:

```graphql
query { myUploadSettings { libraryId libraryName effectiveLibraryId effectiveLibraryName } }
mutation($id: ID) { setMyUploadSettings(input: { libraryId: $id }) { libraryId effectiveLibraryId } }
```

`libraryId` must be a library the user can upload to (`libraryId`: `not_found` otherwise); `null` goes back to the
server's default. `effectiveLibraryId` is where an upload without a library goes now (null: none, offer to choose).

## Refreshing incrementally

1. First read: page through `tracks(first: 500, after: …)` and keep the **first page's** `serverTime`.
2. Later: `tracks(changedSince: <that time>)` returns only tracks the catalog changed after it (tags, covers, copies
   added) or that the user rated or played after it. Keep the new first page's `serverTime`.
3. Removed tracks: `trackIds(libraryId: …) { ids serverTime }` lists every track id the user can play, sorted: those you
   have that are not there left (deleted, gone from disk, no longer shared with the user).
4. `playlists(changedSince: …)` lists playlists changed after it; deleted ones are not listed (the full `playlists`
   says which are left). Entries: `playlist(id) { … }` / `playlistEntries`.
5. StarTracks: read `starTracks` whole (small) when `starTracksChanged` fires.
6. A rating or StarTrack **removed** elsewhere does not bring its track back in step 2 (nothing of the track changed):
   refresh ratings with `starTracks` (step 5) or, in five-star mode, re-read the ratings of the tracks you show.

Live, instead of polling:

```graphql
subscription { catalogChanged { libraryIds updatedTrackIds removedTrackIds scanned changedAt } }
```

It fires after edits, covers and deletions through the API (with the ids) and after a scan that added, updated or
lost files (`scanned: true`, no ids: refresh with steps 2 and 3). Only libraries the user can access are told.
Connect with `connection_init` `{ "Authorization": "Bearer …" }` (see [README.md](README.md#live-updates)).

REST: `GET /rest/v1/tracks?changedSince=2026-10-24T09:00:00Z`, `GET /rest/v1/tracks/ids`,
`GET /rest/v1/playlists?changedSince=…` (no subscriptions in REST).

## Copying the music to the app's own disk

### As a user ("local backup", no administrator needed)

```graphql
query Files($after: String) {
  myExportFiles(first: 500, after: $after, libraryId: null) {
    totalCount nextCursor
    items { trackId fileId libraryId path size contentHash format modifiedAt cueTrack downloadUrl }
  }
}
query { myExportData }
```

- `myExportFiles` pages every file the user can play (optionally one library) in a stable order; pass `nextCursor` as
  `after` until it is null. Needs `DOWNLOAD` (`forbidden`).
- `downloadUrl` (relative to the server) sends the original **with its tags** as an attachment and supports HTTP
  `Range`, so an interrupted copy resumes (`Range: bytes=<what you have>-`). It carries a token valid a few hours for
  the session; after that, page again or send the `Authorization` header. Downloads never count as plays.
- Skip what you have by `contentHash` (and `size`). Keep `path` to rebuild the library's layout under a folder per
  `libraryId`. Rows with `cueTrack > 0` share their file: copy it once (with its `.cue`, which is not listed).
- `myExportData` is a JSON document (as a string) in the backups' format ([../backups.md](../backups.md)):
  `{ "catalog": { "tracks": […], "albums": […] }, "userData": { "users": [ { "ratings", "playCounts", "plays",
  "playlists" } ] } }`, with each track's title, artists, album, numbers, MusicBrainz id, AcoustID and audio hashes, so
  the app (or another Arcanist) recognises them. Any signed-in user, only their own data.

REST: `GET /rest/v1/me/export/files?first=&after=&libraryId=`, `GET /rest/v1/me/export/data`.

### As an administrator: the migration export

The server-to-server migration API ([migration.md](migration.md)) also serves a client that copies whole libraries
(every file of the folders, not only audio: covers, lyrics, cue sheets):

1. `createMigrationCode { code expiresAt }` (administrator, recent password; valid 24 h; `revokeMigrationCode(id)`).
2. Every request carries `X-Arcanist-Migration: <code>` (wrong or expired: 403 `migration_code_invalid`; repeated
   failures: 429 `too_many_requests`).
3. `GET /rest/v1/export/manifest`: libraries (`files`, `bytes`, `tracks`), users, extensions.
4. `GET /rest/v1/export/libraries/{id}/files[?after=<path>&first=<n>]`: `[{ path, size, modifiedAt, contentHash }]`
   in ordinal path order; page with the last `path` as `after` (up to 10,000 per page; without both, the whole list).
   `contentHash` is null for what is not catalogued audio.
5. `GET /rest/v1/export/libraries/{id}/file?path=<path>`: the file as stored, with `Range` (resumable).
6. `GET /rest/v1/export/userdata?users=<id,id>`: users' data in the backups' format.

These export endpoints are REST only and outside the OpenAPI document (they are not the public contract).

## Backups (administrators)

Everything is in GraphQL; all of it needs the administrator role (`forbidden` otherwise) and the operations marked ★
a recent password (`reauthentication_required`). Explain to a non-administrator that backups are the server
administrator's, and offer the personal copy above instead.

| Operation | What |
|---|---|
| `backupDestinations`, `createBackupDestination(input)` ★, `testBackupDestination(id)`, `deleteBackupDestination(id)` ★ | folders of the server, S3 buckets, other Arcanist servers (P2P) |
| `backups`, `backup(id)`, `createBackup(input)` ★, `updateBackup(id, input)` ★, `deleteBackup(id)` ★ | what to back up and when (`schedule`: `MANUAL`, `HOURS` + `intervalHours`, `DAILY`/`WEEKLY` + `timeOfDay`, `dayOfWeek`, `timeZone`), retention |
| `runBackup(id)`, `cancelBackupRun(id)`, `backupRuns(backupId)`, `backupRun(id)` | running now and following it |
| `storedBackups(destinationId)`, `backupSnapshotContents(destinationId, folder, snapshot)` | what can be restored |
| `startRestore(input)` ★, `cancelRestore(id)`, `restoreJobs`, `restoreJob(id)` | restoring files and user data, never overwriting |
| subscriptions `backupJobUpdated`, `restoreJobUpdated` | progress |

```graphql
mutation Create($input: CreateBackupInput!) { createBackup(input: $input) { id name schedule nextRunAt } }
```

```json
{ "input": {
  "name": "Music every night", "destinationId": "…", "scope": "LIBRARY", "scopeIds": ["…"],
  "includeUserData": true, "includeServer": false, "password": null,
  "schedule": "DAILY", "timeOfDay": 180, "timeZone": "America/Lima", "retentionDays": 30, "enabled": true
} }
```

`scope` is `SERVER`, `LIBRARY`, `ALBUM` or `PLAYLIST` (`scopeIds` the ids); `includeServer` (database, settings, keys,
encrypted with `password`, at least 8 characters: `backup_password_required`) only for `SERVER` and `LIBRARY`. The
format of what lands in the destination is in [../backups.md](../backups.md). See the schema for every field and
`backup_*` error code.
