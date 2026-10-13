using Arcanist.Core.Application.Users;

namespace Arcanist.Core.Api.Rest;

public sealed record CreateAppPasswordInput(string Name);

public static class AppPasswordEndpoints
{
    public static IEndpointRouteBuilder MapAppPasswordEndpoints(this IEndpointRouteBuilder rest)
    {
        var passwords = rest.MapGroup("/me/app-passwords").WithTags("Account");

        passwords.MapGet("", (AppPasswordService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("AppPasswords")
            .WithSummary("The user's app passwords, for Subsonic and mStream apps; their secrets are never shown again.");

        passwords.MapPost("", (CreateAppPasswordInput input, AppPasswordService service, CancellationToken ct) => service.CreateAsync(input.Name, ct))
            .WithName("CreateAppPassword")
            .WithSummary("Creates a password for one app (Subsonic, mStream) and returns its secret, only this once. Needs a recent authentication.");

        passwords.MapDelete("/{id:guid}", async (Guid id, AppPasswordService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteAppPassword")
            .WithSummary("Revokes one of the user's app passwords.");

        return rest;
    }
}
