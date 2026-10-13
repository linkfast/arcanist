using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arcanist.Core.Dlna;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Extensions;

/// <summary>A DLNA renderer that answers the UPnP actions Arcanist sends and remembers them.</summary>
public sealed class FakeRenderer : HttpMessageHandler
{
    public const string Description = "http://192.0.2.50:49152/description.xml";

    public ConcurrentQueue<(string Action, Dictionary<string, string> Arguments)> Actions { get; } = new();

    public string State { get; set; } = "STOPPED";

    public string TrackUri { get; set; } = "";

    public int Volume { get; set; } = 30;

    public string Accepts { get; set; } = "http-get:*:audio/wav:*,http-get:*:audio/mpeg:*";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
        {
            return Xml("""
                <?xml version="1.0"?>
                <root xmlns="urn:schemas-upnp-org:device-1-0">
                  <device>
                    <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
                    <friendlyName>Living room</friendlyName>
                    <manufacturer>Acme</manufacturer>
                    <modelName>Speaker 1</modelName>
                    <UDN>uuid:5a4c0f1e-0000-4000-8000-000000000001</UDN>
                    <serviceList>
                      <service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>/av</controlURL></service>
                      <service><serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType><controlURL>/rc</controlURL></service>
                      <service><serviceType>urn:schemas-upnp-org:service:ConnectionManager:1</serviceType><controlURL>/cm</controlURL></service>
                    </serviceList>
                  </device>
                </root>
                """);
        }

        var action = request.Headers.GetValues("SOAPACTION").Single().Trim('"').Split('#')[1];
        var body = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var arguments = body.Descendants().Single(e => e.Name.LocalName == action).Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        Actions.Enqueue((action, arguments));

        var output = action switch
        {
            "SetAVTransportURI" => Do(() => TrackUri = arguments["CurrentURI"]),
            "Play" => Do(() => State = "PLAYING"),
            "Pause" => Do(() => State = "PAUSED_PLAYBACK"),
            "Stop" => Do(() => State = "STOPPED"),
            "SetVolume" => Do(() => Volume = int.Parse(arguments["DesiredVolume"])),
            "GetTransportInfo" => $"<CurrentTransportState>{State}</CurrentTransportState><CurrentTransportStatus>OK</CurrentTransportStatus>",
            "GetPositionInfo" => $"<TrackURI>{WebUtility.HtmlEncode(TrackUri)}</TrackURI><RelTime>0:00:01</RelTime><TrackDuration>0:00:02</TrackDuration>",
            "GetVolume" => $"<CurrentVolume>{Volume}</CurrentVolume>",
            "GetProtocolInfo" => $"<Source></Source><Sink>{Accepts}</Sink>",
            _ => "",
        };
        return Xml($"""
            <?xml version="1.0"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><u:{action}Response xmlns:u="x">{output}</u:{action}Response></s:Body></s:Envelope>
            """);
    }

    private static string Do(Action action)
    {
        action();
        return "";
    }

    private static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml.Trim(), Encoding.UTF8, "text/xml") };
}

/// <summary>The DLNA media server and "play to".</summary>
public sealed class DlnaTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-dlna-").FullName;
    private readonly FakeRenderer _renderer = new();
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;
    private HttpClient _lan = null!;
    private string _libraryId = null!;

    public DlnaTests()
    {
        _factory = new ArcanistFactory(
            new Dictionary<string, string?> { ["Arcanist:Extensions:dlna:Enabled"] = "true", ["Arcanist:Dlna:Port"] = "8484" },
            services: services => services.AddHttpClient(RendererRegistry.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _renderer));
    }

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "01.wav"), 1,
            new FixtureTags("Opening", "Band", "First Album", "Band", TrackNumber: 1, Year: 1994, Genre: "Rock", WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(_music, "Band", "First", "02.wav"), 2,
            new FixtureTags("Closing", "Band", "First Album", "Band", TrackNumber: 2, Year: 1994, Genre: "Rock", WithCover: true));
        AudioFixtures.WriteWav(Path.Combine(_music, "Zed", "01.wav"), 3, new FixtureTags("Alone", "Zed", "Solo", "Zed", TrackNumber: 1, Year: 2003, Genre: "Jazz"));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
        _libraryId = (await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))![0].GetProperty("id").GetString()!;
        _lan = _factory.CreateRemoteClient("192.168.1.20");
    }

    private async Task PublishAsync(params string[] sections)
    {
        var response = await _admin.PutAsJsonAsync("/rest/v1/dlna/settings", new
        {
            friendlyName = "Home music",
            libraryIds = new[] { _libraryId },
            sections = sections.Length > 0 ? sections : ["ARTISTS", "ALBUMS", "GENRES", "DECADES", "FOLDERS", "PLAYLISTS", "RECENTLY_ADDED", "MIXES"],
        }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Browses as a DLNA device does and returns the DIDL-Lite result and the total.</summary>
    private async Task<(XElement Didl, int Total)> BrowseAsync(string objectId, string flag = "BrowseDirectChildren", int start = 0, int count = 0)
    {
        var body = $"""
            <?xml version="1.0"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:Browse xmlns:u="urn:schemas-upnp-org:service:ContentDirectory:1"><ObjectID>{WebUtility.HtmlEncode(objectId)}</ObjectID><BrowseFlag>{flag}</BrowseFlag>
            <Filter>*</Filter><StartingIndex>{start}</StartingIndex><RequestedCount>{count}</RequestedCount><SortCriteria></SortCriteria></u:Browse>
            </s:Body></s:Envelope>
            """;
        var response = await _lan.PostAsync("/ext/dlna/content-directory/control", new StringContent(body.Trim(), Encoding.UTF8, "text/xml"), Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        var envelope = XDocument.Parse(text);
        var result = envelope.Descendants().Single(e => e.Name.LocalName == "Result").Value;
        var total = int.Parse(envelope.Descendants().Single(e => e.Name.LocalName == "TotalMatches").Value);
        return (XElement.Parse(result), total);
    }

    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DidlNs = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";

    private static List<string> Titles(XElement didl) => didl.Elements().Select(e => e.Element(Dc + "title")!.Value).ToList();

    [Fact]
    public async Task Devices_on_the_local_network_find_and_browse_the_published_libraries()
    {
        var description = XDocument.Parse(await _lan.GetStringAsync("/ext/dlna/description.xml", Cancel));
        XNamespace device = "urn:schemas-upnp-org:device-1-0";
        Assert.Equal("urn:schemas-upnp-org:device:MediaServer:1", description.Descendants(device + "deviceType").Single().Value);
        var udn = description.Descendants(device + "UDN").Single().Value;
        Assert.StartsWith("uuid:", udn);
        Assert.Contains("Browse", await _lan.GetStringAsync("/ext/dlna/content-directory.xml", Cancel));

        // Nothing is published until an administrator picks libraries.
        var (empty, total) = await BrowseAsync("albums");
        Assert.Equal(0, total);
        Assert.Empty(empty.Elements());

        await PublishAsync();
        description = XDocument.Parse(await _lan.GetStringAsync("/ext/dlna/description.xml", Cancel));
        Assert.Equal(("Home music", udn), (description.Descendants(device + "friendlyName").Single().Value, description.Descendants(device + "UDN").Single().Value));

        var (root, sections) = await BrowseAsync("0");
        Assert.Equal(8, sections);
        Assert.Equal(["Artists", "Albums", "Genres", "Decades", "Folders", "Playlists", "Recently added", "Mixes"], Titles(root));

        var (albums, albumCount) = await BrowseAsync("albums");
        Assert.Equal(["First Album", "Solo"], Titles(albums));
        Assert.Equal(2, albumCount);
        var first = albums.Elements().First();
        Assert.Equal("object.container.album.musicAlbum", first.Elements().Single(e => e.Name.LocalName == "class").Value);

        var (tracks, _) = await BrowseAsync(first.Attribute("id")!.Value);
        Assert.Equal(["Opening", "Closing"], Titles(tracks));
        var item = tracks.Elements().First();
        var resource = item.Elements(DidlNs + "res").First();
        // The original first: it is lossless.
        Assert.StartsWith("http-get:*:audio/wav:DLNA.ORG_OP=01", resource.Attribute("protocolInfo")!.Value);
        Assert.NotNull(resource.Attribute("size"));

        var (metadata, _) = await BrowseAsync(item.Attribute("id")!.Value, "BrowseMetadata");
        Assert.Equal("Opening", Titles(metadata).Single());

        // The original, with byte ranges and DLNA headers.
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(resource.Value).PathAndQuery);
        request.Headers.Range = new RangeHeaderValue(0, 9);
        var media = await _lan.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, media.StatusCode);
        Assert.Contains("DLNA.ORG_OP=01", media.Headers.GetValues("contentFeatures.dlna.org").Single());

        // Paging and the other sections.
        var (page, pageTotal) = await BrowseAsync("albums", start: 1, count: 1);
        Assert.Equal(["Solo"], Titles(page));
        Assert.Equal(2, pageTotal);
        Assert.Equal(["Band", "Zed"], Titles((await BrowseAsync("artists")).Didl));
        Assert.Equal(["Jazz", "Rock"], Titles((await BrowseAsync("genres")).Didl));
        Assert.Equal(["2000s", "1990s"], Titles((await BrowseAsync("decades")).Didl));
        var (libraries, _) = await BrowseAsync("folders");
        var music = Assert.Single(libraries.Elements());
        Assert.Equal("Music", music.Element(Dc + "title")!.Value);
        var (folders, _) = await BrowseAsync(music.Attribute("id")!.Value);
        Assert.Equal(["Band", "Zed"], Titles(folders));
        var (zed, _) = await BrowseAsync(folders.Elements().Last().Attribute("id")!.Value);
        Assert.Equal(["Alone"], Titles(zed));

        Assert.Equal(701, await FaultAsync("album/" + Guid.NewGuid()));
    }

    private async Task<int> FaultAsync(string objectId)
    {
        var body = $"""<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><u:Browse xmlns:u="urn:schemas-upnp-org:service:ContentDirectory:1"><ObjectID>{objectId}</ObjectID><BrowseFlag>BrowseDirectChildren</BrowseFlag></u:Browse></s:Body></s:Envelope>""";
        var response = await _lan.PostAsync("/ext/dlna/content-directory/control", new StringContent(body, Encoding.UTF8, "text/xml"), Cancel);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        return int.Parse(XDocument.Parse(await response.Content.ReadAsStringAsync(Cancel)).Descendants().Single(e => e.Name.LocalName == "errorCode").Value);
    }

    [Fact]
    public async Task Only_the_local_network_and_the_chosen_sections_and_libraries_are_served()
    {
        await PublishAsync("ALBUMS", "ARTISTS");
        Assert.Equal(["Albums", "Artists"], Titles((await BrowseAsync("0")).Didl));

        var internet = _factory.CreateRemoteClient("203.0.113.9");
        Assert.Equal(HttpStatusCode.NotFound, (await internet.GetAsync("/ext/dlna/description.xml", Cancel)).StatusCode);

        // A track of a library that is not published is not served, even with its URL.
        var (albums, _) = await BrowseAsync("albums");
        var (tracks, _) = await BrowseAsync(albums.Elements().First().Attribute("id")!.Value);
        var url = new Uri(tracks.Elements().First().Elements(DidlNs + "res").First().Value).PathAndQuery;
        var response = await _admin.PutAsJsonAsync("/rest/v1/dlna/settings", new { libraryIds = Array.Empty<string>(), sections = new[] { "ALBUMS" } },
            ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _lan.GetAsync(url, Cancel)).StatusCode);

        // Settings are for administrators.
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/rest/v1/dlna", Cancel)).StatusCode);
        var status = await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/dlna", ArcanistFactory.Json, Cancel);
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("friendlyName").ValueKind);
    }

    [Fact]
    public async Task Users_play_their_tracks_on_a_renderer_which_records_the_plays()
    {
        var added = await _admin.PostAsJsonAsync("/rest/v1/dlna/renderers", new { url = FakeRenderer.Description }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var renderer = Assert.Single((await _admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/dlna/renderers", ArcanistFactory.Json, Cancel))!);
        Assert.Equal("Living room", renderer.GetProperty("name").GetString());
        var rendererId = renderer.GetProperty("id").GetString()!;

        // Play to does not need the libraries to be published: users play what they can see.
        var tracks = (await _admin.GetFromJsonAsync<JsonElement>("/rest/v1/tracks", ArcanistFactory.Json, Cancel)).GetProperty("items").EnumerateArray()
            .ToDictionary(t => t.GetProperty("title").GetString()!, t => t.GetProperty("id").GetString()!);
        var play = await _admin.PostAsJsonAsync("/rest/v1/dlna/play",
            new { rendererId, trackIds = new[] { tracks["Opening"], tracks["Closing"] } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, play.StatusCode);
        var state = await play.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("PLAYING", "Opening", 0), (state.GetProperty("state").GetString(), state.GetProperty("track").GetProperty("title").GetString(),
            state.GetProperty("queueIndex").GetInt32()));

        var sent = _renderer.Actions.ToList();
        var uri = sent.First(a => a.Action == "SetAVTransportURI").Arguments;
        // The renderer plays WAV: the lossless original goes as it is.
        Assert.Matches(new Regex("/ext/dlna/play/[^/]+/track\\.wav$"), uri["CurrentURI"]);
        Assert.Contains("<dc:title>Opening</dc:title>", uri["CurrentURIMetaData"]);
        var next = sent.First(a => a.Action == "SetNextAVTransportURI").Arguments["NextURI"];

        // The renderer fetches the audio with the URL's grant.
        var audio = await _lan.GetAsync(new Uri(uri["CurrentURI"]).PathAndQuery, Cancel);
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("audio/wav", audio.Content.Headers.ContentType!.MediaType);

        // It moves on to the next track by itself: the first one counts as played.
        _renderer.TrackUri = next;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/tracks/{tracks["Opening"]}", ArcanistFactory.Json, Cancel)).GetProperty("playCount").GetInt32() == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The play was not recorded.");
            await Task.Delay(200, Cancel);
        }

        var current = await _admin.GetFromJsonAsync<JsonElement>($"/rest/v1/dlna/renderers/{rendererId}", ArcanistFactory.Json, Cancel);
        Assert.Equal(("Closing", 1, 30), (current.GetProperty("track").GetProperty("title").GetString(), current.GetProperty("queueIndex").GetInt32(),
            current.GetProperty("volume").GetInt32()));

        var paused = await _admin.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId, action = "PAUSE" }, ArcanistFactory.Json, Cancel);
        Assert.Equal("PAUSED", (await paused.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("state").GetString());
        var louder = await _admin.PutAsJsonAsync($"/rest/v1/dlna/renderers/{rendererId}/volume", new { volume = 55 }, ArcanistFactory.Json, Cancel);
        Assert.Equal(55, (await louder.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("volume").GetInt32());
        var back = await _admin.PostAsJsonAsync("/rest/v1/dlna/control", new { rendererId, action = "PREVIOUS" }, ArcanistFactory.Json, Cancel);
        Assert.Equal("Opening", (await back.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("track").GetProperty("title").GetString());

        // A grant is only good for its track.
        var forged = Regex.Replace(uri["CurrentURI"], "/play/[^/]+/", "/play/nope/");
        Assert.Equal(HttpStatusCode.NotFound, (await _lan.GetAsync(new Uri(forged).PathAndQuery, Cancel)).StatusCode);
    }

    [Fact]
    public void Ssdp_messages_announce_the_server_and_find_renderers()
    {
        var id = Guid.NewGuid();
        var targets = Ssdp.Targets(id);
        Assert.Contains(targets, t => t.Type == Ssdp.MediaServer && t.Usn == $"uuid:{id}::{Ssdp.MediaServer}");
        Assert.Equal(5, Ssdp.Matching("ssdp:all", id).Count());
        Assert.Single(Ssdp.Matching("urn:schemas-upnp-org:service:ContentDirectory:1", id));
        Assert.Empty(Ssdp.Matching("urn:schemas-upnp-org:device:MediaRenderer:1", id));

        var search = Ssdp.Parse("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nst: ssdp:all\r\n\r\n")!;
        Assert.True(search.IsSearch);
        Assert.Equal("ssdp:all", search["ST"]);

        var response = Ssdp.Parse(Ssdp.SearchResponse("http://10.0.0.2:8484/ext/dlna/description.xml", Ssdp.MediaServer, $"uuid:{id}", "x", DateTime.UtcNow))!;
        Assert.True(response.IsResponse);
        Assert.Equal("http://10.0.0.2:8484/ext/dlna/description.xml", response["LOCATION"]);

        var renderer = Ssdp.Parse("NOTIFY * HTTP/1.1\r\nNT: urn:schemas-upnp-org:device:MediaRenderer:1\r\nNTS: ssdp:alive\r\nLOCATION: http://10.0.0.9/d.xml\r\n\r\n")!;
        Assert.True(renderer.IsNotify && Ssdp.IsRenderer(renderer));
    }

    [Fact]
    public void Formats_put_lossless_first_and_follow_what_renderers_play()
    {
        var all = new HashSet<DlnaFormat> { DlnaFormat.Flac, DlnaFormat.Lpcm, DlnaFormat.Mp3 };
        Assert.Equal([DlnaFormat.Original, DlnaFormat.Flac, DlnaFormat.Lpcm, DlnaFormat.Mp3], DlnaFormats.Offered("a/b.ape", false, all));
        Assert.Equal([DlnaFormat.Original, DlnaFormat.Lpcm, DlnaFormat.Mp3], DlnaFormats.Offered("a/b.flac", false, all));
        Assert.Equal([DlnaFormat.Flac, DlnaFormat.Lpcm, DlnaFormat.Mp3], DlnaFormats.Offered("a/b.flac", true, all));
        Assert.Equal([DlnaFormat.Original], DlnaFormats.Offered("a/b.flac", false, new HashSet<DlnaFormat>()));

        var tv = new HashSet<string> { "audio/mpeg", "audio/l16" };
        Assert.Equal(DlnaFormat.Lpcm, DlnaFormats.Choose("a/b.flac", false, all, tv));
        Assert.Equal(DlnaFormat.Mp3, DlnaFormats.Choose("a/b.ogg", false, all, tv));
        Assert.Equal(DlnaFormat.Original, DlnaFormats.Choose("a/b.flac", false, all, new HashSet<string> { DlnaFormats.Normalize("audio/x-flac") }));
        Assert.Equal(DlnaFormat.Original, DlnaFormats.Choose("a/b.flac", false, all, new HashSet<string>()));
        Assert.Equal(12.5, Arcanist.Core.Extensions.Dlna.DlnaExtension.TimeSeek("npt=12.5-"));
        Assert.Equal(83.0, Arcanist.Core.Extensions.Dlna.DlnaExtension.TimeSeek("npt=0:01:23-"));
    }

    public async ValueTask DisposeAsync()
    {
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
