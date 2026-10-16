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

/// <summary>Users who link their own Ultimate Cloud account: their music, plays, StarTracks, and an administrator's plan.</summary>
public sealed class UserCloudAccountsTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private const string Mine = "query { myUltimateCloud { enabled state account tier errorCode libraryId plan { name } pending { userCode } } }";

    private readonly FakeUltimateCloud _cloud = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-user-cloud-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _single;
    private Guid _singleAcoustId;

    private UltimateCloudReporter Reporter => _factory.Services.GetRequiredService<UltimateCloudReporter>();

    private async Task StartAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Single", "s.wav"), 3, new FixtureTags("Single", "Soloist", "Single", "Soloist", TrackNumber: 1));
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = FakeUltimateCloud.ApiUrl,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
        }, services: _cloud.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var track = await db.Tracks.Include(t => t.Recording).SingleAsync(Cancel);
            track.Recording.AcoustId = Guid.NewGuid();
            await db.SaveChangesAsync(Cancel);
            (_single, _singleAcoustId) = (track.Id, track.Recording.AcoustId.Value);
        }

        // The plays made before are never sent: the reporter starts from here.
        await Reporter.RunAsync(Cancel);
    }

    private static async Task<JsonElement> MineAsync(HttpClient client) =>
        (await client.GraphQLAsync(Mine)).GetProperty("data").GetProperty("myUltimateCloud");

    private async Task<JsonElement> LinkAsync(HttpClient client)
    {
        _cloud.ApprovingAs = "user-2";
        var started = (await client.GraphQLAsync("mutation { startMyUltimateCloudLink(input: {libraryName: \"Mi Ultimate Cloud\"}) { state pending { userCode } } }"))
            .GetProperty("data").GetProperty("startMyUltimateCloudLink");
        Assert.Equal("PENDING", started.GetProperty("state").GetString());
        Assert.Equal("WDJB-MJHT", started.GetProperty("pending").GetProperty("userCode").GetString());
        _cloud.Approve = true;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await MineAsync(client);
            if (status.GetProperty("state").GetString() == "LINKED" && status.GetProperty("plan").ValueKind != JsonValueKind.Null
                && status.GetProperty("libraryId").ValueKind != JsonValueKind.Null)
                return status;
            Assert.True(DateTime.UtcNow < deadline, $"Still {status}");
            await Task.Delay(100, Cancel);
        }
    }

    private static async Task<List<string?>> TitlesAsync(HttpClient client) =>
        (await client.GraphQLAsync("query { tracks(first: 50) { items { title } } }"))
        .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()).ToList();

    [Fact]
    public async Task Each_user_links_their_own_account_and_gets_their_music_plays_and_stars()
    {
        _cloud.Plans["user-2"] = "Standard";
        _cloud.AddAccountTrack("user-2", "b1", "Bruno's Song", "Bruno", "Mine", 1, Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray());
        await StartAsync();
        await _factory.CreateUserAsync("bruno");
        var bruno = await _factory.CreateAuthenticatedClientAsync("bruno");

        Assert.Equal("UNLINKED", (await MineAsync(bruno)).GetProperty("state").GetString());
        var status = await LinkAsync(bruno);
        Assert.Equal("bruno", status.GetProperty("account").GetString());
        Assert.Equal("FREE", status.GetProperty("tier").GetString());
        // The server's account is another matter.
        Assert.Equal("UNLINKED", (await _admin.GraphQLAsync("query { ultimateCloud { state } }"))
            .GetProperty("data").GetProperty("ultimateCloud").GetProperty("state").GetString());

        // Their music is a library only they see, administrators included.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!(await TitlesAsync(bruno)).Contains("Bruno's Song"))
        {
            Assert.True(DateTime.UtcNow < deadline, "The personal library was not scanned");
            await Task.Delay(100, Cancel);
        }

        var libraries = (await bruno.GraphQLAsync("query { libraries { id name personal } }")).GetProperty("data").GetProperty("libraries").EnumerateArray().ToList();
        var personal = Assert.Single(libraries, l => l.GetProperty("personal").GetBoolean());
        Assert.Equal("Mi Ultimate Cloud", personal.GetProperty("name").GetString());
        Assert.Equal(status.GetProperty("libraryId").GetString(), personal.GetProperty("id").GetString());
        Assert.DoesNotContain("Bruno's Song", await TitlesAsync(_admin));
        Assert.DoesNotContain("Mi Ultimate Cloud", (await _admin.GraphQLAsync("query { libraries { name } }"))
            .GetProperty("data").GetProperty("libraries").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Guid songId;
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            songId = await db.Tracks.Where(t => t.Title == "Bruno's Song").Select(t => t.Id).SingleAsync(Cancel);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/stream/{songId}", Cancel)).StatusCode);
        Assert.Equal(3000, (await bruno.GetByteArrayAsync($"/rest/v1/stream/{songId}", Cancel)).Length);

        // Their plays go to their account; the administrator's (with no account) to none.
        var played = await bruno.PostAsJsonAsync("/rest/v1/plays",
            new { trackId = _single, playedMs = 60_000, playedAt = DateTime.UtcNow.AddMinutes(-1), client = "tests" }, ArcanistFactory.Json, Cancel);
        Assert.True(played.IsSuccessStatusCode);
        await _admin.PostAsJsonAsync("/rest/v1/plays",
            new { trackId = _single, playedMs = 60_000, playedAt = DateTime.UtcNow.AddMinutes(-2), client = "tests" }, ArcanistFactory.Json, Cancel);
        await Reporter.RunAsync(Cancel);
        Assert.Equal(("user-2", _singleAcoustId.ToString()), Assert.Single(_cloud.PlaysBy));

        // Their stars are their StarTracks.
        await bruno.GraphQLAsync("mutation($t: ID!) { rateTrack(trackId: $t, rating: 5) { id } }", new { t = _single });
        Reporter.StarsChanged();
        await Reporter.RunAsync(Cancel);
        Assert.Equal([_singleAcoustId.ToString()], _cloud.AccountStars["user-2"].Keys);
        Assert.Empty(_cloud.Stars);

        // Unlinking ends the session and takes the library away.
        var unlinked = (await bruno.GraphQLAsync("mutation { unlinkMyUltimateCloud { state libraryId } }")).GetProperty("data").GetProperty("unlinkMyUltimateCloud");
        Assert.Equal("UNLINKED", unlinked.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("libraryId").ValueKind);
        Assert.Single(_cloud.Revoked);
        Assert.DoesNotContain("Bruno's Song", await TitlesAsync(bruno));
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            Assert.False(await db.Libraries.AnyAsync(l => l.OwnerUserId != null, Cancel));
            Assert.False(await db.Settings.AnyAsync(s => s.Key.StartsWith(SettingKeys.UltimateCloudUserPrefix), Cancel));
        }
    }

    [Fact]
    public async Task An_administrator_s_own_Cloud_plan_unlocks_the_paid_features()
    {
        await StartAsync();
        Assert.Equal("NONE", (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", Cancel)).GetProperty("cloudTier").GetString());

        var status = await LinkAsync(_admin);
        Assert.Equal("PAID", status.GetProperty("tier").GetString());
        Assert.Equal("PAID", (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", Cancel)).GetProperty("cloudTier").GetString());

        // It is one of the accounts music can go to, and a library syncs with it.
        Guid adminId;
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
            adminId = await db.Users.Where(u => u.UserName == ArcanistFactory.AdminUserName).Select(u => u.Id).SingleAsync(Cancel);
        var account = Assert.Single((await _admin.GraphQLAsync("query { ultimateCloudAccounts { id userId } }"))
            .GetProperty("data").GetProperty("ultimateCloudAccounts").EnumerateArray());
        Assert.Equal(("user-2", adminId.ToString()), (account.GetProperty("id").GetString(), account.GetProperty("userId").GetString()));

        var library = (await _admin.GraphQLAsync("query { libraries { id personal } }")).GetProperty("data").GetProperty("libraries").EnumerateArray()
            .Single(l => !l.GetProperty("personal").GetBoolean()).GetProperty("id").GetString();
        const string set = "mutation($id: ID!, $a: ID) { setLibraryCloudSync(libraryId: $id, input: {mode: UPLOAD, accountUserId: $a}) { mode accountUserId } }";
        // Not with the server's account, which is not linked.
        Assert.Equal("cloud_sync_unavailable", (await _admin.GraphQLAsync(set, new { id = library, a = (Guid?)null })).ErrorCode());
        var sync = (await _admin.GraphQLAsync(set, new { id = library, a = adminId })).GetProperty("data").GetProperty("setLibraryCloudSync");
        Assert.Equal(("UPLOAD", adminId.ToString()), (sync.GetProperty("mode").GetString(), sync.GetProperty("accountUserId").GetString()));

        // Another user's account is theirs alone.
        var other = await _factory.CreateUserAsync("carla", UserRole.Admin);
        Assert.Equal("validation_failed", (await _admin.GraphQLAsync(set, new { id = library, a = other.Id })).ErrorCode());
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
