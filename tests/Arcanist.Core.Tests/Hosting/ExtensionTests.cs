using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>Installing, loading and switching third-party extensions (the test extension, packaged like any other).</summary>
public sealed class ExtensionTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly string Assembly = Path.Combine(AppContext.BaseDirectory, "test-extension", "Arcanist.TestExtension.dll");

    private static string Manifest(string id = "hello", string[]? routes = null) =>
        JsonSerializer.Serialize(new { id, name = "Hello", version = "1.0.0", license = "AGPL-3.0-or-later", assembly = "Arcanist.TestExtension.dll", routes });

    private static byte[] Package(string? manifest = null)
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
    public async Task An_enabled_extension_runs_in_its_own_load_context_with_the_servers_services()
    {
        await using var factory = await StartedWithAsync(enabled: true);
        var client = factory.CreateClient();

        Assert.Equal("hello from hello", await client.GetStringAsync("/ext/hello/hello", Cancel));
        Assert.Equal("extension:hello", await client.GetStringAsync("/ext/hello/where", Cancel));
        Assert.Equal("Arcanist", await client.GetStringAsync("/ext/hello/server", Cancel));
        Assert.Equal("ok", await client.GetStringAsync("/ext/hello/data", Cancel));

        var admin = await factory.CreateAuthenticatedClientAsync();
        var listed = Assert.Single(await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel) ?? []);
        Assert.Equal(("RUNNING", true, false), (listed.GetProperty("status").GetString(), listed.GetProperty("enabled").GetBoolean(), listed.GetProperty("restartRequired").GetBoolean()));

        // Switching it off takes effect at once.
        var off = await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = false }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ext/hello/hello", Cancel)).StatusCode);
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

        var listed = Assert.Single(await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel) ?? []);
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
        Assert.Empty((await admin.GetFromJsonAsync<List<JsonElement>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel))!);

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/extensions", Cancel)).StatusCode);
    }
}
