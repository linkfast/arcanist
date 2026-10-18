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
    private readonly FakeUltimateAudio _audio = new();
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

    [Fact]
    public async Task Songs_go_to_the_users_own_Ultimate_Cloud_library()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Single", "s.wav"), 3, new FixtureTags("Single", "Soloist", "Single", "Soloist", TrackNumber: 1));
        _cloud.Plans["user-2"] = "Standard";
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: services =>
        {
            _cloud.Register(services);
            services.AddSingleton<IUltimateAudio>(_audio);
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);
        Guid single;
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            single = await db.Tracks.Where(t => t.Title == "Single").Select(t => t.Id).SingleAsync(Cancel);
        var upload = $"mutation {{ uploadTracksToMyCloud(input: {{trackIds: [\"{single}\"]}}) {{ status errorCode }} }}";

        // Sending a song gives away a copy of it: DOWNLOAD, and an account of their own.
        await _factory.CreateUserAsync("carla");
        Assert.Equal("forbidden", (await (await _factory.CreateAuthenticatedClientAsync("carla")).GraphQLAsync(upload)).ErrorCode());
        await _factory.CreateUserAsync("bruno", permissions: Permissions.Download);
        var bruno = await _factory.CreateAuthenticatedClientAsync("bruno");
        Assert.Equal("cloud_not_linked", (await bruno.GraphQLAsync(upload)).ErrorCode());

        _cloud.ApprovingAs = "user-2";
        await bruno.GraphQLAsync("mutation { startMyUltimateCloudLink(input: {libraryName: \"Mi Ultimate Cloud\"}) { state } }");
        _cloud.Approve = true;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await bruno.GraphQLAsync("query { myUltimateCloud { tier } }")).GetProperty("data").GetProperty("myUltimateCloud").GetProperty("tier").GetString() == "NONE")
        {
            Assert.True(DateTime.UtcNow < deadline, "Not linked");
            await Task.Delay(100, Cancel);
        }

        // Queued, then uploaded into their account's library (not the server's).
        var queued = (await bruno.GraphQLAsync(upload)).GetProperty("data").GetProperty("uploadTracksToMyCloud")[0];
        Assert.Equal("QUEUED", queued.GetProperty("status").GetString());
        var history = await UntilAsync(bruno, h => StatusOf(h, 0) is "UPLOADED" or "FAILED");
        Assert.Equal("UPLOADED", StatusOf(history, 0));
        Assert.Equal("ULTIMATE_CLOUD", history.GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.Equal("Single", history.GetProperty("items")[0].GetProperty("track").GetProperty("title").GetString());
        Assert.Contains(_cloud.AccountTracks["user-2"], t => t.Id.StartsWith("up-user-2-", StringComparison.Ordinal));
        Assert.Empty(_cloud.Tracks);

        // Once there, it is not uploaded again.
        await bruno.GraphQLAsync(upload);
        history = await UntilAsync(bruno, h => StatusOf(h, 0) is "ALREADY_IN_CLOUD" or "UPLOADED" or "FAILED");
        Assert.Equal("ALREADY_IN_CLOUD", StatusOf(history, 0));

        // A song only shared by another server goes only when that server allows downloads.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var source = await db.LibrarySources.SingleAsync(s => s.Library.Name == "Music", Cancel);
            var remote = new P2PRemote { NodeId = "00", Ticket = "{}", Name = "Other", LibraryId = source.LibraryId, Rules = new P2PRules { AllowDownload = false } };
            db.P2PRemotes.Add(remote);
            await db.SaveChangesAsync(Cancel);
            await db.LibrarySources.Where(s => s.Id == source.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Kind, LibrarySourceKind.Peer).SetProperty(x => x.Location, remote.Id.ToString()), Cancel);
            // Without the copy their own library may have catalogued by now (that one would make it "already there").
            await db.MediaFiles.Where(f => f.LibrarySource.Kind == LibrarySourceKind.UltimateCloud).ExecuteDeleteAsync(Cancel);
        }

        var refused = (await bruno.GraphQLAsync(upload)).GetProperty("data").GetProperty("uploadTracksToMyCloud")[0];
        Assert.Equal("FAILED", refused.GetProperty("status").GetString());
        Assert.Equal("download_not_allowed", refused.GetProperty("errorCode").GetString());

        // What is still on its way stays when the history is forgotten; the rest goes.
        Assert.Equal(3, (await bruno.GraphQLAsync("mutation { clearMyUploads }")).GetProperty("data").GetProperty("clearMyUploads").GetInt32());
    }
}
