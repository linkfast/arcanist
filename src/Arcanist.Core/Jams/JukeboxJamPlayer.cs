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

    /// <summary>What a Jam gave its renderer: the entry playing (at its place in the renderer's queue) and the one put after it.</summary>
    private sealed class Feed
    {
        public (Guid EntryId, Guid TrackId)? Current;
        public int CurrentIndex = -1;
        public (Guid EntryId, Guid TrackId)? Next;
    }

    private readonly ConcurrentDictionary<Guid, Feed> _fed = new();
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
        var feed = _fed.GetOrAdd(jam.Id, _ => new Feed());

        // The renderer may already play a queue of the host's (or get one while the Jam goes on): it is kept, and the
        // Jam's songs go right after the one playing, one at a time; the host's own queue goes on when none waits.
        var state = await dlna.RendererAsync(rendererId, cancellationToken);
        var current = Follow(feed, state);

        foreach (var command in commands)
        {
            if (!Active(state))
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
                // To the Jam's first waiting song (a song chosen to play now is first): put after this one if it is not.
                var first = (await jams.WaitingAsync(jam, cancellationToken)).FirstOrDefault(e => e.Id != current);
                if (first is not null && feed.Next?.EntryId != first.Id && HasQueue(state))
                {
                    state = await dlna.EditQueueAsync(new EditDlnaQueueInput(rendererId, DlnaQueueAction.InsertNext, [first.TrackId], null, null), cancellationToken);
                    feed.Next = (first.Id, first.TrackId);
                }
                if (state.QueueIndex + 1 < state.Queue.Count)
                    state = await dlna.ControlAsync(new ControlDlnaRendererInput(rendererId, DlnaRendererAction.Next, null), cancellationToken);
                else
                    state = await dlna.ControlAsync(new ControlDlnaRendererInput(rendererId, DlnaRendererAction.Stop, null), cancellationToken);
                current = Follow(feed, state);
            }
        }

        // An entry taken out of the Jam after it was given to the renderer plays there as any song of the host's.
        var allWaiting = await jams.WaitingAsync(jam, cancellationToken);
        if (feed.Next is { } given && allWaiting.All(e => e.Id != given.EntryId))
            feed.Next = null;
        if (current is { } id && id != jam.CurrentEntryId && allWaiting.All(e => e.Id != id))
        {
            current = null;
            feed.Current = null;
            feed.CurrentIndex = -1;
        }
        var waiting = allWaiting.Where(e => e.Id != current && e.Id != feed.Next?.EntryId).ToList();
        if (!Active(state))
        {
            // Nothing plays there: the next waiting song starts a new queue.
            feed.Next = null;
            if (waiting.FirstOrDefault() is { } next)
            {
                state = await dlna.PlayAsync(new PlayOnDlnaRendererInput(rendererId, [next.TrackId], 0), cancellationToken);
                feed.Current = (next.Id, next.TrackId);
                feed.CurrentIndex = 0;
                current = next.Id;
            }
        }
        else if (HasQueue(state) && feed.Next is null && waiting.FirstOrDefault() is { } following)
        {
            // Right after the one playing (the Jam's or the host's own), to play next without a gap.
            state = await dlna.EditQueueAsync(new EditDlnaQueueInput(rendererId, DlnaQueueAction.InsertNext, [following.TrackId], null, null), cancellationToken);
            feed.Next = (following.Id, following.TrackId);
        }

        // A song of the host's own playing is shown as such; nothing playing is nothing.
        var hostSong = current is null && Active(state) ? JukeboxJamService.HostSong(state.Track) : null;
        var playback = current is null && hostSong is null ? JukeboxJamPlaybackState.Stopped
            : state.State == DlnaTransportState.Paused ? JukeboxJamPlaybackState.Paused
            : JukeboxJamPlaybackState.Playing;
        if (await jams.PlayingAsync(jam, current, hostSong, playback, state.PositionMs, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, JukeboxJamChangeKind.NowPlaying, null, null, time.GetUtcNow().UtcDateTime));
        }
        else if (jam.PositionAt is null || time.GetUtcNow().UtcDateTime - jam.PositionAt > TimeSpan.FromSeconds(10))
            await db.SaveChangesAsync(cancellationToken);
    }

    private static bool Active(DlnaRendererState state) =>
        state.State is DlnaTransportState.Playing or DlnaTransportState.Paused or DlnaTransportState.Transitioning;

    /// <summary>The renderer plays a queue of Arcanist's (songs can be put after the one playing).</summary>
    private static bool HasQueue(DlnaRendererState state) => state.QueueIndex >= 0 && state.QueueIndex < state.Queue.Count;

    /// <summary>
    /// The Jam's entry that plays on the renderer, if any: the one put next once it starts, or the one playing while
    /// it still does (at its place: the host's queue may hold the same song elsewhere).
    /// </summary>
    private static Guid? Follow(Feed feed, DlnaRendererState state)
    {
        if (!Active(state) || !HasQueue(state))
        {
            feed.Current = null;
            feed.CurrentIndex = -1;
            return null;
        }

        var playing = state.Queue[state.QueueIndex];
        if (feed.Next is { } next && next.TrackId == playing && state.QueueIndex != feed.CurrentIndex)
        {
            feed.Current = next;
            feed.CurrentIndex = state.QueueIndex;
            feed.Next = null;
        }
        if (feed.Current is { } current && current.TrackId == playing && state.QueueIndex == feed.CurrentIndex)
            return current.EntryId;

        feed.Current = null;
        feed.CurrentIndex = -1;
        return null;
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
            await JukeboxJamService.EndOrDiscardAsync(db, jam, now, cancellationToken);
            signals.Publish(jam.Id, new JukeboxJamChange(jam.Code, JukeboxJamChangeKind.Ended, null, null, now));
        }
    }
}
