using Arcanist.Core.Application.Online;

namespace Arcanist.Core.Api.Rest;

public static class OnlineEndpoints
{
    public static IEndpointRouteBuilder MapOnlineEndpoints(this IEndpointRouteBuilder rest)
    {
        var online = rest.MapGroup("/online-services").WithTags("Online services");

        online.MapGet("", (OnlineServicesService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithName("OnlineServices")
            .WithSummary("Which online services are on (MusicBrainz, AcoustID, Cover Art Archive, LRCLib) and what each has done. Requires MANAGE_SETTINGS.");

        online.MapPut("", (SetOnlineServicesInput input, OnlineServicesService service, CancellationToken ct) => service.SetAsync(input, ct))
            .WithName("SetOnlineServices")
            .WithSummary("Turns online services on or off. They send only fingerprints, identifiers and artist, album and track names. Requires MANAGE_SETTINGS.");

        online.MapPut("/acoustid-key", (SetAcoustIdKeyInput input, OnlineServicesService service, CancellationToken ct) => service.SetAcoustIdKeyAsync(input.Key, ct))
            .WithName("SetAcoustIdKey")
            .WithSummary("Uses your own AcoustID application key instead of the project's; null or empty goes back to the project's. Requires MANAGE_SETTINGS.");

        return rest;
    }
}
