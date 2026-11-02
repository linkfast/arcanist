# Jukebox Jams

A Jukebox Jam is a shared listening session: a live queue several people add songs to and change while it plays on
**one** player of the host's. Users of the server take part with their account; when the server allows it, guests
without one join with the Jam's link or QR code. By default everyone changes what plays, and when the queue runs out
the server suggests songs for the group, so the music does not stop. A Jam can also be voted on, and its host can be
Ultimate Desktop running its AI DJ, which everyone in the Jam is told about.

The server has it when `serverInfo.features` lists `JUKEBOX_JAM` (and `JUKEBOX_JAM_AI_DJ` for the DJ session;
`JUKEBOX_JAM_SHARED_CONTROL`, `JUKEBOX_JAM_SUGGESTIONS` and `JUKEBOX_JAM_TAKE_OVER` for what this page says about them). Every
operation is in GraphQL and, for clients that cannot use it, in REST under `/rest/v1/jams` and
`/rest/v1/public/jams/{code}` (see `/rest/docs`). Errors come as codes (see [README.md](README.md#errors)).

## Starting one

`startJukeboxJam(input: {name, playsOn, rendererId, voting, allowGuests, guestLibraryIds, allowReplace, saveAsPlaylist, suggestions, queueTrackIds})`,
for any user. Only `name` and `playsOn` are needed: **start it with one tap, from what plays**, and leave the rest for later
(`updateJukeboxJam`).

`queueTrackIds` is the queue it starts with: the song playing and what comes after it in the client's list. A client that
hosts it reports the first entry as playing, without stopping it, and takes the rest out of its own list: they now wait
in the Jam, where everyone sees and changes them. Songs the Jam cannot have stay out; start with nothing when nothing plays.

| `playsOn` | Where it plays |
|---|---|
| `CLIENT` | A client of the host's that implements the host API below: a web player tab (Webamp does), Ultimate Desktop, a phone app. |
| `SERVER` | The server's own audio output (needs `PLAY_ON_SERVER` and the Server audio extension). |
| `DLNA` | A DLNA renderer (`rendererId`, from `dlnaRenderers`). |

For `SERVER` and `DLNA` the server plays it: it follows the renderer and does what the host's controls ask. Plays
count for the host. **The renderer's own queue is kept**: when the host already plays something there (or plays more
later), the Jam's next song goes right after the one playing, one at a time, and the host's own queue goes on when the
Jam has nothing waiting; meanwhile `nowPlaying.external` shows the host's song. An idle renderer starts with the Jam's.

The Jam has a playlist of its own (`playlistId`, of type `JUKEBOX_JAM`): what played stays there as a normal playlist of
the host's when the Jam ends, unless `saveAsPlaylist` is false (`JUKEBOX_JAM_SAVE_AS_PLAYLIST`; true by default, and
`updateJukeboxJam` changes it): then the playlist and the Jam are deleted when it ends. `guestLibraryIds` limits the songs that can be added to some of the host's libraries
(all of them when null). `allowGuests` (off by default) needs the server's setting (`sharing.jamGuests`, off at first) and the
`SHARE_LINKS` permission. `voting` is off by default. `allowReplace` (**on by default**) lets anyone taking part change
what plays (below): the host's switch "let others change what plays". `suggestions` (on by default) is below too.

`myJukeboxJams` lists the Jams the user hosts or takes part in. `jukeboxJam.url` is the Jam's link: absolute when the
server has a public URL (`sharing.publicUrl`), relative to the server otherwise (resolve it against the server's
address). Share it or show it as a QR code. It opens the Jam's **client**, where those taking part follow it, vote and add
songs: Ultimate Web Player when it is installed and declares `"features": ["jukebox-jam"]`, otherwise the server's own
generic client (`/players/jam/#/jam/<code>`, part of Arcanist: a Jam never depends on a player implementing one). Hosts
link to it to add songs too; a player only has to play the Jam (the host API below).

## Taking part

`jukeboxJam(code, guestToken)` is the Jam as the one asking sees it:

- `nowPlaying`: the entry playing (or `external`: a song the host plays that is not on the server), whether it is
  paused, and `positionMs` at `at` (it moves on from there while it plays).
- `queue`: the songs waiting, in the order they will play: pinned first, then (voting Jams) the most voted, then by
  their place. Each entry has its song (title, artists, album, duration, artwork), who asked for it, its votes
  (`votedByYou`), whether the DJ added it, and whether it is a suggestion (`suggested`, `likedBy`).
- `played`: the last songs played.
- `dj`: the DJ session, if there is one (below).
- `hostGone`: it plays on a client that stopped reporting (below).
- `you`: who is asking (`HOST`, `MANAGER`, `MEMBER`, `GUEST`, `VISITOR`), whether they change what plays (`canControl`,
  `canReplace`), manage it (`canManage`) or can take over (`canTakeOver`), and, for a guest, how many songs they can still
  add (`addsLeft`). `voteIntervalSeconds`, `nextVoteAt` and `nextReplaceAt` are always 0 or null: there is no wait any more.
- To those who manage it: `guests` and `guestLibraryIds`.

A signed-in user calls `joinJukeboxJam(code)` (it returns null) and becomes a member of its playlist. Someone without
an account calls `joinJukeboxJam(code, name)` and gets a guest session: send its `token` as `guestToken` (GraphQL) or
in the `X-Arcanist-Guest` header (REST) from then on. Without an account only Jams open to guests exist (not found
otherwise); a guest the host removed gets `jam_guest_removed`.

- `jukeboxJamSearch(code, query)`: the songs that can be added (full-text search, in the Jam's libraries).
- Browsing them (`JUKEBOX_JAM_BROWSE`): `jukeboxJamArtists(code, search, after, first)` (album artists, with how many
  albums), `jukeboxJamAlbums(code, artistId, search, sort, after, first)` (an artist's by year, or all by `TITLE` or
  `RECENTLY_ADDED`) and `jukeboxJamAlbumSongs(code, albumId)` (in disc and track order, each saying whether it waits in
  the queue). Pages end with `nextCursor`; pass it as `after`. Artwork ids load from `/rest/v1/artwork/{id}`, also
  without an account.
- `addToJukeboxJam(code, trackIds)`: up to 50 at once. A guest adds a few every 10 minutes (the server's setting);
  more is `jam_guest_limit` with `retryAfterSeconds`.
- `voteJukeboxJam(code, entryId)`: voting Jams only (`jam_voting_off`). **One vote per person and song**, without waiting:
  voting again takes it back (`votedByYou`). A song added in a voting Jam comes with a vote of whoever added it. Votes order
  what comes after the next song: **the song already next stays next**, because it may be on its way on the host's client
  (it shows as `pinned` until it plays).
- `replaceJukeboxJamNowPlaying(code, trackId)` (`JUKEBOX_JAM_REPLACE`): the song plays **now**, in place of the one
  playing. Anyone taking part when the Jam has `allowReplace` (`jam_replace_off` otherwise), without waiting; the host and
  those who manage it always. It is added pinned and the host's client gets a `SKIP` command, so it plays next right away.
  For a guest it counts as one of their songs.

`controlJukeboxJam(code, {action, entryId, position}, guestToken)` with `SKIP`, `PAUSE`, `RESUME`, `REMOVE`, `PIN` (plays
next, whatever the votes say), `UNPIN` and `MOVE` (Jams without voting) is for the host and those who manage the playlist,
and for **anyone taking part while the Jam has `allowReplace`**, guests too: with it off they only add songs and vote.
Managing it stays with them: `removeJukeboxJamGuest`; and the host `updateJukeboxJam` (name, voting, guests, `allowReplace`,
`suggestions`, libraries) and `endJukeboxJam`.

### When the queue runs out

With `suggestions` on (`JUKEBOX_JAM_SUGGESTIONS`), when nothing is waiting and something has played the server adds a few
songs for the group: entries with `suggested` and no `addedBy`. They follow from what just played (artist, genre, tempo and
key) and from the tastes of the host and the members: what someone likes is likelier, what someone rated low or keeps
skipping stays out. `likedBy` names who likes them, except to guests, who are not told about the tastes of the server's
users. What someone asks for goes before them, and a suggestion someone removes is not suggested again in that Jam. There
are none while a DJ chooses, and a new Jam nothing has played on does not start by itself.

A host client says it has nothing of its own left with `ownSongsLeft: 0` in its reports (below): the server never suggests
to a client that does not say so, nor while it has songs of its own coming.

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
   Ask again whenever the Jam changes (`QUEUE`, `SETTINGS`): votes and pins change what comes next. **Put it right after
   the song playing** in the client's own queue, without replacing it: the host may be playing something of theirs, or
   have songs of their own coming; those go on after the Jam's when it has none waiting. Nothing playing: start it.
   Keep it ready before the one playing ends, to play without a gap.
3. `reportJukeboxJamPlayback(code, {deviceId, entryId, external, state, positionMs, ownSongsLeft})` every 5 seconds and on
   every change (`PLAYING`, `PAUSED`, `STOPPED`). Reporting an entry takes it out of the queue: it is playing. A song of
   the host's own is `external` (title, artists, album, duration, artwork URL), shown to everyone. `ownSongsLeft` is how many
   songs of the host's own are still to play, besides the Jam's (see above). Reports from another device are `jam_not_host`.
4. On a `COMMAND` change, do `command`: `SKIP` (to the Jam's next song now: ask `jukeboxJamNext` first, a song
   someone chose to play now comes first), `PAUSE`, `RESUME`.
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

### When the host's client is gone

A `CLIENT` Jam whose client has not reported for 2 minutes has `hostGone`: nothing moves on, and everyone should be told.
Then (`JUKEBOX_JAM_TAKE_OVER`):

- Anyone with an account taking part may play it: `takeOverJukeboxJam(code)` (`you.canTakeOver`; `jam_host_online` while
  the client still reports), then claim it as any host. They become its host, and the songs are those they can listen to. The
  one who started it keeps the playlist and manages it (`MANAGER`), and their client gets `jam_not_host` if it comes back.
- Those who manage it may end it.
- After an hour like this it ends by itself.

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

`endJukeboxJam(code)` (the host; those who manage it when the host's client is gone), an hour with that client gone, or 12 hours without activity: the code no longer works (`jam_ended` for changes;
`jukeboxJam` still answers with `endedAt`), what was waiting is dropped, and what played stays as a normal playlist of
the host's (members keep it). With `saveAsPlaylist` false the playlist goes too, and the code answers not found.
