using Arcanist.Core.Application;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Backups.Targets;

/// <summary>Opens destinations on other Arcanist servers, through the P2P network.</summary>
internal sealed class PeerBackupTargets
{
    public IBackupTarget Open(BackupDestination destination) => new UnavailableTarget(ErrorCodes.BackupsNotAccepted);
}
