using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Libraries;
using Arcanist.Core.Organization;
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
    LibraryWatchSignal watches,
    LibraryMoves moves,
    CloudLibraries cloud,
    Cloud.CloudPlus cloudPlus,
    Storage.ILibraryStorageResolver storages,
    TimeProvider time,
    SettingsStore settings,
    IHttpContextAccessor http)
{
    /// <param name="includeVirtual">Also "All" (first), every library the caller can access, named in <paramref name="language"/>.</param>
    public async Task<IReadOnlyList<Library>> ListAsync(CancellationToken cancellationToken, bool includeVirtual = false, string? language = null)
    {
        var access = guard.Authenticated();

        // In the order they were made: editing one never moves it.
        var libraries = await Visible(access)
            .AsNoTracking()
            .Include(l => l.Sources)
            .OrderBy(l => l.Number)
            .ToListAsync(cancellationToken);

        var servers = await SharingServersAsync(cancellationToken);
        var models = libraries.Select(l => l.ToModel(access.Has(Permissions.ManageLibraries), access, cloud) with { SharedBy = servers.GetValueOrDefault(l.Id) }).ToList();
        if (includeVirtual)
            models.Insert(0, await AllAsync(libraries, language, cancellationToken));
        return models;
    }

    public async Task<Library> GetAsync(Guid id, CancellationToken cancellationToken, string? language = null)
    {
        var access = guard.Authenticated();
        if (VirtualLibraries.IsVirtual(id))
            return await AllAsync(await Visible(access).AsNoTracking().ToListAsync(cancellationToken), language, cancellationToken);
        var library = await Visible(access).AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
                      ?? throw new ArcanistException.NotFound();

        return library.ToModel(access.Has(Permissions.ManageLibraries), access, cloud)
            with { SharedBy = (await SharingServersAsync(cancellationToken)).GetValueOrDefault(library.Id) };
    }

    /// <summary>The name of the server that shares each library received through the P2P network.</summary>
    private async Task<Dictionary<Guid, string>> SharingServersAsync(CancellationToken cancellationToken) =>
        await db.P2PRemotes.AsNoTracking().ToDictionaryAsync(r => r.LibraryId, r => r.Name, cancellationToken);

    /// <summary>"All": every library of <paramref name="libraries"/> (those the caller can access) at once.</summary>
    private async Task<Library> AllAsync(IReadOnlyList<LibraryEntity> libraries, string? language, CancellationToken cancellationToken)
    {
        var languages = await RequestLanguages.ResolveAsync(language, http.HttpContext, settings, cancellationToken);
        return new Library(VirtualLibraries.All, VirtualLibraries.AllName(languages), true, null, false,
            libraries.Count == 0 ? time.GetUtcNow().UtcDateTime : libraries.Min(l => l.CreatedAt),
            libraries.Max(l => l.LastScanAt), null, null, Virtual: true);
    }

    public async Task<Library> CreateAsync(CreateLibraryInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);

        var errors = new ValidationErrors();
        LibraryRules.ValidateName(input.Name, "name", errors);
        LibraryRules.ValidateScanInterval(input.ScanIntervalMinutes, "scanIntervalMinutes", errors);
        if (input.Sources is not { Count: > 0 })
            errors.Add("sources", ErrorCodes.Required);
        else if (input.Sources.Count > 1)
            errors.Add("sources", ErrorCodes.TooMany);

        var locations = await rules.ValidateLocationsAsync(
            input.Sources?.Select(s => (string?)s.Location).ToList() ?? [], [], i => $"sources[{i}].location", errors, cancellationToken);
        await EnsureUniqueNameAsync(input.Name, exceptId: null, errors, cancellationToken);
        errors.ThrowIfAny();

        var library = new LibraryEntity
        {
            Number = await NextNumberAsync(db, cancellationToken),
            Name = input.Name.Trim(),
            VisibleToAll = input.VisibleToAll,
            ScanIntervalMinutes = input.ScanIntervalMinutes,
            WatchForChanges = input.WatchForChanges,
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
        if (library.WatchForChanges)
            watches.Refresh();
        await scans.EnqueueAsync(library.Id, ScanKind.Full, actor.UserId, cancellationToken);

        return library.ToModel(includeSources: true, guard.Authenticated(), cloud);
    }

    public async Task<Library> UpdateAsync(Guid id, UpdateLibraryInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        var library = await FindManageableAsync(actor, id, cancellationToken);
        await cloud.EnsureEnabledAsync(db, id, cancellationToken);

        var errors = new ValidationErrors();
        LibraryRules.ValidateName(input.Name, "name", errors);
        LibraryRules.ValidateScanInterval(input.ScanIntervalMinutes, "scanIntervalMinutes", errors);
        if (input.WatchForChanges && !library.Sources.All(s => LibraryRules.CanWatch(s.Kind)))
            errors.Add("watchForChanges", ErrorCodes.WatchUnsupported);
        await EnsureUniqueNameAsync(input.Name, exceptId: id, errors, cancellationToken);
        errors.ThrowIfAny();

        var visibilityChanged = library.VisibleToAll != input.VisibleToAll;
        var watchChanged = library.WatchForChanges != input.WatchForChanges;
        library.Name = input.Name.Trim();
        library.VisibleToAll = input.VisibleToAll;
        library.ScanIntervalMinutes = input.ScanIntervalMinutes;
        library.WatchForChanges = input.WatchForChanges;
        await db.SaveChangesAsync(cancellationToken);

        if (visibilityChanged)
            accessCache.InvalidateAll();
        if (watchChanged)
            watches.Refresh();

        return library.ToModel(includeSources: true, guard.Authenticated(), cloud);
    }

    /// <summary>
    /// Renames a library, nothing else: for apps that show the server's libraries as their own. Needs MANAGE_LIBRARIES
    /// (and a library the manager can access), or to be the owner of a personal library (their own Ultimate Cloud).
    /// </summary>
    public async Task<Library> RenameAsync(Guid id, RenameLibraryInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (VirtualLibraries.IsVirtual(id))
            throw new ArcanistException.Conflict(ErrorCodes.LibraryVirtual);
        var library = await Visible(access).Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        if (library.OwnerUserId != access.UserId && !access.Has(Permissions.ManageLibraries))
            throw new ArcanistException.Forbidden();
        if (library.ManagedBy is not null)
            throw new ArcanistException.Conflict(ErrorCodes.LibraryManaged);

        var errors = new ValidationErrors();
        LibraryRules.ValidateName(input.Name, "name", errors);
        await EnsureUniqueNameAsync(input.Name, exceptId: id, errors, cancellationToken);
        errors.ThrowIfAny();

        library.Name = input.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return library.ToModel(access.Has(Permissions.ManageLibraries), access, cloud)
            with { SharedBy = (await SharingServersAsync(cancellationToken)).GetValueOrDefault(library.Id) };
    }

    /// <summary>Removes the library and its files from the catalog. Files on disk are never touched.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        guard.RecentAuthentication();
        var library = await FindManageableAsync(actor, id, cancellationToken);
        if (library.ManagedBy is not null)
            throw new ArcanistException.Conflict(ErrorCodes.LibraryManaged);
        // Ultimate Cloud Plus keeps S3 libraries from being the only ones left.
        if (cloudPlus.S3 is { } s3 && await s3.CanDeleteAsync(id, cancellationToken) is { } refused)
            throw new ArcanistException.Conflict(refused);
        await RemoveAsync(db, scans, library, cancellationToken);
        accessCache.InvalidateAll();
    }

    /// <summary>Removes a library from the catalog (never its files); its number is never given again.</summary>
    internal static async Task RemoveAsync(ArcanistDbContext db, ScanCoordinator scans, LibraryEntity library, CancellationToken cancellationToken)
    {
        await scans.CancelLibraryAsync(library.Id, cancellationToken);
        if (library.Number > await LastNumberAsync(db, cancellationToken))
        {
            var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKeys.LastLibraryNumber, cancellationToken);
            if (setting is null)
                db.Settings.Add(new Setting { Key = SettingKeys.LastLibraryNumber, Value = library.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            else
                setting.Value = library.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        db.Libraries.Remove(library);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Library> AddSourceAsync(Guid libraryId, LibrarySourceInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        var library = await FindManageableAsync(actor, libraryId, cancellationToken);
        if (library.ManagedBy is not null)
            throw new ArcanistException.Conflict(ErrorCodes.LibraryManaged);
        // Libraries shared by another server or kept in Ultimate Cloud show only that: folders are not added to them.
        if (library.Sources.Any(s => s.Kind != LibrarySourceKind.Local))
            throw ArcanistException.Field("location", ErrorCodes.ReadOnly);
        // A local library has one folder. Libraries created with several before that rule keep them, but get no more.
        if (library.Sources.Count > 0)
            throw ArcanistException.Field("location", ErrorCodes.TooMany);

        var errors = new ValidationErrors();
        var locations = await rules.ValidateLocationsAsync(
            [input.Location], library.Sources.Select(s => s.Location).ToList(), _ => "location", errors, cancellationToken);
        errors.ThrowIfAny();

        db.LibrarySources.Add(new LibrarySourceEntity { LibraryId = library.Id, Kind = LibrarySourceKind.Local, Location = locations[0]! });
        await db.SaveChangesAsync(cancellationToken);
        watches.Refresh();
        await scans.EnqueueAsync(library.Id, ScanKind.Incremental, actor.UserId, cancellationToken);

        return await GetManagedAsync(library.Id, cancellationToken);
    }

    /// <summary>Removes a source and its files from the catalog. Files on disk are never touched.</summary>
    public async Task<Library> RemoveSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageLibraries);
        var source = await db.LibrarySources.SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        if ((await FindManageableAsync(actor, source.LibraryId, cancellationToken)).ManagedBy is not null)
            throw new ArcanistException.Conflict(ErrorCodes.LibraryManaged);
        if (source.Kind == LibrarySourceKind.Local
            && !await db.LibrarySources.AnyAsync(s => s.LibraryId == source.LibraryId && s.Id != source.Id, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.LastSource);
        if (moves.IsMoving(source.LibraryId))
            throw new ArcanistException.Conflict(ErrorCodes.Busy);

        db.LibrarySources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);
        watches.Refresh();

        // The scan removes tracks left without files.
        await scans.EnqueueAsync(source.LibraryId, ScanKind.Incremental, actor.UserId, cancellationToken);

        return await GetManagedAsync(source.LibraryId, cancellationToken);
    }

    /// <summary>
    /// Moves a local library's folder to another folder of the server, in the background (see <see cref="LibraryMoves"/>).
    /// Administrators only, with a recent authentication: it writes, and maybe deletes, the server's files.
    /// </summary>
    public async Task<LibrarySourceMove> MoveSourceAsync(Guid sourceId, MoveLibrarySourceInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        var source = await db.LibrarySources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
                     ?? throw new ArcanistException.NotFound();
        if (source.Kind != LibrarySourceKind.Local)
            throw ArcanistException.Field("id", ErrorCodes.ReadOnly);
        if (moves.IsMoving(source.LibraryId))
            throw new ArcanistException.Conflict(ErrorCodes.Busy);

        var errors = new ValidationErrors();
        var destination = (await rules.ValidateLocationsAsync([input.Destination], [source.Location], _ => "destination", errors, cancellationToken))[0];
        errors.ThrowIfAny();
        if (!await storages.For(LibrarySourceKind.Local).CanWriteAsync(new Storage.StorageLocation(LibrarySourceKind.Local, destination!), cancellationToken))
            throw ArcanistException.Field("destination", ErrorCodes.ReadOnly);

        return moves.Start(source, destination!, input.KeepOriginals, actor.UserId);
    }

    /// <summary>Folder moves since the server started, the latest first. Administrators only.</summary>
    public IReadOnlyList<LibrarySourceMove> Moves()
    {
        guard.Admin();
        return moves.List();
    }

    /// <summary>Lists folders on the server for choosing library locations. Administrators only: it reveals the
    /// server's file system.</summary>
    public ServerDirectoryListing BrowseServerDirectories(string? path)
    {
        guard.Admin();
        return Browse(directories, path);
    }

    /// <summary>Creates a folder on the server, to pick it as a location. Administrators only.</summary>
    public ServerDirectory CreateServerDirectory(CreateServerDirectoryInput input)
    {
        guard.Admin();
        return CreateDirectory(directories, input);
    }

    internal static ServerDirectory CreateDirectory(DirectoryBrowser directories, CreateServerDirectoryInput input)
    {
        var (directory, error) = directories.Create(input.Parent, input.Name);
        return error switch
        {
            null => new ServerDirectory(directory!.Name, directory.Path, directory.IsHidden),
            DirectoryBrowseError.InvalidName => throw ArcanistException.Field("name", ErrorCodes.InvalidFormat),
            DirectoryBrowseError.AlreadyExists => throw ArcanistException.Field("name", ErrorCodes.Duplicate),
            DirectoryBrowseError.ReadOnly => throw ArcanistException.Field("parent", ErrorCodes.ReadOnly),
            _ => throw new ArcanistException.NotFound(),
        };
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

    private IQueryable<LibraryEntity> Visible(UserAccess access) => Visible(db, access);

    /// <summary>The libraries a user can access (all of them for administrators).</summary>
    internal static IQueryable<LibraryEntity> Visible(ArcanistDbContext db, UserAccess access)
    {
        // Other users' personal libraries (their own Ultimate Cloud) are theirs alone, even for administrators.
        if (access.IsAdmin)
            return db.Libraries.Where(l => l.OwnerUserId == null || l.OwnerUserId == access.UserId);

        var ids = access.LibraryIds.ToList();
        return db.Libraries.Where(l => ids.Contains(l.Id));
    }

    private async Task<LibraryEntity> FindManageableAsync(UserAccess actor, Guid id, CancellationToken cancellationToken) =>
        await Visible(actor).Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
        ?? throw new ArcanistException.NotFound();

    private async Task<Library> GetManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.AsNoTracking().Include(l => l.Sources).SingleAsync(l => l.Id == id, cancellationToken);
        return library.ToModel(includeSources: true, guard.Authenticated(), cloud);
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

    /// <summary>The next <see cref="LibraryEntity.Number"/>: never reused, even after deleting the last library.</summary>
    internal static async Task<int> NextNumberAsync(ArcanistDbContext db, CancellationToken cancellationToken) =>
        Math.Max(await db.Libraries.MaxAsync(l => (int?)l.Number, cancellationToken) ?? 0, await LastNumberAsync(db, cancellationToken)) + 1;

    private static async Task<int> LastNumberAsync(ArcanistDbContext db, CancellationToken cancellationToken) =>
        await db.Settings.Where(s => s.Key == SettingKeys.LastLibraryNumber).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken) is { } value
            && int.TryParse(value, out var number) ? number : 0;

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
