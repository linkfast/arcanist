using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Users;
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
    public sealed record Success(IssuedTokens Tokens) : AuthResult;

    /// <param name="Code">One of <see cref="ErrorCodes"/>.</param>
    public sealed record Failure(string Code, TimeSpan? RetryAfter = null) : AuthResult;
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
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    // Verified against when the user does not exist, so both cases take the same time.
    private static readonly User TimingUser = new() { UserName = "", NormalizedUserName = "", PasswordHash = "" };
    private static readonly string TimingHash = new PasswordHasher<User>().HashPassword(TimingUser, Guid.NewGuid().ToString());

    public async Task<AuthResult> LoginAsync(string? userName, string? password, ClientInfo client, CancellationToken cancellationToken)
    {
        var (found, failure) = await CheckPasswordAsync(userName, password, cancellationToken);
        if (failure is not null)
            return failure;

        var user = found!;
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
            AuthenticatedAt = now,
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
        return (user!.Id, null);
    }

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

    /// <summary>Confirms the password of an already signed-in user, unlocking sensitive actions for a while.</summary>
    public async Task<AuthResult> ReauthenticateAsync(Guid sessionId, string? password, CancellationToken cancellationToken)
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
        session.AuthenticatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);

        return await IssueAsync(user, session, refreshToken: null, cancellationToken);
    }

    private async Task<AuthResult> IssueAsync(User user, UserSession session, string? refreshToken, CancellationToken cancellationToken)
    {
        var account = user.ToAccount(await db.LibraryAccess.Where(a => a.UserId == user.Id).ToListAsync(cancellationToken));
        return new AuthResult.Success(new IssuedTokens(tokens.CreateAccessToken(user, session), refreshToken, session.ExpiresAt, account));
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
