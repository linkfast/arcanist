using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Users;

/// <summary>
/// User administration and self-service. Users with <see cref="Permissions.ManageUsers"/> who are not
/// administrators can only manage regular users and only grant permissions and libraries they have themselves,
/// so the permission cannot be used to escalate privileges.
/// </summary>
public sealed class UserService(
    ArcanistDbContext db,
    AccessGuard guard,
    IPasswordHasher<User> passwordHasher,
    UserAccessCache accessCache,
    TimeProvider time)
{
    public async Task<UserAccount> MeAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        return await GetAccountAsync(access.UserId, cancellationToken);
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageUsers);

        var users = await db.Users.AsNoTracking().OrderBy(u => u.NormalizedUserName).ToListAsync(cancellationToken);
        var grants = await db.LibraryAccess.AsNoTracking().ToListAsync(cancellationToken);
        var byUser = grants.ToLookup(g => g.UserId);

        return users.Select(u => u.ToAccount(byUser[u.Id])).ToList();
    }

    public async Task<UserAccount> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageUsers);
        return await GetAccountAsync(id, cancellationToken);
    }

    public async Task<UserAccount> CreateAsync(CreateUserInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageUsers);
        guard.RecentAuthentication();

        var errors = new ValidationErrors();
        UserRules.ValidateUserName(input.UserName, "userName", errors);
        UserRules.ValidatePassword(input.Password, "password", errors);
        errors.ThrowIfAny();

        var permissions = PermissionMapping.ToFlags(input.Permissions);
        EnsureCanGrant(actor, input.Role, permissions);
        var (libraryIds, readOnly) = await ValidateGrantsAsync(actor, input.LibraryIds ?? [], input.ReadOnlyLibraryIds, cancellationToken);

        var normalized = TextNormalizer.Normalize(input.UserName);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken))
            throw ArcanistException.Field("userName", ErrorCodes.Duplicate);

        var user = new User
        {
            UserName = input.UserName.Trim(),
            NormalizedUserName = normalized,
            DisplayName = Clean(input.DisplayName),
            Role = input.Role,
            Permissions = input.Role == UserRole.Admin ? Permissions.None : permissions,
            PasswordHash = "",
            CreatedAt = Now(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, input.Password);
        user.LibraryAccess.AddRange(Grants(user.Id, libraryIds, readOnly));

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return user.ToAccount(user.LibraryAccess);
    }

    public async Task<UserAccount> UpdateAsync(Guid id, UpdateUserInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageUsers);
        guard.RecentAuthentication();

        var user = await FindManageableAsync(actor, id, cancellationToken);
        var permissions = PermissionMapping.ToFlags(input.Permissions);
        EnsureCanGrant(actor, input.Role, permissions);

        if (user.Id == actor.UserId && input.IsDisabled)
            throw new ArcanistException.Conflict(ErrorCodes.CannotModifySelf);

        var losesAdmin = user.Role == UserRole.Admin && (input.Role != UserRole.Admin || input.IsDisabled);
        if (losesAdmin && !await HasOtherActiveAdminAsync(user.Id, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.LastAdministrator);

        var privilegesChanged = user.Role != input.Role || user.Permissions != permissions || user.IsDisabled != input.IsDisabled;

        user.DisplayName = Clean(input.DisplayName);
        user.Role = input.Role;
        user.Permissions = input.Role == UserRole.Admin ? Permissions.None : permissions;
        user.IsDisabled = input.IsDisabled;
        if (privilegesChanged)
            user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateUser(user.Id);

        return await GetAccountAsync(user.Id, cancellationToken);
    }

    public async Task<UserAccount> SetLibrariesAsync(Guid id, SetUserLibrariesInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageUsers);
        guard.RecentAuthentication();

        var user = await FindManageableAsync(actor, id, cancellationToken);
        var (libraryIds, readOnly) = await ValidateGrantsAsync(actor, input.LibraryIds, input.ReadOnlyLibraryIds, cancellationToken);

        await db.LibraryAccess.Where(a => a.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        var grants = Grants(user.Id, libraryIds, readOnly);
        db.LibraryAccess.AddRange(grants);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateUser(user.Id);

        return user.ToAccount(grants);
    }

    /// <summary>Sets a new password and signs the user out everywhere.</summary>
    public async Task ResetPasswordAsync(Guid id, ResetUserPasswordInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageUsers);
        guard.RecentAuthentication();

        var errors = new ValidationErrors();
        UserRules.ValidatePassword(input.NewPassword, "newPassword", errors);
        errors.ThrowIfAny();

        var user = await FindManageableAsync(actor, id, cancellationToken);
        user.PasswordHash = passwordHasher.HashPassword(user, input.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(cancellationToken);

        await RevokeSessionsAsync(user.Id, except: null, cancellationToken);
        accessCache.InvalidateUser(user.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageUsers);
        guard.RecentAuthentication();

        if (id == actor.UserId)
            throw new ArcanistException.Conflict(ErrorCodes.CannotModifySelf);

        var user = await FindManageableAsync(actor, id, cancellationToken);
        if (user.Role == UserRole.Admin && !await HasOtherActiveAdminAsync(user.Id, cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.LastAdministrator);

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateUser(user.Id);
    }

    /// <summary>Changes the caller's password and signs out their other devices.</summary>
    public async Task ChangeMyPasswordAsync(ChangeMyPasswordInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var user = await db.Users.SingleAsync(u => u.Id == access.UserId, cancellationToken);

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed)
            throw ArcanistException.Field("currentPassword", ErrorCodes.InvalidCredentials);

        var errors = new ValidationErrors();
        UserRules.ValidatePassword(input.NewPassword, "newPassword", errors);
        errors.ThrowIfAny();

        user.PasswordHash = passwordHasher.HashPassword(user, input.NewPassword);
        await db.SaveChangesAsync(cancellationToken);

        await RevokeSessionsAsync(user.Id, except: guard.SessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> MySessionsAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var current = guard.SessionId;
        var now = Now();

        var sessions = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == access.UserId && s.RevokedAt == null && s.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        return sessions
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new Session(s.Id, s.DeviceName, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.Id == current))
            .ToList();
    }

    public async Task RevokeMySessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var now = Now();

        var revoked = await db.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == access.UserId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

        if (revoked == 0)
            throw new ArcanistException.NotFound();

        accessCache.InvalidateSession(sessionId);
    }

    private async Task<UserAccount> GetAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw new ArcanistException.NotFound();

        var grants = await db.LibraryAccess.Where(a => a.UserId == id).ToListAsync(cancellationToken);
        return user.ToAccount(grants);
    }

    /// <summary>Loads a user the actor is allowed to change. Only administrators can change administrators.</summary>
    private async Task<User> FindManageableAsync(UserAccess actor, Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw new ArcanistException.NotFound();

        if (user.Role == UserRole.Admin && !actor.IsAdmin)
            throw new ArcanistException.Forbidden();

        return user;
    }

    private static void EnsureCanGrant(UserAccess actor, UserRole role, Permissions permissions)
    {
        if (actor.IsAdmin)
            return;

        if (role == UserRole.Admin || (actor.Permissions & permissions) != permissions)
            throw new ArcanistException.Forbidden();
    }

    /// <summary>
    /// The libraries given and those only listened to (given too, or visible to all), checked: a row each, the
    /// read-only ones marked.
    /// </summary>
    private async Task<(IReadOnlyList<Guid> LibraryIds, IReadOnlySet<Guid> ReadOnly)> ValidateGrantsAsync(UserAccess actor,
        IReadOnlyList<Guid> libraryIds, IReadOnlyList<Guid>? readOnlyLibraryIds, CancellationToken cancellationToken)
    {
        var given = await ValidateLibrariesAsync(actor, libraryIds, cancellationToken);
        var readOnly = await ValidateLibrariesAsync(actor, readOnlyLibraryIds ?? [], cancellationToken, "readOnlyLibraryIds");
        return (given.Union(readOnly).Order().ToList(), readOnly.ToHashSet());
    }

    private static List<LibraryAccess> Grants(Guid userId, IReadOnlyList<Guid> libraryIds, IReadOnlySet<Guid> readOnly) =>
        libraryIds.Select(id => new LibraryAccess { UserId = userId, LibraryId = id, ReadOnly = readOnly.Contains(id) }).ToList();

    private async Task<IReadOnlyList<Guid>> ValidateLibrariesAsync(UserAccess actor, IReadOnlyList<Guid> requested, CancellationToken cancellationToken,
        string field = "libraryIds")
    {
        var ids = requested.Distinct().ToList();
        var existing = await db.Libraries.Where(l => ids.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken);

        var errors = new ValidationErrors();
        for (var i = 0; i < requested.Count; i++)
        {
            if (!existing.Contains(requested[i]))
                errors.Add($"{field}[{i}]", ErrorCodes.NotFound);
            else if (!actor.CanAccessLibrary(requested[i]))
                errors.Add($"{field}[{i}]", ErrorCodes.AccessDenied);
        }

        errors.ThrowIfAny();
        return ids.Order().ToList();
    }

    private Task<bool> HasOtherActiveAdminAsync(Guid exceptUserId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id != exceptUserId && u.Role == UserRole.Admin && !u.IsDisabled, cancellationToken);

    private async Task RevokeSessionsAsync(Guid userId, Guid? except, CancellationToken cancellationToken)
    {
        var now = Now();
        var sessions = db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != except);
        var ids = await sessions.Select(s => s.Id).ToListAsync(cancellationToken);
        await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

        foreach (var id in ids)
            accessCache.InvalidateSession(id);
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
