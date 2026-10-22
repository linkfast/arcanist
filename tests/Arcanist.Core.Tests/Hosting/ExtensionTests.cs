using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>Installing, loading and switching third-party extensions (the test extension, packaged like any other).</summary>
public sealed class ExtensionTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    internal static readonly string Assembly = Path.Combine(AppContext.BaseDirectory, "test-extension", "Arcanist.TestExtension.dll");

    private static readonly object[] Apps =
    [
        new
        {
            id = "greeter", name = "Greeter", names = new Dictionary<string, string> { ["es"] = "Saludador" }, description = "Says hello",
            path = "/ext/hello/app/", icon = "/ext/hello/icon.svg", permissions = new[] { "UPLOAD" }, width = 640, height = 480,
        },
        new { id = "tools", name = "Tools", path = "/ext/hello/tools", adminOnly = true },
    ];

    internal static string Manifest(string id = "hello", string[]? routes = null, object[]? apps = null, object? adminPage = null,
        string version = "1.0.0") =>
        JsonSerializer.Serialize(new
        {
            id, name = "Hello", version, license = "AGPL-3.0-or-later", assembly = "Arcanist.TestExtension.dll", routes, apps = apps ?? Apps,
            adminPage = adminPage ?? new { icon = $"/ext/{id}/icon.svg" },
        });

    internal static byte[] Package(string? manifest = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("arcanist-extension.json").Open()))
                writer.Write(manifest ?? Manifest());
            zip.CreateEntryFromFile(Assembly, "Arcanist.TestExtension.dll");
        }

        return buffer.ToArray();
    }

    /// <summary>A server that starts with the test extension installed (and enabled, or not).</summary>
    private static async Task<ArcanistFactory> StartedWithAsync(bool enabled, bool misbehave = false)
    {
        var factory = new ArcanistFactory();
        // In arcanist.json, as the admin panel writes it, so the panel can change it afterwards.
        Directory.CreateDirectory(factory.DataPath);
        File.WriteAllText(Path.Combine(factory.DataPath, "arcanist.json"),
            JsonSerializer.Serialize(new { Arcanist = new { Extensions = new { hello = new { Enabled = enabled, Misbehave = misbehave } } } }));
        var folder = Path.Combine(factory.DataPath, "extensions", "hello");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "arcanist-extension.json"), Manifest());
        File.Copy(Assembly, Path.Combine(folder, "Arcanist.TestExtension.dll"));
        await factory.CompleteSetupAsync();
        return factory;
    }

    [Fact]
    public async Task A_managed_extension_installs_from_the_configuration_and_stays_on_but_can_be_updated()
    {
        var package = Path.Combine(Path.GetTempPath(), $"managed-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(package, Package(), Cancel);
        var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(package, Cancel)));
        try
        {
            await using var factory = new ArcanistFactory(new Dictionary<string, string?>
            {
                ["Arcanist:ManagedExtensions:0:Id"] = "hello",
                ["Arcanist:ManagedExtensions:0:Package"] = package,
                ["Arcanist:ManagedExtensions:0:Sha256"] = sha256,
                ["Arcanist:ManagedExtensions:0:Version"] = "1.0.0",
            });
            // An older copy is there already: the configured version replaces it at startup.
            var folder = Path.Combine(factory.DataPath, "extensions", "hello");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "arcanist-extension.json"), Manifest(version: "0.9.0"));
            File.Copy(Assembly, Path.Combine(folder, "Arcanist.TestExtension.dll"));
            await factory.CompleteSetupAsync();

            // On without anyone switching it on.
            Assert.Equal("hello from hello", await factory.CreateClient().GetStringAsync("/ext/hello/hello", Cancel));
            var admin = await factory.CreateAuthenticatedClientAsync();
            var listed = Assert.Single(await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel) ?? [],
                e => e.GetProperty("id").GetString() == "hello");
            Assert.Equal(("1.0.0", true, true, "RUNNING"), (listed.GetProperty("version").GetString(), listed.GetProperty("managed").GetBoolean(),
                listed.GetProperty("enabled").GetBoolean(), listed.GetProperty("status").GetString()));

            // Never switched off or uninstalled.
            var off = await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel);
            Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
            Assert.Equal("extension_managed", (await off.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/rest/v1/extensions/hello", Cancel)).StatusCode);
            Assert.True(File.Exists(Path.Combine(folder, "arcanist-extension.json")));

            // A newer version installs over it (and runs after a restart); the configured one does not undo it.
            var form = new MultipartFormDataContent { { new ByteArrayContent(Package(Manifest(version: "1.1.0"))), "file", "hello.zip" } };
            var updated = await admin.PostAsync("/rest/v1/extensions", form, Cancel);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var body = await updated.Content.ReadFromJsonAsync<JsonElement>(Cancel);
            Assert.Equal(("1.1.0", true, true), (body.GetProperty("version").GetString(), body.GetProperty("managed").GetBoolean(),
                body.GetProperty("restartRequired").GetBoolean()));
        }
        finally
        {
            File.Delete(package);
        }
    }

    [Fact]
    public void A_managed_extension_needs_no_version()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arcanist:ManagedExtensions:0:Id"] = "hello",
            ["Arcanist:ManagedExtensions:0:Package"] = "/packages/hello.zip",
            ["Arcanist:ManagedExtensions:0:Sha256"] = new string('a', 64),
        }).Build();

        Assert.Equal(new Core.Extensions.ManagedExtension("hello", "/packages/hello.zip", new string('a', 64), null),
            Assert.Single(Core.Extensions.ManagedExtensions.Read(configuration)));
    }

    [Fact]
    public async Task A_managed_extension_with_the_wrong_checksum_is_not_installed()
    {
        var package = Path.Combine(Path.GetTempPath(), $"managed-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(package, Package(), Cancel);
        try
        {
            await using var factory = new ArcanistFactory(new Dictionary<string, string?>
            {
                ["Arcanist:ManagedExtensions:0:Id"] = "hello",
                ["Arcanist:ManagedExtensions:0:Package"] = package,
                ["Arcanist:ManagedExtensions:0:Sha256"] = new string('0', 64),
            });
            await factory.CompleteSetupAsync();

            Assert.False(Directory.Exists(Path.Combine(factory.DataPath, "extensions", "hello")));
            Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/ext/hello/hello", Cancel)).StatusCode);
        }
        finally
        {
            File.Delete(package);
        }
    }

    [Fact]
    public async Task An_enabled_extension_runs_in_its_own_load_context_with_the_servers_services()
    {
        await using var factory = await StartedWithAsync(enabled: true);
        var client = factory.CreateClient();

        Assert.Equal("hello from hello", await client.GetStringAsync("/ext/hello/hello", Cancel));
        Assert.Equal("extension:hello", await client.GetStringAsync("/ext/hello/where", Cancel));
        Assert.Equal("Arcanist", await client.GetStringAsync("/ext/hello/server", Cancel));
        Assert.Equal("ok", await client.GetStringAsync("/ext/hello/data", Cancel));

        var admin = await factory.CreateAuthenticatedClientAsync();
        var listed = Assert.Single(await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel) ?? [], e => e.GetProperty("id").GetString() == "hello");
        Assert.Equal(("RUNNING", true, false), (listed.GetProperty("status").GetString(), listed.GetProperty("enabled").GetBoolean(), listed.GetProperty("restartRequired").GetBoolean()));
        // Its settings page, which the panel lists under Extensions.
        Assert.Equal(("/admin/extensions/hello", "/ext/hello/icon.svg"), (listed.GetProperty("adminUrl").GetString(), listed.GetProperty("iconUrl").GetString()));

        // Switching it off takes effect at once.
        var off = await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ext/hello/hello", Cancel)).StatusCode);
    }

    [Fact]
    public async Task An_extension_describes_its_settings_page_and_the_panel_draws_it()
    {
        await using var factory = await StartedWithAsync(enabled: true);
        var admin = await factory.CreateAuthenticatedClientAsync();

        var page = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/extensions/hello/admin?language=es", ArcanistFactory.Json, Cancel);
        var section = page.GetProperty("sections")[0];
        Assert.Equal("Saludo", section.GetProperty("title").GetString());
        Assert.Equal(["FIELD", "FIELD", "STATUS"], section.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("kind").GetString()));
        Assert.Equal(("TEXT", "hi"), (section.GetProperty("blocks")[0].GetProperty("type").GetString(), section.GetProperty("blocks")[0].GetProperty("value").GetString()));

        async Task<JsonElement> SaveAsync(object values) =>
            await (await admin.PutAsJsonAsync("/rest/v1/extensions/hello/admin", new { values }, ArcanistFactory.Json, Cancel)).Content
                .ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        var refused = await SaveAsync(new { greeting = "" });
        Assert.Equal((false, "required"), (refused.GetProperty("ok").GetBoolean(), refused.GetProperty("errors").GetProperty("greeting").GetString()));
        Assert.True((await SaveAsync(new { greeting = "hola", loud = true })).GetProperty("ok").GetBoolean());
        page = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/extensions/hello/admin", ArcanistFactory.Json, Cancel);
        Assert.Equal(("hola", "LOUD", "WARNING"), (page.GetProperty("sections")[0].GetProperty("blocks")[0].GetProperty("value").GetString(),
            page.GetProperty("sections")[0].GetProperty("blocks")[2].GetProperty("text").GetString(),
            page.GetProperty("sections")[0].GetProperty("blocks")[2].GetProperty("tone").GetString()));

        var shout = await admin.PostAsJsonAsync("/rest/v1/extensions/hello/admin/actions/shout", new { values = new { } }, ArcanistFactory.Json, Cancel);
        Assert.Equal("HOLA", (await shout.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("message").GetString());
        // A page that throws does not take the request down.
        var broken = await admin.PostAsJsonAsync("/rest/v1/extensions/hello/admin/actions/break", new { values = new { } }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, broken.StatusCode);
        Assert.Contains("extension_failed", await broken.Content.ReadAsStringAsync(Cancel));

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/extensions/hello/admin", Cancel)).StatusCode);

        // Switched off, it has no page.
        await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/rest/v1/extensions/hello/admin", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_extension_is_not_loaded()
    {
        await using var factory = await StartedWithAsync(enabled: false);

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/ext/hello/hello", Cancel)).StatusCode);

        var admin = await factory.CreateAuthenticatedClientAsync();
        var on = await (await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("NOT_LOADED", true), (on.GetProperty("status").GetString(), on.GetProperty("restartRequired").GetBoolean()));
    }

    [Fact]
    public async Task Extensions_cannot_take_routes_they_do_not_own()
    {
        await using var factory = await StartedWithAsync(enabled: true, misbehave: true);
        var admin = await factory.CreateAuthenticatedClientAsync();

        var listed = Assert.Single(await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel) ?? [], e => e.GetProperty("id").GetString() == "hello");
        Assert.Equal(("FAILED", "route_not_allowed"), (listed.GetProperty("status").GetString(), listed.GetProperty("errorCode").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/rest/v1/hijacked", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/ext/hello/hello", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Administrators_install_and_remove_extension_packages()
    {
        await using var factory = new ArcanistFactory();
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var form = new MultipartFormDataContent { { new ByteArrayContent(Package()), "file", "hello.zip" } };
        var response = await admin.PostAsync("/rest/v1/extensions", form, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var installed = await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("hello", "NOT_LOADED", false), (installed.GetProperty("id").GetString(), installed.GetProperty("status").GetString(), installed.GetProperty("enabled").GetBoolean()));

        // Only the prefixes kept for compatibility layers can be claimed.
        var greedy = new MultipartFormDataContent { { new ByteArrayContent(Package(Manifest("greedy", ["/graphql"]))), "file", "greedy.zip" } };
        var refused = await admin.PostAsync("/rest/v1/extensions", greedy, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/rest/v1/extensions/hello", Cancel)).StatusCode);
        // Only the built-in ones are left.
        Assert.All((await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel))!,
            e => Assert.True(e.GetProperty("builtIn").GetBoolean()));

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/extensions", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Users_see_the_apps_of_running_extensions_they_may_use()
    {
        await using var factory = await StartedWithAsync(enabled: true);
        var admin = await factory.CreateAuthenticatedClientAsync();
        Assert.Equal("ok", await factory.CreateClient().GetStringAsync("/ext/hello/package", Cancel));

        var apps = await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extension-apps?language=es-PE", ArcanistFactory.Json, Cancel);
        Assert.Equal(["Saludador", "Tools"], apps!.Select(a => a.GetProperty("name").GetString()));
        var greeter = apps[0];
        Assert.Equal(("hello", "greeter", "/ext/hello/app/", "/ext/hello/icon.svg", 640),
            (greeter.GetProperty("extensionId").GetString(), greeter.GetProperty("id").GetString(), greeter.GetProperty("url").GetString(),
                greeter.GetProperty("iconUrl").GetString(), greeter.GetProperty("width").GetInt32()));

        // In English otherwise; the same through GraphQL.
        var english = (await admin.GraphQLAsync("query { extensionApps(language: \"en\") { id name description } }")).GetProperty("data").GetProperty("extensionApps");
        Assert.Equal("Greeter", english[0].GetProperty("name").GetString());
        Assert.Equal("Says hello", english[0].GetProperty("description").GetString());
        Assert.Contains("EXTENSION_APPS", (await admin.GraphQLAsync("query { serverInfo { features } }")).GetRawText());
        // Asked for in English, the base names win over other languages the request accepts.
        using (var english2 = new HttpRequestMessage(HttpMethod.Get, "/rest/v1/extension-apps?language=en"))
        {
            english2.Headers.AcceptLanguage.ParseAdd("es");
            var listed = await (await admin.SendAsync(english2, Cancel)).Content.ReadFromJsonAsync<List<JsonElement>>(ArcanistFactory.Json, Cancel);
            Assert.Equal("Greeter", listed![0].GetProperty("name").GetString());
        }

        // Users see those they have the permissions for, and never the ones for administrators.
        await factory.CreateUserAsync("listener");
        await factory.CreateUserAsync("uploader", permissions: Permissions.Upload);
        Assert.Empty((await (await factory.CreateAuthenticatedClientAsync("listener")).GetFromJsonAsync<List<JsonElement>>("/rest/v1/extension-apps", ArcanistFactory.Json, Cancel))!);
        Assert.Equal(["greeter"], (await (await factory.CreateAuthenticatedClientAsync("uploader")).GetFromJsonAsync<List<JsonElement>>("/rest/v1/extension-apps", ArcanistFactory.Json, Cancel))!
            .Select(a => a.GetProperty("id").GetString()));

        // Switched off, its apps go away.
        await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel);
        Assert.Empty((await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extension-apps", ArcanistFactory.Json, Cancel))!);
    }

    [Fact]
    public async Task Apps_must_be_pages_of_their_own_extension()
    {
        await using var factory = new ArcanistFactory();
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var outside = new MultipartFormDataContent { { new ByteArrayContent(Package(Manifest(adminPage: new { icon = "/admin/icon.svg" }))), "file", "hello.zip" } };
        var refusedPage = await admin.PostAsync("/rest/v1/extensions", outside, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refusedPage.StatusCode);
        Assert.Contains("adminPage", await refusedPage.Content.ReadAsStringAsync(Cancel));

        foreach (var app in new object[]
                 {
                     new { id = "away", name = "Away", path = "/ext/other/app" },
                     new { id = "core", name = "Core", path = "/rest/v1/users" },
                     new { id = "climb", name = "Climb", path = "/ext/hello/../../admin" },
                     new { id = "power", name = "Power", path = "/ext/hello/app", permissions = new[] { "ROOT" } },
                     new { id = "Bad Id", name = "Bad", path = "/ext/hello/app" },
                 })
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(Package(Manifest(apps: [app]))), "file", "hello.zip" } };
            var response = await admin.PostAsync("/rest/v1/extensions", form, Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("apps", await response.Content.ReadAsStringAsync(Cancel));
        }
    }

    [Fact]
    public async Task Extensions_put_files_into_libraries_on_behalf_of_users()
    {
        var music = Path.Combine(Path.GetTempPath(), "arcanist-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(music);
        await using var factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Uploads:ScanDelay"] = "00:00:00.2" });
        Directory.CreateDirectory(factory.DataPath);
        File.WriteAllText(Path.Combine(factory.DataPath, "arcanist.json"),
            JsonSerializer.Serialize(new { Arcanist = new { Extensions = new { hello = new { Enabled = true } } } }));
        var folder = Path.Combine(factory.DataPath, "extensions", "hello");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "arcanist-extension.json"), Manifest());
        File.Copy(Assembly, Path.Combine(folder, "Arcanist.TestExtension.dll"));
        await factory.CompleteSetupAsync(new SetupLibrary("Music", [music]));
        var admin = await factory.CreateAuthenticatedClientAsync();
        await factory.WaitForScansAsync(admin);
        var library = Guid.Parse((await admin.GraphQLAsync("query { libraries { id } }")).GetProperty("data").GetProperty("libraries")[0].GetProperty("id").GetString()!);
        var uploader = await factory.CreateUserAsync("uploader", permissions: Permissions.Upload);
        var listener = await factory.CreateUserAsync("listener");
        await using (var db = factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext())
        {
            db.LibraryAccess.Add(new LibraryAccess { UserId = uploader.Id, LibraryId = library });
            db.LibraryAccess.Add(new LibraryAccess { UserId = listener.Id, LibraryId = library });
            await db.SaveChangesAsync(Cancel);
        }

        var wav = AudioFixtures.WriteWav(Path.Combine(music + "-staging", "fetched.wav"), 2, new FixtureTags("Fetched", "Band", "Online", "Band", TrackNumber: 1));
        var client = factory.CreateClient();
        var sent = await client.PostAsync($"/ext/hello/import?user={uploader.Id}&library={library}&name=fetched.wav", new ByteArrayContent(File.ReadAllBytes(wav)), Cancel);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        // In the user's history, as their upload, naming the extension that brought it.
        var history = (await (await factory.CreateAuthenticatedClientAsync("uploader")).GraphQLAsync("query { myUploads { items { name status source } } }"))
            .GetProperty("data").GetProperty("myUploads").GetProperty("items");
        Assert.Equal(("fetched.wav", "hello"), (history[0].GetProperty("name").GetString(), history[0].GetProperty("source").GetString()));

        // Without the UPLOAD permission, nothing goes in.
        var refused = await client.PostAsync($"/ext/hello/import?user={listener.Id}&library={library}&name=fetched.wav", new ByteArrayContent(File.ReadAllBytes(wav)), Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Administrators_restart_the_server()
    {
        await using var factory = new ArcanistFactory(new Dictionary<string, string?> { ["Arcanist:Hosting:StopToRestart"] = "false" });
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var restart = await admin.GraphQLAsync("mutation { restartServer { restartRequired } }");
        Assert.True(restart.GetProperty("data").GetProperty("restartServer").GetProperty("restartRequired").GetBoolean());

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.PostAsync("/rest/v1/server/restart", null, Cancel)).StatusCode);
    }
}
