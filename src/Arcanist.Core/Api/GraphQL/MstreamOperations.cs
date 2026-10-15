using System.ComponentModel;
using Arcanist.Core.Application.Mstream;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("mstream")]
    [Description("How the mStream extension behaves with the mStream apps: the star threshold, signing in with the user's password and the apps' own functions. Administrators only.")]
    public static Task<MstreamStatus> Mstream([FromServices] MstreamService mstream, CancellationToken cancellationToken) =>
        mstream.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setMstreamSettings")]
    [Description("Changes how the mStream extension behaves with the mStream apps. Administrators only, with a recent authentication.")]
    public static Task<MstreamStatus> SetMstreamSettings(MstreamSettingsInput input, [FromServices] MstreamService mstream, CancellationToken cancellationToken) =>
        mstream.SetSettingsAsync(input, cancellationToken);

    [Name("rotateMstreamQuickConnect")]
    [Description("A new pairing code for the mStream apps' quick connect: apps that scanned the old one are disconnected. Administrators only, with a recent authentication.")]
    public static Task<MstreamStatus> RotateMstreamQuickConnect([FromServices] MstreamService mstream, CancellationToken cancellationToken) =>
        mstream.RotateQuickConnectAsync(cancellationToken);
}
