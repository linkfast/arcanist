using Arcanist.Core.Application;
using Arcanist.Core.Backups.Targets;

namespace Arcanist.Core.Backups;

/// <summary>Reads backups in a destination, whoever made them: what is there, and where each file of a snapshot is.</summary>
internal static class BackupReader
{
    /// <summary>The backups in a destination: the folders at its root that hold a manifest this version reads.</summary>
    public static async Task<IReadOnlyList<(string Folder, BackupManifest Manifest)>> FindAsync(IBackupTarget target, CancellationToken cancellationToken)
    {
        var found = new List<(string, BackupManifest)>();
        foreach (var folder in await target.FoldersAsync("", cancellationToken))
        {
            try
            {
                if (await BackupFormat.ReadReplacedAsync<BackupManifest>(target, BackupPaths.Join(folder, BackupFormat.ManifestFile), cancellationToken) is
                    { Format: <= BackupFormat.Version } manifest)
                    found.Add((folder, manifest));
            }
            catch (ArcanistException.Conflict)
            {
                // Not a backup, or a damaged one: it is not listed.
            }
        }

        return found;
    }

    public static async Task<BackupManifest> ManifestAsync(IBackupTarget target, string folder, CancellationToken cancellationToken)
    {
        if (!BackupPaths.IsSafe(folder) || folder.Contains('/'))
            throw new ArcanistException.NotFound();
        var manifest = await BackupFormat.ReadReplacedAsync<BackupManifest>(target, BackupPaths.Join(folder, BackupFormat.ManifestFile), cancellationToken)
                       ?? throw new ArcanistException.Conflict(ErrorCodes.BackupNotFound);
        if (manifest.Format > BackupFormat.Version)
            throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
        return manifest;
    }

    public static async Task<BackupIndex> IndexAsync(IBackupTarget target, string folder, BackupManifest manifest, string snapshot, CancellationToken cancellationToken)
    {
        if (manifest.Snapshots.All(s => s.Name != snapshot))
            throw new ArcanistException.Conflict(ErrorCodes.BackupNotFound);
        return await BackupFormat.ReadAsync<BackupIndex>(target, BackupPaths.Join(folder, BackupFormat.Snapshots, snapshot, BackupFormat.IndexFile), cancellationToken)
               ?? throw new ArcanistException.Conflict(ErrorCodes.BackupNotFound);
    }

    /// <summary>A file of the snapshot (catalog, user data…); null when the snapshot has none.</summary>
    public static Task<T?> SnapshotFileAsync<T>(IBackupTarget target, string folder, string snapshot, string file, CancellationToken cancellationToken)
        where T : class =>
        BackupFormat.ReadAsync<T>(target, BackupPaths.Join(folder, BackupFormat.Snapshots, snapshot, file), cancellationToken);

    /// <summary>
    /// Where each file of <paramref name="snapshot"/> is now: in <c>versions/S/</c> of the first later run S that
    /// replaced or removed it, or still in <c>current/</c>.
    /// </summary>
    public static async Task<Dictionary<string, string>> LocateAsync(
        IBackupTarget target, string folder, BackupManifest manifest, BackupIndex index, CancellationToken cancellationToken)
    {
        var wanted = index.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var located = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var later in manifest.Snapshots.Where(s => string.CompareOrdinal(s.Name, index.Snapshot) > 0).OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var laterIndex = await BackupFormat.ReadAsync<BackupIndex>(target,
                                 BackupPaths.Join(folder, BackupFormat.Snapshots, later.Name, BackupFormat.IndexFile), cancellationToken)
                             ?? throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
            foreach (var moved in laterIndex.Moved)
            {
                if (wanted.Contains(moved) && !located.ContainsKey(moved))
                    located[moved] = BackupFormat.VersionPath(folder, later.Name, moved);
            }
        }

        foreach (var path in wanted.Where(p => !located.ContainsKey(p)))
            located[path] = BackupFormat.CurrentPath(folder, path);
        return located;
    }
}
