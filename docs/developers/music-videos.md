# Music videos

Arcanist keeps the music videos of its libraries and plays them with the **song's audio from the library** wherever
the video plays the song, and with the video's own audio elsewhere: an intro, dialogue, a scene between two parts,
the end. A live performance keeps its own audio unless the library has that very recording. This guide is for
players: how to find a song's videos, play them, let the user switch audio, and report plays.

`serverInfo.features` lists `MUSIC_VIDEOS` on servers that have them.

## Where videos come from

- **Files in the libraries** (MP4, M4V, MKV, WebM, MOV). A video named like an audio file of its folder
  (`03 Song.mp4` next to `03 Song.flac`) is that song's; otherwise its tags or file name (`Artist - Title (Official
  Video)`) name the song. Words such as *live*, *en vivo* or *unplugged* make it a live performance, linked only to a
  live track.
- **Uploads**: `POST /rest/v1/tracks/{id}/video?fileName=clip.mp4&client=WEB` with the file as the body (UPLOAD
  permission) puts it next to the track's copy in a library the user adds to. A video sent to
  `POST /rest/v1/libraries/{id}/uploads` without a track goes where the library's template puts the artist's music
  videos. With `wishId`, a video goes next to the wish's song.
- **Extensions**, such as YouTube Audio, which can find a song's video and bring it.

Managers (MANAGE_LIBRARIES or EDIT_METADATA) can link a video by hand (`linkMusicVideo`), give it back to the automatic
linking (`unlockMusicVideo`), mark it live (`setMusicVideoLive`), align it again (`realignMusicVideo`) and delete it
(`deleteMusicVideo`, DELETE_TRACKS).

## Finding them

- `Track.videos`: the videos of a track's song (also those linked to another track of the same recording).
- `musicVideos(input: { libraryId, search, linked, live, trackId, after, first })` /
  `GET /rest/v1/videos`: every video the user can see, by title, with keyset pagination.
- `musicVideo(id)` / `GET /rest/v1/videos/{id}`: one, with its `segments`.

A `VideoClip` has its `title` and `artist`, `durationMs`, `width` and `height`, `isLive`, `artworkId` (a frame of the
video, served by the artwork endpoint) and `trackId` (its song, when the libraries have it).

## Playing them

`sync` says whether the song's audio can play with it:

| `sync` | Meaning |
| --- | --- |
| `READY` | Aligned: `trackAudioUrl` plays the song's audio where the video plays it. `coverage` is the share of the video that does. |
| `NO_MATCH` | Aligned, but another performance: it plays with its own audio. |
| `PENDING` | Not aligned yet (it happens in the background, a second or two per video): its own audio until then. |
| `UNAVAILABLE` | No song, or nothing to align with (another server's copy, no ffmpeg). |

The URLs carry a stream token (`st`), so they work in `<video>` elements:

- `trackAudioUrl`: MP4 with the song's audio where it matches (the video's own elsewhere, with its loudness matched
  and short crossfades at each switch).
- `videoAudioUrl`: MP4 with the video's own audio.
- `streamUrl`: one of the two, as the user chose (`audioMode`: `TRACK` or `VIDEO`). Users choose in their account
  (`myVideoSettings` / `setMyVideoSettings`); administrators set the default (`videoSettings`).

**Both have the same timeline.** To switch audio while playing, load the other URL and seek to the same position.
`X-Arcanist-Video-Audio` says whose audio a response has (`video` until the alignment is ready).

Append `maxHeight`, `maxBitRate` (bits per second) or `rendition` (`native`, `2160p` … `360p`, from `versions`) to
choose a version. A version not made yet streams while ffmpeg makes it: fragmented MP4 without ranges (seek by asking
again with `t`, in seconds, as for audio). Once made it is kept and served with ranges.

### Adaptive streaming

`hlsUrl`, when the server prepared the video (ahead of time, or after someone played it), is an HLS master playlist:
the picture in every prepared height and the audio as alternative renditions in group `audio`: `NAME="Song"` (the
song's audio) and `NAME="Video"` (its own), the default per the user's choice. Switching renditions (hls.js:
`hls.audioTrack = index`; Safari: `video.audioTracks`) changes the audio without touching the picture. Every URI in
the playlists carries the token.

## Segments

`musicVideo(id).segments` lists, in order, the stretches of the video and whose audio plays in each with the song's
audio on: `TRACK` stretches give `trackStartMs` (where the song is when the stretch starts), `rate` (song seconds per
video second, 1 unless the video runs a little faster or slower) and `similarity`. Players can draw them on a seek bar.

## Reporting plays

A music video played with the song's audio is the song played: report it with `recordPlay` for its `trackId`, with the
time watched, as for audio.

## DLNA and Subsonic

DLNA devices find the videos of the published libraries in the **Music videos** section (when the administrator adds
it), each with the server's default audio first and the other second. Subsonic apps get them from `getVideos`, and
`stream` takes `audioTrack=track|video` (`getVideoInfo` lists both).
