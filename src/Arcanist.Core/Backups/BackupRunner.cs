using System.Security.Cryptography;
using Arcanist.Core.Application;
using Arcanist.Core.Backups.Targets;
using Arcanist.Core.Organization;
using Arcanist.Core.Storage;

namespace Arcanist.Core.Backups;

/// <summary>A library folder to back up, and which of its files (all when <see cref="Paths"/> is null).</summary>
internal sealed record BackupPlanSource(
    Guid SourceId,
    Guid LibraryId,
    string LibraryName,
    ILibraryStorage Storage,
    StorageLocation Location,
    IReadOnlySet<string>? Paths);

/// <summary>What one run copies.</summary>
/// <param name="ContentHashes">The catalog's audio hash of each file, by source and path.</param>
/// <param name="ServerPassword">The password of the server's encrypted part; null when the backup does not carry it.</param>
internal sealed record BackupPlan(
    Guid BackupId,
    string Name,
    string Folder,
    string Scope,
    int RetentionDays,
    IReadOnlyList<BackupPlanSource> Sources,
    IReadOnlyDictionary<(Guid SourceId, string Path), string> ContentHashes)
{
    public Data.Entities.BackupScope ScopeKind { get; init; }

    public IReadOnlyList<Guid> ScopeIds { get; init; } = [];

    public bool IncludeUserData { get; init; }

    public string? ServerPassword { get; init; }
}

/// <summary>A snapshot being written: where, for which plan, and what its files are.</summary>
/// <param name="SourceFolders">Each library folder's folder in <c>current/</c>.</param>
/// <param name="Index">The snapshot's files.</param>
internal sealed record BackupSnapshot(
    IBackupTarget Target,
    string BackupFolder,
    string SnapshotFolder,
    string Name,
    BackupPlan Plan,
    IReadOnlyDictionary<Guid, string> SourceFolders,
    IReadOnlyList<BackupIndexEntry> Index);

/// <summary>How far a run got.</summary>
internal sealed class BackupCounters
{
    public int Files;
    public int Copied;
    public int Unchanged;
    public int Versioned;
    public int Skipped;
    public int Failed;
    public long Bytes;

    public List<(string Path, string Code)> Failures { get; } = [];

    public void Fail(string path, string code)
    {
        Failed++;
        if (Failures.Count < 1000)
            Failures.Add((path, code));
    }
}

/// <summary>Things a snapshot holds besides the files: user data, playlists, the server's encrypted package.</summary>
internal interface IBackupExtra
{
    /// <summary>Writes its files into <c>snapshots/&lt;snapshot&gt;/</c>; returns what the manifest says it holds.</summary>
    Task<(bool UserData, bool Server)> WriteAsync(BackupSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>
/// Mirrors files into a destination with old versions kept: what changed is copied into <c>current/</c> after its
/// previous copy moves to <c>versions/&lt;snapshot&gt;/</c>, and what disappeared moves there too. Unchanged files
/// (same size and modification time, or same SHA-256) are not read. A run interrupted by a restart resumes from its
/// journal. Old versions go after the retention, with the snapshots that needed them.
/// </summary>
internal sealed class BackupRunner(ILogger logger, TimeProvider time)
{
    private const int JournalEvery = 100;
    private const int MaxConsecutiveWriteFailures = 5;
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs the backup into a snapshot named <paramref name="snapshot"/>, unless the destination holds an unfinished
    /// one (a run that failed, was cancelled or stopped): that one is finished instead, since its changes are already
    /// in <c>current/</c> and <c>versions/</c>. Returns the snapshot's name.
    /// </summary>
    public async Task<string> RunAsync(
        BackupPlan plan,
        IBackupTarget target,
        string snapshot,
        IReadOnlyList<IBackupExtra> extras,
        BackupCounters counters,
        Action<string> started,
        Action changed,
        CancellationToken cancellationToken)
    {
        if (await target.ProbeAsync(cancellationToken) is { } problem)
            throw new ArcanistException.Conflict(problem);

        var folder = plan.Folder;
        var manifestPath = BackupPaths.Join(folder, BackupFormat.ManifestFile);
        var now = time.GetUtcNow().UtcDateTime;
        var manifest = await BackupFormat.ReadReplacedAsync<BackupManifest>(target, manifestPath, cancellationToken)
                       ?? new BackupManifest
                       {
                           Id = plan.BackupId, Name = plan.Name, Scope = plan.Scope, ArcanistVersion = "", Machine = "", CreatedAt = now,
                       };
        if (manifest.Format > BackupFormat.Version)
            throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);

        snapshot = await UnfinishedSnapshotAsync(target, folder, manifest, cancellationToken) ?? Later(snapshot, manifest);
        started(snapshot);
        var folders = AssignFolders(manifest, plan.Sources);
        var previous = await LatestIndexAsync(target, folder, manifest, cancellationToken);
        var journal = await ReadJournalAsync(target, folder, snapshot, cancellationToken);
        if (journal.Parts == 0)
        {
            // Marks the snapshot as started before anything moves, so a stop before the first journal entry is
            // still finished by the next run (which then finds what moved in versions/).
            var marker = BackupPaths.Join(folder, BackupFormat.InProgress, snapshot, "000000.json");
            await target.DeleteAsync(marker, cancellationToken);
            await BackupFormat.WriteAsync(target, marker, new BackupJournal([], []), cancellationToken);
            journal.Parts = 1;
        }

        var run = new Run(plan, target, snapshot, counters, journal, changed);

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in plan.Sources)
        {
            var sourceFolder = folders[source.SourceId];
            var probe = await source.Storage.ProbeAsync(source.Location, cancellationToken);
            if (!probe.IsAvailable)
            {
                // A disconnected drive or share is not a deleted library: its last copy stays as it is.
                logger.LogWarning("Backup {Backup}: {Library} is not available, its last copy stays", plan.Name, source.LibraryName);
                counters.Fail(sourceFolder, ErrorCodes.SourceUnavailable);
                foreach (var entry in previous.Values.Where(e => e.Path.StartsWith(sourceFolder + "/", StringComparison.Ordinal)))
                    run.Keep(entry);
                continue;
            }

            available.Add(sourceFolder);
            await foreach (var file in source.Storage.EnumerateAsync(source.Location, cancellationToken))
            {
                if (source.Paths is { } paths && !paths.Contains(file.RelativePath))
                    continue;
                await BackUpFileAsync(run, source, sourceFolder, file, previous, cancellationToken);
            }
        }

        // What is gone from the libraries (or from the backup's scope) becomes an old version; what belongs to a
        // folder that could not be read stays.
        var sourceFolders = folders.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in previous.Values)
        {
            if (run.Seen.Contains(entry.Path))
                continue;
            var top = entry.Path[..Math.Max(0, entry.Path.IndexOf('/'))];
            if (sourceFolders.Contains(top) && !available.Contains(top))
                continue;
            await run.MoveToVersionAsync(entry.Path, cancellationToken);
            run.Seen.Add(entry.Path);
        }

        await run.FlushJournalAsync(cancellationToken);

        // The snapshot: its index, then what the extras add, then the manifest that lists it.
        var snapshotFolder = BackupPaths.Join(folder, BackupFormat.Snapshots, snapshot);
        await target.DeleteFolderAsync(snapshotFolder, cancellationToken);
        await BackupFormat.WriteAsync(target, BackupPaths.Join(snapshotFolder, BackupFormat.IndexFile),
            new BackupIndex(BackupFormat.Version, snapshot, run.Index, run.Moved.ToList()), cancellationToken);
        var userData = false;
        var server = false;
        var context = new BackupSnapshot(target, folder, snapshotFolder, snapshot, plan, folders, run.Index);
        foreach (var extra in extras)
        {
            var (writesUserData, writesServer) = await extra.WriteAsync(context, cancellationToken);
            userData |= writesUserData;
            server |= writesServer;
        }

        counters.Files = run.Index.Count;
        manifest.Name = plan.Name;
        manifest.Scope = plan.Scope;
        manifest.ArcanistVersion = ServerVersion;
        manifest.Machine = Environment.MachineName;
        manifest.UpdatedAt = time.GetUtcNow().UtcDateTime;
        manifest.Snapshots.RemoveAll(s => s.Name == snapshot);
        manifest.Snapshots.Add(new BackupSnapshotInfo(snapshot, now, run.Index.Count, run.Index.Sum(e => e.Size), userData, server));
        manifest.Snapshots.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        await PurgeAsync(target, folder, manifest, plan.RetentionDays, cancellationToken);
        await BackupFormat.ReplaceAsync(target, manifestPath, manifest, cancellationToken);
        await target.DeleteFolderAsync(BackupPaths.Join(folder, BackupFormat.InProgress, snapshot), cancellationToken);
        changed();
        return snapshot;
    }

    /// <summary>Snapshots follow each other: a new one is named after the latest (two runs in a second, or a clock set back).</summary>
    private static string Later(string snapshot, BackupManifest manifest)
    {
        if (manifest.Snapshots.Count == 0 || string.CompareOrdinal(snapshot, manifest.Snapshots[^1].Name) > 0
            || !BackupFormat.TryParseSnapshot(manifest.Snapshots[^1].Name, out var latest))
            return snapshot;
        return BackupFormat.SnapshotName(latest.AddSeconds(1));
    }

    /// <summary>The newest snapshot with a journal and not in the manifest yet: a run that did not finish.</summary>
    private static async Task<string?> UnfinishedSnapshotAsync(IBackupTarget target, string folder, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var prefix = BackupPaths.Join(folder, BackupFormat.InProgress) + "/";
        string? latest = null;
        await foreach (var file in target.ListAsync(BackupPaths.Join(folder, BackupFormat.InProgress), cancellationToken))
        {
            if (!file.Path.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var name = file.Path[prefix.Length..].Split('/')[0];
            if (BackupFormat.TryParseSnapshot(name, out _) && manifest.Snapshots.All(s => s.Name != name)
                && (latest is null || string.CompareOrdinal(name, latest) > 0))
                latest = name;
        }

        return latest;
    }

    private static string ServerVersion =>
        typeof(BackupRunner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private async Task BackUpFileAsync(
        Run run, BackupPlanSource source, string sourceFolder, StorageEntry file,
        IReadOnlyDictionary<string, BackupIndexEntry> previous, CancellationToken cancellationToken)
    {
        var path = $"{sourceFolder}/{file.RelativePath}";
        if (!run.Seen.Add(path))
            return;
        run.Plan.ContentHashes.TryGetValue((source.SourceId, file.RelativePath), out var contentHash);

        if (run.Journal.Files.TryGetValue(path, out var done))
        {
            run.Keep(done);
            run.Counters.Unchanged++;
            return;
        }

        previous.TryGetValue(path, out var before);
        if (before is not null && before.Size == file.Size && (before.Modified - file.ModifiedAt).Duration() <= ModifiedTolerance)
        {
            run.Keep(before with { ContentHash = contentHash ?? before.ContentHash });
            run.Counters.Unchanged++;
            return;
        }

        try
        {
            if (before is not null && before.Size == file.Size)
            {
                // Touched but maybe not changed (a copy, a restore, a file system that rounds times): the hash says.
                string sha;
                await using (var stream = await source.Storage.OpenReadAsync(source.Location, file.RelativePath, cancellationToken))
                    sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                if (sha == before.Sha256)
                {
                    await run.RecordAsync(before with { Modified = file.ModifiedAt, ContentHash = contentHash ?? before.ContentHash }, cancellationToken);
                    run.Counters.Unchanged++;
                    return;
                }
            }

            if (before is not null)
                await run.MoveToVersionAsync(path, cancellationToken);
            else
                await run.Target.DeleteAsync(BackupFormat.CurrentPath(run.Plan.Folder, path), cancellationToken); // ours, from an interrupted run

            await using var input = await source.Storage.OpenReadAsync(source.Location, file.RelativePath, cancellationToken);
            await using var hashing = new HashingStream(input);
            var length = input.CanSeek ? input.Length : file.Size;
            await run.Target.WriteAsync(BackupFormat.CurrentPath(run.Plan.Folder, path), hashing, length, cancellationToken);
            if (hashing.BytesRead != length)
                throw new IOException("The file changed while it was read.");

            await run.RecordAsync(new BackupIndexEntry(path, hashing.BytesRead, file.ModifiedAt, hashing.Hash(), contentHash), cancellationToken);
            run.Counters.Copied++;
            run.Counters.Bytes += hashing.BytesRead;
            run.WriteFailures = 0;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Gone while the run went through the library (organized, deleted): the next run takes it.
            run.Counters.Skipped++;
            if (before is not null && !run.Moved.Contains(path))
                run.Keep(before);
        }
        catch (ArcanistException.Conflict conflict) when (conflict.Code == ErrorCodes.BackupFileTooLarge)
        {
            run.Counters.Fail(path, conflict.Code);
        }
        catch (ArcanistException.Conflict conflict) when (conflict.Code is ErrorCodes.BackupQuotaExceeded or ErrorCodes.BackupsNotAccepted)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or ArcanistException)
        {
            logger.LogWarning("Backup {Backup}: could not copy {Path}: {Error}", run.Plan.Name, path, e.Message);
            run.Counters.Fail(path, e is ArcanistException a ? a.Code : ErrorCodes.WriteFailed);
            if (++run.WriteFailures >= MaxConsecutiveWriteFailures && await run.Target.ProbeAsync(cancellationToken) is { } problem)
                throw new ArcanistException.Conflict(problem);
        }
        finally
        {
            run.Changed();
        }
    }

    /// <summary>Each library folder's folder in <c>current/</c>: its library's name, chosen once and kept.</summary>
    private static Dictionary<Guid, string> AssignFolders(BackupManifest manifest, IReadOnlyList<BackupPlanSource> sources)
    {
        var result = new Dictionary<Guid, string>();
        var taken = manifest.Sources.Select(s => s.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (manifest.Sources.FirstOrDefault(s => s.SourceId == source.SourceId) is { } known)
            {
                result[source.SourceId] = known.Folder;
                continue;
            }

            var name = PathTemplate.SanitizeName(source.LibraryName);
            var candidate = name;
            for (var copy = 2; !taken.Add(candidate); copy++)
                candidate = $"{name} ({copy})";
            manifest.Sources.Add(new BackupSourceFolder(source.SourceId, source.LibraryId, source.LibraryName, candidate));
            result[source.SourceId] = candidate;
        }

        return result;
    }

    private static async Task<Dictionary<string, BackupIndexEntry>> LatestIndexAsync(
        IBackupTarget target, string folder, BackupManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.Snapshots.Count == 0)
            return new(StringComparer.Ordinal);
        var latest = manifest.Snapshots[^1].Name;
        var index = await BackupFormat.ReadAsync<BackupIndex>(target,
                        BackupPaths.Join(folder, BackupFormat.Snapshots, latest, BackupFormat.IndexFile), cancellationToken)
                    ?? throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
        return index.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
    }

    private static async Task<JournalState> ReadJournalAsync(IBackupTarget target, string folder, string snapshot, CancellationToken cancellationToken)
    {
        var state = new JournalState();
        var journalFolder = BackupPaths.Join(folder, BackupFormat.InProgress, snapshot);
        var parts = new List<string>();
        await foreach (var file in target.ListAsync(journalFolder, cancellationToken))
        {
            if (file.Path.EndsWith(".json", StringComparison.Ordinal))
                parts.Add(file.Path);
        }

        foreach (var part in parts.Order(StringComparer.Ordinal))
        {
            if (await BackupFormat.ReadAsync<BackupJournal>(target, part, cancellationToken) is not { } journal)
                continue;
            foreach (var entry in journal.Files)
                state.Files[entry.Path] = entry;
            state.Moved.UnionWith(journal.Moved);
            state.Parts++;
        }

        return state;
    }

    /// <summary>
    /// Deletes old versions after the retention. A snapshot needs the versions of every later run, so the snapshots
    /// older than the newest one whose versions went cannot be restored any more and go too.
    /// </summary>
    private async Task PurgeAsync(IBackupTarget target, string folder, BackupManifest manifest, int retentionDays, CancellationToken cancellationToken)
    {
        if (retentionDays <= 0 || manifest.Snapshots.Count < 2)
            return;
        var limit = time.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var old = manifest.Snapshots.Take(manifest.Snapshots.Count - 1).Where(s => s.CreatedAt < limit).ToList();
        if (old.Count == 0)
            return;

        foreach (var snapshot in old)
            await target.DeleteFolderAsync(BackupPaths.Join(folder, BackupFormat.Versions, snapshot.Name), cancellationToken);
        foreach (var snapshot in old.Take(old.Count - 1))
        {
            await target.DeleteFolderAsync(BackupPaths.Join(folder, BackupFormat.Snapshots, snapshot.Name), cancellationToken);
            manifest.Snapshots.Remove(snapshot);
        }

        logger.LogInformation("Backup {Backup}: removed versions older than {Days} days", manifest.Name, retentionDays);
    }

    private sealed class JournalState
    {
        public Dictionary<string, BackupIndexEntry> Files { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Moved { get; } = new(StringComparer.Ordinal);

        public int Parts { get; set; }
    }

    /// <summary>One run's state: the new index, what it moved, and its journal.</summary>
    private sealed class Run(BackupPlan plan, IBackupTarget target, string snapshot, BackupCounters counters, JournalState journal, Action changed)
    {
        private readonly List<BackupIndexEntry> _pendingFiles = [];
        private readonly List<string> _pendingMoved = [];
        private int _part = journal.Parts;

        public BackupPlan Plan => plan;

        public IBackupTarget Target => target;

        public BackupCounters Counters => counters;

        public JournalState Journal => journal;

        public List<BackupIndexEntry> Index { get; } = [];

        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Moved { get; } = new(journal.Moved, StringComparer.Ordinal);

        public int WriteFailures { get; set; }

        public void Changed() => changed();

        /// <summary>A file of the snapshot that needs nothing written.</summary>
        public void Keep(BackupIndexEntry entry) => Index.Add(entry);

        /// <summary>A file of the snapshot that this run wrote (or whose index entry changed): journaled.</summary>
        public async Task RecordAsync(BackupIndexEntry entry, CancellationToken cancellationToken)
        {
            Index.Add(entry);
            _pendingFiles.Add(entry);
            if (_pendingFiles.Count + _pendingMoved.Count >= JournalEvery)
                await FlushJournalAsync(cancellationToken);
        }

        /// <summary>Moves a file's copy from <c>current/</c> to this run's versions (once, even when resuming).</summary>
        public async Task MoveToVersionAsync(string path, CancellationToken cancellationToken)
        {
            if (Moved.Contains(path))
            {
                // Moved before a restart: what is in current/ now is this run's own unfinished copy.
                if (!journal.Files.ContainsKey(path))
                    await target.DeleteAsync(BackupFormat.CurrentPath(plan.Folder, path), cancellationToken);
                return;
            }

            var current = BackupFormat.CurrentPath(plan.Folder, path);
            var version = BackupFormat.VersionPath(plan.Folder, snapshot, path);
            if (await target.StatAsync(version, cancellationToken) is not null)
            {
                // Moved, but the journal did not say so before the restart.
                await target.DeleteAsync(current, cancellationToken);
            }
            else if (await target.StatAsync(current, cancellationToken) is not null)
            {
                await target.MoveAsync(current, version, cancellationToken);
                counters.Versioned++;
            }

            Moved.Add(path);
            _pendingMoved.Add(path);
            if (_pendingFiles.Count + _pendingMoved.Count >= JournalEvery)
                await FlushJournalAsync(cancellationToken);
        }

        public async Task FlushJournalAsync(CancellationToken cancellationToken)
        {
            if (_pendingFiles.Count == 0 && _pendingMoved.Count == 0)
                return;
            var part = BackupPaths.Join(plan.Folder, BackupFormat.InProgress, snapshot, $"{_part++:D6}.json");
            await target.DeleteAsync(part, cancellationToken);
            await BackupFormat.WriteAsync(target, part, new BackupJournal([.. _pendingFiles], [.. _pendingMoved]), cancellationToken);
            _pendingFiles.Clear();
            _pendingMoved.Clear();
        }
    }
}

/// <summary>Passes a stream through while computing its SHA-256 and counting its bytes.</summary>
internal sealed class HashingStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _length;

    /// <summary>Bytes read so far.</summary>
    public long BytesRead => _length;

    public override long Length => throw new NotSupportedException();

    /// <summary>The SHA-256 of what was read; callable once, also after the stream was disposed (HTTP content disposes it).</summary>
    public string Hash()
    {
        var hash = Convert.ToHexStringLower(_hash.GetHashAndReset());
        _hash.Dispose();
        return hash;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        _hash.AppendData(buffer[..read]);
        _length += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        _hash.AppendData(buffer.Span[..read]);
        _length += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();


}
