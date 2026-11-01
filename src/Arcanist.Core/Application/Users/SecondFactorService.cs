using Arcanist.Core.Auth;
using Arcanist.Core.Auth.SecondFactor;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Users;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A passkey or security key of the user.</summary>
/// <param name="RpId">The domain it works on: passkeys belong to one.</param>
public sealed record Passkey(Guid Id, string Name, string RpId, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>The user's second factors.</summary>
/// <param name="Enabled">They sign in with one.</param>
/// <param name="RecoveryCodesLeft">One-time codes for when the others are not at hand.</param>
/// <param name="Required">The server requires a second factor of everyone.</param>
public sealed record MySecondFactor(bool Enabled, bool TotpEnabled, DateTime? TotpEnabledAt, IReadOnlyList<Passkey> Passkeys, int RecoveryCodesLeft, bool Required);

/// <param name="Secret">For typing it into the authenticator app.</param>
/// <param name="Uri">What its QR code holds.</param>
public sealed record TotpSetup(string Secret, string Uri);

/// <param name="Codes">Shown only now. Empty when the user had a factor already, and so has their codes.</param>
public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

/// <param name="Options">What <c>navigator.credentials.create</c> needs, as JSON.</param>
public sealed record PasskeyRegistration(string Options);

/// <param name="RecoveryCodes">Shown only now. Empty when the user had a factor already, and so has their codes.</param>
public sealed record RegisteredPasskey(Passkey Passkey, IReadOnlyList<string> RecoveryCodes);

/// <summary>Whether the server requires a second factor of everyone.</summary>
/// <param name="SetByEnvironment">It comes from <c>Arcanist__Auth__RequireSecondFactor</c>: the panel cannot change it.</param>
/// <param name="UsersWithout">Active users without one: they pair one at their next sign-in if it is required.</param>
public sealed record SecondFactorPolicy(bool Required, bool SetByEnvironment, int UsersWithout);

/// <summary>
/// What users do with their own second factors, and what administrators do about them. Adding or removing one needs
/// a recent authentication and signs the user's other devices out.
/// </summary>
public sealed class SecondFactorService(
    ArcanistDbContext db,
    AccessGuard guard,
    SecondFactors factors,
    SecondFactorSettings settings,
    ILogger<SecondFactorService> logger)
{
    public async Task<MySecondFactor> MineAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentAsync(cancellationToken);
        var passkeys = await db.UserPasskeys.AsNoTracking().Where(p => p.UserId == user.Id).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        var codes = await db.UserRecoveryCodes.CountAsync(c => c.UserId == user.Id && c.UsedAt == null, cancellationToken);
        return new MySecondFactor(user.HasSecondFactor, user.TotpEnabledAt is not null, user.TotpEnabledAt, passkeys.Select(ToModel).ToList(), codes,
            await settings.RequiredAsync(cancellationToken));
    }

    public async Task<TotpSetup> StartTotpAsync(CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var (secret, uri) = await factors.StartTotpAsync(await CurrentAsync(cancellationToken), cancellationToken);
        return new TotpSetup(secret, uri);
    }

    public async Task<RecoveryCodes> ConfirmTotpAsync(string? code, CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        return new RecoveryCodes(await factors.ConfirmTotpAsync(await CurrentAsync(cancellationToken), code, guard.SessionId, cancellationToken));
    }

    public async Task DisableTotpAsync(CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var user = await CurrentAsync(cancellationToken);
        if (user.TotpEnabledAt is null && user.TotpSecret is null)
            return;
        if (user.TotpEnabledAt is not null)
            await EnsureNotLastAsync(user, await db.UserPasskeys.CountAsync(p => p.UserId == user.Id, cancellationToken), cancellationToken);

        user.TotpSecret = null;
        user.TotpEnabledAt = null;
        user.TotpLastStep = null;
        await factors.FactorRemovedAsync(user, guard.SessionId, cancellationToken);
    }

    public async Task<PasskeyRegistration> PasskeyRegistrationOptionsAsync(CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var user = await CurrentAsync(cancellationToken);
        return new PasskeyRegistration(await factors.PasskeyRegistrationOptionsAsync(user, AuthService.SessionAskedBy(guard.SessionId), cancellationToken));
    }

    public async Task<RegisteredPasskey> RegisterPasskeyAsync(string? name, string? attestation, CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var user = await CurrentAsync(cancellationToken);
        var (passkey, codes) = await factors.RegisterPasskeyAsync(user, AuthService.SessionAskedBy(guard.SessionId), name, attestation, guard.SessionId, cancellationToken);
        return new RegisteredPasskey(ToModel(passkey), codes);
    }

    public async Task<Passkey> RenamePasskeyAsync(Guid id, string? name, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var passkey = await db.UserPasskeys.SingleOrDefaultAsync(p => p.Id == id && p.UserId == access.UserId, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        if (string.IsNullOrWhiteSpace(name))
            throw ArcanistException.Field("name", ErrorCodes.Required);
        passkey.Name = SecondFactors.CleanName(name);
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(passkey);
    }

    public async Task RemovePasskeyAsync(Guid id, CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var user = await CurrentAsync(cancellationToken);
        var passkey = await db.UserPasskeys.SingleOrDefaultAsync(p => p.Id == id && p.UserId == user.Id, cancellationToken)
                      ?? throw new ArcanistException.NotFound();
        var others = await db.UserPasskeys.CountAsync(p => p.UserId == user.Id && p.Id != id, cancellationToken);
        await EnsureNotLastAsync(user, others + (user.TotpEnabledAt is null ? 0 : 1), cancellationToken);

        db.UserPasskeys.Remove(passkey);
        await factors.FactorRemovedAsync(user, guard.SessionId, cancellationToken);
    }

    /// <summary>New recovery codes, shown only now; the old ones stop working.</summary>
    public async Task<RecoveryCodes> RegenerateRecoveryCodesAsync(CancellationToken cancellationToken)
    {
        guard.RecentAuthentication();
        var user = await CurrentAsync(cancellationToken);
        if (!user.HasSecondFactor)
            throw new ArcanistException.Conflict(ErrorCodes.InvalidSecondFactor);
        return new RecoveryCodes(await factors.NewRecoveryCodesAsync(user, cancellationToken));
    }

    /// <summary>
    /// For who lost their factors: takes them all away, signs them out everywhere, and they pair one again at their next
    /// sign-in. Only administrators do this to administrators.
    /// </summary>
    public async Task ResetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var actor = guard.Permission(Permissions.ManageUsers);
        guard.RecentAuthentication();
        if (userId == actor.UserId)
            throw new ArcanistException.Conflict(ErrorCodes.CannotModifySelf);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (user.Role == UserRole.Admin && !actor.IsAdmin)
            throw new ArcanistException.Forbidden();

        await factors.ResetAsync(user, cancellationToken);
        logger.LogWarning("{Actor} reset the second factor of {User}", actor.UserName, user.UserName);
    }

    public async Task<SecondFactorPolicy> PolicyAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return await PolicyModelAsync(cancellationToken);
    }

    /// <summary>Required, whoever has no factor pairs one at their next sign-in; sessions already open stay.</summary>
    public async Task<SecondFactorPolicy> SetPolicyAsync(bool required, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        if (settings.Environment is not null)
            throw new ArcanistException.Conflict(ErrorCodes.SetByEnvironment);
        await settings.SetAsync(required, cancellationToken);
        return await PolicyModelAsync(cancellationToken);
    }

    private async Task<SecondFactorPolicy> PolicyModelAsync(CancellationToken cancellationToken) => new(
        await settings.RequiredAsync(cancellationToken),
        settings.Environment is not null,
        await db.Users.CountAsync(u => !u.HasSecondFactor && !u.IsDisabled, cancellationToken));

    private async Task EnsureNotLastAsync(User user, int factorsLeft, CancellationToken cancellationToken)
    {
        if (factorsLeft == 0 && await settings.RequiredAsync(cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.SecondFactorLast);
    }

    private async Task<User> CurrentAsync(CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        return await db.Users.SingleAsync(u => u.Id == access.UserId, cancellationToken);
    }

    private static Passkey ToModel(UserPasskey passkey) => new(passkey.Id, passkey.Name, passkey.RpId, passkey.CreatedAt, passkey.LastUsedAt);
}
