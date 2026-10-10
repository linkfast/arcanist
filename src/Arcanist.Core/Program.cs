using System.Text.Json.Serialization;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
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
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();

builder.Services.AddCapabilities();
builder.Services.AddArcanistDatabase(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSetup(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Logs the path without the query string, which will carry stream tokens.
app.UseSerilogRequestLogging();

app.UseSetupGate();

app.MapHealthChecks("/health");

var rest = app.MapGroup("/rest/v1");
rest.MapSetupEndpoints();

app.MapGet("/", () => "Hello World!");

app.Logger.LogInformation("Data directory: {DataDirectory}", paths.Root);

app.Run();
