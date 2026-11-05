using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Arcanist.Core.Auth;

/// <summary>Who approved a sign-in in the browser. The session it starts counts their password from when they typed it, never from now.</summary>
public sealed record BrowserApproval(Guid UserId, string Fingerprint, DateTime AuthenticatedAt);

/// <summary>A code the browser hands back to the app: once, to whoever has the verifier.</summary>
public sealed record BrowserGrant(BrowserApproval Approval, string CodeChallenge, string RedirectUri, string? DeviceName, DateTime ExpiresAt);

public enum DeviceLoginStatus
{
    Pending,
    Approved,
    Denied,
    Expired,
    SlowDown,
}

/// <summary>A device waiting for someone to approve its code from a browser.</summary>
public sealed class DeviceLogin(string deviceHash, string userCode, string clientName, string? deviceName, string? ipAddress, DateTime createdAt, DateTime expiresAt)
{
    public string DeviceHash { get; } = deviceHash;
    public string UserCode { get; } = userCode;
    public string ClientName { get; } = clientName;
    public string? DeviceName { get; } = deviceName;
    public string? IpAddress { get; } = ipAddress;
    public DateTime CreatedAt { get; } = createdAt;
    public DateTime ExpiresAt { get; } = expiresAt;

    public bool Denied { get; set; }
    public BrowserApproval? Approval { get; set; }
    public DateTime? LastPoll { get; set; }
}

/// <summary>
/// What a browser sign-in is waiting for, in memory: all of it lasts minutes, and a restart only makes the app ask
/// again. Only the hashes of the codes the app holds are kept.
/// </summary>
public sealed class BrowserLoginStore(TimeProvider time)
{
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DeviceLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Asking for a code needs no account: enough for any server, and no more.</summary>
    private const int MaxPending = 1000;

    // No vowels: a code never spells a word. Nothing that reads like something else (0/O, 1/I).
    private const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";

    private readonly ConcurrentDictionary<string, BrowserGrant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DeviceLogin> _devices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DeviceLogin> _userCodes = new(StringComparer.Ordinal);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <returns>The code for the app.</returns>
    public string AddGrant(BrowserApproval approval, string codeChallenge, string redirectUri, string? deviceName)
    {
        Purge();
        var code = TokenService.CreateRefreshToken();
        _grants[TokenService.Hash(code)] = new BrowserGrant(approval, codeChallenge, redirectUri, deviceName, Now + GrantLifetime);
        return code;
    }

    /// <summary>Takes a code: whatever comes of it, it does not work again.</summary>
    public BrowserGrant? TakeGrant(string code) =>
        _grants.TryRemove(TokenService.Hash(code), out var grant) && grant.ExpiresAt > Now ? grant : null;

    /// <returns>Null when too many are waiting.</returns>
    public (DeviceLogin Login, string DeviceCode)? AddDevice(string clientName, string? deviceName, string? ipAddress)
    {
        Purge();
        if (_devices.Count >= MaxPending)
            return null;

        var deviceCode = TokenService.CreateRefreshToken();
        while (true)
        {
            var userCode = RandomNumberGenerator.GetString(UserCodeAlphabet, 8);
            var login = new DeviceLogin(TokenService.Hash(deviceCode), userCode, clientName, deviceName, ipAddress, Now, Now + DeviceLifetime);
            if (!_userCodes.TryAdd(userCode, login))
                continue;
            _devices[login.DeviceHash] = login;
            return (login, deviceCode);
        }
    }

    /// <summary>The device a code someone typed is for, while it waits. Dashes, spaces and case do not matter.</summary>
    public DeviceLogin? FindDevice(string? userCode)
    {
        if (userCode is null)
            return null;
        var code = string.Concat(userCode.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        if (!_userCodes.TryGetValue(code, out var login))
            return null;
        lock (login)
            return login.ExpiresAt > Now && login.Approval is null && !login.Denied ? login : null;
    }

    /// <returns>Whether it was still waiting: a code answers only once.</returns>
    public bool Answer(DeviceLogin login, BrowserApproval? approval)
    {
        lock (login)
        {
            if (login.ExpiresAt <= Now || login.Approval is not null || login.Denied)
                return false;
            login.Approval = approval;
            login.Denied = approval is null;
            return true;
        }
    }

    /// <summary>What a device asking for its result gets. An approval or a refusal is given once.</summary>
    public (DeviceLoginStatus Status, DeviceLogin? Login) Poll(string deviceCode)
    {
        if (!_devices.TryGetValue(TokenService.Hash(deviceCode), out var login))
            return (DeviceLoginStatus.Expired, null);

        lock (login)
        {
            if (login.ExpiresAt <= Now)
            {
                Forget(login);
                return (DeviceLoginStatus.Expired, null);
            }

            if (login.Approval is not null || login.Denied)
            {
                Forget(login);
                return (login.Denied ? DeviceLoginStatus.Denied : DeviceLoginStatus.Approved, login);
            }

            // A second of grace: a client's timer is never exact.
            if (login.LastPoll is { } last && Now - last < PollInterval - TimeSpan.FromSeconds(1))
                return (DeviceLoginStatus.SlowDown, null);
            login.LastPoll = Now;
            return (DeviceLoginStatus.Pending, null);
        }
    }

    private void Forget(DeviceLogin login)
    {
        _devices.TryRemove(login.DeviceHash, out _);
        _userCodes.TryRemove(login.UserCode, out _);
    }

    private void Purge()
    {
        var now = Now;
        foreach (var (hash, grant) in _grants)
        {
            if (grant.ExpiresAt <= now)
                _grants.TryRemove(hash, out _);
        }

        foreach (var login in _devices.Values)
        {
            if (login.ExpiresAt <= now)
                Forget(login);
        }
    }
}

/// <summary>Where a browser may send a code back: this device's loopback, or an app's own scheme from a fixed list.</summary>
public static partial class BrowserRedirects
{
    /// <summary>The apps' schemes every server accepts. <see cref="AuthOptions.BrowserLoginSchemes"/> adds others.</summary>
    public static readonly IReadOnlyList<string> BuiltInSchemes = ["ultimate"];

    // A setting can add an app's scheme, never one a page or the system would open as something else.
    private static readonly HashSet<string> NeverSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "ws", "wss", "ftp", "file", "data", "blob", "about", "javascript", "vbscript", "filesystem", "view-source",
    };

    public static bool IsAllowedScheme(string scheme, IEnumerable<string> extra) =>
        !NeverSchemes.Contains(scheme) && SchemeFormat().IsMatch(scheme)
        && BuiltInSchemes.Concat(extra).Contains(scheme, StringComparer.OrdinalIgnoreCase);

    /// <returns>Whether it may be used, and if it goes to this device's loopback.</returns>
    public static bool TryParse(string? value, IEnumerable<string> extra, out bool loopback, out string? scheme)
    {
        loopback = false;
        scheme = null;
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0 || value.Contains('#'))
            return false;

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            // By address: RFC 8252, a name could resolve anywhere.
            loopback = uri.Host is "127.0.0.1" or "[::1]";
            return loopback;
        }

        scheme = uri.Scheme;
        return IsAllowedScheme(scheme, extra);
    }

    [GeneratedRegex("^[a-z][a-z0-9+.-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeFormat();
}
