# Jukebox Jams

A Jukebox Jam is a live queue several people add songs to while it plays on **one** player of the host's. Users of the
server take part with their account; when the server allows it, guests without one join with the Jam's link or QR
code. A Jam can be voted on (the most voted songs play first), and its host can be Ultimate Desktop running its AI DJ,
which everyone in the Jam is told about.

The server has it when `serverInfo.features` lists `JUKEBOX_JAM` (and `JUKEBOX_JAM_AI_DJ` for the DJ session). Every
operation is in GraphQL and, for clients that cannot use it, in REST under `/rest/v1/jams` and
`/rest/v1/public/jams/{code}` (see `/rest/docs`). Errors come as codes (see [README.md](README.md#errors)).

## Starting one

`startJukeboxJam(input: {name, playsOn, rendererId, voting, allowGuests, guestLibraryIds})`, for any user:

| `playsOn` | Where it plays |
|---|---|
| `CLIENT` | A client of the host's that implements the host API below: a web player tab (Webamp does), Ultimate Desktop, a phone app. |
| `SERVER` | The server's own audio output (needs `PLAY_ON_SERVER` and the Server audio extension). |
| `DLNA` | A DLNA renderer (`rendererId`, from `dlnaRenderers`). |

For `SERVER` and `DLNA` the server plays it: it gives the renderer the next song (and the one after, without a gap),
follows it and does what the host's controls ask. Plays count for the host.

The Jam has a playlist of its own (`playlistId`, of type `JUKEBOX_JAM`): what played stays there as a normal playlist of
the host's when the Jam ends. `guestLibraryIds` limits the songs that can be added to some of the host's libraries
(all of them when null). `allowGuests` needs the server's setting (`sharing.jamGuests`, off at first) and the
`SHARE_LINKS` permission.

`myJukeboxJams` lists the Jams the user hosts or takes part in. `jukeboxJam.url` is the Jam's link, relative to the
server (a player's page, `/players/<id>/#/jam/<code>`): share it or show it as a QR code.

## Taking part

`jukeboxJam(code, guestToken)` is the Jam as the one asking sees it:

- `nowPlaying`: the entry playing (or `external`: a song the host plays that is not on the server), whether it is
  paused, and `positionMs` at `at` (it moves on from there while it plays).
- `queue`: the songs waiting, in the order they will play: pinned first, then (voting Jams) the most voted, then by
  arrival. Each entry has its song (title, artists, album, duration, artwork), who asked for it, its votes, whether
  the DJ added it.
- `played`: the last songs played.
- `dj`: the DJ session, if there is one (below).
- `you`: who is asking (`HOST`, `MANAGER`, `MEMBER`, `GUEST`, `VISITOR`), whether they can control it, when they can
  vote again (`nextVoteAt`) and, for a guest, how many songs they can still add (`addsLeft`).
- To those who control it: `guests` and `guestLibraryIds`.

A signed-in user calls `joinJukeboxJam(code)` (it returns null) and becomes a member of its playlist. Someone without
an account calls `joinJukeboxJam(code, name)` and gets a guest session: send its `token` as `guestToken` (GraphQL) or
in the `X-Arcanist-Guest` header (REST) from then on. Without an account only Jams open to guests exist (not found
otherwise); a guest the host removed gets `jam_guest_removed`.

- `jukeboxJamSearch(code, query)`: the songs that can be added (full-text search, in the Jam's libraries).
- `addToJukeboxJam(code, trackIds)`: up to 50 at once. A guest adds a few every 10 minutes (the server's setting);
  more is `jam_guest_limit` with `retryAfterSeconds`.
- `voteJukeboxJam(code, entryId)`: voting Jams only (`jam_voting_off`). Anyone taking part votes as often as they like,
  but **once every 30 seconds** in the whole Jam: sooner is `jam_vote_too_soon` (HTTP 429) with `retryAfterSeconds`.
  A song added in a voting Jam comes with a vote of whoever added it, which does not make them wait.

The host and those who manage the playlist control it: `controlJukeboxJam(code, {action, entryId, position})` with
`SKIP`, `PAUSE`, `RESUME`, `REMOVE`, `PIN` (plays next, whatever the votes say), `UNPIN` and `MOVE` (Jams without
voting); `removeJukeboxJamGuest`; and the host `updateJukeboxJam` (name, voting, guests, libraries) and `endJukeboxJam`.

### Following it live

The subscription `jukeboxJamChanged(code)` (anyone with the code, also without an account: send `connection_init`
without a token) says what changed: `QUEUE`, `NOW_PLAYING`, `SETTINGS`, `DJ`, `HOST`, `GUESTS`, `ENDED` or `COMMAND`.
Read the Jam again with `jukeboxJam`. Clients that cannot subscribe poll `GET /rest/v1/public/jams/{code}`.

## The host API: playing a Jam on a client

Any client of the host's can play a `CLIENT` Jam, in this cycle:

1. `claimJukeboxJamHost(code, {deviceId, kind, playerId, name, version})`. `deviceId` names this tab or device (keep it
   while it lives); `kind` is `WEB`, `DESKTOP`, `MOBILE` or `OTHER`; `playerId` a web player's id. Another device of the
   host's claiming it takes over: the one before gets a `HOST` change whose `hostDeviceId` is not its own, and must stop.
2. `jukeboxJamNext(code)`: the song to play next (`entryId` and the `track`, with its `streamUrl` for the host), or null.
   Ask again whenever the Jam changes (`QUEUE`, `SETTINGS`): votes and pins change what comes next. Keep the next song
   ready before the one playing ends, to play without a gap.
3. `reportJukeboxJamPlayback(code, {deviceId, entryId, external, state, positionMs})` every 5 seconds and on every
   change (`PLAYING`, `PAUSED`, `STOPPED`). Reporting an entry takes it out of the queue: it is playing. Reports from
   another device are `jam_not_host`.
4. On a `COMMAND` change, do `command`: `SKIP` (play the next one now), `PAUSE`, `RESUME`.
5. On `ENDED`, stop.

`jukeboxJam.hostClient` shows the client to everyone (`online` while it reported in the last minute). A minimal host in
a browser, with the API helpers left out:

```js
const deviceId = sessionStorage.deviceId ??= crypto.randomUUID();
await gql(`mutation($c: String!, $i: ClaimJukeboxJamHostInput!) { claimJukeboxJamHost(code: $c, input: $i) { code } }`,
  { c: code, i: { deviceId, kind: "WEB", playerId: "my-player", name: "My Player" } });

async function playNext() {
  const { jukeboxJamNext: next } = await gql(`query($c: String!) { jukeboxJamNext(code: $c) { entryId track { streamUrl } } }`, { c: code });
  if (!next) return;
  audio.src = next.track.streamUrl;
  await audio.play();
  report(next.entryId);
}

let entryId = null;
function report(id = entryId) {
  entryId = id;
  return gql(`mutation($c: String!, $i: ReportJukeboxJamPlaybackInput!) { reportJukeboxJamPlayback(code: $c, input: $i) { code } }`,
    { c: code, i: { deviceId, entryId, state: audio.paused ? "PAUSED" : "PLAYING", positionMs: Math.round(audio.currentTime * 1000) } });
}

audio.onended = playNext;
setInterval(report, 5000);
subscribe(`subscription($c: String!) { jukeboxJamChanged(code: $c) { kind command hostDeviceId } }`, { c: code }, ({ jukeboxJamChanged: c }) => {
  if (c.kind === "COMMAND" && c.command === "SKIP") playNext();
  if (c.kind === "COMMAND" && c.command === "PAUSE") audio.pause();
  if (c.kind === "COMMAND" && c.command === "RESUME") audio.play();
  if ((c.kind === "HOST" && c.hostDeviceId !== deviceId) || c.kind === "ENDED") audio.pause();
});
playNext();
```

A web player that implements it declares `"features": ["jukebox-jam-host"]` in its manifest
([frontends.md](frontends.md)).

## Sessions with Ultimate's AI DJ

Ultimate Desktop can host a Jam and let its AI DJ run it (`JUKEBOX_JAM_AI_DJ`):

- `setJukeboxJamDj(code, {kind: ULTIMATE_AI_DJ, name, message})` when the DJ session starts (and again to change its
  `message`, a short line such as "Raising the tempo for the closing set"); `kind: NONE` when it ends. Host only.
- While there is a DJ, **the host chooses the order**: `reportJukeboxJamPlayback` takes any waiting entry, not only
  `jukeboxJamNext`'s. Votes and requests go on working: they are what the DJ reads to know what the floor wants
  (`queue[].votes`, `addedBy`).
- The DJ adds songs nobody asked for with `addToJukeboxJam(code, trackIds, source: DJ)` (no limit, no vote; the entries
  say `addedByDj`). A song it plays from Desktop's own library, not on the server, is reported as `external {title,
  artists, album, durationMs, artworkUrl}`: shown to everyone, never added to the playlist.
- The DJ is taken off by itself when the host's client has not reported for 5 minutes.

`jukeboxJam.dj` (`{kind, name, message, since}`) reaches everyone, guests too. **Players show it**: a visible band that
says it is a session with Ultimate's AI DJ, with the DJ's name and its message, a badge on the songs it added, and,
while there is a DJ, that the DJ chooses the order and votes tell it what people want. Webamp does it with a band in
ULTIMATE's colors above the queue.

## Ending

`endJukeboxJam(code)` (host), or 12 hours without activity: the code no longer works (`jam_ended` for changes;
`jukeboxJam` still answers with `endedAt`), what was waiting is dropped, and what played stays as a normal playlist of
the host's (members keep it).
