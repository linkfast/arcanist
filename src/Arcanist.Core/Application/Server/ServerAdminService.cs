using Arcanist.Core.Application.Listening;
using Arcanist.Core.Application.Online;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Application.Server;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>What the setup wizard chose, as it is now: the wizard runs again from these.</summary>
/// <param name="DefaultLanguage">The language for new users (BCP 47); null for the browser's.</param>
/// <param name="ShareAnonymousStatistics">Anonymous play counts go to Ultimate Cloud's global statistics.</param>
/// <param name="FfmpegPath">The ffmpeg chosen by hand; null when it is found by itself.</param>
public sealed record ServerConfiguration(
    string? DefaultLanguage,
    RatingMode RatingMode,
    OnlineServicesSettings OnlineServices,
    bool ShareAnonymousStatistics,
    string? FfmpegPath,
    Privacy Privacy);

/// <summary>The setup wizard's answers, run again on a server that is already set up. Libraries are added apart.</summary>
/// <param name="Privacy">Applied first: what it leaves out stays off whatever the rest says.</param>
public sealed record ReconfigureServerInput(
    string? DefaultLanguage,
    RatingMode RatingMode,
    SetOnlineServicesInput OnlineServices,
    bool ShareAnonymousStatistics,
    string? FfmpegPath,
    SetPrivacyInput? Privacy = null);

/// <param name="RemoveAddons">Also the installed players and extensions (with their data) and the downloaded tools
/// (ffmpeg, ultimate-audio).</param>
public sealed record FactoryResetInput(bool RemoveAddons);

/// <param name="RestartRequired">The server restarts to finish (a factory reset): wait for it, then open the setup wizard.</param>
public sealed record ResetResult(bool RestartRequired);

/// <summary>
/// Running the setup wizard again, anonymous statistics, and resetting the server: its settings, its users' data, or
/// everything (a factory reset). Administrators only; changes need a recent authentication. Music files are never
/// touched by any of them.
/// </summary>
public sealed class ServerAdminService(
    ArcanistDbContext db,
    AccessGuard guard,
    SettingsStore settings,
    RatingSettings ratings,
    IServiceProvider services,
    ISettingsFileWriter settingsFile,
    Core.Extensions.ExtensionHost extensions,
    ArcanistPaths paths,
    IOptionsMonitor<DatabaseOptions> database,
    DatabaseMaintenance maintenance,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<ServerAdminService> logger)
{
    public async Task<ServerConfiguration> ConfigurationAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return new ServerConfiguration(
            await settings.GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken),
            await ratings.GetAsync(cancellationToken),
            await settings.GetAsync(SettingKeys.OnlineServices, OnlineServicesSettings.None, cancellationToken),
            await settings.GetAsync(SettingKeys.ShareAnonymousStatistics, false, cancellationToken),
            await settings.GetAsync<string?>(SettingKeys.FfmpegPath, null, cancellationToken),
            await services.GetRequiredService<PrivacySettings>().GetAsync(cancellationToken));
    }

    /// <summary>The setup wizard's answers again: language, ratings, online services, anonymous statistics and ffmpeg.</summary>
    public async Task<ServerConfiguration> ReconfigureAsync(ReconfigureServerInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var language = string.IsNullOrWhiteSpace(input.DefaultLanguage) ? null : input.DefaultLanguage.Trim();
        if (language is { Length: > 35 })
            throw ArcanistException.Field("defaultLanguage", ErrorCodes.InvalidFormat);
        if (!Enum.IsDefined(input.RatingMode))
            throw ArcanistException.Field("ratingMode", ErrorCodes.InvalidFormat);

        await settings.SetAsync(SettingKeys.DefaultLanguage, language, cancellationToken);
        await ratings.SetAsync(input.RatingMode, cancellationToken);
        var privacy = input.Privacy is { } chosen
            ? await services.GetRequiredService<PrivacyService>().SetAsync(chosen, cancellationToken)
            : await services.GetRequiredService<PrivacySettings>().GetAsync(cancellationToken);
        await services.GetRequiredService<OnlineServicesService>().SetAsync(
            privacy.ExternalServices == ExternalServices.All ? input.OnlineServices : new SetOnlineServicesInput(false, false, false, false), cancellationToken);
        await settings.SetAsync(SettingKeys.ShareAnonymousStatistics,
            input.ShareAnonymousStatistics && privacy is { NoTelemetry: false, ExternalServices: not ExternalServices.None }, cancellationToken);
        await services.GetRequiredService<Media.FfmpegService>().SetPathAsync(input.FfmpegPath, cancellationToken);
        WakeReporter();
        return await ConfigurationAsync(cancellationToken);
    }

    /// <summary>Whether anonymous play counts go to Ultimate Cloud's global statistics. Requires MANAGE_SETTINGS.</summary>
    public async Task<bool> SetAnonymousStatisticsAsync(bool enabled, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        if (enabled && !await services.GetRequiredService<PrivacySettings>().TelemetryAllowedAsync(cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.TelemetryDisabled);
        await settings.SetAsync(SettingKeys.ShareAnonymousStatistics, enabled, cancellationToken);
        WakeReporter();
        return enabled;
    }

    public async Task<bool> AnonymousStatisticsAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        return await settings.GetAsync(SettingKeys.ShareAnonymousStatistics, false, cancellationToken);
    }

    /// <summary>
    /// Every server setting back to how a new server starts: ratings, online services, anonymous statistics, ffmpeg,
    /// the default player, DLNA, mStream, the P2P network, Ultimate Cloud's services and which built-in extensions
    /// are on. Libraries, users, their data and the linked Ultimate Cloud account stay.
    /// </summary>
    public async Task<ResetResult> ResetSettingsAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();

        await ratings.SetAsync(RatingSettings.Default, cancellationToken);
        await settings.SetAsync(SettingKeys.OnlineServices, OnlineServicesSettings.None, cancellationToken);
        await settings.SetAsync(SettingKeys.ShareAnonymousStatistics, false, cancellationToken);
        foreach (var key in new[]
                 {
                     SettingKeys.AcoustIdKey, SettingKeys.DefaultLanguage, SettingKeys.AnalysisEnabled, SettingKeys.UltimateCloudServices, SettingKeys.Privacy,
                     SettingKeys.UpdatesEnabled,
                 })
            await db.Settings.Where(s => s.Key == key).ExecuteDeleteAsync(cancellationToken);
        await services.GetRequiredService<Media.FfmpegService>().SetPathAsync(null, cancellationToken);
        await services.GetRequiredService<FrontendCatalog>().ResetDefaultAsync(cancellationToken);
        await services.GetRequiredService<Core.Dlna.DlnaSettings>().SetAsync(Core.Dlna.DlnaConfig.Default, cancellationToken);
        await services.GetRequiredService<Core.Extensions.Mstream.MstreamSettings>().SetAsync(Core.Extensions.Mstream.MstreamConfig.Default, cancellationToken);
        await services.GetRequiredService<Core.P2P.P2PSettings>().SetAsync(Core.P2P.P2PConfig.Default, cancellationToken);
        services.GetRequiredService<Core.P2P.P2PNode>().Changed();
        services.GetRequiredService<Core.Cloud.UltimateCloudAccount>().Wake();

        // Built-in and installed extensions back to their defaults (those set by the environment stay as they are).
        var keys = extensions.Extensions.Select(e => Core.Extensions.ExtensionHost.EnabledKey(e.Manifest.Id))
            .Where(key => !settingsFile.IsSetOutsideSettingsFile(key))
            .ToDictionary(key => key, _ => (object?)null);
        if (keys.Count > 0)
            await settingsFile.SetAsync(keys, cancellationToken);
        foreach (var extension in extensions.Extensions)
            extensions.OnEnabledChanged(extension.Manifest.Id);

        logger.LogWarning("{User} reset the server's settings", guard.Authenticated().UserName);
        return new ResetResult(false);
    }

    /// <summary>
    /// Deletes every user's ratings, plays (and their history and counts) and playlists. Users, libraries and settings
    /// stay. StarTracks in Ultimate Cloud are not touched: they come back on the next sync.
    /// </summary>
    public async Task<ResetResult> ResetUserDataAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PlaylistEntries.ExecuteDeleteAsync(cancellationToken);
        await db.PlaylistCollaborators.ExecuteDeleteAsync(cancellationToken);
        await db.Playlists.ExecuteDeleteAsync(cancellationToken);
        await db.TrackRatings.ExecuteDeleteAsync(cancellationToken);
        await db.TrackPlayStats.ExecuteDeleteAsync(cancellationToken);
        await db.PlayEvents.ExecuteDeleteAsync(cancellationToken);
        // Never "unstar" the account's StarTracks because the stars here were deleted: the next sync starts over.
        await db.Settings.Where(s => s.Key == SettingKeys.UltimateCloudStars
                                     || s.Key.StartsWith(SettingKeys.UltimateCloudUserPrefix) && s.Key.EndsWith(".stars"))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning("{User} deleted every user's ratings, plays and playlists", guard.Authenticated().UserName);
        return new ResetResult(false);
    }

    /// <summary>
    /// Everything back to a freshly installed server: the database (its file, or PostgreSQL's tables), the settings,
    /// the keys and the cache go, the Ultimate Cloud account is unlinked, and the server restarts into the setup wizard.
    /// Music folders and logs stay.
    /// </summary>
    public Task<ResetResult> FactoryResetAsync(FactoryResetInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var user = guard.Authenticated().UserName;
        var account = services.GetRequiredService<Core.Cloud.UltimateCloudAccount>();
        var stop = configuration.GetValue("Arcanist:Hosting:StopAfterFactoryReset", true);

        // In the background: this request is work the maintenance mode would wait for.
        var auth = services.GetRequiredService<Core.Cloud.UltimateCloudAuth>();
        _ = Task.Run(() => RunFactoryResetAsync(input, user, account, auth, stop));
        return Task.FromResult(new ResetResult(true));
    }

    private async Task RunFactoryResetAsync(
        FactoryResetInput input, string user, Core.Cloud.UltimateCloudAccount account, Core.Cloud.UltimateCloudAuth auth, bool stop)
    {
        var cancellationToken = lifetime.ApplicationStopping;
        try
        {
            await account.UnlinkAsync(cancellationToken);
        }
        catch (Exception e) when (e is Core.Cloud.UltimateCloudException or HttpRequestException or ArcanistException)
        {
            logger.LogInformation("The Ultimate Cloud account could not be unlinked before the reset: {Message}", e.Message);
        }

        // The users' own accounts too: their sessions in Ultimate Cloud end with the server's data.
        try
        {
            foreach (var linked in await auth.LinkedUsersAsync(cancellationToken))
                await auth.UnlinkAsync(cancellationToken, linked);
        }
        catch (Exception e) when (e is Core.Cloud.UltimateCloudException or HttpRequestException or ArcanistException)
        {
            logger.LogInformation("The users' Ultimate Cloud accounts could not all be unlinked before the reset: {Message}", e.Message);
        }

        // Nothing else writes from now on; the restart ends it.
        if (!await maintenance.EnterAsync(TimeSpan.FromMinutes(1), cancellationToken))
        {
            logger.LogWarning("The factory reset did not start: work on the server did not finish in time");
            return;
        }

        maintenance.Phase = "factory_reset";
        try
        {
            string? sqliteFile = null;
            var options = database.CurrentValue;
            if (options.Provider == DatabaseProvider.Postgres)
            {
                await using var postgres = ArcanistDbContextFactory.Create(options, paths);
                var tables = postgres.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>().Distinct()
                    .Append("__EFMigrationsHistory").ToList();
                foreach (var table in tables)
#pragma warning disable EF1002 // Table names come from the model, never from input.
                    await postgres.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{table}\" CASCADE", cancellationToken);
#pragma warning restore EF1002
            }
            else if (options.Provider == DatabaseProvider.Sqlite)
            {
                var connection = string.IsNullOrWhiteSpace(options.ConnectionString)
                    ? ArcanistDbContextFactory.DefaultSqliteConnectionString(paths)
                    : options.ConnectionString;
                sqliteFile = Path.GetFullPath(new SqliteConnectionStringBuilder(connection).DataSource);
                SqliteConnection.ClearAllPools();
            }

            await FactoryReset.RequestAsync(paths, new FactoryResetRequest(sqliteFile, input.RemoveAddons, DateTime.UtcNow), cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "The factory reset failed; the server goes on as it was");
            maintenance.Leave();
            return;
        }

        logger.LogWarning("{User} asked for a factory reset; the server restarts to finish it", user);
        if (!stop)
            return;
        // Let the answer go out, then stop: supervisors start the server again (exit code 75).
        await Task.Delay(TimeSpan.FromSeconds(1));
        Environment.ExitCode = FactoryReset.RestartExitCode;
        lifetime.StopApplication();
    }

    /// <summary>
    /// Restarts the server, as installing an extension (or switching an installed one on) needs: it stops with exit
    /// code 75 and its supervisor (the tray, the Windows service, systemd, Docker with a restart policy, Synology's
    /// package) starts it again. Without one it just stops. Administrators only, with a recent authentication.
    /// </summary>
    public ResetResult Restart()
    {
        guard.Admin();
        guard.RecentAuthentication();
        logger.LogWarning("{User} asked the server to restart", guard.Authenticated().UserName);
        if (configuration.GetValue("Arcanist:Hosting:StopToRestart", true))
        {
            _ = Task.Run(async () =>
            {
                // Let the answer go out first.
                await Task.Delay(TimeSpan.FromSeconds(1));
                Environment.ExitCode = FactoryReset.RestartExitCode;
                lifetime.StopApplication();
            });
        }

        return new ResetResult(true);
    }

    private void WakeReporter() => services.GetService<Core.Cloud.UltimateCloudReporter>()?.Wake();
}
