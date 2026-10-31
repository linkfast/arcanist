using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage.S3;
using Arcanist.Core.Tests.Cloud;
using Arcanist.Core.Tests.Scanning;
using Arcanist.Core.Tests.Storage;

namespace Arcanist.Core.Tests.Backups;

/// <summary>Backups into an S3 bucket (the Cloud plan), against a real S3 server (skipped without Docker).</summary>
public sealed class BackupS3Tests(S3Container s3) : IClassFixture<S3Container>, IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeUltimateCloud _cloud = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-backup-s3-").FullName;
    private readonly string _bucket = $"backups-{Guid.NewGuid():N}"[..20];
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private S3Client Client() =>
        new(new HttpClient(), new S3Connection(new Uri(s3.Endpoint), "us-east-1", _bucket, S3Container.User, S3Container.Password, PathStyle: true), TimeProvider.System);

    private async Task StartAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", TrackNumber: 2));
        _cloud.Plan = "Cloud";
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: services =>
        {
            _cloud.Register(services);
            // The Cloud plan's storage comes with Ultimate Cloud Plus (here, its stand-in).
            Cloud.FakeCloudPlus.Register(services);
        });
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((await _admin.GraphQLAsync("query { ultimateCloud { plan { name } } }")).GetProperty("data").GetProperty("ultimateCloud").GetProperty("plan").ValueKind == JsonValueKind.Null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The account was not linked.");
            await Task.Delay(50, Cancel);
        }
    }

    private async Task<JsonElement> PostAsync(string path, object body)
    {
        var response = await _admin.PostAsJsonAsync(path, body, ArcanistFactory.Json, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<JsonElement> RunAsync(Guid backupId)
    {
        var id = (await PostAsync($"/rest/v1/backups/{backupId}/run", new { })).GetProperty("id").GetGuid();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var run = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/backups/runs/{id}", ArcanistFactory.Json, Cancel);
            if (run.GetProperty("finishedAt").ValueKind != JsonValueKind.Null)
                return run;
            Assert.True(DateTime.UtcNow < deadline, $"The run did not end: {run}");
            await Task.Delay(50, Cancel);
        }
    }

    private async Task<List<string>> KeysAsync(string prefix) =>
        (await Client().ListAsync(prefix, null, 1000, Cancel)).Objects.Select(o => o.Key).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task A_bucket_keeps_the_mirror_and_its_old_versions()
    {
        Assert.SkipUnless(s3.IsAvailable, $"The S3 server (Docker) is not available: {s3.UnavailableReason}");
        await Client().CreateBucketAsync(Cancel);
        await StartAsync();

        var destination = await PostAsync("/rest/v1/backups/destinations", new
        {
            name = "Bucket",
            kind = "S3",
            s3 = new
            {
                endpoint = s3.Endpoint, region = "us-east-1", bucket = _bucket, prefix = "arcanist", accessKeyId = S3Container.User,
                secretAccessKey = S3Container.Password, pathStyle = true,
            },
        });
        Assert.Equal($"s3://{_bucket}/arcanist", destination.GetProperty("location").GetString());
        var backup = await PostAsync("/rest/v1/backups", new
        {
            name = "Music", destinationId = destination.GetProperty("id").GetGuid(), scope = "SERVER", includeUserData = true,
            includeServer = false, schedule = "MANUAL", retentionDays = 30, enabled = true,
        });
        var backupId = backup.GetProperty("id").GetGuid();
        var root = $"arcanist/{backup.GetProperty("folder").GetString()}";

        var first = await RunAsync(backupId);
        Assert.Equal("COMPLETED", first.GetProperty("status").GetString());
        var keys = await KeysAsync(root + "/current/");
        Assert.Equal([$"{root}/current/Music/Band/Pulse/01 Alpha.wav", $"{root}/current/Music/Band/Pulse/02 Beta.wav"], keys);

        var alpha = Path.Combine(_music, "Band", "Pulse", "01 Alpha.wav");
        AudioFixtures.WriteWav(alpha, 7, new FixtureTags("Alpha", "Band", "Pulse", TrackNumber: 1));
        File.SetLastWriteTimeUtc(alpha, DateTime.UtcNow.AddMinutes(1));
        var second = await RunAsync(backupId);
        Assert.Equal((1, 1), (second.GetProperty("copied").GetInt32(), second.GetProperty("versioned").GetInt32()));
        Assert.Contains($"{root}/versions/{second.GetProperty("snapshot").GetString()}/Music/Band/Pulse/01 Alpha.wav", await KeysAsync(root + "/versions/"));
        Assert.Contains($"{root}/arcanist-backup.json", await KeysAsync(root + "/"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        _cloud.Dispose();
        try
        {
            Directory.Delete(_music, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
