using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;

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
