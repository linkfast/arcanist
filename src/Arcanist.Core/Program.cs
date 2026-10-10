using Arcanist.Core.Capabilities;
using Arcanist.Core.Hosting;
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

builder.Services.AddHealthChecks();
builder.Services.AddCapabilities();

var app = builder.Build();

// Logs the path without the query string, which will carry stream tokens.
app.UseSerilogRequestLogging();

app.MapHealthChecks("/health");
app.MapGet("/", () => "Hello World!");

app.Logger.LogInformation("Data directory: {DataDirectory}", paths.Root);

app.Run();
