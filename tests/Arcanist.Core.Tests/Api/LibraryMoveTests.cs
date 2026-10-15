using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Api;

/// <summary>Moving a local library's folder: everything is copied, the library follows, the old files go or stay.</summary>
public sealed class LibraryMoveTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-move-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;
    private string _old = null!;
    private Guid _sourceId;
    private Dictionary<string, Guid> _tracks = null!;

    public async ValueTask InitializeAsync()
    {
        _old = Path.Combine(_root, "Old");
        AudioFixtures.WriteWav(Path.Combine(_old, "Band", "Album", "01 One.wav"), 1, new FixtureTags("One", "Band", "Album", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_old, "Band", "Album", "02 Two.wav"), 2, new FixtureTags("Two", "Band", "Album", "Band", TrackNumber: 2));
        File.WriteAllText(Path.Combine(_old, "Band", "Album", "cover.jpg"), "not really a picture");
        File.WriteAllText(Path.Combine(_old, ".hidden-notes"), "kept too");
        File.SetLastWriteTimeUtc(Path.Combine(_old, "Band", "Album", "01 One.wav"), new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_old]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        var library = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0];
        _sourceId = library.GetProperty("sources")[0].GetProperty("id").GetGuid();
        _tracks = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> MoveAsync(string destination, bool keepOriginals)
    {
        var response = await _admin.PostAsJsonAsync($"/rest/v1/library-sources/{_sourceId}/move", new { destination, keepOriginals }, ArcanistFactory.Json, Cancel);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var move = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/library-sources/moves", ArcanistFactory.Json, Cancel))
                .EnumerateArray().First(m => m.GetProperty("sourceId").GetGuid() == _sourceId);
            if (move.GetProperty("phase").GetString() is "COMPLETED" or "FAILED")
                return move;
            Assert.True(DateTime.UtcNow < deadline, "The move did not finish");
            await Task.Delay(100, Cancel);
        }
    }

    private async Task<string> LocationAsync() =>
        (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))[0]
        .GetProperty("sources")[0].GetProperty("location").GetString()!;

    [Fact]
    public async Task Moving_copies_everything_follows_it_and_cleans_the_old_folder()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "New")).FullName;
        var move = await MoveAsync(target, keepOriginals: false);
        Assert.Equal(("COMPLETED", 4, JsonValueKind.Null), (move.GetProperty("phase").GetString(), move.GetProperty("filesDone").GetInt32(), move.GetProperty("errorCode").ValueKind));

        Assert.Equal(target, await LocationAsync());
        Assert.True(File.Exists(Path.Combine(target, "Band", "Album", "cover.jpg")));
        Assert.True(File.Exists(Path.Combine(target, ".hidden-notes")));
        Assert.Equal(new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(target, "Band", "Album", "01 One.wav")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_old));

        // The catalog is the same, and plays from the new folder.
        await _factory.WaitForScansAsync(_admin);
        var tracks = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetGuid());
        Assert.Equal(_tracks, tracks);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/rest/v1/stream/{_tracks["One"]}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Keeping_the_originals_only_copies_and_nothing_is_overwritten()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "Copy")).FullName;
        var move = await MoveAsync(target, keepOriginals: true);
        Assert.Equal("COMPLETED", move.GetProperty("phase").GetString());
        Assert.True(File.Exists(Path.Combine(_old, "Band", "Album", "02 Two.wav")));
        Assert.True(File.Exists(Path.Combine(target, "Band", "Album", "02 Two.wav")));

        // Back to the old folder: it holds the same files, so nothing is copied over them.
        var back = await MoveAsync(_old, keepOriginals: true);
        Assert.Equal(("COMPLETED", JsonValueKind.Null), (back.GetProperty("phase").GetString(), back.GetProperty("errorCode").ValueKind));

        // A different file with the same name stops the move before the library changes folder.
        var other = Directory.CreateDirectory(Path.Combine(_root, "Other", "Band", "Album")).FullName;
        File.WriteAllText(Path.Combine(other, "cover.jpg"), "a different cover");
        var failed = await MoveAsync(Path.Combine(_root, "Other"), keepOriginals: false);
        Assert.Equal(("FAILED", "storage_conflict"), (failed.GetProperty("phase").GetString(), failed.GetProperty("errorCode").GetString()));
        Assert.Equal(_old, await LocationAsync());
        Assert.Equal("a different cover", File.ReadAllText(Path.Combine(other, "cover.jpg")));
    }

    [Fact]
    public async Task A_folder_inside_the_library_is_refused()
    {
        var inside = Path.Combine(_old, "Band");
        var response = await _admin.PostAsJsonAsync($"/rest/v1/library-sources/{_sourceId}/move", new { destination = inside }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overlapping", await response.Content.ReadAsStringAsync(Cancel));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
