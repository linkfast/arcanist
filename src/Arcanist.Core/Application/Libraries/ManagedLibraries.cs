using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Arcanist.Core.Storage.S3;
using Microsoft.EntityFrameworkCore;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using LibrarySourceEntity = Arcanist.Core.Data.Entities.LibrarySource;

namespace Arcanist.Core.Application.Libraries;

/// <summary>
/// Libraries a managed extension makes and keeps (<c>Library.ManagedBy</c>), for a hosted service whose music lives
/// in its own storage: administrators use them as any other library, but cannot delete them or change their sources
/// (<c>library_managed</c>), and they do not depend on Ultimate Cloud. For extensions, from their own code (it checks
/// nobody: the extension decides when to call it).
/// </summary>
public sealed class ManagedLibraries(
    IArcanistDbContextFactory databases,
    ILibraryStorageResolver storages,
    ScanCoordinator scans,
    UserAccessCache accessCache,
    TimeProvider time)
{
    /// <summary>
    /// Makes sure the extension's library <paramref name="key"/> exists in an S3 bucket, encrypted with
    /// <paramref name="encryptionKey"/> when given (see docs/storage-encryption.md): creates it the first time, and
    /// otherwise updates its name, credentials and key. The first time, and when its place changes, it is scanned.
    /// </summary>
    /// <returns>The library's id, which never changes.</returns>
    public async Task<Guid> EnsureS3Async(string extensionId, string key, string name, S3ConnectionInput connection, string? encryptionKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(extensionId) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The extension, the key and the name are required.");
        if (encryptionKey is not null && S3Encryption.ParseKey(encryptionKey) is null)
            throw new ArgumentException("The encryption key must be 256 bits in base64.", nameof(encryptionKey));
        var errors = new ValidationErrors();
        var (s3, prefix) = S3LibraryService.Validate(connection, errors);
        errors.ThrowIfAny();

        var storage = (S3Storage)storages.For(LibrarySourceKind.S3);
        var location = storage.NormalizeLocation($"{S3Storage.Scheme}{s3.Bucket}/{prefix}");
        var settings = S3LibraryService.SettingsJson(storage, s3, encryptionKey, managed: true);

        await using var db = databases.CreateDbContext();
        var library = await db.Libraries.Include(l => l.Sources)
            .SingleOrDefaultAsync(l => l.ManagedBy == extensionId && l.ManagedKey == key, cancellationToken);
        var scan = false;
        if (library is null)
        {
            library = new LibraryEntity
            {
                Number = await LibraryService.NextNumberAsync(db, cancellationToken),
                Name = name.Trim(),
                VisibleToAll = true,
                ScanIntervalMinutes = 24 * 60,
                ManagedBy = extensionId,
                ManagedKey = key,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            };
            library.Sources.Add(new LibrarySourceEntity { Library = library, Kind = LibrarySourceKind.S3, Location = location, Settings = settings });
            db.Libraries.Add(library);
            scan = true;
        }
        else
        {
            library.Name = name.Trim();
            var source = library.Sources.SingleOrDefault(s => s.Kind == LibrarySourceKind.S3);
            if (source is null)
            {
                library.Sources.Add(new LibrarySourceEntity { Library = library, Kind = LibrarySourceKind.S3, Location = location, Settings = settings });
                scan = true;
            }
            else
            {
                scan = source.Location != location;
                source.Location = location;
                // Credentials rotate: always the newest. (Protecting again gives other bytes for the same secret.)
                source.Settings = settings;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        if (scan)
            await scans.EnqueueAsync(library.Id, ScanKind.Full, requestedBy: null, cancellationToken);
        return library.Id;
    }
}
