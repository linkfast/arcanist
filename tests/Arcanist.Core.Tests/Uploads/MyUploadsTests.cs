using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Cloud;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Uploads;

/// <summary>A user's history of uploads: files into libraries, and songs into their own Ultimate Cloud library.</summary>
public sealed class MyUploadsTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string Uploads = "query { myUploads(first: 50) { totalCount active items { kind status name size libraryName errorCode track { title } } } }";

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-my-uploads-").FullName;
    private readonly string _staging = Directory.CreateTempSubdirectory("arcanist-my-uploads-staging-").FullName;
    private readonly FakeUltimateCloud _cloud = new();
    private ArcanistFactory _factory = null!;

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        _cloud.Dispose();
        foreach (var folder in new[] { _music, _staging })
            try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    private static async Task<JsonElement> HistoryAsync(HttpClient client) =>
        (await client.GraphQLAsync(Uploads)).GetProperty("data").GetProperty("myUploads");

    private static async Task<JsonElement> UntilAsync(HttpClient client, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var history = await HistoryAsync(client);
            if (done(history))
                return history;
            Assert.True(DateTime.UtcNow < deadline, $"Still {history}");
            await Task.Delay(100, Cancel);
        }
    }

    private static string StatusOf(JsonElement history, int index) =>
        history.GetProperty("items")[index].GetProperty("status").GetString()!;

    [Fact]
    public async Task Every_upload_from_any_client_is_in_the_log_administrators_see()
    {
        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2" });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);
        var libraryId = Guid.Parse((await admin.GraphQLAsync("query { libraries { id } }")).GetProperty("data").GetProperty("libraries")[0].GetProperty("id").GetString()!);
        HttpContent Body(int seed) => new ByteArrayContent(File.ReadAllBytes(
                AudioFixtures.WriteWav(Path.Combine(_staging, $"s{seed}.wav"), seed, new FixtureTags($"Song {seed}", "Band", "Sent", "Band", TrackNumber: seed))))
            { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };

        var user = await _factory.CreateUserAsync("listener", permissions: Permissions.Upload);
        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=a.wav&client=panel", Body(1), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await listener.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=b.wav&client=WEB", Body(2), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await listener.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=c.wav", Body(3), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await listener.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=d.wav&client=fridge", Body(4), Cancel)).StatusCode);
        // An extension, in the server: the same service.
        using (var scope = _factory.Services.CreateScope())
        {
            var access = (await scope.ServiceProvider.GetRequiredService<Core.Auth.UserAccessCache>().GetUserAsync(user.Id, Cancel))!;
            await using var stream = new MemoryStream(await Body(5).ReadAsByteArrayAsync(Cancel));
            await scope.ServiceProvider.GetRequiredService<Core.Application.Libraries.UploadService>().UploadAsync(access, libraryId, "e.wav", stream, "youtube", Cancel);
        }

        var log = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/uploads", ArcanistFactory.Json, Cancel);
        var entries = log.GetProperty("items").EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => (Origin: e.GetProperty("origin").GetString(), User: e.GetProperty("userName").GetString()));
        Assert.Equal(("PANEL", "admin"), entries["a.wav"]);
        Assert.Equal(("WEB", "listener"), entries["b.wav"]);
        Assert.Equal(("API", "listener"), entries["c.wav"]);
        Assert.Equal(("EXTENSION", "listener"), entries["e.wav"]);
        // A request that does not say a known client never reaches the library.
        Assert.False(entries.ContainsKey("d.wav"));
        Assert.Equal("Music", log.GetProperty("items")[0].GetProperty("libraryName").GetString());

        var web = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/uploads?origin=web", ArcanistFactory.Json, Cancel);
        Assert.Equal("b.wav", Assert.Single(web.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var extension = (await admin.GraphQLAsync("query { uploads(input: { origin: EXTENSION }) { items { source } } }"))
            .GetProperty("data").GetProperty("uploads").GetProperty("items");
        Assert.Equal("youtube", Assert.Single(extension.EnumerateArray()).GetProperty("source").GetString());

        // Followed until catalogued, as each user's history is.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await admin.GetFromJsonAsync<JsonElement>("/rest/v1/uploads?status=added", ArcanistFactory.Json, Cancel)).GetProperty("totalCount").GetInt32() < 4)
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(200, Cancel);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/uploads", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Uploads_into_a_library_are_followed_until_catalogued_and_can_be_forgotten()
    {
        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2" });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);
        var libraryId = (await admin.GraphQLAsync("query { libraries { id } }")).GetProperty("data").GetProperty("libraries")[0].GetProperty("id").GetString();
        var file = AudioFixtures.WriteWav(Path.Combine(_staging, "song.wav"), 2, new FixtureTags("Uploaded", "Band", "Sent", "Band", TrackNumber: 1));

        HttpContent Body() => new ByteArrayContent(File.ReadAllBytes(file)) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=song.wav", Body(), Cancel)).StatusCode);

        // Followed until the library's scan catalogues it.
        var history = await UntilAsync(admin, h => StatusOf(h, 0) == "ADDED");
        var added = history.GetProperty("items")[0];
        Assert.Equal("LIBRARY", added.GetProperty("kind").GetString());
        Assert.Equal("song.wav", added.GetProperty("name").GetString());
        Assert.Equal(new FileInfo(file).Length, added.GetProperty("size").GetInt64());
        Assert.Equal("Music", added.GetProperty("libraryName").GetString());
        Assert.Equal("Uploaded", added.GetProperty("track").GetProperty("title").GetString());

        // The same audio again, and a file that is not audio: both in the history too.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=again.wav", Body(), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=notes.txt", Body(), Cancel)).StatusCode);
        history = await HistoryAsync(admin);
        Assert.Equal(3, history.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, history.GetProperty("active").GetInt32());
        Assert.Equal("FAILED", StatusOf(history, 0));
        Assert.Equal("invalid_format", history.GetProperty("items")[0].GetProperty("errorCode").GetString());
        Assert.Equal("DUPLICATE", StatusOf(history, 1));
        Assert.Equal("Uploaded", history.GetProperty("items")[1].GetProperty("track").GetProperty("title").GetString());

        // Each user sees their own.
        await _factory.CreateUserAsync("ana", permissions: Permissions.Upload);
        var ana = await _factory.CreateAuthenticatedClientAsync("ana");
        Assert.Equal(0, (await HistoryAsync(ana)).GetProperty("totalCount").GetInt32());

        // REST says the same, and forgetting them leaves nothing.
        var rest = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/me/uploads", ArcanistFactory.Json, Cancel);
        Assert.Equal(3, rest.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, (await admin.GraphQLAsync("mutation { clearMyUploads }")).GetProperty("data").GetProperty("clearMyUploads").GetInt32());
        Assert.Equal(0, (await HistoryAsync(admin)).GetProperty("totalCount").GetInt32());
    }

}
