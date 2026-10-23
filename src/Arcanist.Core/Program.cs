using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Ai;
using Arcanist.Core.Analysis;
using Arcanist.Core.Api.GraphQL;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Discovery;
using Arcanist.Core.Application.Extensions;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.Mstream;
using Arcanist.Core.ServerAudio;
using Arcanist.Core.Application.Frontends;
using Arcanist.Core.Application.Media;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Application.Organization;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Application.Search;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Backups;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Dlna;
using Arcanist.Core.Hosting;
using Arcanist.Core.Import.Mstream;
using Arcanist.Core.ServerMigration;
using Arcanist.Core.Media;
using Arcanist.Core.Online;
using Arcanist.Core.Organization;
using Arcanist.Core.P2P;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Events;

TextNormalizer.EnsureSupported();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ContentRoot.Resolve(),
});

builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    [DataDirectory.CommandLineSwitch] = DataDirectory.ConfigurationKey,
});

var paths = new ArcanistPaths(DataDirectory.Resolve(builder.Configuration, builder.Environment));
paths.EnsureCreated();
// A factory reset asked for by the previous run: its files go before anything reads them.
FactoryReset.ApplyIfRequested(paths, Console.WriteLine);
PendingRestore.ApplyIfRequested(paths, Console.WriteLine);
builder.Configuration.AddArcanistSettingsFile(paths);
builder.Services.AddSingleton(paths);
// ASP.NET Core creates these keys anyway; keep them with the rest of the state instead of the user profile. Arcanist
// protects nothing with them (tokens have their own key), so they are stored unencrypted like that key, and the
// warning saying so is silenced in appsettings.json.
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Keys, "data-protection")));

// No-ops unless the process is actually started by the Windows SCM or systemd.
builder.Services.AddWindowsService(options => options.ServiceName = "Arcanist");
builder.Services.AddSystemd();
builder.Services.AddSupervision(builder.Configuration);

builder.Services.AddSerilog((_, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(paths.Logs, "arcanist-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

builder.Services.AddProblemDetails();
// Enum values as UPPER_SNAKE_CASE, matching how the same enums appear in the GraphQL schema.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)));
builder.Services.AddHealthChecks();

builder.Services.AddCapabilities();
builder.Services.AddLibraryStorage();
builder.Services.AddArcanistDatabase(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher<User>, ArcanistPasswordHasher>();
builder.Services.AddSetup(builder.Configuration);
builder.Services.AddArcanistAuth(builder.Configuration);
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Arcanist"));
builder.Services.AddScoped<AccessGuard>();
builder.Services.AddScoped<UserService>();
builder.Services.AddSingleton<LibraryRules>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddSingleton<Arcanist.Core.Application.Libraries.LibraryMoves>();
builder.Services.AddSingleton<Arcanist.Core.Application.Libraries.CloudLibraries>();
builder.Services.AddSingleton<Arcanist.Core.Application.Libraries.ManagedLibraries>();
builder.Services.AddScoped<S3LibraryService>();
builder.Services.AddScoped<ScanService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<FfmpegService>();
builder.Services.AddScoped<TranscodingService>();
builder.Services.AddScoped<StreamingDefaultsService>();
builder.Services.AddSingleton<Arcanist.Core.Application.Server.EditionResolver>();
builder.Services.AddScoped<Arcanist.Core.Application.Server.OpenSourceService>();
builder.Services.AddScoped<OnlineServicesService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddSingleton<LyricsTiming>();
builder.Services.AddScoped<LyricsTimingService>();
builder.Services.AddScoped<TrackMatchService>();
builder.Services.AddScoped<Arcanist.Core.Application.Server.ServerAdminService>();
builder.Services.AddScoped<Arcanist.Core.Application.Server.StorageUsageService>();
builder.Services.AddSingleton<Arcanist.Core.Application.Server.StorageUsageCache>();
builder.Services.AddSingleton<Arcanist.Core.Application.Server.PrivacySettings>();
builder.Services.AddScoped<Arcanist.Core.Application.Server.PrivacyService>();
builder.Services.AddSingleton<Arcanist.Core.Application.Listening.ListeningSignals>();
builder.Services.Configure<Arcanist.Core.Application.Libraries.UploadOptions>(builder.Configuration.GetSection("Arcanist:Uploads"));
builder.Services.AddScoped<Arcanist.Core.Application.Libraries.UploadService>();
builder.Services.AddScoped<Arcanist.Core.Application.Libraries.UploadDestinations>();
builder.Services.AddScoped<Arcanist.Core.Application.Uploads.UploadLogService>();
builder.Services.AddSingleton<Arcanist.Core.Application.Libraries.UploadScans>();
builder.Services.AddScoped<Arcanist.Core.Application.Uploads.MyUploadService>();
builder.Services.AddScoped<Arcanist.Core.Application.WishList.WishListService>();
builder.Services.AddScoped<Arcanist.Core.Application.WishList.WishLinkResolver>();
builder.Services.AddSingleton<Arcanist.Core.Application.WishList.WishLinkGate>();
builder.Services.AddScoped<Arcanist.Extensibility.IWishLists, Arcanist.Core.Application.WishList.ExtensionWishLists>();
builder.Services.AddHttpClient(Arcanist.Core.Application.WishList.WishLinkResolver.HttpClientName, client =>
{
    // Pages describe the song (Open Graph) to link previewers: say what Arcanist is, as they do.
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (compatible; Arcanist/{Arcanist.Core.Hosting.ArcanistVersion.Current}; +https://github.com/linkfast/arcanist)");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { MaxAutomaticRedirections = 5 });
builder.Services.AddSingleton<Arcanist.Core.Application.WishList.WishListSignals>();
builder.Services.AddSingleton<Arcanist.Core.Application.WishList.WishListMatcher>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<Arcanist.Core.Application.WishList.WishListMatcher>());
builder.Services.AddScoped<DuplicateService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ListeningService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<Arcanist.Core.Application.Listening.TrackStatsService>();
builder.Services.AddScoped<PlaylistService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<DiscoveryService>();
builder.Services.AddScoped<FrontendService>();
builder.Services.AddScoped<ExtensionService>();
builder.Services.Configure<MarketplaceOptions>(builder.Configuration.GetSection(MarketplaceOptions.Section));
builder.Services.AddSingleton<MarketplaceCatalog>();
builder.Services.AddScoped<ExtensionMarketplaceService>();
builder.Services.AddScoped<PlayerMarketplaceService>();
builder.Services.AddScoped<ExtensionAdminPages>();
builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection(UpdateOptions.Section));
builder.Services.AddHttpClient(UpdateChecker.HttpClientName, client =>
{
    // The GitHub API rejects requests without a user agent.
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Arcanist/{Arcanist.Core.Hosting.ArcanistVersion.Current}");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddScoped<UpdateService>();
builder.Services.AddSingleton<HostPlatform>();
builder.Services.AddScoped<HostService>();
builder.Services.AddHostedService<UpdateCheckWorker>();
builder.Services.AddScoped<AppPasswordService>();
builder.Services.AddHttpClient(FrontendService.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddSingleton<RatingSettings>();
builder.Services.AddHostedService<RatingSettingsWarmup>();
builder.Services.AddHostedService<SearchIndexBackfill>();
builder.Services.AddScoped<StreamService>();
builder.Services.AddScoped<ArtworkService>();
builder.Services.AddScanning(builder.Configuration);
builder.Services.AddMediaTools(builder.Configuration);
builder.Services.AddAnalysis(builder.Configuration);
builder.Services.AddOnlineServices(builder.Configuration);
builder.Services.AddOrganization(builder.Configuration);
builder.Services.AddMstreamImport();
builder.Services.AddArcanistMigration();
builder.Services.AddSingleton<Arcanist.Core.Legal.LegalService>();
builder.Services.AddDlna(builder.Configuration);
builder.Services.AddServerAudio();
builder.Services.AddAi(builder.Configuration);
builder.Services.AddMstream();
builder.Services.AddP2P(builder.Configuration);
builder.Services.AddBackups();
builder.Services.AddUltimateCloud(builder.Configuration);
builder.Services.AddSingleton<ServerInfoService>();
builder.Services.AddArcanistRest();
builder.Services.AddArcanistGraphQL(builder.Environment);
builder.Services.AddFrontends(builder.Configuration);

// Last, so extensions can override services and see everything the server registered.
builder.AddExtensions(paths);

var app = builder.Build();

// Requests through the P2P network come from loopback but are anyone's: they get an untrusted address first.
app.UseP2PGate();
// Then, so everything after sees the real client address behind trusted reverse proxies.
app.UseForwardedHeaders();
app.UseExceptionHandler();
// While the database is migrated, requests wait or get 503; otherwise each one counts as work maintenance waits for.
app.UseMiddleware<MaintenanceMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Logs the path without the query string, which will carry stream tokens. Health checks are polled by launchers
// and monitors every few seconds, so they only appear at Verbose unless they fail.
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
    exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
    : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
    : LogEventLevel.Information);

app.UseSetupGate();
app.UseWebSockets();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapArcanistRest();
app.MapExtensions();
app.MapArcanistGraphQL();

app.UseFrontends();

app.Logger.LogInformation("Data directory: {DataDirectory}", paths.Root);

app.Run();
