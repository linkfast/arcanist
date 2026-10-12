using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Storage;

public sealed class LocalStorageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-storage").FullName;
    private readonly LocalStorage _storage = new();

    private StorageLocation Location => new(LibrarySourceKind.Local, _root);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Enumerates_files_recursively_with_slash_separated_relative_paths()
    {
        Write("Artist/Album/01 Track.flac", 10);
        Write("Artist/Album/cover.jpg", 3);
        Write("loose.mp3", 5);

        var entries = await ListAsync();

        Assert.Equal(["Artist/Album/01 Track.flac", "Artist/Album/cover.jpg", "loose.mp3"], entries.Select(e => e.RelativePath).Order());
        Assert.Equal(10, entries.Single(e => e.RelativePath.EndsWith(".flac")).Size);
        Assert.All(entries, e => Assert.Equal(DateTimeKind.Utc, e.ModifiedAt.Kind));
    }

    [Fact]
    public async Task Skips_hidden_files_and_nas_system_folders()
    {
        Write("music.mp3");
        Write(".hidden.mp3");
        Write(".Trash/old.mp3");
        Write("@eaDir/music.mp3/SYNOPHOTO_THUMB.jpg");
        Write("#recycle/deleted.mp3");

        Assert.Equal(["music.mp3"], (await ListAsync()).Select(e => e.RelativePath));
    }

    [Fact]
    public async Task Opens_files_by_relative_path_but_never_outside_the_root()
    {
        Write("a/b.mp3", 7);

        await using (var stream = await _storage.OpenReadAsync(Location, "a/b.mp3", Cancel))
            Assert.Equal(7, stream.Length);

        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync(Location, "../outside.mp3", Cancel));
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync(Location, "a/../../outside.mp3", Cancel));
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync(Location, Path.Combine(_root, "a", "b.mp3"), Cancel));
    }

    [Fact]
    public async Task Probe_reports_missing_and_malformed_locations()
    {
        Assert.True((await _storage.ProbeAsync(Location, Cancel)).IsAvailable);
        Assert.Equal(StorageProblem.NotFound,
            (await _storage.ProbeAsync(Location with { Location = Path.Combine(_root, "missing") }, Cancel)).Problem);
        Assert.Equal(StorageProblem.Invalid, (await _storage.ProbeAsync(Location with { Location = "relative/path" }, Cancel)).Problem);
    }

    [Fact]
    public void Containment_compares_whole_path_segments()
    {
        var music = Path.Combine(_root, "Music");

        Assert.True(_storage.Contains(music, music));
        Assert.True(_storage.Contains(music, Path.Combine(music, "Rock")));
        Assert.True(_storage.Contains(music + Path.DirectorySeparatorChar, Path.Combine(music, "Rock")));
        Assert.False(_storage.Contains(music, Path.Combine(_root, "Music Videos")));
        Assert.False(_storage.Contains(Path.Combine(music, "Rock"), music));
    }

    [Fact]
    public async Task Moves_create_folders_and_never_overwrite()
    {
        Write("in/a.flac", 3);
        Write("taken/b.flac", 5);

        await _storage.MoveAsync(Location, "in/a.flac", "Artist/Album/01 A.flac", Cancel);

        Assert.True(File.Exists(Path.Combine(_root, "Artist", "Album", "01 A.flac")));
        Assert.False(File.Exists(Path.Combine(_root, "in", "a.flac")));

        Write("in/c.flac", 7);
        await Assert.ThrowsAsync<StorageConflictException>(() => _storage.MoveAsync(Location, "in/c.flac", "taken/b.flac", Cancel));
        Assert.Equal(5, new FileInfo(Path.Combine(_root, "taken", "b.flac")).Length);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.MoveAsync(Location, "in/c.flac", "../escaped.flac", Cancel));
    }

    [Fact]
    public async Task Imports_local_files_without_leaving_partial_copies()
    {
        var outside = Directory.CreateTempSubdirectory("arcanist-import").FullName;
        try
        {
            var file = Path.Combine(outside, "song.flac");
            File.WriteAllBytes(file, new byte[11]);

            await _storage.ImportAsync(Location, "Artist/song.flac", file, Cancel);

            Assert.False(File.Exists(file));
            Assert.Equal(["Artist/song.flac"], (await ListAsync()).Select(e => e.RelativePath));
            Assert.DoesNotContain(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories), f => Path.GetFileName(f).StartsWith(".arcanist-partial"));

            File.WriteAllBytes(file, new byte[2]);
            await Assert.ThrowsAsync<StorageConflictException>(() => _storage.ImportAsync(Location, "Artist/song.flac", file, Cancel));
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Trash_keeps_files_hidden_until_purged()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var storage = new LocalStorage(time);
        Write("Album/song.mp3");
        Write("Album/other/song.mp3");

        var first = await storage.TrashAsync(Location, "Album/song.mp3", Cancel);
        Write("Album/song.mp3");
        var second = await storage.TrashAsync(Location, "Album/song.mp3", Cancel);

        Assert.Equal(".arcanist-trash/2026-03-01/Album/song.mp3", first);
        Assert.Equal(".arcanist-trash/2026-03-01/Album/song (2).mp3", second);
        // Scans never see the trash.
        Assert.Equal(["Album/other/song.mp3"], (await ListAsync()).Select(e => e.RelativePath));

        await storage.PurgeTrashAsync(Location, new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc), Cancel);
        Assert.True(File.Exists(Path.Combine(_root, ".arcanist-trash", "2026-03-01", "Album", "song.mp3")));

        await storage.PurgeTrashAsync(Location, new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), Cancel);
        Assert.False(Directory.Exists(Path.Combine(_root, ".arcanist-trash")));
    }

    [Fact]
    public async Task Removes_emptied_folders_up_to_the_root_ignoring_system_clutter()
    {
        Write("Artist/Album/CD1/Thumbs.db");
        Write("Artist/Album/.DS_Store");
        Write("Artist/Album/@eaDir/x/SYNOPHOTO_THUMB.jpg");
        Write("Artist/Other/keep.flac");

        await _storage.RemoveEmptyFoldersAsync(Location, "Artist/Album/CD1", Cancel);

        Assert.False(Directory.Exists(Path.Combine(_root, "Artist", "Album")));
        // A parent with something left in it stays, and so does the root.
        Assert.True(File.Exists(Path.Combine(_root, "Artist", "Other", "keep.flac")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task Reports_whether_the_location_is_writable()
    {
        Assert.True(await _storage.CanWriteAsync(Location, Cancel));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
        Assert.False(await _storage.CanWriteAsync(Location with { Location = Path.Combine(_root, "missing") }, Cancel));
    }

    private async Task<List<StorageEntry>> ListAsync()
    {
        var entries = new List<StorageEntry>();
        await foreach (var entry in _storage.EnumerateAsync(Location, Cancel))
            entries.Add(entry);
        return entries;
    }

    private void Write(string relativePath, int size = 1)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
