using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanist.Core.Api.GraphQL;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Artwork;
using Arcanist.Core.Application.Catalog;
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
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.AspNetCore.Identity;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    [DataDirectory.CommandLineSwitch] = DataDirectory.ConfigurationKey,
});

var paths = new ArcanistPaths(DataDirectory.Resolve(builder.Configuration, builder.Environment));
paths.EnsureCreated();
builder.Configuration.AddArcanistSettingsFile(paths);
builder.Services.AddSingleton(paths);

// No-ops unless the process is actually started by the Windows SCM or systemd.
builder.Services.AddWindowsService(options => options.ServiceName = "Arcanist");
builder.Services.AddSystemd();

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
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<StreamService>();
builder.Services.AddScoped<ArtworkService>();
builder.Services.AddScanning(builder.Configuration);
builder.Services.AddSingleton<ServerInfoService>();
builder.Services.AddArcanistRest();
builder.Services.AddArcanistGraphQL(builder.Environment);

var app = builder.Build();

// First, so everything after sees the real client address behind trusted reverse proxies.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Logs the path without the query string, which will carry stream tokens.
app.UseSerilogRequestLogging();

app.UseSetupGate();
app.UseWebSockets();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapArcanistRest();
app.MapArcanistGraphQL();

// Placeholder until the frontend host serves the web player here.
app.MapGet("/", () => "Hello World!").ExcludeFromDescription();

app.Logger.LogInformation("Data directory: {DataDirectory}", paths.Root);

app.Run();
