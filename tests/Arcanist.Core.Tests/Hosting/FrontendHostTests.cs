using System.Net;

namespace Arcanist.Core.Tests.Hosting;

public sealed class FrontendHostTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("arcanist-frontends").FullName;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Admin => Path.Combine(_root, "admin");
    private string Player => Path.Combine(_root, "player");

    public ValueTask InitializeAsync()
    {
        Write(Admin, "index.html", "<html>admin</html>");
        Write(Admin, "main-ABCD1234.js", "console.log('admin')");
        Write(Player, "index.html", "<html>player</html>");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_admin_panel_is_served_under_admin_with_client_side_routes()
    {
        await using var factory = Factory(player: false);
        await factory.CompleteSetupAsync();
        var client = factory.CreateClient();

        Assert.Contains("admin", await client.GetStringAsync("/admin", Cancel));
        Assert.Contains("admin", await client.GetStringAsync("/admin/", Cancel));
        Assert.Contains("admin", await client.GetStringAsync("/admin/users/42", Cancel));

        var asset = await client.GetAsync("/admin/main-ABCD1234.js", Cancel);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl!.Extensions.Select(e => e.Name));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/missing-file.js", Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_wizard_is_reachable_before_setup()
    {
        await using var factory = Factory(player: false);

        Assert.Contains("admin", await factory.CreateClient().GetStringAsync("/admin/setup", Cancel));
    }

    [Fact]
    public async Task Without_a_player_the_root_points_to_the_admin_panel()
    {
        await using var factory = Factory(player: false);
        await factory.CompleteSetupAsync();

        var page = await factory.CreateClient().GetStringAsync("/", Cancel);
        Assert.Contains("/admin/", page);
        Assert.Contains("AGPL", page);
    }

    [Fact]
    public async Task The_player_owns_the_root_and_its_client_side_routes()
    {
        await using var factory = Factory(player: true);
        await factory.CompleteSetupAsync();
        var client = factory.CreateClient();

        Assert.Contains("player", await client.GetStringAsync("/", Cancel));
        Assert.Contains("player", await client.GetStringAsync("/albums/123", Cancel));
    }

    [Theory]
    [InlineData("/rest/v1/does-not-exist")]
    [InlineData("/api/v1/ping")]
    [InlineData("/media/music/song.mp3")]
    [InlineData("/players/other/index.html")]
    [InlineData("/ext/something")]
    public async Task Reserved_paths_never_fall_back_to_html(string path)
    {
        await using var factory = Factory(player: true);
        await factory.CompleteSetupAsync();

        var response = await factory.CreateClient().GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Apis_keep_working_next_to_the_fallbacks()
    {
        await using var factory = Factory(player: true);
        await factory.CompleteSetupAsync();
        var client = factory.CreateClient();

        var graphql = await client.GraphQLAsync("{ serverInfo { name } }");
        Assert.Equal("Arcanist", graphql.GetProperty("data").GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/rest/v1/server-info", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Players_that_come_with_the_server_are_served_and_kept()
    {
        var webamp = Path.Combine(_root, "bundled", "webamp");
        Write(webamp, "index.html", "<html><head></head>webamp</html>");
        Write(webamp, "arcanist-frontend.json", "{\"id\":\"webamp\",\"name\":\"Webamp\",\"version\":\"0.1.0\"}");
        Write(Path.Combine(webamp, "assets"), "index-ABCD1234.js", "console.log('webamp')");
        await using var factory = Factory(player: false);
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        // Without the main player, it is the one at /, with its files.
        Assert.Contains("webamp", await admin.GetStringAsync("/", Cancel));
        Assert.Contains("webamp", await admin.GetStringAsync("/assets/index-ABCD1234.js", Cancel));
        Assert.Contains("webamp", await admin.GetStringAsync("/players/webamp/assets/index-ABCD1234.js", Cancel));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/assets/missing.js", Cancel)).StatusCode);
        Assert.Contains("webamp", await admin.GetStringAsync("/players/webamp/some/route", Cancel));
        var listed = (await admin.GraphQLAsync("{ frontends { id builtIn isDefault } }")).GetProperty("data").GetProperty("frontends").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == "webamp");
        Assert.True(listed.GetProperty("builtIn").GetBoolean());
        Assert.True(listed.GetProperty("isDefault").GetBoolean());

        // It cannot be uninstalled.
        Assert.Equal("not_found", (await admin.GraphQLAsync("mutation { uninstallFrontend(id: \"webamp\") }")).ErrorCode());
    }

    private ArcanistFactory Factory(bool player) => new(new Dictionary<string, string?>
    {
        ["Arcanist:Frontends:AdminRoot"] = Admin,
        ["Arcanist:Frontends:PlayerRoot"] = player ? Player : Path.Combine(_root, "no-player"),
        ["Arcanist:Frontends:BundledRoot"] = Path.Combine(_root, "bundled"),
    });

    private static void Write(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), content);
    }
}
