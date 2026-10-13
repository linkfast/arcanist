using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using AppPasswordEntity = Arcanist.Core.Data.Entities.AppPassword;

namespace Arcanist.Core.Application.Users;

/// <summary>An app password, without its secret. Part of the API contract.</summary>
public sealed record AppPassword(Guid Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>A new app password and its secret, shown only now.</summary>
/// <param name="Secret">Enter it in the app as the password (or as the API key of OpenSubsonic apps).</param>
public sealed record CreatedAppPassword(AppPassword AppPassword, string Secret);

/// <summary>
/// App passwords: what users give Subsonic and mStream apps instead of their own password. Each user manages theirs;
/// creating one needs a recent authentication.
/// </summary>
public sealed class AppPasswordService(
    ArcanistDbContext db,
    AccessGuard guard,
    IDataProtectionProvider protection,
    LoginThrottle throttle,
    TimeProvider time)
{
    public const int MaxPerUser = 50;

    /// <summary>Letters and digits that cannot be mistaken for each other when typed on a phone.</summary>
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private IDataProtector Protector => protection.CreateProtector("Arcanist.AppPasswords");

    public async Task<IReadOnlyList<AppPassword>> ListAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var passwords = await db.AppPasswords.AsNoTracking().Where(p => p.UserId == access.UserId)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        return passwords.Select(ToModel).ToList();
    }

    public async Task<CreatedAppPassword> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        guard.RecentAuthentication();
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is 0 or > 100)
            throw ArcanistException.Field("name", trimmed.Length == 0 ? ErrorCodes.Required : ErrorCodes.InvalidFormat);
        if (await db.AppPasswords.CountAsync(p => p.UserId == access.UserId, cancellationToken) >= MaxPerUser)
            throw ArcanistException.Field("name", ErrorCodes.TooMany);

        // 4 groups of 4: about 79 bits, and easy to type.
        var secret = string.Join('-', Enumerable.Range(0, 4).Select(_ => RandomNumberGenerator.GetString(Alphabet, 4)));
        var password = new AppPasswordEntity
        {
            UserId = access.UserId,
            Name = trimmed,
            EncryptedSecret = Protector.Protect(secret),
            SecretHash = Hash(secret),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.AppPasswords.Add(password);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedAppPassword(ToModel(password), secret);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var deleted = await db.AppPasswords.Where(p => p.Id == id && p.UserId == access.UserId).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
            throw new ArcanistException.NotFound();
    }

    /// <summary>The user whose app password this is (sent as a password: Subsonic <c>p</c>, mStream login).</summary>
    public async Task<Guid?> VerifyPasswordAsync(string userName, string password, CancellationToken cancellationToken) =>
        (await VerifyAsync(userName, secret => FixedTimeEquals(secret, password), cancellationToken))?.UserId;

    /// <summary>
    /// The app password a user name and password match, for apps that get a token of their own at sign-in (mStream):
    /// the token names the app password, so revoking it signs the app out.
    /// </summary>
    public async Task<(Guid UserId, Guid PasswordId)?> SignInAsync(string userName, string password, CancellationToken cancellationToken) =>
        await VerifyAsync(userName, secret => FixedTimeEquals(secret, password), cancellationToken) is { } match ? (match.UserId, match.Id) : null;

    /// <summary>The user of an app password, while it exists.</summary>
    public async Task<Guid?> UserOfAsync(Guid passwordId, CancellationToken cancellationToken)
    {
        var password = await db.AppPasswords.SingleOrDefaultAsync(p => p.Id == passwordId, cancellationToken);
        if (password is null)
            return null;
        await TouchAsync(password, cancellationToken);
        return password.UserId;
    }

    /// <summary>The user whose app password gives this Subsonic token: <c>md5(password + salt)</c>, hex.</summary>
    public async Task<Guid?> VerifyTokenAsync(string userName, string token, string salt, CancellationToken cancellationToken) =>
        (await VerifyAsync(userName, secret => FixedTimeEquals(Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(secret + salt))), token.ToLowerInvariant()),
            cancellationToken))?.UserId;

    /// <summary>The user of an app password sent as an OpenSubsonic API key.</summary>
    public async Task<Guid?> VerifyApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var hash = Hash(apiKey);
        var password = await db.AppPasswords.SingleOrDefaultAsync(p => p.SecretHash == hash, cancellationToken);
        if (password is null)
            return null;
        await TouchAsync(password, cancellationToken);
        return password.UserId;
    }

    private async Task<AppPasswordEntity?> VerifyAsync(string userName, Func<string, bool> matches, CancellationToken cancellationToken)
    {
        var normalized = TextNormalizer.Normalize(userName ?? "");
        var key = "app:" + normalized;
        if (throttle.GetLockout(key) is not null)
            return null;

        var passwords = await db.AppPasswords.Where(p => p.User.NormalizedUserName == normalized).ToListAsync(cancellationToken);
        foreach (var password in passwords)
        {
            string secret;
            try
            {
                secret = Protector.Unprotect(password.EncryptedSecret);
            }
            catch (CryptographicException)
            {
                // Encrypted with keys that are gone (a data directory restored without its keys).
                continue;
            }

            if (!matches(secret))
                continue;

            throttle.RecordSuccess(key);
            await TouchAsync(password, cancellationToken);
            return password;
        }

        throttle.RecordFailure(key);
        return null;
    }

    /// <summary>Records the use, at most once a minute so busy clients do not write on every request.</summary>
    private async Task TouchAsync(AppPasswordEntity password, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (password.LastUsedAt is { } last && now - last < TimeSpan.FromMinutes(1))
            return;
        await db.AppPasswords.Where(p => p.Id == password.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastUsedAt, now), cancellationToken);
    }

    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static AppPassword ToModel(AppPasswordEntity password) => new(password.Id, password.Name, password.CreatedAt, password.LastUsedAt);
}
