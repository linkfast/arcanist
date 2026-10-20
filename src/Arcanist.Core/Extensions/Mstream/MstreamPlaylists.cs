using System.Text.Json.Nodes;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Application.Playlists;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>
/// Playlists, which mStream identifies by name. They are the user's own playlists and those they collaborate on.
/// mStream entries have integer ids: here a number derived from the entry's id, stable while the entry exists.
/// </summary>
internal sealed partial class MstreamExtension
{
    private static async Task<IResult> PlaylistsAsync(MstreamCall call)
    {
        var names = (await EditablePlaylistsAsync(call)).Select(p => p.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase);
        return MstreamCall.Json(new JsonArray(names.Select(n => (JsonNode?)new JsonObject { ["name"] = n }).ToArray()));
    }

    private static async Task<IResult> LoadPlaylistAsync(MstreamCall call)
    {
        var playlist = await FindPlaylistAsync(call, call.Body.Required("playlistname"));
        if (playlist is null)
            return MstreamCall.Json(new JsonArray());

        var library = new MstreamLibrary(call);
        var entries = (await EntriesAsync(call, playlist.Id)).Where(e => e.Track.Files.Count > 0).ToList();
        var objects = await library.ObjectsByFileAsync(entries.Select(e => (e.Track, e.Track.Files[0].Id)).ToList());
        var loaded = new JsonArray();
        foreach (var entry in entries)
        {
            if (!objects.TryGetValue(entry.Track.Files[0].Id, out var track))
                continue;
            var number = EntryNumber(entry.Id);
            loaded.Add(new JsonObject
            {
                ["id"] = number,
                ["lokiId"] = number,
                ["filepath"] = track["filepath"]!.DeepClone(),
                ["metadata"] = track["metadata"]!.DeepClone(),
            });
        }

        // The first songs of other servers' libraries are fetched while it starts playing.
        await call.Service<P2P.PeerCache>().PrefetchTracksAsync(library.Db, entries.Select(e => e.Track.Id).ToList(), 5, call.Cancellation);
        return MstreamCall.Json(loaded);
    }

    /// <summary>Appends a song, creating the playlist when there is none with that name.</summary>
    private static async Task<IResult> AddToPlaylistAsync(MstreamCall call)
    {
        var file = await new MstreamLibrary(call).ResolveAsync(call.Body.Required("song")) ?? throw MstreamException.NotFound();
        var name = call.Body.Required("playlist");
        var playlists = call.Service<PlaylistService>();
        var playlist = await FindPlaylistAsync(call, name);
        if (playlist is null)
            await playlists.CreateAsync(new CreatePlaylistInput(name, null, false, [file.TrackId]), call.Cancellation);
        else
            await playlists.AddTracksAsync(playlist.Id, new AddPlaylistTracksInput([file.TrackId], null), call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    /// <summary>Removes one entry, by the number <c>load</c> gave it.</summary>
    private static async Task<IResult> RemoveFromPlaylistAsync(MstreamCall call)
    {
        var number = call.Body.Int("id") ?? call.Body.Int("lokiid") ?? throw MstreamException.Invalid("id");
        foreach (var playlist in await EditablePlaylistsAsync(call))
        {
            var entry = (await EntriesAsync(call, playlist.Id)).FirstOrDefault(e => EntryNumber(e.Id) == number);
            if (entry is null)
                continue;
            await call.Service<PlaylistService>().RemoveEntriesAsync(playlist.Id, [entry.Id], call.Cancellation);
            return MstreamCall.Json(new JsonObject());
        }

        throw MstreamException.NotFound();
    }

    private static async Task<IResult> NewPlaylistAsync(MstreamCall call)
    {
        var name = call.Body.Required("title");
        if (await FindPlaylistAsync(call, name) is not null)
            throw new MstreamException(StatusCodes.Status400BadRequest, "Playlist Already Exists");
        await call.Service<PlaylistService>().CreateAsync(new CreatePlaylistInput(name, null, false, []), call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    /// <summary>Creates a playlist with these songs, or replaces the songs of the one with that name.</summary>
    private static async Task<IResult> SavePlaylistAsync(MstreamCall call)
    {
        var name = call.Body.Required("title");
        var library = new MstreamLibrary(call);
        var tracks = new List<Guid>();
        foreach (var path in call.Body.Strings("songs"))
        {
            if (await library.ResolveAsync(path) is { } file)
                tracks.Add(file.TrackId);
        }

        var playlists = call.Service<PlaylistService>();
        var playlist = await FindPlaylistAsync(call, name);
        if (playlist is null)
        {
            await playlists.CreateAsync(new CreatePlaylistInput(name, null, false, tracks), call.Cancellation);
            return MstreamCall.Json(new JsonObject());
        }

        var entries = await EntriesAsync(call, playlist.Id);
        if (entries.Count > 0)
            await playlists.RemoveEntriesAsync(playlist.Id, entries.Select(e => e.Id).ToList(), call.Cancellation);
        if (tracks.Count > 0)
            await playlists.AddTracksAsync(playlist.Id, new AddPlaylistTracksInput(tracks, null), call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    private static async Task<IResult> RenamePlaylistAsync(MstreamCall call)
    {
        var playlist = await FindPlaylistAsync(call, call.Body.Required("oldName")) ?? throw MstreamException.NotFound();
        var name = call.Body.Required("newName");
        if (await FindPlaylistAsync(call, name) is { } existing && existing.Id != playlist.Id)
            throw new MstreamException(StatusCodes.Status400BadRequest, "Playlist Already Exists");
        await call.Service<PlaylistService>().UpdateAsync(playlist.Id, new UpdatePlaylistInput(name, playlist.Description, playlist.IsPublic), call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    private static async Task<IResult> DeletePlaylistAsync(MstreamCall call)
    {
        if (await FindPlaylistAsync(call, call.Body.Required("playlistname")) is { } playlist)
            await call.Service<PlaylistService>().DeleteAsync(playlist.Id, call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    /// <summary>Records a play of a whole song, as mStream apps report them.</summary>
    private static async Task<IResult> ScrobbleAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        if (await library.ResolveAsync(call.Body.String("filePath")) is not { } file)
            return MstreamCall.Json(new JsonObject { ["scrobble"] = false });

        var duration = await library.Db.Tracks.Where(t => t.Id == file.TrackId).Select(t => t.DurationMs).SingleAsync(call.Cancellation);
        await call.Service<ListeningService>().RecordPlayAsync(new RecordPlayInput(file.TrackId, duration, null, "mStream"), call.Cancellation);
        return MstreamCall.Json(new JsonObject());
    }

    /// <summary>The playlist with this name among those the user can change; their own first.</summary>
    private static async Task<Playlist?> FindPlaylistAsync(MstreamCall call, string name) =>
        (await EditablePlaylistsAsync(call)).Where(p => p.Name == name).OrderBy(p => p.Role).FirstOrDefault();

    private static async Task<List<PlaylistEntry>> EntriesAsync(MstreamCall call, Guid id)
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

    /// <summary>A positive integer for an entry, from the random end of its id.</summary>
    private static int EntryNumber(Guid id) => BitConverter.ToInt32(id.ToByteArray(), 12) & int.MaxValue;
}
