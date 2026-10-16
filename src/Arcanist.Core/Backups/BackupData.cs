using System.Data;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Organization;
using Arcanist.Core.Scanning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Backups;

// What a snapshot holds besides its files, in snapshots/<snapshot>/ (see docs/backups.md). Tracks are referred to by
// their id on the server that made the backup; restoring matches them by their files' audio hash, MusicBrainz id,
// AcoustID or tags, as the catalog describes them.

/// <summary><c>catalog.json.gz</c>: the tracks of the snapshot (and of its user data), to recognize them on restore.</summary>
internal sealed record BackupCatalog(int Format, List<BackupCatalogTrack> Tracks, List<BackupCatalogAlbum> Albums);

/// <param name="Files">Its files in the snapshot (relative to <c>current/</c>), best first.</param>
/// <param name="Lyrics">Lyrics found online (those of files and sidecars are in the files).</param>
internal sealed record BackupCatalogTrack(
    Guid Id,
    string Title,
    List<string> Artists,
    Guid AlbumId,
    int? Disc,
    int? Number,
    int DurationMs,
    Guid? MusicBrainzId,
    Guid? AcoustId,
    List<string> ContentHashes,
    List<string> Files,
    BackupLyrics? Lyrics);

internal sealed record BackupLyrics(string? Plain, string? Synced, bool Instrumental);

/// <param name="Artwork">Its artwork in <c>artwork/</c> of the backup.</param>
internal sealed record BackupCatalogAlbum(Guid Id, string Title, string Artist, int? Year, Guid? MusicBrainzId, string? Artwork);

/// <summary><c>userdata.json.gz</c>: ratings, play counts, history and playlists of the snapshot's music.</summary>
internal sealed record BackupUserData(int Format, List<BackupUser> Users);

internal sealed record BackupUser(
    Guid Id,
    string UserName,
    string? DisplayName,
    List<BackupRating> Ratings,
    List<BackupPlayCount> PlayCounts,
    List<BackupPlay> Plays,
    List<BackupPlaylist> Playlists);

internal sealed record BackupRating(Guid Track, int Rating, DateTime RatedAt);

internal sealed record BackupPlayCount(Guid Track, int PlayCount, int SkipCount, DateTime? LastPlayedAt);

/// <param name="Id">The play's id on the server that made the backup, so restoring twice adds it once.</param>
internal sealed record BackupPlay(Guid Id, Guid Track, DateTime PlayedAt, int PlayedMs, bool Counted, bool Skipped, string? Client);

/// <param name="Collaborators">Their user names.</param>
internal sealed record BackupPlaylist(
    Guid Id,
    string Name,
    string? Description,
    bool IsPublic,
    DateTime CreatedAt,
    List<string> Collaborators,
    List<BackupPlaylistEntry> Entries);

internal sealed record BackupPlaylistEntry(Guid Track, DateTime AddedAt, string? AddedBy);

/// <summary>
/// The catalog of the snapshot's music, the artwork that is not in its folders (found online, or embedded), and,
/// when the backup takes them, its users' data and playlists (also as <c>.m3u8</c> pointing into <c>current/</c>).
/// </summary>
internal sealed class BackupDataExtra(IArcanistDbContextFactory databases, ArtworkStore artworkStore) : IBackupExtra
{
    public const string CatalogFile = "catalog.json.gz";
    public const string UserDataFile = "userdata.json.gz";
    public const string PlaylistsFolder = "playlists";
    public const string ArtworkFolder = "artwork";

    public async Task<(bool UserData, bool Server)> WriteAsync(BackupSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var plan = snapshot.Plan;

        // The snapshot's audio files, by track: what the catalog describes, and what playlists point to.
        var inSnapshot = snapshot.Index.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var sourceIds = snapshot.SourceFolders.Keys.ToList();
        var files = (await db.MediaFiles.AsNoTracking()
                .Where(f => sourceIds.Contains(f.LibrarySourceId) && f.MissingSince == null)
                .Select(f => new { f.TrackId, f.LibrarySourceId, f.RelativePath, f.ContentHash, f.BitDepth, f.SampleRate, f.BitRate })
                .ToListAsync(cancellationToken))
            .Select(f => new { f.TrackId, f.ContentHash, f.BitDepth, f.SampleRate, f.BitRate, Path = $"{snapshot.SourceFolders[f.LibrarySourceId]}/{f.RelativePath}" })
            .Where(f => inSnapshot.Contains(f.Path))
            .ToList();
        var filesByTrack = files.GroupBy(f => f.TrackId).ToDictionary(g => g.Key,
            g => g.OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.SampleRate ?? 0).ThenByDescending(f => f.BitRate ?? 0).ToList());
        var tracks = filesByTrack.Keys.ToHashSet();

        BackupUserData? userData = null;
        if (plan.IncludeUserData)
            userData = await UserDataAsync(db, plan, tracks, cancellationToken);

        // The catalog also describes the tracks the user data refers to beyond the snapshot's files.
        var described = tracks.ToHashSet();
        if (userData is not null)
        {
            foreach (var user in userData.Users)
            {
                described.UnionWith(user.Ratings.Select(r => r.Track));
                described.UnionWith(user.PlayCounts.Select(c => c.Track));
                described.UnionWith(user.Plays.Select(p => p.Track));
                described.UnionWith(user.Playlists.SelectMany(p => p.Entries).Select(e => e.Track));
            }
        }

        var catalog = await CatalogAsync(db, snapshot, described, filesByTrack.ToDictionary(t => t.Key, t => t.Value.Select(f => (f.Path, f.ContentHash)).ToList()), cancellationToken);
        await BackupFormat.WriteAsync(snapshot.Target, BackupPaths.Join(snapshot.SnapshotFolder, CatalogFile), catalog, cancellationToken);

        if (userData is null)
            return (false, false);
        await BackupFormat.WriteAsync(snapshot.Target, BackupPaths.Join(snapshot.SnapshotFolder, UserDataFile), userData, cancellationToken);
        await PlaylistFilesAsync(snapshot, userData, catalog, cancellationToken);
        return (true, false);
    }

    /// <summary>
    /// Whose data: everyone's on the tracks of the snapshot (all of it for a server backup), with the playlists of the
    /// backup (a playlist backup), those holding its tracks (a library backup) or all (a server backup).
    /// </summary>
    private static async Task<BackupUserData> UserDataAsync(ArcanistDbContext db, BackupPlan plan, HashSet<Guid> tracks, CancellationToken cancellationToken)
    {
        var all = plan.ScopeKind == BackupScope.Server;
        var trackIds = tracks.ToList();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).Select(u => new { u.Id, u.UserName, u.DisplayName }).ToListAsync(cancellationToken);
        var names = users.ToDictionary(u => u.Id, u => u.UserName);

        var ratings = await db.TrackRatings.AsNoTracking().Where(r => all || trackIds.Contains(r.TrackId)).ToListAsync(cancellationToken);
        var counts = await db.TrackPlayStats.AsNoTracking().Where(s => all || trackIds.Contains(s.TrackId)).ToListAsync(cancellationToken);
        var plays = await db.PlayEvents.AsNoTracking().Where(p => p.TrackId != null && (all || trackIds.Contains(p.TrackId.Value)))
            .OrderBy(p => p.PlayedAt).ToListAsync(cancellationToken);

        var playlistIds = plan.ScopeKind switch
        {
            BackupScope.Server => await db.Playlists.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken),
            BackupScope.Playlist => plan.ScopeIds.ToList(),
            BackupScope.Library => await db.PlaylistEntries.AsNoTracking().Where(e => trackIds.Contains(e.TrackId)).Select(e => e.PlaylistId).Distinct().ToListAsync(cancellationToken),
            _ => [],
        };
        var playlists = await db.Playlists.AsNoTracking()
            .Where(p => playlistIds.Contains(p.Id))
            .Include(p => p.Entries)
            .Include(p => p.Collaborators)
            .ToListAsync(cancellationToken);

        return new BackupUserData(BackupFormat.Version, users.Select(u => new BackupUser(
            u.Id,
            u.UserName,
            u.DisplayName,
            ratings.Where(r => r.UserId == u.Id).Select(r => new BackupRating(r.TrackId, r.Rating, r.RatedAt)).ToList(),
            counts.Where(c => c.UserId == u.Id).Select(c => new BackupPlayCount(c.TrackId, c.PlayCount, c.SkipCount, c.LastPlayedAt)).ToList(),
            plays.Where(p => p.UserId == u.Id).Select(p => new BackupPlay(p.Id, p.TrackId!.Value, p.PlayedAt, p.PlayedMs, p.Counted, p.Skipped, p.Client)).ToList(),
            playlists.Where(p => p.OwnerId == u.Id).OrderBy(p => p.Name).Select(p => new BackupPlaylist(
                p.Id, p.Name, p.Description, p.IsPublic, p.CreatedAt,
                p.Collaborators.Select(c => names.GetValueOrDefault(c.UserId)).OfType<string>().ToList(),
                p.Entries.OrderBy(e => e.Position)
                    .Select(e => new BackupPlaylistEntry(e.TrackId, e.AddedAt, e.AddedByUserId is { } by ? names.GetValueOrDefault(by) : null)).ToList())).ToList()))
            .Where(u => u.Ratings.Count + u.PlayCounts.Count + u.Plays.Count + u.Playlists.Count > 0)
            .ToList());
    }

    private async Task<BackupCatalog> CatalogAsync(
        ArcanistDbContext db, BackupSnapshot snapshot, HashSet<Guid> described,
        Dictionary<Guid, List<(string Path, string ContentHash)>> files, CancellationToken cancellationToken)
    {
        var ids = described.ToList();
        var tracks = new List<BackupCatalogTrack>();
        var albumIds = new HashSet<Guid>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var rows = await db.Tracks.AsNoTracking()
                .Where(t => chunk.Contains(t.Id))
                .Select(t => new
                {
                    t.Id, t.Title, t.AlbumId, t.DiscNumber, t.TrackNumber, t.DurationMs,
                    t.Recording.MusicBrainzId, t.Recording.AcoustId,
                })
                .ToListAsync(cancellationToken);
            var artists = (await db.TrackArtists.AsNoTracking()
                    .Where(a => chunk.Contains(a.TrackId) && (a.Role == ArtistRole.Primary || a.Role == ArtistRole.Featured))
                    .Select(a => new { a.TrackId, a.Position, a.Artist.Name })
                    .ToListAsync(cancellationToken))
                .GroupBy(a => a.TrackId)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Position).Select(a => a.Name).ToList());
            var hashes = (await db.MediaFiles.AsNoTracking()
                    .Where(f => chunk.Contains(f.TrackId) && f.MissingSince == null)
                    .Select(f => new { f.TrackId, f.ContentHash })
                    .ToListAsync(cancellationToken))
                .GroupBy(f => f.TrackId)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ContentHash).Distinct().ToList());
            var lyrics = await db.Lyrics.AsNoTracking()
                .Where(l => chunk.Contains(l.TrackId) && l.Source == LyricsSource.Online)
                .ToDictionaryAsync(l => l.TrackId, cancellationToken);
            foreach (var row in rows)
            {
                albumIds.Add(row.AlbumId);
                var trackFiles = files.GetValueOrDefault(row.Id) ?? [];
                tracks.Add(new BackupCatalogTrack(
                    row.Id, row.Title, artists.GetValueOrDefault(row.Id) ?? [], row.AlbumId, row.DiscNumber, row.TrackNumber, row.DurationMs,
                    row.MusicBrainzId, row.AcoustId,
                    (hashes.GetValueOrDefault(row.Id) ?? []).Union(trackFiles.Select(f => f.ContentHash)).Distinct().ToList(),
                    trackFiles.Select(f => f.Path).ToList(),
                    lyrics.GetValueOrDefault(row.Id) is { } l ? new BackupLyrics(l.Plain, l.Synced, l.Instrumental) : null));
            }
        }

        var albums = new List<BackupCatalogAlbum>();
        foreach (var chunk in albumIds.Chunk(1000))
        {
            var rows = await db.Albums.AsNoTracking()
                .Where(a => chunk.Contains(a.Id))
                .Select(a => new { a.Id, a.Title, Artist = a.AlbumArtist.Name, a.Year, a.MusicBrainzReleaseId, Hash = a.Artwork != null ? a.Artwork.Hash : null, Mime = a.Artwork != null ? a.Artwork.MimeType : null })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                string? artwork = null;
                if (row.Hash is { } hash && row.Mime is { } mime)
                    artwork = await ArtworkAsync(snapshot, hash, mime, cancellationToken);
                albums.Add(new BackupCatalogAlbum(row.Id, row.Title, row.Artist, row.Year, row.MusicBrainzReleaseId, artwork));
            }
        }

        return new BackupCatalog(BackupFormat.Version, tracks.OrderBy(t => t.Id).ToList(), albums.OrderBy(a => a.Id).ToList());
    }

    /// <summary>The album's artwork in the backup's <c>artwork/</c>, written once (it is named after its hash).</summary>
    private async Task<string?> ArtworkAsync(BackupSnapshot snapshot, string hash, string mimeType, CancellationToken cancellationToken)
    {
        var source = artworkStore.PathFor(hash, mimeType);
        if (!File.Exists(source))
            return null;
        var name = hash + Path.GetExtension(source);
        var target = BackupPaths.Join(snapshot.BackupFolder, ArtworkFolder, name);
        if (await snapshot.Target.StatAsync(target, cancellationToken) is null)
        {
            await using var stream = File.OpenRead(source);
            await snapshot.Target.WriteAsync(target, stream, stream.Length, cancellationToken);
        }

        return name;
    }

    /// <summary>Each playlist as an <c>.m3u8</c> playing the snapshot's copies (of the songs it has).</summary>
    private static async Task PlaylistFilesAsync(BackupSnapshot snapshot, BackupUserData userData, BackupCatalog catalog, CancellationToken cancellationToken)
    {
        var tracks = catalog.Tracks.ToDictionary(t => t.Id);
        foreach (var user in userData.Users.Where(u => u.Playlists.Count > 0))
        {
            var owner = PathTemplate.SanitizeName(user.UserName);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var playlist in user.Playlists)
            {
                var name = PathTemplate.SanitizeName(playlist.Name);
                var file = name;
                for (var copy = 2; !taken.Add(file); copy++)
                    file = $"{name} ({copy})";

                var text = new StringBuilder("#EXTM3U\n");
                foreach (var entry in playlist.Entries)
                {
                    if (!tracks.TryGetValue(entry.Track, out var track) || track.Files.Count == 0)
                        continue;
                    var artist = string.Join(", ", track.Artists);
                    text.Append("#EXTINF:").Append(track.DurationMs / 1000).Append(',')
                        .Append(artist.Length > 0 ? $"{artist} - {track.Title}" : track.Title).Append('\n')
                        // From snapshots/<snapshot>/playlists/<owner>/ up to the backup's folder.
                        .Append("../../../../").Append(BackupFormat.Current).Append('/').Append(track.Files[0]).Append('\n');
                }

                var bytes = Encoding.UTF8.GetBytes(text.ToString());
                var path = BackupPaths.Join(snapshot.SnapshotFolder, PlaylistsFolder, owner, file + ".m3u8");
                await snapshot.Target.DeleteAsync(path, cancellationToken);
                await snapshot.Target.WriteAsync(path, new MemoryStream(bytes), bytes.Length, cancellationToken);
            }
        }
    }
}

/// <summary>
/// The server itself, for a backup that carries it: a consistent copy of the database (as SQLite, also from
/// PostgreSQL), <c>arcanist.json</c> and <c>keys/</c>, zipped and encrypted with the backup's password into
/// <c>server.arcb</c>.
/// </summary>
internal sealed class BackupServerExtra(
    IOptionsMonitor<DatabaseOptions> database,
    DatabaseInitializer initializer,
    ArcanistPaths paths,
    ILogger<BackupServerExtra> logger) : IBackupExtra
{
    public const string ServerFile = "server.arcb";
    public const string DatabaseEntry = "arcanist.db";
    public const string KeysEntry = "keys/";

    public async Task<(bool UserData, bool Server)> WriteAsync(BackupSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Plan.ServerPassword is not { } password)
            return (false, false);

        var folder = Path.Combine(paths.Cache, "backup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var databaseFile = Path.Combine(folder, "arcanist.db");
            await DatabaseAsync(databaseFile, cancellationToken);

            var zipFile = Path.Combine(folder, "server.zip");
            await using (var zipStream = File.Create(zipFile))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                await zip.CreateEntryFromFileAsync(databaseFile, DatabaseEntry, CompressionLevel.Optimal, cancellationToken);
                var settings = zip.CreateEntry(ArcanistPaths.SettingsFileName);
                await using (var writer = new StreamWriter(await settings.OpenAsync(cancellationToken)))
                    await writer.WriteAsync(SettingsForRestore());
                if (Directory.Exists(paths.Keys))
                {
                    foreach (var key in Directory.EnumerateFiles(paths.Keys, "*", SearchOption.AllDirectories))
                        await zip.CreateEntryFromFileAsync(key, KeysEntry + Path.GetRelativePath(paths.Keys, key).Replace(Path.DirectorySeparatorChar, '/'),
                            CompressionLevel.Optimal, cancellationToken);
                }
            }

            var encrypted = Path.Combine(folder, ServerFile);
            await using (var input = File.OpenRead(zipFile))
            await using (var output = File.Create(encrypted))
                await BackupCrypto.EncryptAsync(input, output, password, cancellationToken);

            var target = BackupPaths.Join(snapshot.SnapshotFolder, ServerFile);
            await snapshot.Target.DeleteAsync(target, cancellationToken);
            await using (var stream = File.OpenRead(encrypted))
                await snapshot.Target.WriteAsync(target, stream, stream.Length, cancellationToken);
            return (false, true);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException e)
            {
                logger.LogDebug(e, "Could not delete {Folder}", folder);
            }
        }
    }

    /// <summary>A consistent copy of the database as a SQLite file: SQLite's online backup, or PostgreSQL's tables in one snapshot.</summary>
    private async Task DatabaseAsync(string file, CancellationToken cancellationToken)
    {
        var options = database.CurrentValue;
        var copy = new DatabaseOptions
        {
            Provider = DatabaseProvider.Sqlite,
            ConnectionString = new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString(),
        };
        if (options.Provider == DatabaseProvider.Sqlite)
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder(
                string.IsNullOrWhiteSpace(options.ConnectionString) ? ArcanistDbContextFactory.DefaultSqliteConnectionString(paths) : options.ConnectionString)
            {
                Pooling = false,
            }.ToString());
            await using var target = new SqliteConnection(copy.ConnectionString);
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
            return;
        }

        await initializer.MigrateAsync(copy, cancellationToken);
        await using var from = ArcanistDbContextFactory.Create(options, paths);
        await using var transaction = await from.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var to = ArcanistDbContextFactory.Create(copy, paths))
        {
            foreach (var type in DatabaseMigrator.ModelOrder())
                await DatabaseMigrator.CopyTableAsync(type, from, to, (_, _) => { }, cancellationToken);
            await to.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary><c>arcanist.json</c> as a restored server reads it: SQLite in its data folder (PostgreSQL's password stays out).</summary>
    private string SettingsForRestore()
    {
        var root = File.Exists(paths.SettingsFile)
            ? JsonNode.Parse(File.ReadAllText(paths.SettingsFile), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip }) as JsonObject ?? []
            : [];
        var arcanist = root["Arcanist"] as JsonObject ?? (JsonObject)(root["Arcanist"] = new JsonObject());
        arcanist["Database"] = new JsonObject { ["Provider"] = nameof(DatabaseProvider.Sqlite) };
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
