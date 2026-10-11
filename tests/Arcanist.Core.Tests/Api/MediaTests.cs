using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Permissions = Arcanist.Core.Data.Entities.Permissions;

namespace Arcanist.Core.Tests.Api;

public sealed class MediaTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-media").FullName;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ArcanistFactory _factory;
    private HttpClient _admin = null!;
    private Track _track = null!;

    public MediaTests() => _factory = new ArcanistFactory(time: _time);

    private string SongPath => Path.Combine(_root, "Music", "Album", "song.wav");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(SongPath, 1, new FixtureTags("Song", "Artist", "Album", TrackNumber: 1));
        // A cover larger than the thumbnail sizes, stored next to the music.
        using (var bitmap = new SKBitmap(600, 400))
        {
            bitmap.Erase(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            File.WriteAllBytes(Path.Combine(_root, "Music", "Album", "cover.png"), image.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }
        AudioFixtures.WriteWav(Path.Combine(_root, "Private", "secret.wav"), 2, new FixtureTags("Secret", "Someone", "Hidden"));

        await _factory.CompleteSetupAsync(
            new SetupLibrary("Music", [Path.Combine(_root, "Music")], VisibleToAll: true),
            new SetupLibrary("Private", [Path.Combine(_root, "Private")], VisibleToAll: false));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);

        _track = (await _admin.GetFromJsonAsync<TrackPage>("/rest/v1/tracks?search=song", ArcanistFactory.Json, Cancel))!.Items.Single();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Streams_the_whole_file_with_its_type_and_validators()
    {
        var response = await _admin.GetAsync($"/rest/v1/stream/{_track.Id}", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(SongPath, Cancel), await response.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal("bytes", Assert.Single(response.Headers.AcceptRanges));
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task Range_requests_return_partial_content_for_seeking()
    {
        var size = new FileInfo(SongPath).Length;
        var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/stream/{_track.Id}");
        request.Headers.Range = new RangeHeaderValue(100, 199);

        var response = await _admin.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal($"bytes 100-199/{size}", response.Content.Headers.ContentRange?.ToString());
        var expected = (await File.ReadAllBytesAsync(SongPath, Cancel))[100..200];
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task Head_reports_the_length_without_a_body()
    {
        var response = await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/rest/v1/stream/{_track.Id}"), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new FileInfo(SongPath).Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task Stream_urls_work_without_an_authorization_header()
    {
        Assert.Contains("st=", _track.StreamUrl);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(_track.StreamUrl, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(_track.Files[0].StreamUrl, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/rest/v1/stream/{_track.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(_track.StreamUrl.Replace("st=", "st=x"), Cancel)).StatusCode);
    }

    [Fact]
    public async Task Stream_tokens_expire_and_die_with_their_session()
    {
        var tokens = await _factory.LoginAsync();
        var track = (await _factory.CreateClient(tokens.AccessToken).GetFromJsonAsync<Track>($"/rest/v1/tracks/{_track.Id}", ArcanistFactory.Json, Cancel))!;
        var anonymous = _factory.CreateClient();

        await _factory.CreateClient(tokens.AccessToken).PostAsJsonAsync("/rest/v1/auth/logout", new Arcanist.Core.Api.Rest.RefreshRequest(tokens.RefreshToken), Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(track.StreamUrl, Cancel)).StatusCode);

        _time.Advance(TimeSpan.FromHours(7));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(_track.StreamUrl, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Users_cannot_stream_tracks_outside_their_libraries()
    {
        var secret = (await _admin.GetFromJsonAsync<TrackPage>("/rest/v1/tracks?search=secret", ArcanistFactory.Json, Cancel))!.Items.Single();
        await _factory.CreateUserAsync("guest");
        var guest = await _factory.CreateAuthenticatedClientAsync("guest");

        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/rest/v1/stream/{_track.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/rest/v1/stream/{secret.Id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Downloading_the_original_requires_the_permission()
    {
        await _factory.CreateUserAsync("listener");
        await _factory.CreateUserAsync("collector", permissions: Permissions.Download);

        var listener = await _factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync($"/rest/v1/stream/{_track.Id}?download=true", Cancel)).StatusCode);

        var collector = await _factory.CreateAuthenticatedClientAsync("collector");
        var response = await collector.GetAsync($"/rest/v1/stream/{_track.Id}?download=true", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("song.wav", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task A_file_deleted_since_the_last_scan_is_not_found()
    {
        File.Delete(SongPath);

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/rest/v1/stream/{_track.Id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Artwork_is_public_scaled_on_demand_and_cacheable()
    {
        var anonymous = _factory.CreateClient();
        var artworkId = _track.ArtworkId!.Value;

        var original = await anonymous.GetAsync($"/rest/v1/artwork/{artworkId}", Cancel);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal("image/png", original.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", original.Headers.CacheControl!.Extensions.Select(e => e.Name));

        var thumbnail = await anonymous.GetAsync($"/rest/v1/artwork/{artworkId}?size=200", Cancel);
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);
        using (var bitmap = SKBitmap.Decode(await thumbnail.Content.ReadAsByteArrayAsync(Cancel)))
        {
            Assert.Equal(256, bitmap.Width);
            Assert.Equal(171, bitmap.Height);
        }

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/rest/v1/artwork/{artworkId}?size=200");
        conditional.Headers.IfNoneMatch.Add(thumbnail.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await anonymous.SendAsync(conditional, Cancel)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/rest/v1/artwork/{Guid.NewGuid()}", Cancel)).StatusCode);
    }
}
