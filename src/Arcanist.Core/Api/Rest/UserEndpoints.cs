using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;

namespace Arcanist.Core.Api.Rest;

// Endpoints only forward to application services, which own validation and authorization.
// Operation names are the GraphQL field names in PascalCase (checked by ApiParityTests).

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/server-info", (ServerInfoService service) => service.Get())
            .WithTags("Server")
            .WithName("ServerInfo")
            .WithSummary("Public description of the server. Does not require authentication.");

        rest.MapGet("/legal/terms", (string? language, string? audience, Legal.LegalService legal) =>
                legal.Get(language, string.Equals(audience, "USER", StringComparison.OrdinalIgnoreCase) ? Legal.LegalAudience.User : Legal.LegalAudience.Admin))
            .WithTags("Server")
            .WithName("LegalTerms")
            .WithSummary("The terms and conditions and the disclaimer, in a language, for this server (self-hosted, or part of LinkFast's service): the setup wizard asks administrators to accept them, and players their users (audience USER). Does not require authentication.");

        var me = rest.MapGroup("/me").WithTags("Me");

        me.MapGet("", (UserService users, CancellationToken ct) => users.MeAsync(ct))
            .WithName("Me")
            .WithSummary("The signed-in user.");

        me.MapGet("/sessions", (UserService users, CancellationToken ct) => users.MySessionsAsync(ct))
            .WithName("MySessions")
            .WithSummary("Devices where the signed-in user is currently signed in.");

        me.MapDelete("/sessions/{id:guid}", async (Guid id, UserService users, CancellationToken ct) =>
            {
                await users.RevokeMySessionAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("RevokeMySession")
            .WithSummary("Signs out one of the signed-in user's devices.");

        me.MapPost("/password", async (ChangeMyPasswordInput input, UserService users, CancellationToken ct) =>
            {
                await users.ChangeMyPasswordAsync(input, ct);
                return Results.NoContent();
            })
            .WithName("ChangeMyPassword")
            .WithSummary("Changes the signed-in user's password and signs out their other devices.");

        var users = rest.MapGroup("/users").WithTags("Users");

        users.MapGet("", (UserService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("Users")
            .WithSummary("All users. Requires the MANAGE_USERS permission.");

        users.MapGet("/{id:guid}", (Guid id, UserService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("User")
            .WithSummary("A user by id. Requires the MANAGE_USERS permission.");

        users.MapPost("", async (CreateUserInput input, UserService service, CancellationToken ct) =>
            {
                var user = await service.CreateAsync(input, ct);
                return Results.Created($"/rest/v1/users/{user.Id}", user);
            })
            .WithName("CreateUser")
            .WithSummary("Requires MANAGE_USERS and a recent authentication.")
            .Produces<UserAccount>(StatusCodes.Status201Created);

        users.MapPut("/{id:guid}", (Guid id, UpdateUserInput input, UserService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateUser")
            .WithSummary("Requires MANAGE_USERS and a recent authentication.");

        users.MapPost("/{id:guid}/approve", (Guid id, ApproveUserInput? input, UserService service, CancellationToken ct) =>
                service.ApproveAsync(id, input, ct))
            .WithName("ApproveUser")
            .WithSummary("Lets in an account asked for with an invitation (pendingApproval): turns it on with the permissions and libraries given. Requires MANAGE_USERS and a recent authentication.");

        var invitations = rest.MapGroup("/invitations").WithTags("Invitations");

        invitations.MapGet("", (bool? all, InvitationService service, CancellationToken ct) => service.ListAsync(all == true, ct))
            .WithName("Invitations")
            .WithSummary("The user's invitations to the server, newest first (INVITE); with all=true, everyone's (MANAGE_USERS). Codes are only shown on making them.");

        invitations.MapPost("", (CreateInvitationInput? input, InvitationService service, CancellationToken ct) => service.CreateAsync(input, ct))
            .WithName("CreateInvitation")
            .WithSummary("Makes an invitation (INVITE; it needs the server's public URL, otherwise public_url_required): single use, a week long. Its link and QR code add the server to Ultimate apps and, if the server allows it, ask for an account that waits for an administrator.");

        invitations.MapPost("/{id:guid}/revoke", (Guid id, InvitationService service, CancellationToken ct) => service.RevokeAsync(id, ct))
            .WithName("RevokeInvitation")
            .WithSummary("Stops an invitation from working: its maker, or whoever manages users.");

        // Reachable without an account, with the code in the body (never in a URL that a log could keep).
        var publicInvitations = rest.MapGroup("/public/invitations").WithTags("Invitations")
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit);

        publicInvitations.MapPost("/check", (InvitationCodeInput input, InvitationService service, CancellationToken ct) =>
                service.CheckAsync(input.Code, ct))
            .WithName("CheckInvitation")
            .WithSummary("What an invitation leads to, for anyone with its code: the server's public URL, who invited and whether an account can be asked for. 403 invitation_invalid when it does not work (used, revoked, expired); failures count against the address.");

        publicInvitations.MapPost("/signup", (InvitationSignupInput input, InvitationService service, CancellationToken ct) =>
                service.SignUpAsync(input, ct))
            .WithName("SignUpWithInvitation")
            .WithSummary("Asks for an account with an invitation, when the server allows it (otherwise invitation_signup_disabled): it is made disabled and waits for an administrator to approve it; signing in before answers account_pending_approval. The invitation does not work again.");

        users.MapPut("/{id:guid}/libraries",(Guid id, SetUserLibrariesInput input, UserService service, CancellationToken ct) =>
                service.SetLibrariesAsync(id, input, ct))
            .WithName("SetUserLibraries")
            .WithSummary("Replaces the libraries explicitly granted to a user. Requires MANAGE_USERS and a recent authentication.");

        users.MapPost("/{id:guid}/password", async (Guid id, ResetUserPasswordInput input, UserService service, CancellationToken ct) =>
            {
                await service.ResetPasswordAsync(id, input, ct);
                return Results.NoContent();
            })
            .WithName("ResetUserPassword")
            .WithSummary("Sets a new password and signs the user out everywhere. Requires MANAGE_USERS and a recent authentication.");

        users.MapDelete("/{id:guid}", async (Guid id, UserService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteUser")
            .WithSummary("Requires MANAGE_USERS and a recent authentication.");

        return rest;
    }
}
