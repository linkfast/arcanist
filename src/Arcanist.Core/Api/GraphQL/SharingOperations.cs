using System.ComponentModel;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.Sharing;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("sharing")]
    [Description("Whether the server lets its users share outside it (public playlist links, guests without an account in Jukebox Jams) and whether the user may (SHARE_LINKS).")]
    public static Task<SharingStatus> Sharing([FromServices] SharingService sharing, CancellationToken cancellationToken) =>
        sharing.GetAsync(cancellationToken);

    [Name("sharedPlaylist")]
    [Description("A playlist by its public link, page by page; anyone can ask, without an account. Songs come with a 30-second preview; to a signed-in user, also as tracks when they can play them in full. Not found when the link does not work (revoked, or public links turned off).")]
    public static Task<SharedPlaylist> SharedPlaylist(string token, string? after, int? first, [FromServices] PublicPlaylistService shared,
        CancellationToken cancellationToken) =>
        shared.GetAsync(token, after, first, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setSharingSettings")]
    [Description("Turns public playlist links and guests in Jukebox Jams on or off (both off at first): turned off, those there were stop working at once. Administrators, with a recent sign-in.")]
    public static Task<SharingStatus> SetSharingSettings(SharingSettingsInput input, [FromServices] SharingService sharing, CancellationToken cancellationToken) =>
        sharing.SetAsync(input, cancellationToken);

    [Name("copySharedPlaylist")]
    [Description("Makes a private playlist of the user's own with the songs of a shared one (by its public link) they can play.")]
    public static Task<Playlist> CopySharedPlaylist(string token, string? name, [FromServices] PublicPlaylistService shared, CancellationToken cancellationToken) =>
        shared.CopyAsync(token, name, cancellationToken);
}
