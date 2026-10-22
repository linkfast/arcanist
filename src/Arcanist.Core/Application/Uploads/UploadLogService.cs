using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Application.Uploads;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Where an upload came from.</summary>
public enum UploadOrigin
{
    /// <summary>The admin panel (Import → Upload music).</summary>
    Panel,

    /// <summary>Ultimate Desktop.</summary>
    Desktop,

    /// <summary>A web player.</summary>
    Web,

    /// <summary>A phone or tablet app.</summary>
    Mobile,

    /// <summary>An extension (<c>source</c> is its id), such as YouTube Audio.</summary>
    Extension,

    /// <summary>The upload API, by a client that did not say which it is.</summary>
    Api,
}

/// <summary>An upload into a library, by any user and from any client.</summary>
/// <param name="Source">The extension that brought it (such as <c>youtube</c>), for <see cref="UploadOrigin.Extension"/>.</param>
/// <param name="Path">Where it went, relative to the library's folder.</param>
/// <param name="Track">The song, once catalogued.</param>
public sealed record UploadLogEntry(
    Guid Id,
    UploadOrigin Origin,
    string? Source,
    MyUploadStatus Status,
    string Name,
    long? Size,
    Guid? LibraryId,
    string? LibraryName,
    Guid UserId,
    string UserName,
    string? Path,
    Track? Track,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <param name="Active">How many are still waiting for their library's scan: worth asking again in a few seconds.</param>
public sealed record UploadLogPage(IReadOnlyList<UploadLogEntry> Items, string? NextCursor, int TotalCount, int Active);

/// <summary>Which uploads to list; every filter is optional.</summary>
public sealed record UploadLogInput(
    Guid? LibraryId = null,
    Guid? UserId = null,
    UploadOrigin? Origin = null,
    MyUploadStatus? Status = null,
    string? After = null,
    int? First = null);

/// <summary>
/// Every upload into the server's libraries, whoever sent it and however (the admin panel, Ultimate Desktop, web
/// players, phones, extensions): they all go through the same API (<see cref="UploadService"/>), which records them.
/// For those who manage libraries (only those they can access, unless they are administrators); newest first.
/// </summary>
public sealed class UploadLogService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, MyUploadService uploads)
{
    public async Task<UploadLogPage> ListAsync(UploadLogInput input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        await uploads.ResolveAsync(null, cancellationToken);

        var query = db.UserUploads.AsNoTracking().Where(u => u.Kind == UserUploadKind.Library);
        if (!access.IsAdmin)
        {
            var reachable = access.LibraryIds.ToList();
            query = query.Where(u => u.LibraryId != null && reachable.Contains(u.LibraryId.Value));
        }

        if (input.LibraryId is { } library)
            query = query.Where(u => u.LibraryId == library);
        if (input.UserId is { } user)
            query = query.Where(u => u.UserId == user);
        // Converted before the query: the columns hold the entities' enums as text.
        if (input.Status is { } status)
        {
            var wanted = Enum.Parse<UserUploadStatus>(status.ToString());
            query = query.Where(u => u.Status == wanted);
        }

        if (input.Origin is UploadOrigin.Extension)
            query = query.Where(u => u.Source != null);
        else if (input.Origin is UploadOrigin.Api)
            query = query.Where(u => u.Source == null && u.Client == null);
        else if (input.Origin is { } origin)
        {
            UploadClient? client = Enum.Parse<UploadClient>(origin.ToString());
            query = query.Where(u => u.Source == null && u.Client == client);
        }

        var total = await query.CountAsync(cancellationToken);
        var active = await query.CountAsync(u => u.Status == UserUploadStatus.Processing, cancellationToken);
        if (input.After is not null)
        {
            var (at, id) = TimeCursor.Decode(input.After);
            query = query.Where(u => u.CreatedAt < at || (u.CreatedAt == at && u.Id.CompareTo(id) < 0));
        }

        var size = ListeningService.PageSize(input.First);
        var page = await query.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id).Take(size + 1)
            .Select(u => new { Upload = u, UserName = u.User.UserName, LibraryName = u.LibraryId == null ? null : db.Libraries.Where(l => l.Id == u.LibraryId).Select(l => l.Name).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        var next = page.Count > size ? TimeCursor.Encode(page[size - 1].Upload.CreatedAt, page[size - 1].Upload.Id) : null;
        page = page.Take(size).ToList();

        var tracks = await catalog.VisibleTracksAsync(access, page.Where(p => p.Upload.TrackId is not null).Select(p => p.Upload.TrackId!.Value).Distinct().ToList(),
            cancellationToken);
        return new UploadLogPage(page.Select(p =>
            {
                var u = p.Upload;
                var origin = u.Source is not null ? UploadOrigin.Extension : u.Client is { } client ? Enum.Parse<UploadOrigin>(client.ToString()) : UploadOrigin.Api;
                return new UploadLogEntry(u.Id, origin, u.Source, (MyUploadStatus)u.Status, u.Name, u.Size, u.LibraryId, p.LibraryName, u.UserId,
                    p.UserName, u.Path, u.TrackId is { } track ? tracks.GetValueOrDefault(track) : null, u.ErrorCode, u.CreatedAt, u.UpdatedAt);
            }).ToList(),
            next, total, active);
    }
}
