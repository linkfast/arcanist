using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>The linked account as stored: who it is, and its refresh token protected with Data Protection.</summary>
/// <param name="LinkedByUserId">The administrator who linked it: the account's devices are theirs on this server.</param>
public sealed record StoredCloudLink(string Subject, string? UserName, string? Email, DateTime LinkedAt, string ProtectedRefreshToken, Guid? LinkedByUserId = null);

/// <summary>A device authorization in progress: what the administrator types (or opens) on Ultimate Cloud's site.</summary>
public sealed record CloudLinkRequest(string UserCode, string VerificationUri, string? VerificationUriComplete, DateTime ExpiresAt);

/// <summary>
/// Signs this server in to Ultimate Cloud with the OAuth 2.0 device authorization grant (RFC 8628): Arcanist shows a
/// code, the administrator approves it on Ultimate Cloud's site, and Arcanist never sees the password. The refresh
/// token (offline, long lived) is kept protected in the settings; access tokens only in memory.
/// </summary>
public sealed class UltimateCloudAuth(
    IHttpClientFactory clients,
    IOptionsMonitor<UltimateCloudOptions> options,
    SettingsStore settings,
    IDataProtectionProvider protection,
    TimeProvider time,
    ILogger<UltimateCloudAuth> logger)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Arcanist.UltimateCloud.RefreshToken");
    private readonly SemaphoreSlim _tokens = new(1, 1);
    private JsonNode? _discovery;
    private string? _discoveredFor;
    private string? _accessToken;
    private DateTimeOffset _accessExpires;
    private StoredCloudLink? _link;
    private bool _linkLoaded;

    private HttpClient Http => clients.CreateClient(UltimateCloudClient.HttpClientName);

    public async Task<StoredCloudLink?> LinkAsync(CancellationToken cancellationToken)
    {
        if (!_linkLoaded)
        {
            _link = await settings.GetAsync<StoredCloudLink?>(SettingKeys.UltimateCloudLink, null, cancellationToken);
            _linkLoaded = true;
        }

        return _link;
    }

    /// <summary>The id this server uses as its device in Ultimate Cloud, created once.</summary>
    public async Task<string> DeviceIdAsync(CancellationToken cancellationToken)
    {
        var id = await settings.GetAsync<string?>(SettingKeys.UltimateCloudDeviceId, null, cancellationToken);
        if (id is null)
        {
            id = "arcanist-" + Guid.NewGuid().ToString("N");
            await settings.SetAsync(SettingKeys.UltimateCloudDeviceId, id, cancellationToken);
        }

        return id;
    }

    /// <summary>Asks for a device code; <see cref="WaitForApprovalAsync"/> then waits for the administrator.</summary>
    public async Task<(CloudLinkRequest Request, string DeviceCode, TimeSpan Interval)> StartAsync(CancellationToken cancellationToken)
    {
        var endpoint = (await DiscoveryAsync(cancellationToken))["device_authorization_endpoint"]?.GetValue<string>()
                       ?? throw new UltimateCloudException(ErrorCodes.CloudLinkUnsupported);
        using var response = await PostFormAsync(endpoint, new()
        {
            ["client_id"] = options.CurrentValue.ClientId,
            ["scope"] = "openid offline_access",
        }, cancellationToken);
        var body = await ReadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ultimate Cloud refused a device code: {Error}", body?["error"]);
            throw new UltimateCloudException(ErrorCodes.CloudLinkUnsupported);
        }

        var request = new CloudLinkRequest(
            body!["user_code"]!.GetValue<string>(),
            body["verification_uri"]!.GetValue<string>(),
            body["verification_uri_complete"]?.GetValue<string>(),
            time.GetUtcNow().UtcDateTime.AddSeconds(body["expires_in"]?.GetValue<int>() ?? 600));
        return (request, body["device_code"]!.GetValue<string>(), TimeSpan.FromSeconds(body["interval"]?.GetValue<int>() ?? 5));
    }

    /// <summary>
    /// Polls until the administrator approves (the account is stored and returned), refuses or the code expires
    /// (<see cref="UltimateCloudException"/> with <c>cloud_link_denied</c> or <c>cloud_link_expired</c>).
    /// </summary>
    public async Task<StoredCloudLink> WaitForApprovalAsync(string deviceCode, TimeSpan interval, DateTime expiresAt, CancellationToken cancellationToken)
    {
        var endpoint = await TokenEndpointAsync(cancellationToken);
        while (true)
        {
            await Task.Delay(interval, time, cancellationToken);
            if (time.GetUtcNow().UtcDateTime >= expiresAt)
                throw new UltimateCloudException(ErrorCodes.CloudLinkExpired);

            using var response = await PostFormAsync(endpoint, new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode,
                ["client_id"] = options.CurrentValue.ClientId,
            }, cancellationToken);
            var body = await ReadAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode)
                return await StoreAsync(body!, cancellationToken);

            switch (body?["error"]?.GetValue<string>())
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "access_denied":
                    throw new UltimateCloudException(ErrorCodes.CloudLinkDenied);
                default:
                    throw new UltimateCloudException(ErrorCodes.CloudLinkExpired);
            }
        }
    }

    /// <summary>A valid access token, refreshed (and the refresh token rotated) when it is about to expire.</summary>
    /// <exception cref="UltimateCloudException"><c>cloud_not_linked</c>, <c>cloud_link_expired</c> (the account must be linked again) or <c>cloud_unreachable</c>.</exception>
    public async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokens.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && time.GetUtcNow() < _accessExpires - TimeSpan.FromSeconds(30))
                return _accessToken;

            var link = await LinkAsync(cancellationToken) ?? throw new UltimateCloudException(ErrorCodes.CloudNotLinked);
            string refreshToken;
            try
            {
                refreshToken = _protector.Unprotect(link.ProtectedRefreshToken);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new UltimateCloudException(ErrorCodes.CloudLinkExpired);
            }

            HttpResponseMessage response;
            try
            {
                response = await PostFormAsync(await TokenEndpointAsync(cancellationToken), new()
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = options.CurrentValue.ClientId,
                }, cancellationToken);
            }
            catch (HttpRequestException e)
            {
                throw new UltimateCloudException(ErrorCodes.CloudUnreachable, e.Message);
            }

            using (response)
            {
                var body = await ReadAsync(response, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // invalid_grant: the session was revoked or expired on Ultimate Cloud's side.
                    throw new UltimateCloudException(body?["error"]?.GetValue<string>() == "invalid_grant"
                        ? ErrorCodes.CloudLinkExpired
                        : ErrorCodes.CloudUnreachable);
                }

                await StoreAsync(body!, cancellationToken, link);
                return _accessToken!;
            }
        }
        finally
        {
            _tokens.Release();
        }
    }

    /// <summary>Records who linked the account.</summary>
    public async Task SetLinkedByAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await LinkAsync(cancellationToken) is not { } link || link.LinkedByUserId == userId)
            return;
        _link = link with { LinkedByUserId = userId };
        await settings.SetAsync(SettingKeys.UltimateCloudLink, _link, cancellationToken);
    }

    /// <summary>Forgets the account, and tells Ultimate Cloud to end the session (best effort).</summary>
    public async Task UnlinkAsync(CancellationToken cancellationToken)
    {
        await _tokens.WaitAsync(cancellationToken);
        try
        {
            var link = await LinkAsync(cancellationToken);
            if (link is not null)
            {
                try
                {
                    if ((await DiscoveryAsync(cancellationToken))["revocation_endpoint"]?.GetValue<string>() is { } revoke)
                    {
                        using var _ = await PostFormAsync(revoke, new()
                        {
                            ["token"] = _protector.Unprotect(link.ProtectedRefreshToken),
                            ["token_type_hint"] = "refresh_token",
                            ["client_id"] = options.CurrentValue.ClientId,
                        }, cancellationToken);
                    }
                }
                catch (Exception e) when (e is HttpRequestException or UltimateCloudException or System.Security.Cryptography.CryptographicException)
                {
                    logger.LogWarning("The Ultimate Cloud session could not be revoked: {Error}", e.Message);
                }
            }

            await settings.SetAsync<StoredCloudLink?>(SettingKeys.UltimateCloudLink, null, cancellationToken);
            (_link, _linkLoaded, _accessToken) = (null, true, null);
        }
        finally
        {
            _tokens.Release();
        }
    }

    /// <summary>Keeps the tokens of a token response; the account's identity comes from the access token.</summary>
    private async Task<StoredCloudLink> StoreAsync(JsonNode body, CancellationToken cancellationToken, StoredCloudLink? current = null)
    {
        _accessToken = body["access_token"]!.GetValue<string>();
        _accessExpires = time.GetUtcNow().AddSeconds(body["expires_in"]?.GetValue<int>() ?? 300);
        var refresh = body["refresh_token"]?.GetValue<string>();
        var claims = Claims(_accessToken);
        var link = new StoredCloudLink(
            claims?["sub"]?.GetValue<string>() ?? current?.Subject ?? "",
            claims?["preferred_username"]?.GetValue<string>() ?? current?.UserName,
            claims?["email"]?.GetValue<string>() ?? current?.Email,
            current?.LinkedAt ?? time.GetUtcNow().UtcDateTime,
            refresh is null ? current?.ProtectedRefreshToken ?? throw new UltimateCloudException(ErrorCodes.CloudLinkUnsupported) : _protector.Protect(refresh),
            current?.LinkedByUserId);
        if (link != current)
        {
            await settings.SetAsync(SettingKeys.UltimateCloudLink, link, cancellationToken);
            (_link, _linkLoaded) = (link, true);
        }

        return link;
    }

    /// <summary>The payload of a JWT, unverified: the token came straight from the issuer over TLS.</summary>
    private static JsonNode? Claims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    private async Task<string> TokenEndpointAsync(CancellationToken cancellationToken) =>
        (await DiscoveryAsync(cancellationToken))["token_endpoint"]?.GetValue<string>() ?? throw new UltimateCloudException(ErrorCodes.CloudLinkUnsupported);

    private async Task<JsonNode> DiscoveryAsync(CancellationToken cancellationToken)
    {
        var authority = options.CurrentValue.Authority.TrimEnd('/');
        if (_discovery is not null && _discoveredFor == authority)
            return _discovery;
        try
        {
            _discovery = await Http.GetFromJsonAsync<JsonNode>($"{authority}/.well-known/openid-configuration", cancellationToken)
                         ?? throw new UltimateCloudException(ErrorCodes.CloudUnreachable);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            throw new UltimateCloudException(ErrorCodes.CloudUnreachable, e.Message);
        }

        _discoveredFor = authority;
        return _discovery;
    }

    private Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken) =>
        Http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);

    private static async Task<JsonNode?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
