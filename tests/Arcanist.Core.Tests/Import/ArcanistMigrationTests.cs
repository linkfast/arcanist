using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.ServerMigration;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.ServerMigration;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Import;

/// <summary>
/// One Arcanist brought into another through the export API, with a migration code: the source's in-memory server
/// answers the target's HTTP client, so nothing reaches the network.
/// </summary>
public sealed class ArcanistMigrationTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string SourceUrl = "http://old-server.test";

    private readonly string _sourceMusic = Directory.CreateTempSubdirectory("arcanist-migrate-source-").FullName;
    private readonly string _targetMusic = Directory.CreateTempSubdirectory("arcanist-migrate-target-").FullName;
    private readonly List<ArcanistFactory> _factories = [];
    private ArcanistFactory _source = null!;
    private HttpClient _sourceAdmin = null!;
    private string _trackId = null!;

    /// <summary>The source: two songs and a cover, a user with a rating, plays, a playlist and a wish.</summary>
    private async Task StartSourceAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_sourceMusic, "Band", "Pulse", "01 Alpha.wav"), 1, new FixtureTags("Alpha", "Band", "Pulse", "Band", TrackNumber: 1));
        AudioFixtures.WriteWav(Path.Combine(_sourceMusic, "Band", "Pulse", "02 Beta.wav"), 2, new FixtureTags("Beta", "Band", "Pulse", "Band", TrackNumber: 2));
        File.WriteAllBytes(Path.Combine(_sourceMusic, "Band", "Pulse", "cover.png"), AudioFixtures.Png(3));

        _source = Track(new ArcanistFactory());
        await _source.CompleteSetupAsync(new SetupLibrary("Music", [_sourceMusic], VisibleToAll: false));
        _sourceAdmin = await _source.CreateAuthenticatedClientAsync();
        await _source.WaitForScansAsync(_sourceAdmin);

        var ana = await _source.CreateUserAsync("ana", permissions: Permissions.Download | Permissions.Upload);
        var libraryId = (await _sourceAdmin.GraphQLAsync("query { libraries { id } }")).GetProperty("data").GetProperty("libraries")[0].GetProperty("id").GetString();
        await _sourceAdmin.GraphQLAsync("mutation($id: ID!, $l: [ID!]!) { setUserLibraries(id: $id, input: { libraryIds: $l }) { id } }",
            new { id = ana.Id, l = new[] { libraryId } });

        var anaClient = await _source.CreateAuthenticatedClientAsync("ana");
        _trackId = (await anaClient.GraphQLAsync("query { tracks(first: 10) { items { id title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray()
            .Single(t => t.GetProperty("title").GetString() == "Alpha").GetProperty("id").GetString()!;
        await anaClient.GraphQLAsync("mutation($t: ID!) { rateTrack(trackId: $t, rating: 4) { id } }", new { t = _trackId });
        await anaClient.GraphQLAsync("mutation($t: ID!) { recordPlay(input: { trackId: $t, playedMs: 60000 }) { counted } }", new { t = _trackId });
        await anaClient.GraphQLAsync("mutation($t: ID!) { createPlaylist(input: { name: \"Road\", isPublic: false, trackIds: [$t] }) { id } }", new { t = _trackId });
        await anaClient.GraphQLAsync("mutation { addWish(input: { title: \"Gamma\", artist: \"Band\" }) { id } }");
    }

    private ArcanistFactory Track(ArcanistFactory factory)
    {
        _factories.Add(factory);
        return factory;
    }

    /// <summary>A server whose HTTP to other Arcanist servers reaches the source.</summary>
    private ArcanistFactory Target() => Track(new ArcanistFactory(services: services =>
        services.AddHttpClient(ArcanistSourceClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _source.Server.CreateHandler())));

    private async Task<string> CodeAsync() =>
        (await _sourceAdmin.GraphQLAsync("mutation { createMigrationCode { code } }"))
        .GetProperty("data").GetProperty("createMigrationCode").GetProperty("code").GetString()!;

    [Fact]
    public async Task The_export_API_needs_a_valid_migration_code()
    {
        await StartSourceAsync();
        var client = _source.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/rest/v1/export/manifest", Cancel)).StatusCode);
        var code = await CodeAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/rest/v1/export/manifest") { Headers = { { ExportApi.CodeHeader, code.ToLowerInvariant().Replace("-", " ") } } };
        var response = await client.SendAsync(request, Cancel);
        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(["Music"], manifest.GetProperty("libraries").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.Equal(["admin", "ana"], manifest.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("userName").GetString()));

        // Only the code's hash is kept, and a revoked code stops working.
        var codes = (await _sourceAdmin.GraphQLAsync("query { migrationCodes { id active } }")).GetProperty("data").GetProperty("migrationCodes");
        await using (var scope = _source.Services.CreateAsyncScope())
            Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<ArcanistDbContext>().MigrationGrants, g => g.CodeHash.Contains(code, StringComparison.OrdinalIgnoreCase));
        await _sourceAdmin.GraphQLAsync("mutation($id: ID!) { revokeMigrationCode(id: $id) }", new { id = codes[0].GetProperty("id").GetString() });
        request = new HttpRequestMessage(HttpMethod.Get, "/rest/v1/export/manifest") { Headers = { { ExportApi.CodeHeader, code } } };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request, Cancel)).StatusCode);
    }

    private static async Task<JsonElement> WaitForMigrationAsync(HttpClient admin)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var job = (await admin.GraphQLAsync(
                    "query { arcanistImports { id status errorCode usersCreated usersMapped librariesCreated settingsApplied filesTotal filesCopied filesPresent filesFailed ratings plays playlists wishes unmatched } }"))
                .GetProperty("data").GetProperty("arcanistImports")[0];
            if (job.GetProperty("status").GetString() is "COMPLETED" or "FAILED" or "CANCELLED")
                return job;
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Still {job}");
            await Task.Delay(100, Cancel);
        }
    }

    [Fact]
    public async Task Brings_another_server_users_music_and_data()
    {
        await StartSourceAsync();
        var code = await CodeAsync();
        var target = Target();
        await target.CompleteSetupAsync();
        var admin = await target.CreateAuthenticatedClientAsync();

        var analysis = (await admin.GraphQLAsync(
                "query($i: ArcanistSourceInput!) { analyzeArcanistServer(input: $i) { serverName libraries { id name files } users { id userName exists } } }",
                new { i = new { url = SourceUrl, code } }))
            .GetProperty("data").GetProperty("analyzeArcanistServer");
        var library = analysis.GetProperty("libraries")[0];
        Assert.Equal(2, library.GetProperty("files").GetInt32());
        var users = analysis.GetProperty("users").EnumerateArray().ToDictionary(u => u.GetProperty("userName").GetString()!, u => u);
        Assert.True(users["admin"].GetProperty("exists").GetBoolean());
        Assert.False(users["ana"].GetProperty("exists").GetBoolean());

        var copied = Path.Combine(_targetMusic, "Copied");
        Directory.CreateDirectory(copied);
        var started = await admin.GraphQLAsync("mutation($i: StartArcanistImportInput!) { startArcanistImport(input: $i) { id status } }", new
        {
            i = new
            {
                source = new { url = SourceUrl, code },
                libraries = new[] { new { sourceLibraryId = library.GetProperty("id").GetString(), newLibraryName = "Copied", newLibraryPath = copied } },
                users = users.Values.Select(u => u.GetProperty("id").GetString()).ToArray(),
                copyPasswords = true,
                permissions = true,
                userData = true,
                settings = true,
                extensions = false,
            },
        });
        Assert.Null(started.ErrorCode());

        var job = await WaitForMigrationAsync(admin);
        Assert.Equal("COMPLETED", job.GetProperty("status").GetString());
        Assert.Equal(1, job.GetProperty("usersCreated").GetInt32());
        Assert.Equal(1, job.GetProperty("usersMapped").GetInt32());
        Assert.Equal(1, job.GetProperty("librariesCreated").GetInt32());
        Assert.True(job.GetProperty("settingsApplied").GetBoolean());
        Assert.Equal(3, job.GetProperty("filesTotal").GetInt32());
        Assert.Equal(3, job.GetProperty("filesCopied").GetInt32());
        Assert.Equal(1, job.GetProperty("ratings").GetInt32());
        Assert.Equal(1, job.GetProperty("plays").GetInt32());
        Assert.Equal(1, job.GetProperty("playlists").GetInt32());
        Assert.Equal(1, job.GetProperty("wishes").GetInt32());

        // The files with their paths, the cover too.
        Assert.True(File.Exists(Path.Combine(copied, "Band", "Pulse", "01 Alpha.wav")));
        Assert.True(File.Exists(Path.Combine(copied, "Band", "Pulse", "cover.png")));

        // Ana signs in with her password, sees the library she had and finds her rating, play and playlist.
        var ana = await target.CreateAuthenticatedClientAsync("ana");
        var tracks = (await ana.GraphQLAsync("query { tracks(first: 10) { items { title rating playCount } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().ToDictionary(t => t.GetProperty("title").GetString()!);
        Assert.Equal(4, tracks["Alpha"].GetProperty("rating").GetInt32());
        Assert.Equal(1, tracks["Alpha"].GetProperty("playCount").GetInt32());
        var playlists = (await ana.GraphQLAsync("query { playlists { name trackCount } }")).GetProperty("data").GetProperty("playlists");
        Assert.Equal("Road", playlists[0].GetProperty("name").GetString());
        var wishes = (await ana.GraphQLAsync("query { wishList { items { title } } }")).GetProperty("data").GetProperty("wishList").GetProperty("items");
        Assert.Equal("Gamma", wishes[0].GetProperty("title").GetString());

        // Reading the source never counted as plays there.
        var sourceAna = await _source.CreateAuthenticatedClientAsync("ana");
        var there = (await sourceAna.GraphQLAsync("query($id: ID!) { track(id: $id) { playCount } }", new { id = _trackId }))
            .GetProperty("data").GetProperty("track");
        Assert.Equal(1, there.GetProperty("playCount").GetInt32());

        // Again, into the library it made: nothing doubles (files present, plays once, playlist by name).
        var copiedId = (await admin.GraphQLAsync("query { libraries { id name } }")).GetProperty("data").GetProperty("libraries").EnumerateArray()
            .Single(l => l.GetProperty("name").GetString() == "Copied").GetProperty("id").GetString();
        var again = await admin.GraphQLAsync("mutation($i: StartArcanistImportInput!) { startArcanistImport(input: $i) { id } }", new
        {
            i = new
            {
                source = new { url = SourceUrl, code },
                libraries = new[] { new { sourceLibraryId = library.GetProperty("id").GetString(), libraryId = copiedId } },
                users = users.Values.Select(u => u.GetProperty("id").GetString()).ToArray(),
                copyPasswords = true,
                permissions = true,
                userData = true,
                settings = false,
                extensions = false,
            },
        });
        Assert.Null(again.ErrorCode());
        job = await WaitForMigrationAsync(admin);
        Assert.Equal("COMPLETED", job.GetProperty("status").GetString());
        Assert.Equal(3, job.GetProperty("filesPresent").GetInt32());
        Assert.Equal(0, job.GetProperty("plays").GetInt32());
        Assert.Equal(0, job.GetProperty("playlists").GetInt32());
        Assert.Equal(0, job.GetProperty("wishes").GetInt32());
        tracks = (await ana.GraphQLAsync("query { tracks(first: 10) { items { title playCount } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().ToDictionary(t => t.GetProperty("title").GetString()!);
        Assert.Equal(1, tracks["Alpha"].GetProperty("playCount").GetInt32());
    }

    [Fact]
    public async Task The_setup_wizard_brings_another_server_and_waits_for_it()
    {
        await StartSourceAsync();
        var code = await CodeAsync();
        var target = Target();
        var client = target.CreateClient();

        var analysis = await (await client.PostAsJsonAsync("/rest/v1/setup/arcanist/analyze", new { url = SourceUrl, code }, ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        var library = analysis.GetProperty("libraries")[0].GetProperty("id").GetGuid();
        var users = analysis.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();

        var copied = Path.Combine(_targetMusic, "Music");
        Directory.CreateDirectory(copied);
        var response = await client.PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Sqlite, null),
            new SetupAdmin(ArcanistFactory.AdminUserName, "another long password", null),
            [new SetupLibrary("Music", [copied])],
            ShareAnonymousStatistics: false,
            Language: null,
            RatingMode: RatingMode.Stars,
            Arcanist: new SetupArcanistImport(new ArcanistSourceInput(SourceUrl, null, null, code), [new SetupArcanistLibrary(library, copied)], users,
                CopyPasswords: true, Permissions: true, UserData: true, Settings: true, Extensions: false),
            AcceptedTerms: Arcanist.Core.Legal.LegalService.CurrentVersion), ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();

        // The admin of the same name is the new one: their password is the one typed in the wizard.
        var admin = await target.CreateAuthenticatedClientAsync(password: "another long password");
        var job = await WaitForMigrationAsync(admin);
        Assert.Equal("COMPLETED", job.GetProperty("status").GetString());
        Assert.Equal(1, job.GetProperty("usersMapped").GetInt32());
        Assert.True(File.Exists(Path.Combine(copied, "Band", "Pulse", "02 Beta.wav")));

        // Once it ended, the wizard lets the panel open.
        for (var i = 0; i < 100; i++)
        {
            var status = await client.GetFromJsonAsync<JsonElement>("/rest/v1/setup/status", ArcanistFactory.Json, Cancel);
            if (!status.GetProperty("migrating").GetBoolean())
                return;
            await Task.Delay(50, Cancel);
        }

        Assert.Fail("The setup still shows the migration running.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
        foreach (var folder in new[] { _sourceMusic, _targetMusic })
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
