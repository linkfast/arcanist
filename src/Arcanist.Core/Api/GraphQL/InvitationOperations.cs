using System.ComponentModel;
using Arcanist.Core.Application.Users;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

// Checking an invitation and asking for an account with it need no account: REST only (/rest/v1/public/invitations).

public sealed partial class Query
{
    [Name("invitations")]
    [Description("The user's invitations to the server, newest first (INVITE); with all, everyone's (MANAGE_USERS). Codes are only shown on making them.")]
    public static Task<IReadOnlyList<Invitation>> Invitations(bool? all, [FromServices] InvitationService invitations, CancellationToken cancellationToken) =>
        invitations.ListAsync(all == true, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("createInvitation")]
    [Description("Makes an invitation (INVITE; it needs the server's public URL, otherwise public_url_required): single use, a week long. Its link and QR code add the server to Ultimate apps and, if the server allows it, ask for an account that waits for an administrator.")]
    public static Task<NewInvitation> CreateInvitation(CreateInvitationInput? input, [FromServices] InvitationService invitations, CancellationToken cancellationToken) =>
        invitations.CreateAsync(input, cancellationToken);

    [Name("revokeInvitation")]
    [Description("Stops an invitation from working: its maker, or whoever manages users.")]
    public static Task<bool> RevokeInvitation([Id] Guid id, [FromServices] InvitationService invitations, CancellationToken cancellationToken) =>
        invitations.RevokeAsync(id, cancellationToken);

    [Name("approveUser")]
    [Description("Lets in an account asked for with an invitation (pendingApproval): turns it on with the permissions and libraries given. Requires MANAGE_USERS and a recent authentication.")]
    public static Task<UserAccount> ApproveUser([Id] Guid id, ApproveUserInput? input, [FromServices] UserService users, CancellationToken cancellationToken) =>
        users.ApproveAsync(id, input, cancellationToken);
}
