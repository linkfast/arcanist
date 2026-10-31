using System.Text.Json.Nodes;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Extensibility;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Extensions.Sdk;

/// <summary>The server's Ultimate Cloud accounts, for extensions (<see cref="IUltimateCloud"/>).</summary>
internal sealed class CoreUltimateCloud : IUltimateCloud, IDisposable
{
    private readonly UltimateCloudAccount _server;
    private readonly UserCloudAccounts _users;
    private readonly UltimateCloudAuth _auth;
    private readonly UltimateCloudClient _client;
    private readonly IHttpClientFactory _clients;
    private readonly ICloudLinkState _tiers;
    private readonly IArcanistDbContextFactory _databases;
    private readonly UltimateCloudSignals _signals;

    public CoreUltimateCloud(UltimateCloudAccount server, UserCloudAccounts users, UltimateCloudAuth auth, UltimateCloudClient client,
        IHttpClientFactory clients, ICloudLinkState tiers, IArcanistDbContextFactory databases, UltimateCloudSignals signals)
    {
        (_server, _users, _auth, _client, _clients, _tiers, _databases, _signals) = (server, users, auth, client, clients, tiers, databases, signals);
        _signals.Changed += OnChanged;
    }

    public bool Enabled => _server.Enabled;

    public event Action? Changed;

    private void OnChanged() => Changed?.Invoke();

    public async Task<IReadOnlyList<CloudAccount>> AccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = new List<CloudAccount>();
        if (await _auth.LinkAsync(cancellationToken) is { } server)
            accounts.Add(new CloudAccount(null, server.Subject, ToPlan(_server.Tier)));
        foreach (var user in _users.Linked)
            if (await _auth.LinkAsync(cancellationToken, user) is { } own)
                accounts.Add(new CloudAccount(user, own.Subject, ToPlan(_users.TierOf(user))));
        return accounts;
    }

    public CloudPlan PlanOf(Guid? userId) => ToPlan(_users.TierOf(userId));

    public CloudPlan ServerPlan => ToPlan(_tiers.Tier);

    public async Task<JsonNode> SendAsync(Guid? userId, string query, object? variables, CancellationToken cancellationToken)
    {
        using (UltimateCloudClient.As(userId))
        {
            try
            {
                return await _client.SendAsync(query, variables, cancellationToken);
            }
            catch (UltimateCloudException e)
            {
                throw new CloudApiException(e.Code, e.Message);
            }
        }
    }

    public Task<(bool Found, Guid? UserId)> AccountOfAsync(string subject, CancellationToken cancellationToken) =>
        _auth.AccountOfAsync(subject, cancellationToken);

    public async Task<Guid?> ServerAccountOwnerAsync(CancellationToken cancellationToken)
    {
        await using var db = _databases.CreateDbContext();
        if ((await _auth.LinkAsync(cancellationToken))?.LinkedByUserId is { } linkedBy && await db.Users.AnyAsync(u => u.Id == linkedBy, cancellationToken))
            return linkedBy;
        return await db.Users.Where(u => u.Role == UserRole.Admin).OrderBy(u => u.CreatedAt).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public HttpClient FilesClient() => _clients.CreateClient(UltimateCloudStorage.FilesClientName);

    internal static CloudPlan ToPlan(CloudTier tier) => tier switch
    {
        CloudTier.Paid => CloudPlan.Paid,
        CloudTier.Free => CloudPlan.Free,
        _ => CloudPlan.None,
    };

    public void Dispose() => _signals.Changed -= OnChanged;
}
