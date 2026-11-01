using System.ComponentModel;
using Arcanist.Core.Application.Users;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("mySecondFactor")]
    [Description("The user's second factors: the authenticator app, passkeys and how many recovery codes are left.")]
    public static Task<MySecondFactor> MySecondFactor([FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.MineAsync(cancellationToken);

    [Name("secondFactorPolicy")]
    [Description("Whether the server requires a second factor of everyone, and who still has none. Administrators only.")]
    public static Task<SecondFactorPolicy> SecondFactorPolicy([FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.PolicyAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("startTotpSetup")]
    [Description("A secret for an authenticator app; confirmTotpSetup enables it. Needs a recent authentication.")]
    public static Task<TotpSetup> StartTotpSetup([FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.StartTotpAsync(cancellationToken);

    [Name("confirmTotpSetup")]
    [Description("Enables the authenticator app with its first code. With the user's first factor come their recovery codes, shown only now. Signs their other devices out.")]
    public static Task<RecoveryCodes> ConfirmTotpSetup(string code, [FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.ConfirmTotpAsync(code, cancellationToken);

    [Name("disableTotp")]
    [Description("Removes the authenticator app. Needs a recent authentication; not the user's last factor if the server requires one.")]
    public static async Task<bool> DisableTotp([FromServices] SecondFactorService factors, CancellationToken cancellationToken)
    {
        await factors.DisableTotpAsync(cancellationToken);
        return true;
    }

    [Name("passkeyRegistrationOptions")]
    [Description("What navigator.credentials.create needs, as JSON; registerPasskey takes its answer. Needs a recent authentication.")]
    public static Task<PasskeyRegistration> PasskeyRegistrationOptions([FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.PasskeyRegistrationOptionsAsync(cancellationToken);

    [Name("registerPasskey")]
    [Description("Adds a passkey from the browser's answer, as JSON. With the user's first factor come their recovery codes, shown only now. Signs their other devices out.")]
    public static Task<RegisteredPasskey> RegisterPasskey(string? name, string attestation, [FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.RegisterPasskeyAsync(name, attestation, cancellationToken);

    [Name("renamePasskey")]
    [Description("Renames one of the user's passkeys.")]
    public static Task<Passkey> RenamePasskey([Id] Guid id, string name, [FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.RenamePasskeyAsync(id, name, cancellationToken);

    [Name("removePasskey")]
    [Description("Removes one of the user's passkeys. Needs a recent authentication; not their last factor if the server requires one.")]
    public static async Task<bool> RemovePasskey([Id] Guid id, [FromServices] SecondFactorService factors, CancellationToken cancellationToken)
    {
        await factors.RemovePasskeyAsync(id, cancellationToken);
        return true;
    }

    [Name("regenerateRecoveryCodes")]
    [Description("New recovery codes, shown only now; the old ones stop working. Needs a recent authentication.")]
    public static Task<RecoveryCodes> RegenerateRecoveryCodes([FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.RegenerateRecoveryCodesAsync(cancellationToken);

    [Name("resetUserSecondFactor")]
    [Description("For who lost their factors: removes them all and signs the user out; they pair one again at their next sign-in. Needs MANAGE_USERS and a recent authentication.")]
    public static async Task<bool> ResetUserSecondFactor([Id] Guid userId, [FromServices] SecondFactorService factors, CancellationToken cancellationToken)
    {
        await factors.ResetUserAsync(userId, cancellationToken);
        return true;
    }

    [Name("setSecondFactorPolicy")]
    [Description("Whether the server requires a second factor of everyone: who has none pairs one at their next sign-in. Administrators only, with a recent authentication.")]
    public static Task<SecondFactorPolicy> SetSecondFactorPolicy(bool required, [FromServices] SecondFactorService factors, CancellationToken cancellationToken) =>
        factors.SetPolicyAsync(required, cancellationToken);
}
