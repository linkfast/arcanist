using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Media;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Organization;

/// <summary>Files dropped into a drop folder find their way into the library.</summary>
public sealed class DropFolderTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly FixtureTags One = new("One", "The Band", "First", "The Band", TrackNumber: 1, Year: 2001);

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-drop-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private string _sourceId = null!;
    private string _dropId = null!;

    private string Music => Path.Combine(_root, "library");

    private string Drop => Path.Combine(_root, "drop");

    /// <summary>Files are prepared here and then moved into the drop folder at once, as the importer could otherwise
    /// read them while they are being written (files settle at once in these tests).</summary>
    private string Staging => Path.Combine(_root, "staging");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Music);
        Directory.CreateDirectory(Drop);
        Directory.CreateDirectory(Staging);
    }

    /// <summary>Moves everything prepared in <see cref="Staging"/> into the drop folder.</summary>
    private void Deliver()
    {
        foreach (var folder in Directory.EnumerateDirectories(Staging))
            Directory.Move(folder, Path.Combine(Drop, Path.GetFileName(folder)));
        foreach (var file in Directory.EnumerateFiles(Staging))
            File.Move(file, Path.Combine(Drop, Path.GetFileName(file)));
    }

    private async Task StartAsync(string policy = "SKIP", string? convertTo = null)
    {
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:Organization:StableSeconds"] = "0",
            ["Arcanist:Ffmpeg:SearchSystem"] = "false",
            ["Arcanist:Ffmpeg:Path"] = TestFfmpeg.Path ?? Path.Combine(_root, "no-ffmpeg"),
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [Music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        var library = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0];
        _sourceId = library.GetProperty("sources")[0].GetProperty("id").GetString()!;
        var response = await _admin.PostAsJsonAsync("/rest/v1/drop-folders", Folder(policy, convertTo), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        _dropId = (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("id").GetString()!;
    }

    private object Folder(string policy, string? convertTo = null, string? path = null) => new
    {
        path = path ?? Drop,
        librarySourceId = _sourceId,
        enabled = true,
        duplicatePolicy = policy,
        convertTo,
        convertBitRate = (int?)null,
        convertLossy = false,
    };

    private static string In(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>Writes a tagged WAV of music at <paramref name="sampleRate"/>; equal seeds and rates give equal audio.</summary>
    private static string WriteSong(string path, FixtureTags tags, int seed = 1, int sampleRate = 44100)
    {
        File.WriteAllBytes(path, AudioFixtures.Music(bpm: 120, seconds: 3, chordSeed: seed, sampleRate: sampleRate));
        AudioFixtures.Tag(path, tags);
        return path;
    }

    private static List<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .ToList();

    private async Task<List<JsonElement>> ItemsAsync(string? status = null) =>
        (await _admin.GetFromJsonAsync<List<JsonElement>>($"/rest/v1/import-items{(status is null ? "" : $"?status={status}")}", ArcanistFactory.Json, Cancel))!;

    private ArcanistDbContext Db() => _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();

    [Fact]
    public async Task Tagged_albums_go_into_the_library_by_its_template_with_their_other_files()
    {
        await StartAsync();
        WriteSong(In(Staging, "Some Album/a.wav"), One);
        WriteSong(In(Staging, "Some Album/b.wav"), One with { Title = "Two", TrackNumber = 2 }, seed: 2);
        File.WriteAllText(In(Staging, "Some Album/a.lrc"), "[00:01.00]Hello");
        File.WriteAllBytes(In(Staging, "Some Album/folder.jpg"), AudioFixtures.Png(1));

        Deliver();
        await _factory.WaitForImportsAsync();

        Assert.Equal(
        [
            "The Band/2001 - First/01 One.lrc",
            "The Band/2001 - First/01 One.wav",
            "The Band/2001 - First/02 Two.wav",
            "The Band/2001 - First/folder.jpg",
        ], Files(Music));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Drop));

        // The library was scanned: the songs are there, with their lyrics.
        await using var db = Db();
        Assert.Equal(["One", "Two"], await db.Tracks.OrderBy(t => t.TrackNumber).Select(t => t.Title).ToListAsync(Cancel));
        Assert.Equal(1, await db.Lyrics.CountAsync(Cancel));

        var imported = await ItemsAsync("IMPORTED");
        Assert.Equal(2, imported.Count);
        Assert.Contains(imported, i => i.GetProperty("destinationPath").GetString() == "The Band/2001 - First/01 One.wav");
        Assert.Empty(await ItemsAsync());
    }

    [Fact]
    public async Task Files_without_enough_tags_wait_for_review_and_import_with_the_tags_given()
    {
        await StartAsync();
        WriteSong(In(Staging, "mystery.wav"), new FixtureTags(Title: "Something"));

        Deliver();
        await _factory.WaitForImportsAsync();

        var item = Assert.Single(await ItemsAsync());
        Assert.Equal(("NEEDS_REVIEW", "MISSING_TAGS"), (item.GetProperty("status").GetString(), item.GetProperty("reason").GetString()));
        Assert.Equal("Something", item.GetProperty("details").GetProperty("title").GetString());
        Assert.True(File.Exists(Path.Combine(Drop, "mystery.wav")));

        // Nothing changes on its own while it waits.
        Deliver();
        await _factory.WaitForImportsAsync();
        Assert.Equal("NEEDS_REVIEW", Assert.Single(await ItemsAsync()).GetProperty("status").GetString());

        var response = await _admin.PostAsJsonAsync($"/rest/v1/import-items/{item.GetProperty("id").GetString()}/accept",
            new { tags = new { title = "Found", artist = "Someone", album = "Lost Tapes", trackNumber = 4 } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Deliver();
        await _factory.WaitForImportsAsync();

        var path = Path.Combine(Music, "Someone", "Lost Tapes", "04 Found.wav");
        Assert.True(File.Exists(path));
        await using (var stream = File.OpenRead(path))
            Assert.Equal(("Found", "Lost Tapes"), (TagReader.Read(stream, "audio/wav") is var tags ? (tags.Title, tags.Album) : default));
        Assert.Empty(await ItemsAsync());
    }

    [Fact]
    public async Task The_same_audio_stays_in_the_drop_folder_until_discarded()
    {
        WriteSong(In(Music, "Album/song.wav"), One);
        await StartAsync();
        File.Copy(Path.Combine(Music, "Album", "song.wav"), In(Staging, "again.wav"));

        Deliver();
        await _factory.WaitForImportsAsync();

        var item = Assert.Single(await ItemsAsync());
        Assert.Equal(("DUPLICATE", "SAME_AUDIO"), (item.GetProperty("status").GetString(), item.GetProperty("reason").GetString()));
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("trackId").ValueKind);

        var response = await _admin.PostAsync($"/rest/v1/import-items/{item.GetProperty("id").GetString()}/discard", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Files(Drop));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Drop, ".arcanist-trash"), "again.wav", SearchOption.AllDirectories));
        Assert.Equal("DISCARDED", Assert.Single(await ItemsAsync("DISCARDED")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Keeping_the_best_copy_replaces_worse_ones_and_skips_worse_ones()
    {
        WriteSong(In(Music, "old/one.wav"), One, sampleRate: 22050);
        await StartAsync("KEEP_BEST");
        WriteSong(In(Staging, "better.wav"), One, sampleRate: 44100);

        Deliver();
        await _factory.WaitForImportsAsync();

        var item = Assert.Single(await ItemsAsync("IMPORTED"));
        Assert.Equal("REPLACED_WORSE", item.GetProperty("reason").GetString());
        Assert.Equal(["The Band/2001 - First/01 One.wav"], Files(Music));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Music, ".arcanist-trash"), "one.wav", SearchOption.AllDirectories));

        // One track, now with the better file only.
        await using (var db = Db())
        {
            var track = await db.Tracks.Include(t => t.Files).SingleAsync(Cancel);
            Assert.Equal(44100, Assert.Single(track.Files, f => f.MissingSince == null).SampleRate);
        }

        WriteSong(In(Staging, "worse.wav"), One, sampleRate: 11025);
        Deliver();
        await _factory.WaitForImportsAsync();
        var worse = Assert.Single(await ItemsAsync());
        Assert.Equal(("DUPLICATE", "NOT_BETTER"), (worse.GetProperty("status").GetString(), worse.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Other_copies_of_a_song_are_skipped_or_added_as_versions()
    {
        WriteSong(In(Music, "Album/one.wav"), One);
        await StartAsync("SKIP");
        WriteSong(In(Staging, "other rip.wav"), One, sampleRate: 22050);

        Deliver();
        await _factory.WaitForImportsAsync();
        var item = Assert.Single(await ItemsAsync());
        Assert.Equal(("DUPLICATE", "SAME_TRACK"), (item.GetProperty("status").GetString(), item.GetProperty("reason").GetString()));

        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"/rest/v1/drop-folders/{_dropId}", Folder("ADD_VERSION"), ArcanistFactory.Json, Cancel)).StatusCode);
        await _admin.PostAsync($"/rest/v1/import-items/{item.GetProperty("id").GetString()}/retry", null, Cancel);
        Deliver();
        await _factory.WaitForImportsAsync();

        Assert.Equal(["Album/one.wav", "The Band/2001 - First/01 One.wav"], Files(Music));
        await using var db = Db();
        Assert.Equal(2, (await db.Tracks.Include(t => t.Files).SingleAsync(Cancel)).Files.Count);
    }

    [Fact]
    public async Task Lossless_files_can_be_converted_to_flac_on_the_way_in()
    {
        TestFfmpeg.Require();
        await StartAsync(convertTo: "FLAC");
        WriteSong(In(Staging, "Album/one.wav"), One with { WithCover = true });

        Deliver();
        await _factory.WaitForImportsAsync();

        var path = Path.Combine(Music, "The Band", "2001 - First", "01 One.flac");
        Assert.True(File.Exists(path), string.Join(", ", Files(Music)));
        await using (var stream = File.OpenRead(path))
        {
            var tags = TagReader.Read(stream, "audio/flac");
            Assert.Equal(("One", "First", 1), (tags.Title, tags.Album, tags.TrackNumber));
            Assert.NotNull(tags.EmbeddedPicture);
        }

        // The original is kept in the drop folder's trash for a while.
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Drop, ".arcanist-trash"), "one.wav", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Drop_folders_are_outside_libraries_and_configured_by_administrators()
    {
        await StartAsync();

        var response = await _admin.PostAsJsonAsync("/rest/v1/drop-folders", Folder("SKIP", path: Directory.CreateDirectory(Path.Combine(Music, "inbox")).FullName), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal("overlapping", problem.GetProperty("errors").GetProperty("path")[0].GetString());

        await _factory.CreateUserAsync("manager", permissions: Permissions.ManageLibraries);
        var manager = await _factory.CreateAuthenticatedClientAsync("manager");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/rest/v1/drop-folders", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/rest/v1/drop-folders", Folder("SKIP"), ArcanistFactory.Json, Cancel)).StatusCode);
        // Managers of the library review what arrives.
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/rest/v1/import-items/summary", Cancel)).StatusCode);

        var folders = await _admin.GraphQLAsync("{ dropFolders { path available duplicatePolicy } }");
        Assert.True(folders.GetProperty("data").GetProperty("dropFolders")[0].GetProperty("available").GetBoolean());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
