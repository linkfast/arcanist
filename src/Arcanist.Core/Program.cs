using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Analysis;
using Arcanist.Core.Api.GraphQL;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Media;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Application.Server;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Application.Users;
using Arcanist.Core.Auth;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Arcanist.Core.Media;
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
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSetup(builder.Configuration);
builder.Services.AddArcanistAuth(builder.Configuration);
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Arcanist"));
builder.Services.AddScoped<AccessGuard>();
builder.Services.AddScoped<UserService>();
builder.Services.AddSingleton<LibraryRules>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<ScanService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<FfmpegService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<DuplicateService>();
builder.Services.AddScoped<StreamService>();
builder.Services.AddScoped<ArtworkService>();
builder.Services.AddScanning(builder.Configuration);
builder.Services.AddMediaTools(builder.Configuration);
builder.Services.AddAnalysis(builder.Configuration);
builder.Services.AddSingleton<ServerInfoService>();
builder.Services.AddArcanistRest();
builder.Services.AddArcanistGraphQL(builder.Environment);
builder.Services.AddFrontends(builder.Configuration);

var app = builder.Build();

// First, so everything after sees the real client address behind trusted reverse proxies.
app.UseForwardedHeaders();
app.UseExceptionHandler();
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
app.MapArcanistGraphQL();

app.UseFrontends();

app.Logger.LogInformation("Data directory: {DataDirectory}", paths.Root);

app.Run();
