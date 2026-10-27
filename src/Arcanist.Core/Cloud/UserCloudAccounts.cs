using System.Collections.Concurrent;
using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Auth;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>
/// The Ultimate Cloud accounts users link for themselves (any user, from their account page), apart from the server's
/// (<see cref="UltimateCloudAccount"/>). Each one brings its owner's music as a personal library only they see, their
/// plays, statistics and StarTracks, and their devices through the P2P network; its plan is checked every hour like the
/// server's. An administrator's own Cloud plan also unlocks the paid features of the server (<see cref="CloudTiers"/>).
/// Unlinking (or deleting the user) removes the personal library; nothing is touched in Ultimate Cloud.
/// </summary>
public sealed class UserCloudAccounts(
    UltimateCloudAuth auth,
    UltimateCloudClient client,
    UltimateCloudAccount server,
    SettingsStore settings,
    IArcanistDbContextFactory databases,
    IServiceProvider services,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<UltimateCloudOptions> options,
    DatabaseMaintenance maintenance,
    UserAccessCache accessCache,
    TimeProvider time,
    ILogger<UserCloudAccounts> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, PendingLink> _pending = new();
    private readonly ConcurrentDictionary<Guid, CloudPlanSnapshot> _plans = new();
    private readonly ConcurrentDictionary<Guid, string> _errors = new();
    private readonly SemaphoreSlim _wake = new(0);
    private volatile IReadOnlyCollection<Guid> _admins = [];
    private volatile IReadOnlyCollection<Guid> _linked = [];

    private sealed record PendingLink(CloudLinkRequest Request, CancellationTokenSource Cancel);

    /// <summary>The users with a linked account, as of the last check (every hour, and after each link or unlink).</summary>
    public IReadOnlyCollection<Guid> Linked => _linked;

    public CloudLinkRequest? PendingOf(Guid user) => _pending.TryGetValue(user, out var pending) ? pending.Request : null;

    public CloudPlanSnapshot? PlanOf(Guid user) => _plans.GetValueOrDefault(user);

    public string? ErrorOf(Guid user) => _errors.GetValueOrDefault(user);

    /// <summary>The tier of a user's own account; of the server's with null.</summary>
    public CloudTier TierOf(Guid? account)
    {
        if (account is not { } user)
            return server.Tier;
        if (!server.Enabled || !_plans.TryGetValue(user, out var plan) || ErrorOf(user) == ErrorCodes.CloudLinkExpired
            || time.GetUtcNow().UtcDateTime - plan.CheckedAt > options.CurrentValue.OfflineGrace)
            return CloudTier.None;
        return plan.Plan == "Cloud" ? CloudTier.Paid : CloudTier.Free;
    }

    /// <summary>The best tier among administrators' own accounts.</summary>
    public CloudTier AdminTier => _admins.Select(id => TierOf(id)).DefaultIfEmpty(CloudTier.None).Max();

    /// <summary>Starts linking a user's own account: returns the code to approve, and waits for it in the background.</summary>
    /// <param name="libraryName">The name of their personal library; by default "Ana's Ultimate Cloud" with the account's
    /// first name, in <paramref name="language"/> (or the server's).</param>
    public async Task<CloudLinkRequest> StartLinkAsync(Guid user, string? libraryName, CancellationToken cancellationToken, string? language = null)
    {
        if (!server.Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.CloudDisabled);
        if (!server.IsConfigured)
            throw new ArcanistException.Conflict(ErrorCodes.CloudNotConfigured);
        CancelLink(user);
        (CloudLinkRequest Request, string DeviceCode, TimeSpan Interval) started;
        try
        {
            started = await auth.StartAsync(cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            throw new ArcanistException.Conflict(e.Code);
        }

        var cancel = new CancellationTokenSource();
        _pending[user] = new PendingLink(started.Request, cancel);
        _errors.TryRemove(user, out _);
        _ = Task.Run(async () =>
        {
            try
            {
                StoredCloudLink link;
                using (await maintenance.WorkAsync(cancel.Token))
                    link = await auth.WaitForApprovalAsync(started.DeviceCode, started.Interval, started.Request.ExpiresAt, cancel.Token, user);
                logger.LogInformation("User {User} linked their Ultimate Cloud account {Account}", user, link.UserName ?? link.Email ?? link.Subject);
                Remove(user, cancel);
                using (await maintenance.WorkAsync(CancellationToken.None))
                    await AfterLinkAsync(user, link, libraryName, language, CancellationToken.None);
            }
            catch (UltimateCloudException e)
            {
                _errors[user] = e.Code;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                logger.LogError(e, "Linking the Ultimate Cloud account of user {User} failed", user);
                _errors[user] = ErrorCodes.CloudUnreachable;
            }
            finally
            {
                Remove(user, cancel);
            }
        }, CancellationToken.None);
        return started.Request;
    }

    /// <summary>Ends a pending link, unless a newer one took its place.</summary>
    private void Remove(Guid user, CancellationTokenSource cancel)
    {
        if (_pending.TryGetValue(user, out var pending) && pending.Cancel == cancel)
            _pending.TryRemove(new KeyValuePair<Guid, PendingLink>(user, pending));
    }

    public void CancelLink(Guid user)
    {
        if (_pending.TryRemove(user, out var pending))
            pending.Cancel.Cancel();
    }

    /// <summary>Forgets a user's account and removes their personal library.</summary>
    public async Task UnlinkAsync(Guid user, CancellationToken cancellationToken)
    {
        CancelLink(user);
        await auth.UnlinkAsync(cancellationToken, user);
        _plans.TryRemove(user, out _);
        _errors.TryRemove(user, out _);
        foreach (var key in await settings.KeysAsync(SettingKeys.UltimateCloudUser(user, ""), cancellationToken))
            await settings.DeleteAsync(key, cancellationToken);
        _linked = _linked.Where(id => id != user).ToList();

        await using (var db = databases.CreateDbContext())
        {
            var scans = services.GetRequiredService<ScanCoordinator>();
            foreach (var library in await db.Libraries.Where(l => l.OwnerUserId == user).ToListAsync(cancellationToken))
                await LibraryService.RemoveAsync(db, scans, library, cancellationToken);
        }

        accessCache.InvalidateAll();
        await ConnectAsync(cancellationToken);
    }

    /// <summary>Checks a user's plan now.</summary>
    public async Task RefreshAsync(Guid user, CancellationToken cancellationToken)
    {
        if (!server.Enabled)
            return;
        if (await auth.LinkAsync(cancellationToken, user) is not { } link)
        {
            _plans.TryRemove(user, out _);
            return;
        }

        await RenameGenericLibraryAsync(user, link, cancellationToken);

        try
        {
            using (UltimateCloudClient.As(user))
            {
                var plan = UltimateCloudAccount.ParsePlan(await client.SendAsync(UltimateCloudAccount.PlanQuery, null, cancellationToken), time);
                _plans[user] = plan;
                _errors.TryRemove(user, out _);
                await settings.SetAsync(SettingKeys.UltimateCloudUser(user, "plan"), plan, cancellationToken);
                // An administrator's account can sync libraries without the server's: it keeps the sync component up to date too.
                var tool = services.GetRequiredService<UltimateAudioTool>();
                if (_admins.Contains(user) && await auth.LinkAsync(cancellationToken) is null && tool.IsInstalled)
                    await tool.EnsureAsync(cancellationToken);
            }
        }
        catch (UltimateCloudException e)
        {
            _errors[user] = e.Code;
            logger.LogWarning("The Ultimate Cloud plan of user {User} could not be checked: {Error}", user, e.Code);
        }

        await ConnectAsync(cancellationToken);
    }

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <summary>Registers the server as a device of the account, reads its plan and adds the personal library.</summary>
    private async Task AfterLinkAsync(Guid user, StoredCloudLink link, string? libraryName, string? language, CancellationToken cancellationToken)
    {
        await using (var db = databases.CreateDbContext())
        {
            if (await db.Users.AnyAsync(u => u.Id == user && u.Role == UserRole.Admin, cancellationToken))
                _admins = _admins.Append(user).Distinct().ToList();
        }

        _linked = _linked.Append(user).Distinct().ToList();
        try
        {
            using (UltimateCloudClient.As(user))
                await client.SendAsync(
                    """
                    mutation Touch($deviceId: String!, $deviceName: String!, $platform: String, $appVersion: String) {
                      touchSyncedDevice(deviceId: $deviceId, deviceName: $deviceName, deviceType: SERVER, platform: $platform, appVersion: $appVersion)
                    }
                    """,
                    new
                    {
                        deviceId = await auth.DeviceIdAsync(cancellationToken),
                        deviceName = $"Arcanist ({Environment.MachineName})",
                        platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        appVersion = typeof(UserCloudAccounts).Assembly.GetName().Version?.ToString(3),
                    },
                    cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            logger.LogWarning("Ultimate Cloud did not register this server as a device of user {User}: {Error}", user, e.Code);
        }

        await RefreshAsync(user, cancellationToken);
        await AddLibraryAsync(user, link, libraryName, language, cancellationToken);
        services.GetService<UltimateCloudReporter>()?.StarsChanged();
    }

    /// <summary>The source settings that tell a personal library's account.</summary>
    internal static string SourceSettings(Guid user) => JsonSerializer.Serialize(new { user = user.ToString("N") });

    /// <summary>The user whose own account a source shows; null for the server's.</summary>
    internal static Guid? UserOf(string? sourceSettings)
    {
        if (string.IsNullOrEmpty(sourceSettings))
            return null;
        try
        {
            return JsonDocument.Parse(sourceSettings).RootElement.TryGetProperty("user", out var user) && Guid.TryParse(user.GetString(), out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The account's music as a library only its owner sees, scanned now and every 6 hours (and on Ultimate Cloud's notices).</summary>
    private async Task AddLibraryAsync(Guid user, StoredCloudLink link, string? libraryName, string? language, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        if (await db.Libraries.AnyAsync(l => l.OwnerUserId == user && l.Sources.Any(s => s.Kind == LibrarySourceKind.UltimateCloud), cancellationToken)
            || !await db.Users.AnyAsync(u => u.Id == user, cancellationToken))
            return;

        var name = string.IsNullOrWhiteSpace(libraryName)
            ? Application.Cloud.UltimateCloudService.DefaultName(UltimateCloudCollection.Library, link.DisplayFirstName,
                language ?? await settings.GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken))
            : libraryName.Trim();
        if (name.Length > 100)
            name = name[..100];
        var unique = await UniqueNameAsync(db, name, null, cancellationToken);

        var library = new Data.Entities.Library
        {
            Number = await LibraryService.NextNumberAsync(db, cancellationToken),
            Name = unique,
            OwnerUserId = user,
            VisibleToAll = false,
            ScanIntervalMinutes = 6 * 60,
            WatchForChanges = true,
        };
        library.Sources.Add(new Data.Entities.LibrarySource
        {
            Library = library,
            Kind = LibrarySourceKind.UltimateCloud,
            Location = UltimateCloudCollection.Library.ToString(),
            Settings = SourceSettings(user),
        });
        library.Access.Add(new LibraryAccess { UserId = user, Library = library });
        db.Libraries.Add(library);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        await services.GetRequiredService<ScanCoordinator>().EnqueueAsync(library.Id, ScanKind.Full, user, cancellationToken);
    }

    /// <summary><paramref name="name"/>, or with " (2)", " (3)"… when another library has it.</summary>
    private static async Task<string> UniqueNameAsync(ArcanistDbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        var taken = (await db.Libraries.Where(l => l.Id != except).Select(l => l.Name).ToListAsync(cancellationToken))
            .Select(TextNormalizer.Normalize).ToHashSet();
        var unique = name;
        for (var i = 2; taken.Contains(TextNormalizer.Normalize(unique)); i++)
            unique = $"{name} ({i})";
        return unique;
    }

    /// <summary>
    /// Before 0.1.0-alpha.13 the panel named personal libraries "My Ultimate Cloud" / "Mi Ultimate Cloud": they take
    /// their owner's first name ("Ultimate Cloud de Giancarlo") once it is known. Renamed ones are left alone.
    /// </summary>
    private async Task RenameGenericLibraryAsync(Guid user, StoredCloudLink link, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(link.DisplayFirstName))
            return;
        await using var db = databases.CreateDbContext();
        var library = await db.Libraries.FirstOrDefaultAsync(l => l.OwnerUserId == user && l.Sources.Any(s => s.Kind == LibrarySourceKind.UltimateCloud),
            cancellationToken);
        var language = library?.Name switch
        {
            "Mi Ultimate Cloud" => "es",
            "My Ultimate Cloud" => "en",
            _ => null,
        };
        if (library is null || language is null)
            return;
        var name = Application.Cloud.UltimateCloudService.DefaultName(UltimateCloudCollection.Library, link.DisplayFirstName, language);
        library.Name = await UniqueNameAsync(db, name.Length > 100 ? name[..100] : name, library.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await services.GetRequiredService<UltimateCloudConnect>().SyncAsync(cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            logger.LogWarning("The P2P connection through Ultimate Cloud could not be updated: {Error}", e.Code);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured)
                {
                    using (await maintenance.WorkAsync(stoppingToken))
                        await CheckAllAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Checking the users' Ultimate Cloud accounts failed");
            }

            try
            {
                await _wake.WaitAsync(options.CurrentValue.PlanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        var linked = await auth.LinkedUsersAsync(cancellationToken);
        List<(Guid Id, UserRole Role)> users;
        await using (var db = databases.CreateDbContext())
        {
            users = (await db.Users.AsNoTracking().Where(u => linked.Contains(u.Id)).Select(u => new { u.Id, u.Role }).ToListAsync(cancellationToken))
                .Select(u => (u.Id, u.Role)).ToList();
        }

        // Deleted users: their link goes too (their library went with them).
        foreach (var gone in linked.Except(users.Select(u => u.Id)))
            await UnlinkAsync(gone, cancellationToken);

        _linked = users.Select(u => u.Id).ToList();
        _admins = users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).ToList();
        foreach (var (id, _) in users)
        {
            if (!_plans.ContainsKey(id) && await settings.GetAsync<CloudPlanSnapshot?>(SettingKeys.UltimateCloudUser(id, "plan"), null, cancellationToken) is { } plan)
                _plans[id] = plan;
        }

        if (!server.Enabled)
        {
            foreach (var id in _pending.Keys)
                CancelLink(id);
            return;
        }

        if (!server.IsConfigured)
            return;
        foreach (var (id, _) in users)
            await RefreshAsync(id, cancellationToken);
    }
}

/// <summary>
/// The tier the server's features go by (<see cref="ICapabilityService"/>): the server's account, or the best of the
/// administrators' own accounts. Whose account each feature uses is chosen apart (a library's sync, a drop folder's uploads).
/// </summary>
public sealed class CloudTiers(UltimateCloudAccount server, IServiceProvider services) : ICloudLinkState
{
    public CloudTier Tier
    {
        get
        {
            var tier = server.Tier;
            return services.GetService<UserCloudAccounts>()?.AdminTier is { } admins && admins > tier ? admins : tier;
        }
    }
}
