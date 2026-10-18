using Arcanist.Core.Ai;
using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Extensions;
using Arcanist.Core.Extensions.Ai;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Ai;

/// <summary>The AI extension's page in the panel: what the analysis is doing, its settings, analyzing everything again. Administrators.</summary>
public sealed class AiAdminService(ArcanistDbContext db, AccessGuard guard, ExtensionHost extensions, AiSettings settings, AiMonitor monitor)
{
    public async Task<AiStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        return await BuildAsync(cancellationToken);
    }

    public async Task<AiStatus> SetSettingsAsync(SetAiSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        if (input.Threads < 1 || input.Threads > AiConfig.MaxThreads)
            throw ArcanistException.Field("threads", ErrorCodes.InvalidFormat);
        await settings.SetAsync(new AiConfig(input.Threads, input.PauseWhileScanning, input.PauseWhileStreaming, input.OnDemandSources, input.ShareServerStats),
            cancellationToken);
        monitor.RequestRun();
        return await BuildAsync(cancellationToken);
    }

    /// <summary>Analyzes every song again (the current analyses stay, served as stale, until each is redone).</summary>
    public async Task<AiStatus> ReanalyzeAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        await db.TrackAnalyses.ExecuteUpdateAsync(s => s.SetProperty(a => a.AnalysisVersion, 0), cancellationToken);
        monitor.RequestRun();
        return await BuildAsync(cancellationToken);
    }

    /// <summary>The status as it changes (at most once a second).</summary>
    public IObservable<AiStatus> Watch()
    {
        guard.Admin();
        var enabled = () => extensions.IsEnabled(AiExtension.Id);
        return new StatusObservable(monitor, enabled, settings);
    }

    private async Task<AiStatus> BuildAsync(CancellationToken cancellationToken)
    {
        var config = await settings.GetAsync(cancellationToken);
        var snapshot = monitor.Current;
        // Counted here too, so the page shows the library's progress even before the worker runs.
        var progress = DjAudioAnalyzer.IsAvailable ? await TrackAnalysisWorker.ProgressAsync(db, false, cancellationToken) : snapshot.Progress;
        return Status(extensions.IsEnabled(AiExtension.Id), snapshot with { Progress = progress }, config);
    }

    private static AiStatus Status(bool enabled, AiMonitor.Snapshot snapshot, AiConfig config) =>
        new(enabled, enabled ? snapshot.Activity : AiActivity.Disabled, snapshot.Progress, snapshot.SongsPerMinute, snapshot.LastAnalyzedAt, snapshot.Error,
            config.ToModel(), AiConfig.MaxThreads);

    private sealed class StatusObservable(AiMonitor monitor, Func<bool> enabled, AiSettings settings) : IObservable<AiStatus>
    {
        public IDisposable Subscribe(IObserver<AiStatus> observer) => monitor.Subscribe(new Mapper(observer, enabled, settings));

        private sealed class Mapper(IObserver<AiStatus> inner, Func<bool> enabled, AiSettings settings) : IObserver<AiMonitor.Snapshot>
        {
            public void OnNext(AiMonitor.Snapshot value) =>
                inner.OnNext(Status(enabled(), value, settings.GetAsync(CancellationToken.None).GetAwaiter().GetResult()));

            public void OnError(Exception error) => inner.OnError(error);

            public void OnCompleted() => inner.OnCompleted();
        }
    }
}
