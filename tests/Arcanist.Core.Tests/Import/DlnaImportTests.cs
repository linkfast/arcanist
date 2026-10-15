using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Arcanist.Core.Dlna;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Import;

/// <summary>
/// A DLNA media server (description, ContentDirectory and files) answered from memory: a tree of containers whose
/// items point to files of a folder. Nothing reaches the network.
/// </summary>
public sealed class FakeMediaServer : HttpMessageHandler
{
    public const string Base = "http://192.0.2.60:8200";
    public const string Description = Base + "/description.xml";

    private static readonly XNamespace Didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";

    /// <summary>Children of each container: other containers (id, title) or items.</summary>
    public Dictionary<string, List<object>> Tree { get; } = [];

    /// <summary>Files served under /media/{name}.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    public byte[]? Artwork { get; set; }

    public ConcurrentQueue<string> Requested { get; } = new();

    public sealed record Container(string Id, string Title);

    /// <param name="File">The file under /media; the item also offers an MP3 conversion of it, which must not be picked.</param>
    public sealed record Item(string Id, string Title, string File, string? Artist = null, string? Album = null, int? Track = null, bool Art = false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requested.Enqueue(path);
        if (request.Method == HttpMethod.Get && path == "/description.xml")
        {
            return Xml("""
                <?xml version="1.0"?>
                <root xmlns="urn:schemas-upnp-org:device-1-0">
                  <device>
                    <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
                    <friendlyName>Old NAS</friendlyName>
                    <manufacturer>MinimServer</manufacturer>
                    <UDN>uuid:7d1c5e1a-0000-4000-8000-000000000060</UDN>
                    <serviceList>
                      <service><serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType><controlURL>/ctl/cd</controlURL></service>
                    </serviceList>
                  </device>
                </root>
                """);
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/media/", StringComparison.Ordinal))
        {
            return Files.TryGetValue(path["/media/".Length..], out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") } } }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Get && path == "/art.png" && Artwork is not null)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Artwork) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } } };

        if (request.Method == HttpMethod.Post && path == "/ctl/cd")
        {
            var body = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var arguments = body.Descendants().Single(e => e.Name.LocalName == "Browse").Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
            var children = Tree.GetValueOrDefault(arguments["ObjectID"]) ?? [];
            var start = int.Parse(arguments["StartingIndex"]);
            var count = int.Parse(arguments["RequestedCount"]);
            var page = children.Skip(start).Take(count == 0 ? int.MaxValue : count).ToList();
            var didl = new XElement(Didl + "DIDL-Lite", new XAttribute(XNamespace.Xmlns + "dc", Dc), new XAttribute(XNamespace.Xmlns + "upnp", Upnp),
                page.Select(child => child switch
                {
                    Container c => new XElement(Didl + "container", new XAttribute("id", c.Id), new XAttribute("parentID", arguments["ObjectID"]),
                        new XAttribute("childCount", Tree.GetValueOrDefault(c.Id)?.Count ?? 0),
                        new XElement(Dc + "title", c.Title), new XElement(Upnp + "class", "object.container.storageFolder")),
                    Item i => new XElement(Didl + "item", new XAttribute("id", i.Id), new XAttribute("parentID", arguments["ObjectID"]),
                        new XElement(Dc + "title", i.Title),
                        new XElement(Upnp + "class", "object.item.audioItem.musicTrack"),
                        i.Artist is null ? null : new XElement(Upnp + "artist", i.Artist),
                        i.Album is null ? null : new XElement(Upnp + "album", i.Album),
                        i.Track is null ? null : new XElement(Upnp + "originalTrackNumber", i.Track),
                        i.Art ? new XElement(Upnp + "albumArtURI", $"{Base}/art.png") : null,
                        new XElement(Didl + "res", new XAttribute("protocolInfo", "http-get:*:audio/mpeg:DLNA.ORG_PN=MP3;DLNA.ORG_CI=1"), $"{Base}/media/{i.File}.mp3"),
                        new XElement(Didl + "res", new XAttribute("protocolInfo", "http-get:*:audio/wav:*"), new XAttribute("size", 1000), $"{Base}/media/{i.File}")),
                    _ => new XElement(Didl + "item", new XAttribute("id", "photo"), new XElement(Dc + "title", "Photo"),
                        new XElement(Upnp + "class", "object.item.imageItem.photo"),
                        new XElement(Didl + "res", new XAttribute("protocolInfo", "http-get:*:image/jpeg:*"), $"{Base}/photo.jpg")),
                }));
            return Xml($"""
                <?xml version="1.0"?>
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><u:BrowseResponse xmlns:u="urn:schemas-upnp-org:service:ContentDirectory:1">
                <Result>{WebUtility.HtmlEncode(didl.ToString(SaveOptions.DisableFormatting))}</Result><NumberReturned>{page.Count}</NumberReturned>
                <TotalMatches>{children.Count}</TotalMatches><UpdateID>1</UpdateID></u:BrowseResponse></s:Body></s:Envelope>
                """);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml.Trim(), Encoding.UTF8, "text/xml") };

    public void Register(IServiceCollection services)
    {
        services.AddHttpClient(MediaServerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => this);
        services.AddHttpClient(MediaServerClient.DownloadClientName).ConfigurePrimaryHttpMessageHandler(() => this);
    }
}

public sealed class DlnaImportTests : IAsyncDisposable
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeMediaServer _server = new();
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-dlna-import-").FullName;
    private readonly string _staging = Directory.CreateTempSubdirectory("arcanist-dlna-server-").FullName;
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private async Task StartAsync()
    {
        // The library already has one song; the server has it too.
        var existing = AudioFixtures.WriteWav(Path.Combine(_music, "Old", "existing.wav"), 1, new FixtureTags("Known", "Band", "Older"));

        _server.Files["alpha.wav"] = File.ReadAllBytes(AudioFixtures.WriteWav(Path.Combine(_staging, "alpha.wav"), 2,
            new FixtureTags("Alpha", "Band", "Pulse", "Band", TrackNumber: 1, Year: 2001)));
        // No tags at all: the server's description names it.
        _server.Files["beta.wav"] = File.ReadAllBytes(AudioFixtures.WriteWav(Path.Combine(_staging, "beta.wav"), 3, new FixtureTags()));
        _server.Files["same.wav"] = File.ReadAllBytes(existing);
        _server.Artwork = AudioFixtures.Png(7);

        FakeMediaServer.Item alpha = new("a1", "Alpha (server title)", "alpha.wav", "Band", "Pulse", 1);
        FakeMediaServer.Item beta = new("b1", "Beta", "beta.wav", "Band", "Pulse", 2, Art: true);
        _server.Tree["0"] = [new FakeMediaServer.Container("music", "Music"), new FakeMediaServer.Container("pictures", "Pictures")];
        _server.Tree["pictures"] = ["photo"];
        _server.Tree["music"] = [new FakeMediaServer.Container("albums", "Albums"), new FakeMediaServer.Container("folders", "Folders")];
        _server.Tree["albums"] = [new FakeMediaServer.Container("album-pulse", "Pulse")];
        _server.Tree["album-pulse"] = [alpha, beta, new FakeMediaServer.Item("s1", "Known", "same.wav", "Band", "Older"), new FakeMediaServer.Item("m1", "Gone", "missing.wav")];
        // The same files again, as another view of the server lists them.
        _server.Tree["folders"] = [alpha with { Id = "a2" }, beta with { Id = "b2" }];

        _factory = new ArcanistFactory(services: _server.Register);
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    private async Task<JsonElement> WaitForImportAsync(string id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var imports = (await _admin.GraphQLAsync(
                    "query { dlnaImports { id status found copied duplicates failed copiedBytes errorCode folders libraryName failures { title reason } } }"))
                .GetProperty("data").GetProperty("dlnaImports");
            var import = imports.EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);
            if (import.GetProperty("status").GetString() is not ("QUEUED" or "LISTING" or "COPYING"))
                return import;
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Still {import}");
            await Task.Delay(50, Cancel);
        }
    }

    [Fact]
    public async Task Copies_the_music_of_a_media_server_into_a_library()
    {
        await StartAsync();

        var server = (await _admin.GraphQLAsync("query($l: String!) { dlnaMediaServer(location: $l) { id name manufacturer location } }",
            new { l = FakeMediaServer.Description })).GetProperty("data").GetProperty("dlnaMediaServer");
        Assert.Equal("Old NAS", server.GetProperty("name").GetString());
        Assert.Equal("7d1c5e1a-0000-4000-8000-000000000060", server.GetProperty("id").GetString());

        const string browse = "query($l: String!, $f: String) { browseDlnaMediaServer(location: $l, folderId: $f) { id tracks truncated folders { id title childCount } } }";
        var root = (await _admin.GraphQLAsync(browse, new { l = FakeMediaServer.Description, f = (string?)null })).GetProperty("data").GetProperty("browseDlnaMediaServer");
        Assert.Equal(["Music", "Pictures"], root.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("title").GetString()));
        var album = (await _admin.GraphQLAsync(browse, new { l = FakeMediaServer.Description, f = "album-pulse" })).GetProperty("data").GetProperty("browseDlnaMediaServer");
        Assert.Equal(4, album.GetProperty("tracks").GetInt32());

        var sourceId = (await _admin.GraphQLAsync("query { libraries { sources { id } } }"))
            .GetProperty("data").GetProperty("libraries")[0].GetProperty("sources")[0].GetProperty("id").GetString();
        var started = await _admin.GraphQLAsync("mutation($i: StartDlnaImportInput!) { startDlnaImport(input: $i) { id status serverName folders } }",
            new { i = new { location = FakeMediaServer.Description, folders = new[] { new { id = "music", title = "Music" } }, sourceId } });
        var job = started.GetProperty("data").GetProperty("startDlnaImport");
        Assert.Equal("Old NAS", job.GetProperty("serverName").GetString());

        var import = await WaitForImportAsync(job.GetProperty("id").GetString()!);
        Assert.Equal("COMPLETED", import.GetProperty("status").GetString());
        // Each file once, although two views list Alpha and Beta.
        Assert.Equal(4, import.GetProperty("found").GetInt32());
        Assert.Equal(2, import.GetProperty("copied").GetInt32());
        Assert.Equal(1, import.GetProperty("duplicates").GetInt32());
        Assert.Equal(1, import.GetProperty("failed").GetInt32());
        Assert.Equal("download_failed", import.GetProperty("failures")[0].GetProperty("reason").GetString());

        // Placed as the library's template says: the file's tags first, the server's description for the untagged one.
        Assert.True(File.Exists(Path.Combine(_music, "Band", "2001 - Pulse", "01 Alpha.wav")));
        Assert.True(File.Exists(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav")));
        Assert.Equal(_server.Artwork, File.ReadAllBytes(Path.Combine(_music, "Band", "Pulse", "cover.png")));
        // The originals, never the server's conversions.
        Assert.DoesNotContain(_server.Requested, p => p.EndsWith(".mp3", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_factory.DataPath, "cache", "import")));

        await _factory.WaitForScansAsync(_admin);
        var titles = (await _admin.GraphQLAsync("query { tracks(first: 50) { items { title } } }"))
            .GetProperty("data").GetProperty("tracks").GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()).Order().ToList();
        // The untagged copy got the server's description as tags; the tagged one kept its own title.
        Assert.Equal(["Alpha", "Beta", "Known"], titles);
        var beta = new ATL.Track(Path.Combine(_music, "Band", "Pulse", "02 Beta.wav"));
        Assert.Equal(("Beta", "Band", "Pulse", 2), (beta.Title, beta.Artist, beta.Album, beta.TrackNumber ?? 0));

        // Retrying walks the server again and only tries what is missing.
        var requestsBefore = _server.Requested.Count(p => p.StartsWith("/media/", StringComparison.Ordinal));
        await _admin.GraphQLAsync("mutation($id: ID!) { retryDlnaImport(id: $id) { status } }", new { id = job.GetProperty("id").GetString() });
        import = await WaitForImportAsync(job.GetProperty("id").GetString()!);
        Assert.Equal(2, import.GetProperty("copied").GetInt32());
        Assert.Equal(4, import.GetProperty("found").GetInt32());
        Assert.Equal(requestsBefore + 1, _server.Requested.Count(p => p.StartsWith("/media/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Copies_into_a_new_library()
    {
        await StartAsync();
        var folder = Path.Combine(Directory.CreateTempSubdirectory("arcanist-dlna-new-").FullName, "nas");
        Directory.CreateDirectory(folder);

        var started = await _admin.GraphQLAsync("mutation($i: StartDlnaImportInput!) { startDlnaImport(input: $i) { id libraryName } }",
            new { i = new { location = FakeMediaServer.Description, folders = new[] { new { id = "album-pulse", title = "Pulse" } }, newLibraryName = "From the NAS", newLibraryPath = folder } });
        var job = started.GetProperty("data").GetProperty("startDlnaImport");
        Assert.Equal("From the NAS", job.GetProperty("libraryName").GetString());

        var import = await WaitForImportAsync(job.GetProperty("id").GetString()!);
        // A different library: the song the first one has is copied too.
        Assert.Equal(3, import.GetProperty("copied").GetInt32());
        Assert.True(File.Exists(Path.Combine(folder, "Band", "Older", "Known.wav")));
    }

    [Fact]
    public async Task Checks_what_it_is_given()
    {
        await StartAsync();
        Assert.Equal("validation_failed", (await _admin.GraphQLAsync("query { dlnaMediaServer(location: \"file:///etc/passwd\") { id } }")).ErrorCode());
        Assert.Equal("dlna_server_unreachable", (await _admin.GraphQLAsync("query { dlnaMediaServer(location: \"http://192.0.2.60:8200/nothing.xml\") { id } }")).ErrorCode());

        var missing = await _admin.GraphQLAsync("mutation($i: StartDlnaImportInput!) { startDlnaImport(input: $i) { id } }",
            new { i = new { location = FakeMediaServer.Description, folders = Array.Empty<object>() } });
        Assert.Equal("validation_failed", missing.ErrorCode());

        await _factory.CreateUserAsync("bob");
        var bob = await _factory.CreateAuthenticatedClientAsync("bob");
        Assert.Equal("forbidden", (await bob.GraphQLAsync("query { dlnaImports { id } }")).ErrorCode());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/rest/v1/dlna-import/imports", Cancel)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }
}
