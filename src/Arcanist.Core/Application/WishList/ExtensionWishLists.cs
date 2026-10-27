using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Extensibility;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.WishList;

/// <summary>The wish lists for extensions (<see cref="IWishLists"/>), over <see cref="WishListService"/>.</summary>
public sealed class ExtensionWishLists(
    WishListService wishes,
    WishLinkResolver links,
    UserAccessCache users,
    ArcanistDbContext db) : IWishLists
{
    public async Task<IReadOnlyList<WishListEntry>> ListAsync(Guid userId, bool wantedOnly, CancellationToken cancellationToken)
    {
        var access = await UserAsync(userId, cancellationToken);
        var list = await wishes.ListAsync(access, userId, wantedOnly ? WishStatus.Wanted : null, cancellationToken);
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.UserName).SingleAsync(cancellationToken);
        return list.Items.Select(w => ToEntry(w, userId, name)).ToList();
    }

    public async Task<IReadOnlyList<WishListEntry>> ListAllAsync(bool wantedOnly, CancellationToken cancellationToken)
    {
        // Tracks are shown as an administrator sees them: everyone's wishes, whatever library fulfilled them.
        var admin = await db.Users.Where(u => u.Role == Data.Entities.UserRole.Admin).OrderBy(u => u.CreatedAt).Select(u => u.Id).FirstAsync(cancellationToken);
        var list = await wishes.ListAsync(await UserAsync(admin, cancellationToken), null, wantedOnly ? WishStatus.Wanted : null, cancellationToken);
        return list.Items.Select(w => ToEntry(w, w.Owner!.Id, w.Owner.UserName)).ToList();
    }

    public async Task<WishListEntry> AddAsync(Guid userId, WishListEntryInput input, CancellationToken cancellationToken)
    {
        var access = await UserAsync(userId, cancellationToken);
        var wish = await wishes.AddAsync(access,
            new WishInput(input.Title, input.Artist, input.Album, input.MusicBrainzRecordingId, input.AcoustId, input.DurationMs, input.Note,
                input.SourceUrl, input.ArtworkId, WantsVideo: input.WantsVideo),
            cancellationToken);
        return ToEntry(wish, userId, access.UserName);
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid wishId, CancellationToken cancellationToken)
    {
        try
        {
            return await wishes.RemoveAsync(userId, wishId, cancellationToken);
        }
        catch (ArcanistException.NotFound)
        {
            return false;
        }
    }

    public async Task<WishListEntryInput?> ResolveLinkAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var found = await links.ResolveAsync(url, cancellationToken);
            return new WishListEntryInput(found.Title, found.Artist, found.Album, found.DurationMs, found.MusicBrainzRecordingId,
                SourceUrl: found.Url, ArtworkId: found.ArtworkId);
        }
        catch (ArcanistException)
        {
            return null;
        }
    }

    private async Task<UserAccess> UserAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.GetUserAsync(userId, cancellationToken) ?? throw new ArcanistException.NotFound();

    private static WishListEntry ToEntry(Wish wish, Guid userId, string userName) =>
        new(wish.Id, userId, userName, wish.Title, wish.Artist, wish.Album, wish.DurationMs, wish.MusicBrainzRecordingId, wish.AcoustId, wish.Note,
            wish.CreatedAt, wish.ObtainedAt, wish.Track?.Id, wish.SourceUrl, wish.ArtworkId)
        {
            WantsVideo = wish.WantsVideo,
            VideoObtainedAt = wish.VideoObtainedAt,
        };
}
