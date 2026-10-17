using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Data;

/// <summary>How much space Arcanist and its music take, for the Database and Home pages.</summary>
public sealed class StorageUsageTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-storage-").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        var album = new FixtureTags(Album: "First", AlbumArtist: "Band", Artist: "Band", WithCover: true);
        AudioFixtures.WriteWav(Path.Combine(_music, "01.wav"), 1, album with { Title = "One", TrackNumber = 1 });
        AudioFixtures.WriteWav(Path.Combine(_music, "02.wav"), 2, album with { Title = "Two", TrackNumber = 2 });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }

    private async Task<JsonElement> UsageAsync(bool refresh = false) =>
        await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/storage-usage?refresh={refresh.ToString().ToLowerInvariant()}", ArcanistFactory.Json, Cancel);

    private static long Part(JsonElement usage, string kind) =>
        usage.GetProperty("parts").EnumerateArray().Single(p => p.GetProperty("kind").GetString() == kind).GetProperty("bytes").GetInt64();

    [Fact]
    public async Task Administrators_see_what_takes_space_and_what_is_in_the_catalog()
    {
        var usage = await UsageAsync();

        // Arcanist's own data, by part: the database (SQLite here) and the covers read from the music among them.
        Assert.Equal(_factory.DataPath, usage.GetProperty("dataDirectory").GetString());
        Assert.True(Part(usage, "DATABASE") > 0);
        Assert.True(Part(usage, "ARTWORK") > 0);
        Assert.Equal(usage.GetProperty("parts").EnumerateArray().Sum(p => p.GetProperty("bytes").GetInt64()), usage.GetProperty("arcanistBytes").GetInt64());
        var database = usage.GetProperty("database");
        Assert.Equal("SQLITE", database.GetProperty("provider").GetString());
        Assert.False(string.IsNullOrEmpty(database.GetProperty("version").GetString()));
        Assert.Equal(Part(usage, "DATABASE"), database.GetProperty("sizeBytes").GetInt64());
        var tables = database.GetProperty("tables").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("rows").GetInt64());
        Assert.Equal(2, tables["Tracks"]);
        Assert.Equal(1, tables["Users"]);

        // What is in the catalog, and the music of each library as it is on disk.
        var catalog = usage.GetProperty("catalog");
        Assert.Equal((1, 2, 1, 2), (catalog.GetProperty("libraries").GetInt32(), catalog.GetProperty("tracks").GetInt32(),
            catalog.GetProperty("albums").GetInt32(), catalog.GetProperty("files").GetInt32()));
        var size = Directory.EnumerateFiles(_music).Sum(f => new FileInfo(f).Length);
        var library = Assert.Single(usage.GetProperty("libraries").EnumerateArray());
        Assert.Equal(("Music", "LOCAL", 2, size), (library.GetProperty("name").GetString(), library.GetProperty("kind").GetString(),
            library.GetProperty("files").GetInt32(), library.GetProperty("bytes").GetInt64()));
        Assert.Equal(size, usage.GetProperty("musicBytes").GetInt64());
        Assert.True(usage.GetProperty("disk").GetProperty("freeBytes").GetInt64() > 0);

        // Measured again only when asked (or a few minutes later).
        var logs = Path.Combine(_factory.DataPath, "logs", "extra.log");
        await File.WriteAllBytesAsync(logs, new byte[100_000], Cancel);
        Assert.Equal(Part(usage, "LOGS"), Part(await UsageAsync(), "LOGS"));
        Assert.True(Part(await UsageAsync(refresh: true), "LOGS") >= Part(usage, "LOGS") + 100_000);

        // The same through GraphQL.
        var graphql = await _admin.GraphQLAsync("query { storageUsage { arcanistBytes catalog { tracks } } }");
        Assert.Equal(2, graphql.GetProperty("data").GetProperty("storageUsage").GetProperty("catalog").GetProperty("tracks").GetInt32());

        // Only administrators.
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/rest/v1/storage-usage", Cancel)).StatusCode);
    }
}
