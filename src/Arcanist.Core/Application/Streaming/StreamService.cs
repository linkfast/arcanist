using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Streaming;

/// <summary>A file ready to be sent to a player.</summary>
public sealed record StreamTarget(
    StorageLocation Location,
    string RelativePath,
    string ContentType,
    string FileName,
    long Size,
    DateTime ModifiedAt,
    string ContentHash,
    int? BitRate,
    int DurationMs);

public sealed class StreamService(AccessGuard guard, CatalogService catalog)
{
    /// <summary>
    /// Picks the file to play for a track: the requested one, or the best quality copy the caller can access.
    /// Downloading the original requires the DOWNLOAD permission.
    /// </summary>
    public async Task<StreamTarget> ResolveAsync(Guid trackId, Guid? fileId, bool download, CancellationToken cancellationToken)
    {
        var access = download ? guard.Permission(Permissions.Download) : guard.Authenticated();

        var files = catalog.VisibleFiles(access).Where(f => f.TrackId == trackId);
        if (fileId is { } id)
            files = files.Where(f => f.Id == id);

        var file = await files.AsNoTracking()
            .Include(f => f.LibrarySource)
            .OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.SampleRate ?? 0).ThenByDescending(f => f.BitRate ?? 0)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArcanistException.NotFound();

        return new StreamTarget(
            StorageLocation.From(file.LibrarySource),
            file.RelativePath,
            AudioFormats.MimeType(file.RelativePath),
            file.RelativePath[(file.RelativePath.LastIndexOf('/') + 1)..],
            file.Size,
            file.ModifiedAt,
            file.ContentHash,
            file.BitRate,
            file.DurationMs);
    }
}
