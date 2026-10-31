using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

public sealed class UltimateCloudTests : IAsyncDisposable
{
    private const string Status = """
        query { ultimateCloud { state account pending { userCode verificationUri verificationUriComplete } plan { name usedBytes readOnly } tier capabilities errorCode } }
        """;

    private readonly FakeUltimateCloud _cloud = new();
    private ArcanistFactory _factory = null!;

    private async Task<HttpClient> StartAsync(bool configured = true, bool dlna = false)
    {
        _factory = new ArcanistFactory(new Dictionary<string, string?>
        {
            ["Arcanist:UltimateCloud:ApiUrl"] = configured ? FakeUltimateCloud.ApiUrl : null,
            ["Arcanist:UltimateCloud:Authority"] = FakeUltimateCloud.Authority,
            ["Arcanist:Extensions:dlna:Enabled"] = dlna ? "true" : null,
        }, services: _cloud.Register);
        await _factory.CompleteSetupAsync();
        return await _factory.CreateAuthenticatedClientAsync();
    }

    private static async Task<JsonElement> StatusAsync(HttpClient admin) =>
        (await admin.GraphQLAsync(Status)).GetProperty("data").GetProperty("ultimateCloud");

    private static async Task<JsonElement> WaitForStateAsync(HttpClient admin, string state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var status = await StatusAsync(admin);
            if (status.GetProperty("state").GetString() == state && status.GetProperty("pending").ValueKind == JsonValueKind.Null
                && (state != "LINKED" || status.GetProperty("plan").ValueKind != JsonValueKind.Null))
                return status;
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Still {status}");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(UltimateCloudCollection.Library, "Ana", null, "Ana's Ultimate Cloud")]
    [InlineData(UltimateCloudCollection.Library, "Ana", "es", "Ultimate Cloud de Ana")]
    [InlineData(UltimateCloudCollection.CloudDrop, "Ana", "es-PE", "Cloud Drop de Ana")]
    [InlineData(UltimateCloudCollection.CloudDrop, "Ana", "en", "Ana's Cloud Drop")]
    [InlineData(UltimateCloudCollection.Library, null, "es", "Ultimate Cloud")]
    public void Cloud_libraries_are_named_after_the_account_s_owner(UltimateCloudCollection collection, string? owner, string? language, string expected) =>
        Assert.Equal(expected, Arcanist.Core.Application.Cloud.UltimateCloudService.DefaultName(collection, owner, language));

    [Fact]
    public async Task Without_an_address_linking_is_unavailable()
    {
        var admin = await StartAsync(configured: false);

        var status = await StatusAsync(admin);
        Assert.Equal("NOT_CONFIGURED", status.GetProperty("state").GetString());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
        Assert.Equal("cloud_not_configured", (await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }")).ErrorCode());
    }

    [Fact]
    public async Task Linking_with_a_code_reads_the_plan_and_enables_the_capabilities()
    {
        var admin = await StartAsync();

        var started = await admin.GraphQLAsync("mutation { startUltimateCloudLink { state pending { userCode verificationUriComplete } } }");
        var link = started.GetProperty("data").GetProperty("startUltimateCloudLink");
        Assert.Equal("PENDING", link.GetProperty("state").GetString());
        Assert.Equal("WDJB-MJHT", link.GetProperty("pending").GetProperty("userCode").GetString());
        Assert.Equal("https://auth.test/device?user_code=WDJB-MJHT", link.GetProperty("pending").GetProperty("verificationUriComplete").GetString());

        _cloud.Approve = true;
        var status = await WaitForStateAsync(admin, "LINKED");
        Assert.Equal("ana", status.GetProperty("account").GetString());
        Assert.Equal("Cloud", status.GetProperty("plan").GetProperty("name").GetString());
        Assert.Equal("PAID", status.GetProperty("tier").GetString());
        // A linked account uses Ultimate Cloud as a library source; the Cloud plan's features need Ultimate Cloud Plus.
        var capabilities = status.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains("ULTIMATE_CLOUD_SOURCE", capabilities);
        Assert.DoesNotContain("CLOUD_SYNC", capabilities);

        var info = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", TestContext.Current.CancellationToken);
        Assert.Equal("PAID", info.GetProperty("cloudTier").GetString());

        // The server registered itself as a device, identifying itself as every Ultimate client must.
        var touch = Assert.Single(_cloud.Api, a => a.Operation == "touch");
        Assert.Contains("SERVER", touch.Body);
        Assert.Equal("arcanist-server", touch.Request.Headers.GetValues("X-Client-App").Single());
        Assert.StartsWith("arcanist-", touch.Request.Headers.GetValues("X-Device-Id").Single());

        // The refresh token is kept protected, never as text.
        await using (var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            var stored = await db.Settings.SingleAsync(s => s.Key == SettingKeys.UltimateCloudLink, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("refresh-", stored.Value);
        }

        // Changing plans shows after a refresh.
        _cloud.Plan = "Standard";
        var refreshed = (await admin.GraphQLAsync("mutation { refreshUltimateCloud { tier capabilities plan { name } } }"))
            .GetProperty("data").GetProperty("refreshUltimateCloud");
        Assert.Equal("FREE", refreshed.GetProperty("tier").GetString());
        Assert.Equal(["ULTIMATE_CLOUD_SOURCE"], refreshed.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));

        // Unlinking ends the session on Ultimate Cloud and turns everything off.
        var unlinked = (await admin.GraphQLAsync("mutation { unlinkUltimateCloud { state tier plan { name } } }"))
            .GetProperty("data").GetProperty("unlinkUltimateCloud");
        Assert.Equal("UNLINKED", unlinked.GetProperty("state").GetString());
        Assert.Equal("NONE", unlinked.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("plan").ValueKind);
        Assert.Equal(_cloud.RefreshToken, Assert.Single(_cloud.Revoked));
    }

    [Fact]
    public async Task A_refused_code_leaves_the_server_unlinked()
    {
        var admin = await StartAsync();

        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Deny = true;

        var status = await WaitForStateAsync(admin, "UNLINKED");
        Assert.Equal("cloud_link_denied", status.GetProperty("errorCode").GetString());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task A_session_ended_on_Ultimate_Cloud_asks_to_link_again()
    {
        var admin = await StartAsync();
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        _cloud.SessionEnded = true;
        var status = (await admin.GraphQLAsync("mutation { refreshUltimateCloud { state tier errorCode } }"))
            .GetProperty("data").GetProperty("refreshUltimateCloud");
        Assert.Equal("cloud_link_expired", status.GetProperty("errorCode").GetString());
        Assert.Equal("NONE", status.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task The_music_of_the_account_is_a_library_streamed_on_demand()
    {
        var audio = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        _cloud.Plan = "Standard";
        _cloud.Cover = Scanning.AudioFixtures.Png(3);
        _cloud.AddTrack("t1", "Drift", "Haze", "Low Tide", 1, audio, cover: "c1");
        _cloud.AddTrack("t2", "Undertow", "Haze", "Low Tide", 2, audio[..1000], cover: "c1");
        var admin = await StartAsync();
        const string add = "mutation($c: UltimateCloudCollection!) { addUltimateCloudLibrary(input: {collection: $c}) { libraries { libraryId name collection } } }";

        // Only with a linked account.
        Assert.Equal("cloud_not_linked", (await admin.GraphQLAsync(add, new { c = "LIBRARY" })).ErrorCode());
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        var added = (await admin.GraphQLAsync(add, new { c = "LIBRARY" })).GetProperty("data").GetProperty("addUltimateCloudLibrary");
        var library = added.GetProperty("libraries").EnumerateArray().Single();
        // Named after the account's owner.
        Assert.Equal(("Ana's Ultimate Cloud", "LIBRARY"), (library.GetProperty("name").GetString(), library.GetProperty("collection").GetString()));
        Assert.Equal("validation_failed", (await admin.GraphQLAsync(add, new { c = "LIBRARY" })).ErrorCode());
        await _factory.WaitForScansAsync(admin);

        // Described by Ultimate Cloud: nothing but the cover was downloaded to catalog it.
        var tracks = (await admin.GraphQLAsync("query { tracks(first: 10) { items { id title artworkId } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().OrderBy(t => t.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Drift", "Undertow"], tracks.Select(t => t.GetProperty("title").GetString()));
        Assert.NotEqual(JsonValueKind.Null, tracks[0].GetProperty("artworkId").ValueKind);
        Assert.Equal(0, _cloud.StreamUrls);

        // Played through the server: Range goes to the signed URL, which is reused.
        var trackId = tracks[0].GetProperty("id").GetString();
        using var partial = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{trackId}");
        partial.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 199);
        var response = await admin.SendAsync(partial, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("audio/flac", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(audio[100..200], await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(audio, await admin.GetByteArrayAsync($"/rest/v1/stream/{trackId}", TestContext.Current.CancellationToken));
        Assert.Equal(1, _cloud.StreamUrls);

        // Folders are not added to it.
        var libraryId = library.GetProperty("libraryId").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/rest/v1/libraries/{libraryId}/sources",
            new { location = Path.GetTempPath() }, TestContext.Current.CancellationToken)).StatusCode);

        // Unlinked, the library stays as it is: a scan finds the source unavailable and marks nothing missing.
        await admin.GraphQLAsync("mutation { unlinkUltimateCloud { state } }");
        await admin.GraphQLAsync("mutation($id: ID!) { startScan(libraryId: $id, full: false) { id } }", new { id = libraryId });
        await _factory.WaitForScansAsync(admin);
        Assert.Equal(2, (await admin.GraphQLAsync("query { tracks(first: 10) { totalCount } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Dlna_devices_browse_an_Ultimate_Cloud_library_by_artist_and_album()
    {
        var audio = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        _cloud.AddTrack("t2", "Undertow", "Haze", "Low Tide", 2, audio);
        _cloud.AddTrack("t1", "Drift", "Haze", "Low Tide", 1, audio);
        var admin = await StartAsync(dlna: true);
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");
        var libraryId = (await admin.GraphQLAsync("mutation { addUltimateCloudLibrary(input: {collection: LIBRARY}) { libraries { libraryId } } }"))
            .GetProperty("data").GetProperty("addUltimateCloudLibrary").GetProperty("libraries")[0].GetProperty("libraryId").GetString();
        await _factory.WaitForScansAsync(admin);
        var published = await admin.PutAsJsonAsync("/rest/v1/dlna/settings",
            new { friendlyName = (string?)null, libraryIds = new[] { libraryId }, sections = new[] { "FOLDERS" } }, ArcanistFactory.Json, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var lan = _factory.CreateRemoteClient("192.168.1.20");
        async Task<List<(string Id, string Title)>> BrowseAsync(string objectId)
        {
            var body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:Browse xmlns:u=\"urn:schemas-upnp-org:service:ContentDirectory:1\"><ObjectID>" + System.Net.WebUtility.HtmlEncode(objectId) + "</ObjectID><BrowseFlag>BrowseDirectChildren</BrowseFlag><Filter>*</Filter><StartingIndex>0</StartingIndex><RequestedCount>0</RequestedCount><SortCriteria></SortCriteria></u:Browse></s:Body></s:Envelope>";
            var response = await lan.PostAsync("/ext/dlna/content-directory/control", new StringContent(body, Encoding.UTF8, "text/xml"), TestContext.Current.CancellationToken);
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, text);
            var result = System.Xml.Linq.XDocument.Parse(text).Descendants().Single(e => e.Name.LocalName == "Result").Value;
            return System.Xml.Linq.XElement.Parse(result).Elements()
                .Select(e => (e.Attribute("id")!.Value, e.Elements().Single(c => c.Name.LocalName == "title").Value)).ToList();
        }

        // Ultimate Cloud names its files by id: the folders come from what it says about each song.
        var root = Assert.Single(await BrowseAsync("folders"));
        var artist = Assert.Single(await BrowseAsync(root.Id));
        Assert.Equal("Haze", artist.Title);
        var album = Assert.Single(await BrowseAsync(artist.Id));
        Assert.Equal("Low Tide (2020)", album.Title);
        Assert.Equal(["Drift", "Undertow"], (await BrowseAsync(album.Id)).Select(s => s.Title));
    }

    private static async Task<string?> EditionAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, TestContext.Current.CancellationToken))
        .GetProperty("edition").GetString();

    [Fact]
    public async Task Without_Ultimate_Cloud_Plus_the_Cloud_plans_features_ask_for_it()
    {
        var admin = await StartAsync();
        await admin.GraphQLAsync("mutation { startUltimateCloudLink { state } }");
        _cloud.Approve = true;
        await WaitForStateAsync(admin, "LINKED");

        // The paid plan alone makes nothing closed source run: the server stays open source.
        var plus = (await admin.GraphQLAsync("query { ultimateCloud { plus { installed available extensionId } } }"))
            .GetProperty("data").GetProperty("ultimateCloud").GetProperty("plus");
        Assert.Equal((false, false, "ultimate-cloud-plus"), (plus.GetProperty("installed").GetBoolean(), plus.GetProperty("available").GetBoolean(),
            plus.GetProperty("extensionId").GetString()));
        Assert.Equal("OPEN_SOURCE", await EditionAsync(admin));

        // Syncing, uploading to Ultimate Cloud and S3 libraries answer that they need it.
        var music = Directory.CreateTempSubdirectory("arcanist-cloud-plus-").FullName;
        var libraryId = (await admin.GraphQLAsync("mutation($p: String!) { createLibrary(input: { name: \"Music\", visibleToAll: true, watchForChanges: false, sources: [{ location: $p }] }) { id } }", new { p = music }))
            .GetProperty("data").GetProperty("createLibrary").GetProperty("id").GetString();
        Assert.Equal("cloud_plus_required", (await admin.GraphQLAsync(
            "mutation($id: ID!) { setLibraryCloudSync(libraryId: $id, input: { mode: UPLOAD }) { mode } }", new { id = libraryId })).ErrorCode());
        Assert.Equal("cloud_plus_required", (await admin.GraphQLAsync(
            "mutation($t: [ID!]!) { uploadTracksToMyCloud(input: { trackIds: $t }) { status } }", new { t = new[] { Guid.NewGuid() } })).ErrorCode());
        var s3 = (await admin.GraphQLAsync("query { s3LibraryAvailability { canCreate errorCode } }")).GetProperty("data").GetProperty("s3LibraryAvailability");
        Assert.Equal((false, "cloud_plus_required"), (s3.GetProperty("canCreate").GetBoolean(), s3.GetProperty("errorCode").GetString()));
        var features = (await admin.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, TestContext.Current.CancellationToken))
            .GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.DoesNotContain("MY_CLOUD_UPLOADS", features);
        Assert.DoesNotContain("WISH_LIST_CLOUD_SYNC", features);
    }

    [Fact]
    public async Task Only_administrators_see_the_account()
    {
        var admin = await StartAsync();
        await _factory.CreateUserAsync("bob");
        var bob = await _factory.CreateAuthenticatedClientAsync("bob");

        Assert.Equal("forbidden", (await bob.GraphQLAsync(Status)).ErrorCode());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync("/rest/v1/ultimate-cloud/link", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/rest/v1/ultimate-cloud", TestContext.Current.CancellationToken)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }
}
