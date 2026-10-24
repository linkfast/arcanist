using System.Globalization;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Discovery;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Data;
using Arcanist.Core.Dlna;
using Arcanist.Core.Media;
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Arcanist.Core.Extensions.Dlna;

/// <summary>
/// The DLNA media server: TVs, consoles, Kodi or VLC on the local network browse the published libraries and play
/// them, and Arcanist plays to renderers ("play to", see <c>Application/Dlna</c>). DLNA has no sign-in, so it only
/// answers on the local network, and devices only see the libraries an administrator published.
/// </summary>
internal sealed class DlnaExtension : IBuiltInExtension
{
    public const string Id = "dlna";
    public const string BasePath = "/ext/dlna";
    public const string DescriptionPath = BasePath + "/description.xml";

    public ExtensionManifest Manifest { get; } = new(
        Id,
        "DLNA",
        typeof(DlnaExtension).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "DLNA/UPnP media server for TVs, consoles and players on the local network, and playing to DLNA speakers and TVs.",
        "Arcanist",
        "AGPL-3.0-or-later");

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        var dlna = endpoints.MapGroup(BasePath).ExcludeFromDescription().AddEndpointFilter(async (invocation, next) =>
            LocalNetwork.Contains(invocation.HttpContext.Connection.RemoteIpAddress)
                ? await next(invocation)
                : Results.NotFound());

        dlna.MapGet("/description.xml", DescriptionAsync);
        dlna.MapGet("/content-directory.xml", () => Xml(DlnaDescriptions.ContentDirectory));
        dlna.MapGet("/connection-manager.xml", () => Xml(DlnaDescriptions.ConnectionManager));
        dlna.MapPost("/content-directory/control", ContentDirectoryAsync).DisableAntiforgery();
        dlna.MapPost("/connection-manager/control", ConnectionManagerAsync).DisableAntiforgery();
        dlna.MapMethods("/{service}/events", ["SUBSCRIBE", "UNSUBSCRIBE"], Subscribe);
        dlna.MapMethods("/media/{trackId:guid}/{fileId:guid}/{name}", [HttpMethods.Get, HttpMethods.Head], MediaAsync);
        dlna.MapMethods("/play/{token}/{name}", [HttpMethods.Get, HttpMethods.Head], PlayAsync);
        dlna.MapGet("/art/{name}", ArtAsync);
    }

    private static IResult Xml(string xml) => Results.Text(xml, "text/xml; charset=\"utf-8\"", System.Text.Encoding.UTF8);

    private static async Task<IResult> DescriptionAsync(DlnaSettings settings, ServerInfoService serverInfo, CancellationToken cancellationToken)
    {
        var config = await settings.GetAsync(cancellationToken);
        var info = serverInfo.Get();
        return Xml(DlnaDescriptions.Device(await settings.DeviceIdAsync(cancellationToken), config.Name, info.Version, info.SourceUrl, BasePath));
    }

    /// <summary>Makes the request act as DLNA devices: the published libraries, nothing personal.</summary>
    private static async Task<DlnaConfig> SignInAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var config = await context.RequestServices.GetRequiredService<DlnaSettings>().GetAsync(cancellationToken);
        DlnaAccess.SignIn(context, DlnaAccess.For(config));
        return config;
    }

    private static async Task<IResult> ContentDirectoryAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            var action = await Soap.ReadAsync(context.Request, cancellationToken);
            var config = await SignInAsync(context, cancellationToken);
            var services = context.RequestServices;
            var db = services.GetRequiredService<ArcanistDbContext>();
            var libraries = config.LibraryIds.ToList();
            var lastScan = await db.Libraries.Where(l => libraries.Contains(l.Id)).MaxAsync(l => l.LastScanAt, cancellationToken);
            // Changes when a published library is scanned, so devices refresh what they cached.
            var updateId = ((lastScan?.Ticks ?? 0) / TimeSpan.TicksPerSecond % uint.MaxValue).ToString(CultureInfo.InvariantCulture);

            switch (action.Name)
            {
                case "GetSearchCapabilities":
                    return Soap.Response(Ssdp.ContentDirectory, action.Name, [("SearchCaps", "")]);
                case "GetSortCapabilities":
                    return Soap.Response(Ssdp.ContentDirectory, action.Name, [("SortCaps", "")]);
                case "GetSystemUpdateID":
                    return Soap.Response(Ssdp.ContentDirectory, action.Name, [("Id", updateId)]);
                case "Browse":
                {
                    var language = await services.GetRequiredService<SettingsStore>().GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken);
                    var library = new DlnaLibrary(db, services.GetRequiredService<CatalogService>(), services.GetRequiredService<PlaylistService>(),
                        services.GetRequiredService<DiscoveryService>(), DlnaAccess.For(config), config,
                        DlnaFormats.Convertible(services.GetRequiredService<FfmpegLocator>().Current),
                        $"{context.Request.Scheme}://{context.Request.Host}", language?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true,
                        await Application.Dlna.DlnaService.RatingsUserAsync(db, config, cancellationToken),
                        await services.GetRequiredService<Application.Listening.RatingSettings>().GetAsync(cancellationToken),
                        await Application.Dlna.DlnaService.StarTracksUsersAsync(db, config, cancellationToken));
                    var id = action.Argument("ObjectID") is { Length: > 0 } objectId ? objectId : DlnaLibrary.Root;
                    _ = int.TryParse(action.Argument("StartingIndex"), out var start);
                    _ = int.TryParse(action.Argument("RequestedCount"), out var count);

                    IReadOnlyList<DidlObject> result;
                    int total;
                    if (action.Argument("BrowseFlag") == "BrowseMetadata")
                        (result, total) = ([await library.ObjectAsync(id, cancellationToken)], 1);
                    else if (action.Argument("BrowseFlag") == "BrowseDirectChildren")
                        (result, total) = await library.ChildrenAsync(id, start, count, cancellationToken);
                    else
                        throw new UpnpException(402, "Invalid Args");

                    return Soap.Response(Ssdp.ContentDirectory, action.Name,
                    [
                        ("Result", Didl.Write(result)),
                        ("NumberReturned", result.Count.ToString(CultureInfo.InvariantCulture)),
                        ("TotalMatches", total.ToString(CultureInfo.InvariantCulture)),
                        ("UpdateID", updateId),
                    ]);
                }
                default:
                    throw new UpnpException(401, "Invalid Action");
            }
        }
        catch (UpnpException e)
        {
            return Soap.Fault(e);
        }
        catch (ArcanistException.NotFound)
        {
            return Soap.Fault(new UpnpException(701, "No such object"));
        }
    }

    private static async Task<IResult> ConnectionManagerAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            var action = await Soap.ReadAsync(context.Request, cancellationToken);
            return action.Name switch
            {
                "GetProtocolInfo" => Soap.Response(Ssdp.ConnectionManager, action.Name, [("Source", DlnaFormats.SourceProtocols()), ("Sink", "")]),
                "GetCurrentConnectionIDs" => Soap.Response(Ssdp.ConnectionManager, action.Name, [("ConnectionIDs", "0")]),
                "GetCurrentConnectionInfo" => Soap.Response(Ssdp.ConnectionManager, action.Name,
                [
                    ("RcsID", "-1"), ("AVTransportID", "-1"), ("ProtocolInfo", ""), ("PeerConnectionManager", ""), ("PeerConnectionID", "-1"),
                    ("Direction", "Output"), ("Status", "OK"),
                ]),
                _ => throw new UpnpException(401, "Invalid Action"),
            };
        }
        catch (UpnpException e)
        {
            return Soap.Fault(e);
        }
    }

    /// <summary>
    /// Event subscriptions, which many devices insist on. They are accepted (Arcanist sends no events: devices see
    /// changes through SystemUpdateID when they browse).
    /// </summary>
    private static IResult Subscribe(HttpContext context)
    {
        if (HttpMethods.Equals(context.Request.Method, "SUBSCRIBE"))
        {
            context.Response.Headers["SID"] = context.Request.Headers["SID"].FirstOrDefault() ?? $"uuid:{Guid.NewGuid()}";
            context.Response.Headers["TIMEOUT"] = "Second-1800";
        }

        return Results.Ok();
    }

    /// <summary>Audio for devices that browse: a copy of a published track, as it is or converted.</summary>
    private static async Task<IResult> MediaAsync(Guid trackId, Guid fileId, string name, HttpContext context, CancellationToken cancellationToken)
    {
        await SignInAsync(context, cancellationToken);
        return await SendAsync(context, trackId, fileId, name, cancellationToken);
    }

    /// <summary>Audio for "play to": the URL's grant says whose track it is and how to send it.</summary>
    private static async Task<IResult> PlayAsync(string token, HttpContext context, CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetRequiredService<DlnaPlayTokens>().Read(token) is not { } grant
            || !await ExtensionAuthentication.SignInAsync(context, grant.UserId, "dlna", cancellationToken))
            return Results.NotFound();
        return await SendAsync(context, grant.TrackId, grant.FileId, DlnaFormats.Name(grant.Format), cancellationToken);
    }

    private static async Task<IResult> SendAsync(HttpContext context, Guid trackId, Guid fileId, string name, CancellationToken cancellationToken)
    {
        var dot = name.IndexOf('.');
        if (DlnaFormats.Parse(dot > 0 ? name[..dot] : name) is not { } format)
            return Results.NotFound();

        var access = context.Items[typeof(Auth.UserAccess)] as Auth.UserAccess;
        var catalog = context.RequestServices.GetRequiredService<CatalogService>();
        var file = await catalog.VisibleFiles(access!).AsNoTracking().Where(f => f.Id == fileId && f.TrackId == trackId)
            .Select(f => new { f.RelativePath, f.DurationMs }).FirstOrDefaultAsync(cancellationToken);
        if (file is null)
            return Results.NotFound();

        var response = context.Response;
        response.Headers["transferMode.dlna.org"] = "Streaming";
        response.Headers["contentFeatures.dlna.org"] = DlnaFormats.Features(format, file.RelativePath);

        double? offset = null;
        if (format != DlnaFormat.Original && TimeSeek(context.Request.Headers["TimeSeekRange.dlna.org"].FirstOrDefault()) is { } seconds)
        {
            offset = seconds;
            var total = file.DurationMs / 1000.0;
            response.Headers["TimeSeekRange.dlna.org"] = string.Create(CultureInfo.InvariantCulture, $"npt={seconds:0.000}-{total:0.000}/{total:0.000}");
        }

        try
        {
            return await MediaResponses.StreamWithProfileAsync(context, trackId, fileId, false, DlnaFormats.Profile(format), null, offset, cancellationToken);
        }
        catch (ArcanistException.NotFound)
        {
            return Results.NotFound();
        }
    }

    /// <summary>The start of a DLNA time seek (<c>npt=123.4-</c> or <c>npt=0:02:03.4-</c>), in seconds.</summary>
    internal static double? TimeSeek(string? header)
    {
        if (header is null || !header.StartsWith("npt=", StringComparison.OrdinalIgnoreCase))
            return null;
        var start = header[4..].Split('-')[0].Trim();
        if (start.Contains(':'))
            return RendererControl.ParseTime(start) / 1000.0;
        return double.TryParse(start, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;
    }

    private static async Task<IResult> ArtAsync(string name, HttpContext context, ArtworkService artwork, CancellationToken cancellationToken)
    {
        var dot = name.IndexOf('.');
        if (!Guid.TryParseExact(dot > 0 ? name[..dot] : name, "N", out var id))
            return Results.NotFound();
        try
        {
            var file = await artwork.GetAsync(id, 512, cancellationToken);
            context.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(file.Path, file.ContentType, entityTag: new EntityTagHeaderValue(file.ETag));
        }
        catch (ArcanistException.NotFound)
        {
            return Results.NotFound();
        }
    }
}
