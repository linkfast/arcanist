using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>Cloud Drop as a drop folder: its songs are brought, imported, and leave Cloud Drop or stay there.</summary>
public sealed class CloudDropTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeUltimateCloud _cloud = new();
    private readonly FakeUltimateAudio _audio = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-clouddrop-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private string _sourceId = null!;

    private async Task StartAsync()
    {
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
            ["Arcanist:Organization:StableSeconds"] = "0",
            ["Arcanist:Organization:PollSeconds"] = "1",
        }, services: services =>
        {
            _cloud.Register(services);
            services.AddSingleton<IUltimateAudio>(_audio);
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        _sourceId = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0]
            .GetProperty("sources")[0].GetProperty("id").GetString()!;

        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitAsync(async () => (await _admin.GraphQLAsync("query { ultimateCloud { plan { name } } }"))
            .GetProperty("data").GetProperty("ultimateCloud").GetProperty("plan").ValueKind != JsonValueKind.Null, "The account was not linked");
    }

    private async Task<HttpResponseMessage> AddCloudDropFolderAsync(string mode) =>
        await _admin.PostAsJsonAsync("/rest/v1/drop-folders", new
        {
            path = (string?)null,
            librarySourceId = _sourceId,
            enabled = true,
            duplicatePolicy = "SKIP",
            convertTo = (string?)null,
            convertBitRate = (int?)null,
            convertLossy = false,
            mode,
            kind = "ULTIMATE_CLOUD_DROP",
        }, ArcanistFactory.Json, Cancel);

    private List<string> Music() =>
        Directory.EnumerateFiles(_music, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)).Where(f => !f.StartsWith('.')).Order().ToList();

    private async Task<List<CloudDropFetch>> FetchesAsync()
    {
        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        return await db.CloudDropFetches.AsNoTracking().ToListAsync(Cancel);
    }

    private static async Task WaitAsync(Func<Task<bool>> done, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await done())
        {
            Assert.True(DateTime.UtcNow < deadline, message);
            await Task.Delay(200, Cancel);
        }
    }

    [Fact]
    public async Task Moving_brings_the_songs_into_the_library_and_takes_them_off_Cloud_Drop()
    {
        _cloud.AddDropTrack("drop-1", "Drift", "Haze", "Low Tide", 1, AudioFixtures.Music(bpm: 120, seconds: 3, chordSeed: 1));
        await StartAsync();

        // Cloud Drop is no library to add.
        Assert.Equal("validation_failed", (await _admin.GraphQLAsync("mutation { addUltimateCloudLibrary(input: {collection: CLOUD_DROP}) { state } }")).ErrorCode());

        var added = await AddCloudDropFolderAsync("MOVE");
        Assert.True(added.StatusCode == HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(HttpStatusCode.BadRequest, (await AddCloudDropFolderAsync("MOVE")).StatusCode);

        await WaitAsync(async () => (await FetchesAsync()).Any(f => f.State == CloudDropFetchState.Removed), "The song did not leave Cloud Drop");
        Assert.Contains("drop-1", _cloud.RemovedTracks);
        Assert.Contains("01 Drift.wav", Music());
    }

    [Fact]
    public async Task Copying_leaves_the_songs_in_Cloud_Drop_and_brings_each_once()
    {
        _cloud.AddDropTrack("drop-2", "Undertow", "Haze", "Low Tide", 2, AudioFixtures.Music(bpm: 100, seconds: 3, chordSeed: 2));
        await StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await AddCloudDropFolderAsync("COPY")).StatusCode);

        await WaitAsync(async () => (await FetchesAsync()).Any(f => f.State == CloudDropFetchState.Kept), "The song was not kept");
        Assert.Contains("02 Undertow.wav", Music());
        Assert.Empty(_cloud.RemovedTracks);
        Assert.Single(_cloud.DropTracks);

        // Looked at again, nothing more comes.
        await _factory.Services.GetRequiredService<CloudDropWorker>().RunAsync(Cancel);
        await _factory.WaitForImportsAsync();
        Assert.Single(Music());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_music, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
