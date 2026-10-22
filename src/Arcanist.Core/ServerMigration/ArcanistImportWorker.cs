using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Extensions;
using Arcanist.Core.Application.Frontends;
using Arcanist.Core.Application.ServerMigration;
using Arcanist.Core.Auth;
using Arcanist.Core.Backups;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.P2P;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.ServerMigration;

/// <summary>
/// Brings another Arcanist over, one migration at a time, in steps that resume after a restart: its settings,
/// extensions and players (as the administrator who started it, who had just authenticated); its music, file by
/// file into the chosen libraries with the same paths (never overwriting: a file of the same size counts as there);
/// then, once the libraries are scanned, its users' ratings, plays, playlists and wishes, matched by audio, ids and
/// tags as restoring a backup does.
/// </summary>
internal sealed class ArcanistImportWorker(
    IArcanistDbContextFactory databases,
    IServiceScopeFactory scopes,
    ArcanistSourceClient client,
    ILibraryStorageResolver storages,
    LibraryLocks locks,
    ScanCoordinator scans,
    DatabaseMaintenance maintenance,
    UserAccessCache users,
    IOptionsMonitor<Setup.SetupOptions> setup,
    ArcanistPaths paths,
    TimeProvider time,
    IServiceProvider services,
    ILogger<ArcanistImportWorker> logger) : BackgroundService
{
    private const int ScanEvery = 500;
    private const int MaxFailuresInARow = 5;

    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _lock = new();
    private (Guid Id, CancellationTokenSource Cancel)? _running;

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <summary>Stops the migration if it is the one running; returns whether it was.</summary>
    public bool Cancel(Guid importId)
    {
        lock (_lock)
        {
            if (_running is not { } running || running.Id != importId)
                return false;
            running.Cancel.Cancel();
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await NextAsync(stoppingToken) is { } importId)
                {
                    await RunAsync(importId, stoppingToken);
                    continue;
                }

                await _wake.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Migration from another Arcanist failed");
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task<Guid?> NextAsync(CancellationToken cancellationToken)
    {
        if (!databases.IsConfigured || !setup.CurrentValue.Completed)
            return null;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            return await db.ArcanistImports
                .Where(i => i.Status != ArcanistImportStatus.Completed && i.Status != ArcanistImportStatus.Failed && i.Status != ArcanistImportStatus.Cancelled)
                .OrderBy(i => i.CreatedAt)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private async Task RunAsync(Guid importId, CancellationToken stoppingToken)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_lock)
            _running = (importId, cancel);
        try
        {
            var token = cancel.Token;
            var import = await UpdateAsync(importId, _ => { }, token);
            var plan = JsonSerializer.Deserialize<ArcanistImportPlan>(import.Plan, JsonSerializerOptions.Web)!;
            var libraries = JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(import.LibraryMap) ?? [];
            var userMap = JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(import.UserMap) ?? [];
            var source = Endpoint(import);
            await using var scope = scopes.CreateAsyncScope();
            if (source.Ticket is not null)
                await ArcanistImportService.EnsureP2PAsync(scope.ServiceProvider, setupPending: false, token);
            var manifest = await client.GetAsync<ExportManifest>(source, "/manifest", token);
            await UpdateAsync(importId, i =>
            {
                i.SourceName = manifest.ServerName.Length > 200 ? manifest.ServerName[..200] : manifest.ServerName;
                i.SourceVersion = manifest.Version;
            }, token);

            if (import.Status is ArcanistImportStatus.Queued or ArcanistImportStatus.Preparing)
            {
                await UpdateAsync(importId, i => i.Status = ArcanistImportStatus.Preparing, token);
                await PrepareAsync(scope.ServiceProvider, import, plan, manifest, source, libraries, userMap, token);
                import.Status = ArcanistImportStatus.Copying;
                await UpdateAsync(importId, i => i.Status = ArcanistImportStatus.Copying, token);
            }

            if (import.Status is ArcanistImportStatus.Copying)
            {
                await CopyAsync(import, source, libraries, token);
                // The scans first: whoever sees it waiting must find them queued.
                foreach (var libraryId in libraries.Values.Distinct())
                {
                    using (await maintenance.WorkAsync(token))
                        await scans.EnqueueAsync(libraryId, ScanKind.Incremental, import.RequestedByUserId, token);
                }

                import.Status = ArcanistImportStatus.Waiting;
                await UpdateAsync(importId, i => i.Status = ArcanistImportStatus.Waiting, token);
            }

            if (import.Status is ArcanistImportStatus.Waiting)
            {
                await WaitForScansAsync(libraries.Values.Distinct().ToList(), token);
                import.Status = ArcanistImportStatus.Applying;
                await UpdateAsync(importId, i => i.Status = ArcanistImportStatus.Applying, token);
            }

            if (plan.UserData && userMap.Count > 0)
                await ApplyUserDataAsync(import, manifest, source, userMap, token);

            await FinishAsync(importId, ArcanistImportStatus.Completed, null);
            services.GetService<Application.WishList.WishListMatcher>()?.Wake();
            logger.LogInformation("Migrated {Source} into this server", manifest.ServerName);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Goes on after the restart.
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(importId, ArcanistImportStatus.Cancelled, null);
        }
        catch (ArcanistException e)
        {
            logger.LogWarning("Migration {Import} failed: {Error}", importId, e.Code);
            await FinishAsync(importId, ArcanistImportStatus.Failed, e.Code);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Migration {Import} failed", importId);
            await FinishAsync(importId, ArcanistImportStatus.Failed, ErrorCodes.ImportFailed);
        }
        finally
        {
            lock (_lock)
                _running = null;
            DeleteTemporaryFiles();
        }
    }

    private ArcanistEndpoint Endpoint(ArcanistImport import)
    {
        var code = ArcanistImportService.Protector(services).Unprotect(import.ProtectedCode);
        return import.SourceTicket is { } ticket
            ? new ArcanistEndpoint(null, JsonSerializer.Deserialize<NodeTicket>(ticket, NativeP2P.Json), code)
            : new ArcanistEndpoint(new Uri(import.SourceUrl!), null, code);
    }

    /// <summary>Settings, extensions and players, as the administrator who started the migration.</summary>
    private async Task PrepareAsync(
        IServiceProvider scope, ArcanistImport import, ArcanistImportPlan plan, ExportManifest manifest, ArcanistEndpoint source,
        Dictionary<Guid, Guid> libraries, Dictionary<Guid, Guid> userMap, CancellationToken cancellationToken)
    {
        if (!plan.Settings && !plan.Extensions)
            return;
        var actor = await users.GetUserAsync(import.RequestedByUserId, cancellationToken);
        if (actor is not { IsAdmin: true })
            throw new ArcanistException.Forbidden();
        using var acting = HttpCurrentUser.ActAs(actor, time.GetUtcNow());

        if (plan.Settings && !import.SettingsApplied)
        {
            var values = await client.GetAsync<Dictionary<string, JsonElement>>(source, "/settings", cancellationToken);
            using (await maintenance.WorkAsync(cancellationToken))
                await ApplySettingsAsync(scope, values, libraries.Concat(userMap).ToList(), cancellationToken);
            await UpdateAsync(import.Id, i => i.SettingsApplied = true, cancellationToken);
        }

        if (plan.Extensions)
            await InstallComponentsAsync(scope, import, manifest, source, cancellationToken);
    }

    /// <summary>
    /// Stores the other server's settings, with its library and user ids changed to this server's, then refreshes
    /// whatever keeps them in memory (the same setters the panel uses).
    /// </summary>
    private static async Task ApplySettingsAsync(
        IServiceProvider scope, Dictionary<string, JsonElement> values, IReadOnlyList<KeyValuePair<Guid, Guid>> ids, CancellationToken cancellationToken)
    {
        var store = scope.GetRequiredService<SettingsStore>();
        foreach (var (key, value) in values)
        {
            if (!ExportApi.PortableSettings.Contains(key))
                continue;
            var json = value.GetRawText();
            foreach (var (from, to) in ids)
                json = json.Replace(from.ToString(), to.ToString(), StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            await store.SetAsync(key, document.RootElement.Clone(), cancellationToken);
        }

        async Task Refresh<T>(string key, T fallback, Func<T, Task> set)
        {
            if (values.ContainsKey(key))
                await set(await store.GetAsync(key, fallback, cancellationToken));
        }

        var ratings = scope.GetRequiredService<Application.Listening.RatingSettings>();
        await Refresh(SettingKeys.RatingMode, ratings.Current, mode => ratings.SetAsync(mode, cancellationToken));
        var dlna = scope.GetRequiredService<Dlna.DlnaSettings>();
        await Refresh(SettingKeys.Dlna, Dlna.DlnaConfig.Default, config => dlna.SetAsync(config, cancellationToken));
        var mstream = scope.GetRequiredService<Extensions.Mstream.MstreamSettings>();
        await Refresh(SettingKeys.Mstream, Extensions.Mstream.MstreamConfig.Default, config => mstream.SetAsync(config, cancellationToken));
        var transcoding = scope.GetRequiredService<Media.TranscodingSettings>();
        await Refresh(SettingKeys.Transcoding, Media.TranscodingConfig.Default, config => transcoding.SetAsync(config, cancellationToken));
        var streaming = scope.GetRequiredService<Media.StreamingDefaultsSettings>();
        await Refresh(SettingKeys.StreamingDefaults, Media.StreamingDefaults.Default, config => streaming.SetAsync(config, cancellationToken));
        var remote = scope.GetRequiredService<RemoteLibrariesSettings>();
        await Refresh(SettingKeys.RemoteLibraries, RemoteLibrariesConfig.Default, config => remote.SetAsync(config, cancellationToken));
        var ai = scope.GetRequiredService<Ai.AiSettings>();
        await Refresh(SettingKeys.Ai, Ai.AiConfig.Default, config => ai.SetAsync(config, cancellationToken));
        // Privacy turns off what it leaves out (online services, the Ultimate Cloud extension).
        await Refresh(SettingKeys.Privacy, Application.Server.PrivacySettings.Default,
            choice => scope.GetRequiredService<Application.Server.PrivacyService>().ApplyAsync(choice, cancellationToken));
        scope.GetService<Online.OnlineMonitor>()?.RequestRun();
    }

    /// <summary>
    /// Extensions and players: built-in ones switched on or off as there; the others installed from the marketplace when
    /// it has them, else from the other server; then its default player. One that cannot be brought is left out.
    /// </summary>
    private async Task InstallComponentsAsync(
        IServiceProvider scope, ArcanistImport import, ExportManifest manifest, ArcanistEndpoint source, CancellationToken cancellationToken)
    {
        var host = scope.GetRequiredService<Extensions.ExtensionHost>();
        var extensions = scope.GetRequiredService<ExtensionService>();
        var marketplace = scope.GetRequiredService<ExtensionMarketplaceService>();
        var installedExtensions = 0;
        foreach (var extension in manifest.Extensions)
        {
            try
            {
                var here = host.Find(extension.Id);
                if (here is null && !extension.BuiltIn)
                {
                    try
                    {
                        await marketplace.InstallAsync(new InstallMarketplaceExtensionInput(extension.Id, extension.Enabled), cancellationToken);
                    }
                    catch (ArcanistException e) when (extension.Package && e.Code is not ErrorCodes.ClosedSourceDisabled)
                    {
                        await using var package = await DownloadAsync(source, $"/extensions/{Uri.EscapeDataString(extension.Id)}/package", cancellationToken);
                        await extensions.InstallAsync(package, cancellationToken);
                        if (extension.Enabled)
                            await extensions.SetEnabledAsync(extension.Id, true, cancellationToken);
                    }

                    installedExtensions++;
                }
                else if (here is not null && !host.IsManaged(extension.Id) && host.IsEnabled(extension.Id) != extension.Enabled)
                    await extensions.SetEnabledAsync(extension.Id, extension.Enabled, cancellationToken);
            }
            catch (ArcanistException e)
            {
                logger.LogWarning("The extension {Extension} of {Source} was not brought: {Error}", extension.Id, manifest.ServerName, e.Code);
            }
        }

        var catalog = scope.GetRequiredService<FrontendCatalog>();
        var frontends = scope.GetRequiredService<FrontendService>();
        var players = scope.GetRequiredService<PlayerMarketplaceService>();
        var installedPlayers = 0;
        foreach (var player in manifest.Players.Where(p => !p.BuiltIn && catalog.Find(p.Id) is null))
        {
            try
            {
                try
                {
                    await players.InstallAsync(new InstallMarketplacePlayerInput(player.Id), cancellationToken);
                }
                catch (ArcanistException e) when (player.Package && e.Code is not ErrorCodes.ClosedSourceDisabled)
                {
                    await using var package = await DownloadAsync(source, $"/players/{Uri.EscapeDataString(player.Id)}/package", cancellationToken);
                    await frontends.InstallAsync(package, cancellationToken);
                }

                installedPlayers++;
            }
            catch (ArcanistException e)
            {
                logger.LogWarning("The web player {Player} of {Source} was not brought: {Error}", player.Id, manifest.ServerName, e.Code);
            }
        }

        if (manifest.DefaultPlayer is { } defaultPlayer && catalog.Find(defaultPlayer) is not null)
        {
            try
            {
                await frontends.SetDefaultAsync(defaultPlayer, cancellationToken);
            }
            catch (ArcanistException e)
            {
                logger.LogWarning("The default player {Player} could not be set: {Error}", defaultPlayer, e.Code);
            }
        }

        await UpdateAsync(import.Id, i =>
        {
            i.ExtensionsInstalled = installedExtensions;
            i.PlayersInstalled = installedPlayers;
        }, cancellationToken);
    }

    /// <summary>A package of the other server, in a temporary file deleted when closed.</summary>
    private async Task<Stream> DownloadAsync(ArcanistEndpoint source, string path, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(source, path, null, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var file = new FileStream(TemporaryPath(".zip"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await response.Content.CopyToAsync(file, cancellationToken);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Lists the libraries' files once (one row each), then copies those still pending.</summary>
    private async Task CopyAsync(ArcanistImport import, ArcanistEndpoint source, Dictionary<Guid, Guid> libraries, CancellationToken cancellationToken)
    {
        bool listed;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            listed = await db.ArcanistImportFiles.AnyAsync(f => f.ImportId == import.Id, cancellationToken);
        }

        if (!listed)
        {
            var total = 0;
            foreach (var (sourceLibrary, libraryId) in libraries)
            {
                var files = await client.GetAsync<List<ExportFile>>(source, $"/libraries/{sourceLibrary}/files", cancellationToken);
                foreach (var chunk in files.Chunk(1000))
                {
                    using (await maintenance.WorkAsync(cancellationToken))
                    {
                        await using var db = databases.CreateDbContext();
                        db.ArcanistImportFiles.AddRange(chunk.Select(f => new ArcanistImportFile
                        {
                            ImportId = import.Id, SourceLibraryId = sourceLibrary, LibraryId = libraryId, Path = f.Path, Size = f.Size,
                            Status = ArcanistImportFileStatus.Pending,
                        }));
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }

                total += files.Count;
            }

            await UpdateAsync(import.Id, i => i.FilesTotal = total, cancellationToken);
        }

        var targets = new Dictionary<Guid, Target>();
        var failuresInARow = 0;
        var copiedSinceScan = new Dictionary<Guid, int>();
        while (true)
        {
            List<ArcanistImportFile> files;
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                files = await db.ArcanistImportFiles.AsNoTracking()
                    .Where(f => f.ImportId == import.Id && f.Status == ArcanistImportFileStatus.Pending)
                    .OrderBy(f => f.LibraryId).ThenBy(f => f.Path)
                    .Take(100)
                    .ToListAsync(cancellationToken);
            }

            if (files.Count == 0)
                return;

            foreach (var file in files)
            {
                if (!targets.TryGetValue(file.LibraryId, out var target))
                    targets[file.LibraryId] = target = await TargetAsync(file.LibraryId, cancellationToken);

                var (status, reason, bytes) = await CopyOneAsync(source, target, file, cancellationToken);
                await SettleAsync(file, status, reason, bytes, cancellationToken);
                failuresInARow = status == ArcanistImportFileStatus.Failed ? failuresInARow + 1 : 0;
                if (failuresInARow >= MaxFailuresInARow)
                    throw new ArcanistException.Conflict(reason == ErrorCodes.WriteFailed ? ErrorCodes.WriteFailed : ErrorCodes.MigrationSourceUnreachable);

                if (status == ArcanistImportFileStatus.Copied && (copiedSinceScan[file.LibraryId] = copiedSinceScan.GetValueOrDefault(file.LibraryId) + 1) >= ScanEvery)
                {
                    // Long copies show up in the library as they go.
                    copiedSinceScan[file.LibraryId] = 0;
                    using (await maintenance.WorkAsync(cancellationToken))
                        await scans.EnqueueAsync(file.LibraryId, ScanKind.Incremental, import.RequestedByUserId, cancellationToken);
                }
            }
        }
    }

    private sealed record Target(Guid LibraryId, ILibraryStorage Storage, StorageLocation Location);

    private async Task<Target> TargetAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        LibrarySource source;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            source = await db.LibrarySources.AsNoTracking()
                         .Where(s => s.LibraryId == libraryId && s.Kind == LibrarySourceKind.Local)
                         .OrderBy(s => s.Id)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);
        }

        var storage = storages.For(source.Kind);
        var location = StorageLocation.From(source);
        if (!await storage.CanWriteAsync(location, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.ReadOnly);
        return new Target(libraryId, storage, location);
    }

    /// <summary>
    /// One file, to the same path in the library. Already there with the same size: present. Another file there: the
    /// copy gets " (2)" and so on, never overwriting.
    /// </summary>
    private async Task<(ArcanistImportFileStatus Status, string? Reason, long Bytes)> CopyOneAsync(
        ArcanistEndpoint source, Target target, ArcanistImportFile file, CancellationToken cancellationToken)
    {
        if (await SizeAsync(target, file.Path, cancellationToken) == file.Size)
            return (ArcanistImportFileStatus.Present, null, 0);

        var temporary = TemporaryPath(Path.GetExtension(file.Path));
        try
        {
            try
            {
                using var response = await client.SendAsync(source,
                    $"/libraries/{file.SourceLibraryId}/file?path={Uri.EscapeDataString(file.Path)}", null, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                await using var output = File.Create(temporary);
                await response.Content.CopyToAsync(output, cancellationToken);
            }
            catch (Exception e) when (e is ArcanistException or HttpRequestException or IOException
                                      || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Could not download {Path}: {Error}", file.Path, e.Message);
                return (ArcanistImportFileStatus.Failed, ErrorCodes.DownloadFailed, 0);
            }

            var length = new FileInfo(temporary).Length;
            if (length != file.Size)
                return (ArcanistImportFileStatus.Failed, ErrorCodes.DownloadFailed, 0);

            using (await locks.AcquireForWritingAsync(target.LibraryId, cancellationToken))
            {
                try
                {
                    var destination = file.Path;
                    for (var copy = 2; await target.Storage.ExistsAsync(target.Location, destination, cancellationToken); copy++)
                    {
                        if (await SizeAsync(target, destination, cancellationToken) == file.Size)
                            return (ArcanistImportFileStatus.Present, null, 0);
                        var extension = Path.GetExtension(file.Path);
                        destination = $"{file.Path[..^extension.Length]} ({copy}){extension}";
                    }

                    await target.Storage.ImportAsync(target.Location, destination, temporary, cancellationToken);
                    return (ArcanistImportFileStatus.Copied, null, length);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageConflictException or ArgumentException)
                {
                    logger.LogWarning(e, "Could not write {Path} into the library", file.Path);
                    return (ArcanistImportFileStatus.Failed, ErrorCodes.WriteFailed, 0);
                }
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>The size of a file of the (local) library, or null when there is none.</summary>
    private static Task<long?> SizeAsync(Target target, string path, CancellationToken cancellationToken)
    {
        try
        {
            var local = target.Storage.TryGetLocalPath(target.Location, path);
            return Task.FromResult(local is not null && File.Exists(local) ? new FileInfo(local).Length : (long?)null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult<long?>(null);
        }
    }

    private async Task WaitForScansAsync(IReadOnlyList<Guid> libraries, CancellationToken cancellationToken)
    {
        while (true)
        {
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                var busy = await db.ScanJobs.AnyAsync(j => libraries.Contains(j.LibraryId)
                                                           && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running), cancellationToken);
                if (!busy)
                    return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), time, cancellationToken);
        }
    }

    /// <summary>The users' data, matched to this server's songs; plays brought twice count once.</summary>
    private async Task ApplyUserDataAsync(
        ArcanistImport import, ExportManifest manifest, ArcanistEndpoint source, Dictionary<Guid, Guid> userMap, CancellationToken cancellationToken)
    {
        var data = await client.GetAsync<ExportUserData>(source, "/userdata?users=" + string.Join(',', userMap.Keys), cancellationToken);
        Dictionary<Guid, Guid> matches;
        Dictionary<string, Guid> local;
        await using (var db = databases.CreateDbContext())
        {
            matches = await UserDataApplier.MatchAsync(db, data.Catalog, cancellationToken);
            local = (await db.Users.AsNoTracking().Select(u => new { u.Id, u.NormalizedUserName }).ToListAsync(cancellationToken))
                .ToDictionary(u => u.NormalizedUserName, u => u.Id);
        }

        var byName = manifest.Users.Where(u => userMap.ContainsKey(u.Id)).ToDictionary(u => u.UserName, u => userMap[u.Id], StringComparer.Ordinal);
        Guid? UserOf(string name) => byName.TryGetValue(name, out var mapped) ? mapped : local.TryGetValue(TextNormalizer.Normalize(name), out var same) ? same : null;

        var counts = new UserDataCounts();
        var prefix = $"arcanist:{manifest.ServerId:N}:";
        foreach (var user in data.UserData.Users)
        {
            if (!userMap.TryGetValue(user.Id, out var userId))
                continue;
            using (await maintenance.WorkAsync(cancellationToken))
            {
                await using var db = databases.CreateDbContext();
                await UserDataApplier.ApplyUserAsync(db, counts, user, userId, matches, UserOf, prefix, "Arcanist: " + manifest.ServerName, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var wishes = 0;
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            foreach (var group in data.Wishes.Where(w => userMap.ContainsKey(w.UserId)).GroupBy(w => userMap[w.UserId]))
            {
                var have = (await db.WishListItems.AsNoTracking().Where(w => w.UserId == group.Key).Select(w => new { w.Title, w.Artist }).ToListAsync(cancellationToken))
                    .Select(w => WishKey(w.Title, w.Artist)).ToHashSet(StringComparer.Ordinal);
                foreach (var wish in group)
                {
                    if (!have.Add(WishKey(wish.Title, wish.Artist)))
                        continue;
                    db.WishListItems.Add(new WishListItem
                    {
                        UserId = group.Key, Title = wish.Title, Artist = wish.Artist, Album = wish.Album, DurationMs = wish.DurationMs,
                        RecordingMbid = wish.MusicBrainzRecordingId, AcoustId = wish.AcoustId, Note = wish.Note, CreatedAt = wish.CreatedAt,
                        SourceUrl = wish.SourceUrl,
                    });
                    wishes++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await UpdateAsync(import.Id, i =>
        {
            i.Ratings = counts.Ratings;
            i.PlayCounts = counts.PlayCounts;
            i.Plays = counts.Plays;
            i.Playlists = counts.Playlists;
            i.Unmatched = counts.Unmatched;
            i.Wishes = wishes;
        }, cancellationToken);
    }

    private static string WishKey(string title, string artist) => TextNormalizer.Normalize(title) + '\u001f' + TextNormalizer.Normalize(artist);

    private async Task SettleAsync(ArcanistImportFile file, ArcanistImportFileStatus status, string? reason, long bytes, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            await db.ArcanistImportFiles.Where(f => f.Id == file.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, status).SetProperty(f => f.Reason, reason), cancellationToken);
            await db.ArcanistImports.Where(i => i.Id == file.ImportId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.FilesCopied, i => i.FilesCopied + (status == ArcanistImportFileStatus.Copied ? 1 : 0))
                    .SetProperty(i => i.FilesPresent, i => i.FilesPresent + (status == ArcanistImportFileStatus.Present ? 1 : 0))
                    .SetProperty(i => i.FilesFailed, i => i.FilesFailed + (status == ArcanistImportFileStatus.Failed ? 1 : 0))
                    .SetProperty(i => i.CopiedBytes, i => i.CopiedBytes + bytes), cancellationToken);
        }
    }

    private async Task<ArcanistImport> UpdateAsync(Guid importId, Action<ArcanistImport> change, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
        {
            await using var db = databases.CreateDbContext();
            var import = await db.ArcanistImports.SingleAsync(i => i.Id == importId, cancellationToken);
            change(import);
            await db.SaveChangesAsync(cancellationToken);
            return import;
        }
    }

    private async Task FinishAsync(Guid importId, ArcanistImportStatus status, string? errorCode)
    {
        using (await maintenance.WorkAsync(CancellationToken.None))
        {
            await using var db = databases.CreateDbContext();
            var import = await db.ArcanistImports.SingleOrDefaultAsync(i => i.Id == importId);
            if (import is null)
                return;
            if (status != ArcanistImportStatus.Completed && import.FilesCopied > 0)
            {
                foreach (var libraryId in (JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(import.LibraryMap) ?? []).Values.Distinct())
                    await scans.EnqueueAsync(libraryId, ScanKind.Incremental, import.RequestedByUserId, CancellationToken.None);
            }

            import.Status = status;
            import.ErrorCode = errorCode;
            import.FinishedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }
    }

    private string TemporaryPath(string extension)
    {
        var folder = Path.Combine(paths.Cache, "import");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"arcanist-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>Leftovers of a stop in the middle of a download.</summary>
    private void DeleteTemporaryFiles()
    {
        try
        {
            var folder = Path.Combine(paths.Cache, "import");
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "arcanist-*"))
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Could not delete the temporary files of migrations");
        }
    }
}
