using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arcanist.Core.Application.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Hosting;

/// <summary>GitHub's list of releases of Arcanist, in memory.</summary>
public sealed class FakeGitHubReleases : HttpMessageHandler
{
    public const string Url = "https://github.test/repos/linkfast/arcanist/releases";

    public bool Failing { get; set; }

    public object[] Releases { get; set; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Failing || request.RequestUri!.ToString() != Url
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Releases) });

    public static object Release(string tag, bool prerelease = false, bool draft = false) => new
    {
        tag_name = tag, name = $"Arcanist {tag.TrimStart('v')}", body = $"## What's changed\n* Things in {tag}",
        html_url = $"https://github.com/linkfast/arcanist/releases/tag/{tag}", draft, prerelease, published_at = "2026-10-19T12:00:00Z",
    };
}

public sealed class UpdateTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeGitHubReleases _github = new()
    {
        Releases =
        [
            FakeGitHubReleases.Release("v0.1.0-alpha.1", prerelease: true),
            FakeGitHubReleases.Release("v0.1.0-alpha.2", prerelease: true),
            FakeGitHubReleases.Release("v0.0.9"),
            FakeGitHubReleases.Release("v0.3.0", draft: true),
            FakeGitHubReleases.Release("nightly", prerelease: true),
        ],
    };

    private ArcanistFactory Create(string current) => new(
        new Dictionary<string, string?>
        {
            ["Arcanist:Updates:ReleasesUrl"] = FakeGitHubReleases.Url,
            ["Arcanist:Updates:CurrentVersion"] = current,
        },
        services: services => services.AddHttpClient(UpdateChecker.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _github));

    [Fact]
    public async Task A_pre_release_server_is_told_of_newer_pre_releases()
    {
        await using var factory = Create("0.1.0-alpha.1");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.True(update!.UpdateAvailable);
        Assert.Equal(("0.1.0-alpha.2", "Arcanist 0.1.0-alpha.2", true), (update.Latest!.Version, update.Latest.Name, update.Latest.Prerelease));
        Assert.Contains("Things in v0.1.0-alpha.2", update.Latest.Notes);
        Assert.Equal("0.1.0-alpha.1", update.CurrentVersion);

        // A failed check keeps what was found before.
        _github.Failing = true;
        update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("update_check_failed", "0.1.0-alpha.2"), (update!.ErrorCode, update.Latest?.Version));

        // Switched off, nothing is checked or shown.
        var off = await admin.PutAsJsonAsync("/rest/v1/server/update/settings", new SetServerUpdateSettingsInput(false), ArcanistFactory.Json, Cancel);
        update = await off.Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal((false, false, null), (update!.Enabled, update.UpdateAvailable, update.Latest));

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/server/update", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_stable_server_only_follows_stable_releases()
    {
        await using var factory = Create("0.1.0");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal(("0.0.9", false), (update!.Latest?.Version, update.UpdateAvailable));
    }

    [Fact]
    public async Task Without_external_services_there_are_no_checks()
    {
        await using var factory = Create("0.1.0-alpha.1");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.PutAsJsonAsync("/rest/v1/settings/privacy", new { noTelemetry = false, externalServices = "NONE" }, ArcanistFactory.Json, Cancel)).EnsureSuccessStatusCode();

        var update = await (await admin.PostAsync("/rest/v1/server/update/check", null, Cancel)).Content.ReadFromJsonAsync<ServerUpdate>(ArcanistFactory.Json, Cancel);
        Assert.Equal((false, null, null), (update!.Enabled, update.Latest, update.CheckedAt));
    }

    [Fact]
    public async Task Administrators_read_where_the_server_runs()
    {
        await using var factory = Create("0.1.0");
        await factory.CompleteSetupAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var host = await admin.GetFromJsonAsync<JsonElement>("/rest/v1/server/host", ArcanistFactory.Json, Cancel);
        Assert.Equal(factory.DataPath, host.GetProperty("dataDirectory").GetString());
        Assert.Equal(Environment.UserName, host.GetProperty("user").GetString());
        Assert.False(string.IsNullOrEmpty(host.GetProperty("kind").GetString()));
        Assert.Equal(host.GetProperty("kind").GetString(),
            (await admin.GraphQLAsync("{ serverHost { kind } }")).GetProperty("data").GetProperty("serverHost").GetProperty("kind").GetString());

        await factory.CreateUserAsync("listener");
        var listener = await factory.CreateAuthenticatedClientAsync("listener");
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/rest/v1/server/host", Cancel)).StatusCode);
    }

    [Fact]
    public void A_NAS_app_says_how_it_was_installed()
    {
        // Only this test sets the variable, and only DetectInstallKind reads it.
        try
        {
            foreach (var (value, kind) in new[] { ("ROCKSTOR", InstallKind.Rockstor), ("TRUE_NAS", InstallKind.TrueNas), ("qnap", InstallKind.Qnap) })
            {
                Environment.SetEnvironmentVariable("ARCANIST_INSTALL_KIND", value);
                Assert.Equal(kind, UpdateChecker.DetectInstallKind());
            }
            Environment.SetEnvironmentVariable("ARCANIST_INSTALL_KIND", "SOMETHING_ELSE");
            Assert.NotEqual(InstallKind.Rockstor, UpdateChecker.DetectInstallKind());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARCANIST_INSTALL_KIND", null);
        }
    }
}
