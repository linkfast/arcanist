using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Subsonic;

internal static partial class SubsonicMethods
{
    /// <summary>The user's playlists, those shared with them and public ones. Administrators can pick another user's.</summary>
    private static async Task<IResult> GetPlaylistsAsync(SubsonicCall call)
    {
        var playlists = await call.Service<PlaylistService>().ListAsync(call.Cancellation);
        if (call.Parameters["username"] is { Length: > 0 } userName
            && TextNormalizer.Normalize(userName) != TextNormalizer.Normalize(call.Access.UserName))
        {
            if (!call.Access.IsAdmin)
                throw new SubsonicException(SubsonicErrors.NotAuthorized, "Only administrators can list other users' playlists");
            var normalized = TextNormalizer.Normalize(userName);
            var owner = await call.Service<ArcanistDbContext>().Users.Where(u => u.NormalizedUserName == normalized)
                .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(call.Cancellation);
            playlists = playlists.Where(p => p.Owner.Id == owner).ToList();
        }

        return call.Ok(new SubsonicNode("playlists").List("playlist", await PlaylistNodesAsync(call, playlists)));
    }

    private static async Task<IResult> GetPlaylistAsync(SubsonicCall call) =>
        call.Ok(await PlaylistWithSongsAsync(call, call.Parameters.RequiredId("id")));

    /// <summary>Creates a playlist, or with <c>playlistId</c> replaces the songs of one.</summary>
    private static async Task<IResult> CreatePlaylistAsync(SubsonicCall call)
    {
        var playlists = call.Service<PlaylistService>();
        var songs = call.Parameters.Ids("songId");
        if (call.Parameters["playlistId"] is not { Length: > 0 } existing)
        {
            var created = await playlists.CreateAsync(new CreatePlaylistInput(call.Parameters.Required("name"), null, false, songs), call.Cancellation);
            return call.Ok(await PlaylistWithSongsAsync(call, created.Id));
        }

        var id = SubsonicParameters.Id(existing);
        var entries = await EntriesAsync(call, id);
        if (entries.Count > 0)
            await playlists.RemoveEntriesAsync(id, entries.Select(e => e.Id).ToList(), call.Cancellation);
        if (songs.Count > 0)
            await playlists.AddTracksAsync(id, new AddPlaylistTracksInput(songs, null), call.Cancellation);
        if (call.Parameters["name"] is { Length: > 0 } name)
        {
            var current = await playlists.GetAsync(id, call.Cancellation);
            await playlists.UpdateAsync(id, new UpdatePlaylistInput(name, current.Description, current.IsPublic), call.Cancellation);
        }

        return call.Ok(await PlaylistWithSongsAsync(call, id));
    }

    /// <summary>
    /// Renames, describes or shares a playlist (owner only), and removes songs by their position in it and adds
    /// songs at the end (owner and collaborators).
    /// </summary>
    private static async Task<IResult> UpdatePlaylistAsync(SubsonicCall call)
    {
        var parameters = call.Parameters;
        var playlists = call.Service<PlaylistService>();
        var id = parameters.RequiredId("playlistId");

        var name = parameters["name"];
        var comment = parameters["comment"];
        var isPublic = parameters.Bool("public");
        if (name is not null || comment is not null || isPublic is not null)
        {
            var current = await playlists.GetAsync(id, call.Cancellation);
            await playlists.UpdateAsync(id, new UpdatePlaylistInput(
                string.IsNullOrWhiteSpace(name) ? current.Name : name,
                comment ?? current.Description,
                isPublic ?? current.IsPublic), call.Cancellation);
        }

        var positions = parameters.All("songIndexToRemove")
            .Select(value => int.TryParse(value, out var index) ? index : throw SubsonicException.Invalid("songIndexToRemove"))
            .ToHashSet();
        if (positions.Count > 0)
        {
            var entries = await EntriesAsync(call, id);
            var remove = positions.Where(i => i >= 0 && i < entries.Count).Select(i => entries[i].Id).ToList();
            if (remove.Count > 0)
                await playlists.RemoveEntriesAsync(id, remove, call.Cancellation);
        }

        var add = parameters.Ids("songIdToAdd");
        if (add.Count > 0)
            await playlists.AddTracksAsync(id, new AddPlaylistTracksInput(add, null), call.Cancellation);
        return call.Ok();
    }

    private static async Task<IResult> DeletePlaylistAsync(SubsonicCall call)
    {
        await call.Service<PlaylistService>().DeleteAsync(call.Parameters.RequiredId("id"), call.Cancellation);
        return call.Ok();
    }

    /// <summary>The entries the user can play, in order: what Subsonic positions refer to.</summary>
    private static async Task<List<PlaylistEntry>> EntriesAsync(SubsonicCall call, Guid id)
    {
        var playlists = call.Service<PlaylistService>();
        var entries = new List<PlaylistEntry>();
        string? after = null;
        do
        {
            var page = await playlists.EntriesAsync(id, after, PlaylistService.MaxPageSize, call.Cancellation);
            entries.AddRange(page.Items);
            after = page.NextCursor;
        } while (after is not null);

        return entries;
    }

    private static async Task<SubsonicNode> PlaylistWithSongsAsync(SubsonicCall call, Guid id)
    {
        var playlist = await call.Service<PlaylistService>().GetAsync(id, call.Cancellation);
        var entries = await EntriesAsync(call, id);
        var node = (await PlaylistNodesAsync(call, [playlist]))[0];
        return node.List("entry", await new SubsonicLibrary(call).SongsAsync(entries.Select(e => e.Track).ToList(), "entry"));
    }

    /// <summary>
    /// Playlists as Subsonic sees them. The owner is a user name (apps compare it with theirs to allow editing), and
    /// playlists the user can only listen to are read-only.
    /// </summary>
    private static async Task<List<SubsonicNode>> PlaylistNodesAsync(SubsonicCall call, IReadOnlyList<Playlist> playlists)
    {
        var ownerIds = playlists.Select(p => p.Owner.Id).Distinct().ToList();
        var owners = await call.Service<ArcanistDbContext>().Users.Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName, call.Cancellation);

        return playlists.Select(p => new SubsonicNode("playlist")
            .Attr("id", p.Id)
            .Attr("name", p.Name)
            .Attr("comment", p.Description)
            .Attr("owner", owners.GetValueOrDefault(p.Owner.Id, p.Owner.Name))
            .Attr("public", p.IsPublic)
            .Attr("songCount", p.TrackCount)
            .Attr("duration", SubsonicLibrary.Seconds(p.DurationMs))
            .Attr("created", p.CreatedAt)
            .Attr("changed", p.UpdatedAt)
            .Attr("coverArt", p.ArtworkIds.Count > 0 ? p.ArtworkIds[0] : null)
            .Attr("readonly", p.Role == PlaylistRole.Viewer)).ToList();
    }
}
