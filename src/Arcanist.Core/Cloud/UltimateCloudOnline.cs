using System.Text.Json.Nodes;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>
/// The linked account's online services this server takes part in, each one on or off
/// (<see cref="SettingKeys.UltimateCloudServices"/>). Anonymous statistics are apart: a server setting
/// (<see cref="SettingKeys.ShareAnonymousStatistics"/>), asked by the setup wizard, that works without an account.
/// </summary>
/// <param name="PersonalPlays">The plays of the administrator who linked the account count as theirs: personal statistics and rankings.</param>
/// <param name="AlbumPlaythroughs">Their albums listened to from start to finish feed the album rankings.</param>
/// <param name="StarTracks">The stars of the administrator who linked the account are their StarTracks, shared with their Ultimate apps.</param>
/// <param name="Metrics">The account's statistics are shown in the panel.</param>
public sealed record UltimateCloudServicesConfig(bool PersonalPlays, bool AlbumPlaythroughs, bool StarTracks, bool Metrics);

/// <summary>The services of the server's account, or of a user's own (<c>user</c>).</summary>
public sealed class UltimateCloudServicesSettings(SettingsStore settings)
{
    /// <summary>The account's choices; until it makes them, everything on (plays and albums off with no telemetry).</summary>
    public async Task<UltimateCloudServicesConfig> GetAsync(CancellationToken cancellationToken, Guid? user = null)
    {
        if (await settings.GetAsync<UltimateCloudServicesConfig?>(Key(user), null, cancellationToken) is { } chosen)
            return chosen;
        var quiet = (await settings.GetAsync(SettingKeys.Privacy, Application.Server.PrivacySettings.Default, cancellationToken)).NoTelemetry;
        return new UltimateCloudServicesConfig(!quiet, !quiet, true, true);
    }

    public Task SetAsync(UltimateCloudServicesConfig config, CancellationToken cancellationToken, Guid? user = null) =>
        settings.SetAsync(Key(user), config, cancellationToken);

    private static string Key(Guid? user) => user is { } id ? SettingKeys.UltimateCloudUser(id, "services") : SettingKeys.UltimateCloudServices;
}

/// <summary>
/// Feeds Ultimate Cloud: with a linked account, the linking administrator's plays as their own, their albums heard
/// from start to finish and their stars as StarTracks (both ways); and, with anonymous statistics on (with or without
/// an account), everyone else's plays and albums heard whole, anonymously, for the global rankings. Plays are sent
/// from where it left off (it starts with the plays made once it is on, never the history); only tracks identified by
/// AcoustID take part, as in every Ultimate app. Nothing while the Ultimate Cloud extension is off. Users who linked
/// their own account send theirs to it, with the services they chose (the server's account then is not theirs).
/// </summary>
public sealed class UltimateCloudReporter(
    UltimateCloudClient client,
    UltimateCloudAuth auth,
    UltimateCloudAccount account,
    UserCloudAccounts accounts,
    UltimateCloudServicesSettings services,
    SettingsStore settings,
    ListeningSignals signals,
    IArcanistDbContextFactory databases,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<SetupOptions> setup,
    TimeProvider time,
    ILogger<UltimateCloudReporter> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int Batch = 200;

    /// <summary>Stars are compared with the account's at least this often (and whenever either side changes).</summary>
    public static readonly TimeSpan StarInterval = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly SemaphoreSlim _run = new(1, 1);
    private DateTime _starsCheckedAt = DateTime.MinValue;
    private bool _starsDue = true;

    /// <summary>Ultimate Cloud says the account's StarTracks changed.</summary>
    public void StarsChanged()
    {
        _starsDue = true;
        Wake();
    }

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        signals.Rated += _ => StarsChanged();
        signals.Played += Wake;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured && account.Enabled && account.IsConfigured)
                    await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (UltimateCloudException e)
            {
                logger.LogDebug("Ultimate Cloud's online services wait: {Error}", e.Code);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Reporting to Ultimate Cloud failed");
            }

            try
            {
                await _wake.WaitAsync(Interval, stoppingToken);
                // Plays come in bursts (a player catching up): let them gather.
                await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: the plays made since the last, then the stars when due.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _run.WaitAsync(cancellationToken);
        try
        {
            await RunOnceAsync(cancellationToken);
        }
        finally
        {
            _run.Release();
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!account.Enabled || !account.IsConfigured)
            return;
        var linked = account.Tier != CloudTier.None;
        var anonymous = await settings.GetAsync(SettingKeys.ShareAnonymousStatistics, false, cancellationToken)
                        && !(await settings.GetAsync(SettingKeys.Privacy, Application.Server.PrivacySettings.Default, cancellationToken)).NoTelemetry;
        var config = await services.GetAsync(cancellationToken);
        var linker = linked ? await LinkerAsync(cancellationToken) : null;
        var own = new Dictionary<Guid, UltimateCloudServicesConfig>();
        foreach (var user in accounts.Linked.Where(u => accounts.TierOf(u) != CloudTier.None))
            own[user] = await services.GetAsync(cancellationToken, user);
        // The cursor moves even with nothing to send: turning something on later never sends the plays before.
        await ReportPlaysAsync(config, linker, own, anonymous, cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        if (_starsDue || now - _starsCheckedAt > StarInterval)
        {
            _starsDue = false;
            _starsCheckedAt = now;
            foreach (var (user, userConfig) in own.Where(o => o.Value.StarTracks))
            {
                try
                {
                    await SyncStarsAsync(user, cancellationToken, user);
                }
                catch (UltimateCloudException e)
                {
                    logger.LogDebug("The StarTracks of user {User} wait: {Error}", user, e.Code);
                }
            }

            // The server's account is the linking administrator's, unless they linked their own.
            if (linked && config.StarTracks && linker is { } admin && !own.ContainsKey(admin))
                await SyncStarsAsync(admin, cancellationToken);
        }
    }

    /// <summary>The administrator who linked the account (or the first one), whose plays and stars are the account's.</summary>
    private async Task<Guid?> LinkerAsync(CancellationToken cancellationToken)
    {
        if (await auth.LinkAsync(cancellationToken) is not { } stored)
            return null;
        await using var db = databases.CreateDbContext();
        if (stored.LinkedByUserId is { } id && await db.Users.AnyAsync(u => u.Id == id && !u.IsDisabled, cancellationToken))
            return id;
        return await db.Users.Where(u => u.Role == UserRole.Admin && !u.IsDisabled).OrderBy(u => u.CreatedAt).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
    }

    // ── Plays ────────────────────────────────────────────────────────────────

    private sealed record PlayRow(
        Guid Id, Guid UserId, Guid? TrackId, DateTime PlayedAt, Guid? AcoustId, Guid? RecordingMbid, string? Title, string? Artist,
        Guid? ArtistMbid, string? Album, string? AlbumArtist, Guid? AlbumId, string? Genre);

    private async Task ReportPlaysAsync(
        UltimateCloudServicesConfig config, Guid? linker, IReadOnlyDictionary<Guid, UltimateCloudServicesConfig> own, bool anonymous, CancellationToken cancellationToken)
    {
        var cursor = await settings.GetAsync<Guid?>(SettingKeys.UltimateCloudPlaysCursor, null, cancellationToken);
        await using var db = databases.CreateDbContext();
        if (cursor is null)
        {
            // From now on: the plays made before are not sent.
            var last = await db.PlayEvents.OrderByDescending(e => e.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
            await SaveCursorAsync(last ?? Guid.Empty, cancellationToken);
            return;
        }

        while (true)
        {
            var after = cursor.Value;
            var rows = await db.PlayEvents.AsNoTracking()
                .Where(e => e.Id.CompareTo(after) > 0)
                .OrderBy(e => e.Id)
                .Take(Batch)
                .Select(e => new PlayRow(
                    e.Id, e.UserId, e.Counted ? e.TrackId : null, e.PlayedAt,
                    e.Track!.Recording.AcoustId,
                    e.Track.Recording.MusicBrainzId,
                    e.Track.Title,
                    e.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
                    e.Track.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.MusicBrainzId).FirstOrDefault(),
                    e.Track.Album.Title,
                    e.Track.Album.AlbumArtist.Name,
                    e.Track.AlbumId,
                    e.Track.Genres.Select(g => g.Genre.Name).FirstOrDefault()))
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return;

            // The anonymous ones go together; before one of the linker's (sent alone), those before it are sent.
            var plays = new List<(Guid Id, object Input)>();
            var albums = new List<object>();
            async Task FlushAsync()
            {
                if (plays.Count == 0 && albums.Count == 0)
                    return;
                await SendAsync(
                    "mutation Anonymous($p: [SharedPlayInput!]!, $a: [AnonymousAlbumInput!]) { recordAnonymousPlays(plays: $p, albums: $a) }",
                    new { p = plays.Select(s => s.Input).ToList(), a = albums }, cancellationToken, anonymous: true);
                if (plays.Count > 0)
                    await SaveCursorAsync(plays[^1].Id, cancellationToken);
                plays.Clear();
                albums.Clear();
            }

            foreach (var play in rows)
            {
                // Only counted plays of identified tracks; skips and the unidentified are passed over.
                if (play.TrackId is null || play.AcoustId is not { } acoustId)
                    continue;

                // Whose account the play is: the user's own, or the server's when they linked it.
                var (playAccount, playConfig) = own.TryGetValue(play.UserId, out var userConfig) ? ((Guid?)play.UserId, userConfig)
                    : linker is not null && play.UserId == linker ? (null, config)
                    : ((Guid?)null, (UltimateCloudServicesConfig?)null);
                if (playConfig is { PersonalPlays: true })
                {
                    // The account's own: its statistics and the rankings.
                    await FlushAsync();
                    using var as_ = UltimateCloudClient.As(playAccount);
                    await SendAsync(
                        """
                        mutation Play($a: String!, $title: String, $artist: String, $album: String, $genre: String, $rec: String, $art: String, $at: DateTime) {
                          recordPlay(acoustId: $a, title: $title, artist: $artist, album: $album, genre: $genre, musicBrainzRecordingId: $rec, musicBrainzArtistId: $art, playedAt: $at)
                        }
                        """,
                        new
                        {
                            a = acoustId.ToString(), title = play.Title, artist = play.Artist, album = play.Album, genre = play.Genre,
                            rec = play.RecordingMbid?.ToString(), art = play.ArtistMbid?.ToString(), at = play.PlayedAt,
                        },
                        cancellationToken);
                    if (playConfig.AlbumPlaythroughs && await PlaythroughAsync(db, play, cancellationToken) is { } whole)
                    {
                        await SendAsync("mutation Album($artist: String!, $album: String!) { recordAlbumPlaythrough(artist: $artist, album: $album) }",
                            new { artist = whole.Artist, album = whole.Album }, cancellationToken);
                    }

                    await SaveCursorAsync(play.Id, cancellationToken);
                }
                else if (anonymous)
                {
                    plays.Add((play.Id, new
                    {
                        acoustId = acoustId.ToString(), playedAt = play.PlayedAt, title = play.Title, artist = play.Artist, album = play.Album,
                        genre = play.Genre, musicBrainzRecordingId = play.RecordingMbid?.ToString(), musicBrainzArtistId = play.ArtistMbid?.ToString(),
                    }));
                    if (await PlaythroughAsync(db, play, cancellationToken) is { } whole)
                        albums.Add(new { artist = whole.Artist, album = whole.Album });
                }
            }

            await FlushAsync();
            cursor = rows[^1].Id;
            await SaveCursorAsync(cursor.Value, cancellationToken);
            if (rows.Count < Batch)
                return;
        }
    }

    /// <summary>
    /// A play of an album's last track that ends the whole album heard in order (the user's plays before it are its
    /// other tracks, from the first): Ultimate Cloud's album rankings count it as listened from start to finish.
    /// </summary>
    private static async Task<(string Artist, string Album)?> PlaythroughAsync(ArcanistDbContext db, PlayRow play, CancellationToken cancellationToken)
    {
        if (play.AlbumId is not { } albumId || play.Album is null)
            return null;
        var album = await db.Tracks.AsNoTracking()
            .Where(t => t.AlbumId == albumId && t.Files.Any(f => f.MissingSince == null))
            .OrderBy(t => t.DiscNumber ?? 1).ThenBy(t => t.TrackNumber).ThenBy(t => t.NormalizedTitle)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        if (album.Count < 2 || album[^1] != play.TrackId)
            return null;

        var heard = await db.PlayEvents.AsNoTracking()
            .Where(e => e.UserId == play.UserId && e.Counted && e.PlayedAt <= play.PlayedAt && e.Id != play.Id)
            .OrderByDescending(e => e.PlayedAt)
            .Take(album.Count - 1)
            .Select(e => e.TrackId)
            .ToListAsync(cancellationToken);
        heard.Reverse();
        return heard.SequenceEqual(album.Take(album.Count - 1).Select(id => (Guid?)id))
            ? (play.AlbumArtist ?? play.Artist ?? "", play.Album)
            : null;
    }

    private Task SaveCursorAsync(Guid cursor, CancellationToken cancellationToken) =>
        Unit(() => settings.SetAsync<Guid?>(SettingKeys.UltimateCloudPlaysCursor, cursor, cancellationToken), cancellationToken);

    private async Task Unit(Func<Task> work, CancellationToken cancellationToken)
    {
        using (await maintenance.WorkAsync(cancellationToken))
            await work();
    }

    /// <summary>Sends to Ultimate Cloud; anything but not reaching it (or the link being gone) is logged and passed over.</summary>
    private async Task<JsonNode?> SendAsync(string query, object variables, CancellationToken cancellationToken, bool anonymous = false)
    {
        try
        {
            return anonymous
                ? await client.SendAnonymousAsync(query, variables, cancellationToken)
                : await client.SendAsync(query, variables, cancellationToken);
        }
        catch (UltimateCloudException e) when (e.Code is not (Application.ErrorCodes.CloudUnreachable or Application.ErrorCodes.CloudLinkExpired
                                                   or Application.ErrorCodes.CloudNotLinked or Application.ErrorCodes.CloudNotConfigured))
        {
            logger.LogWarning("Ultimate Cloud refused a report: {Error} ({Message})", e.Code, e.Message);
            return null;
        }
    }

    // ── StarTracks ───────────────────────────────────────────────────────────

    /// <summary>
    /// The linking administrator's stars and the account's StarTracks, merged both ways against what both last agreed
    /// on: a star added or removed on one side is added or removed on the other. Only tracks with an AcoustID take
    /// part; StarTracks of songs this server does not have stay as they are.
    /// </summary>
    /// <param name="account">A user's own account (theirs); the server's with null.</param>
    public async Task SyncStarsAsync(Guid user, CancellationToken cancellationToken, Guid? account = null)
    {
        using var as_ = UltimateCloudClient.As(account);
        var agreedKey = account is { } owner ? SettingKeys.UltimateCloudUser(owner, "stars") : SettingKeys.UltimateCloudStars;
        var data = await client.SendAsync("query Stars { starTracks { acoustId } }", null, cancellationToken);
        var cloud = (data["starTracks"]?.AsArray() ?? [])
            .Select(s => Guid.TryParse(s?["acoustId"]?.GetValue<string>(), out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .ToHashSet();
        var agreed = (await settings.GetAsync<List<Guid>?>(agreedKey, null, cancellationToken))?.ToHashSet();

        await using var db = databases.CreateDbContext();
        var local = (await db.TrackRatings.AsNoTracking()
                .Where(r => r.UserId == user && r.Rating == 5 && r.Track.Recording.AcoustId != null)
                .Select(r => r.Track.Recording.AcoustId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var candidates = cloud.Concat(agreed ?? []).Except(local).ToList();
        var known = (await db.Tracks.AsNoTracking()
                .Where(t => t.Recording.AcoustId != null && candidates.Contains(t.Recording.AcoustId.Value) && t.Files.Any(f => f.MissingSince == null))
                .Select(t => t.Recording.AcoustId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Concat(local)
            .ToHashSet();

        HashSet<Guid> addToCloud, removeFromCloud, starHere, unstarHere;
        if (agreed is null)
        {
            // The first time: both sides keep everything they have.
            addToCloud = [.. local.Except(cloud)];
            removeFromCloud = [];
            starHere = [.. cloud.Intersect(known).Except(local)];
            unstarHere = [];
        }
        else
        {
            addToCloud = [.. local.Except(agreed).Except(cloud)];
            removeFromCloud = [.. agreed.Intersect(known).Except(local).Intersect(cloud)];
            starHere = [.. cloud.Intersect(known).Except(agreed).Except(local)];
            unstarHere = [.. agreed.Except(cloud).Intersect(local)];
        }

        if (addToCloud.Count > 0)
            await ChangeCloudStarsAsync("addStarTracks", addToCloud, db, cancellationToken);
        if (removeFromCloud.Count > 0)
            await ChangeCloudStarsAsync("removeStarTracks", removeFromCloud, db, cancellationToken);

        if (starHere.Count > 0 || unstarHere.Count > 0)
        {
            using (await maintenance.WorkAsync(cancellationToken))
            {
                if (unstarHere.Count > 0)
                {
                    var unstar = unstarHere.ToList();
                    var unstarTracks = await db.Tracks.Where(t => t.Recording.AcoustId != null && unstar.Contains(t.Recording.AcoustId.Value))
                        .Select(t => t.Id).ToListAsync(cancellationToken);
                    await db.TrackRatings.Where(r => r.UserId == user && r.Rating == 5 && unstarTracks.Contains(r.TrackId))
                        .ExecuteDeleteAsync(cancellationToken);
                }

                if (starHere.Count > 0)
                {
                    // One track per song: the one that came first.
                    var star = starHere.ToList();
                    var tracks = (await db.Tracks.AsNoTracking()
                            .Where(t => t.Recording.AcoustId != null && star.Contains(t.Recording.AcoustId.Value) && t.Files.Any(f => f.MissingSince == null))
                            .Select(t => new { t.Id, AcoustId = t.Recording.AcoustId!.Value, t.AddedAt })
                            .ToListAsync(cancellationToken))
                        .GroupBy(t => t.AcoustId)
                        .Select(g => g.OrderBy(t => t.AddedAt).First().Id)
                        .ToList();
                    var ratings = await db.TrackRatings.Where(r => r.UserId == user && tracks.Contains(r.TrackId)).ToDictionaryAsync(r => r.TrackId, cancellationToken);
                    var now = time.GetUtcNow().UtcDateTime;
                    foreach (var trackId in tracks)
                    {
                        if (ratings.TryGetValue(trackId, out var rating))
                        {
                            rating.Rating = 5;
                            rating.RatedAt = now;
                        }
                        else
                            db.TrackRatings.Add(new TrackRating { UserId = user, TrackId = trackId, Rating = 5, RatedAt = now });
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            // Apps following the user's StarTracks hear of it.
            signals.OnRated(user);
        }

        var localNow = new HashSet<Guid>(local);
        localNow.ExceptWith(unstarHere);
        localNow.UnionWith(starHere);
        var cloudNow = new HashSet<Guid>(cloud);
        cloudNow.ExceptWith(removeFromCloud);
        cloudNow.UnionWith(addToCloud);
        await Unit(() => settings.SetAsync(agreedKey, localNow.Intersect(cloudNow).Order().ToList(), cancellationToken), cancellationToken);
        if (addToCloud.Count + removeFromCloud.Count + starHere.Count + unstarHere.Count > 0)
            logger.LogInformation("StarTracks synced: {Added} up, {Removed} removed there, {Starred} starred here, {Unstarred} unstarred here",
                addToCloud.Count, removeFromCloud.Count, starHere.Count, unstarHere.Count);
    }

    private async Task ChangeCloudStarsAsync(string mutation, IReadOnlyCollection<Guid> acoustIds, ArcanistDbContext db, CancellationToken cancellationToken)
    {
        var ids = acoustIds.ToList();
        var described = (await db.Tracks.AsNoTracking()
                .Where(t => t.Recording.AcoustId != null && ids.Contains(t.Recording.AcoustId.Value))
                .Select(t => new
                {
                    AcoustId = t.Recording.AcoustId!.Value,
                    t.Title,
                    Artist = t.Artists.Where(a => a.Role == ArtistRole.Primary).OrderBy(a => a.Position).Select(a => a.Artist.Name).FirstOrDefault(),
                    Album = t.Album.Title,
                    t.DurationMs,
                })
                .ToListAsync(cancellationToken))
            .DistinctBy(t => t.AcoustId)
            .ToDictionary(t => t.AcoustId);

        foreach (var chunk in ids.Chunk(1000))
        {
            var items = chunk.Select(id => described.TryGetValue(id, out var t)
                    ? (object)new { acoustId = id.ToString(), title = t.Title, artist = t.Artist, album = t.Album, durationSeconds = t.DurationMs / 1000.0 }
                    : new { acoustId = id.ToString() })
                .ToList();
            await client.SendAsync($"mutation Stars($items: [StarTrackInput!]!) {{ {mutation}(items: $items) }}", new { items }, cancellationToken);
        }
    }
}
