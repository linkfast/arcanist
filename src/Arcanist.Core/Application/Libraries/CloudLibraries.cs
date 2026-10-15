using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.UltimateCloud;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Libraries;

/// <summary>
/// Libraries whose music lives in Ultimate Cloud or in S3 (a feature of Ultimate Cloud's plan) depend on the Ultimate
/// Cloud extension: while it is off they are disabled, hidden from listening and only deleting them works
/// (<see cref="ErrorCodes.LibraryDisabled"/>).
/// </summary>
public sealed class CloudLibraries(ExtensionHost extensions)
{
    public bool Enabled => extensions.IsEnabled(UltimateCloudExtension.Id);

    public static bool DependsOnCloud(LibrarySourceKind kind) => kind is LibrarySourceKind.UltimateCloud or LibrarySourceKind.S3;

    public bool IsDisabled(IEnumerable<LibrarySourceKind> kinds) => !Enabled && kinds.Any(DependsOnCloud);

    /// <summary>Refuses to work on a library that is disabled now.</summary>
    public async Task EnsureEnabledAsync(ArcanistDbContext db, Guid libraryId, CancellationToken cancellationToken)
    {
        if (!Enabled && await db.LibrarySources.AnyAsync(s => s.LibraryId == libraryId
                                                              && (s.Kind == LibrarySourceKind.UltimateCloud || s.Kind == LibrarySourceKind.S3), cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.LibraryDisabled);
    }
}
