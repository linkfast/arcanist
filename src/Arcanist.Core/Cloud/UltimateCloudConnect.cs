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
/// The transparent P2P connection of the Cloud plan (<see cref="Capability.CloudConnect"/>): this server registers its
/// node in Ultimate Cloud, so the account's apps find it without pairing; the account's devices are trusted here as
/// they register theirs (<see cref="P2PPeerSource.UltimateCloud"/>), and dropped when they no longer are; and the
/// node may use Ultimate's relay (<see cref="P2PRelay.Ultimate"/>). Run after every plan check.
/// </summary>
public sealed class UltimateCloudConnect(
    UltimateCloudClient client,
    UltimateCloudAuth auth,
    ICloudLinkState link,
    P2PSettings p2p,
    P2PNode node,
    P2PUltimateRelay relay,
    P2PPeerDirectory directory,
    IArcanistDbContextFactory databases,
    TimeProvider time,
    ILogger<UltimateCloudConnect> logger)
{
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var config = await p2p.GetAsync(cancellationToken);
        if (link.Tier != CloudTier.Paid || !config.Enabled)
        {
            SetRelay(null, config);
            await TrustAsync([], cancellationToken);
            return;
        }

        var data = await client.SendAsync("query Connect { p2pRelay { url } myP2PNodes { nodeId kind name } }", null, cancellationToken);
        SetRelay(data["p2pRelay"]?["url"]?.GetValue<string>(), config);

        var state = node.State;
        if (state is { Running: true, NodeId: { } nodeId, Ticket: { } ticket })
        {
            await client.SendAsync(
                "mutation Register($n: String!, $name: String!, $t: String) { registerP2PNode(nodeId: $n, kind: SERVER, name: $name, ticket: $t) { nodeId } }",
                new { n = nodeId, name = config.DisplayName, t = Application.P2P.P2PService.Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticket, NativeP2P.Json))) },
                cancellationToken);
        }

        var devices = (data["myP2PNodes"]?.AsArray() ?? [])
            .OfType<JsonNode>()
            .Where(n => n["kind"]?.GetValue<string>() == "DEVICE" && n["nodeId"]?.GetValue<string>() is { Length: 64 } id && id != state.NodeId)
            .Select(n => (Id: n["nodeId"]!.GetValue<string>().ToLowerInvariant(), Name: n["name"]?.GetValue<string>() ?? "Ultimate"))
            .DistinctBy(n => n.Id)
            .ToList();
        await TrustAsync(devices, cancellationToken);
    }

    private void SetRelay(string? url, P2PConfig config)
    {
        if (relay.Url == url)
            return;
        relay.Url = url;
        // A node on Ultimate's relay restarts on it (or back on n0's when the plan ends).
        if (config.Relay == P2PRelay.Ultimate)
            node.Changed();
    }

    /// <summary>Makes the account's devices the peers that came from Ultimate Cloud: adds the new, drops the gone.</summary>
    private async Task TrustAsync(IReadOnlyList<(string Id, string Name)> devices, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var current = await db.P2PPeers.Where(p => p.Source == P2PPeerSource.UltimateCloud).ToListAsync(cancellationToken);
        var wanted = devices.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
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
            var owner = await OwnerAsync(db, cancellationToken);
            foreach (var id in newIds.Except(paired))
            {
                db.P2PPeers.Add(new P2PPeer
                {
                    Kind = P2PPeerKind.Device,
                    NodeId = id,
                    Name = Cut(wanted[id]),
                    UserId = owner,
                    Source = P2PPeerSource.UltimateCloud,
                    Rules = new P2PRules { AllowDownload = true },
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                });
                changed = true;
            }
        }

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
