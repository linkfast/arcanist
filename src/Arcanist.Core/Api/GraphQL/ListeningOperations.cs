using System.ComponentModel;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Listening;
using GraphQL;
using RatingMode = Arcanist.Core.Data.Entities.RatingMode;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("ratedTracks")]
    [Description("The user's tracks rated at least minRating (5 for starred ones), most recently rated first. With the single-star rating mode, only starred tracks.")]
    public static Task<RatedTrackPage> RatedTracks(int? minRating, string? after, int? first,
        [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RatedTracksAsync(minRating, after, first, cancellationToken);

    [Name("playHistory")]
    [Description("The user's counted plays, newest first.")]
    public static Task<PlayHistoryPage> PlayHistory(string? after, int? first, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.HistoryAsync(after, first, cancellationToken);

    [Name("listeningStats")]
    [Description("Listening over a period: the user's own, or another user's with VIEW_ALL_STATS. timeZone is an IANA id (UTC by default) for days and months.")]
    public static Task<ListeningStats> ListeningStats(StatsPeriod period, string? timeZone, [Id] Guid? userId,
        [FromServices] StatsService stats, CancellationToken cancellationToken) =>
        stats.ListeningAsync(period, timeZone, userId, cancellationToken);

    [Name("serverStats")]
    [Description("What the libraries hold and how the server is listened to, per user. Requires VIEW_ALL_STATS.")]
    public static Task<ServerStats> ServerStats(StatsPeriod period, string? timeZone, [FromServices] StatsService stats, CancellationToken cancellationToken) =>
        stats.ServerAsync(period, timeZone, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("rateTrack")]
    [Description("Rates a track 1–5 for the user, or clears the rating with null. With the single-star rating mode only 5 is accepted.")]
    public static Task<Track> RateTrack([Id] Guid trackId, int? rating, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RateTrackAsync(trackId, rating, cancellationToken);

    [Name("starTrack")]
    [Description("Stars a track for the user (rates it 5) or unstars it (clears a 5). Works with both rating modes.")]
    public static Task<Track> StarTrack([Id] Guid trackId, bool starred, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.StarTrackAsync(trackId, starred, cancellationToken);

    [Name("recordPlay")]
    [Description("Reports that the user listened to a track. The server decides whether it counts as a play (30 seconds, and half the track or four minutes) or a skip.")]
    public static Task<RecordedPlay> RecordPlay(RecordPlayInput input, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.RecordPlayAsync(input, cancellationToken);

    [Name("setRatingMode")]
    [Description("Chooses how users rate tracks: STARS (one to five) or STAR (a single star, kept as five). Nothing is lost when switching. Requires MANAGE_SETTINGS.")]
    public static Task<RatingMode> SetRatingMode(RatingMode mode, [FromServices] ListeningService listening, CancellationToken cancellationToken) =>
        listening.SetRatingModeAsync(mode, cancellationToken);
}
