using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Auth;

/// <summary>What a user may do, as of the last database read. The single source for authorization decisions.</summary>
public sealed record UserAccess(
    Guid UserId,
    string UserName,
    string? DisplayName,
    UserRole Role,
    Permissions Permissions,
    string SecurityStamp,
    bool IsDisabled,
    IReadOnlySet<Guid> LibraryIds)
{
    public bool IsAdmin => Role == UserRole.Admin;

    public bool Has(Permissions permission) => IsAdmin || (Permissions & permission) == permission;

    public bool CanAccessLibrary(Guid libraryId) => IsAdmin || LibraryIds.Contains(libraryId);
}

/// <summary>
/// Caches <see cref="UserAccess"/> and session validity briefly, so authorization does not hit the database on
/// every request while revocations and permission changes still apply within seconds — immediately when the
/// change goes through <see cref="InvalidateUser"/>, <see cref="InvalidateSession"/> or <see cref="InvalidateAll"/>.
/// </summary>
public sealed class UserAccessCache(
    IMemoryCache cache,
    IArcanistDbContextFactory databases,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    private long _generation;

    public async Task<UserAccess?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(UserKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = options.Value.AccessCacheDuration;
            await using var db = databases.CreateDbContext();

            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
                return null;

            var libraryIds = await db.Libraries
                .Where(l => l.VisibleToAll || l.Access.Any(a => a.UserId == userId))
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);

            return new UserAccess(user.Id, user.UserName, user.DisplayName, user.Role, user.Permissions,
                user.SecurityStamp, user.IsDisabled, libraryIds.ToHashSet());
        });

    public async Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(SessionKey(sessionId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = options.Value.AccessCacheDuration;
            await using var db = databases.CreateDbContext();
            var now = time.GetUtcNow().UtcDateTime;
            return await db.UserSessions.AnyAsync(
                s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);
        });

    public void InvalidateUser(Guid userId) => cache.Remove(UserKey(userId));

    public void InvalidateSession(Guid sessionId) => cache.Remove(SessionKey(sessionId));

    /// <summary>For changes that affect many users at once, such as a library's visibility.</summary>
    public void InvalidateAll() => Interlocked.Increment(ref _generation);

    private (string, Guid, long) UserKey(Guid userId) => ("user-access", userId, Interlocked.Read(ref _generation));

    private (string, Guid, long) SessionKey(Guid sessionId) => ("session-active", sessionId, Interlocked.Read(ref _generation));
}

/// <summary>The authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Throws when the request is anonymous; endpoints that use it must require authorization.</summary>
    UserAccess Access { get; }

    Guid SessionId { get; }

    /// <summary>When the password was last entered on this session (login or re-authentication).</summary>
    DateTimeOffset? AuthenticatedAt { get; }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.Items[typeof(UserAccess)] is UserAccess;

    public UserAccess Access => accessor.HttpContext?.Items[typeof(UserAccess)] as UserAccess
                                ?? throw new InvalidOperationException("The request is not authenticated.");

    public Guid SessionId => Guid.Parse(accessor.HttpContext!.User.FindFirst(ArcanistClaims.SessionId)!.Value);

    public DateTimeOffset? AuthenticatedAt =>
        long.TryParse(accessor.HttpContext?.User.FindFirst(ArcanistClaims.AuthenticatedAt)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
