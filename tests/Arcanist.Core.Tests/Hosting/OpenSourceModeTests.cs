using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>The 100% open source mode, and the edition the server's mark shows.</summary>
public sealed class OpenSourceModeTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("AGPL-3.0-or-later", true)]
    [InlineData("AGPL-3.0-or-later AND MIT", true)]
    [InlineData("(MIT OR Apache-2.0) AND BSD-3-Clause", true)]
    [InlineData("MIT OR LicenseRef-Ultimate", true)]
    [InlineData("GPL-3.0-or-later WITH Classpath-exception-2.0", true)]
    [InlineData("AGPL-3.0-or-later (Webamp: MIT)", true)]
    [InlineData("MIT AND LicenseRef-Ultimate", false)]
    [InlineData("LicenseRef-Ultimate", false)]
    [InlineData("Proprietary", false)]
    [InlineData(null, false)]
    public void Licenses_are_read_as_SPDX_expressions(string? license, bool open) =>
        Assert.Equal(open, OpenSource.IsOpenLicense(license));

    /// <summary>A web player's folder, as installing its package leaves it.</summary>
    private static void Player(string dataPath, string id, string license)
    {
        var folder = Path.Combine(dataPath, "frontends", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FrontendCatalog.ManifestFile),
            JsonSerializer.Serialize(new { id, name = "Ultimate Web Player", version = "1.0.0", license }));
        File.WriteAllText(Path.Combine(folder, "index.html"), "<!doctype html><title>Player</title>");
    }

    private static byte[] PlayerPackage(string id, string license)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(FrontendCatalog.ManifestFile).Open()))
                writer.Write(JsonSerializer.Serialize(new { id, name = id, version = "1.0.0", license }));
            using (var writer = new StreamWriter(zip.CreateEntry("index.html").Open()))
                writer.Write("<!doctype html>");
        }

        return buffer.ToArray();
    }

    private static async Task<JsonElement> InfoAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/rest/v1/server-info", ArcanistFactory.Json, Cancel);

    [Fact]
    public async Task Closed_source_parts_make_it_Standard_and_the_mode_keeps_them_from_installing_or_running()
    {
        await using var factory = new ArcanistFactory();
        Directory.CreateDirectory(factory.DataPath);
        File.WriteAllText(Path.Combine(factory.DataPath, "arcanist.json"),
            JsonSerializer.Serialize(new { Arcanist = new { Extensions = new { hello = new { Enabled = true } } } }));
        var extension = Path.Combine(factory.DataPath, "extensions", "hello");
        Directory.CreateDirectory(extension);
        File.WriteAllText(Path.Combine(extension, "arcanist-extension.json"), ExtensionTests.Manifest().Replace("AGPL-3.0-or-later", "LicenseRef-Proprietary"));
        File.Copy(ExtensionTests.Assembly, Path.Combine(extension, "Arcanist.TestExtension.dll"));
        Player(factory.DataPath, "ultimate", "LicenseRef-Ultimate");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        var anonymous = factory.CreateClient();

        // A closed source player and extension in use: Standard.
        Assert.Equal("hello from hello", await anonymous.GetStringAsync("/ext/hello/hello", Cancel));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/players/ultimate/", Cancel)).StatusCode);
        Assert.Equal(("STANDARD", false), ((await InfoAsync(anonymous)).GetProperty("edition").GetString(),
            (await InfoAsync(anonymous)).GetProperty("openSourceOnly").GetBoolean()));

        // The mode on: they stop at once, and the server is open source only.
        var on = await admin.PutAsJsonAsync("/rest/v1/settings/open-source", new { enabled = true }, ArcanistFactory.Json, Cancel);
        var mode = await on.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("OPEN_SOURCE", true), (mode.GetProperty("edition").GetString(), mode.GetProperty("enabled").GetBoolean()));
        Assert.Equal(["EXTENSION:hello", "PLAYER:ultimate"], mode.GetProperty("components").EnumerateArray()
            .Select(c => $"{c.GetProperty("kind").GetString()}:{c.GetProperty("id").GetString()}").Order());
        Assert.All(mode.GetProperty("components").EnumerateArray(), c => Assert.False(c.GetProperty("active").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/ext/hello/hello", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/players/ultimate/", Cancel)).StatusCode);
        Assert.Equal(("OPEN_SOURCE", true), ((await InfoAsync(anonymous)).GetProperty("edition").GetString(),
            (await InfoAsync(anonymous)).GetProperty("openSourceOnly").GetBoolean()));

        // Nothing closed source installs, nor switches on.
        var form = new MultipartFormDataContent { { new ByteArrayContent(PlayerPackage("other", "LicenseRef-Other")), "file", "other.zip" } };
        var refused = await admin.PostAsync("/rest/v1/frontends", form, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("closed_source_disabled", (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        var open = new MultipartFormDataContent { { new ByteArrayContent(PlayerPackage("free", "MIT")), "file", "free.zip" } };
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/rest/v1/frontends", open, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync("/rest/v1/extensions/hello/enabled", new { enabled = true }, ArcanistFactory.Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync("/rest/v1/frontends/default/ultimate", null, Cancel)).StatusCode);

        // Off again: they run again.
        await admin.PutAsJsonAsync("/rest/v1/settings/open-source", new { enabled = false }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/players/ultimate/", Cancel)).StatusCode);
        Assert.Equal("hello from hello", await anonymous.GetStringAsync("/ext/hello/hello", Cancel));
        Assert.Equal("STANDARD", (await InfoAsync(anonymous)).GetProperty("edition").GetString());
    }

    [Fact]
    public async Task The_wizard_can_set_up_a_server_that_is_100_percent_open_source()
    {
        await using var factory = new ArcanistFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/rest/v1/setup/complete", new SetupRequest(
            new SetupDatabase(DatabaseProvider.Sqlite, null),
            new SetupAdmin(ArcanistFactory.AdminUserName, ArcanistFactory.Password, null),
            [], ShareAnonymousStatistics: false, Language: null, OpenSourceOnly: true,
            AcceptedTerms: Arcanist.Core.Legal.LegalService.CurrentVersion), ArcanistFactory.Json, Cancel);
        response.EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!(await InfoAsync(factory.CreateClient())).GetProperty("openSourceOnly").GetBoolean())
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(50, Cancel);
        }

        Assert.Equal("OPEN_SOURCE", (await InfoAsync(factory.CreateClient())).GetProperty("edition").GetString());
    }
}
