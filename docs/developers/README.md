# Building clients for Arcanist

Arcanist exposes everything a music player needs through two equivalent APIs. This guide covers what you need to
write a player or any other client. Clients that work with Arcanist are expected to work the same with Ultimate
Cloud, which implements the same contract.

| | |
|---|---|
| GraphQL (primary) | `POST /graphql`, subscriptions over WebSocket at `/graphql` |
| REST | `/rest/v1/…`, same operations except subscriptions |
| Contracts | [`docs/api/schema.graphql`](../api/schema.graphql) and [`docs/api/openapi.json`](../api/openapi.json) |
| Explore a running server | GraphiQL at `/graphql/ui`, REST reference at `/rest/docs`, SDL at `/graphql/schema.graphql` |

Every GraphQL query and mutation has a REST operation whose `operationId` is the field name in PascalCase
(`createUser` ↔ `CreateUser`). Enum values are `UPPER_SNAKE_CASE` in both APIs. Ids are UUIDs (GraphQL `ID`).
`serverInfo.apiVersion` is the contract's major version; it only changes on breaking changes.

## Discovering a server

`GET /rest/v1/server-info` (or the `serverInfo` query) works without authentication and returns the version, API
version, whether setup is complete, the Ultimate Cloud features enabled, `features` (parts of the API that older servers lack:
`LIBRARY_FILTER`, `UPLOADS`, `TRACK_MATCHING`, `LISTENING_IMPORT`, `STAR_TRACKS`, `UPLOAD_HISTORY`, `MY_CLOUD_UPLOADS`, `VIRTUAL_LIBRARIES` and others listed in the contract) and `sourceUrl` (where to get the server's
source code, as the AGPL requires). Until setup is complete every other call answers `503` with code
`setup_required`; send the user to `/admin/setup`.

## Signing in

Authentication is REST-only.

```http
POST /rest/v1/auth/login
{ "userName": "alice", "password": "…", "deviceName": "Living room TV" }
```

The response carries `accessToken` (a JWT valid for 15 minutes), `refreshToken`, `sessionExpiresAt` and `user`.
Send the access token as `Authorization: Bearer <token>` on every API call.

- **Refreshing:** `POST /rest/v1/auth/refresh` with `{ "refreshToken": "…" }` returns new tokens. Refresh tokens
  rotate: each one works once, so always keep the latest. A session lasts 30 days from its last refresh.
- **Browser clients** served by the same Arcanist can pass `"useCookie": true` at login: the refresh token is then
  set as an HttpOnly cookie instead of returned, and `POST /rest/v1/auth/refresh` with an empty body uses it.
- **Signing out:** `POST /rest/v1/auth/logout` with the refresh token revokes the session.
- **Sensitive actions** (managing users and deleting libraries) need the password to have been entered in the last
  10 minutes. Otherwise they fail with `reauthentication_required`; call `POST /rest/v1/auth/reauthenticate` with
  `{ "password": "…" }`, use the access token it returns, and retry.

A `401` with code `unauthorized` (REST) or an error with that code (GraphQL) means the access token is missing,
expired or revoked: refresh once and retry, and sign in again if refreshing fails. Login attempts are rate-limited
per address and per account (`429 too_many_requests`, with `Retry-After`).

## Errors

Clients show their own messages, keyed by error code; never display server text.

- **REST** answers RFC 7807 problem details with a `code` member. Validation failures are `400` with
  `code: "validation_failed"` and `errors: { "<field path>": ["<code>", …] }`, e.g.
  `{ "userName": ["duplicate"], "sources[0].location": ["not_found"] }`.
- **GraphQL** errors carry the same values in `extensions.code` and, for validation, `extensions.errors`.

The codes are listed in `src/Arcanist.Core/Application/Errors.cs`.

While an administrator moves the server to another database, everything answers `503` with `code: "maintenance"`
and a `Retry-After` header, for a few minutes at most. `GET /rest/v1/maintenance` (no sign-in needed, also during
it) says whether it is going on and how far it got (`{"active": true, "phase": "copying", "percent": 40}`): show
that the server is busy, and retry. The session survives it.

## Browsing the catalog

`artists`, `albums` and `tracks` return pages: `{ items, nextCursor, totalCount }`. Ask for `first` items (default
50, at most 500) and pass the previous `nextCursor` as `after` for the next page; `nextCursor` is null on the last
page. Cursors are opaque. All three accept `search`, which ignores case and accents.

```graphql
query {
  albums(first: 50) {
    items { id title year artworkId artist { id name } trackCount durationMs }
    nextCursor
    totalCount
  }
}
```

`tracks(albumId: …)` returns the whole album in disc and track order without paging.

`albums` come by title; `sort: RECENTLY_ADDED` lists those with music added most recently first, and `sort: RANDOM`
at random, in the same order all day for the user (pass another `seed` for another order). Both still page with
`after`.

`libraries` lists the libraries the user can use. To show them apart, pass `libraryId` to `artists`, `albums` or
`tracks` (an album's counts are then those of that library); `Track.libraryIds` says which of the user's libraries
have a copy of a track. A library the user cannot use answers `not_found`.

With `libraries(includeVirtual: true)` (`VIRTUAL_LIBRARIES`), the list starts with **All** (`virtual: true`, named in
`language`: "All", "Todo"), every library the user can use at once. Its id (`a11a11a1-0000-4000-8000-000000000001`,
the same on every server) works wherever `libraryId` does and means no filter. It has no folder: nothing is uploaded
to it (`library_virtual`, and `canUpload` is false), so an app that keeps a library of its own adds it as one more
Arcanist library and never migrates its music into it.

What a user sees depends on their library access: a track appears only while one of its files is present in a
library they can use, and albums and artists appear through such tracks. Anything else answers `not_found`.

A track can have several files (the same song in FLAC and MP3, or in two libraries). `Track.files` lists the copies
the user can reach, best quality first, with format, bit rate, sample rate, bit depth and ReplayGain values.

Files move: administrators organize libraries into folders by template, and drop folders import new files (or
better copies that replace old ones). Track, album and artist ids survive all of that, so store ids, never file
paths or file ids.

## Searching

`search(query, first)` / `GET /rest/v1/search?query=…` returns the best `artists`, `albums`, `tracks` and
`playlists` at once (`first` per kind: 10 by default, at most 50), best match first. Every word must start a word of
the name, its artists or its album (`ros staral` finds "Starálfur" by Sigur Rós); accents and case never matter. The
`search` argument of `artists`, `albums` and `tracks` uses the same rules, keeping their alphabetical paging.

## Playing audio

Each `Track` has a `streamUrl`, and each of its `files` has its own. They are relative to the server and carry a
signed `st` parameter, so they work where headers cannot be sent, such as an HTML `<audio>` element:

```html
<audio src="https://music.example/rest/v1/stream/0199…?st=…"></audio>
```

- Stream URLs are valid for 6 hours and only while the session that produced them is active. Fetch the track again
  to get a fresh one.
- `GET`/`HEAD /rest/v1/stream/{trackId}` also accepts `Authorization: Bearer`. It serves the original file with
  HTTP range support (`206 Partial Content`), so players can seek, and an `ETag` that changes only when the audio
  does. Add `fileId` to pick a copy.
- `download=true` sends the original as an attachment and requires the `DOWNLOAD` permission.

### Transcoding

When ffmpeg is available, the server converts audio for playback on slow connections or devices that cannot play
the original. `serverInfo.transcodingFormats` lists what it can produce (`OPUS`, `MP3`, `AAC`, `FLAC`; empty
without ffmpeg). Add parameters to a stream URL:

| Parameter | Effect |
|---|---|
| `format=opus\|mp3\|aac\|flac` | Convert to that format (Opus in Ogg, MP3, AAC in ADTS, lossless FLAC). |
| `maxBitRate=128` | Bit rate in kbit/s (clamped to the format's range). Alone, it converts to MP3 only if the original exceeds it. |
| `timeOffset=93.5` | Start that many seconds in. |

Converted audio is streamed as it is produced: there is no `Content-Length` or range support, so players seek by
requesting the URL again with `timeOffset`. `X-Content-Duration` gives the remaining duration in seconds. A song the
server converted before (its conversion cache, or the versions it makes ahead of time) comes as a file instead: with
`Content-Length`, `Accept-Ranges: bytes` and ranges, like an original; check `Accept-Ranges` rather than assuming
either. Without
ffmpeg, or when too many conversions are running, the answer is `503` with code `transcoding_unavailable`: fall back
to the original.

Tracks of albums stored as one file with a cue sheet are parts of that file, so they are always sent converted (as
FLAC when the file is lossless, otherwise as MP3, unless `format` says otherwise) and need ffmpeg on the server.

## Audio analysis

Arcanist analyzes every track in the background after scanning it. Until a copy is analyzed, the fields below are
null (and the waveform answers `not_found`); clients should cope with that.

- **Loudness normalization:** each entry of `Track.files` has `loudness` with the EBU R128 measurements and
  ReplayGain 2.0 gains for a −18 LUFS reference: `trackGain` (dB) for shuffled playback, `albumGain` for whole
  albums (null until all of the album's tracks are analyzed), and the linear `truePeak` / `albumPeak` to avoid
  clipping when applying positive gain. `replayGainTrackGain` and `replayGainAlbumGain` are the file's own tags, when
  it has them.
- **Tempo and key:** `Track.bpm` (null when there is no steady beat) and `Track.key` with the tonic, mode, usual
  notation (`F#m`), Camelot code (`11A`) and a 0–1 confidence.
- **Waveform:** `trackWaveform(trackId, fileId)` / `GET /rest/v1/tracks/{trackId}/waveform` returns 1000 peak values
  (0–255) spread evenly over the duration, for seek bars. It is a separate call so track lists stay small.

## Track analysis for DJ software (AI extension)

With the **AI** extension switched on (it starts off), Arcanist analyzes every song for DJ software and AI-assisted
clients such as the ULTIMATE AI DJ: beat grid, bars and phrases, named sections, energy, voice, silences, mix points
and scratch points, so a client can mix without downloading the audio. `TRACK_ANALYSIS` is in
`serverInfo.features` while the extension is on and the server can analyze, and `serverInfo.trackAnalysis` gives the
progress (`analysisVersion`, `analyzed`, `pending`, `failed`). With the extension off, these operations answer 409
`ai_disabled`.

Local songs are analyzed in the background. Songs on S3, Ultimate Cloud or another Arcanist server are analyzed when
a client asks for them (once per audio: copies with the same content share it); if the administrator turned that off,
they answer `analysis_unavailable`.

`trackAnalysis(trackId, include)` / `GET /rest/v1/tracks/{trackId}/analysis?include=`:

- `status`: `READY`, `PENDING` (REST answers 202, with the song's `position` in line; asking puts it in line) or
  `FAILED` (with `errorCode`).
- `stale: true`: the analysis is from an earlier version of the analyzer and is being redone. It is still usable.
- `include` is a comma-separated list of blocks: `beats`, `onsets`, `energy`, `vocals`, `sections`, `silences`,
  `cues`, `loudness`, `key` and `stats`. Without it you get all but `stats`. Blocks not asked for are null. The tempo,
  phrases, fades and `musicStart`/`musicEnd` always come.
- Times are seconds from the start of the song. Dense series are base64: times as float32 little-endian, levels as
  one byte per step (0–255).
- When `tempo.stable` is true the tempo holds within 1% through the song: the grid is `firstBeat + n × 60 / bpm`, a
  bar every `beatsPerBar` beats from `firstDownbeat`, and `beats`/`downbeats` are not sent. Otherwise they list every
  beat and bar.
- `sections` have a `label` (the same letter for parts that sound alike), a `name` (`intro`, `verse`, `build`,
  `chorus`, `drop`, `break`, `bridge`, `outro`, or `section` when unsure), their `energy` (0–1), a `trend`
  (`rising`, `steady`, `falling`) and which `occurrence` of that label they are. `highlight` is the most
  representative chorus or drop.
- `mixPoints`: `introEnd`, `outroStart`, and downbeats that start a phrase without voice, good for mixing in (`mixIn`)
  and out (`mixOut`).
- `scratchCues` (at most 12, best first): short isolated sounds (`vocal`, `stab` or `drum`), with `t` at the attack.
- `vocals.presence` is how likely a voice is at each step: a light estimate, not a separation.

An example (No Limit, by 2 Unlimited, with `include=sections,silences,cues,loudness,key`; trimmed):

```json
{
  "trackId": "01a14cf2-54c4-77c8-91bd-27609330f692",
  "status": "READY",
  "stale": false,
  "analysisVersion": 1,
  "durationSeconds": 223.686,
  "musicStart": 0,
  "musicEnd": 222.47,
  "tempo": { "bpm": 141.02, "confidence": 0.76, "stable": true, "beatsPerBar": 4, "firstBeat": 0.236, "firstDownbeat": 1.513, "beats": null, "downbeats": null },
  "phrases": [{ "start": 1.513, "bars": 8 }, { "start": 15.127, "bars": 4 }, { "start": 21.935, "bars": 8 }],
  "loudness": { "integratedLufs": -9.5, "range": 2.5, "truePeakDb": -0.4 },
  "key": { "tonic": "Bb", "mode": "major", "camelot": "6B", "confidence": 0.24 },
  "energy": null,
  "vocals": null,
  "onsets": null,
  "silences": [{ "start": 222.47, "end": 223.685, "kind": "trailing" }],
  "fades": { "fadeInEnd": null, "fadeOutStart": null },
  "sections": [
    { "start": 1.513, "end": 21.935, "label": "A", "name": "intro", "energy": 0.63, "trend": "steady", "occurrence": 1 },
    { "start": 69.586, "end": 90.008, "label": "E", "name": "chorus", "energy": 0.96, "trend": "steady", "occurrence": 1 },
    { "start": 139.361, "end": 152.976, "label": "E", "name": "chorus", "energy": 1, "trend": "steady", "occurrence": 2 }
  ],
  "highlight": { "start": 139.361, "end": 152.976, "confidence": 0.61 },
  "mixPoints": { "introEnd": 21.935, "outroStart": 200.627, "mixIn": [1.513, 15.127, 21.935, 35.549], "mixOut": [152.976, 180.205, 185.311] },
  "scratchCues": [
    { "t": 148.722, "duration": 0.22, "kind": "vocal", "isolation": 0.84, "strength": 0.69 },
    { "t": 7.265, "duration": 0.43, "kind": "stab", "isolation": 0.89, "strength": 0.97 }
  ],
  "stats": null
}
```

The whole analysis of a four-minute song is about 15–20 KB.

- **Prioritize:** `prioritizeTrackAnalysis(trackId)` / `POST /rest/v1/tracks/{trackId}/analysis/prioritize` puts a
  song first in line (for example, the next ones in a set).
- **Keep a cache:** `trackAnalyses(updatedSince, after, first, include)` / `GET /rest/v1/tracks/analysis` lists the
  analyses of the songs the user sees, from the least to the most recently analyzed, up to 200 a page
  (`after`/`nextCursor`). Pass the latest `analyzedAt` you hold as `updatedSince` to get only what changed.
- **Live:** the `trackAnalysisReady` subscription sends `{ trackId, analysisVersion }` when a song the user sees is
  analyzed.
- **Hear a moment:** `GET /rest/v1/tracks/{trackId}/audio-window?start=148.7&duration=4` returns that piece decoded,
  as a WAV file of 16-bit stereo at 44.1 kHz (`duration` at most 8 seconds), for scratching or previewing without
  downloading the song. It authenticates like `/stream` (Bearer, or the `st` of a `streamUrl`) and needs ffmpeg
  (`transcoding_unavailable` otherwise).

### Listening statistics per song

`trackStats(scope, updatedSince, after, first, timeZone)` / `GET /rest/v1/stats/tracks?scope=ME` (the
`TRACK_STATS` feature; it does not need the AI extension) gives, for each song played, its counted `plays`, `skips`
and where they happen on average (`avgSkipPosition`, seconds), `completions` (listened to 90% or to the last 10
seconds), `avgListenRatio` (0–1), `lastPlayedAt` and `hourHistogram` (24 counts, in the IANA `timeZone` you pass).
Songs come from the least to the most recently played; pass the latest `lastPlayedAt` you hold as `updatedSince`.
`scope: SERVER` is everyone's plays together, with no names: the administrator can stop sharing it (it answers 403
then, except to users who may see everyone's statistics). `include=stats` in the analysis adds the user's
statistics of that song.

## Lyrics

`Track.hasLyrics` says whether `trackLyrics(trackId)` / `GET /rest/v1/tracks/{trackId}/lyrics` has something
(otherwise it answers `not_found`). The result has the whole text in `plain`, time-synchronized `synced` lines
(`timeMs`, `text`; each shown until the next) when known, `instrumental` for tracks without words, and `source`:
`SIDECAR` (a `.lrc` file next to the audio), `EMBEDDED` (the file's tags) or `ONLINE`.

## Ratings and StarTracks

The songs a user loves are their **StarTracks**, as every Ultimate app calls them. `serverInfo.ratingMode` says how
this server rates tracks, and your interface should follow it:

- `STARS`: one to five stars; five stars is a StarTrack. `rateTrack(trackId, rating)` /
  `PUT /rest/v1/tracks/{trackId}/rating` with 1–5, or null to clear it.
- `STAR`: the StarTrack alone. `starTrack(trackId, starred)` / `PUT /rest/v1/tracks/{trackId}/star`. `rateTrack`
  only accepts 5 here.

A StarTrack is a five-star track, so `starTrack` works in both modes and the administrator can switch modes without
anyone losing theirs. `Track.rating` (null when unrated; only 5 in `STAR` mode), `Track.starred` (a StarTrack),
`Track.playCount` and `Track.lastPlayedAt` are the signed-in user's. `Track.year` is its album's release year, and `Track.musicBrainzRecordingId` /
`Track.acoustId` identify the recording when it is known (to match it with the same song elsewhere). `ratedTracks(minRating)` /
`GET /rest/v1/me/rated-tracks` lists their rated tracks, most recently rated first, with `after`/`nextCursor` paging.

With the `STAR_TRACKS` feature in `serverInfo.features`:

- `starTracks(after, first)` / `GET /rest/v1/me/star-tracks`: the user's StarTracks, most recently starred first,
  each with its `track`, the song's `acoustId` (when identified) and `starredAt`.
- `addStarTracks(input: { items })` / `POST /rest/v1/me/star-tracks` and `removeStarTracks` /
  `POST /rest/v1/me/star-tracks/remove`: up to 1000 items, each a `trackId` or an `acoustId` (as Ultimate Cloud's
  StarTracks name songs). Adding by AcoustID stars one of the song's tracks here, unless one already is; removing
  takes every track of the song. The answer says how many `changed` and which items were `notFound`.
- The `starTracksChanged` subscription tells when the user's StarTracks (or other ratings) change, here or through
  their Ultimate Cloud account: read them again then.

## Reporting plays

Report each track the user listened to, when it ends or is skipped:

```graphql
mutation {
  recordPlay(input: { trackId: "…", playedMs: 183000, playedAt: "2026-10-12T21:04:00Z", client: "My Player" }) {
    counted
    skipped
  }
}
```

`playedMs` is the time actually played (pauses excluded); `playedAt` is when playback started (now when omitted).
The server decides what counts as a play, the same for every player: at least 30 seconds, and half the track or
four minutes. Stopping within the first 30 seconds of a longer track is a skip, and the user's mixes avoid tracks
they keep skipping. Plays up to 14 days old are accepted, so queue them while offline and send them later; sending
the same report twice is harmless.

`playHistory` / `GET /rest/v1/me/plays` lists the user's plays, newest first. `listeningStats(period, timeZone)` /
`GET /rest/v1/stats/listening?period=WEEK` gives their plays, time listened, top tracks, artists, albums and genres,
a timeline with one entry per day (week and month) or month (year and all time), and their plays by hour of the day
(`playsByHour`, 24 entries) and by weekday (`playsByWeekday`, 7, Sunday first). Pass the user's IANA time zone so
days and hours are theirs.

## Playlists

`playlists` lists the user's own, those they collaborate on and public ones; `Playlist.role` says what they can do:
`OWNER` (everything), `COLLABORATOR` (add, remove and reorder tracks) or `VIEWER` (listen and `copyPlaylist`).
`playlistEntries(playlistId, after, first)` pages through the tracks in order. Entries, not tracks, are removed and
moved (`removePlaylistEntries`, `movePlaylistEntries`), since a track can appear twice. Tracks from libraries the user
cannot access stay in the playlist but are not listed for them. `artworkIds` has up to four covers for a collage.

## Wish lists

With `WISH_LIST` in `serverInfo.features`, each user keeps a list of songs they want. `addWish` /
`POST /rest/v1/me/wishes` takes a `title` and an `artist` (both required), and optionally an `album` and a
`durationMs` (give or take three seconds) that narrow it, a `musicBrainzRecordingId` or an `acoustId` that identify it
exactly, and a `note`. When a library the user can play has it already, it comes back `OBTAINED` with
`foundWhenAdded: true`; otherwise it is `WANTED`, and the server looks for it after every scan (so also after uploads,
imports and syncs): by MusicBrainz recording, AcoustID, or title and artist, the same rules as `matchTracks`.

`wishList(status)` / `GET /rest/v1/me/wishes` lists the wanted ones first and then those that arrived (the history),
each with its `track` and `matchedBy`, plus `wanted` and `unseen` counts. The `wishObtained` subscription tells the
user as soon as one arrives; `markWishesSeen` (`POST /rest/v1/me/wishes/seen`) clears `unseen`. `updateWish` changes
one (another song is wanted again) and `removeWish` drops it. Every user sees only their own.

## Apps with a library of their own

An app that keeps its own library (Ultimate Desktop, for one) can use the server next to it, or move to it:

- **Matching:** `matchTracks` / `POST /rest/v1/tracks/match` takes up to 1000 `candidates`, each with the app's own
  `key` and what it knows (`contentHash`, `acoustId`, `musicBrainzRecordingId`, `title`, `artist`, `album`,
  `discNumber`, `trackNumber`, `durationMs`), and answers each key with the server's `trackId` (or null) and
  `matchedBy`: the same audio first, then the MusicBrainz recording, the AcoustID, and last the same title, artist and
  album (with the duration within three seconds). `libraryId` limits it to one library. A candidate with a
  `contentHash` that is not found yet but may be soon (uploaded, its library not scanned since, or a scan going on)
  comes back with `pending: true`: ask again later.
- **Uploading:** `POST /rest/v1/libraries/{id}/uploads?fileName=…` with the file as the body
  (`application/octet-stream`, up to 2 GB) adds it to a library the user can use. It needs the `UPLOAD` permission and
  a library folder the server can write to (`read_only` otherwise); `Library.canUpload` says beforehand whether both
  hold. The file is placed with the library's folder
  template and never overwrites anything: `201` with `status: IMPORTED`, its `contentHash` and `path`, or `200` with
  `status: DUPLICATE` when the library already has that audio (with its `trackId` once catalogued). The library is
  scanned shortly after the last upload of a batch; then `matchTracks` finds the new tracks by `contentHash`.
- **The user's uploads** (`UPLOAD_HISTORY`): `myUploads` / `GET /rest/v1/me/uploads` lists them newest first, with
  `active` (how many are still on their way: worth asking again in a few seconds). An upload into a library is
  `PROCESSING` until the library's scan catalogues it (`ADDED`, with its `track`), or `DUPLICATE`, `NOT_ADDED` (the
  scan did not find it) or `FAILED` (with `errorCode`). `clearMyUploads` / `DELETE /rest/v1/me/uploads` forgets the
  finished ones. `source` names the extension that brought a file (such as `youtube`); it is null
  for the files the user sent.
- **Sending songs to the user's own Ultimate Cloud** (`MY_CLOUD_UPLOADS`): `uploadTracksToMyCloud` /
  `POST /rest/v1/me/uploads/ultimate-cloud` with up to 1000 `trackIds` queues them; each one shows in `myUploads` as
  `QUEUED`, `UPLOADING`, `UPLOADED`, `ALREADY_IN_CLOUD` or `FAILED`. It needs the `DOWNLOAD` permission (it gives away
  a copy) and an Ultimate Cloud account of the user's own (`cloud_not_linked`; `myUltimateCloud` says whether there is
  one). A song only in a library another Arcanist shares goes only if that server allows downloads
  (`download_not_allowed`); one that only exists as part of a file split by a CUE sheet cannot go
  (`cloud_upload_cue`).
- **Ratings and play counts:** `importListening` / `POST /rest/v1/me/listening/import` brings up to 1000 `ratings`
  (1–5; a star is 5) and `playCounts` (`playCount`, `lastPlayedAt`) per call. Ratings only fill gaps and play counts
  keep the highest, so sending them twice changes nothing. Unknown tracks are counted as `ignored`.
- **Playlists:** `createPlaylist` with the matched `trackIds` (up to 10,000 per playlist).

## Home page, mixes and artist pages

- `home` / `GET /rest/v1/home` returns the user's sections in order. Each has a `kind` (title it in your own
  language) and one list with items: `albums` (`RECENTLY_PLAYED`, `RECENTLY_ADDED`, `MOST_PLAYED`, `REDISCOVER`,
  `NEVER_PLAYED`), `mixes` (`MIXES`), `tracks` (`STARRED`) or `playlists` (`PLAYLISTS`). Empty sections are left out.
- `mixes` / `GET /rest/v1/mixes` lists mixes made for the user; `mix(id)` / `GET /rest/v1/mixes/{id}` returns one's
  tracks in play order. A mix has a `kind` (`ARTIST`, `GENRE`, `DECADE`, `FAVORITES`, `DISCOVERY`, `TRACK`) and a
  `subject` (the artist or genre name, or the decade) to build its title. Mixes stay the same all day and change the
  next. Any `artist:<id>`, `genre:<id>`, `decade:<year>` or `track:<id>` (ids without dashes) is a valid mix id:
  `track:<id>` is a radio that starts with that track.
- `artistOverview(id)` / `GET /rest/v1/artists/{id}/overview` has what an artist page shows: top tracks, albums
  (newest first), albums the artist appears on, related artists, the artist's mix and an `artworkId`.

## Artwork

Albums and tracks have an `artworkId`. Images are public, so they work directly in `<img>`:

```
GET /rest/v1/artwork/{artworkId}?size=256
```

`size` (64, 128, 256, 512 or 1024; other values round up) returns a JPEG whose longest side fits; without it you
get the original. Responses are immutable: cache them forever.

## Live updates

Subscriptions use the `graphql-transport-ws` WebSocket protocol (the older `graphql-ws` also works) at `/graphql`.
Browsers cannot send headers on WebSockets, so put the token in the connection payload:

```json
{ "type": "connection_init", "payload": { "Authorization": "Bearer <access token>" } }
```

The token is checked when connecting; an open subscription is not closed when the token later expires. Available
subscriptions are listed in the schema's `Subscription` type (for example `scanJobUpdated`). REST clients poll
instead, e.g. `GET /rest/v1/scans/{id}`.

## Web players served by Arcanist

Arcanist serves the default web player at `/` and every installed player at `/players/<id>/`, on the same origin as
the APIs, so players need no CORS setup and can use the cookie-based refresh described above. Their client-side
routes fall back to their entry page; API paths and the prefixes `/api`, `/media`, `/album-art`, `/transcode`,
`/shared` and `/ext` are reserved and never reach them. Administrators install players from the admin panel: see
[Packaging a web player](frontends.md).

### Extension apps

With the `EXTENSION_APPS` feature, `extensionApps(language)` / `GET /rest/v1/extension-apps` lists pages that
installed extensions offer the user (such as one to bring songs from YouTube): its `name` and `description` in the
language asked for, the page's `url`, an `iconUrl` and a preferred `width` and `height`. Only those of running
extensions whose permissions the user has are listed. Show them as you show your own views, and open one in a
window (an `iframe`) or a tab of its own. The app gets the session, the language and your look from your player
through the **host protocol** (`postMessage`; see [Writing a server extension](extensions.md#the-host-protocol)):
answer its `arcanist:hello` with `arcanist:session`, send that again whenever your token is refreshed or your theme
changes, and handle `arcanist:token-expired` and `arcanist:open`.

## Reaching the server from anywhere

Servers with the P2P network on can be reached without opening ports: a device pairs once with a QR code and then
talks to the server through a local port, with the same API. See [p2p.md](p2p.md).

## Playing to DLNA devices

When an administrator switches DLNA on, players can send music to speakers and TVs on the server's local network:

- `dlnaRenderers` lists the renderers found (an administrator can add one by its description URL with
  `addDlnaRenderer`); `searchDlnaRenderers` asks the network again and returns the list a few seconds later.
- `playOnDlnaRenderer(input: { rendererId, trackIds, startIndex })` replaces what the renderer plays with a queue of
  tracks the user can see. Arcanist picks the best format each renderer says it plays (the lossless original, then
  FLAC, LPCM and MP3), gives it the next track ahead for gapless playback when it can, and records the plays for that
  user as they end.
- `dlnaRenderer(id)` tells what it is doing (state, position, volume, the queue and the current track, and
  `startedByYou`); poll it every second or two while showing it.
- `editDlnaQueue(input: { rendererId, action, trackIds, index, toIndex })` changes the queue while it plays: `APPEND`
  or `INSERT_NEXT` tracks, `REMOVE` or `MOVE` an entry (not the one playing for `REMOVE`) or `JUMP` to one. The track
  playing goes on and the renderer is handed the new next track.
- `controlDlnaRenderer(input: { rendererId, action, positionMs })` plays, pauses, stops, skips (`NEXT`,
  `PREVIOUS`) or seeks, and `setDlnaRendererVolume(rendererId, volume)` sets the volume (0–100).

A queue playing on a renderer belongs to who started it: while it plays, only they (or an administrator) can control
it, change its queue or replace it; anyone else gets `renderer_in_use` until it stops or runs out.

Errors: `dlna_disabled`, `renderer_unreachable` (switched off or gone), `renderer_failed` (it refused),
`nothing_queued` (skipping without an Arcanist queue on it) and `renderer_in_use`.

### The server's own speakers

With the server audio extension on, the server itself plays through an audio device attached to it (speakers, a USB
DAC, HDMI). It is one more renderer, with the id `server`, listed first by `dlnaRenderers` even while DLNA is off, to
administrators and users with the `PLAY_ON_SERVER` permission; everything above works on it the same way, with the
same rule about who controls a queue. Its errors add `server_audio_disabled` (the extension is off) and
`server_audio_unavailable` (the server cannot play audio).

Apps of the Ultimate family also find it as an Ultimate Connect device of each linked Ultimate Cloud account (named
as the administrator chose; `X-Client-App: arcanist-server-audio`). It takes every command type; `PLAY_ITEMS` and
`ENQUEUE_NEXT` find each item by `localTrackId` (a track id of this server), `acoustId`, `cloudTrackId` (a song of
that account's Ultimate Cloud library catalogued here) or, last, title and artist, and leave out what the server does
not have. `PLAY_PLAYLIST` takes an Arcanist playlist's id as `localPlaylistId`. Commands run as the account's user:
for the server's account, the administrator who linked it; for a user's own account, that user.

## Subsonic and mStream apps

Arcanist also speaks the Subsonic API (1.16.1 with OpenSubsonic extensions, at `/rest/{method}[.view]`) and the
mStream API (at `/api/v1/…`, `/media`, `/album-art` and `/transcode`), through built-in extensions an administrator
switches on. Those apps never use the user's password: users create an **app password** in their account
(`createAppPassword`) and enter it in the app, as the password, as a Subsonic token (`t`/`s`) or as an OpenSubsonic
API key. New clients should use the native APIs above instead.

## Licensing

Arcanist is AGPL-3.0-or-later. The project's intent is that clients which only talk to the server over these APIs
are separate programs that may use any license (Ultimate Web Player and Ultimate Desktop are built that way). Code
that runs inside the server, such as [server extensions](extensions.md), must be AGPL-compatible. This is not legal
advice.
