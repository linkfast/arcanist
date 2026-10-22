using System.Collections.Concurrent;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Libraries;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum UploadStatus
{
    /// <summary>The file entered the library; it is in the catalog after the next scan (soon).</summary>
    Imported,

    /// <summary>The library already had this audio; nothing was written.</summary>
    Duplicate,
}

/// <param name="ContentHash">Arcanist's hash of the audio: matchTracks finds its track by it once catalogued.</param>
/// <param name="Path">Where it went, relative to the library's folder.</param>
/// <param name="TrackId">The track that already has it (duplicates of catalogued files).</param>
public sealed record UploadResult(UploadStatus Status, string ContentHash, string? Path, Guid? TrackId);

/// <summary>Bound from <c>Arcanist:Uploads</c>.</summary>
public sealed class UploadOptions
{
    /// <summary>Largest file accepted (2 GB).</summary>
    public long MaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>A library is scanned this long after its last upload, so a batch of uploads makes one scan.</summary>
    public TimeSpan ScanDelay { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>The kind of client an upload came from, as it says with <c>client=</c>. Part of the API contract.</summary>
public enum UploadClient
{
    /// <summary>Arcanist's admin panel (Import → Upload music).</summary>
    Panel,

    /// <summary>Ultimate Desktop.</summary>
    Desktop,

    /// <summary>A web player (Ultimate Web Player, Webamp for Arcanist…).</summary>
    Web,

    /// <summary>A phone or tablet app.</summary>
    Mobile,
}

/// <summary>
/// Files sent by apps into a library (migrating Ultimate Desktop's library, for one): placed with the library's
/// template like a drop folder's, never overwriting, and skipped when the library already has the audio. Needs the
/// UPLOAD permission and a library the user can access with a folder Arcanist can write to. Each upload goes into
/// the user's history (<see cref="UserUpload"/>), failed ones too, where it shows once it is in the catalog.
/// </summary>
public sealed class UploadService(
    ArcanistDbContext db,
    AccessGuard guard,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    UploadScans scans,
    ArcanistPaths paths,
    TimeProvider time,
    ILogger<UploadService> logger)
{
    /// <param name="client">The kind of client sending it (the admin panel, Ultimate Desktop, a web player, a phone), for the log of uploads.</param>
    public Task<UploadResult> UploadAsync(Guid libraryId, string fileName, Stream body, UploadClient? client, CancellationToken cancellationToken) =>
        UploadAsync(guard.Permission(Permissions.Upload), libraryId, fileName, body, null, client, cancellationToken);

    /// <summary>
    /// A file into a library on behalf of a user, as if they had uploaded it (for extensions, also from background
    /// work: get the user with <c>UserAccessCache.GetUserAsync</c>). It needs the UPLOAD permission and access to the library, goes
    /// where the library's template says without overwriting anything (not into a library where the user only listens:
    /// <c>library_read_only</c>), and shows in the user's history of uploads
    /// with <paramref name="source"/> (the extension's id).
    /// </summary>
    public Task<UploadResult> UploadAsync(UserAccess access, Guid libraryId, string fileName, Stream body, string? source,
        CancellationToken cancellationToken) =>
        UploadAsync(access, libraryId, fileName, body, source, null, cancellationToken);

    private async Task<UploadResult> UploadAsync(UserAccess access, Guid libraryId, string fileName, Stream body, string? source,
        UploadClient? client, CancellationToken cancellationToken)
    {
        if (access.IsDisabled || !access.Has(Permissions.Upload))
            throw new ArcanistException.Forbidden();
        if (VirtualLibraries.IsVirtual(libraryId))
            throw new ArcanistException.Conflict(ErrorCodes.LibraryVirtual);
        if (!access.CanAccessLibrary(libraryId))
            throw new ArcanistException.NotFound();
        if (!access.CanUploadTo(libraryId))
            throw new ArcanistException.Forbidden(ErrorCodes.LibraryReadOnly);
        if (source is { Length: > 64 })
            throw ArcanistException.Field("source", ErrorCodes.InvalidFormat);

        long? size = null;
        try
        {
            var result = await ImportAsync(access, libraryId, fileName, body, length => size = length, cancellationToken);
            await RecordAsync(access.UserId, libraryId, fileName, size, result, null, source, client, cancellationToken);
            return result;
        }
        catch (ArcanistException e) when (e is not ArcanistException.NotFound && !cancellationToken.IsCancellationRequested)
        {
            var code = e is ArcanistException.Invalid invalid ? invalid.Errors.ToDictionary().Values.SelectMany(c => c).FirstOrDefault() ?? e.Code : e.Code;
            await RecordAsync(access.UserId, libraryId, fileName, size, null, code, source, client, cancellationToken);
            throw;
        }
    }

    private async Task RecordAsync(Guid user, Guid libraryId, string fileName, long? size, UploadResult? result, string? error, string? source,
        UploadClient? client, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        db.ChangeTracker.Clear();
        db.UserUploads.Add(new UserUpload
        {
            UserId = user,
            Kind = UserUploadKind.Library,
            Status = result is null ? UserUploadStatus.Failed : result.Status == UploadStatus.Duplicate ? UserUploadStatus.Duplicate : UserUploadStatus.Processing,
            Name = Application.Uploads.MyUploadService.Cut(Path.GetFileName(fileName ?? "")),
            Size = size,
            LibraryId = libraryId,
            TrackId = result?.TrackId,
            ContentHash = result?.ContentHash,
            Path = result?.Path,
            ErrorCode = error,
            Source = source,
            Client = client,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<UploadResult> ImportAsync(
        UserAccess access, Guid libraryId, string fileName, Stream body, Action<long> measured, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fileName) || !AudioFormats.IsAudio(fileName))
            throw ArcanistException.Field("fileName", ErrorCodes.InvalidFormat);

        var library = await db.Libraries.AsNoTracking().Include(l => l.Sources).SingleOrDefaultAsync(l => l.Id == libraryId, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        var source = library.Sources.Where(s => s.Kind is LibrarySourceKind.Local or LibrarySourceKind.S3).OrderBy(s => s.Id).FirstOrDefault()
                     ?? throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);
        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        if (!await storage.CanWriteAsync(location, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);

        var folder = Path.Combine(paths.Cache, "uploads");
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $"upload-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var file = File.Create(temporary))
            {
                await body.CopyToAsync(file, cancellationToken);
                measured(file.Length);
            }

            AudioMetadata metadata;
            string hash;
            try
            {
                await using var stream = File.OpenRead(temporary);
                metadata = TagReader.Read(stream, AudioFormats.MimeType(temporary));
                hash = await ContentHasher.HashAsync(stream, metadata.AudioDataOffset, metadata.AudioDataSize, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogDebug(e, "Could not read the uploaded {File}", fileName);
                throw ArcanistException.Field("file", ErrorCodes.Unreadable);
            }

            var template = PathTemplate.TryParse(library.OrganizeTemplate, out var parsed, out _) ? parsed : PathTemplate.DefaultTemplate;
            var values = new PathValues(metadata.AlbumArtist, metadata.Artists.Count > 0 ? string.Join(", ", metadata.Artists) : null, metadata.Album,
                metadata.Year, metadata.DiscNumber, metadata.DiscNumber > 1, metadata.TrackNumber,
                metadata.Title ?? AudioFormats.WithoutExtension(Path.GetFileName(fileName)), metadata.Genres.FirstOrDefault());
            var wanted = template.Render(values);

            using (await locks.AcquireForWritingAsync(libraryId, cancellationToken))
            {
                var existing = await db.MediaFiles.AsNoTracking()
                    .Where(f => f.ContentHash == hash && f.MissingSince == null && f.LibrarySource.LibraryId == libraryId)
                    .Select(f => new { f.TrackId, f.RelativePath })
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is not null)
                    return new UploadResult(UploadStatus.Duplicate, hash, existing.RelativePath, existing.TrackId);
                if (scans.IsPending(libraryId, hash))
                    return new UploadResult(UploadStatus.Duplicate, hash, null, null);

                string destination;
                for (var copy = 1; ; copy++)
                {
                    destination = copy == 1 ? wanted + extension : $"{wanted} ({copy}){extension}";
                    if (!await storage.ExistsAsync(location, destination, cancellationToken))
                        break;
                }

                try
                {
                    await storage.ImportAsync(location, destination, temporary, cancellationToken);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(e, "Could not write the uploaded {File} into {Library}", fileName, library.Name);
                    throw new ArcanistException.Conflict(ErrorCodes.WriteFailed);
                }

                scans.Uploaded(libraryId, hash, access.UserId);
                logger.LogInformation("{User} uploaded {Path} to {Library}", access.UserName, destination, library.Name);
                return new UploadResult(UploadStatus.Imported, hash, destination, null);
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

/// <summary>
/// Scans a library a little after its last upload, so a batch of uploads makes one scan; until then it remembers
/// what was uploaded, so the same audio sent twice in a batch is a duplicate too.
/// </summary>
public sealed class UploadScans(IServiceScopeFactory scopes, IOptionsMonitor<UploadOptions> options, ILogger<UploadScans> logger)
{
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    private sealed class Pending
    {
        public CancellationTokenSource Delay { get; set; } = new();
        public HashSet<string> Hashes { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The audio was uploaded to some library whose scan has not been queued yet.</summary>
    public bool IsPendingAnywhere(string hash) => _pending.Keys.Any(library => IsPending(library, hash));

    public bool IsPending(Guid libraryId, string hash)
    {
        if (!_pending.TryGetValue(libraryId, out var pending))
            return false;
        lock (pending)
            return pending.Hashes.Contains(hash);
    }

    public void Uploaded(Guid libraryId, string hash, Guid userId)
    {
        var pending = _pending.GetOrAdd(libraryId, _ => new Pending());
        CancellationTokenSource delay;
        lock (pending)
        {
            pending.Hashes.Add(hash);
            pending.Delay.Cancel();
            pending.Delay = delay = new CancellationTokenSource();
        }

        _ = ScanLaterAsync(libraryId, pending, delay, userId);
    }

    private async Task ScanLaterAsync(Guid libraryId, Pending pending, CancellationTokenSource delay, Guid userId)
    {
        try
        {
            await Task.Delay(options.CurrentValue.ScanDelay, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return; // a newer upload scans later
        }

        try
        {
            using var scope = scopes.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<DatabaseMaintenance>();
            using (await maintenance.WorkAsync(CancellationToken.None))
                await scope.ServiceProvider.GetRequiredService<ScanCoordinator>().EnqueueAsync(libraryId, ScanKind.Incremental, userId, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not scan library {Library} after its uploads", libraryId);
        }
        finally
        {
            lock (pending)
            {
                if (pending.Delay == delay)
                    _pending.TryRemove(libraryId, out _);
            }
        }
    }
}
