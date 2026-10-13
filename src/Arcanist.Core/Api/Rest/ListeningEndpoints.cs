using Arcanist.Core.Application;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Api.Rest;

/// <param name="Rating">1–5, or null to clear the rating.</param>
public sealed record RateTrackInput(int? Rating);

public sealed record StarTrackInput(bool Starred);

public sealed record SetRatingModeInput(RatingMode Mode);

public static class ListeningEndpoints
{
    public static IEndpointRouteBuilder MapListeningEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapPut("/tracks/{trackId:guid}/rating", (Guid trackId, RateTrackInput input, ListeningService service, CancellationToken ct) =>
                service.RateTrackAsync(trackId, input.Rating, ct))
            .WithTags("Listening")
            .WithName("RateTrack")
            .WithSummary("Rates a track 1–5 for the user, or clears the rating with null. With the single-star rating mode only 5 is accepted.");

        rest.MapPut("/tracks/{trackId:guid}/star", (Guid trackId, StarTrackInput input, ListeningService service, CancellationToken ct) =>
                service.StarTrackAsync(trackId, input.Starred, ct))
            .WithTags("Listening")
            .WithName("StarTrack")
            .WithSummary("Stars a track for the user (rates it 5) or unstars it (clears a 5). Works with both rating modes.");

        rest.MapGet("/me/rated-tracks", (int? minRating, string? after, int? first, ListeningService service, CancellationToken ct) =>
                service.RatedTracksAsync(minRating, after, first, ct))
            .WithTags("Listening")
            .WithName("RatedTracks")
            .WithSummary("The user's tracks rated at least minRating (5 for starred ones), most recently rated first. With the single-star rating mode, only starred tracks.");

        rest.MapPost("/plays", (RecordPlayInput input, ListeningService service, CancellationToken ct) => service.RecordPlayAsync(input, ct))
            .WithTags("Listening")
            .WithName("RecordPlay")
            .WithSummary("Reports that the user listened to a track. The server decides whether it counts as a play (30 seconds, and half the track or four minutes) or a skip.");

        rest.MapGet("/me/plays", (string? after, int? first, ListeningService service, CancellationToken ct) => service.HistoryAsync(after, first, ct))
            .WithTags("Listening")
            .WithName("PlayHistory")
            .WithSummary("The user's counted plays, newest first.");

        rest.MapGet("/stats/listening", (string period, string? timeZone, Guid? userId, StatsService service, CancellationToken ct) =>
                service.ListeningAsync(ParsePeriod(period), timeZone, userId, ct))
            .WithTags("Statistics")
            .WithName("ListeningStats")
            .WithSummary("Listening over a `period` (WEEK, MONTH, YEAR, ALL): the user's own, or another user's with VIEW_ALL_STATS. timeZone is an IANA id (UTC by default) for days and months.");

        rest.MapGet("/stats/server", (string period, string? timeZone, StatsService service, CancellationToken ct) =>
                service.ServerAsync(ParsePeriod(period), timeZone, ct))
            .WithTags("Statistics")
            .WithName("ServerStats")
            .WithSummary("What the libraries hold and how the server is listened to over a `period` (WEEK, MONTH, YEAR, ALL), per user. Requires VIEW_ALL_STATS.");

        rest.MapPut("/settings/rating-mode", (SetRatingModeInput input, ListeningService service, CancellationToken ct) =>
                service.SetRatingModeAsync(input.Mode, ct))
            .WithTags("Settings")
            .WithName("SetRatingMode")
            .WithSummary("Chooses how users rate tracks: STARS (one to five) or STAR (a single star, kept as five). Nothing is lost when switching. Requires MANAGE_SETTINGS.");

        return rest;
    }

    // Query binding of enums is case-sensitive; the API spells them in upper case.
    private static StatsPeriod ParsePeriod(string period) =>
        Enum.TryParse<StatsPeriod>(period, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw ArcanistException.Field("period", ErrorCodes.InvalidFormat);
}
