using Arcanist.Core.Application.Jams;
using Arcanist.Core.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Arcanist.Core.Api.Rest;

public sealed record JoinJukeboxJamInput(string? Name);

public sealed record JukeboxJamTracksInput(IReadOnlyList<Guid> TrackIds, JukeboxJamAddSource? Source);

public sealed record JukeboxJamVoteInput(Guid EntryId);

public static class JukeboxJamEndpoints
{
    /// <summary>A guest's session token, in REST (GraphQL takes it as <c>guestToken</c>).</summary>
    public const string GuestHeader = "X-Arcanist-Guest";

    public static IEndpointRouteBuilder MapJukeboxJamEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/jams", (JukeboxJamService service, CancellationToken ct) => service.MineAsync(ct))
            .WithTags("Jukebox Jam")
            .WithName("MyJukeboxJams")
            .WithSummary("The Jukebox Jams the user hosts or takes part in that have not ended.");

        rest.MapPost("/jams", (StartJukeboxJamInput input, JukeboxJamService service, CancellationToken ct) => service.StartAsync(input, ct))
            .WithTags("Jukebox Jam")
            .WithName("StartJukeboxJam")
            .WithSummary("Starts a Jukebox Jam the user hosts: a live queue (a playlist of its own) that plays on a client of theirs (any that implements the host API), the server's audio or a DLNA renderer. Opening it to guests without an account needs SHARE_LINKS and the server's setting.");

        // Guests without an account reach these: limited per address.
        var jams = rest.MapGroup("/public/jams/{code}").WithTags("Jukebox Jam").RequireRateLimiting(AuthServiceCollectionExtensions.PublicRateLimit);

        jams.MapGet("", (string code, [FromHeader(Name = GuestHeader)] string? guest, JukeboxJamService service, CancellationToken ct) =>
                service.GetAsync(code, guest, ct))
            .WithName("JukeboxJam")
            .WithSummary("A Jukebox Jam by its code, as the one asking sees it: what plays, the queue in the order it will play, the DJ if there is one. Users of the server with its code; without an account, only Jams open to guests (with the X-Arcanist-Guest header once joined). An ended one says so (`endedAt`).");

        jams.MapGet("/search", (string code, string query, int? first, [FromHeader(Name = GuestHeader)] string? guest, JukeboxJamService service,
                CancellationToken ct) => service.SearchAsync(code, query, first, guest, ct))
            .WithName("JukeboxJamSearch")
            .WithSummary("Songs that can be added to a Jam (from its libraries, among those the host can listen to). For those taking part.");

        jams.MapPost("/join", (string code, JoinJukeboxJamInput? input, JukeboxJamService service, CancellationToken ct) =>
                service.JoinAsync(code, input?.Name, ct))
            .WithName("JoinJukeboxJam")
            .WithSummary("Takes part in a Jam. A signed-in user becomes a member (null is returned); someone without an account gets a guest session by the name they give (send its token in X-Arcanist-Guest).");

        jams.MapPost("/tracks", (string code, JukeboxJamTracksInput input, [FromHeader(Name = GuestHeader)] string? guest, JukeboxJamService service,
                CancellationToken ct) => service.AddAsync(code, input.TrackIds, input.Source, guest, ct))
            .WithName("AddToJukeboxJam")
            .WithSummary("Adds songs to a Jam's queue (at most 50 at once). Guests within their limit (jam_guest_limit, with retryAfterSeconds); in voting Jams each comes with a vote of whoever adds it. `source: DJ` is the host's DJ: no limit and no vote.");

        jams.MapPost("/votes", (string code, JukeboxJamVoteInput input, [FromHeader(Name = GuestHeader)] string? guest, JukeboxJamService service,
                CancellationToken ct) => service.VoteAsync(code, input.EntryId, guest, ct))
            .WithName("VoteJukeboxJam")
            .WithSummary("A vote for a waiting song (voting Jams). As often as one likes, but once every 30 seconds in the whole Jam: sooner is jam_vote_too_soon with retryAfterSeconds.");

        jams.MapPost("/control", (string code, ControlJukeboxJamInput input, JukeboxJamService service, CancellationToken ct) =>
                service.ControlAsync(code, input, ct))
            .WithName("ControlJukeboxJam")
            .WithSummary("Skips, pauses, resumes, removes, pins or moves (Jams without voting) songs. The host and those who manage its playlist.");

        jams.MapPut("", (string code, UpdateJukeboxJamInput input, JukeboxJamService service, CancellationToken ct) => service.UpdateAsync(code, input, ct))
            .WithName("UpdateJukeboxJam")
            .WithSummary("Changes a Jam's name, voting, guests and libraries. Host only.");

        jams.MapDelete("/guests/{guestId:guid}", (string code, Guid guestId, JukeboxJamService service, CancellationToken ct) =>
                service.RemoveGuestAsync(code, guestId, ct))
            .WithName("RemoveJukeboxJamGuest")
            .WithSummary("Takes a guest out of a Jam: their session no longer works. The host and those who manage it.");

        jams.MapPost("/end", (string code, JukeboxJamService service, CancellationToken ct) => service.EndAsync(code, ct))
            .WithName("EndJukeboxJam")
            .WithSummary("Ends a Jam: its code no longer works and what played stays as a playlist of the host's. Host only.");

        jams.MapPost("/host", (string code, ClaimJukeboxJamHostInput input, JukeboxJamService service, CancellationToken ct) =>
                service.ClaimHostAsync(code, input, ct))
            .WithName("ClaimJukeboxJamHost")
            .WithSummary("Host API: this client (a web player tab, Ultimate Desktop) plays the Jam; another one claiming it takes over (the one before gets a HOST change with the new device). Host only, for Jams that play on a client.");

        jams.MapGet("/next", (string code, JukeboxJamService service, CancellationToken ct) => service.NextAsync(code, ct))
            .WithName("JukeboxJamNext")
            .WithSummary("Host API: the song the host's client should play next (pinned, then most voted, then by arrival), with its stream; null when nothing waits. Host only.");

        jams.MapPost("/playback", (string code, ReportJukeboxJamPlaybackInput input, JukeboxJamService service, CancellationToken ct) =>
                service.ReportAsync(code, input, ct))
            .WithName("ReportJukeboxJamPlayback")
            .WithSummary("Host API: what the host's client plays (an entry of the Jam, which leaves the queue; a song not on the server; or nothing), paused or not and where. Every 5 seconds and on every change, from the device that claimed it (jam_not_host otherwise).");

        jams.MapPut("/dj", (string code, SetJukeboxJamDjInput input, JukeboxJamService service, CancellationToken ct) => service.SetDjAsync(code, input, ct))
            .WithName("SetJukeboxJamDj")
            .WithSummary("Host API: a DJ chooses what plays (ULTIMATE_AI_DJ: Ultimate's AI DJ), or no longer (NONE); everyone in the Jam sees it. Taken off by itself when the host's client is gone for 5 minutes. Host only.");

        return rest;
    }
}
