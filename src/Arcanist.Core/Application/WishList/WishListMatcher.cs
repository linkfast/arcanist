using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScanJob = Arcanist.Core.Application.Scanning.ScanJob;
using ScanStatus = Arcanist.Core.Data.Entities.ScanStatus;

namespace Arcanist.Core.Application.WishList;

/// <summary>Tells each user's <c>wishObtained</c> subscription that their wishes came true.</summary>
public sealed class WishListSignals
{
    private event Action<Guid, WishObtained>? Obtained;

    /// <summary>How many subscriptions are listening (tests wait for theirs).</summary>
    internal int Listeners => Obtained?.GetInvocationList().Length ?? 0;

    internal void OnObtained(Guid userId, WishObtained wish) => Obtained?.Invoke(userId, wish);

    internal IObservable<WishObtained> Of(Guid userId) => new Feed(this, userId);

    private sealed class Feed(WishListSignals signals, Guid userId) : IObservable<WishObtained>
    {
        public IDisposable Subscribe(IObserver<WishObtained> observer)
        {
            void Handle(Guid user, WishObtained wish)
            {
                if (user == userId)
                    observer.OnNext(wish);
            }

            signals.Obtained += Handle;
            return new Unsubscriber(() => signals.Obtained -= Handle);
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}

/// <summary>
/// Looks for the wanted songs of every user after each scan (whatever brought the music: a scan, an upload, an
/// import, a copy or a sync), and every few hours (a user may be given a library). Short maintenance units.
/// </summary>
internal sealed class WishListMatcher(
    IServiceScopeFactory scopes,
    IArcanistDbContextFactory databases,
    ScanEvents scans,
    WishListSignals signals,
    UserAccessCache users,
    IOptionsMonitor<SetupOptions> setup,
    DatabaseMaintenance maintenance,
    TimeProvider time,
    ILogger<WishListMatcher> logger) : BackgroundService, IObserver<ScanJob>
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    /// <summary>After a scan, a little while: scans often come one after another.</summary>
    internal static TimeSpan Settle { get; set; } = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _wake = new(0);

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var subscription = scans.Subscribe(this);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured)
                    await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Looking for wished songs failed");
            }

            try
            {
                await _wake.WaitAsync(Every, stoppingToken);
                await Task.Delay(Settle, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        List<Guid> owners;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            owners = await db.WishListItems.Where(w => w.ObtainedAt == null).Select(w => w.UserId).Distinct().ToListAsync(cancellationToken);
        }

        foreach (var owner in owners)
        {
            if (await users.GetUserAsync(owner, cancellationToken) is not { IsDisabled: false } access)
                continue;

            var obtained = new List<WishObtained>();
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
                var wanted = await db.WishListItems.Where(w => w.UserId == owner && w.ObtainedAt == null).ToListAsync(cancellationToken);
                var now = time.GetUtcNow().UtcDateTime;
                if (await WishListService.MatchAsync(scope.ServiceProvider.GetRequiredService<CatalogService>(), access, wanted, now, cancellationToken) == 0)
                    continue;

                await db.SaveChangesAsync(cancellationToken);
                obtained.AddRange(wanted.Where(w => w.ObtainedAt is not null).Select(w => new WishObtained(w.Id, w.Title, w.Artist, w.TrackId, w.ObtainedAt!.Value)));
            }

            foreach (var wish in obtained)
                signals.OnObtained(owner, wish);
            logger.LogInformation("{Count} wished songs arrived for user {UserId}", obtained.Count, owner);
        }
    }

    void IObserver<ScanJob>.OnNext(ScanJob job)
    {
        if (job.Status == ScanStatus.Completed)
            Wake();
    }

    void IObserver<ScanJob>.OnError(Exception error)
    {
    }

    void IObserver<ScanJob>.OnCompleted()
    {
    }
}
