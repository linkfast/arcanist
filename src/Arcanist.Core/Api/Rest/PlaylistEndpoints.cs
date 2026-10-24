using Arcanist.Core.Application.Playlists;

namespace Arcanist.Core.Api.Rest;

public sealed record CopyPlaylistInput(string? Name);

public sealed record PlaylistEntryIdsInput(IReadOnlyList<Guid> EntryIds);

public sealed record PlaylistCollaboratorsInput(IReadOnlyList<Guid> UserIds);

public static class PlaylistEndpoints
{
    public static IEndpointRouteBuilder MapPlaylistEndpoints(this IEndpointRouteBuilder rest)
    {
        var playlists = rest.MapGroup("/playlists").WithTags("Playlists");

        playlists.MapGet("", (DateTime? changedSince, PlaylistService service, CancellationToken ct) => service.ListAsync(ct, changedSince))
            .WithName("Playlists")
            .WithSummary("The user's playlists, those they collaborate on and public ones, most recently changed first. With `changedSince`, only those changed after it (deleted ones are not listed: the whole list says which are left).");

        playlists.MapGet("/{id:guid}", (Guid id, PlaylistService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("Playlist")
            .WithSummary("A playlist by id. Playlists the user cannot see are reported as not found.");

        playlists.MapGet("/{playlistId:guid}/entries", (Guid playlistId, string? after, int? first, PlaylistService service, CancellationToken ct) =>
                service.EntriesAsync(playlistId, after, first, ct))
            .WithName("PlaylistEntries")
            .WithSummary("A playlist's tracks in order, page by page (100 by default, at most 1000).");

        playlists.MapPost("", async (CreatePlaylistInput input, PlaylistService service, CancellationToken ct) =>
            {
                var playlist = await service.CreateAsync(input, ct);
                return Results.Created($"/rest/v1/playlists/{playlist.Id}", playlist);
            })
            .WithName("CreatePlaylist")
            .Produces<Playlist>(StatusCodes.Status201Created);

        playlists.MapPut("/{id:guid}", (Guid id, UpdatePlaylistInput input, PlaylistService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdatePlaylist")
            .WithSummary("Renames, describes and shares a playlist. Owner only.");

        playlists.MapDelete("/{id:guid}", async (Guid id, PlaylistService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeletePlaylist")
            .WithSummary("Owner only.");

        playlists.MapPost("/{id:guid}/copy", (Guid id, CopyPlaylistInput? input, PlaylistService service, CancellationToken ct) =>
                service.CopyAsync(id, input?.Name, ct))
            .WithName("CopyPlaylist")
            .WithSummary("Makes a private playlist of the user's own with the tracks of one they can see.");

        playlists.MapPost("/{playlistId:guid}/entries", (Guid playlistId, AddPlaylistTracksInput input, PlaylistService service, CancellationToken ct) =>
                service.AddTracksAsync(playlistId, input, ct))
            .WithName("AddPlaylistTracks")
            .WithSummary("Adds tracks at the end or at a position. Owner and collaborators.");

        playlists.MapPost("/{playlistId:guid}/entries/remove", (Guid playlistId, PlaylistEntryIdsInput input, PlaylistService service, CancellationToken ct) =>
                service.RemoveEntriesAsync(playlistId, input.EntryIds, ct))
            .WithName("RemovePlaylistEntries")
            .WithSummary("Removes entries. Owner and collaborators.");

        playlists.MapPost("/{playlistId:guid}/entries/move", (Guid playlistId, MovePlaylistEntriesInput input, PlaylistService service, CancellationToken ct) =>
                service.MoveEntriesAsync(playlistId, input, ct))
            .WithName("MovePlaylistEntries")
            .WithSummary("Moves entries together to a position, keeping their order. Owner and collaborators.");

        playlists.MapPut("/{playlistId:guid}/collaborators", (Guid playlistId, PlaylistCollaboratorsInput input, PlaylistService service, CancellationToken ct) =>
                service.SetCollaboratorsAsync(playlistId, input.UserIds, ct))
            .WithName("SetPlaylistCollaborators")
            .WithSummary("Replaces the users who can add, remove and reorder the playlist's tracks. Owner only.");

        rest.MapGet("/shareable-users", (PlaylistService service, CancellationToken ct) => service.ShareableUsersAsync(ct))
            .WithTags("Playlists")
            .WithName("ShareableUsers")
            .WithSummary("The server's other active users, to invite as playlist collaborators.");

        return rest;
    }
}
