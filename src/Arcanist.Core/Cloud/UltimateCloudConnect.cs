using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.P2P;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Cloud;

/// <summary>
/// The transparent P2P connection, with any linked account (<see cref="Capability.CloudConnect"/>): this server registers its
/// node in Ultimate Cloud, so the account's apps find it without pairing; the account's devices are trusted here as
/// they register theirs (<see cref="P2PPeerSource.UltimateCloud"/>), and dropped when they no longer are. The nodes
/// reach each other through the relays chosen in the P2P settings. Run after every plan check. Every account takes
/// part: the server's (its devices belong to the administrator who linked it) and each user's own (theirs).
/// </summary>
public sealed class UltimateCloudConnect(
    UltimateCloudClient client,
    UltimateCloudAuth auth,
    UserCloudAccounts accounts,
    P2PSettings p2p,
    P2PNode node,
    P2PPeerDirectory directory,
    IArcanistDbContextFactory databases,
    TimeProvider time,
    ILogger<UltimateCloudConnect> logger)
{
    private readonly SemaphoreSlim _run = new(1, 1);

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _run.WaitAsync(cancellationToken);
        try
        {
            await SyncAllAsync(cancellationToken);
        }
        finally
        {
            _run.Release();
        }
    }

    private async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        var config = await p2p.GetAsync(cancellationToken);
        var devices = new List<(string Id, string Name, Guid? Owner)>();
        if (config.Enabled)
        {
            await using var db = databases.CreateDbContext();
            foreach (var account in new Guid?[] { null }.Concat(accounts.Linked.Select(u => (Guid?)u)))
            {
                if (accounts.TierOf(account) == CloudTier.None)
                    continue;
                var owner = account ?? await OwnerAsync(db, cancellationToken);
                try
                {
                    using (UltimateCloudClient.As(account))
                    {
                        foreach (var (id, name) in await AccountDevicesAsync(config, cancellationToken))
                            devices.Add((id, name, owner));
                    }
                }
                catch (UltimateCloudException e) when (account is not null)
                {
                    // A user's account that cannot be reached keeps its devices until it can.
                    logger.LogDebug("The P2P nodes of user {User} could not be read: {Error}", account, e.Code);
                    devices.AddRange((await db.P2PPeers.AsNoTracking()
                            .Where(p => p.Source == P2PPeerSource.UltimateCloud && p.UserId == account)
                            .Select(p => new { p.NodeId, p.Name })
                            .ToListAsync(cancellationToken))
                        .Select(p => (p.NodeId, p.Name, account)));
                }
            }
        }

        await TrustAsync(devices.DistinctBy(d => d.Id).ToList(), cancellationToken);
    }

    /// <summary>Registers this server's node in the account (of this flow), and returns the account's devices.</summary>
    private async Task<List<(string Id, string Name)>> AccountDevicesAsync(P2PConfig config, CancellationToken cancellationToken)
    {
        var data = await client.SendAsync("query Connect { myP2PNodes { nodeId kind name } }", null, cancellationToken);

        var state = node.State;
        if (state is { Running: true, NodeId: { } nodeId, Ticket: { } ticket })
        {
            await client.SendAsync(
                "mutation Register($n: String!, $name: String!, $t: String) { registerP2PNode(nodeId: $n, kind: SERVER, name: $name, ticket: $t) { nodeId } }",
                new { n = nodeId, name = config.DisplayName, t = Application.P2P.P2PService.Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticket, NativeP2P.Json))) },
                cancellationToken);
        }

        return (data["myP2PNodes"]?.AsArray() ?? [])
            .OfType<JsonNode>()
            .Where(n => n["kind"]?.GetValue<string>() == "DEVICE" && n["nodeId"]?.GetValue<string>() is { Length: 64 } id && id != state.NodeId)
            .Select(n => (Id: n["nodeId"]!.GetValue<string>().ToLowerInvariant(), Name: n["name"]?.GetValue<string>() ?? "Ultimate"))
            .DistinctBy(n => n.Id)
            .ToList();
    }

    /// <summary>Makes the accounts' devices the peers that came from Ultimate Cloud: adds the new, drops the gone.</summary>
    private async Task TrustAsync(IReadOnlyList<(string Id, string Name, Guid? Owner)> devices, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var current = await db.P2PPeers.Where(p => p.Source == P2PPeerSource.UltimateCloud).ToListAsync(cancellationToken);
        var wanted = devices.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
        var owners = devices.ToDictionary(d => d.Id, d => d.Owner, StringComparer.Ordinal);
        var changed = false;

        foreach (var peer in current.Where(p => !wanted.ContainsKey(p.NodeId)))
        {
            db.P2PPeers.Remove(peer);
            changed = true;
        }

        var newIds = wanted.Keys.Except(current.Select(p => p.NodeId)).ToList();
        if (newIds.Count > 0)
        {
            // Nodes paired by hand stay as they are.
            var paired = await db.P2PPeers.Where(p => newIds.Contains(p.NodeId)).Select(p => p.NodeId).ToListAsync(cancellationToken);
            foreach (var id in newIds.Except(paired))
            {
                db.P2PPeers.Add(new P2PPeer
                {
                    Kind = P2PPeerKind.Device,
                    NodeId = id,
                    Name = Cut(wanted[id]),
                    UserId = owners[id],
                    Source = P2PPeerSource.UltimateCloud,
                    Rules = new P2PRules { AllowDownload = true },
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                });
                changed = true;
            }
        }

        // A device keeps the owner it came with (one in several accounts belongs to the first).
        foreach (var peer in current.Where(p => wanted.TryGetValue(p.NodeId, out var name) && p.Name != Cut(name)))
        {
            peer.Name = Cut(wanted[peer.NodeId]);
            changed = true;
        }

        if (!changed)
            return;
        await db.SaveChangesAsync(cancellationToken);
        await directory.InvalidateAsync(cancellationToken);
        logger.LogInformation("Devices of the Ultimate Cloud account trusted through the P2P network: {Count}", devices.Count);
    }

    /// <summary>The devices belong to the administrator who linked the account (or the first administrator).</summary>
    private async Task<Guid?> OwnerAsync(ArcanistDbContext db, CancellationToken cancellationToken)
    {
        if ((await auth.LinkAsync(cancellationToken))?.LinkedByUserId is { } linkedBy && await db.Users.AnyAsync(u => u.Id == linkedBy, cancellationToken))
            return linkedBy;
        return await db.Users.Where(u => u.Role == UserRole.Admin).OrderBy(u => u.CreatedAt).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static string Cut(string name) => name.Length > 120 ? name[..120] : name;
}
