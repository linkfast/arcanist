using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Setup;
using Arcanist.Core.Storage.S3;
using Arcanist.Core.Tests.Cloud;
using Arcanist.Core.Tests.Scanning;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Storage;

public sealed class S3SignatureTests
{
    // The examples of AWS's "Signature Version 4: authenticating requests (Authorization header)" for S3.
    private const string AccessKey = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static readonly DateTime When = new(2013, 5, 24, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Signs_a_GET_with_a_range_as_AWS_does()
    {
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = EmptyHash,
            ["x-amz-date"] = "20130524T000000Z",
        };

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, " +
            "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            S3Client.Authorization("GET", "/test.txt", "", headers, EmptyHash, When, "us-east-1", AccessKey, SecretKey));
    }

    [Fact]
    public void Signs_a_listing_with_its_query_as_AWS_does()
    {
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["x-amz-content-sha256"] = EmptyHash,
            ["x-amz-date"] = "20130524T000000Z",
        };
        var query = S3Client.CanonicalQuery(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["max-keys"] = "2", ["prefix"] = "J" });

        Assert.Equal("max-keys=2&prefix=J", query);
        Assert.EndsWith("Signature=34b48302e7b5fa45bde8084f4b7868a86f0a534bc59db6670ed5711ef69dc6f7",
            S3Client.Authorization("GET", "/", query, headers, EmptyHash, When, "us-east-1", AccessKey, SecretKey));
    }

    [Fact]
    public void Encodes_keys_as_SigV4_asks() =>
        Assert.Equal("%C3%81g%C3%A6tis%20byrjun/01%20Svefn%20%28live%29.wav", S3Client.EncodePath("Ágætis byrjun/01 Svefn (live).wav"));
}

/// <summary>
/// A disposable S3 server shared by the tests of a class: VersityGW (Apache-2.0), which checks signatures as S3 does.
/// MinIO no longer publishes images.
/// </summary>
public sealed class S3Container : IAsyncLifetime
{
    public const string User = "arcanist";
    public const string Password = "arcanist-secret";

    private IContainer? _container;

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public string Endpoint => $"http://{_container!.Hostname}:{_container.GetMappedPublicPort(7070)}";

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder("versity/versitygw:v1.9.0")
                .WithCommand("--access", User, "--secret", Password, "--port", ":7070", "posix", "/tmp")
                .WithPortBinding(7070, true)
                // Unsigned requests are refused: answering 403 means it is up.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/").ForPort(7070).ForStatusCode(HttpStatusCode.Forbidden)))
                .Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception e)
        {
            UnavailableReason = e.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

/// <summary>Libraries in an S3 bucket, against a real S3 server (skipped without Docker).</summary>
public sealed class S3StorageTests(S3Container s3) : IClassFixture<S3Container>, IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeUltimateCloud _cloud = new();
    private readonly string _staging = Directory.CreateTempSubdirectory("arcanist-s3-").FullName;
    private readonly string _bucket = $"music-{Guid.NewGuid():N}"[..20];
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private S3Client Client(string secret = S3Container.Password) =>
        new(new HttpClient(), new S3Connection(new Uri(s3.Endpoint), "us-east-1", _bucket, S3Container.User, secret, PathStyle: true), TimeProvider.System);

    private object Connection(string? secret = null, string? bucket = null) => new
    {
        endpoint = s3.Endpoint,
        region = "us-east-1",
        bucket = bucket ?? _bucket,
        prefix = "Music",
        accessKeyId = S3Container.User,
        secretAccessKey = secret ?? S3Container.Password,
        pathStyle = true,
    };

    private async Task PutAsync(string key, string file)
    {
        await using var stream = File.OpenRead(file);
        await Client().PutAsync(key, stream, stream.Length, "audio/wav", Cancel);
    }

    private async Task StartAsync(string plan = "Cloud")
    {
        _cloud.Plan = plan;
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: _cloud.Register);
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((await _admin.GraphQLAsync("query { ultimateCloud { plan { name } } }")).GetProperty("data").GetProperty("ultimateCloud").GetProperty("plan").ValueKind == JsonValueKind.Null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The account was not linked.");
            await Task.Delay(50, Cancel);
        }
    }

    [Fact]
    public async Task A_bucket_is_a_library_that_is_read_streamed_and_organized_in_place()
    {
        Assert.SkipUnless(s3.IsAvailable, $"The S3 server (Docker) is not available: {s3.UnavailableReason}");
        await Client().CreateBucketAsync(Cancel);
        var alpha = AudioFixtures.WriteWav(Path.Combine(_staging, "a.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", "Band", TrackNumber: 1));
        await PutAsync("Music/incoming/a.wav", alpha);
        await PutAsync("Music/Ágætis byrjun/01 Svefn (live).wav",
            AudioFixtures.WriteWav(Path.Combine(_staging, "s.wav"), 2, new FixtureTags("Svefn", "Sigur Rós", "Ágætis byrjun", "Sigur Rós", TrackNumber: 1)));
        await PutAsync("Music/.arcanist-trash/2026-01-01/old.wav", alpha);
        await PutAsync("Elsewhere/not-music.wav", alpha);
        await StartAsync();

        const string test = "mutation($i: S3ConnectionInput!) { testS3Connection(input: $i) { success errorCode audioFiles truncated } }";
        var tested = (await _admin.GraphQLAsync(test, new { i = Connection() })).GetProperty("data").GetProperty("testS3Connection");
        Assert.True(tested.GetProperty("success").GetBoolean());
        // Hidden keys (Arcanist's trash) and what is outside the prefix do not count.
        Assert.Equal(2, tested.GetProperty("audioFiles").GetInt32());
        Assert.Equal("s3_access_denied", (await _admin.GraphQLAsync(test, new { i = Connection(secret: "wrong") }))
            .GetProperty("data").GetProperty("testS3Connection").GetProperty("errorCode").GetString());
        Assert.Equal("s3_bucket_not_found", (await _admin.GraphQLAsync(test, new { i = Connection(bucket: "nothing-here") }))
            .GetProperty("data").GetProperty("testS3Connection").GetProperty("errorCode").GetString());

        var created = (await _admin.GraphQLAsync("mutation($i: CreateS3LibraryInput!) { createS3Library(input: $i) { id sources { kind location } } }",
            new { i = new { name = "Bucket", connection = Connection() } })).GetProperty("data").GetProperty("createS3Library");
        var source = created.GetProperty("sources")[0];
        Assert.Equal(("S3", $"s3://{_bucket}/Music"), (source.GetProperty("kind").GetString(), source.GetProperty("location").GetString()));
        await _factory.WaitForScansAsync(_admin);

        var tracks = (await _admin.GraphQLAsync("query { tracks(first: 10) { items { id title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().OrderBy(t => t.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Alpha", "Svefn"], tracks.Select(t => t.GetProperty("title").GetString()));

        // Streamed from the bucket, a range at a time.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{tracks[0].GetProperty("id").GetString()}");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1000, 1999);
        var response = await _admin.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(File.ReadAllBytes(alpha)[1000..2000], await response.Content.ReadAsByteArrayAsync(Cancel));

        // Organized in the bucket: moved to the template's path, nothing overwritten, nothing lost.
        var libraryId = Guid.Parse(created.GetProperty("id").GetString()!);
        await _admin.GraphQLAsync("mutation($id: ID!) { organizeLibrary(libraryId: $id) { id } }", new { id = libraryId });
        var run = await ArcanistFactory.WaitForOrganizeAsync(_admin, libraryId);
        Assert.Equal("COMPLETED", run.GetProperty("status").GetString());
        var keys = (await Client().ListAsync("Music/", null, 100, Cancel)).Objects.Select(o => o.Key).Order().ToList();
        Assert.Contains("Music/Band/Pulse/01 Alpha.wav", keys);
        Assert.Contains("Music/Sigur Rós/Ágætis byrjun/01 Svefn.wav", keys);
        Assert.DoesNotContain("Music/incoming/a.wav", keys);
    }

    [Fact]
    public async Task An_encrypted_library_keeps_names_and_music_unreadable_in_the_bucket()
    {
        Assert.SkipUnless(s3.IsAvailable, $"The S3 server (Docker) is not available: {s3.UnavailableReason}");
        await Client().CreateBucketAsync(Cancel);
        await StartAsync();
        var key = S3EncryptionKey();

        var invalid = await _admin.GraphQLAsync("mutation($i: CreateS3LibraryInput!) { createS3Library(input: $i) { id } }",
            new { i = new { name = "Bad", connection = Connection(), encryptionKey = "short" } });
        Assert.Equal("validation_failed", invalid.ErrorCode());

        var created = (await _admin.GraphQLAsync("mutation($i: CreateS3LibraryInput!) { createS3Library(input: $i) { id } }",
            new { i = new { name = "Vault", connection = Connection(), encryptionKey = key } })).GetProperty("data").GetProperty("createS3Library");
        var libraryId = created.GetProperty("id").GetString();
        await _factory.WaitForScansAsync(_admin);

        // Music comes in as uploads (or a drop folder, or organizing): encrypted on the way.
        var song = AudioFixtures.WriteWav(Path.Combine(_staging, "secret.wav"), 7,
            new FixtureTags("Secret Song", "Hidden Band", "Private Album", "Hidden Band", TrackNumber: 1));
        using (var upload = new ByteArrayContent(await File.ReadAllBytesAsync(song, Cancel)))
        {
            var response = await _admin.PostAsync($"/rest/v1/libraries/{libraryId}/uploads?fileName=secret.wav", upload, Cancel);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancel));
        }

        // Uploads are scanned a little later, after the batch: now, here.
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/scans", new { full = false }, Cancel);
        await _factory.WaitForScansAsync(_admin);
        var track = (await _admin.GraphQLAsync("query { tracks(first: 10) { items { id title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items")[0];
        Assert.Equal("Secret Song", track.GetProperty("title").GetString());

        // Played (a range) decrypted on the server.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{track.GetProperty("id").GetString()}");
        // Across a 64 KiB block when the file is that long.
        var length = new FileInfo(song).Length;
        var start = length > S3Encryption.BlockSize + 600 ? S3Encryption.BlockSize - 500 : length / 2;
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, start + 999);
        var streamed = await _admin.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, streamed.StatusCode);
        Assert.Equal(File.ReadAllBytes(song)[(int)start..(int)Math.Min(length, start + 1000)], await streamed.Content.ReadAsByteArrayAsync(Cancel));

        // In the bucket: neither the names nor the audio; only the key's marker, which says nothing of it.
        var objects = (await Client().ListAsync("Music/", null, 100, Cancel)).Objects;
        Assert.Contains(objects, o => o.Key == "Music/" + S3Storage.KeyMarker);
        var stored = Assert.Single(objects, o => o.Key != "Music/" + S3Storage.KeyMarker);
        foreach (var word in new[] { "Hidden", "Band", "Private", "Secret", ".wav" })
            Assert.DoesNotContain(word, stored.Key);
        using var raw = await Client().GetAsync(stored.Key, 0, null, Cancel);
        var bytes = await raw.Content.ReadAsByteArrayAsync(Cancel);
        Assert.DoesNotContain("RIFF", System.Text.Encoding.ASCII.GetString(bytes[..64]));
        Assert.DoesNotContain("Secret Song", System.Text.Encoding.UTF8.GetString(bytes));
        using (var marker = await Client().GetAsync("Music/" + S3Storage.KeyMarker, 0, null, Cancel))
            Assert.DoesNotContain(key.TrimEnd('='), await marker.Content.ReadAsStringAsync(Cancel));

        // Another key for the same prefix would hide that music and mix two keys: refused.
        var other = await _admin.GraphQLAsync("mutation($i: CreateS3LibraryInput!) { createS3Library(input: $i) { id } }",
            new { i = new { name = "Vault 2", connection = Connection(), encryptionKey = S3EncryptionKey() } });
        Assert.Equal("s3_wrong_key", other.ErrorCode());
    }

    private static string S3EncryptionKey() => S3Encryption.NewKey();

    [Fact]
    public async Task A_managed_extensions_library_works_without_the_Cloud_plan_and_cannot_be_deleted()
    {
        Assert.SkipUnless(s3.IsAvailable, $"The S3 server (Docker) is not available: {s3.UnavailableReason}");
        await Client().CreateBucketAsync(Cancel);
        await StartAsync(plan: "Standard");
        var managed = _factory.Services.GetRequiredService<Core.Application.Libraries.ManagedLibraries>();
        var connection = new Core.Application.Libraries.S3ConnectionInput(s3.Endpoint, "us-east-1", _bucket, "Tenant", S3Container.User,
            S3Container.Password, PathStyle: true);
        var key = S3Encryption.NewKey();

        var id = await managed.EnsureS3Async("cloud-storage", "main", "My music", connection, key, Cancel);
        await _factory.WaitForScansAsync(_admin);

        var library = (await _admin.GraphQLAsync("query($id: ID!) { library(id: $id) { name managedBy disabled canUpload } }", new { id }))
            .GetProperty("data").GetProperty("library");
        Assert.Equal(("My music", "cloud-storage", false, true), (library.GetProperty("name").GetString(), library.GetProperty("managedBy").GetString(),
            library.GetProperty("disabled").GetBoolean(), library.GetProperty("canUpload").GetBoolean()));

        // Without the Cloud plan, its bucket still works (encrypted on the way).
        var song = AudioFixtures.WriteWav(Path.Combine(_staging, "m.wav"), 9, new FixtureTags("Managed Song", "Band", "Hosted", "Band", TrackNumber: 1));
        using (var upload = new ByteArrayContent(await File.ReadAllBytesAsync(song, Cancel)))
            Assert.True((await _admin.PostAsync($"/rest/v1/libraries/{id}/uploads?fileName=m.wav", upload, Cancel)).IsSuccessStatusCode);
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{id}/scans", new { full = false }, Cancel);
        await _factory.WaitForScansAsync(_admin);
        Assert.Equal("Managed Song", (await _admin.GraphQLAsync("query { tracks(first: 5) { items { title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items")[0].GetProperty("title").GetString());
        Assert.DoesNotContain("Managed", Assert.Single((await Client().ListAsync("Tenant/", null, 10, Cancel)).Objects,
            o => o.Key != "Tenant/" + S3Storage.KeyMarker).Key);
        var wrongKey = await Assert.ThrowsAsync<Core.Application.ArcanistException.Conflict>(() =>
            managed.EnsureS3Async("cloud-storage", "main", "My music", connection, S3Encryption.NewKey(), Cancel));
        Assert.Equal("s3_wrong_key", wrongKey.Code);

        // Administrators cannot delete it.
        var delete = await _admin.DeleteAsync($"/rest/v1/libraries/{id}", Cancel);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("library_managed", (await delete.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());

        // Called again (new credentials, another name): the same library.
        Assert.Equal(id, await managed.EnsureS3Async("cloud-storage", "main", "Music", connection, key, Cancel));
        Assert.Equal("Music", (await _admin.GraphQLAsync("query($id: ID!) { library(id: $id) { name } }", new { id }))
            .GetProperty("data").GetProperty("library").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Buckets_need_the_Cloud_plan()
    {
        await StartAsync(plan: "Standard");
        Assert.Equal("s3_unavailable", (await _admin.GraphQLAsync(
            "mutation($i: S3ConnectionInput!) { testS3Connection(input: $i) { success } }",
            new { i = new { endpoint = "http://localhost:9", region = "", bucket = "music", prefix = "", accessKeyId = "a", secretAccessKey = "b", pathStyle = true } }))
            .ErrorCode());
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }
}
