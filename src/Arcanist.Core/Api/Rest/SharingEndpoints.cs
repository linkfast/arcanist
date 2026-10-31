using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.Sharing;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Api.Rest;

public static class SharingEndpoints
{
    public static IEndpointRouteBuilder MapSharingEndpoints(this IEndpointRouteBuilder rest)
    {
        var sharing = rest.MapGroup("/sharing").WithTags("Sharing");

        sharing.MapGet("", (SharingService service, CancellationToken ct) => service.GetAsync(ct))
            .WithName("Sharing")
            .WithSummary("Whether the server lets its users share outside it (public playlist links, guests without an account in Jukebox Jams) and whether the user may (SHARE_LINKS).");

        sharing.MapPut("", (SharingSettingsInput input, SharingService service, CancellationToken ct) => service.SetAsync(input, ct))
            .WithName("SetSharingSettings")
            .WithSummary("Turns public playlist links and guests in Jukebox Jams on or off (both off at first): turned off, those there were stop working at once. Administrators, with a recent sign-in.");

        // Reachable without an account: limited per address.
        var shared = rest.MapGroup("/public/playlists").WithTags("Sharing").RequireRateLimiting(AuthServiceCollectionExtensions.PublicRateLimit);

        shared.MapGet("/{token}", (string token, string? after, int? first, PublicPlaylistService service, CancellationToken ct) =>
                service.GetAsync(token, after, first, ct))
            .WithName("SharedPlaylist")
            .WithSummary("A playlist by its public link, page by page; anyone can ask, without an account. Songs come with a 30-second preview; to a signed-in user, also as tracks when they can play them in full. Not found when the link does not work (revoked, or public links turned off).");

        shared.MapPost("/{token}/copy", (string token, CopyPlaylistInput? input, PublicPlaylistService service, CancellationToken ct) =>
                service.CopyAsync(token, input?.Name, ct))
            .WithName("CopySharedPlaylist")
            .WithSummary("Makes a private playlist of the user's own with the songs of a shared one (by its public link) they can play.")
            .Produces<Playlist>();

        rest.MapMethods("/public/playlists/{token}/preview/{entryId:guid}", [HttpMethods.Get, HttpMethods.Head],
                async (HttpContext context, string token, Guid entryId, PublicPlaylistService service, CancellationToken ct) =>
                {
                    var (owner, trackId) = await service.PreviewAsync(token, entryId, ct);
                    return await MediaResponses.PreviewAsync(context, owner, trackId, PublicPlaylistService.PreviewSeconds, ct);
                })
            .WithTags("Sharing")
            .WithName("SharedPlaylistPreview")
            .WithSummary("30 seconds of a song of a shared playlist, as MP3, for anyone (the entry's `previewUrl`). Never counted as a play.")
            .RequireRateLimiting(AuthServiceCollectionExtensions.PreviewRateLimit)
            .Produces(StatusCodes.Status200OK, contentType: "audio/mpeg");

        return rest;
    }
}
