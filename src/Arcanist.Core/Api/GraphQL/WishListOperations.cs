using System.ComponentModel;
using Arcanist.Core.Application.WishList;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("wishList")]
    [Description("The user's wish list: songs they want (newest first), then those that arrived (by when). Optionally only the wanted or the obtained ones.")]
    public static Task<WishListResult> WishList(WishStatus? status, [FromServices] WishListService wishes, CancellationToken cancellationToken) =>
        wishes.WishListAsync(status, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("addWish")]
    [Description("Adds a song to the user's wish list (title and artist; album, duration, MusicBrainz recording or AcoustID narrow it). When a library the user can play has it already, it comes obtained (foundWhenAdded).")]
    public static Task<Wish> AddWish(WishInput input, [FromServices] WishListService wishes, CancellationToken cancellationToken) =>
        wishes.AddAsync(input, cancellationToken);

    [Name("updateWish")]
    [Description("Changes a wish of the user. Another song makes it wanted again, and it is looked for at once.")]
    public static Task<Wish> UpdateWish([Id] Guid id, WishInput input, [FromServices] WishListService wishes, CancellationToken cancellationToken) =>
        wishes.UpdateAsync(id, input, cancellationToken);

    [Name("removeWish")]
    [Description("Removes a wish (wanted or obtained) from the user's list.")]
    public static Task<bool> RemoveWish([Id] Guid id, [FromServices] WishListService wishes, CancellationToken cancellationToken) =>
        wishes.RemoveAsync(id, cancellationToken);

    [Name("markWishesSeen")]
    [Description("The user saw the wishes that came true (WishListResult.unseen goes to 0). Returns how many were new.")]
    public static Task<int> MarkWishesSeen([FromServices] WishListService wishes, CancellationToken cancellationToken) =>
        wishes.MarkSeenAsync(cancellationToken);
}

public sealed partial class Subscription
{
    [Name("wishObtained")]
    [Description("A song of the user's wish list arrived in a library they can play (after a scan, an upload, an import or a sync).")]
    public static IObservable<WishObtained> WishObtained([FromServices] WishListService wishes) => wishes.Watch();
}
