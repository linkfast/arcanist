using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Uploads;

/// <summary>Who uploaded each song: kept with the copy the upload brought, shown on the track and on that copy.</summary>
public sealed class TrackUploaderTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string Tracks = "query { tracks(first: 10) { items { title uploadedBy { id name } files { uploadedBy { name } } } } }";

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-uploader-").FullName;
    private readonly string _staging = Directory.CreateTempSubdirectory("arcanist-uploader-staging-").FullName;
    private ArcanistFactory _factory = null!;

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        foreach (var folder in new[] { _music, _staging })
            try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    private static async Task<Dictionary<string, JsonElement>> TracksAsync(HttpClient client) =>
        (await client.GraphQLAsync(Tracks)).GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray()
        .ToDictionary(t => t.GetProperty("title").GetString()!);

    [Fact]
    public async Task A_song_says_who_uploaded_it_and_one_a_scan_found_says_nobody()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Found.wav"), 1, new FixtureTags("Found", "Band", "Here", "Band", TrackNumber: 1));
        _factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2" });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        var admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(admin);
        var libraryId = (await admin.GraphQLAsync("query { libraries { id } }")).GetProperty("data").GetProperty("libraries")[0].GetProperty("id").GetString();

        var features = (await admin.GraphQLAsync("query { serverInfo { features } }")).GetProperty("data").GetProperty("serverInfo").GetProperty("features")
            .EnumerateArray().Select(f => f.GetString());
        Assert.Contains("TRACK_UPLOADER", features);

        var ana = await _factory.CreateUserAsync("ana", permissions: Permissions.Upload);
        var anaClient = await _factory.CreateAuthenticatedClientAsync("ana");
        var file = AudioFixtures.WriteWav(Path.Combine(_staging, "sent.wav"), 2, new FixtureTags("Sent", "Band", "Here", "Band", TrackNumber: 2));
        var body = new ByteArrayContent(File.ReadAllBytes(file)) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } };
        Assert.Equal(HttpStatusCode.Created, (await anaClient.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=sent.wav", body, Cancel)).StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        Dictionary<string, JsonElement> tracks;
        while (!(tracks = await TracksAsync(admin)).ContainsKey("Sent"))
        {
            Assert.True(DateTime.UtcNow < deadline, "The upload was not catalogued");
            await Task.Delay(100, Cancel);
        }

        // Everyone who sees the song sees who uploaded it, on the song and on its copy.
        var sent = tracks["Sent"].GetProperty("uploadedBy");
        Assert.Equal((ana.Id.ToString(), "ana"), (sent.GetProperty("id").GetString(), sent.GetProperty("name").GetString()));
        Assert.Equal("ana", tracks["Sent"].GetProperty("files")[0].GetProperty("uploadedBy").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, tracks["Found"].GetProperty("uploadedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, tracks["Found"].GetProperty("files")[0].GetProperty("uploadedBy").ValueKind);

        // Forgetting the history of uploads does not forget who uploaded the song, nor does a scan of the library.
        await anaClient.GraphQLAsync("mutation { clearMyUploads }");
        await admin.GraphQLAsync($"mutation {{ startScan(libraryId: \"{libraryId}\", full: true) {{ id }} }}");
        await _factory.WaitForScansAsync(admin);
        Assert.Equal("ana", (await TracksAsync(anaClient))["Sent"].GetProperty("uploadedBy").GetProperty("name").GetString());
    }
}
