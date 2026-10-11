using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Libraries;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using LibrarySourceEntity = Arcanist.Core.Data.Entities.LibrarySource;

namespace Arcanist.Core.Application.Libraries;

/// <summary>
/// Libraries and their sources. Everyone sees the libraries they can access; managing them requires
/// <see cref="Permissions.ManageLibraries"/>, and non-administrators can only manage libraries they can access.
/// Libraries a caller cannot access are reported as not found, so their existence is not revealed.
/// </summary>
public sealed class LibraryService(
    ArcanistDbContext db,
    AccessGuard guard,
    LibraryRules rules,
    DirectoryBrowser directories,
    UserAccessCache accessCache,
    ScanCoordinator scans,
    TimeProvider time)
{
    public async Task<IReadOnlyList<Library>> ListAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();

        var libraries = await Visible(access)
            .AsNoTracking()
            .Include(l => l.Sources)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

        return libraries.Select(l => l.ToModel(access.Has(Permissions.ManageLibraries))).ToList();
    }

    public async Task<Library> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var library = await Visible(access).AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
                      ?? throw new ArcanistException.NotFound();

        return library.ToModel(access.Has(Permissions.ManageLibraries));
    }

    public async Task<Library> CreateAsync(CreateLibraryInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);

        var errors = new ValidationErrors();
        LibraryRules.ValidateName(input.Name, "name", errors);
        LibraryRules.ValidateScanInterval(input.ScanIntervalHours, "scanIntervalHours", errors);
        if (input.Sources is not { Count: > 0 })
            errors.Add("sources", ErrorCodes.Required);

        var locations = await rules.ValidateLocationsAsync(
            input.Sources?.Select(s => (string?)s.Location).ToList() ?? [], [], i => $"sources[{i}].location", errors, cancellationToken);
        await EnsureUniqueNameAsync(input.Name, exceptId: null, errors, cancellationToken);
        errors.ThrowIfAny();

        var library = new LibraryEntity
        {
            Name = input.Name.Trim(),
            VisibleToAll = input.VisibleToAll,
            ScanIntervalHours = input.ScanIntervalHours,
            CreatedAt = Now(),
        };
        library.Sources.AddRange(locations.Select(location => new LibrarySourceEntity
        {
            Library = library,
            Kind = LibrarySourceKind.Local,
            Location = location!,
        }));

        // A manager who is not an administrator must keep seeing what they create.
        if (!actor.IsAdmin)
            library.Access.Add(new LibraryAccess { UserId = actor.UserId, LibraryId = library.Id });

        db.Libraries.Add(library);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        await scans.EnqueueAsync(library.Id, ScanKind.Full, actor.UserId, cancellationToken);

        return library.ToModel(includeSources: true);
    }

    public async Task<Library> UpdateAsync(Guid id, UpdateLibraryInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        var library = await FindManageableAsync(actor, id, cancellationToken);

        var errors = new ValidationErrors();
        LibraryRules.ValidateName(input.Name, "name", errors);
        LibraryRules.ValidateScanInterval(input.ScanIntervalHours, "scanIntervalHours", errors);
        await EnsureUniqueNameAsync(input.Name, exceptId: id, errors, cancellationToken);
        errors.ThrowIfAny();

        var visibilityChanged = library.VisibleToAll != input.VisibleToAll;
        library.Name = input.Name.Trim();
        library.VisibleToAll = input.VisibleToAll;
        library.ScanIntervalHours = input.ScanIntervalHours;
        await db.SaveChangesAsync(cancellationToken);

        if (visibilityChanged)
            accessCache.InvalidateAll();

        return library.ToModel(includeSources: true);
    }

    /// <summary>Removes the library and its files from the catalog. Files on disk are never touched.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        guard.RecentAuthentication();
        var library = await FindManageableAsync(actor, id, cancellationToken);

        await scans.CancelLibraryAsync(library.Id, cancellationToken);
        db.Libraries.Remove(library);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
    }

    public async Task<Library> AddSourceAsync(Guid libraryId, LibrarySourceInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        var library = await FindManageableAsync(actor, libraryId, cancellationToken);

        var errors = new ValidationErrors();
        var locations = await rules.ValidateLocationsAsync(
            [input.Location], library.Sources.Select(s => s.Location).ToList(), _ => "location", errors, cancellationToken);
        errors.ThrowIfAny();

        db.LibrarySources.Add(new LibrarySourceEntity { LibraryId = library.Id, Kind = LibrarySourceKind.Local, Location = locations[0]! });
        await db.SaveChangesAsync(cancellationToken);
        await scans.EnqueueAsync(library.Id, ScanKind.Incremental, actor.UserId, cancellationToken);

        return await GetManagedAsync(library.Id, cancellationToken);
    }

    /// <summary>Removes a source and its files from the catalog. Files on disk are never touched.</summary>
    public async Task<Library> RemoveSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        var source = await db.LibrarySources.SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        await FindManageableAsync(actor, source.LibraryId, cancellationToken);

        db.LibrarySources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);

        // The scan removes tracks left without files.
        await scans.EnqueueAsync(source.LibraryId, ScanKind.Incremental, actor.UserId, cancellationToken);

        return await GetManagedAsync(source.LibraryId, cancellationToken);
    }

    /// <summary>Lists folders on the server for choosing library locations. Administrators only: it reveals the
    /// server's file system.</summary>
    public ServerDirectoryListing BrowseServerDirectories(string? path)
    {
        guard.Admin();
        return Browse(directories, path);
    }

    internal static ServerDirectoryListing Browse(DirectoryBrowser directories, string? path)
    {
        var (listing, error) = directories.List(path);
        return error switch
        {
            null => new ServerDirectoryListing(
                listing!.Path,
                listing.Parent,
                listing.Directories.Select(d => new ServerDirectory(d.Name, d.Path, d.IsHidden)).ToList()),
            DirectoryBrowseError.NotFound => throw new ArcanistException.NotFound(),
            _ => throw new ArcanistException.Forbidden(ErrorCodes.AccessDenied),
        };
    }

    private IQueryable<LibraryEntity> Visible(UserAccess access)
    {
        if (access.IsAdmin)
            return db.Libraries;

        var ids = access.LibraryIds.ToList();
        return db.Libraries.Where(l => ids.Contains(l.Id));
    }

    private async Task<LibraryEntity> FindManageableAsync(UserAccess actor, Guid id, CancellationToken cancellationToken) =>
        await Visible(actor).Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
        ?? throw new ArcanistException.NotFound();

    private async Task<Library> GetManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.AsNoTracking().Include(l => l.Sources).SingleAsync(l => l.Id == id, cancellationToken);
        return library.ToModel(includeSources: true);
    }

    private async Task EnsureUniqueNameAsync(string? name, Guid? exceptId, ValidationErrors errors, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        // Names are few; comparing normalized forms in memory avoids provider-specific collations.
        var normalized = TextNormalizer.Normalize(name);
        var names = await db.Libraries.Where(l => l.Id != exceptId).Select(l => l.Name).ToListAsync(cancellationToken);
        if (names.Any(n => TextNormalizer.Normalize(n) == normalized))
            errors.Add("name", ErrorCodes.Duplicate);
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
