using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Data.Entities;
using Microsoft.Extensions.Time.Testing;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>Installing web players from the admin panel and serving them.</summary>
public sealed class FrontendManagerTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ArcanistFactory _factory = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ArcanistFactory(time: _time);
        await _factory.CompleteSetupAsync();
        _admin = await _factory.CreateAuthenticatedClientAsync();
    }

    private static byte[] Package(string id = "my-player", int minApiVersion = 1, string? manifestPath = "arcanist-frontend.json", params (string Path, string Content)[] extra)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8);
                writer.Write(content);
            }

            if (manifestPath is not null)
                Add(manifestPath, JsonSerializer.Serialize(new { id, name = "My Player", version = "1.0.0", license = "MIT", minApiVersion }));
            var root = manifestPath?.Contains('/') == true ? manifestPath[..(manifestPath.LastIndexOf('/') + 1)] : "";
            Add(root + "index.html", "<!doctype html><html><head><base href=\"/\"><title>Player</title></head><body>player</body></html>");
            Add(root + "assets/app-abcdef1234.js", "console.log('hi')");
            foreach (var (path, content) in extra)
                Add(path, content);
        }

        return buffer.ToArray();
    }

    private Task<HttpResponseMessage> UploadAsync(byte[] archive, HttpClient? client = null)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(archive), "file", "player.zip" } };
        return (client ?? _admin).PostAsync("/rest/v1/frontends", form, Cancel);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json);
        return problem.TryGetProperty("errors", out var errors) ? errors.EnumerateObject().First().Value[0].GetString() : problem.GetProperty("code").GetString();
    }

    [Fact]
    public async Task An_uploaded_player_is_served_under_players_and_can_become_the_default()
    {
        var response = await UploadAsync(Package());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var frontend = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("my-player", "/players/my-player/", false), (frontend.GetProperty("id").GetString(), frontend.GetProperty("path").GetString(), frontend.GetProperty("isDefault").GetBoolean()));

        var client = _factory.CreateClient();
        var page = await client.GetStringAsync("/players/my-player/library/albums", Cancel);
        Assert.Contains("<base href=\"/players/my-player/\">", page);
        var asset = await client.GetAsync("/players/my-player/assets/app-abcdef1234.js", Cancel);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/players/my-player/missing.js", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/players/nobody/", Cancel)).StatusCode);

        // Until chosen, / keeps the placeholder page.
        Assert.Contains("No web player is installed", await client.GetStringAsync("/", Cancel));
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsync("/rest/v1/frontends/default/my-player", null, Cancel)).StatusCode);
        var root = await client.GetStringAsync("/some/route", Cancel);
        Assert.Contains("<base href=\"/\">", root);
        Assert.Contains("player", root);

        var list = await _admin.GraphQLAsync("{ frontends { id isDefault builtIn } }");
        Assert.True(list.GetProperty("data").GetProperty("frontends")[0].GetProperty("isDefault").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync("/rest/v1/frontends/my-player", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/players/my-player/", Cancel)).StatusCode);
        Assert.Contains("No web player is installed", await client.GetStringAsync("/", Cancel));
    }

    [Fact]
    public async Task Packages_wrapped_in_a_folder_and_updates_are_accepted()
    {
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(Package(manifestPath: "my-player-1.0.0/arcanist-frontend.json"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(Package(extra: ("new.txt", "v2")))).StatusCode);
        Assert.Equal("v2", await _factory.CreateClient().GetStringAsync("/players/my-player/new.txt", Cancel));
    }

    [Fact]
    public async Task Invalid_packages_are_refused()
    {
        var notZip = await UploadAsync(Encoding.UTF8.GetBytes("not a zip"));
        Assert.Equal((HttpStatusCode.BadRequest, "package_invalid"), (notZip.StatusCode, await ErrorAsync(notZip)));

        var noManifest = await UploadAsync(Package(manifestPath: null));
        Assert.Equal("manifest_missing", await ErrorAsync(noManifest));

        var future = await UploadAsync(Package(minApiVersion: 99));
        Assert.Equal("api_version_unsupported", await ErrorAsync(future));

        var badId = await UploadAsync(Package(id: "Admin"));
        Assert.Equal("invalid_format", await ErrorAsync(badId));

        var escape = await UploadAsync(Package(extra: ("../../escaped.txt", "x")));
        Assert.Equal("package_invalid", await ErrorAsync(escape));
        Assert.False(File.Exists(Path.Combine(_factory.DataPath, "escaped.txt")));

        var url = await _admin.PostAsJsonAsync("/rest/v1/frontends/from-url", new InstallFromUrlInput("ftp://example.com/x.zip"), ArcanistFactory.Json, Cancel);
        Assert.Equal("invalid_format", await ErrorAsync(url));
        var unreachable = await _admin.PostAsJsonAsync("/rest/v1/frontends/from-url", new InstallFromUrlInput("http://127.0.0.1:9/player.zip"), ArcanistFactory.Json, Cancel);
        Assert.Equal("download_failed", await ErrorAsync(unreachable));
    }

    [Fact]
    public async Task Only_administrators_who_just_confirmed_their_password_install_players()
    {
        await _factory.CreateUserAsync("manager", permissions: Permissions.ManageLibraries | Permissions.ManageSettings);
        var manager = await _factory.CreateAuthenticatedClientAsync("manager");
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(Package(), manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/rest/v1/frontends", Cancel)).StatusCode);

        // The admin signed in more than ten minutes ago: installing asks for the password again.
        _time.Advance(TimeSpan.FromMinutes(11));
        var stale = await UploadAsync(Package());
        Assert.Equal("reauthentication_required", await ErrorAsync(stale));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
