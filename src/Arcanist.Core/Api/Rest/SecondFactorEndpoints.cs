using Arcanist.Core.Application.Users;

namespace Arcanist.Core.Api.Rest;

public sealed record ConfirmTotpInput(string Code);

public sealed record RegisterPasskeyInput(string? Name, string Attestation);

public sealed record RenamePasskeyInput(string Name);

public sealed record SetSecondFactorPolicyInput(bool Required);

public static class SecondFactorEndpoints
{
    public static IEndpointRouteBuilder MapSecondFactorEndpoints(this IEndpointRouteBuilder rest)
    {
        var mine = rest.MapGroup("/me/second-factor").WithTags("Account");

        mine.MapGet("", (SecondFactorService service, CancellationToken ct) => service.MineAsync(ct))
            .WithName("MySecondFactor")
            .WithSummary("The user's second factors: the authenticator app, passkeys and how many recovery codes are left.");

        mine.MapPost("/totp", (SecondFactorService service, CancellationToken ct) => service.StartTotpAsync(ct))
            .WithName("StartTotpSetup")
            .WithSummary("A secret for an authenticator app; confirming a code enables it. Needs a recent authentication.");

        mine.MapPost("/totp/confirm", (ConfirmTotpInput input, SecondFactorService service, CancellationToken ct) => service.ConfirmTotpAsync(input.Code, ct))
            .WithName("ConfirmTotpSetup")
            .WithSummary("Enables the authenticator app with its first code. With the user's first factor come their recovery codes, shown only now. Signs their other devices out.");

        mine.MapDelete("/totp", async (SecondFactorService service, CancellationToken ct) =>
            {
                await service.DisableTotpAsync(ct);
                return Results.NoContent();
            })
            .WithName("DisableTotp")
            .WithSummary("Removes the authenticator app. Needs a recent authentication; not the user's last factor if the server requires one.");

        mine.MapPost("/passkeys/options", (SecondFactorService service, CancellationToken ct) => service.PasskeyRegistrationOptionsAsync(ct))
            .WithName("PasskeyRegistrationOptions")
            .WithSummary("What navigator.credentials.create needs, as JSON. Needs a recent authentication.");

        mine.MapPost("/passkeys", (RegisterPasskeyInput input, SecondFactorService service, CancellationToken ct) => service.RegisterPasskeyAsync(input.Name, input.Attestation, ct))
            .WithName("RegisterPasskey")
            .WithSummary("Adds a passkey from the browser's answer, as JSON. With the user's first factor come their recovery codes, shown only now. Signs their other devices out.");

        mine.MapPut("/passkeys/{id:guid}/name", (Guid id, RenamePasskeyInput input, SecondFactorService service, CancellationToken ct) => service.RenamePasskeyAsync(id, input.Name, ct))
            .WithName("RenamePasskey")
            .WithSummary("Renames one of the user's passkeys.");

        mine.MapDelete("/passkeys/{id:guid}", async (Guid id, SecondFactorService service, CancellationToken ct) =>
            {
                await service.RemovePasskeyAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("RemovePasskey")
            .WithSummary("Removes one of the user's passkeys. Needs a recent authentication; not their last factor if the server requires one.");

        mine.MapPost("/recovery-codes", (SecondFactorService service, CancellationToken ct) => service.RegenerateRecoveryCodesAsync(ct))
            .WithName("RegenerateRecoveryCodes")
            .WithSummary("New recovery codes, shown only now; the old ones stop working. Needs a recent authentication.");

        rest.MapDelete("/users/{id:guid}/second-factor", async (Guid id, SecondFactorService service, CancellationToken ct) =>
            {
                await service.ResetUserAsync(id, ct);
                return Results.NoContent();
            })
            .WithTags("Users")
            .WithName("ResetUserSecondFactor")
            .WithSummary("For who lost their factors: removes them all and signs the user out; they pair one again at their next sign-in. Needs MANAGE_USERS and a recent authentication.");

        rest.MapGet("/server/second-factor", (SecondFactorService service, CancellationToken ct) => service.PolicyAsync(ct))
            .WithTags("Server")
            .WithName("SecondFactorPolicy")
            .WithSummary("Whether the server requires a second factor of everyone, and who still has none. Administrators only.");

        rest.MapPut("/server/second-factor", (SetSecondFactorPolicyInput input, SecondFactorService service, CancellationToken ct) => service.SetPolicyAsync(input.Required, ct))
            .WithTags("Server")
            .WithName("SetSecondFactorPolicy")
            .WithSummary("Whether the server requires a second factor of everyone: who has none pairs one at their next sign-in. Administrators only, with a recent authentication.");

        return rest;
    }
}
