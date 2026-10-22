using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Application.WishList;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Tests.Online;
using Arcanist.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Arcanist.Core.Tests.Api;

/// <summary>Wishes from streaming services' links, everyone's wish list, and the wish lists of extensions.</summary>
public sealed class WishLinkTests : IAsyncLifetime
{
    private static readonly Guid Recording = Guid.Parse("8f3471b5-7e6a-48da-86a9-c1c07a0f47ae");
    private static readonly Guid Release = Guid.Parse("4f5fd5d9-6b2b-4f3a-9b4c-33c0a5a0e0b1");

    private readonly FakeOnlineServices _online = new();
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new ArcanistFactory(services: _online.Register);
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
        _online.Respond = Respond;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static byte[] Png(SKColor color)
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static HttpResponseMessage Image(SKColor color) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png(color)) };

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    /// <summary>The services, as they answer.</summary>
    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        return url switch
        {
            "https://api.deezer.com/track/3135556" => FakeOnlineServices.Json("""
                {"id":3135556,"title":"Harder, Better, Faster, Stronger","duration":224,"isrc":"GBDUW0000059",
                 "artist":{"name":"Daft Punk"},"album":{"title":"Discovery","cover_xl":"https://cdn.deezer.test/cover.jpg"}}
                """),
            _ when url.StartsWith("https://musicbrainz.org/ws/2/isrc/GBDUW0000059", StringComparison.Ordinal) => FakeOnlineServices.Json($$$"""
                {"recordings":[{"id":"{{{Recording}}}","title":"Harder, Better, Faster, Stronger",
                  "releases":[{"id":"00000000-0000-0000-0000-000000000001","status":"Bootleg"},
                              {"id":"{{{Release}}}","status":"Official","release-group":{"primary-type":"Album"}}]}]}
                """),
            _ when url == $"https://coverartarchive.org/release/{Release}/front-1200" => Image(SKColors.Red),
            _ when url.StartsWith("https://www.youtube.com/oembed", StringComparison.Ordinal) => FakeOnlineServices.Json("""
                {"title":"Weird Fishes / Arpeggi (Remastered)","author_name":"Radiohead - Topic","thumbnail_url":"https://i.ytimg.com/vi/abc/hqdefault.jpg"}
                """),
            _ when url.StartsWith("https://musicbrainz.org/ws/2/recording/?query=", StringComparison.Ordinal) => FakeOnlineServices.Json("""{"recordings":[]}"""),
            "https://i.ytimg.com/vi/abc123/hqdefault.jpg" => Image(SKColors.Blue),
            "https://artist.bandcamp.test/track/song" => Html("""
                <html><head><meta property="og:title" content="Night Drive, by The Midnight &amp; Friends">
                <meta property="og:image" content="https://f4.bcbits.test/img.jpg"></head></html>
                """),
            "https://f4.bcbits.test/img.jpg" => Image(SKColors.Green),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private async Task<WishLinkPreview> ResolveAsync(string url)
    {
        var response = await _admin.PostAsJsonAsync("/rest/v1/me/wishes/resolve-link", new ResolveWishLinkInput(url), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WishLinkPreview>(ArcanistFactory.Json, Cancel))!;
    }

    private async Task<string?> ErrorAsync(string url) =>
        (await (await _admin.PostAsJsonAsync("/rest/v1/me/wishes/resolve-link", new ResolveWishLinkInput(url), ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString();

    [Fact]
    public async Task A_link_fills_the_wish_with_its_recording_and_the_cover_art_archives_cover()
    {
        // A pasted share text, with the link inside.
        var preview = await ResolveAsync("Listen on Deezer: https://www.deezer.com/en/track/3135556?utm_source=share");
        Assert.Equal((WishLinkService.Deezer, "Harder, Better, Faster, Stronger", "Daft Punk", "Discovery", 224_000, Recording),
            (preview.Service, preview.Title, preview.Artist, preview.Album, preview.DurationMs, preview.MusicBrainzRecordingId));
        // The Cover Art Archive's cover of the official album, not the service's.
        Assert.NotNull(preview.ArtworkId);
        var cover = await _admin.GetByteArrayAsync($"/rest/v1/artwork/{preview.ArtworkId}", Cancel);
        using (var bitmap = SKBitmap.Decode(cover))
            Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.DoesNotContain(_online.Requests, r => r.RequestUri!.Host == "cdn.deezer.test");

        var wish = await (await _admin.PostAsJsonAsync("/rest/v1/me/wishes",
                new WishInput(preview.Title, preview.Artist, preview.Album, preview.MusicBrainzRecordingId, DurationMs: preview.DurationMs,
                    SourceUrl: preview.Url, ArtworkId: preview.ArtworkId), ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<Wish>(ArcanistFactory.Json, Cancel);
        Assert.Equal((WishLinkService.Deezer, preview.ArtworkId, Recording), (wish!.SourceService, wish.ArtworkId, wish.MusicBrainzRecordingId));
        Assert.StartsWith("https://www.deezer.com/en/track/3135556", wish.SourceUrl);

        // The catalog's cleanup keeps the covers wishes use.
        using (var scope = _factory.Services.CreateScope())
            await new Core.Scanning.CatalogCleaner(scope.ServiceProvider.GetRequiredService<Core.Data.ArcanistDbContext>(),
                scope.ServiceProvider.GetRequiredService<Core.Scanning.ArtworkStore>(), TimeProvider.System).CleanAsync(TimeSpan.FromDays(7), Cancel);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/rest/v1/artwork/{preview.ArtworkId}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task YouTube_Music_and_other_pages_say_the_song_and_lists_are_not_songs()
    {
        var topic = await ResolveAsync("https://music.youtube.com/watch?v=abc123&si=xyz");
        // A Topic channel is the artist's; "(Remastered)" is not part of the name.
        Assert.Equal((WishLinkService.YouTubeMusic, "Weird Fishes / Arpeggi", "Radiohead", null), (topic.Service, topic.Title, topic.Artist, topic.MusicBrainzRecordingId));
        Assert.NotNull(topic.ArtworkId);

        var page = await ResolveAsync("https://artist.bandcamp.test/track/song");
        Assert.Equal((WishLinkService.Other, "Night Drive", "The Midnight & Friends"), (page.Service, page.Title, page.Artist));

        Assert.Equal("wish_link_not_a_song", await ErrorAsync("https://open.spotify.com/album/2noRn2Aes5aoNVsU6iWThc"));
        Assert.Equal("wish_link_not_a_song", await ErrorAsync("https://www.deezer.com/playlist/908622995"));
        Assert.Equal("wish_link_unreadable", await ErrorAsync("https://unknown.test/song"));
        Assert.Equal("invalid_format", (await (await _admin.PostAsJsonAsync("/rest/v1/me/wishes/resolve-link", new ResolveWishLinkInput("not a link"),
            ArcanistFactory.Json, Cancel)).Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors").GetProperty("url")[0].GetString());

        // Without external services, nothing is read.
        (await _admin.PutAsJsonAsync("/rest/v1/settings/privacy", new { noTelemetry = false, externalServices = "ULTIMATE_CLOUD_ONLY" }, ArcanistFactory.Json, Cancel))
            .EnsureSuccessStatusCode();
        var before = _online.Requests.Count;
        Assert.Equal("external_services_disabled", await ErrorAsync("https://www.deezer.com/track/3135556"));
        Assert.Equal(before, _online.Requests.Count);
    }

    [Fact]
    public async Task Everyone_s_wishes_need_their_permission_and_extensions_reach_both_lists()
    {
        var listener = await _factory.CreateUserAsync("listener");
        var curator = await _factory.CreateUserAsync("curator", permissions: Permissions.ViewAllWishes);
        var listenerClient = await _factory.CreateAuthenticatedClientAsync("listener");
        var curatorClient = await _factory.CreateAuthenticatedClientAsync("curator");
        (await listenerClient.PostAsJsonAsync("/rest/v1/me/wishes", new WishInput("Teardrop", "Massive Attack"), ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();
        (await _admin.PostAsJsonAsync("/rest/v1/me/wishes", new WishInput("Angel", "Massive Attack"), ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await listenerClient.GetAsync("/rest/v1/me/wishes?scope=ALL", Cancel)).StatusCode);
        var mine = await listenerClient.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes", ArcanistFactory.Json, Cancel);
        Assert.Equal(["Teardrop"], mine!.Items.Select(w => w.Title));
        Assert.Null(mine.Items[0].Owner);

        var all = await curatorClient.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes?scope=ALL", ArcanistFactory.Json, Cancel);
        Assert.Equal(["admin:Angel", "listener:Teardrop"], all!.Items.Select(w => $"{w.Owner!.UserName}:{w.Title}").Order());
        Assert.Equal(2, all.Wanted);

        // An extension adds to a user's list, reads it and everyone's, and removes.
        using var scope = _factory.Services.CreateScope();
        var lists = scope.ServiceProvider.GetRequiredService<IWishLists>();
        var added = await lists.AddAsync(curator.Id, new WishListEntryInput("Unfinished Sympathy", "Massive Attack"), Cancel);
        Assert.Equal(("curator", "Unfinished Sympathy"), (added.UserName, added.Title));
        Assert.Equal(["Unfinished Sympathy"], (await lists.ListAsync(curator.Id, wantedOnly: true, Cancel)).Select(w => w.Title));
        Assert.Equal(3, (await lists.ListAllAsync(wantedOnly: true, Cancel)).Count);
        Assert.Contains((await lists.ListAllAsync(false, Cancel)), w => w.UserId == listener.Id && w.Title == "Teardrop");
        Assert.True(await lists.RemoveAsync(curator.Id, added.Id, Cancel));
        Assert.False(await lists.RemoveAsync(listener.Id, added.Id, Cancel));

        var resolved = await lists.ResolveLinkAsync("https://www.deezer.com/track/3135556", Cancel);
        Assert.Equal(("Daft Punk", Recording), (resolved!.Artist, resolved.MusicBrainzRecordingId));
        Assert.Null(await lists.ResolveLinkAsync("https://open.spotify.com/album/2noRn2Aes5aoNVsU6iWThc", Cancel));
    }
}
