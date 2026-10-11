using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Users;

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

        users.MapPut("/{id:guid}/libraries", (Guid id, SetUserLibrariesInput input, UserService service, CancellationToken ct) =>
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
