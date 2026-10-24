using System.Text.Json;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Backups;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.ServerMigration;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>One file a user may copy to their own disk: its original, with its tags, through its download URL.</summary>
/// <param name="Path">Where it is, relative to its library's folder: keep it to rebuild the same layout.</param>
/// <param name="ContentHash">Arcanist's hash of the audio (tags excluded): skip what you already copied.</param>
/// <param name="DownloadUrl">The original as an attachment (<c>download=true</c>), with HTTP ranges so a copy resumes.
/// Relative to the server; it carries a token valid a few hours for the session (or send the Bearer token instead).</param>
/// <param name="CueTrack">0 for a whole file. For an album in one file with a cue sheet, every track of it is listed with
/// the same path: copy the file once.</param>
public sealed record ExportableFile(
    Guid TrackId, Guid FileId, Guid LibraryId, string Path, long Size, string ContentHash, string? Format, DateTime ModifiedAt, int CueTrack,
    string DownloadUrl);

/// <param name="NextCursor">Pass as <c>after</c> for the next page; null on the last.</param>
public sealed record ExportableFilePage(IReadOnlyList<ExportableFile> Items, string? NextCursor, int TotalCount);

/// <summary>
/// A user's own copy of what they can reach (a "local backup", without the backup administrator's rights): the original
/// files of the libraries they can play, page by page with sizes and hashes, and their own data (ratings, StarTracks,
/// play counts, plays and playlists) in the backups' format (docs/backups.md), which a restore or a migration applies.
/// Files need the DOWNLOAD permission (a copy, like any download); their data only a sign-in. Nothing here counts as a play.
/// </summary>
public sealed class PersonalExportService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, Auth.StreamTokens streamTokens)
{
    public const int DefaultPageSize = 500;
    public const int MaxPageSize = 2000;

    /// <param name="libraryId">Only this library's files (all of them otherwise).</param>
    /// <param name="after">The <c>nextCursor</c> of the previous page.</param>
    public async Task<ExportableFilePage> FilesAsync(Guid? libraryId, string? after, int? first, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.Download);
        var visible = catalog.Visible(access, libraryId);
        var files = catalog.VisibleFiles(access).Where(f => visible.Any(t => t.Id == f.TrackId));
        if (Libraries.VirtualLibraries.Filter(libraryId) is { } only)
            files = files.Where(f => f.LibrarySource.LibraryId == only);

        var total = await files.CountAsync(cancellationToken);
        if (after is not null)
        {
            if (!Guid.TryParse(after, out var last))
                throw ArcanistException.Field("after", ErrorCodes.InvalidFormat);
            files = files.Where(f => f.Id.CompareTo(last) > 0);
        }

        var size = Math.Clamp(first ?? DefaultPageSize, 1, MaxPageSize);
        var page = await files.AsNoTracking().OrderBy(f => f.Id).Take(size + 1)
            .Select(f => new { f.Id, f.TrackId, f.LibrarySource.LibraryId, f.RelativePath, f.Size, f.ContentHash, f.Format, f.ModifiedAt, f.CueTrack })
            .ToListAsync(cancellationToken);
        var token = streamTokens.Create(access.UserId, guard.SessionId);
        var items = page.Take(size).Select(f => new ExportableFile(f.TrackId, f.Id, f.LibraryId, f.RelativePath, f.Size, f.ContentHash, f.Format,
            f.ModifiedAt, f.CueTrack, StreamUrls.For(f.TrackId, token, f.Id) + "&download=true")).ToList();
        return new ExportableFilePage(items, page.Count > size ? items[^1].FileId.ToString() : null, total);
    }

    /// <summary>The user's own data as a JSON document in the backups' format: <c>{ catalog, userData }</c>.</summary>
    public async Task<string> DataAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var data = await Core.ServerMigration.MigrationData.UserDataAsync(db, new HashSet<Guid> { access.UserId }, cancellationToken);
        var tracks = data.Users.SelectMany(u => u.Ratings.Select(r => r.Track)
                .Concat(u.PlayCounts.Select(c => c.Track))
                .Concat(u.Plays.Select(p => p.Track))
                .Concat(u.Playlists.SelectMany(p => p.Entries).Select(e => e.Track)))
            .ToHashSet();
        // Only what the user can see is described: a collaborator's entry from a library the user cannot reach (a
        // personal one, say) stays a bare id, as playlists show it.
        var visible = (await catalog.Visible(access).Where(t => tracks.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var catalogData = await Core.ServerMigration.MigrationData.CatalogAsync(db, visible, cancellationToken);
        return JsonSerializer.Serialize(new { catalog = catalogData, userData = data }, BackupFormat.Json);
    }
}
