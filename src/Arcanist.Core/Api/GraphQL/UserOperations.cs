using System.ComponentModel;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Users;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

// Root operation types are partial: each feature adds its operations in its own file. Resolvers only forward to
// application services, which own validation and authorization. Every field must have a REST counterpart whose
// operation name is the field name in PascalCase (checked by ApiContractTests).

public sealed partial class Query
{
    [Name("serverInfo")]
    [Description("Public description of the server. Does not require authentication.")]
    public static ServerInfo ServerInfo([FromServices] ServerInfoService service) => service.Get();

    [Name("legalTerms")]
    [Description("The terms and conditions and the disclaimer, in a language, for this server (self-hosted, or part of LinkFast's service): the setup wizard asks administrators to accept them, and players their users (audience USER). Does not require authentication.")]
    public static Legal.LegalTerms LegalTerms(string? language, Legal.LegalAudience? audience, [FromServices] Legal.LegalService legal) =>
        legal.Get(language, audience ?? Legal.LegalAudience.Admin);

    [Name("me")]
    [Description("The signed-in user.")]
    public static Task<UserAccount> Me([FromServices] UserService users, CancellationToken cancellationToken) =>
        users.MeAsync(cancellationToken);

    [Name("mySessions")]
    [Description("Devices where the signed-in user is currently signed in.")]
    public static Task<IReadOnlyList<Session>> MySessions([FromServices] UserService users, CancellationToken cancellationToken) =>
        users.MySessionsAsync(cancellationToken);

    [Name("users")]
    [Description("All users. Requires the MANAGE_USERS permission.")]
    public static Task<IReadOnlyList<UserAccount>> Users([FromServices] UserService users, CancellationToken cancellationToken) =>
        users.ListAsync(cancellationToken);

    [Name("user")]
    [Description("A user by id. Requires the MANAGE_USERS permission.")]
    public static Task<UserAccount> User([Id] Guid id, [FromServices] UserService users, CancellationToken cancellationToken) =>
        users.GetAsync(id, cancellationToken);
}

public sealed partial class Mutation
{
    [Name("createUser")]
    [Description("Requires MANAGE_USERS and a recent authentication.")]
    public static Task<UserAccount> CreateUser(CreateUserInput input, [FromServices] UserService users, CancellationToken cancellationToken) =>
        users.CreateAsync(input, cancellationToken);

    [Name("updateUser")]
    [Description("Requires MANAGE_USERS and a recent authentication.")]
    public static Task<UserAccount> UpdateUser([Id] Guid id, UpdateUserInput input, [FromServices] UserService users, CancellationToken cancellationToken) =>
        users.UpdateAsync(id, input, cancellationToken);

    [Name("setUserLibraries")]
    [Description("Replaces the libraries explicitly granted to a user. Requires MANAGE_USERS and a recent authentication.")]
    public static Task<UserAccount> SetUserLibraries([Id] Guid id, SetUserLibrariesInput input, [FromServices] UserService users, CancellationToken cancellationToken) =>
        users.SetLibrariesAsync(id, input, cancellationToken);

    [Name("resetUserPassword")]
    [Description("Sets a new password and signs the user out everywhere. Requires MANAGE_USERS and a recent authentication.")]
    public static async Task<bool> ResetUserPassword([Id] Guid id, ResetUserPasswordInput input, [FromServices] UserService users, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(id, input, cancellationToken);
        return true;
    }

    [Name("deleteUser")]
    [Description("Requires MANAGE_USERS and a recent authentication.")]
    public static async Task<bool> DeleteUser([Id] Guid id, [FromServices] UserService users, CancellationToken cancellationToken)
    {
        await users.DeleteAsync(id, cancellationToken);
        return true;
    }

    [Name("changeMyPassword")]
    [Description("Changes the signed-in user's password and signs out their other devices.")]
    public static async Task<bool> ChangeMyPassword(ChangeMyPasswordInput input, [FromServices] UserService users, CancellationToken cancellationToken)
    {
        await users.ChangeMyPasswordAsync(input, cancellationToken);
        return true;
    }

    [Name("revokeMySession")]
    [Description("Signs out one of the signed-in user's devices.")]
    public static async Task<bool> RevokeMySession([Id] Guid id, [FromServices] UserService users, CancellationToken cancellationToken)
    {
        await users.RevokeMySessionAsync(id, cancellationToken);
        return true;
    }
}
