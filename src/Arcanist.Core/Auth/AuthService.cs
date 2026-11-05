using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth.SecondFactor;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Auth;

/// <summary>Tokens issued to a client. <see cref="RefreshToken"/> is only set when it was created or rotated.</summary>
public sealed record IssuedTokens(
    AccessToken AccessToken,
    string? RefreshToken,
    DateTime SessionExpiresAt,
    UserAccount User);

public sealed record ClientInfo(string? IpAddress, string? UserAgent, string? DeviceName);

public abstract record AuthResult
{
    /// <param name="UseCookie">How the sign-in was asked for, when it took more than one request.</param>
    /// <param name="RecoveryCodes">The user's new recovery codes, when pairing their first factor signed them in.</param>
    public sealed record Success(IssuedTokens Tokens, bool? UseCookie = null, IReadOnlyList<string>? RecoveryCodes = null) : AuthResult;

    /// <param name="Code">One of <see cref="ErrorCodes"/>.</param>
    public sealed record Failure(string Code, TimeSpan? RetryAfter = null) : AuthResult;

    /// <summary>The password was right, and more is needed.</summary>
    /// <param name="Code"><c>second_factor_required</c> or <c>second_factor_setup_required</c>.</param>
    /// <param name="Challenge">What the next request sends. Null on a step-up: the session says who it is.</param>
    public sealed record SecondFactorNeeded(string Code, string? Challenge, UserFactors Factors) : AuthResult;

    /// <summary>What a browser's <c>navigator.credentials</c> needs, as JSON.</summary>
    public sealed record PasskeyOptions(string Options) : AuthResult;

    /// <summary>A secret to add to an authenticator app.</summary>
    public sealed record TotpSecret(string Secret, string Uri) : AuthResult;
}

/// <summary>
/// Checks a password somewhere else when the stored hash says so (users imported from an mStream server, whose
/// hashes could not be read). A match is then replaced by Arcanist's own hash.
/// </summary>
public interface IExternalSignIn
{
    Task<bool> VerifyAsync(User user, string password, CancellationToken cancellationToken);
}

public sealed class AuthService(
    ArcanistDbContext db,
    IPasswordHasher<User> passwordHasher,
    IEnumerable<IExternalSignIn> externalSignIns,
    TokenService tokens,
    LoginThrottle throttle,
    UserAccessCache accessCache,
    SecondFactors factors,
    SecondFactorChallenges challenges,
    SecondFactorSettings secondFactorSettings,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    // Verified against when the user does not exist, so both cases take the same time.
    private static readonly User TimingUser = new() { UserName = "", NormalizedUserName = "", PasswordHash = "" };
    private static readonly string TimingHash = new PasswordHasher<User>().HashPassword(TimingUser, Guid.NewGuid().ToString());

    public async Task<AuthResult> LoginAsync(string? userName, string? password, ClientInfo client, bool useCookie, CancellationToken cancellationToken)
    {
        var (found, failure) = await CheckPasswordAsync(userName, password, cancellationToken);
        if (failure is not null)
            return failure;

        var user = found!;
        // Saved now: a rehash changes what the challenge is sealed with.
        await db.SaveChangesAsync(cancellationToken);

        if (user.HasSecondFactor)
            return new AuthResult.SecondFactorNeeded(ErrorCodes.SecondFactorRequired,
                challenges.Create(user, ChallengePurpose.Verify, client.DeviceName, useCookie), await factors.FactorsAsync(user, cancellationToken));
        if (await MustPairAsync(user, cancellationToken))
            return new AuthResult.SecondFactorNeeded(ErrorCodes.SecondFactorSetupRequired,
                challenges.Create(user, ChallengePurpose.Setup, client.DeviceName, useCookie), new UserFactors([], []));

        return await StartSessionAsync(user, client, cancellationToken);
    }

    /// <summary>Finishes a sign-in with a code from the app, or a recovery code.</summary>
    public async Task<AuthResult> LoginWithCodeAsync(string? challenge, string? code, ClientInfo client, CancellationToken cancellationToken)
    {
        var (user, data) = await ReadChallengeAsync(challenge, ChallengePurpose.Verify, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        if (await factors.VerifyCodeAsync(user, code, cancellationToken) is { } failure)
            return failure;
        return await StartSessionAsync(user, ClientOf(client, data!), cancellationToken) with { UseCookie = data!.UseCookie };
    }

    /// <summary>What the browser's passkey has to answer to finish a sign-in.</summary>
    public async Task<AuthResult> LoginPasskeyOptionsAsync(string? challenge, CancellationToken cancellationToken)
    {
        var (user, _) = await ReadChallengeAsync(challenge, ChallengePurpose.Verify, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        return await PasskeyOptionsOrFailureAsync(() => factors.PasskeyOptionsAsync(user, AskedBy(challenge!), cancellationToken));
    }

    public async Task<AuthResult> LoginWithPasskeyAsync(string? challenge, string? assertion, ClientInfo client, CancellationToken cancellationToken)
    {
        var (user, data) = await ReadChallengeAsync(challenge, ChallengePurpose.Verify, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        if (await factors.VerifyPasskeyAsync(user, AskedBy(challenge!), assertion, cancellationToken) is { } failure)
            return failure;
        return await StartSessionAsync(user, ClientOf(client, data!), cancellationToken) with { UseCookie = data!.UseCookie };
    }

    /// <summary>An authenticator app's secret, for a user who has to pair a factor to sign in.</summary>
    public async Task<AuthResult> SetupTotpAsync(string? challenge, CancellationToken cancellationToken)
    {
        var (user, _) = await ReadChallengeAsync(challenge, ChallengePurpose.Setup, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        var (secret, uri) = await factors.StartTotpAsync(user, cancellationToken);
        return new AuthResult.TotpSecret(secret, uri);
    }

    /// <summary>Pairs the app with its first code, and that signs the user in.</summary>
    public async Task<AuthResult> SetupTotpConfirmAsync(string? challenge, string? code, ClientInfo client, CancellationToken cancellationToken)
    {
        var (user, data) = await ReadChallengeAsync(challenge, ChallengePurpose.Setup, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        try
        {
            var codes = await factors.ConfirmTotpAsync(user, code, keepSession: null, cancellationToken);
            return await StartSessionAsync(user, ClientOf(client, data!), cancellationToken) with { UseCookie = data!.UseCookie, RecoveryCodes = codes };
        }
        catch (ArcanistException.Conflict e)
        {
            return new AuthResult.Failure(e.Code);
        }
    }

    public async Task<AuthResult> SetupPasskeyOptionsAsync(string? challenge, CancellationToken cancellationToken)
    {
        var (user, _) = await ReadChallengeAsync(challenge, ChallengePurpose.Setup, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        return await PasskeyOptionsOrFailureAsync(() => factors.PasskeyRegistrationOptionsAsync(user, AskedBy(challenge!), cancellationToken));
    }

    /// <summary>Pairs a passkey, and that signs the user in.</summary>
    public async Task<AuthResult> SetupPasskeyAsync(string? challenge, string? name, string? attestation, ClientInfo client, CancellationToken cancellationToken)
    {
        var (user, data) = await ReadChallengeAsync(challenge, ChallengePurpose.Setup, cancellationToken);
        if (user is null)
            return new AuthResult.Failure(ErrorCodes.SecondFactorExpired);
        try
        {
            var (_, codes) = await factors.RegisterPasskeyAsync(user, AskedBy(challenge!), name, attestation, keepSession: null, cancellationToken);
            return await StartSessionAsync(user, ClientOf(client, data!), cancellationToken) with { UseCookie = data!.UseCookie, RecoveryCodes = codes };
        }
        catch (ArcanistException.Conflict e)
        {
            return new AuthResult.Failure(e.Code);
        }
    }

    /// <summary>What the browser's passkey has to answer to confirm a step-up.</summary>
    public async Task<AuthResult> ReauthenticatePasskeyOptionsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.UserSessions.Include(s => s.User).SingleAsync(s => s.Id == sessionId, cancellationToken);
        return await PasskeyOptionsOrFailureAsync(() => factors.PasskeyOptionsAsync(session.User, SessionAskedBy(sessionId), cancellationToken));
    }

    private static async Task<AuthResult> PasskeyOptionsOrFailureAsync(Func<Task<string>> options)
    {
        try
        {
            return new AuthResult.PasskeyOptions(await options());
        }
        catch (ArcanistException.Conflict e)
        {
            return new AuthResult.Failure(e.Code);
        }
    }

    /// <summary>The user a challenge is for, while it still holds: same password and stamp, enabled, and still in the same case.</summary>
    private async Task<(User? User, ChallengeData? Data)> ReadChallengeAsync(string? challenge, ChallengePurpose purpose, CancellationToken cancellationToken)
    {
        if (challenges.Read(challenge) is not { } data || data.Purpose != purpose)
            return (null, null);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == data.UserId, cancellationToken);
        return user is null || user.IsDisabled || SecondFactorChallenges.Fingerprint(user) != data.Fingerprint
               || user.HasSecondFactor != (purpose == ChallengePurpose.Verify)
            ? (null, null)
            : (user, data);
    }

    private Task<bool> MustPairAsync(User user, CancellationToken cancellationToken) =>
        user.MustSetUpSecondFactor ? Task.FromResult(true) : secondFactorSettings.RequiredAsync(cancellationToken);

    private static ClientInfo ClientOf(ClientInfo client, ChallengeData data) => client with { DeviceName = data.DeviceName };

    /// <summary>What a passkey's question is kept under: the challenge's hash, or the session on a step-up.</summary>
    private static string AskedBy(string challenge) => "challenge:" + TokenService.Hash(challenge);

    public static string SessionAskedBy(Guid sessionId) => "session:" + sessionId;

    /// <summary>
    /// Starts an app's session from what its user approved in the browser. It has their password as recent as the
    /// session that approved it: approving is not typing it, so it never extends a step-up. A change to the password
    /// or the stamp since then, or a disabled account, ends it.
    /// </summary>
    public async Task<AuthResult> SignInApprovedAsync(BrowserApproval approval, ClientInfo client, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == approval.UserId, cancellationToken);
        if (user is null || user.IsDisabled || SecondFactorChallenges.Fingerprint(user) != approval.Fingerprint)
            throw new ArcanistException.Conflict(ErrorCodes.BrowserLoginExpired);
        return await StartSessionAsync(user, client, cancellationToken, approval.AuthenticatedAt);
    }

    private async Task<AuthResult.Success> StartSessionAsync(User user, ClientInfo client, CancellationToken cancellationToken, DateTime? authenticatedAt = null)
    {
        var now = Now();
        var refreshToken = TokenService.CreateRefreshToken();
        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = TokenService.Hash(refreshToken),
            DeviceName = Truncate(client.DeviceName, 512),
            UserAgent = Truncate(client.UserAgent, 1024),
            IpAddress = client.IpAddress,
            CreatedAt = now,
            AuthenticatedAt = authenticatedAt ?? now,
            LastSeenAt = now,
            ExpiresAt = now + options.Value.SessionLifetime,
        };

        user.LastLoginAt = now;
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return await IssueAsync(user, session, refreshToken, cancellationToken);
    }

    /// <summary>
    /// Checks a user's own password without starting a session, for apps that sign in their own way (the mStream
    /// apps, when an administrator allows it). Failures count towards the same lockout as the login.
    /// </summary>
    /// <returns>The user, or the error code: <c>invalid_credentials</c>, <c>too_many_requests</c> or <c>account_disabled</c>.</returns>
    public async Task<(Guid? UserId, string? ErrorCode)> VerifyPasswordAsync(string? userName, string? password, CancellationToken cancellationToken)
    {
        var (user, failure) = await CheckPasswordAsync(userName, password, cancellationToken);
        if (failure is not null)
            return (null, failure.Code);
        await db.SaveChangesAsync(cancellationToken);
        return await PasswordAloneIsEnoughAsync(user!.Id, cancellationToken) ? (user.Id, null) : (null, ErrorCodes.SecondFactorRequired);
    }

    /// <summary>Whether the user's password alone may still sign an app in: not once they have a second factor or need one.</summary>
    public async Task<bool> PasswordAloneIsEnoughAsync(Guid userId, CancellationToken cancellationToken) =>
        !await secondFactorSettings.RequiredAsync(cancellationToken)
        && !await db.Users.AnyAsync(u => u.Id == userId && (u.HasSecondFactor || u.MustSetUpSecondFactor), cancellationToken);

    /// <summary>The user whose password this is (rehashed when needed, not saved yet), or why not.</summary>
    private async Task<(User? User, AuthResult.Failure? Failure)> CheckPasswordAsync(string? userName, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return (null, new AuthResult.Failure(ErrorCodes.InvalidCredentials));

        var normalized = TextNormalizer.Normalize(userName);
        if (throttle.GetLockout(normalized) is { } lockout)
            return (null, new AuthResult.Failure(ErrorCodes.TooManyRequests, lockout));

        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken);
        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(TimingUser, TimingHash, password);
            throttle.RecordFailure(normalized);
            return (null, new AuthResult.Failure(ErrorCodes.InvalidCredentials));
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        foreach (var external in externalSignIns)
        {
            if (verification == PasswordVerificationResult.Failed && await external.VerifyAsync(user, password, cancellationToken))
                verification = PasswordVerificationResult.SuccessRehashNeeded;
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            throttle.RecordFailure(normalized);
            return (null, new AuthResult.Failure(ErrorCodes.InvalidCredentials));
        }

        throttle.RecordSuccess(normalized);

        // Only revealed once the password is right, so it does not help guessing accounts.
        if (user.IsDisabled)
            return (null, new AuthResult.Failure(ErrorCodes.AccountDisabled));

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, password);
        return (user, null);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return new AuthResult.Failure(ErrorCodes.InvalidRefreshToken);

        var now = Now();
        var hash = TokenService.Hash(refreshToken);
        var session = await db.UserSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.RefreshTokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);

        if (session is null)
            return new AuthResult.Failure(ErrorCodes.InvalidRefreshToken);

        if (session.User.IsDisabled)
            return new AuthResult.Failure(ErrorCodes.AccountDisabled);

        // Rotate atomically: if two requests race with the same token, only one wins.
        var next = TokenService.CreateRefreshToken();
        var expiresAt = now + options.Value.SessionLifetime;
        var rotated = await db.UserSessions
            .Where(s => s.Id == session.Id && s.RefreshTokenHash == hash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RefreshTokenHash, TokenService.Hash(next))
                .SetProperty(x => x.LastSeenAt, now)
                .SetProperty(x => x.ExpiresAt, expiresAt), cancellationToken);

        if (rotated == 0)
            return new AuthResult.Failure(ErrorCodes.InvalidRefreshToken);

        session.ExpiresAt = expiresAt;
        return await IssueAsync(session.User, session, next, cancellationToken);
    }

    /// <summary>Revokes the session identified by a refresh token or, failing that, by its id.</summary>
    public async Task LogoutAsync(string? refreshToken, Guid? sessionId, CancellationToken cancellationToken)
    {
        var hash = string.IsNullOrEmpty(refreshToken) ? null : TokenService.Hash(refreshToken);
        if (hash is null && sessionId is null)
            return;

        var now = Now();
        var sessions = db.UserSessions.Where(s => s.RevokedAt == null && (s.RefreshTokenHash == hash || s.Id == sessionId));
        var ids = await sessions.Select(s => s.Id).ToListAsync(cancellationToken);
        await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

        foreach (var id in ids)
            accessCache.InvalidateSession(id);
    }

    /// <summary>
    /// Confirms the password of an already signed-in user, unlocking sensitive actions for a while. Who has a second
    /// factor confirms that too, with <paramref name="code"/> or <paramref name="passkey"/>.
    /// </summary>
    public async Task<AuthResult> ReauthenticateAsync(Guid sessionId, string? password, string? code, string? passkey, CancellationToken cancellationToken)
    {
        var session = await db.UserSessions.Include(s => s.User).SingleAsync(s => s.Id == sessionId, cancellationToken);
        var user = session.User;

        if (throttle.GetLockout(user.NormalizedUserName) is { } lockout)
            return new AuthResult.Failure(ErrorCodes.TooManyRequests, lockout);

        if (string.IsNullOrEmpty(password)
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            throttle.RecordFailure(user.NormalizedUserName);
            return new AuthResult.Failure(ErrorCodes.InvalidCredentials);
        }

        throttle.RecordSuccess(user.NormalizedUserName);

        if (user.HasSecondFactor)
        {
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrEmpty(passkey))
                return new AuthResult.SecondFactorNeeded(ErrorCodes.SecondFactorRequired, null, await factors.FactorsAsync(user, cancellationToken));
            var failure = !string.IsNullOrEmpty(passkey)
                ? await factors.VerifyPasskeyAsync(user, SessionAskedBy(sessionId), passkey, cancellationToken)
                : await factors.VerifyCodeAsync(user, code, cancellationToken);
            if (failure is not null)
                return failure;
        }

        session.AuthenticatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);

        return await IssueAsync(user, session, refreshToken: null, cancellationToken);
    }

    private async Task<AuthResult.Success> IssueAsync(User user, UserSession session, string? refreshToken, CancellationToken cancellationToken)
    {
        var account = user.ToAccount(await db.LibraryAccess.Where(a => a.UserId == user.Id).ToListAsync(cancellationToken));
        return new AuthResult.Success(new IssuedTokens(tokens.CreateAccessToken(user, session), refreshToken, session.ExpiresAt, account));
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
