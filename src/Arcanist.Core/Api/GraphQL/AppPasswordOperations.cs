using System.ComponentModel;
using Arcanist.Core.Application.Users;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("appPasswords")]
    [Description("The user's app passwords, for Subsonic and mStream apps; their secrets are never shown again.")]
    public static Task<IReadOnlyList<AppPassword>> AppPasswords([FromServices] AppPasswordService passwords, CancellationToken cancellationToken) =>
        passwords.ListAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("createAppPassword")]
    [Description("Creates a password for one app (Subsonic, mStream) and returns its secret, only this once. Needs a recent authentication.")]
    public static Task<CreatedAppPassword> CreateAppPassword(string name, [FromServices] AppPasswordService passwords, CancellationToken cancellationToken) =>
        passwords.CreateAsync(name, cancellationToken);

    [Name("deleteAppPassword")]
    [Description("Revokes one of the user's app passwords.")]
    public static async Task<bool> DeleteAppPassword([Id] Guid id, [FromServices] AppPasswordService passwords, CancellationToken cancellationToken)
    {
        await passwords.DeleteAsync(id, cancellationToken);
        return true;
    }
}
