using System.Text.Json.Nodes;
using Arcanist.Core.Application;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Setup;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Cloud;

/// <summary>The account's plan as Ultimate Cloud last reported it.</summary>
/// <param name="Plan"><c>Standard</c>, <c>Pro</c> or <c>Cloud</c>.</param>
/// <param name="CloudStatus"><c>None</c>, <c>Active</c>, <c>PastDue</c> or <c>PendingCancellation</c>.</param>
public sealed record CloudPlanSnapshot(string Plan, string CloudStatus, bool FeaturesUnlocked, long UsedBytes, long LimitBytes, bool ReadOnly, DateTime? RenewsAt, DateTime CheckedAt);

/// <summary>
/// The Ultimate Cloud account linked to this server: linking it with a device code, its plan (checked every hour,
/// and kept for a week while Ultimate Cloud cannot be reached) and the tier it gives (<see cref="ICloudLinkState"/>).
/// Any linked account uses Ultimate Cloud as a library source; only the <c>Cloud</c> plan unlocks the paid features.
/// </summary>
public sealed class UltimateCloudAccount(
    UltimateCloudAuth auth,
    UltimateCloudClient client,
    UltimateCloudSignals signals,
    SettingsStore settings,
    IArcanistDbContextFactory databases,
    IOptionsMonitor<SetupOptions> setup,
    IOptionsMonitor<UltimateCloudOptions> options,
    DatabaseMaintenance maintenance,
    Extensions.ExtensionHost extensions,
    Hosting.ArcanistPaths paths,
    TimeProvider time,
    ILogger<UltimateCloudAccount> logger) : BackgroundService, ICloudLinkState
{
    private bool? _wasEnabled;

    /// <summary>The Ultimate Cloud extension is on: while it is off, nothing contacts Ultimate Cloud and the tier is none.</summary>
    public bool Enabled => extensions.IsEnabled(Extensions.UltimateCloud.UltimateCloudExtension.Id);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _lock = new();
    private CloudPlanSnapshot? _plan;
    private PendingLink? _pending;

    private sealed record PendingLink(CloudLinkRequest Request, CancellationTokenSource Cancel);

    /// <summary>The last problem: the link expired, Ultimate Cloud cannot be reached, the request was denied…</summary>
    public string? ErrorCode { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.ApiUrl);

    public CloudLinkRequest? Pending => _pending?.Request;

    public CloudPlanSnapshot? Plan => _plan;

    public CloudTier Tier
    {
        get
        {
            if (!Enabled || _plan is not { } plan || ErrorCode == ErrorCodes.CloudLinkExpired
                                      || time.GetUtcNow().UtcDateTime - plan.CheckedAt > options.CurrentValue.OfflineGrace)
                return CloudTier.None;
            return plan.Plan == "Cloud" ? CloudTier.Paid : CloudTier.Free;
        }
    }

    /// <summary>Starts linking: returns the code to approve, and waits for the approval in the background.</summary>
    public async Task<CloudLinkRequest> StartLinkAsync(Guid? linkedBy, CancellationToken cancellationToken)
    {
        if (!Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.CloudDisabled);
        if (!IsConfigured)
            throw new ArcanistException.Conflict(ErrorCodes.CloudNotConfigured);
        CancelLink();
        (CloudLinkRequest Request, string DeviceCode, TimeSpan Interval) started;
        try
        {
            started = await auth.StartAsync(cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            throw new ArcanistException.Conflict(e.Code);
        }

        var cancel = new CancellationTokenSource();
        lock (_lock)
            _pending = new PendingLink(started.Request, cancel);
        ErrorCode = null;
        _ = Task.Run(async () =>
        {
            try
            {
                StoredCloudLink link;
                using (await maintenance.WorkAsync(cancel.Token))
                    link = await auth.WaitForApprovalAsync(started.DeviceCode, started.Interval, started.Request.ExpiresAt, cancel.Token);
                logger.LogInformation("Linked to Ultimate Cloud as {Account}", link.UserName ?? link.Email ?? link.Subject);
                if (linkedBy is { } userId)
                    await auth.SetLinkedByAsync(userId, CancellationToken.None);
                lock (_lock)
                {
                    if (_pending?.Cancel == cancel)
                        _pending = null;
                }

                using (await maintenance.WorkAsync(CancellationToken.None))
                    await AfterLinkAsync(CancellationToken.None);
            }
            catch (UltimateCloudException e)
            {
                ErrorCode = e.Code;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                logger.LogError(e, "Linking to Ultimate Cloud failed");
                ErrorCode = ErrorCodes.CloudUnreachable;
            }
            finally
            {
                lock (_lock)
                {
                    if (_pending?.Cancel == cancel)
                        _pending = null;
                }
            }
        }, CancellationToken.None);
        return started.Request;
    }

    public void CancelLink()
    {
        lock (_lock)
        {
            _pending?.Cancel.Cancel();
            _pending = null;
        }
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken)
    {
        CancelLink();
        await auth.UnlinkAsync(cancellationToken);
        _plan = null;
        ErrorCode = null;
        await settings.SetAsync<CloudPlanSnapshot?>(SettingKeys.UltimateCloudPlan, null, cancellationToken);
        Changed();
    }

    /// <summary>Checks the plan now.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
            return;
        if (await auth.LinkAsync(cancellationToken) is null)
        {
            _plan = null;
            return;
        }

        try
        {
            _plan = ParsePlan(await client.SendAsync(PlanQuery, null, cancellationToken), time);
            ErrorCode = null;
            await settings.SetAsync(SettingKeys.UltimateCloudPlan, _plan, cancellationToken);
            Changed();
        }
        catch (UltimateCloudException e)
        {
            // Unreachable: the last known plan holds for a while. Expired: linking again is needed.
            ErrorCode = e.Code;
            Changed();
            logger.LogWarning("The Ultimate Cloud plan could not be checked: {Error} ({Message})", e.Code, e.Message);
        }
    }

    /// <summary>The account or its plan changed: extensions (Ultimate Cloud Plus) hear it.</summary>
    private void Changed() => signals.Raise();

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <summary>Registers this server as a device of the account and reads its plan.</summary>
    private async Task AfterLinkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.SendAsync(
                """
                mutation Touch($deviceId: String!, $deviceName: String!, $platform: String, $appVersion: String) {
                  touchSyncedDevice(deviceId: $deviceId, deviceName: $deviceName, deviceType: SERVER, platform: $platform, appVersion: $appVersion)
                }
                """,
                new
                {
                    deviceId = await auth.DeviceIdAsync(cancellationToken),
                    deviceName = $"Arcanist ({Environment.MachineName})",
                    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    appVersion = typeof(UltimateCloudAccount).Assembly.GetName().Version?.ToString(3),
                },
                cancellationToken);
        }
        catch (UltimateCloudException e)
        {
            logger.LogWarning("Ultimate Cloud did not register this server as a device: {Error} ({Message})", e.Code, e.Message);
        }

        await RefreshAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Before Ultimate Cloud Plus, the sync component (ultimate-audio) was downloaded here: nothing uses it now.
        try
        {
            var tool = Path.Combine(paths.Root, "tools", "ultimate-audio");
            if (Directory.Exists(tool))
                Directory.Delete(tool, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "The old ultimate-audio folder could not be removed");
        }
        extensions.EnabledChanged += id =>
        {
            if (id == Extensions.UltimateCloud.UltimateCloudExtension.Id)
                Wake();
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (setup.CurrentValue.Completed && databases.IsConfigured)
                {
                    using (await maintenance.WorkAsync(stoppingToken))
                    {
                        _plan ??= await settings.GetAsync<CloudPlanSnapshot?>(SettingKeys.UltimateCloudPlan, null, stoppingToken);
                        var enabled = Enabled;
                        if (enabled && IsConfigured)
                            await RefreshAsync(stoppingToken);
                        else if (!enabled && _wasEnabled != false)
                        {
                            // Switched off: what depends on the account (extensions) hears it.
                            CancelLink();
                            Changed();
                        }

                        _wasEnabled = enabled;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Checking the Ultimate Cloud account failed");
            }

            try
            {
                await _wake.WaitAsync(options.CurrentValue.PlanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Reads the answer to <see cref="PlanQuery"/>.</summary>
    internal static CloudPlanSnapshot ParsePlan(JsonNode data, TimeProvider time)
    {
        var plan = data["myPlan"]!;
        var storage = plan["storage"]!;
        return new CloudPlanSnapshot(
            plan["plan"]!.GetValue<string>(),
            plan["cloudStatus"]!.GetValue<string>(),
            plan["featuresUnlocked"]!.GetValue<bool>(),
            storage["usedBytes"]!.GetValue<long>(),
            storage["limitBytes"]!.GetValue<long>(),
            storage["isReadOnly"]!.GetValue<bool>(),
            plan["renewsAt"]?.GetValue<DateTime>(),
            time.GetUtcNow().UtcDateTime);
    }

    internal const string PlanQuery = """
        query Plan {
          myPlan {
            plan
            featuresUnlocked
            cloudStatus
            renewsAt
            storage { usedBytes limitBytes isReadOnly }
          }
        }
        """;
}
