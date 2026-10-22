using System.Text.Json;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using DropFolderEntity = Arcanist.Core.Data.Entities.DropFolder;
using ImportItemEntity = Arcanist.Core.Data.Entities.ImportItem;

namespace Arcanist.Core.Application.Organization;

/// <summary>
/// Drop folders and what was found in them. Drop folders are configured by administrators (they are server paths);
/// files waiting for a decision are handled with MANAGE_LIBRARIES on their library.
/// </summary>
public sealed class ImportService(
    ArcanistDbContext db,
    AccessGuard guard,
    ILibraryStorageResolver storages,
    ImportMonitor monitor,
    Hosting.ArcanistPaths paths,
    Capabilities.ICapabilityService capabilities,
    IServiceProvider services,
    TimeProvider time)
{
    public const int MaxItems = 200;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<IReadOnlyList<DropFolder>> DropFoldersAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var folders = await db.DropFolders.AsNoTracking().Include(d => d.LibrarySource).OrderBy(d => d.Path).ToListAsync(cancellationToken);
        var result = new List<DropFolder>();
        foreach (var folder in folders)
            result.Add(await ToModelAsync(folder, cancellationToken));
        return result;
    }

    public async Task<DropFolder> CreateDropFolderAsync(DropFolderInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        var folder = new DropFolderEntity { Path = "", CreatedAt = Now(), Kind = input.Kind };
        await ApplyAsync(folder, input, cancellationToken);
        db.DropFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        monitor.RequestRun();
        if (folder.Kind == DropFolderKind.UltimateCloudDrop)
            services.GetService<Core.Cloud.CloudDropWorker>()?.Wake();
        return await ToModelAsync(folder, cancellationToken);
    }

    public async Task<DropFolder> UpdateDropFolderAsync(Guid id, DropFolderInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        var folder = await db.DropFolders.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        await ApplyAsync(folder, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        monitor.RequestRun();
        if (folder.Kind == DropFolderKind.UltimateCloudDrop)
            services.GetService<Core.Cloud.CloudDropWorker>()?.Wake();
        return await ToModelAsync(folder, cancellationToken);
    }

    /// <summary>Stops importing from the folder and forgets its history. Its files are not touched.</summary>
    public async Task DeleteDropFolderAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var folder = await db.DropFolders.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        db.DropFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAsync(DropFolderEntity folder, DropFolderInput input, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var storage = storages.For(LibrarySourceKind.Local);

        // Cloud Drop gives the audio without its tags: Ultimate Cloud's sync component puts them back (installed now).
        if (folder.Kind == DropFolderKind.UltimateCloudDrop && input.Enabled
            && await services.GetRequiredService<Core.Cloud.IUltimateAudio>().EnsureInstalledAsync(cancellationToken) is { } unavailable)
            throw new ArcanistException.Conflict(unavailable);

        string? path = null;
        if (folder.Kind == DropFolderKind.UltimateCloudDrop)
        {
            // Its songs are downloaded to a folder of the server's own, and imported from there.
            path = Path.Combine(paths.Cache, "cloud-drop", folder.Id.ToString("N"));
            Directory.CreateDirectory(path);
            if (!capabilities.IsEnabled(Capabilities.Capability.UltimateCloudSource))
                errors.Add("kind", ErrorCodes.CloudNotLinked);
            else if (await db.DropFolders.AnyAsync(d => d.Id != folder.Id && d.Kind == DropFolderKind.UltimateCloudDrop, cancellationToken))
                errors.Add("kind", ErrorCodes.Duplicate);
        }
        else if (string.IsNullOrWhiteSpace(input.Path))
            errors.Add("path", ErrorCodes.Required);
        else
        {
            var probe = await storage.ProbeAsync(new StorageLocation(LibrarySourceKind.Local, input.Path.Trim()), cancellationToken);
            if (!probe.IsAvailable)
            {
                errors.Add("path", probe.Problem switch
                {
                    StorageProblem.Invalid => ErrorCodes.InvalidFormat,
                    StorageProblem.AccessDenied => ErrorCodes.AccessDenied,
                    _ => ErrorCodes.NotFound,
                });
            }
            else
            {
                path = storage.NormalizeLocation(input.Path);
                // Inside a library its files would be scanned before being imported; inside another drop folder,
                // imported twice.
                var libraries = await db.LibrarySources.Where(s => s.Kind == LibrarySourceKind.Local).Select(s => s.Location).ToListAsync(cancellationToken);
                var drops = await db.DropFolders.Where(d => d.Id != folder.Id && d.Kind == DropFolderKind.Folder).Select(d => d.Path).ToListAsync(cancellationToken);
                if (libraries.Concat(drops).Any(other => storage.Contains(other, path) || storage.Contains(path, other)))
                    errors.Add("path", ErrorCodes.Overlapping);
                else if (!await storage.CanWriteAsync(new StorageLocation(LibrarySourceKind.Local, path), cancellationToken))
                    errors.Add("path", ErrorCodes.ReadOnly);
            }
        }

        var source = await db.LibrarySources.Include(s => s.Library).SingleOrDefaultAsync(s => s.Id == input.LibrarySourceId, cancellationToken);
        if (source is null)
            errors.Add("librarySourceId", ErrorCodes.NotFound);
        else if (CloudLibraries.DependsOnCloud(source.Kind) && source.Library.ManagedBy is null && !services.GetRequiredService<CloudLibraries>().Enabled)
            errors.Add("librarySourceId", ErrorCodes.LibraryDisabled);
        else if (!await storages.For(source.Kind).CanWriteAsync(StorageLocation.From(source), cancellationToken))
            errors.Add("librarySourceId", ErrorCodes.ReadOnly);

        if (input is { ConvertTo: { } format, ConvertBitRate: { } bitRate })
        {
            var profile = TranscodeProfile.For(format);
            if (!profile.IsLossless && (bitRate < profile.MinBitRate || bitRate > profile.MaxBitRate))
                errors.Add("convertBitRate", ErrorCodes.InvalidFormat);
        }

        string? uploadAccount = null;
        if (folder.Kind == DropFolderKind.UltimateCloudDrop && input.UploadToCloud)
        {
            // The account's library that receives the uploads: the server's account, or the administrator's own.
            var auth = services.GetRequiredService<Core.Cloud.UltimateCloudAuth>();
            var server = (await auth.LinkAsync(cancellationToken))?.Subject;
            var own = (await auth.LinkAsync(cancellationToken, guard.Authenticated().UserId))?.Subject;
            uploadAccount = input.UploadAccount ?? server ?? own;
            if (uploadAccount is null || (uploadAccount != server && uploadAccount != own && uploadAccount != folder.UploadAccount))
                errors.Add("uploadAccount", ErrorCodes.CloudNotLinked);
        }

        errors.ThrowIfAny();

        folder.Path = path!;
        folder.LibrarySourceId = source!.Id;
        folder.Enabled = input.Enabled;
        folder.DuplicatePolicy = input.DuplicatePolicy;
        folder.ConvertTo = input.ConvertTo;
        folder.ConvertBitRate = input.ConvertTo is { } target && !TranscodeProfile.For(target).IsLossless ? input.ConvertBitRate : null;
        folder.ConvertLossy = input.ConvertTo is not (null or TranscodeFormat.Flac) && input.ConvertLossy;
        folder.Mode = input.Mode;
        folder.UploadToCloud = folder.Kind == DropFolderKind.UltimateCloudDrop && input.UploadToCloud;
        folder.UploadAccount = uploadAccount;
    }

    /// <summary>
    /// Files of the drop folders whose library the caller manages, most recently changed first. Without
    /// <paramref name="status"/>, those still in a drop folder (every state but imported and discarded).
    /// </summary>
    public async Task<IReadOnlyList<ImportItem>> ItemsAsync(ImportStatus? status, Guid? dropFolderId, int? first, CancellationToken cancellationToken)
    {
        var items = Visible();
        items = status is { } wanted
            ? items.Where(i => i.Status == wanted)
            : items.Where(i => i.Status != ImportStatus.Imported && i.Status != ImportStatus.Discarded);
        if (dropFolderId is { } folderId)
            items = items.Where(i => i.DropFolderId == folderId);

        var page = await items.AsNoTracking()
            .OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id)
            .Take(Math.Clamp(first ?? 50, 1, MaxItems))
            .ToListAsync(cancellationToken);
        return page.Select(ToModel).ToList();
    }

    public async Task<ImportSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        var counts = await Visible().GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        int Count(ImportStatus status) => counts.GetValueOrDefault(status);
        return new ImportSummary(
            Count(ImportStatus.Pending), Count(ImportStatus.NeedsReview), Count(ImportStatus.Duplicate),
            Count(ImportStatus.Failed), Count(ImportStatus.Ignored), Count(ImportStatus.Imported));
    }

    /// <summary>
    /// Imports a file even though it is incomplete or a duplicate (then as another copy of the track), first writing
    /// <paramref name="tags"/> into it when given.
    /// </summary>
    public async Task<ImportItem> AcceptAsync(Guid id, ImportTagsInput? tags, CancellationToken cancellationToken)
    {
        if (tags is not null)
            Validate(tags);
        return await RequestAsync(id, new ImportRequest(tags, Force: true), cancellationToken);
    }

    /// <summary>Looks at the file again, as if it had just been dropped (after fixing its tags, or installing ffmpeg).</summary>
    public Task<ImportItem> RetryAsync(Guid id, CancellationToken cancellationToken) =>
        RequestAsync(id, null, cancellationToken);

    /// <summary>Leaves the file in the drop folder; it is looked at again only if it changes.</summary>
    public async Task<ImportItem> IgnoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindWaitingAsync(id, cancellationToken);
        item.Status = ImportStatus.Ignored;
        item.Request = null;
        item.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(item);
    }

    /// <summary>Moves the file to the drop folder's trash, from which it is deleted after a while.</summary>
    public async Task<ImportItem> DiscardAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await FindWaitingAsync(id, cancellationToken);
        var folder = await db.DropFolders.AsNoTracking().SingleAsync(d => d.Id == item.DropFolderId, cancellationToken);
        var storage = storages.For(LibrarySourceKind.Local);
        var location = new StorageLocation(LibrarySourceKind.Local, folder.Path);
        try
        {
            if (await storage.ExistsAsync(location, item.RelativePath, cancellationToken))
            {
                await storage.TrashAsync(location, item.RelativePath, cancellationToken);
                await storage.RemoveEmptyFoldersAsync(location, AudioFormats.Folder(item.RelativePath), cancellationToken);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
        }

        item.Status = ImportStatus.Discarded;
        item.Request = null;
        item.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(item);
    }

    private async Task<ImportItem> RequestAsync(Guid id, ImportRequest? request, CancellationToken cancellationToken)
    {
        var item = await FindWaitingAsync(id, cancellationToken);
        item.Status = ImportStatus.Pending;
        item.Reason = null;
        item.Request = request is null ? null : JsonSerializer.Serialize(request, Json);
        item.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        monitor.RequestRun();
        return ToModel(item);
    }

    private static void Validate(ImportTagsInput tags)
    {
        var errors = new ValidationErrors();
        foreach (var (field, value) in new[] { ("title", tags.Title), ("artist", tags.Artist), ("album", tags.Album), ("albumArtist", tags.AlbumArtist), ("genre", tags.Genre) })
        {
            if (value is { Length: > 512 })
                errors.Add($"tags.{field}", ErrorCodes.InvalidFormat);
        }

        if (tags.Year is < 1 or > 9999)
            errors.Add("tags.year", ErrorCodes.InvalidFormat);
        if (tags.TrackNumber is < 1 or > 999)
            errors.Add("tags.trackNumber", ErrorCodes.InvalidFormat);
        if (tags.DiscNumber is < 1 or > 99)
            errors.Add("tags.discNumber", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();
    }

    /// <summary>An item the caller can act on: still in its drop folder and not being processed.</summary>
    private async Task<ImportItemEntity> FindWaitingAsync(Guid id, CancellationToken cancellationToken) =>
        await Visible().SingleOrDefaultAsync(i => i.Id == id && i.Status != ImportStatus.Imported && i.Status != ImportStatus.Discarded, cancellationToken)
        ?? throw new ArcanistException.NotFound();

    /// <summary>Items of drop folders importing into libraries the caller manages and can access.</summary>
    private IQueryable<ImportItemEntity> Visible()
    {
        var access = guard.Permission(Permissions.ManageLibraries);
        var libraries = LibraryService.Visible(db, access).Select(l => l.Id);
        return db.ImportItems.Where(i => libraries.Contains(i.DropFolder.LibrarySource.LibraryId));
    }

    private async Task<DropFolder> ToModelAsync(DropFolderEntity folder, CancellationToken cancellationToken)
    {
        var libraryId = await db.LibrarySources.Where(s => s.Id == folder.LibrarySourceId).Select(s => s.LibraryId).SingleAsync(cancellationToken);
        var available = folder.Kind == DropFolderKind.UltimateCloudDrop
            ? capabilities.IsEnabled(Capabilities.Capability.UltimateCloudSource)
            : (await storages.For(LibrarySourceKind.Local).ProbeAsync(new StorageLocation(LibrarySourceKind.Local, folder.Path), cancellationToken)).IsAvailable;
        return new DropFolder(folder.Id, folder.Path, libraryId, folder.LibrarySourceId, folder.Enabled, folder.DuplicatePolicy,
            folder.ConvertTo, folder.ConvertBitRate, folder.ConvertLossy, folder.Mode, folder.Kind, folder.UploadToCloud, folder.CreatedAt, available, folder.UploadAccount);
    }

    private static ImportItem ToModel(ImportItemEntity item) => new(
        item.Id,
        item.DropFolderId,
        item.RelativePath,
        item.Size,
        item.Status,
        item.Reason,
        item.Tags is { } tags ? JsonSerializer.Deserialize<ImportFileDetails>(tags, Json) : null,
        item.DestinationPath,
        item.TrackId,
        item.CreatedAt,
        item.UpdatedAt,
        item.SuggestionsFor is not null && item.SuggestionsFor == item.ContentHash ? item.SuggestionState : null,
        item.SuggestionsFor is not null && item.SuggestionsFor == item.ContentHash && item.Suggestions is { } suggestions
            ? JsonSerializer.Deserialize<List<ImportSuggestion>>(suggestions, Json) ?? []
            : []);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
