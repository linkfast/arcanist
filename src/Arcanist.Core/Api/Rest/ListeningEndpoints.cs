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
            .WithSummary("Makes a track one of the user's StarTracks (rates it 5), or not (clears a 5). Works with both rating modes.");

        rest.MapGet("/me/star-tracks", (string? after, int? first, ListeningService service, CancellationToken ct) =>
                service.StarTracksAsync(after, first, ct))
            .WithTags("Listening")
            .WithName("StarTracks")
            .WithSummary("The user's StarTracks (the star in single-star mode, five stars otherwise), most recently starred first, with each song's AcoustID.");

        rest.MapPost("/me/star-tracks", (StarTracksInput input, ListeningService service, CancellationToken ct) => service.AddStarTracksAsync(input, ct))
            .WithTags("Listening")
            .WithName("AddStarTracks")
            .WithSummary("Adds songs to the user's StarTracks, by track or by AcoustID (as Ultimate Cloud names them), at most 1000. By AcoustID, one of the song's tracks, unless one already is.");

        rest.MapPost("/me/star-tracks/remove", (StarTracksInput input, ListeningService service, CancellationToken ct) => service.RemoveStarTracksAsync(input, ct))
            .WithTags("Listening")
            .WithName("RemoveStarTracks")
            .WithSummary("Takes songs from the user's StarTracks, by track or by AcoustID (every track of the song), at most 1000.");

        rest.MapGet("/me/rated-tracks", (int? minRating, string? after, int? first, ListeningService service, CancellationToken ct) =>
                service.RatedTracksAsync(minRating, after, first, ct))
            .WithTags("Listening")
            .WithName("RatedTracks")
            .WithSummary("The user's tracks rated at least minRating (5 for StarTracks), most recently rated first. With the single-star rating mode, only StarTracks.");

        rest.MapPost("/plays", (RecordPlayInput input, ListeningService service, CancellationToken ct) => service.RecordPlayAsync(input, ct))
            .WithTags("Listening")
            .WithName("RecordPlay")
            .WithSummary("Reports that the user listened to a track. The server decides whether it counts as a play (30 seconds, and half the track or four minutes) or a skip.");

        rest.MapPost("/me/listening/import", (ImportListeningInput input, ListeningService service, CancellationToken ct) => service.ImportAsync(input, ct))
            .WithTags("Listening")
            .WithName("ImportListening")
            .WithSummary("Brings ratings and play counts an app kept on its own (at most 1000 of each per call). Ratings only fill gaps; play counts keep the highest.");

        rest.MapGet("/me/plays", (string? after, int? first, ListeningService service, CancellationToken ct) => service.HistoryAsync(after, first, ct))
            .WithTags("Listening")
            .WithName("PlayHistory")
            .WithSummary("The user's counted plays, newest first.");

        rest.MapGet("/stats/listening", (string period, string? timeZone, Guid? userId, Guid? libraryId, StatsService service, CancellationToken ct) =>
                service.ListeningAsync(ParsePeriod(period), timeZone, userId, ct, libraryId))
            .WithTags("Statistics")
            .WithName("ListeningStats")
            .WithSummary("Listening over a `period` (WEEK, MONTH, YEAR, ALL): the user's own, or another user's with VIEW_ALL_STATS. timeZone is an IANA id (UTC by default) for days and months.");

        rest.MapGet("/stats/server", (string period, string? timeZone, Guid? libraryId, StatsService service, CancellationToken ct) =>
                service.ServerAsync(ParsePeriod(period), timeZone, ct, libraryId))
            .WithTags("Statistics")
            .WithName("ServerStats")
            .WithSummary("What the libraries hold and how the server is listened to over a `period` (WEEK, MONTH, YEAR, ALL), per user; with libraryId, of one library. Requires VIEW_ALL_STATS.");

        rest.MapGet("/stats/libraries", (StatsService service, CancellationToken ct) => service.LibrarySummariesAsync(ct))
            .WithTags("Statistics")
            .WithName("LibrarySummaries")
            .WithSummary("Each library the user sees at a glance: songs, albums, files, size and length, and with VIEW_ALL_STATS its plays and listeners of the last 30 days.");

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
