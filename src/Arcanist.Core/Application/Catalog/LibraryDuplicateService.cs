using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A user, as the duplicates page names them.</summary>
public sealed record DuplicateUser(Guid Id, string Name);

/// <summary>A library that has a copy of a song another library has too.</summary>
/// <param name="CanDelete">The caller may delete this library's copies: DELETE_TRACKS, not only a listener there, and
/// files Arcanist can write (local or S3; those of other servers and Ultimate Cloud are not).</param>
/// <param name="LosingUsers">The users who would stop having the song if this library's copies were deleted: they
/// listen to this library but to none of the others that have it. Administrators reach every library and never lose it.</param>
public sealed record DuplicateLibrary(Guid Id, string Name, bool VisibleToAll, bool CanDelete, IReadOnlyList<DuplicateUser> LosingUsers);

/// <summary>A song with copies in several libraries: its files (in <c>track.files</c>, by <c>libraryId</c>) and the libraries.</summary>
public sealed record LibraryDuplicate(Track Track, IReadOnlyList<DuplicateLibrary> Libraries);

/// <param name="LibraryId">Only songs that this library has, among others.</param>
public sealed record LibraryDuplicatesInput(Guid? LibraryId, string? After, int? First);

public sealed record LibraryDuplicatePage(IReadOnlyList<LibraryDuplicate> Items, string? NextCursor, int TotalCount);

/// <summary>
/// The songs that more than one library has (the same track, with copies in each): to free space or tidy libraries
/// up, saying who would stop having a song if one library's copies went. Personal libraries (a user's own Ultimate
/// Cloud) are not counted: only their owner sees them. Requires MANAGE_LIBRARIES.
/// </summary>
public sealed class LibraryDuplicateService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog)
{
    public const int MaxResults = 100;

    public async Task<LibraryDuplicatePage> ListAsync(LibraryDuplicatesInput input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        if (input.LibraryId is { } only && !access.CanAccessLibrary(only))
            throw new ArcanistException.NotFound();

        var files = catalog.VisibleFiles(access).Where(f => f.LibrarySource.Library.OwnerUserId == null);
        var shared = files
            .GroupBy(f => f.TrackId)
            .Where(g => g.Select(f => f.LibrarySource.LibraryId).Distinct().Count() > 1)
            .Select(g => g.Key);
        if (input.LibraryId is { } library)
            shared = shared.Where(id => files.Any(f => f.TrackId == id && f.LibrarySource.LibraryId == library));

        var total = await shared.CountAsync(cancellationToken);
        var after = Guid.TryParse(input.After, out var cursor) ? cursor : (Guid?)null;
        var size = Math.Clamp(input.First ?? 50, 1, MaxResults);
        var ids = await shared.Where(id => after == null || id.CompareTo(after.Value) > 0).OrderBy(id => id).Take(size + 1).ToListAsync(cancellationToken);
        var next = ids.Count > size ? ids[size - 1].ToString() : null;
        ids = ids.Take(size).ToList();

        var tracks = await catalog.VisibleTracksAsync(access, ids, cancellationToken);
        var libraryIds = tracks.Values.SelectMany(t => t.Files).Select(f => f.LibraryId).Distinct().ToList();
        var libraries = await db.Libraries.AsNoTracking().Where(l => libraryIds.Contains(l.Id) && l.OwnerUserId == null)
            .Select(l => new { l.Id, l.Name, l.VisibleToAll, Writable = l.Sources.All(s => s.Kind == LibrarySourceKind.Local || s.Kind == LibrarySourceKind.S3) })
            .ToDictionaryAsync(l => l.Id, cancellationToken);
        var listeners = await ListenersAsync(cancellationToken);
        var deletes = access.Has(Permissions.DeleteTracks);

        var items = new List<LibraryDuplicate>();
        foreach (var id in ids)
        {
            if (!tracks.TryGetValue(id, out var track))
                continue;
            var group = track.Files.Select(f => f.LibraryId).Where(libraries.ContainsKey).Distinct().ToList();
            if (group.Count < 2)
                continue;
            items.Add(new LibraryDuplicate(track, group
                .Select(l => libraries[l])
                .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(l => new DuplicateLibrary(l.Id, l.Name, l.VisibleToAll,
                    deletes && l.Writable && access.CanChange(l.Id),
                    listeners.Where(u => u.Reaches(l.Id, l.VisibleToAll) && !group.Where(other => other != l.Id).Any(other => u.Reaches(other, libraries[other].VisibleToAll)))
                        .Select(u => u.User).OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase).ToList()))
                .ToList()));
        }

        return new LibraryDuplicatePage(items, next, total);
    }

    private sealed record Listener(DuplicateUser User, HashSet<Guid> Libraries)
    {
        public bool Reaches(Guid library, bool visibleToAll) => visibleToAll || Libraries.Contains(library);
    }

    /// <summary>The users who are not administrators (those reach every library), with the libraries granted to them.</summary>
    private async Task<List<Listener>> ListenersAsync(CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.Role != UserRole.Admin && !u.IsDisabled)
            .Select(u => new { u.Id, u.UserName, u.DisplayName })
            .ToListAsync(cancellationToken);
        var grants = (await db.LibraryAccess.AsNoTracking().Select(a => new { a.UserId, a.LibraryId }).ToListAsync(cancellationToken))
            .ToLookup(a => a.UserId, a => a.LibraryId);
        return users.Select(u => new Listener(new DuplicateUser(u.Id, string.IsNullOrWhiteSpace(u.DisplayName) ? u.UserName : u.DisplayName),
            grants[u.Id].ToHashSet())).ToList();
    }
}
