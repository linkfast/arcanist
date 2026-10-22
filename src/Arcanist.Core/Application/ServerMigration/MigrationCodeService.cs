using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Application.P2P;
using Arcanist.Core.P2P;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.ServerMigration;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A new migration code: shown once. Another Arcanist uses it with one of the addresses, or the P2P link.</summary>
/// <param name="Addresses">This server's addresses on its local networks.</param>
/// <param name="P2PLink">With the P2P network on: a link with the node and the code, to migrate without opening ports.</param>
public sealed record MigrationCode(Guid Id, string Code, DateTime ExpiresAt, IReadOnlyList<string> Addresses, string? P2PLink);

/// <param name="LastUsedBy">The name of the server that used it last.</param>
public sealed record MigrationCodeInfo(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt, DateTime? LastUsedAt, string? LastUsedBy, bool Active);

/// <summary>
/// Migration codes: an administrator makes one so another Arcanist (a new server) can bring this one over through the
/// export API: its users, libraries and music, their ratings, plays and playlists, its settings, extensions and
/// players. Valid for a day or until revoked; only its hash is kept. Reading through it never counts as a play.
/// </summary>
public sealed class MigrationCodeService(
    ArcanistDbContext db,
    AccessGuard guard,
    TimeProvider time,
    P2PSettings p2p,
    P2PNode node,
    IServer server,
    ILogger<MigrationCodeService> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    // No 0/O or 1/I, as the setup code: it can be read aloud or typed.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<MigrationCode> CreateAsync(CancellationToken cancellationToken)
    {
        var access = guard.Admin();
        guard.RecentAuthentication();
        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, 20);
        var code = string.Join('-', Enumerable.Range(0, 5).Select(i => new string(chars, i * 4, 4)));
        var now = time.GetUtcNow().UtcDateTime;
        var grant = new MigrationGrant { CodeHash = Hash(code), CreatedByUserId = access.UserId, CreatedAt = now, ExpiresAt = now + Lifetime };
        db.MigrationGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("{User} made a migration code (valid until {ExpiresAt:u})", access.UserName, grant.ExpiresAt);

        string? link = null;
        var config = await p2p.GetAsync(cancellationToken);
        if (config.Enabled && node.State.Ticket is { } ticket)
            link = P2PLinks.Build(P2PPeerKind.Server, ticket, code, config.DisplayName, migrate: true);
        return new MigrationCode(grant.Id, code, grant.ExpiresAt, Addresses(server), link);
    }

    public async Task<IReadOnlyList<MigrationCodeInfo>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var now = time.GetUtcNow().UtcDateTime;
        return (await db.MigrationGrants.AsNoTracking().OrderByDescending(g => g.CreatedAt).Take(20).ToListAsync(cancellationToken))
            .Select(g => new MigrationCodeInfo(g.Id, g.CreatedAt, g.ExpiresAt, g.RevokedAt, g.LastUsedAt, g.LastUsedBy, g.RevokedAt is null && g.ExpiresAt > now))
            .ToList();
    }

    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Admin();
        var grant = await db.MigrationGrants.SingleOrDefaultAsync(g => g.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        grant.RevokedAt ??= time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The code's hash, as kept: letters and digits only, upper case (dashes and spaces do not count).</summary>
    public static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(Canonical(code))));

    private static string Canonical(string code) => new(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>This server's http addresses on its local networks (its port, each IPv4 address).</summary>
    internal static IReadOnlyList<string> Addresses(IServer server)
    {
        int? port = null;
        foreach (var address in server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
        {
            if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                continue;
            var parsable = address.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost");
            if (Uri.TryCreate(parsable, UriKind.Absolute, out var uri))
            {
                port = uri.Port;
                break;
            }
        }

        return port is { } p ? Core.Dlna.LocalNetwork.Addresses().Select(a => $"http://{a.Address}:{p}").Distinct().ToList() : [];
    }
}
