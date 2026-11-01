using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Application;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Auth.SecondFactor;

/// <summary>The factors a user can finish a sign-in with. <paramref name="PasskeyDomains"/>: where their passkeys work.</summary>
public sealed record UserFactors(IReadOnlyList<string> Methods, IReadOnlyList<string> PasskeyDomains);

/// <summary>
/// A user's second factors: checking them and pairing or removing them. No permission checks here: the sign-in (which
/// passed the password) and <c>SecondFactorService</c> (which has the guards) use it.
/// </summary>
public sealed class SecondFactors(
    ArcanistDbContext db,
    IDataProtectionProvider protection,
    Passkeys passkeys,
    LoginThrottle throttle,
    UserAccessCache accessCache,
    TimeProvider time)
{
    public const string TotpMethod = "TOTP";
    public const string RecoveryCodeMethod = "RECOVERY_CODE";
    public const string PasskeyMethod = "PASSKEY";

    private const int RecoveryCodes = 10;

    /// <summary>Letters and digits that cannot be mistaken for each other.</summary>
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private IDataProtector Protector => protection.CreateProtector("Arcanist.Auth.Totp");

    public async Task<UserFactors> FactorsAsync(User user, CancellationToken cancellationToken)
    {
        var methods = new List<string>();
        if (user.TotpEnabledAt is not null)
            methods.Add(TotpMethod);
        var domains = await db.UserPasskeys.Where(p => p.UserId == user.Id).Select(p => p.RpId).Distinct().ToListAsync(cancellationToken);
        if (domains.Count > 0)
            methods.Add(PasskeyMethod);
        if (await db.UserRecoveryCodes.AnyAsync(c => c.UserId == user.Id && c.UsedAt == null, cancellationToken))
            methods.Add(RecoveryCodeMethod);
        return new UserFactors(methods, domains);
    }

    /// <summary>Checks a code from the app or a recovery code (which is spent). Failures count towards a lockout of their own.</summary>
    /// <returns>Null when it is right, or why not.</returns>
    public async Task<AuthResult.Failure?> VerifyCodeAsync(User user, string? code, CancellationToken cancellationToken)
    {
        var key = ThrottleKey(user);
        if (throttle.GetLockout(key) is { } lockout)
            return new AuthResult.Failure(ErrorCodes.TooManyRequests, lockout);

        if (await MatchesAsync(user, code, cancellationToken))
        {
            throttle.RecordSuccess(key);
            return null;
        }

        throttle.RecordFailure(key);
        return new AuthResult.Failure(ErrorCodes.InvalidSecondFactor);
    }

    /// <summary>Checks a passkey's answer to what <see cref="PasskeyOptionsAsync"/> asked.</summary>
    /// <returns>Null when it is right, or why not.</returns>
    public async Task<AuthResult.Failure?> VerifyPasskeyAsync(User user, string askedBy, string? assertion, CancellationToken cancellationToken)
    {
        var key = ThrottleKey(user);
        if (throttle.GetLockout(key) is { } lockout)
            return new AuthResult.Failure(ErrorCodes.TooManyRequests, lockout);

        var mine = await db.UserPasskeys.Where(p => p.UserId == user.Id).ToListAsync(cancellationToken);
        UserPasskey? used;
        try
        {
            used = await passkeys.VerifyAssertionAsync(user, mine, askedBy, assertion, cancellationToken);
        }
        catch (ArcanistException.Conflict e)
        {
            return new AuthResult.Failure(e.Code);
        }

        if (used is null)
        {
            throttle.RecordFailure(key);
            return new AuthResult.Failure(ErrorCodes.PasskeyFailed);
        }

        throttle.RecordSuccess(key);
        used.LastUsedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string> PasskeyOptionsAsync(User user, string askedBy, CancellationToken cancellationToken) =>
        await passkeys.AssertionOptionsAsync(await db.UserPasskeys.Where(p => p.UserId == user.Id).ToListAsync(cancellationToken), askedBy, cancellationToken);

    /// <summary>A new secret for the app, waiting for its first code to be enabled. A user with an app already removes it first.</summary>
    public async Task<(string Secret, string Uri)> StartTotpAsync(User user, CancellationToken cancellationToken)
    {
        if (user.TotpEnabledAt is not null)
            throw new ArcanistException.Conflict(ErrorCodes.TotpAlreadyEnabled);

        var secret = Totp.NewSecret();
        user.TotpSecret = Protector.Protect(Convert.ToBase64String(secret));
        await db.SaveChangesAsync(cancellationToken);
        var base32 = Totp.Base32(secret);
        return (base32, Totp.Uri(base32, user.UserName));
    }

    /// <summary>Enables the app with its first code.</summary>
    /// <returns>The recovery codes if this is the user's first factor, shown only now; otherwise none.</returns>
    public async Task<IReadOnlyList<string>> ConfirmTotpAsync(User user, string? code, Guid? keepSession, CancellationToken cancellationToken)
    {
        if (user.TotpEnabledAt is not null)
            throw new ArcanistException.Conflict(ErrorCodes.TotpAlreadyEnabled);
        if (user.TotpSecret is null)
            throw new ArcanistException.Conflict(ErrorCodes.SecondFactorExpired);

        var key = ThrottleKey(user);
        if (throttle.GetLockout(key) is not null)
            throw new ArcanistException.Conflict(ErrorCodes.TooManyRequests);

        var now = time.GetUtcNow();
        if (string.IsNullOrWhiteSpace(code) || Secret(user) is not { } secret || Totp.Match(secret, Clean(code), now, null) is not { } step)
        {
            throttle.RecordFailure(key);
            throw new ArcanistException.Conflict(ErrorCodes.InvalidSecondFactor);
        }

        throttle.RecordSuccess(key);
        user.TotpEnabledAt = now.UtcDateTime;
        user.TotpLastStep = step;
        return await FactorAddedAsync(user, keepSession, cancellationToken);
    }

    public async Task<string> PasskeyRegistrationOptionsAsync(User user, string askedBy, CancellationToken cancellationToken) =>
        await passkeys.RegistrationOptionsAsync(user, await db.UserPasskeys.Where(p => p.UserId == user.Id).ToListAsync(cancellationToken), askedBy, cancellationToken);

    /// <returns>The passkey, and the recovery codes if this is the user's first factor, shown only now.</returns>
    public async Task<(UserPasskey Passkey, IReadOnlyList<string> RecoveryCodes)> RegisterPasskeyAsync(
        User user, string askedBy, string? name, string? attestation, Guid? keepSession, CancellationToken cancellationToken)
    {
        var created = await passkeys.FinishRegistrationAsync(askedBy, attestation, cancellationToken);
        if (await db.UserPasskeys.AnyAsync(p => p.CredentialId == created.CredentialId, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.PasskeyFailed);

        var passkey = new UserPasskey
        {
            UserId = user.Id,
            CredentialId = created.CredentialId,
            PublicKey = created.PublicKey,
            SignCount = created.SignCount,
            AaGuid = created.AaGuid,
            Transports = created.Transports,
            RpId = created.RpId,
            Name = CleanName(name),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.UserPasskeys.Add(passkey);
        return (passkey, await FactorAddedAsync(user, keepSession, cancellationToken));
    }

    /// <summary>A name for a passkey: what the user wrote, or a plain one.</summary>
    public static string CleanName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length > 100)
            throw ArcanistException.Field("name", ErrorCodes.InvalidFormat);
        return trimmed.Length == 0 ? "Passkey" : trimmed;
    }

    /// <summary>New recovery codes, the old ones gone. Shown only now.</summary>
    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(User user, CancellationToken cancellationToken)
    {
        await db.UserRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        // 3 groups of 4: about 59 bits, because only their hash stays here.
        var codes = Enumerable.Range(0, RecoveryCodes)
            .Select(_ => string.Join('-', Enumerable.Range(0, 3).Select(_ => RandomNumberGenerator.GetString(Alphabet, 4)))).ToList();
        db.UserRecoveryCodes.AddRange(codes.Select(code => new UserRecoveryCode { UserId = user.Id, CodeHash = Hash(code) }));
        await db.SaveChangesAsync(cancellationToken);
        return codes;
    }

    /// <summary>After removing a factor: keeps the user's state right, and with no factors left their recovery codes go.</summary>
    public async Task FactorRemovedAsync(User user, Guid? keepSession, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        user.HasSecondFactor = user.TotpEnabledAt is not null || await db.UserPasskeys.AnyAsync(p => p.UserId == user.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (!user.HasSecondFactor)
            await db.UserRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await RevokeSessionsAsync(user.Id, keepSession, cancellationToken);
    }

    /// <summary>Takes every factor of a user away. They pair one again at their next sign-in, signed out everywhere until then.</summary>
    public async Task ResetAsync(User user, CancellationToken cancellationToken)
    {
        await db.UserPasskeys.Where(p => p.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await db.UserRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        user.TotpSecret = null;
        user.TotpEnabledAt = null;
        user.TotpLastStep = null;
        user.HasSecondFactor = false;
        user.MustSetUpSecondFactor = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(cancellationToken);
        await RevokeSessionsAsync(user.Id, null, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> FactorAddedAsync(User user, Guid? keepSession, CancellationToken cancellationToken)
    {
        var first = !user.HasSecondFactor;
        user.HasSecondFactor = true;
        user.MustSetUpSecondFactor = false;
        await db.SaveChangesAsync(cancellationToken);
        await RevokeSessionsAsync(user.Id, keepSession, cancellationToken);
        return first ? await NewRecoveryCodesAsync(user, cancellationToken) : [];
    }

    private async Task<bool> MatchesAsync(User user, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        var clean = Clean(code);

        if (user.TotpEnabledAt is not null && Secret(user) is { } secret && Totp.Match(secret, clean, time.GetUtcNow(), user.TotpLastStep) is { } step)
        {
            // Two requests racing with the same code: only one moves the step on.
            var moved = await db.Users.Where(u => u.Id == user.Id && (u.TotpLastStep == null || u.TotpLastStep < step))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpLastStep, step), cancellationToken);
            return moved == 1;
        }

        var hash = Hash(FormatRecovery(clean));
        var now = time.GetUtcNow().UtcDateTime;
        return await db.UserRecoveryCodes.Where(c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), cancellationToken) == 1;
    }

    private async Task RevokeSessionsAsync(Guid userId, Guid? except, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var sessions = db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != except);
        var ids = await sessions.Select(s => s.Id).ToListAsync(cancellationToken);
        await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        foreach (var id in ids)
            accessCache.InvalidateSession(id);
        accessCache.InvalidateUser(userId);
    }

    private byte[]? Secret(User user)
    {
        try
        {
            return user.TotpSecret is null ? null : Convert.FromBase64String(Protector.Unprotect(user.TotpSecret));
        }
        catch (CryptographicException)
        {
            // Sealed with keys that are gone (a data directory restored without its keys).
            return null;
        }
    }

    private static string ThrottleKey(User user) => "2fa:" + user.NormalizedUserName;

    /// <summary>Codes as people type them: spaces and dashes do not count.</summary>
    private static string Clean(string code) => new(code.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string FormatRecovery(string clean) =>
        clean.Length == 12 ? $"{clean[..4]}-{clean[4..8]}-{clean[8..]}" : clean;

    private static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
