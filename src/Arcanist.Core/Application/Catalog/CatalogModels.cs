using System.Text;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public sealed record Artist(Guid Id, string Name, string? SortName, Guid? MusicBrainzId);

public sealed record ArtistReference(Guid Id, string Name);

/// <param name="ArtworkId">Cover image, served by the artwork endpoint.</param>
/// <param name="TrackCount">Tracks of the album the user can play.</param>
public sealed record Album(
    Guid Id,
    string Title,
    ArtistReference Artist,
    int? Year,
    bool IsCompilation,
    Guid? MusicBrainzId,
    Guid? ArtworkId,
    int TrackCount,
    long DurationMs);

public sealed record AlbumReference(Guid Id, string Title);

public sealed record ArtistCredit(Guid ArtistId, string Name, ArtistRole Role);

public enum KeyMode
{
    Major,
    Minor,
}

/// <summary>Musical key estimated from the audio.</summary>
/// <param name="Tonic">Pitch class of the tonic: 0 = C, 1 = C♯/D♭ … 11 = B.</param>
/// <param name="Notation">Usual chord-symbol name, e.g. "C", "F#m", "Bbm".</param>
/// <param name="Camelot">Camelot wheel code used for harmonic mixing, e.g. "8A" for A minor.</param>
/// <param name="Confidence">0–1. Low values mean an ambiguous or atonal piece.</param>
public sealed record MusicalKey(int Tonic, KeyMode Mode, string Notation, string Camelot, double Confidence);

/// <summary>EBU R128 measurements of a copy, with the ReplayGain 2.0 gains derived from them.</summary>
/// <param name="Integrated">Integrated loudness, LUFS.</param>
/// <param name="Range">Loudness range, LU.</param>
/// <param name="TruePeak">Highest true peak, linear (1.0 = full scale).</param>
/// <param name="TrackGain">Gain to play this track at −18 LUFS, dB.</param>
/// <param name="AlbumGain">Gain to play the whole album at −18 LUFS, keeping its internal dynamics, dB. Null until
/// every track of the album is analyzed.</param>
/// <param name="AlbumPeak">Highest true peak of the album, linear.</param>
public sealed record Loudness(double Integrated, double Range, double TruePeak, double TrackGain, double? AlbumGain, double? AlbumPeak);

/// <summary>One stored copy of a track. A track can have several (e.g. FLAC and MP3).</summary>
/// <param name="ReplayGainTrackGain">From the file's tags, if it has them.</param>
/// <param name="Loudness">Measured by Arcanist; null until the copy has been analyzed.</param>
/// <param name="StreamUrl">Plays this copy. Relative to the server; valid for a few hours for the current session.</param>
/// <param name="LibraryId">The library it is in.</param>
/// <param name="Path">Where it is, relative to its library's folder ('/'-separated); for libraries of other servers
/// and Ultimate Cloud, the name the server gives it.</param>
/// <param name="ContentHash">Arcanist's hash of its audio (tags excluded): the same audio has the same hash on every
/// Arcanist, however it is tagged or named, so importers can skip what they have.</param>
/// <param name="AddedAt">When the server found it.</param>
/// <param name="ModifiedAt">When the file last changed (its tags too).</param>
/// <param name="CueTrack">0 for a whole file; for an album in one file split by a cue sheet, the number of this track.</param>
public sealed record TrackFile(
    Guid Id,
    string? Format,
    int? BitRate,
    int? SampleRate,
    int? BitDepth,
    int? Channels,
    long Size,
    int DurationMs,
    double? ReplayGainTrackGain,
    double? ReplayGainAlbumGain,
    Loudness? Loudness,
    string StreamUrl,
    Guid LibraryId,
    string Path,
    string ContentHash,
    DateTime AddedAt,
    DateTime ModifiedAt,
    int CueTrack);

/// <summary>A line of synchronized lyrics, shown from <paramref name="TimeMs"/> until the next line.</summary>
public sealed record LyricsLine(int TimeMs, string Text);

/// <summary>A track's lyrics.</summary>
/// <param name="Plain">The whole text (also available when only synchronized lyrics exist).</param>
/// <param name="Synced">Time-synchronized lines, when known.</param>
/// <param name="Instrumental">The track has no lyrics.</param>
/// <param name="SyncedSource">Where the timing of <paramref name="Synced"/> came from (a file's plain lyrics may be timed
/// with an online service's).</param>
public sealed record TrackLyrics(
    Guid TrackId, string? Plain, IReadOnlyList<LyricsLine>? Synced, LyricsSource Source, bool Instrumental, LyricsSource? SyncedSource = null);

/// <summary>Overview of a copy's audio for seek bars: peak amplitudes from start to end.</summary>
/// <param name="Peaks">1000 values from 0 (silence) to 255 (full scale), evenly spread over the duration.</param>
public sealed record Waveform(Guid TrackId, Guid FileId, int DurationMs, IReadOnlyList<int> Peaks);

/// <param name="ArtworkId">The track's own image, or else its album's.</param>
/// <param name="Bpm">Tempo measured from the best copy; null until analyzed, or when there is no steady beat.</param>
/// <param name="Key">Key estimated from the best copy; null until analyzed.</param>
/// <param name="HasLyrics">Whether trackLyrics has lyrics for it (instrumental tracks included).</param>
/// <param name="Files">Copies of the track in libraries the user can access, best quality first.</param>
/// <param name="StreamUrl">Plays the best copy. Relative to the server; it carries a token valid for a few hours for
/// the current session, so it works where headers cannot be sent (e.g. an HTML audio element).</param>
/// <param name="Rating">The user's rating, 1–5; null when unrated. With the single-star rating mode only 5 (starred)
/// is reported.</param>
/// <param name="Starred">It is one of the user's StarTracks (the star, or five stars).</param>
/// <param name="PlayCount">How many times the user played it.</param>
/// <param name="LibraryIds">The libraries the user can access with a copy of it.</param>
/// <param name="Year">Release year of its album; null when unknown.</param>
/// <param name="MusicBrainzRecordingId">The recording on MusicBrainz, when identified.</param>
/// <param name="AcoustId">The recording's AcoustID, when identified.</param>
/// <param name="AlbumArtist">The artist of its album.</param>
/// <param name="MusicBrainzReleaseId">Its album's release on MusicBrainz, when identified.</param>
/// <param name="Comment">The comment in its tags.</param>
/// <param name="TaggedBpm">The tempo its tags say, which may differ from the measured <paramref name="Bpm"/>.</param>
/// <param name="UpdatedAt">When the catalog last changed it (its tags, a new copy): what <c>changedSince</c> compares.</param>
/// <param name="CanEdit">The user may edit its tags and cover (EDIT_METADATA, and copies in libraries they add to and the
/// server writes). A hint: the server may still answer <c>read_only</c>.</param>
/// <param name="CanDelete">The user may delete it (DELETE_TRACKS, and every copy where the server can remove files).</param>
public sealed record Track(
    Guid Id,
    string Title,
    AlbumReference Album,
    IReadOnlyList<ArtistCredit> Artists,
    int? DiscNumber,
    int? TrackNumber,
    int DurationMs,
    Guid? ArtworkId,
    IReadOnlyList<string> Genres,
    DateTime AddedAt,
    double? Bpm,
    MusicalKey? Key,
    bool HasLyrics,
    IReadOnlyList<TrackFile> Files,
    string StreamUrl,
    int? Rating,
    bool Starred,
    int PlayCount,
    DateTime? LastPlayedAt,
    IReadOnlyList<Guid> LibraryIds,
    int? Year,
    Guid? MusicBrainzRecordingId,
    Guid? AcoustId,
    ArtistReference AlbumArtist,
    Guid? MusicBrainzReleaseId,
    string? Comment,
    double? TaggedBpm,
    DateTime UpdatedAt,
    bool CanEdit,
    bool CanDelete);

/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record ArtistPage(IReadOnlyList<Artist> Items, string? NextCursor, int TotalCount);

/// <summary>How albums are listed.</summary>
public enum AlbumSort
{
    /// <summary>By title.</summary>
    Title,
    /// <summary>Those with music added most recently first.</summary>
    RecentlyAdded,
    /// <summary>At random, the same order all day (for the same seed).</summary>
    Random,
}

public sealed record AlbumPage(IReadOnlyList<Album> Items, string? NextCursor, int TotalCount);

/// <param name="ServerTime">When the page was read: pass the first page's as <c>changedSince</c> the next time.</param>
public sealed record TrackPage(IReadOnlyList<Track> Items, string? NextCursor, int TotalCount, DateTime ServerTime);

/// <summary>Opaque keyset cursor: the sort key and id of the last item of a page.</summary>
internal static class Cursor
{
    public static string Encode(string key, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id:N}{key}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (string Key, Guid Id) Decode(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return (text[32..], Guid.ParseExact(text[..32], "N"));
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
        {
            throw ArcanistException.Field("after", ErrorCodes.InvalidFormat);
        }
    }
}
