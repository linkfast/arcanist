using System.ComponentModel;
using Arcanist.Core.Application.Online;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("onlineServices")]
    [Description("Which online services are on (MusicBrainz, AcoustID, Cover Art Archive, LRCLib) and what each has done. Requires MANAGE_SETTINGS.")]
    public static Task<OnlineServicesStatus> OnlineServices([FromServices] OnlineServicesService online, CancellationToken cancellationToken) =>
        online.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("setOnlineServices")]
    [Description("Turns online services on or off. They send only fingerprints, identifiers and artist, album and track names. Requires MANAGE_SETTINGS.")]
    public static Task<OnlineServicesStatus> SetOnlineServices(SetOnlineServicesInput input, [FromServices] OnlineServicesService online, CancellationToken cancellationToken) =>
        online.SetAsync(input, cancellationToken);

    [Name("setAcoustIdKey")]
    [Description("Uses your own AcoustID application key instead of the project's; null or empty goes back to the project's. Requires MANAGE_SETTINGS.")]
    public static Task<OnlineServicesStatus> SetAcoustIdKey(SetAcoustIdKeyInput input, [FromServices] OnlineServicesService online, CancellationToken cancellationToken) =>
        online.SetAcoustIdKeyAsync(input.Key, cancellationToken);
}
