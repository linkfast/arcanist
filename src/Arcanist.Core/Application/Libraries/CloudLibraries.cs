using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.UltimateCloud;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Libraries;

/// <summary>
/// Libraries whose music lives in Ultimate Cloud or in S3 depend on Ultimate Cloud: while its extension is off they are
/// disabled, hidden from listening and only deleting them works (<see cref="ErrorCodes.LibraryDisabled"/>). S3 libraries
/// also need the Cloud plan's storage (<see cref="Capability.ThirdPartyStorage"/>, which Ultimate Cloud Plus provides
/// while an account's Cloud plan is up to date): without it they are disabled too. Libraries of a managed extension
/// (<see cref="Data.Entities.Library.ManagedBy"/>) depend on neither.
/// </summary>
public sealed class CloudLibraries(ExtensionHost extensions, ICapabilityService capabilities)
{
    public bool Enabled => extensions.IsEnabled(UltimateCloudExtension.Id);

    public static bool DependsOnCloud(LibrarySourceKind kind) => kind is LibrarySourceKind.UltimateCloud or LibrarySourceKind.S3;

    public bool IsDisabled(IEnumerable<LibrarySourceKind> kinds, bool managed = false)
    {
        if (managed)
            return false;
        var list = kinds as IReadOnlyCollection<LibrarySourceKind> ?? kinds.ToList();
        return (!Enabled && list.Any(DependsOnCloud)) || (list.Contains(LibrarySourceKind.S3) && !capabilities.IsEnabled(Capability.ThirdPartyStorage));
    }

    /// <summary>Refuses to work on a library that is disabled now.</summary>
    public async Task EnsureEnabledAsync(ArcanistDbContext db, Guid libraryId, CancellationToken cancellationToken)
    {
        var kinds = await db.LibrarySources.Where(s => s.LibraryId == libraryId && s.Library.ManagedBy == null).Select(s => s.Kind).ToListAsync(cancellationToken);
        if (IsDisabled(kinds))
            throw new ArcanistException.Conflict(ErrorCodes.LibraryDisabled);
    }
}
