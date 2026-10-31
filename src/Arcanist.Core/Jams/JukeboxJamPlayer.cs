using System.Collections.Concurrent;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Dlna;
using Arcanist.Core.Application.Jams;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Jams;

/// <summary>
/// Plays the Jukebox Jams that play on the server's audio or on a DLNA renderer: it gives the renderer the next song
/// (and the one after, to play without a gap), follows what it plays, and does what the host asks (skip, pause). It
/// acts as the host, through "play to", so plays count for them. It also looks after every Jam: a DJ whose host's
/// client is gone for 5 minutes is taken off, and a Jam without activity for 12 hours ends.
/// </summary>
public sealed class JukeboxJamPlayer(
    IServiceScopeFactory scopes,
    JukeboxJamSignals signals,
    DatabaseMaintenance maintenance,
    IArcanistDbContextFactory databases,
    IOptionsMonitor<SetupOptions> setup,
    TimeProvider time,
    ILogger<JukeboxJamPlayer> logger) : BackgroundService
{
    public static readonly TimeSpan DjTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);

    /// <summary>How often renderers are followed (tests make it shorter).</summary>
    public static TimeSpan FollowInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>For each Jam, the entries given to its renderer, in the renderer's queue order.</summary>
    private readonly ConcurrentDictionary<Guid, List<(Guid EntryId, Guid TrackId)>> _fed = new();
    private readonly ConcurrentQueue<(Guid JamId, JukeboxJamAction Action)> _commands = new();
    private readonly SemaphoreSlim _wake = new(0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        signals.Changed += OnChanged;
        try
        {
            var lastLook = DateTime.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!setup.CurrentValue.Completed || !databases.IsConfigured)
                    {
                        await _wake.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
                        continue;
                    }
                    using var work = await maintenance.WorkAsync(stoppingToken);
                    await FollowAsync(stoppingToken);
                    if (time.GetUtcNow().UtcDateTime - lastLook > TimeSpan.FromMinutes(1))
                    {
                        lastLook = time.GetUtcNow().UtcDateTime;
                        await LookAfterAsync(stoppingToken);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Following the Jukebox Jams failed");
                }

                await _wake.WaitAsync(FollowInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            signals.Changed -= OnChanged;
        }
    }

    private void OnChanged(Guid jamId, JukeboxJamChange change)
    {
        if (change.Kind == JukeboxJamChangeKind.Command && change.Command is { } command)
            _commands.Enqueue((jamId, command));
        if (change.Kind is JukeboxJamChangeKind.Queue or JukeboxJamChangeKind.Command or JukeboxJamChangeKind.Settings or JukeboxJamChangeKind.Ended)
            _wake.Release();
    }

    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
        var jams = await db.JamSessions.Include(j => j.Playlist)
            .Where(j => j.EndedAt == null && j.Target != JamTarget.Client && j.RendererId != null).ToListAsync(cancellationToken);
        foreach (var gone in _fed.Keys.Except(jams.Select(j => j.Id)))
            _fed.TryRemove(gone, out _);

        var commands = new List<(Guid JamId, JukeboxJamAction Action)>();
        while (_commands.TryDequeue(out var command))
            commands.Add(command);

        foreach (var jam in jams)
        {
            try
            {
                await FollowAsync(scope.ServiceProvider, db, jam, commands.Where(c => c.JamId == jam.Id).Select(c => c.Action).ToList(), cancellationToken);
            }
            catch (ArcanistException e)
            {
                // The renderer is gone, in use by someone else, or the host may no longer play there: tried again later.
                logger.LogDebug("Jukebox Jam {Code} cannot play on {Renderer}: {Code2}", jam.Code, jam.RendererId, e.Code);
            }
        }
    }

    private async Task FollowAsync(IServiceProvider services, ArcanistDbContext db, JamSession jam, List<JukeboxJamAction> commands,
        CancellationToken cancellationToken)
    {
        var host = await services.GetRequiredService<UserAccessCache>().GetUserAsync(jam.HostUserId, cancellationToken);
        if (host is null || host.IsDisabled)
            return;

        using var acting = HttpCurrentUser.ActAs(host, DateTimeOffset.MinValue);
        var dlna = services.GetRequiredService<DlnaService>();
        var jams = services.GetRequiredService<JukeboxJamService>();
        var rendererId = jam.RendererId!;
        var fed = _fed.GetOrAdd(jam.Id, _ => []);

        var state = await dlna.RendererAsync(rendererId, cancellationToken);
        // Ours while the renderer plays the queue it was given (someone else may have played something there since).
        var ours = fed.Count > 0 && state.Queue.SequenceEqual(fed.Select(f => f.TrackId));
        var playing = ours && state.State is DlnaTransportState.Playing or DlnaTransportState.Paused or DlnaTransportState.Transitioning;
        Guid? current = playing && state.QueueIndex >= 0 && state.QueueIndex < fed.Count ? fed[state.QueueIndex].EntryId : null;

        foreach (var command in commands)
        {
            if (!ours)
                break;
            var action = command switch
            {
                JukeboxJamAction.Pause => DlnaRendererAction.Pause,
                JukeboxJamAction.Resume => DlnaRendererAction.Play,
                _ => (DlnaRendererAction?)null,
            };
            if (action is { } control)
                state = await dlna.ControlAsync(new ControlDlnaRendererInput(rendererId, control, null), cancellationToken);
            else if (command == JukeboxJamAction.Skip)
            {
                // The next one given, or a new queue with the next waiting song.
                if (state.QueueIndex + 1 < state.Queue.Count)
                    state = await dlna.ControlAsync(new ControlDlnaRendererInput(rendererId, DlnaRendererAction.Next, null), cancellationToken);
                else
                {
                    playing = false;
                    current = null;
                    await dlna.ControlAsync(new ControlDlnaRendererInput(rendererId, DlnaRendererAction.Stop, null), cancellationToken);
                }
                if (playing)
                    current = state.QueueIndex >= 0 && state.QueueIndex < fed.Count ? fed[state.QueueIndex].EntryId : null;
            }
        }

        var waiting = (await jams.WaitingAsync(jam, cancellationToken)).Where(e => e.Id != current && fed.All(f => f.EntryId != e.Id || f.EntryId == current)).ToList();
        if (!playing)
        {
            // Nothing of the Jam plays: the next waiting song starts a new queue.
            if (waiting.FirstOrDefault() is { } next)
            {
                state = await dlna.PlayAsync(new PlayOnDlnaRendererInput(rendererId, [next.TrackId], 0), cancellationToken);
                fed.Clear();
                fed.Add((next.Id, next.TrackId));
                current = next.Id;
                playing = true;
            }
        }
        else if (state.QueueIndex == state.Queue.Count - 1 && waiting.FirstOrDefault() is { } following)
        {
            // The last one given plays: the next waiting one goes after it, to play without a gap.
            state = await dlna.EditQueueAsync(new EditDlnaQueueInput(rendererId, DlnaQueueAction.Append, [following.TrackId], null, null), cancellationToken);
            fed.Add((following.Id, following.TrackId));
        }

        var playback = current is null ? JukeboxJamPlaybackState.Stopped
            : state.State == DlnaTransportState.Paused ? JukeboxJamPlaybackState.Paused
            : JukeboxJamPlaybackState.Playing;
        if (await jams.PlayingAsync(jam, current, null, playback, state.PositionMs, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, JukeboxJamChangeKind.NowPlaying, null, null, time.GetUtcNow().UtcDateTime));
        }
        else if (jam.PositionAt is null || time.GetUtcNow().UtcDateTime - jam.PositionAt > TimeSpan.FromSeconds(10))
            await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>DJs of hosts gone for 5 minutes are taken off; Jams without activity for 12 hours end.</summary>
    private async Task LookAfterAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcanistDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        var djs = await db.JamSessions.Where(j => j.EndedAt == null && j.DjKind != JamDj.None && (j.HostSeenAt == null || j.HostSeenAt < now - DjTimeout))
            .ToListAsync(cancellationToken);
        foreach (var jam in djs)
        {
            jam.DjKind = JamDj.None;
            jam.DjName = null;
            jam.DjMessage = null;
            jam.DjSince = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var jam in djs)
            signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, JukeboxJamChangeKind.Dj, null, null, now));

        var idle = await db.JamSessions.Include(j => j.Playlist).Where(j => j.EndedAt == null && j.LastActivityAt < now - IdleTimeout).ToListAsync(cancellationToken);
        foreach (var jam in idle)
        {
            await JukeboxJamService.EndAsync(db, jam, now, cancellationToken);
            signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, JukeboxJamChangeKind.Ended, null, null, now));
        }
    }
}
