using Arcanist.Core.Application;
using Arcanist.Core.Application.WishList;

namespace Arcanist.Core.Api.Rest;

public static class WishListEndpoints
{
    public static IEndpointRouteBuilder MapWishListEndpoints(this IEndpointRouteBuilder rest)
    {
        var wishes = rest.MapGroup("/me/wishes").WithTags("Wish list");

        wishes.MapGet("", (string? status, WishListService service, CancellationToken ct) =>
                service.WishListAsync(status is null ? null
                    : Enum.TryParse<WishStatus>(status, ignoreCase: true, out var parsed) ? parsed
                    : throw ArcanistException.Field("status", ErrorCodes.InvalidFormat), ct))
            .WithName("WishList")
            .WithSummary("The user's wish list: songs they want (newest first), then those that arrived (by when). Optionally only the WANTED or the OBTAINED ones.");

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
