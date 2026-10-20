using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Extensions;
using Arcanist.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>A catalog and its packages, served in memory as raw.githubusercontent.com and a release would.</summary>
public sealed class FakeMarketplace : HttpMessageHandler
{
    public const string IndexUrl = "https://catalog.test/index.json";

    public Dictionary<string, byte[]> Files { get; } = [];

    public object Index { get; set; } = new { format = 1, extensions = Array.Empty<object>() };

    public int IndexReads { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (url == IndexUrl)
        {
            IndexReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Index) });
        }

        return Task.FromResult(Files.TryGetValue(url, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class MarketplaceTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeMarketplace _catalog = new();

    private ArcanistFactory Create() => new(
        new Dictionary<string, string?> { ["Arcanist:Marketplace:Url"] = FakeMarketplace.IndexUrl },
        services: services => services.AddHttpClient(Core.Application.Frontends.FrontendService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _catalog));

    private static object Entry(string id, bool official, params object[] releases) => new
    {
        id, name = "Hello", names = new Dictionary<string, string> { ["es"] = "Hola" }, description = "Says hello", author = "LinkFast",
        official, categories = new[] { "tools" }, icon = "https://catalog.test/hello.svg", releases,
    };

    private object Release(string version, byte[] package, string? minServerVersion = null, string? sha256 = null)
    {
        var url = $"https://catalog.test/hello-{version}.zip";
        _catalog.Files[url] = package;
        return new { version, url, sha256 = sha256 ?? Convert.ToHexStringLower(SHA256.HashData(package)), size = package.Length, minServerVersion };
    }

    [Fact]
    public async Task Administrators_browse_the_catalog_and_install_from_it_with_one_click()
    {
        var package = ExtensionTests.Package();
        _catalog.Index = new
        {
            format = 1,
            extensions = new object[]
            {
                Entry("hello", true, Release("1.0.0", package), Release("9.0.0", package, minServerVersion: "99.0.0")),
                // Built in: shown as such, never installed over it.
                new { id = "dlna", name = "DLNA", official = true, releases = new[] { Release("1.0.0", package) } },
                // Does not hold together (no releases): left out.
                new { id = "broken", name = "Broken", official = false, releases = Array.Empty<object>() },
            },
        };
        await using var factory = Create();
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var marketplace = await admin.GetFromJsonAsync<ExtensionMarketplaceInfo>("/rest/v1/extensions/marketplace?language=es", ArcanistFactory.Json, Cancel);
        Assert.True(marketplace!.Available);
        Assert.Equal(["dlna", "hello"], marketplace.Extensions.Select(e => e.Id).Order());
        var hello = marketplace.Extensions.Single(e => e.Id == "hello");
        // The newest this server runs, in the language asked for.
        Assert.Equal(("Hola", "1.0.0", true, null, false), (hello.Name, hello.Version, hello.Compatible, hello.InstalledVersion, hello.BuiltIn));
        Assert.True(marketplace.Extensions.Single(e => e.Id == "dlna").BuiltIn);

        // Read once an hour, unless asked for.
        await admin.GetAsync("/rest/v1/extensions/marketplace", Cancel);
        Assert.Equal(1, _catalog.IndexReads);
        await admin.GetAsync("/rest/v1/extensions/marketplace?refresh=true", Cancel);
        Assert.Equal(2, _catalog.IndexReads);

        // Its icon, through this server: players never reach the catalog's host.
        _catalog.Files["https://catalog.test/hello.svg"] = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        Assert.Equal("/rest/v1/extensions/marketplace/icons/hello", hello.IconUrl);
        var icon = await factory.CreateClient().GetAsync(hello.IconUrl, Cancel);
        Assert.Equal(("image/svg+xml", true), (icon.Content.Headers.ContentType?.MediaType, icon.Headers.Contains("Content-Security-Policy")));
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/rest/v1/extensions/marketplace/icons/missing", Cancel)).StatusCode);

        // One click: installed and switched on, waiting for a restart.
        var installed = await admin.PostAsJsonAsync("/rest/v1/extensions/marketplace/install", new InstallMarketplaceExtensionInput("hello"), ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, installed.StatusCode);
        var extension = await installed.Content.ReadFromJsonAsync<Extension>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("hello", "1.0.0", true, true), (extension!.Id, extension.Version, extension.Enabled, extension.RestartRequired));

        marketplace = await admin.GetFromJsonAsync<ExtensionMarketplaceInfo>("/rest/v1/extensions/marketplace", ArcanistFactory.Json, Cancel);
        Assert.Equal(("1.0.0", false), (marketplace!.Extensions.Single(e => e.Id == "hello").InstalledVersion, marketplace.Extensions.Single(e => e.Id == "hello").UpdateAvailable));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/rest/v1/extensions/marketplace/install", new InstallMarketplaceExtensionInput("dlna"), ArcanistFactory.Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync("/rest/v1/extensions/marketplace/install", new InstallMarketplaceExtensionInput("missing"), ArcanistFactory.Json, Cancel)).StatusCode);

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/extensions/marketplace", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Packages_that_are_not_what_the_catalog_says_are_refused()
    {
        var package = ExtensionTests.Package();
        _catalog.Index = new
        {
            format = 1,
            extensions = new object[]
            {
                Entry("hello", false, Release("1.0.0", package, sha256: new string('0', 64))),
                // The package holds "hello", not "other".
                Entry("other", false, Release("1.0.0", package)),
                Entry("future", false, Release("2.0.0", package, minServerVersion: "99.0.0")),
            },
        };
        await using var factory = Create();
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        async Task<string?> Install(string id)
        {
            var response = await admin.PostAsJsonAsync("/rest/v1/extensions/marketplace/install", new InstallMarketplaceExtensionInput(id), ArcanistFactory.Json, Cancel);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(ArcanistFactory.Json, Cancel)).GetProperty("code").GetString();
        }

        Assert.Equal("package_checksum_mismatch", await Install("hello"));
        Assert.Equal("package_mismatch", await Install("other"));
        Assert.Equal("marketplace_incompatible", await Install("future"));
        Assert.DoesNotContain(await admin.GetFromJsonAsync<List<Extension>>("/rest/v1/extensions", ArcanistFactory.Json, Cancel) ?? [], e => !e.BuiltIn);
    }

    [Fact]
    public async Task Without_external_services_the_catalog_is_not_read()
    {
        await using var factory = Create();
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.PutAsJsonAsync("/rest/v1/settings/privacy", new { noTelemetry = false, externalServices = "NONE" }, ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();

        var marketplace = await admin.GetFromJsonAsync<ExtensionMarketplaceInfo>("/rest/v1/extensions/marketplace", ArcanistFactory.Json, Cancel);
        Assert.Equal((false, "external_services_disabled"), (marketplace!.Available, marketplace.ErrorCode));
        Assert.Equal(0, _catalog.IndexReads);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("0.2.0", "0.10.0", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0", -1)]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-beta", "1.0.0-alpha.9", 1)]
    [InlineData("v0.1.0-alpha.1", "0.1.0-alpha.1", 0)]
    [InlineData("1.0.0+build.5", "1.0.0", 0)]
    public void Versions_are_ordered_as_semantic_versioning_says(string a, string b, int order)
    {
        Assert.Equal(order, Math.Sign(ArcanistVersion.Compare(a, b)));
        Assert.Equal(-order, Math.Sign(ArcanistVersion.Compare(b, a)));
    }
}
