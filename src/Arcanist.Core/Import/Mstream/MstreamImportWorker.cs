using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Import.Mstream;

/// <summary>Wakes the import worker when an import is created.</summary>
public sealed class MstreamImportSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
            _signal.Release();
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>
/// Finishes imports read from an mStream folder: once the scans of their libraries are over, so mStream's files can
/// be found in the catalog, brings each user's playlists, ratings and plays. Imports interrupted by a restart are
/// done again, which is safe.
/// </summary>
internal sealed class MstreamImportWorker(
    IArcanistDbContextFactory databases,
    MstreamImporter importer,
    MstreamImportSignal signal,
    TimeProvider time,
    IOptionsMonitor<Setup.SetupOptions> setup,
    DatabaseMaintenance maintenance,
    ILogger<MstreamImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool finished;
                using (await maintenance.WorkAsync(stoppingToken))
                    finished = await RunNextAsync(stoppingToken);
                if (finished)
                    continue;
                // Nothing to do yet: check again soon (scans finishing are not signalled).
                await signal.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "mStream import failed");
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <returns>Whether an import was finished.</returns>
    private async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        // Before setup there is no database yet, and while it runs the database is being created.
        if (!databases.IsConfigured || !setup.CurrentValue.Completed)
            return false;
        await using var db = databases.CreateDbContext();
        var import = await db.MstreamImports
            .Where(i => i.Status == MstreamImportStatus.Waiting || i.Status == MstreamImportStatus.Running)
            .OrderBy(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (import is null)
            return false;

        var vpaths = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.VpathMap) ?? [];
        var sources = vpaths.Values.ToList();
        var libraries = await db.LibrarySources.Where(s => sources.Contains(s.Id)).Select(s => s.LibraryId).Distinct().ToListAsync(cancellationToken);
        var scans = await db.ScanJobs.Where(j => libraries.Contains(j.LibraryId)).Select(j => new { j.LibraryId, j.Status }).ToListAsync(cancellationToken);
        // Every library is scanned when it is created (setup queues those scans just after recording its import), and
        // scan jobs are kept: a library without any has its first scan still to come.
        if (scans.Any(j => j.Status is ScanStatus.Queued or ScanStatus.Running) || libraries.Any(id => scans.All(j => j.LibraryId != id)))
            return false;

        import.Status = MstreamImportStatus.Running;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Importing mStream user data from {Source}", import.Source);

        try
        {
            var data = MstreamFolder.Read(import.Source);
            var users = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.UserMap) ?? [];
            int playlists = 0, ratings = 0, plays = 0, unmatched = data.Unresolved;
            foreach (var (name, content) in data.Content)
            {
                if (!users.TryGetValue(name, out var userId) || !await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
                    continue;
                var counts = await importer.ApplyAsync(db, userId, content, vpaths, cancellationToken);
                (playlists, ratings, plays, unmatched) = (playlists + counts.Playlists, ratings + counts.Ratings, plays + counts.Plays, unmatched + counts.Unmatched);
            }

            (import.Playlists, import.Ratings, import.Plays, import.Unmatched) = (playlists, ratings, plays, unmatched);
            import.Status = MstreamImportStatus.Completed;
        }
        catch (ArcanistException e)
        {
            // The installation was moved or changed since it was analyzed.
            import.Status = MstreamImportStatus.Failed;
            import.ErrorCode = e is ArcanistException.Invalid invalid ? invalid.Errors.ToDictionary().Values.First()[0] : e.Code;
        }

        import.FinishedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("mStream import from {Source} {Status}: {Playlists} playlists, {Ratings} ratings, {Plays} played tracks, {Unmatched} not found",
            import.Source, import.Status, import.Playlists, import.Ratings, import.Plays, import.Unmatched);
        return true;
    }
}

/// <summary>
/// Signs in users imported from an mStream server through its API, whose password hashes could not be read: the
/// first time, the password is checked with that server, kept as the Arcanist password, and the user's playlists,
/// ratings and plays are brought in the background.
/// </summary>
internal sealed class MstreamServerSignIn(
    IArcanistDbContextFactory databases,
    MstreamServerClient client,
    MstreamImporter importer,
    DatabaseMaintenance maintenance,
    ILogger<MstreamServerSignIn> logger) : IExternalSignIn
{
    public async Task<bool> VerifyAsync(User user, string password, CancellationToken cancellationToken)
    {
        if (MstreamPasswords.ServerImport(user.PasswordHash) is not { } importId)
            return false;

        await using var db = databases.CreateDbContext();
        var import = await db.MstreamImports.AsNoTracking().SingleOrDefaultAsync(i => i.Id == importId, cancellationToken);
        if (import is null || MstreamServerClient.ParseUrl(import.Source) is not { } server)
            return false;

        string? token;
        try
        {
            token = await client.LoginAsync(server, user.UserName, password, cancellationToken);
        }
        catch (ArcanistException)
        {
            logger.LogWarning("mStream server {Server} did not answer to sign in {User}", import.Source, user.UserName);
            return false;
        }

        if (token is null)
            return false;

        var vpaths = JsonSerializer.Deserialize<Dictionary<string, Guid>>(import.VpathMap) ?? [];
        Pending = Task.Run(() => BringDataAsync(import.Id, user.Id, server, token, vpaths));
        return true;
    }

    /// <summary>The last data transfer started, for tests to wait on.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    private async Task BringDataAsync(Guid importId, Guid userId, Uri server, string token, Dictionary<string, Guid> vpaths)
    {
        try
        {
            var content = await client.ReadUserAsync(server, token, CancellationToken.None);
            using var work = await maintenance.WorkAsync(CancellationToken.None);
            await using var db = databases.CreateDbContext();
            var counts = await importer.ApplyAsync(db, userId, content, vpaths, CancellationToken.None);
            await db.MstreamImports.Where(i => i.Id == importId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Playlists, i => i.Playlists + counts.Playlists)
                .SetProperty(i => i.Ratings, i => i.Ratings + counts.Ratings)
                .SetProperty(i => i.Plays, i => i.Plays + counts.Plays)
                .SetProperty(i => i.Unmatched, i => i.Unmatched + counts.Unmatched));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not bring the mStream data of user {UserId}", userId);
        }
    }
}

public static class MstreamImportServiceCollectionExtensions
{
    public static IServiceCollection AddMstreamImport(this IServiceCollection services)
    {
        services.AddSingleton<MstreamImportSignal>();
        services.AddSingleton<MstreamImporter>();
        services.AddSingleton<MstreamServerClient>();
        services.AddSingleton<MstreamReader>();
        services.AddSingleton<IExternalSignIn, MstreamServerSignIn>();
        services.AddHostedService<MstreamImportWorker>();
        services.AddScoped<Application.Import.MstreamImportService>();
        services.AddHttpClient(MstreamServerClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        return services;
    }
}
