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
version, whether setup is complete, the Ultimate Cloud features enabled and `sourceUrl` (where to get the server's
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

What a user sees depends on their library access: a track appears only while one of its files is present in a
library they can use, and albums and artists appear through such tracks. Anything else answers `not_found`.

A track can have several files (the same song in FLAC and MP3, or in two libraries). `Track.files` lists the copies
the user can reach, best quality first, with format, bit rate, sample rate, bit depth and ReplayGain values.

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
the original. `serverInfo.transcodingFormats` lists what it can produce (`OPUS`, `MP3`, `AAC`; empty without
ffmpeg). Add parameters to a stream URL:

| Parameter | Effect |
|---|---|
| `format=opus\|mp3\|aac` | Convert to that format (Opus in Ogg, MP3, AAC in ADTS). |
| `maxBitRate=128` | Bit rate in kbit/s (clamped to the format's range). Alone, it converts to MP3 only if the original exceeds it. |
| `timeOffset=93.5` | Start that many seconds in. |

Converted audio is streamed as it is produced: there is no `Content-Length` or range support, so players seek by
requesting the URL again with `timeOffset`. `X-Content-Duration` gives the remaining duration in seconds. Without
ffmpeg, or when too many conversions are running, the answer is `503` with code `transcoding_unavailable`: fall back
to the original.

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

## Lyrics

`Track.hasLyrics` says whether `trackLyrics(trackId)` / `GET /rest/v1/tracks/{trackId}/lyrics` has something
(otherwise it answers `not_found`). The result has the whole text in `plain`, time-synchronized `synced` lines
(`timeMs`, `text`; each shown until the next) when known, `instrumental` for tracks without words, and `source`:
`SIDECAR` (a `.lrc` file next to the audio), `EMBEDDED` (the file's tags) or `ONLINE`.

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

Arcanist serves the active web player at `/`, on the same origin as the APIs, so it needs no CORS setup and can use
the cookie-based refresh described above. Its client-side routes fall back to its `index.html`; API paths and the
prefixes `/api`, `/media`, `/album-art`, `/transcode`, `/shared`, `/ext` and `/players` are reserved and never
reach it. Installing alternative players from the admin panel is planned; until then a player can be placed in the
folder configured as `Arcanist:Frontends:PlayerRoot` (default `wwwroot/player` next to the server).

## Licensing

Arcanist is AGPL-3.0-or-later. The project's intent is that clients which only talk to the server over these APIs
are separate programs that may use any license (Ultimate Web Player and Ultimate Desktop are built that way). Code
that runs inside the server, such as future server extensions, must be AGPL-compatible. This is not legal advice.
