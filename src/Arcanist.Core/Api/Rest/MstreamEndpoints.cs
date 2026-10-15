using Arcanist.Core.Application.Mstream;

namespace Arcanist.Core.Api.Rest;

public static class MstreamEndpoints
{
    public static IEndpointRouteBuilder MapMstreamEndpoints(this IEndpointRouteBuilder rest)
    {
        var mstream = rest.MapGroup("/mstream").WithTags("mStream");

        mstream.MapGet("", (MstreamService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("Mstream")
            .WithSummary("How the mStream extension behaves with the mStream apps: the star threshold, signing in with the user's password " +
                         "and the apps' own functions. Administrators only.");

        mstream.MapPut("/settings", (MstreamSettingsInput input, MstreamService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithName("SetMstreamSettings")
            .WithSummary("Changes how the mStream extension behaves with the mStream apps. Administrators only, with a recent authentication.");

        mstream.MapPost("/quick-connect/rotate", (MstreamService service, CancellationToken ct) => service.RotateQuickConnectAsync(ct))
            .WithName("RotateMstreamQuickConnect")
            .WithSummary("A new pairing code for the mStream apps' quick connect: apps that scanned the old one are disconnected. " +
                         "Administrators only, with a recent authentication.");

        return rest;
    }
}
