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
    IReadOnlySet<Guid> LibraryIds,
    IReadOnlySet<Guid>? OthersPersonalLibraryIds = null,
    IReadOnlySet<Guid>? ReadOnlyLibraryIds = null)
{
    public bool IsAdmin => Role == UserRole.Admin;

    public bool Has(Permissions permission) => IsAdmin || (Permissions & permission) == permission;

    /// <summary>Administrators reach every library but other users' personal ones (their own Ultimate Cloud).</summary>
    public bool CanAccessLibrary(Guid libraryId) =>
        IsAdmin ? OthersPersonalLibraryIds?.Contains(libraryId) != true : LibraryIds.Contains(libraryId);

    /// <summary>
    /// May add music to the library (uploads, also through extensions): the UPLOAD permission, access to it, and
    /// not being only a listener there. Administrators always may.
    /// </summary>
    public bool CanUploadTo(Guid libraryId) =>
        !IsDisabled && Has(Permissions.Upload) && CanAccessLibrary(libraryId) && (IsAdmin || ReadOnlyLibraryIds?.Contains(libraryId) != true);

    /// <summary>
    /// May change what is in the library (edit tags, delete tracks, with their own permissions): access to it and not
    /// being only a listener there. Administrators always may.
    /// </summary>
    public bool CanChange(Guid libraryId) => !IsDisabled && CanAccessLibrary(libraryId) && !IsReadOnlyIn(libraryId);

    /// <summary>Listens to the library but adds nothing to it.</summary>
    public bool IsReadOnlyIn(Guid libraryId) => !IsAdmin && ReadOnlyLibraryIds?.Contains(libraryId) == true;
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

            var othersPersonal = user.Role == UserRole.Admin
                ? (await db.Libraries.Where(l => l.OwnerUserId != null && l.OwnerUserId != userId).Select(l => l.Id).ToListAsync(cancellationToken)).ToHashSet()
                : null;
            var readOnly = await db.LibraryAccess.Where(a => a.UserId == userId && a.ReadOnly).Select(a => a.LibraryId).ToListAsync(cancellationToken);
            return new UserAccess(user.Id, user.UserName, user.DisplayName, user.Role, user.Permissions,
                user.SecurityStamp, user.IsDisabled, libraryIds.ToHashSet(), othersPersonal, readOnly.ToHashSet());
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
    private static readonly AsyncLocal<Acting?> ActingUser = new();

    private sealed record Acting(UserAccess Access, DateTimeOffset AuthenticatedAt);

    /// <summary>
    /// Background work an administrator approved (with a recent authentication) runs as them, until disposed: the
    /// services it calls check that user, as if they asked now. Only for work started by that user's own request.
    /// </summary>
    public static IDisposable ActAs(UserAccess access, DateTimeOffset authenticatedAt)
    {
        var previous = ActingUser.Value;
        ActingUser.Value = new Acting(access, authenticatedAt);
        return new Restore(previous);
    }

    private sealed class Restore(Acting? previous) : IDisposable
    {
        public void Dispose() => ActingUser.Value = previous;
    }

    public bool IsAuthenticated => ActingUser.Value is not null || accessor.HttpContext?.Items[typeof(UserAccess)] is UserAccess;

    public UserAccess Access => ActingUser.Value?.Access
                                ?? accessor.HttpContext?.Items[typeof(UserAccess)] as UserAccess
                                ?? throw new InvalidOperationException("The request is not authenticated.");

    /// <summary>The request's session; none (empty, as the extensions that sign in without one) for background work acting as a user.</summary>
    public Guid SessionId => ActingUser.Value is null && accessor.HttpContext is { } context
        ? Guid.Parse(context.User.FindFirst(ArcanistClaims.SessionId)!.Value)
        : Guid.Empty;

    public DateTimeOffset? AuthenticatedAt =>
        ActingUser.Value is { } acting ? acting.AuthenticatedAt
        : long.TryParse(accessor.HttpContext?.User.FindFirst(ArcanistClaims.AuthenticatedAt)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
