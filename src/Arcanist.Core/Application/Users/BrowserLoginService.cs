using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arcanist.Core.Application.Sharing;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Users;

/// <param name="AuthorizeUrl">The page an app opens in the browser. Absolute with the server's public URL; otherwise relative to the address the app uses.</param>
/// <param name="DeviceUrl">Where someone approves a device's code.</param>
public sealed record BrowserLoginInfo(string AuthorizeUrl, string DeviceUrl);

/// <param name="ClientName">The app's name, as it says it.</param>
/// <param name="RedirectUri">Where the code goes back: <c>http://127.0.0.1:&lt;port&gt;/…</c>, <c>http://[::1]:&lt;port&gt;/…</c> or an app's own scheme the server accepts.</param>
/// <param name="CodeChallenge">The unpadded base64url SHA-256 of the verifier the app keeps (PKCE, S256).</param>
/// <param name="State">Sent back as it is.</param>
/// <param name="DeviceName">What the session is called in My account; the app's name if missing.</param>
public sealed record BrowserLoginRequest(string? ClientName, string? RedirectUri, string? CodeChallenge, string? State = null, string? DeviceName = null);

public enum BrowserLoginTarget
{
    /// <summary>An app on the device this browser runs on.</summary>
    Loopback,

    /// <summary>The app that registered that scheme on this device.</summary>
    App,
}

/// <param name="Scheme">The scheme, for an app.</param>
/// <param name="DenyUrl">Where to go if the user says no.</param>
public sealed record BrowserLoginCheck(string ClientName, string? DeviceName, BrowserLoginTarget Target, string? Scheme, string DenyUrl);

public sealed record BrowserLoginApproval(string RedirectUrl);

/// <param name="Code">The <c>code</c> the redirect brought.</param>
/// <param name="CodeVerifier">What the challenge is the hash of: 43 to 128 unreserved characters.</param>
public sealed record BrowserLoginTokenRequest(string? Code, string? CodeVerifier, string? RedirectUri);

public sealed record DeviceLoginStartRequest(string? ClientName, string? DeviceName = null);

/// <param name="DeviceCode">What the app keeps, and asks with. Never shown.</param>
/// <param name="UserCode">What the person types, like <c>BCDF-GHJK</c>.</param>
/// <param name="VerificationUrl">Where they type it.</param>
/// <param name="VerificationUrlComplete">The same page with the code, for a QR code or a link.</param>
/// <param name="IntervalSeconds">How long to wait between questions.</param>
public sealed record DeviceLoginStart(string DeviceCode, string UserCode, string VerificationUrl, string VerificationUrlComplete, int ExpiresInSeconds, int IntervalSeconds);

public sealed record DeviceLoginTokenRequest(string? DeviceCode);

public sealed record DeviceLoginCodeRequest(string? UserCode);

/// <param name="IpAddress">Where the device asked from, so the person can tell it is theirs.</param>
public sealed record DeviceLoginDetails(string UserCode, string ClientName, string? DeviceName, string? IpAddress, DateTime CreatedAt, DateTime ExpiresAt);

/// <summary>
/// Signing an app in through the browser, where passkeys work and where a user can pair a factor: the login is the
/// panel's, and the app gets a code it trades for the usual tokens. Two ways: the browser goes back to the app (PKCE)
/// or the person types a code the device shows.
/// </summary>
public sealed partial class BrowserLoginService(
    AccessGuard guard,
    ICurrentUser currentUser,
    ArcanistDbContext db,
    AuthService auth,
    BrowserLoginStore store,
    LoginThrottle throttle,
    SharingSettings sharing,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    private const int MaxNameLength = 100;
    private const int MaxStateLength = 1024;

    public async Task<BrowserLoginInfo> InfoAsync(CancellationToken cancellationToken)
    {
        var root = await sharing.PublicUrlAsync(cancellationToken) ?? "";
        return new BrowserLoginInfo(root + "/admin/authorize", root + "/admin/device");
    }

    /// <summary>Whether a link to the authorize page can be honoured, before anyone signs in on it.</summary>
    public BrowserLoginCheck Check(BrowserLoginRequest request)
    {
        var clientName = CleanName(request.ClientName, "clientName") ?? throw ArcanistException.Field("clientName", ErrorCodes.InvalidFormat);
        var deviceName = CleanName(request.DeviceName, "deviceName");
        if (!BrowserRedirects.TryParse(request.RedirectUri, options.Value.BrowserLoginSchemes, out var loopback, out var scheme))
            throw ArcanistException.Field("redirectUri", ErrorCodes.InvalidFormat);
        if (request.CodeChallenge is null || !ChallengeFormat().IsMatch(request.CodeChallenge))
            throw ArcanistException.Field("codeChallenge", ErrorCodes.InvalidFormat);
        if (request.State?.Length > MaxStateLength)
            throw ArcanistException.Field("state", ErrorCodes.InvalidFormat);

        return new BrowserLoginCheck(clientName, deviceName, loopback ? BrowserLoginTarget.Loopback : BrowserLoginTarget.App, scheme,
            Back(request.RedirectUri!, ("error", "access_denied"), ("state", request.State)));
    }

    /// <summary>The user said yes, in the browser: where to send it, with a code for the app.</summary>
    public async Task<BrowserLoginApproval> ApproveAsync(BrowserLoginRequest request, CancellationToken cancellationToken)
    {
        var check = Check(request);
        var approval = await ApprovalAsync(cancellationToken);
        var code = store.AddGrant(approval, request.CodeChallenge!, request.RedirectUri!, check.DeviceName ?? check.ClientName);
        return new BrowserLoginApproval(Back(request.RedirectUri!, ("code", code), ("state", request.State)));
    }

    /// <summary>The app trades its code and its verifier for tokens. A wrong one spends the code all the same.</summary>
    public async Task<AuthResult> RedeemAsync(BrowserLoginTokenRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        var grant = request.Code is null ? null : store.TakeGrant(request.Code);
        if (grant is null || grant.RedirectUri != request.RedirectUri || !VerifierMatches(request.CodeVerifier, grant.CodeChallenge))
            throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginExpired);
        return await auth.SignInApprovedAsync(grant.Approval, client with { DeviceName = grant.DeviceName }, cancellationToken);
    }

    public async Task<DeviceLoginStart> StartDeviceAsync(DeviceLoginStartRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var clientName = CleanName(request.ClientName, "clientName") ?? throw ArcanistException.Field("clientName", ErrorCodes.InvalidFormat);
        var deviceName = CleanName(request.DeviceName, "deviceName");
        if (store.AddDevice(clientName, deviceName, ipAddress) is not { } added)
            throw new ArcanistException.TooSoon(ErrorCodes.TooManyRequests, (int)BrowserLoginStore.PollInterval.TotalSeconds);

        var url = (await InfoAsync(cancellationToken)).DeviceUrl;
        var userCode = Display(added.Login.UserCode);
        return new DeviceLoginStart(added.DeviceCode, userCode, url, QueryHelpers.AddQueryString(url, "code", userCode),
            (int)BrowserLoginStore.DeviceLifetime.TotalSeconds, (int)BrowserLoginStore.PollInterval.TotalSeconds);
    }

    /// <summary>The device asks if someone approved its code yet. Tokens once, when they did.</summary>
    public async Task<AuthResult> PollDeviceAsync(DeviceLoginTokenRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.DeviceCode))
            throw ArcanistException.Field("deviceCode", ErrorCodes.Required);

        var (status, login) = store.Poll(request.DeviceCode);
        return status switch
        {
            DeviceLoginStatus.Approved => await auth.SignInApprovedAsync(login!.Approval!,
                client with { DeviceName = login.DeviceName ?? login.ClientName }, cancellationToken),
            DeviceLoginStatus.Pending => throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginPending),
            DeviceLoginStatus.Denied => throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginDenied),
            DeviceLoginStatus.SlowDown => throw new ArcanistException.TooSoon(ErrorCodes.BrowserLoginSlowDown, (int)BrowserLoginStore.PollInterval.TotalSeconds),
            _ => throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginExpired),
        };
    }

    /// <summary>What the person is about to let in, from the code they typed.</summary>
    public DeviceLoginDetails LookUpDevice(DeviceLoginCodeRequest request)
    {
        var login = FindDevice(request.UserCode);
        return new DeviceLoginDetails(Display(login.UserCode), login.ClientName, login.DeviceName, login.IpAddress, login.CreatedAt, login.ExpiresAt);
    }

    /// <summary>A code from a screen is what phishing asks for, so this takes the password again, unlike the browser's own way back.</summary>
    public async Task ApproveDeviceAsync(DeviceLoginCodeRequest request, CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var login = FindDevice(request.UserCode);
        if (!store.Answer(login, await ApprovalAsync(cancellationToken)))
            throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginExpired);
    }

    public void DenyDevice(DeviceLoginCodeRequest request)
    {
        if (!store.Answer(FindDevice(request.UserCode), null))
            throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginExpired);
    }

    /// <summary>The waiting device for a code. Wrong codes count against the user: there are few letters to guess.</summary>
    private DeviceLogin FindDevice(string? userCode)
    {
        var access = guard.Authenticated();
        var key = "device:" + access.UserId;
        if (throttle.GetLockout(key) is { } lockout)
            throw new ArcanistException.TooSoon(ErrorCodes.TooManyRequests, (int)Math.Ceiling(lockout.TotalSeconds));
        if (store.FindDevice(userCode) is { } login)
        {
            throttle.RecordSuccess(key);
            return login;
        }

        throttle.RecordFailure(key);
        throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginExpired);
    }

    private async Task<BrowserApproval> ApprovalAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == access.UserId, cancellationToken);
        var authenticatedAt = currentUser.AuthenticatedAt ?? DateTimeOffset.MinValue;
        return new BrowserApproval(user.Id, Auth.SecondFactor.SecondFactorChallenges.Fingerprint(user),
            authenticatedAt < time.GetUtcNow() ? authenticatedAt.UtcDateTime : time.GetUtcNow().UtcDateTime);
    }

    private static string Back(string redirectUri, params (string Name, string? Value)[] query) =>
        query.Where(q => q.Value is not null).Aggregate(redirectUri, (url, q) => QueryHelpers.AddQueryString(url, q.Name, q.Value!));

    private static bool VerifierMatches(string? verifier, string challenge)
    {
        if (verifier is null || !VerifierFormat().IsMatch(verifier))
            return false;
        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static string Display(string userCode) => userCode[..4] + "-" + userCode[4..];

    /// <summary>A name the app says it has: it reaches a page that asks for the user's trust, so nothing that could disguise it.</summary>
    private static string? CleanName(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = value.Trim();
        if (name.Length > MaxNameLength || name.Any(char.IsControl))
            throw ArcanistException.Field(field, ErrorCodes.InvalidFormat);
        return name;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex ChallengeFormat();

    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$")]
    private static partial Regex VerifierFormat();
}
