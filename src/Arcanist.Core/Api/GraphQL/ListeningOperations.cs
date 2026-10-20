using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Listening;
using GraphQL;
using RatingMode = Arcanist.Core.Data.Entities.RatingMode;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("starTracks")]
    [Description("The user's StarTracks (the star in single-star mode, five stars otherwise), most recently starred first, with each song's AcoustID.")]
    public static Task<StarTrackPage> StarTracks(string? after, int? first, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.StarTracksAsync(after, first, cancellationToken);

    [Name("ratedTracks")]
    [Description("The user's tracks rated at least minRating (5 for StarTracks), most recently rated first. With the single-star rating mode, only StarTracks.")]
    public static Task<RatedTrackPage> RatedTracks(int? minRating, string? after, int? first,
        [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RatedTracksAsync(minRating, after, first, cancellationToken);

    [Name("playHistory")]
    [Description("The user's counted plays, newest first.")]
    public static Task<PlayHistoryPage> PlayHistory(string? after, int? first, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.HistoryAsync(after, first, cancellationToken);

    [Name("listeningStats")]
    [Description("Listening over a period: the user's own, or another user's with VIEW_ALL_STATS. timeZone is an IANA id (UTC by default) for days and months.")]
    public static Task<ListeningStats> ListeningStats(StatsPeriod period, string? timeZone, [Id] Guid? userId, [Id] Guid? libraryId,
        [FromServices] StatsService stats, CancellationToken cancellationToken) =>
        stats.ListeningAsync(period, timeZone, userId, cancellationToken, libraryId);

    [Name("serverStats")]
    [Description("What the libraries hold and how the server is listened to, per user; with libraryId, of one library. Requires VIEW_ALL_STATS.")]
    public static Task<ServerStats> ServerStats(StatsPeriod period, string? timeZone, [Id] Guid? libraryId, [FromServices] StatsService stats, CancellationToken cancellationToken) =>
        stats.ServerAsync(period, timeZone, cancellationToken, libraryId);

    [Name("librarySummaries")]
    [Description("Each library the user sees at a glance: songs, albums, files, size and length, and with VIEW_ALL_STATS its plays and listeners of the last 30 days.")]
    public static Task<IReadOnlyList<LibrarySummary>> LibrarySummaries([FromServices] StatsService stats, CancellationToken cancellationToken) =>
        stats.LibrarySummariesAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("importListening")]
    [Description("Brings ratings and play counts an app kept on its own (at most 1000 of each per call). Ratings only fill gaps; play counts keep the highest.")]
    public static Task<ListeningImportResult> ImportListening(ImportListeningInput input, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.ImportAsync(input, cancellationToken);

    [Name("rateTrack")]
    [Description("Rates a track 1–5 for the user, or clears the rating with null. With the single-star rating mode only 5 is accepted.")]
    public static Task<Track> RateTrack([Id] Guid trackId, int? rating, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RateTrackAsync(trackId, rating, cancellationToken);

    [Name("starTrack")]
    [Description("Makes a track one of the user's StarTracks (rates it 5), or not (clears a 5). Works with both rating modes.")]
    public static Task<Track> StarTrack([Id] Guid trackId, bool starred, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.StarTrackAsync(trackId, starred, cancellationToken);

    [Name("addStarTracks")]
    [Description("Adds songs to the user's StarTracks, by track or by AcoustID (as Ultimate Cloud names them), at most 1000. By AcoustID, one of the song's tracks, unless one already is.")]
    public static Task<StarTracksResult> AddStarTracks(StarTracksInput input, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.AddStarTracksAsync(input, cancellationToken);

    [Name("removeStarTracks")]
    [Description("Takes songs from the user's StarTracks, by track or by AcoustID (every track of the song), at most 1000.")]
    public static Task<StarTracksResult> RemoveStarTracks(StarTracksInput input, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RemoveStarTracksAsync(input, cancellationToken);

    [Name("recordPlay")]
    [Description("Reports that the user listened to a track. The server decides whether it counts as a play (30 seconds, and half the track or four minutes) or a skip.")]
    public static Task<RecordedPlay> RecordPlay(RecordPlayInput input, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RecordPlayAsync(input, cancellationToken);

    [Name("setRatingMode")]
    [Description("Chooses how users rate tracks: STARS (one to five, five being a StarTrack) or STAR (the StarTrack alone, kept as five). Nothing is lost when switching. Requires MANAGE_SETTINGS.")]
    public static Task<RatingMode> SetRatingMode(RatingMode mode, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.SetRatingModeAsync(mode, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("starTracksChanged")]
    [Description("The user's StarTracks (or other ratings) changed, here or through their Ultimate Cloud account.")]
    public static IObservable<StarTracksChange> StarTracksChanged([FromServices] ListeningService listening) => listening.WatchStarTracks();
}
