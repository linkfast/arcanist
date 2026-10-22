using Arcanist.Core.Application;
using Arcanist.Core.Application.WishList;

namespace Arcanist.Core.Api.Rest;

public static class WishListEndpoints
{
    public static IEndpointRouteBuilder MapWishListEndpoints(this IEndpointRouteBuilder rest)
    {
        var wishes = rest.MapGroup("/me/wishes").WithTags("Wish list");

        wishes.MapGet("", (string? status, string? scope, WishListService service, CancellationToken ct) =>
                service.WishListAsync(status is null ? null
                    : Enum.TryParse<WishStatus>(status, ignoreCase: true, out var parsed) ? parsed
                    : throw ArcanistException.Field("status", ErrorCodes.InvalidFormat),
                    scope is null ? WishListScope.Mine
                    : Enum.TryParse<WishListScope>(scope, ignoreCase: true, out var parsedScope) ? parsedScope
                    : throw ArcanistException.Field("scope", ErrorCodes.InvalidFormat), ct))
            .WithName("WishList")
            .WithSummary("The user's wish list: songs they want (newest first), then those that arrived (by when). Optionally only the WANTED or the OBTAINED ones. With scope=ALL, everyone's wishes, each with its owner (requires VIEW_ALL_WISHES).");

        wishes.MapPost("/resolve-link", (ResolveWishLinkInput input, WishListService service, CancellationToken ct) => service.ResolveLinkAsync(input, ct))
            .WithName("ResolveWishLink")
            .WithSummary("Reads a song's link from a streaming service (Spotify, YouTube Music, Deezer, Apple Music, MusicBrainz, Tidal, SoundCloud, Bandcamp…) to fill a wish: title, artist, album, duration, the MusicBrainz recording and a cover (kept in the artwork cache). Adds nothing: pass what it returns to AddWish with sourceUrl and artworkId. Needs external services allowed (external_services_disabled); wish_link_not_a_song for albums or playlists, wish_link_unreadable when the page does not say the song.");

        wishes.MapPost("", (WishInput input, WishListService service, CancellationToken ct) => service.AddAsync(input, ct))
            .WithName("AddWish")
            .WithSummary("Adds a song to the user's wish list (title and artist; album, duration, MusicBrainz recording or AcoustID narrow it). When a library the user can play has it already, it comes obtained (foundWhenAdded).");

        wishes.MapPut("/{id:guid}", (Guid id, WishInput input, WishListService service, CancellationToken ct) => service.UpdateAsync(id, input, ct))
            .WithName("UpdateWish")
            .WithSummary("Changes a wish of the user. Another song makes it wanted again, and it is looked for at once.");

        wishes.MapDelete("/{id:guid}", (Guid id, WishListService service, CancellationToken ct) => service.RemoveAsync(id, ct))
            .WithName("RemoveWish")
            .WithSummary("Removes a wish (wanted or obtained) from the user's list.");

        wishes.MapPost("/seen", (WishListService service, CancellationToken ct) => service.MarkSeenAsync(ct))
            .WithName("MarkWishesSeen")
            .WithSummary("The user saw the wishes that came true (WishListResult.unseen goes to 0). Returns how many were new.");

        return rest;
    }
}
