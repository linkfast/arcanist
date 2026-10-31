using System.ComponentModel;
using Arcanist.Core.Application.Playlists;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("playlists")]
    [Description("The user's playlists, those they collaborate on and public ones, most recently changed first. With `changedSince`, only those changed after it (deleted ones are not listed: the whole list says which are left).")]
    public static Task<IReadOnlyList<Playlist>> Playlists([FromServices] PlaylistService playlists, CancellationToken cancellationToken, DateTime? changedSince = null) =>
        playlists.ListAsync(cancellationToken, changedSince);

    [Name("playlist")]
    [Description("A playlist by id. Playlists the user cannot see are reported as not found.")]
    public static Task<Playlist> Playlist([Id] Guid id, [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.GetAsync(id, cancellationToken);

    [Name("playlistEntries")]
    [Description("A playlist's tracks in order, page by page (100 by default, at most 1000).")]
    public static Task<PlaylistEntryPage> PlaylistEntries([Id] Guid playlistId, string? after, int? first,
        [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.EntriesAsync(playlistId, after, first, cancellationToken);

    [Name("shareableUsers")]
    [Description("The server's other active users, to invite as playlist collaborators.")]
    public static Task<IReadOnlyList<UserReference>> ShareableUsers([FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.ShareableUsersAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("createPlaylist")]
    public static Task<Playlist> CreatePlaylist(CreatePlaylistInput input, [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.CreateAsync(input, cancellationToken);

    [Name("updatePlaylist")]
    [Description("Renames, describes and makes a playlist public or not. Owner and managers.")]
    public static Task<Playlist> UpdatePlaylist([Id] Guid id, UpdatePlaylistInput input, [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.UpdateAsync(id, input, cancellationToken);

    [Name("deletePlaylist")]
    [Description("Owner only.")]
    public static async Task<bool> DeletePlaylist([Id] Guid id, [FromServices] PlaylistService playlists, CancellationToken cancellationToken)
    {
        await playlists.DeleteAsync(id, cancellationToken);
        return true;
    }

    [Name("copyPlaylist")]
    [Description("Makes a private playlist of the user's own with the tracks of one they can see.")]
    public static Task<Playlist> CopyPlaylist([Id] Guid id, string? name, [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.CopyAsync(id, name, cancellationToken);

    [Name("addPlaylistTracks")]
    [Description("Adds tracks at the end or at a position. Owner and collaborators.")]
    public static Task<Playlist> AddPlaylistTracks([Id] Guid playlistId, AddPlaylistTracksInput input,
        [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.AddTracksAsync(playlistId, input, cancellationToken);

    [Name("removePlaylistEntries")]
    [Description("Removes entries. Owner and collaborators.")]
    public static Task<Playlist> RemovePlaylistEntries([Id] Guid playlistId, [Id] IReadOnlyList<Guid> entryIds,
        [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.RemoveEntriesAsync(playlistId, entryIds, cancellationToken);

    [Name("movePlaylistEntries")]
    [Description("Moves entries together to a position, keeping their order. Owner and collaborators.")]
    public static Task<Playlist> MovePlaylistEntries([Id] Guid playlistId, MovePlaylistEntriesInput input,
        [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.MoveEntriesAsync(playlistId, input, cancellationToken);

    [Name("setPlaylistCollaborators")]
    [Description("Replaces the users who can add, remove and reorder the playlist's tracks (those listed edit it, or still manage it; those who only listen stay). Owner and managers. See setPlaylistMembers.")]
    public static Task<Playlist> SetPlaylistCollaborators([Id] Guid playlistId, [Id] IReadOnlyList<Guid> userIds,
        [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.SetCollaboratorsAsync(playlistId, userIds, cancellationToken);

    [Name("setPlaylistMembers")]
    [Description("Replaces the users the playlist is shared with and what each can do: LISTEN, EDIT (add, remove and reorder tracks) or MANAGE (also rename it, share it and manage its public link). Owner and managers.")]
    public static Task<Playlist> SetPlaylistMembers([Id] Guid playlistId, IReadOnlyList<PlaylistMemberInput> members,
        [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.SetMembersAsync(playlistId, members, cancellationToken);

    [Name("createPlaylistLink")]
    [Description("Gives the playlist a public link (or returns the one it has): anyone with it sees the playlist and hears 30-second previews. Owner and managers with SHARE_LINKS, when the server allows public links (public_links_disabled otherwise).")]
    public static Task<Playlist> CreatePlaylistLink([Id] Guid playlistId, [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.CreateLinkAsync(playlistId, cancellationToken);

    [Name("revokePlaylistLink")]
    [Description("Removes the playlist's public link: it no longer opens. Owner and managers.")]
    public static Task<Playlist> RevokePlaylistLink([Id] Guid playlistId, [FromServices] PlaylistService playlists, CancellationToken cancellationToken) =>
        playlists.RevokeLinkAsync(playlistId, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("playlistChanged")]
    [Description("A playlist the user sees changed (its tracks, name or sharing) or was deleted; with `playlistId`, only that one. Read it again with playlist and playlistEntries.")]
    public static IObservable<PlaylistChange> PlaylistChanged([Id] Guid? playlistId, [FromServices] PlaylistService playlists) =>
        playlists.Watch(playlistId);
}
