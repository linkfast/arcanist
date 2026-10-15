using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Mstream;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Playlist = Arcanist.Core.Application.Playlists.Playlist;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>
/// mStream's API, so the mStream apps can play from Arcanist: its library, file explorer, playlists, media, album
/// art and transcoding routes. A layer over the application services: users see what they see in Arcanist. Apps
/// sign in with an app password and get a token that lasts until that password is deleted. Written from mStream's
/// public API description, without its code.
/// </summary>
internal sealed partial class MstreamExtension : IBuiltInExtension
{
    public const string Id = "mstream";

    /// <summary>
    /// The mStream version whose API this extension speaks, as <c>/api</c> reports it: the apps turn functions on by
    /// version (the auto-DJ's batches and filters, the statistics). Arcanist's own version goes in <c>arcanist</c>.
    /// </summary>
    public const string ApiVersion = "6.28.0";
    private const string TokenName = "x-access-token";

    private static readonly string[] Get = [HttpMethods.Get];
    private static readonly string[] Post = [HttpMethods.Post];
    private static readonly string[] Delete = [HttpMethods.Delete];
    private static readonly string[] GetOrPost = [HttpMethods.Get, HttpMethods.Post];
    private static readonly string[] GetOrHead = [HttpMethods.Get, HttpMethods.Head];

    public ExtensionManifest Manifest { get; } = new(
        Id,
        "mStream",
        typeof(MstreamExtension).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        "mStream API for the mStream apps. Apps sign in with an app password.",
        "Arcanist",
        "AGPL-3.0-or-later",
        Routes: ["/api", "/media", "/album-art", "/transcode"]);

    public void ConfigureServices(IServiceCollection services, ExtensionContext context) => services.AddSingleton<MstreamTokens>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        Map(endpoints, "/api/v1/auth/login", Post, LoginAsync, anonymous: true)
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimit);
        Map(endpoints, "/api", Get, ServerAsync);
        Map(endpoints, "/api/v1/ping", Get, PingAsync);

        Map(endpoints, "/api/v1/db/status", Get, StatusAsync);
        Map(endpoints, "/api/v1/db/metadata", Post, MetadataAsync);
        Map(endpoints, "/api/v1/db/metadata/batch", Post, MetadataBatchAsync);
        Map(endpoints, "/api/v1/db/artists", GetOrPost, ArtistsAsync);
        Map(endpoints, "/api/v1/db/artists-albums", Post, ArtistAlbumsAsync);
        Map(endpoints, "/api/v1/db/albums", GetOrPost, AlbumsAsync);
        Map(endpoints, "/api/v1/db/album-songs", Post, AlbumSongsAsync);
        Map(endpoints, "/api/v1/db/genres", GetOrPost, GenresAsync);
        Map(endpoints, "/api/v1/db/genre-songs", Post, GenreSongsAsync);
        Map(endpoints, "/api/v1/db/search", Post, SearchAsync);
        Map(endpoints, "/api/v1/db/rated", GetOrPost, RatedAsync);
        Map(endpoints, "/api/v1/db/rate-song", Post, RateSongAsync);
        Map(endpoints, "/api/v1/db/recent/added", Post, RecentlyAddedAsync);
        Map(endpoints, "/api/v1/db/stats/recently-played", Post, RecentlyPlayedAsync);
        Map(endpoints, "/api/v1/db/stats/most-played", Post, MostPlayedAsync);
        Map(endpoints, "/api/v1/db/random-songs", Post, RandomSongsAsync);
        Map(endpoints, "/api/v1/lyrics", Get, LyricsAsync);

        // The apps' own functions, each switched on in the panel (MstreamFeature).
        Map(endpoints, "/api/v1/db/waveform", Get, WaveformAsync);
        Map(endpoints, "/api/v1/scan/progress", Get, ScanProgressAsync);
        Map(endpoints, "/api/v1/scan/status", Get, ScanStatusAsync);
        Map(endpoints, "/api/v1/discovery/local/similar/tracks", Post, SimilarTracksAsync);
        Map(endpoints, "/api/v1/discovery/local/similar/artists", Post, SimilarArtistsAsync);
        Map(endpoints, "/api/v1/discovery/local/path", Post, JourneyAsync);
        Map(endpoints, "/api/v1/stats/plays", Post, ReportPlaysAsync);
        Map(endpoints, "/api/v1/stats/plays", Delete, ForgetPlaysAsync);
        Map(endpoints, "/api/v1/stats/plays/{id}", Delete, ForgetPlayAsync);
        Map(endpoints, "/api/v1/stats/now-playing", Post, AnnounceNowPlayingAsync);
        Map(endpoints, "/api/v1/stats/now-playing", Get, NowPlayingAsync);
        Map(endpoints, "/api/v1/stats/summary", Get, StatsSummaryAsync);
        Map(endpoints, "/api/v1/stats/top", Get, StatsTopAsync);
        Map(endpoints, "/api/v1/stats/timeseries", Get, StatsTimeSeriesAsync);
        Map(endpoints, "/api/v1/stats/history", Get, StatsHistoryAsync);
        Map(endpoints, "/api/v1/stats/tracks", Post, StatsTracksAsync);
        Map(endpoints, "/api/v1/stats/periods", Get, StatsPeriodsAsync);
        Map(endpoints, "/api/v1/stats/reset", Post, ResetStatsAsync);
        Map(endpoints, "/api/v1/stats/export", Get, StatsExportAsync);

        Map(endpoints, "/api/v1/file-explorer", Post, FileExplorerAsync);
        Map(endpoints, "/api/v1/file-explorer/recursive", Post, RecursiveAsync);
        Map(endpoints, "/media/{vpath}/{**path}", GetOrHead, MediaAsync);
        Map(endpoints, "/album-art/{file}", Get, AlbumArtAsync);
        Map(endpoints, "/transcode/{vpath}/{**path}", GetOrHead, TranscodeAsync);

        Map(endpoints, "/api/v1/playlist/getall", Get, PlaylistsAsync);
        Map(endpoints, "/api/v1/playlist/load", Post, LoadPlaylistAsync);
        Map(endpoints, "/api/v1/playlist/add-song", Post, AddToPlaylistAsync);
        Map(endpoints, "/api/v1/playlist/remove-song", Post, RemoveFromPlaylistAsync);
        Map(endpoints, "/api/v1/playlist/new", Post, NewPlaylistAsync);
        Map(endpoints, "/api/v1/playlist/save", Post, SavePlaylistAsync);
        Map(endpoints, "/api/v1/playlist/rename", Post, RenamePlaylistAsync);
        Map(endpoints, "/api/v1/playlist/delete", Post, DeletePlaylistAsync);

        Map(endpoints, "/api/v1/lastfm/scrobble-by-filepath", Post, ScrobbleAsync);
        // No Last.fm here: plays are recorded in Arcanist only.
        Map(endpoints, "/api/v1/lastfm/scrobble-by-metadata", Post, _ => Task.FromResult(MstreamCall.Json(new JsonObject())));
        Map(endpoints, "/api/v1/lastfm/status", Get, _ => Task.FromResult(MstreamCall.Json(new JsonObject { ["serverEnabled"] = false })));
        Map(endpoints, "/api/v1/lastfm/similar-artists", Get, _ => Task.FromResult(MstreamCall.Json(new JsonObject { ["artists"] = new JsonArray() })));

        // Web players call from their own origin; the token travels in a header or the query.
        foreach (var prefix in Manifest.Routes!)
        {
            endpoints.MapMethods(prefix + "/{**path}", [HttpMethods.Options], (HttpContext http) =>
                {
                    AllowAnyOrigin(http);
                    http.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, HEAD, OPTIONS";
                    return Results.NoContent();
                })
                .ExcludeFromDescription();
        }
    }

    private static RouteHandlerBuilder Map(IEndpointRouteBuilder endpoints, string pattern, string[] methods,
        Func<MstreamCall, Task<IResult>> handler, bool anonymous = false) =>
        endpoints.MapMethods(pattern, methods, async (HttpContext http, CancellationToken cancellationToken) =>
            {
                AllowAnyOrigin(http);
                var call = new MstreamCall(http, await MstreamBody.ReadAsync(http.Request, cancellationToken), cancellationToken);
                try
                {
                    if (!anonymous && !await AuthenticateAsync(call))
                        return MstreamCall.Error(StatusCodes.Status401Unauthorized, "Authentication Error");
                    return await handler(call);
                }
                catch (MstreamException e)
                {
                    return MstreamCall.Error(e.Status, e.Message);
                }
                catch (ArcanistException e)
                {
                    return e switch
                    {
                        ArcanistException.NotFound => MstreamCall.Error(StatusCodes.Status404NotFound, "Not Found"),
                        ArcanistException.Forbidden => MstreamCall.Error(StatusCodes.Status403Forbidden, "Forbidden"),
                        ArcanistException.Unauthorized => MstreamCall.Error(StatusCodes.Status401Unauthorized, "Authentication Error"),
                        _ => MstreamCall.Error(StatusCodes.Status400BadRequest, "Validation Error"),
                    };
                }
            })
            .ExcludeFromDescription()
            .DisableAntiforgery();

    private static void AllowAnyOrigin(HttpContext http)
    {
        http.Response.Headers.AccessControlAllowOrigin = "*";
        http.Response.Headers.AccessControlAllowHeaders = $"Origin, X-Requested-With, Content-Type, Accept, {TokenName}";
    }

    /// <summary>The token, from where mStream clients put it: the body, the query, a header or a cookie.</summary>
    private static async Task<bool> AuthenticateAsync(MstreamCall call)
    {
        var request = call.Http.Request;
        var token = call.Body.String("token") ?? call.Query("token") ?? request.Headers[TokenName].FirstOrDefault() ?? request.Cookies[TokenName];
        if (string.IsNullOrEmpty(token))
            return false;

        switch (call.Service<MstreamTokens>().Read(token))
        {
            case MstreamTokenSubject.AppPassword app:
                return await call.Service<AppPasswordService>().UserOfAsync(app.PasswordId, call.Cancellation) is { } userId
                       && await ExtensionAuthentication.SignInAsync(call.Http, userId, "mstream", call.Cancellation);
            case MstreamTokenSubject.UserPassword user:
                // Only while that sign-in is allowed, and until the user's password (or state) changes.
                return (await call.Service<MstreamSettings>().GetAsync(call.Cancellation)).PasswordSignIn
                       && await ExtensionAuthentication.SignInAsync(call.Http, user.UserId, "mstream", call.Cancellation)
                       && call.Access.SecurityStamp == user.SecurityStamp
                       && await PasswordHashAsync(call, user.UserId) is { } hash
                       && MstreamTokenSubject.Fingerprint(hash) == user.PasswordFingerprint;
            default:
                return false;
        }
    }

    private static Task<string?> PasswordHashAsync(MstreamCall call, Guid userId) =>
        call.Service<Data.ArcanistDbContext>().Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleOrDefaultAsync(call.Cancellation);

    /// <summary>
    /// Signs in (JSON or form) with an app password, or with the user's own password when an administrator allows it.
    /// Failures are slowed down and all look the same.
    /// </summary>
    private static async Task<IResult> LoginAsync(MstreamCall call)
    {
        var userName = call.Body.Required("username");
        var password = call.Body.Required("password");
        var tokens = call.Service<MstreamTokens>();
        string? token = null;
        if (await call.Service<AppPasswordService>().SignInAsync(userName, password, call.Cancellation) is { } signedIn
            && await ExtensionAuthentication.SignInAsync(call.Http, signedIn.UserId, "mstream", call.Cancellation))
        {
            token = tokens.Create(signedIn.PasswordId);
        }
        else if ((await call.Service<MstreamSettings>().GetAsync(call.Cancellation)).PasswordSignIn
                 && await call.Service<AuthService>().VerifyPasswordAsync(userName, password, call.Cancellation) is ({ } userId, null)
                 && await ExtensionAuthentication.SignInAsync(call.Http, userId, "mstream", call.Cancellation))
        {
            token = tokens.CreateForUser(userId, call.Access.SecurityStamp, (await PasswordHashAsync(call, userId))!);
        }

        if (token is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(800), call.Service<TimeProvider>(), call.Cancellation);
            return MstreamCall.Error(StatusCodes.Status401Unauthorized, "Login Failed");
        }

        call.Http.Response.Cookies.Append(TokenName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = call.Http.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(5 * 365),
        });

        var vpaths = await new MstreamLibrary(call).VpathsAsync();
        return MstreamCall.Json(new JsonObject { ["vpaths"] = Names(vpaths), ["token"] = token });
    }

    /// <summary>What the server is and does, and who the user is (mStream 6's layered answer).</summary>
    private static async Task<IResult> ServerAsync(MstreamCall call)
    {
        var vpaths = await new MstreamLibrary(call).VpathsAsync();
        var config = await call.Service<MstreamSettings>().GetAsync(call.Cancellation);
        var features = new JsonObject
        {
            ["transcode"] = Transcode(call),
            ["supportedAudioFiles"] = SupportedFiles(),
            ["discovery"] = config.Has(MstreamFeature.Similar),
            ["discoveryP2p"] = false,
            ["discoveryReady"] = config.Has(MstreamFeature.Similar),
            ["discoveryPlugins"] = false,
            ["discoveryOwned"] = false,
            ["sync"] = false,
        };
        // Present only on servers that speak the statistics API: apps gate on the key.
        if (config.Has(MstreamFeature.Stats))
            features["stats"] = StatsVersion;
        return MstreamCall.Json(new JsonObject
        {
            ["server"] = ApiVersion,
            ["arcanist"] = call.Service<ServerInfoService>().Get().Version,
            ["apiVersions"] = new JsonArray("1"),
            ["features"] = features,
            ["user"] = new JsonObject
            {
                ["username"] = call.Access.UserName,
                ["admin"] = call.Access.IsAdmin,
                ["vpaths"] = Names(vpaths),
                ["noMkdir"] = true,
                ["noUpload"] = true,
                ["noFileModify"] = true,
                ["vpathMetaData"] = VpathMetadata(vpaths),
                ["federationBrowse"] = false,
            },
        });
    }

    /// <summary>The flat start-up answer older apps ask for.</summary>
    private static async Task<IResult> PingAsync(MstreamCall call)
    {
        var vpaths = await new MstreamLibrary(call).VpathsAsync();
        var playlists = await EditablePlaylistsAsync(call);
        var config = await call.Service<MstreamSettings>().GetAsync(call.Cancellation);
        var ping = new JsonObject
        {
            ["vpaths"] = Names(vpaths),
            ["playlists"] = new JsonArray(playlists.Select(p => p.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => (JsonNode?)new JsonObject { ["name"] = name }).ToArray()),
            ["transcode"] = Transcode(call),
            ["noMkdir"] = true,
            ["noUpload"] = true,
            ["noFileModify"] = true,
            ["supportedAudioFiles"] = SupportedFiles(),
            ["vpathMetaData"] = VpathMetadata(vpaths),
            ["discovery"] = config.Has(MstreamFeature.Similar),
            ["discoveryPath"] = config.Has(MstreamFeature.Similar),
        };
        if (config.Has(MstreamFeature.Stats))
            ping["stats"] = StatsVersion;
        return MstreamCall.Json(ping);
    }

    private static async Task<IResult> StatusAsync(MstreamCall call)
    {
        var library = new MstreamLibrary(call);
        var count = await (await library.FilesAsync()).CountAsync(call.Cancellation);
        var visible = LibraryService.Visible(library.Db, call.Access).Select(l => l.Id);
        var scanning = await library.Db.ScanJobs.AnyAsync(j => visible.Contains(j.LibraryId) && j.Status == ScanStatus.Running, call.Cancellation);
        return MstreamCall.Json(new JsonObject { ["totalFileCount"] = count, ["locked"] = scanning });
    }

    private static JsonArray Names(IReadOnlyList<Vpath> vpaths) => new(vpaths.Select(v => (JsonNode?)v.Name).ToArray());

    private static JsonObject VpathMetadata(IReadOnlyList<Vpath> vpaths)
    {
        var metadata = new JsonObject();
        foreach (var vpath in vpaths)
            metadata[vpath.Name] = new JsonObject { ["type"] = "music" };
        return metadata;
    }

    private static JsonObject SupportedFiles()
    {
        var files = new JsonObject();
        foreach (var extension in AudioFormats.Extensions)
            files[extension.TrimStart('.').ToLowerInvariant()] = true;
        files["m3u"] = false;
        return files;
    }

    /// <summary>The default conversion, or false without ffmpeg.</summary>
    private static JsonNode Transcode(MstreamCall call) =>
        DefaultCodec(call) is { } codec
            ? new JsonObject { ["defaultCodec"] = codec.ToString().ToLowerInvariant(), ["defaultBitrate"] = "96k" }
            : JsonValue.Create(false);

    private static TranscodeFormat? DefaultCodec(MstreamCall call)
    {
        var formats = call.Service<ServerInfoService>().Get().TranscodingFormats;
        return formats.Contains(TranscodeFormat.Opus) ? TranscodeFormat.Opus
            : formats.Contains(TranscodeFormat.Mp3) ? TranscodeFormat.Mp3
            : formats.Count > 0 ? formats[0] : null;
    }

    /// <summary>Playlists the user can change: theirs and those they collaborate on.</summary>
    private static async Task<List<Playlist>> EditablePlaylistsAsync(MstreamCall call) =>
        (await call.Service<PlaylistService>().ListAsync(call.Cancellation)).Where(p => p.Role != PlaylistRole.Viewer).ToList();
}
