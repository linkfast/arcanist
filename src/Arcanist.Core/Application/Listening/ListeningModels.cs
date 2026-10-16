using System.Globalization;
using System.Text;
using Arcanist.Core.Application.Catalog;

namespace Arcanist.Core.Application.Listening;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <param name="TrackId">The track listened to; it must be visible to the user.</param>
/// <param name="PlayedMs">How long it played, in milliseconds (pauses excluded).</param>
/// <param name="PlayedAt">When playback started; now when omitted. Plays up to 14 days old are accepted, so
/// players can report what was played offline.</param>
/// <param name="Client">The player's name, e.g. "Ultimate Web Player".</param>
/// <summary>Ratings and play counts an app kept on its own, brought to the server (migrating its library).</summary>
public sealed record ImportListeningInput(IReadOnlyList<ImportedRating>? Ratings, IReadOnlyList<ImportedPlayCount>? PlayCounts);

/// <param name="Rating">1–5; a star is 5.</param>
public sealed record ImportedRating(Guid TrackId, int Rating);

public sealed record ImportedPlayCount(Guid TrackId, int PlayCount, DateTime? LastPlayedAt);

/// <param name="RatingsAdded">Ratings set on tracks the user had not rated (those they had keep theirs).</param>
/// <param name="PlayCountsRaised">Tracks whose play count went up to the imported one.</param>
/// <param name="Ignored">Entries for tracks the user cannot play.</param>
public sealed record ListeningImportResult(int RatingsAdded, int PlayCountsRaised, int Ignored);

public sealed record RecordPlayInput(Guid TrackId, int PlayedMs, DateTime? PlayedAt, string? Client);

/// <param name="Counted">It counts as a play: at least 30 seconds, and half the track or four minutes.</param>
/// <param name="Skipped">Stopped within the first 30 seconds of a longer track.</param>
/// <param name="Duplicate">The same play was already recorded (a player sending it again); nothing changed.</param>
public sealed record RecordedPlay(Guid Id, bool Counted, bool Skipped, bool Duplicate = false);

/// <summary>A counted play in the user's history.</summary>
public sealed record PlayHistoryItem(Guid Id, Track Track, DateTime PlayedAt, int PlayedMs);

/// <param name="NextCursor">Pass as <c>after</c> to get older plays; null on the last page.</param>
public sealed record PlayHistoryPage(IReadOnlyList<PlayHistoryItem> Items, string? NextCursor);

/// <summary>A rated track (five stars: a StarTrack).</summary>
public sealed record RatedTrack(Track Track, int Rating, DateTime RatedAt);

/// <summary>
/// One of the user's StarTracks: the songs they love, as every Ultimate app calls them. With the single-star rating mode
/// a StarTrack is the star; with five stars, a five-star rating. Ultimate apps match them with Ultimate Cloud's by AcoustID.
/// </summary>
/// <param name="AcoustId">The song's AcoustID, when it was identified.</param>
public sealed record StarTrack(Track Track, string? AcoustId, DateTime StarredAt);

/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record StarTrackPage(IReadOnlyList<StarTrack> Items, string? NextCursor, int TotalCount);

/// <summary>A song to add to (or take from) the StarTracks: a track of this server, or a song by its AcoustID.</summary>
/// <param name="AcoustId">As Ultimate Cloud's StarTracks name songs: one of its tracks here (adding), or all of them (removing).</param>
public sealed record StarTrackItemInput(Guid? TrackId, string? AcoustId);

/// <param name="Items">At most 1000.</param>
public sealed record StarTracksInput(IReadOnlyList<StarTrackItemInput> Items);

/// <param name="Changed">Tracks that became StarTracks, or stopped being.</param>
/// <param name="NotFound">The items (their trackId or acoustId) that match no track the user can see.</param>
public sealed record StarTracksResult(int Changed, IReadOnlyList<string> NotFound);

/// <summary>The user's StarTracks (or other ratings) changed, here or through Ultimate Cloud.</summary>
public sealed record StarTracksChange(DateTime ChangedAt);

/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record RatedTrackPage(IReadOnlyList<RatedTrack> Items, string? NextCursor, int TotalCount);

public enum StatsPeriod
{
    Week,
    Month,
    Year,
    All,
}

public enum StatsBucket
{
    Day,
    Month,
}

/// <param name="Start">First day of the bucket, in the requested time zone.</param>
public sealed record ListeningBucket(DateOnly Start, int Plays, long ListenedMs);

public sealed record TrackPlays(Track Track, int Plays);

public sealed record ArtistPlays(ArtistReference Artist, int Plays);

public sealed record AlbumPlays(Album Album, int Plays);

/// <summary>Listening over a period, of one user or of the whole server.</summary>
/// <param name="From">Start of the period; null for all time.</param>
/// <param name="Plays">Counted plays.</param>
/// <param name="ListenedMs">Time listened, skips included.</param>
/// <param name="Timeline">Plays per day (week and month) or per month (year and all time), one entry for every day or
/// month of the period, quiet ones included.</param>
public sealed record ListeningStats(
    StatsPeriod Period,
    DateTime? From,
    int Plays,
    long ListenedMs,
    int DistinctTracks,
    StatsBucket Bucket,
    IReadOnlyList<ListeningBucket> Timeline,
    IReadOnlyList<TrackPlays> TopTracks,
    IReadOnlyList<ArtistPlays> TopArtists,
    IReadOnlyList<AlbumPlays> TopAlbums);

/// <summary>One user's listening in a period, for the server's statistics.</summary>
public sealed record UserListening(Guid UserId, string UserName, string? DisplayName, int Plays, long ListenedMs, DateTime? LastPlayedAt);

public sealed record FormatTotals(string Format, int Files, long SizeBytes);

/// <summary>What the libraries hold.</summary>
public sealed record LibraryTotals(int Artists, int Albums, int Tracks, int Files, long DurationMs, long SizeBytes, IReadOnlyList<FormatTotals> Formats);

/// <summary>The server's catalog and listening, for administrators (VIEW_ALL_STATS).</summary>
public sealed record ServerStats(LibraryTotals Library, ListeningStats Listening, IReadOnlyList<UserListening> Users);

/// <summary>Opaque cursor for lists ordered by time, newest first: the time and id of the last item.</summary>
internal static class TimeCursor
{
    public static string Encode(DateTime at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.Ticks.ToString(CultureInfo.InvariantCulture)}:{id:N}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (DateTime At, Guid Id) Decode(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
            return (new DateTime(long.Parse(parts[0], CultureInfo.InvariantCulture), DateTimeKind.Utc), Guid.ParseExact(parts[1], "N"));
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
        {
            throw ArcanistException.Field("after", ErrorCodes.InvalidFormat);
        }
    }
}
