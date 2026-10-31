using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Application;

namespace Arcanist.Core.Cloud;

/// <summary>Bound from <c>Arcanist:UltimateCloud</c>.</summary>
public sealed class UltimateCloudOptions
{
    public const string Section = "Arcanist:UltimateCloud";

    /// <summary>Ultimate Cloud's GraphQL endpoint. Linking is unavailable while it is not set.</summary>
    public string? ApiUrl { get; set; }

    /// <summary>The OpenID Connect issuer accounts sign in with (Keycloak realm).</summary>
    public string Authority { get; set; } = "https://auth.linkfast.io/realms/ultimate-dev";

    /// <summary>The public client with the device authorization grant enabled.</summary>
    public string ClientId { get; set; } = "arcanist";

    /// <summary>How often the account's plan is checked.</summary>
    public TimeSpan PlanInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long the last known plan holds while Ultimate Cloud cannot be reached.</summary>
    public TimeSpan OfflineGrace { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Where the change notices are subscribed to (WebSocket); by default the API's URL.</summary>
    public string? EventsUrl { get; set; }
}

/// <summary>A GraphQL error from Ultimate Cloud, with its code (<c>extensions.code</c>).</summary>
public sealed class UltimateCloudException(string code, string? message = null) : Exception(message ?? code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Talks to Ultimate Cloud's GraphQL API as this server's device: every request carries the account's access token
/// and identifies the server (<c>X-Device-Id</c>, <c>X-Client-App: arcanist-server</c>), as Ultimate Cloud requires
/// from every client.
/// </summary>
public sealed class UltimateCloudClient(IHttpClientFactory clients, UltimateCloudAuth auth, Microsoft.Extensions.Options.IOptionsMonitor<UltimateCloudOptions> options)
{
    public const string HttpClientName = "ultimate-cloud";

    /// <summary>The HTTP client for Ultimate Cloud's files (B2 uploads and downloads), without a timeout.</summary>
    public const string FilesClientName = "ultimate-cloud-files";
    public const string ClientApp = "arcanist-server";

    private static readonly AsyncLocal<Guid?> Account = new();

    /// <summary>The user whose own account the calls in this flow use; the server's account when null.</summary>
    public static Guid? CurrentUser => Account.Value;

    /// <summary>Makes the calls of this flow (until disposed) use a user's own account, or the server's with null.</summary>
    public static IDisposable As(Guid? user)
    {
        var previous = Account.Value;
        Account.Value = user;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Account.Value = previous;
    }

    /// <summary>Runs a query or mutation and returns its <c>data</c>.</summary>
    /// <exception cref="UltimateCloudException">The API answered with errors, or could not be reached (<c>cloud_unreachable</c>).</exception>
    public Task<JsonNode> SendAsync(string query, object? variables, CancellationToken cancellationToken) =>
        SendAsync(query, variables, signedIn: true, cancellationToken);

    /// <summary>Runs an operation that needs no account (anonymous statistics), as this server's device.</summary>
    public Task<JsonNode> SendAnonymousAsync(string query, object? variables, CancellationToken cancellationToken) =>
        SendAsync(query, variables, signedIn: false, cancellationToken);

    /// <summary>
    /// Runs an operation as another of this server's devices (its audio output, an Ultimate Connect target), signed
    /// in with the account of this flow.
    /// </summary>
    public Task<JsonNode> SendAsDeviceAsync(string deviceId, string clientApp, string query, object? variables, CancellationToken cancellationToken) =>
        SendAsync(query, variables, signedIn: true, cancellationToken, (deviceId, clientApp));

    private async Task<JsonNode> SendAsync(
        string query, object? variables, bool signedIn, CancellationToken cancellationToken, (string Id, string ClientApp)? device = null)
    {
        var url = options.CurrentValue.ApiUrl ?? throw new UltimateCloudException(ErrorCodes.CloudNotConfigured);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            // Buffered, with a Content-Length: some proxies refuse chunked request bodies.
            Content = new StringContent(JsonSerializer.Serialize(new { query, variables }, Json), System.Text.Encoding.UTF8, "application/json"),
        };
        if (signedIn)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.AccessTokenAsync(cancellationToken, Account.Value));
        request.Headers.Add("X-Device-Id", device?.Id ?? await auth.DeviceIdAsync(cancellationToken));
        request.Headers.Add("X-Client-App", device?.ClientApp ?? ClientApp);

        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new UltimateCloudException(ErrorCodes.CloudUnreachable, e.Message);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UltimateCloudException(ErrorCodes.CloudLinkExpired);
            JsonNode? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            }
            catch (JsonException)
            {
                throw new UltimateCloudException(ErrorCodes.CloudUnreachable, $"HTTP {(int)response.StatusCode}");
            }

            if (body?["errors"] is JsonArray { Count: > 0 } errors)
            {
                var first = errors[0]!;
                throw new UltimateCloudException(first["extensions"]?["code"]?.GetValue<string>() ?? "cloud_error", first["message"]?.GetValue<string>());
            }

            return body?["data"] ?? throw new UltimateCloudException(ErrorCodes.CloudUnreachable, $"HTTP {(int)response.StatusCode}");
        }
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
