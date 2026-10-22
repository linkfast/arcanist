using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Backups;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.P2P;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Backups;

/// <summary>
/// Other Arcanist servers and backups, both ways: joining one that invited this server to keep its backups there
/// (a destination), and inviting others to keep theirs here (guests).
/// </summary>
internal sealed class BackupPeers(
    ArcanistDbContext db,
    AccessGuard guard,
    P2PService p2p,
    PeerBackupStore store,
    ILibraryStorageResolver storages,
    ArcanistPaths paths,
    TimeProvider time,
    IServiceProvider services)
{
    /// <summary>Joins the server that gave <paramref name="invitation"/>: it becomes a destination.</summary>
    public async Task<BackupDestination> JoinAsync(string name, string? invitation, CancellationToken cancellationToken)
    {
        var data = P2PLinks.Parse(invitation);
        if (data is not { Kind: P2PPeerKind.Server, Backup: true, Migrate: false })
            throw ArcanistException.Field("invitation", ErrorCodes.InvitationInvalid);
        var config = await services.GetRequiredService<P2PSettings>().GetAsync(cancellationToken);
        if (!config.Enabled || !services.GetRequiredService<P2PNode>().IsRunning)
            throw new ArcanistException.Conflict(ErrorCodes.P2PDisabled);

        PeerShareInfo info;
        try
        {
            info = await services.GetRequiredService<PeerClient>().PostAsync<PeerShareInfo>(data.Ticket, P2PGateMiddleware.JoinPath,
                new JoinShareRequest(data.Token, config.DisplayName), cancellationToken);
        }
        catch (ArcanistException.Forbidden)
        {
            throw ArcanistException.Field("invitation", ErrorCodes.InvitationInvalid);
        }

        return new BackupDestination
        {
            Name = name.Length > 0 ? name : info.Name,
            Kind = BackupDestinationKind.Peer,
            Location = data.Ticket.Id,
            Settings = JsonSerializer.Serialize(new PeerDestinationSettings(data.Ticket), JsonSerializerOptions.Web),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
    }

    /// <summary>An invitation for another server to keep its backups in a folder of this one.</summary>
    public async Task<P2PInvitationLink> InviteAsync(CreateBackupInvitationInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var errors = new ValidationErrors();
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0)
            errors.Add("name", ErrorCodes.Required);
        if (input.QuotaGb is <= 0 or > 1_000_000)
            errors.Add("quotaGb", ErrorCodes.InvalidFormat);
        errors.ThrowIfAny();
        var folder = await FolderAsync(input.Folder, cancellationToken);
        return await p2p.CreateBackupInvitationAsync(name, folder, input.QuotaGb is { } gb ? (long)(gb * 1024 * 1024 * 1024) : null, cancellationToken);
    }

    /// <summary>The servers that keep their backups here.</summary>
    public async Task<IReadOnlyList<BackupGuest>> GuestsAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var peers = await db.P2PPeers.AsNoTracking().Where(p => p.BackupFolder != null).OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var guests = new List<BackupGuest>();
        foreach (var peer in peers)
        {
            long? used = null;
            try
            {
                used = await store.UsedAsync(peer.Id, new FolderTarget(peer.BackupFolder!), cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A disconnected drive: the space is unknown.
            }

            guests.Add(new BackupGuest(peer.Id, peer.Name, peer.BackupFolder!, used, peer.BackupQuotaBytes, peer.RevokedAt is not null, peer.LastSeenAt));
        }

        return guests;
    }

    /// <summary>A folder of this server for backups: it exists, and it is neither inside a library nor inside the data folder.</summary>
    public async Task<string> FolderAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw ArcanistException.Field("folder", ErrorCodes.InvalidFormat);
        var local = storages.For(LibrarySourceKind.Local);
        var location = local.NormalizeLocation(path);
        if (!Directory.Exists(location))
            throw ArcanistException.Field("folder", ErrorCodes.NotFound);
        var libraries = await db.LibrarySources.AsNoTracking().Where(s => s.Kind == LibrarySourceKind.Local).Select(s => s.Location).ToListAsync(cancellationToken);
        if (libraries.Any(l => local.Contains(l, location)) || local.Contains(paths.Root, location))
            throw ArcanistException.Field("folder", ErrorCodes.BackupDestinationOverlaps);
        return location;
    }
}
