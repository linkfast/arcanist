using Arcanist.Core.Application;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Backups;

/// <summary>Other Arcanist servers that keep this server's backups, and this server keeping theirs.</summary>
internal sealed class BackupPeers
{
    /// <summary>Joins another server with the invitation it gave, as a destination.</summary>
    public Task<BackupDestination> JoinAsync(string name, string? invitation, CancellationToken cancellationToken) =>
        throw ArcanistException.Field("invitation", ErrorCodes.InvitationInvalid);
}
