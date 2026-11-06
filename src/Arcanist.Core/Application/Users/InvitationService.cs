using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Application.Sharing;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Users;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum InvitationState
{
    /// <summary>Not used yet: it still works until it expires.</summary>
    Pending,

    /// <summary>Someone asked for an account with it.</summary>
    Used,

    Expired,
    Revoked,
}

/// <summary>An invitation to the server, as its maker (or an administrator) sees it. The code is only shown on making it.</summary>
/// <param name="Note">Who it is for, as its maker wrote it.</param>
/// <param name="CreatedBy">Who made it (user name).</param>
/// <param name="UsedBy">The account asked for with it, while it exists (user name).</param>
public sealed record Invitation(Guid Id, string? Note, DateTime CreatedAt, DateTime ExpiresAt, InvitationState State, string CreatedBy, string? UsedBy);

/// <summary>A new invitation: its code and link are shown this once.</summary>
/// <param name="Code">Short code (<c>ABCD-EFGH-JKLM</c>) to type in an app next to the server's address.</param>
/// <param name="Url">The link to share and to show as a QR code: <c>{serverUrl}/admin/join#{code}</c>. Ultimate apps
/// take it whole; opened in a browser, it explains how to add the server and, if the server allows it, asks for an account.</param>
/// <param name="ServerUrl">The server's public URL, where apps reach it.</param>
/// <param name="SignupAllowed">Whoever has it can ask for an account now (the server's setting).</param>
public sealed record NewInvitation(Guid Id, string Code, string Url, string ServerUrl, DateTime ExpiresAt, bool SignupAllowed);

/// <param name="Note">Who it is for, only for its maker to recognize it (optional).</param>
public sealed record CreateInvitationInput(string? Note);

/// <summary>What an invitation leads to, for whoever has it (no account needed).</summary>
/// <param name="ServerUrl">The server's public URL: add it to the app with it.</param>
/// <param name="InvitedBy">The name of who invited.</param>
/// <param name="SignupAllowed">An account can be asked for with it; it waits for an administrator's approval.</param>
public sealed record InvitationPreview(string ServerUrl, string InvitedBy, DateTime ExpiresAt, bool SignupAllowed);

public sealed record InvitationCodeInput(string Code);

public sealed record InvitationSignupInput(string Code, string UserName, string Password, string? DisplayName);

/// <param name="PendingApproval">The account waits disabled until an administrator approves it.</param>
public sealed record InvitationSignup(string UserName, bool PendingApproval);

/// <summary>
/// Invitations to the server (permission INVITE, with the server's public URL set): a link and a code that add the server
/// to Ultimate apps and, when the server allows it (sharing.invitationSignup, off by default), let someone ask for an
/// account. That account is made disabled and waits for an administrator (<see cref="UserService.ApproveAsync"/>).
/// Single use, a week long; only the code's hash is kept.
/// </summary>
public sealed class InvitationService(
    ArcanistDbContext db,
    AccessGuard guard,
    SharingSettings sharing,
    IPasswordHasher<User> passwordHasher,
    LoginThrottle throttle,
    IHttpContextAccessor http,
    TimeProvider time,
    ILogger<InvitationService> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>The panel's page that an invitation link opens.</summary>
    public const string JoinPath = "/admin/join";

    // No 0/O or 1/I: it can be read aloud or typed.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<NewInvitation> CreateAsync(CreateInvitationInput? input, CancellationToken cancellationToken)
    {
        var access = guard.Permission(Permissions.Invite);
        var serverUrl = await sharing.PublicUrlAsync(cancellationToken) ?? throw new ArcanistException.Conflict(ErrorCodes.PublicUrlRequired);

        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, 12);
        var code = string.Join('-', Enumerable.Range(0, 3).Select(i => new string(chars, i * 4, 4)));
        var now = Now();
        var note = input?.Note?.Trim();
        var invitation = new ServerInvitation
        {
            CodeHash = Hash(code),
            CreatedByUserId = access.UserId,
            Note = string.IsNullOrEmpty(note) ? null : note.Length > 200 ? note[..200] : note,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        db.ServerInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        var signup = (await sharing.GetAsync(cancellationToken)).InvitationSignup;
        return new NewInvitation(invitation.Id, code, LinkOf(serverUrl, code), serverUrl, invitation.ExpiresAt, signup);
    }

    /// <summary>The user's invitations, newest first; with <paramref name="all"/>, everyone's (MANAGE_USERS).</summary>
    public async Task<IReadOnlyList<Invitation>> ListAsync(bool all, CancellationToken cancellationToken)
    {
        var access = all ? guard.Permission(Permissions.ManageUsers) : guard.Permission(Permissions.Invite);
        var query = db.ServerInvitations.AsNoTracking();
        if (!all)
            query = query.Where(i => i.CreatedByUserId == access.UserId);
        var invitations = await query.OrderByDescending(i => i.CreatedAt).Take(200)
            .Select(i => new { Invitation = i, CreatedBy = i.CreatedBy.UserName })
            .ToListAsync(cancellationToken);
        var usedIds = invitations.Select(i => i.Invitation.UsedByUserId).OfType<Guid>().ToList();
        var used = await db.Users.AsNoTracking().Where(u => usedIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);
        var now = Now();
        return invitations.Select(i => new Invitation(i.Invitation.Id, i.Invitation.Note, i.Invitation.CreatedAt, i.Invitation.ExpiresAt,
                StateOf(i.Invitation, now), i.CreatedBy, i.Invitation.UsedByUserId is { } id ? used.GetValueOrDefault(id) : null))
            .ToList();
    }

    /// <summary>Stops an invitation from working: its maker, or whoever manages users.</summary>
    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        var invitation = await db.ServerInvitations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new ArcanistException.NotFound();
        if (invitation.CreatedByUserId != access.UserId && !access.Has(Permissions.ManageUsers))
            throw new ArcanistException.NotFound();
        invitation.RevokedAt ??= Now();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>What an invitation leads to, for anyone who has its code. Failures count against the address.</summary>
    public async Task<InvitationPreview> CheckAsync(string code, CancellationToken cancellationToken)
    {
        var (invitation, serverUrl, inviter) = await OpenAsync(code, cancellationToken);
        var signup = (await sharing.GetAsync(cancellationToken)).InvitationSignup;
        return new InvitationPreview(serverUrl, inviter.DisplayName ?? inviter.UserName, invitation.ExpiresAt, signup);
    }

    /// <summary>
    /// Asks for an account with an invitation, when the server allows it: the account is made disabled, without
    /// permissions or libraries, and waits for an administrator. The invitation does not work again.
    /// </summary>
    public async Task<InvitationSignup> SignUpAsync(InvitationSignupInput input, CancellationToken cancellationToken)
    {
        var (invitation, _, inviter) = await OpenAsync(input.Code, cancellationToken);
        if (!(await sharing.GetAsync(cancellationToken)).InvitationSignup)
            throw new ArcanistException.Conflict(ErrorCodes.InvitationSignupDisabled);

        var errors = new ValidationErrors();
        UserRules.ValidateUserName(input.UserName, "userName", errors);
        UserRules.ValidatePassword(input.Password, "password", errors);
        errors.ThrowIfAny();

        var normalized = TextNormalizer.Normalize(input.UserName);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken))
            throw ArcanistException.Field("userName", ErrorCodes.Duplicate);

        var displayName = input.DisplayName?.Trim();
        var user = new User
        {
            UserName = input.UserName.Trim(),
            NormalizedUserName = normalized,
            DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName,
            Role = UserRole.User,
            Permissions = Permissions.None,
            PasswordHash = "",
            IsDisabled = true,
            PendingApproval = true,
            CreatedAt = Now(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, input.Password);
        db.Users.Add(user);
        invitation.UsedAt = Now();
        invitation.UsedByUserId = user.Id;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("{User} asked for an account with an invitation from {Inviter}; it waits for approval", user.UserName, inviter.UserName);
        return new InvitationSignup(user.UserName, true);
    }

    /// <summary>The invitation link: the panel's join page on the public URL, with the code after the <c>#</c> so it never reaches a log.</summary>
    public static string LinkOf(string serverUrl, string code) => $"{serverUrl}{JoinPath}#{code}";

    /// <summary>The code's hash, as kept: letters and digits only, upper case (dashes and spaces do not count).</summary>
    public static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(new string(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray()))));

    /// <summary>A working invitation and its inviter; otherwise <c>invitation_invalid</c>, counted against the address.</summary>
    private async Task<(ServerInvitation Invitation, string ServerUrl, User Inviter)> OpenAsync(string? code, CancellationToken cancellationToken)
    {
        var key = "invitation:" + (http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "?");
        if (throttle.GetLockout(key) is { } lockout)
            throw new ArcanistException.TooSoon(ErrorCodes.TooManyRequests, (int)Math.Ceiling(lockout.TotalSeconds));

        var serverUrl = await sharing.PublicUrlAsync(cancellationToken);
        var hash = code is { Length: > 0 and < 100 } ? Hash(code) : null;
        var invitation = hash is null || serverUrl is null
            ? null
            : await db.ServerInvitations.Include(i => i.CreatedBy).SingleOrDefaultAsync(i => i.CodeHash == hash, cancellationToken);
        if (invitation is null || StateOf(invitation, Now()) != InvitationState.Pending || invitation.CreatedBy.IsDisabled
            || (invitation.CreatedBy.Role != UserRole.Admin && !invitation.CreatedBy.Permissions.HasFlag(Permissions.Invite)))
        {
            throttle.RecordFailure(key);
            throw new ArcanistException.Forbidden(ErrorCodes.InvitationInvalid);
        }

        return (invitation, serverUrl!, invitation.CreatedBy);
    }

    private static InvitationState StateOf(ServerInvitation invitation, DateTime now) =>
        invitation.RevokedAt is not null ? InvitationState.Revoked
        : invitation.UsedAt is not null ? InvitationState.Used
        : invitation.ExpiresAt <= now ? InvitationState.Expired
        : InvitationState.Pending;

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
