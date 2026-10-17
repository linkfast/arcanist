using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Dlna;
using Arcanist.Core.Auth;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.ServerAudio;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Application.ServerAudio;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Server audio: whether it plays, on which device, and the devices to choose from.</summary>
/// <param name="Enabled">The server audio extension is on.</param>
/// <param name="Available">The server can play audio (its native player loaded; on Linux it needs ALSA).</param>
/// <param name="UnavailableReason">Why it cannot, for the administrator.</param>
/// <param name="DeviceId">The device chosen; the system's default when null.</param>
/// <param name="Device">The device open now, if any.</param>
/// <param name="DeviceError">Why no device is open (unplugged, taken by another program, none in a container).</param>
/// <param name="Name">The name it shows in "play to" and Ultimate Connect.</param>
/// <param name="CustomName">The name chosen by an administrator; null for the default.</param>
/// <param name="Volume">0–100.</param>
/// <param name="ConnectAccounts">The linked Ultimate Cloud accounts it is an Ultimate Connect device of now.</param>
public sealed record ServerAudioStatus(
    bool Enabled,
    bool Available,
    string? UnavailableReason,
    IReadOnlyList<AudioDevice> Devices,
    string? DeviceId,
    string? Device,
    string? DeviceError,
    string Name,
    string? CustomName,
    int Volume,
    bool Muted,
    int ConnectAccounts);

/// <param name="DeviceId">The device to play on (an id from <c>devices</c>); the system's default when null.</param>
/// <param name="Name">The name it shows; the default when null or empty.</param>
public sealed record ServerAudioSettingsInput(string? DeviceId, string? Name);

/// <summary>
/// Server audio for the API: its settings (administrators, with a recent password) and the server as a device of
/// "play to" (the renderer <see cref="RendererId"/>), for the users with PLAY_ON_SERVER and the administrators. Who
/// starts a queue controls it while it plays (or an administrator), and the plays are theirs.
/// </summary>
public sealed class ServerAudioService(AccessGuard guard, CatalogService catalog, IServiceProvider services)
{
    /// <summary>The server's own output among the renderers of "play to".</summary>
    public const string RendererId = "server";

    public const int NameMaxLength = 64;

    private ServerAudioPlayer Player => services.GetRequiredService<ServerAudioPlayer>();

    public async Task<ServerAudioStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var config = await Player.ConfigAsync(cancellationToken);
        var snapshot = await Player.SnapshotAsync(cancellationToken);
        return new ServerAudioStatus(Player.Enabled, Player.Available, Player.Available ? null : Player.UnavailableReason, Player.Devices(),
            config.DeviceId, snapshot.Device, snapshot.DeviceError, config.DisplayName, config.Name, config.Volume, config.Muted,
            services.GetService<Core.Cloud.ServerAudioConnect>()?.OnlineAccounts ?? 0);
    }

    public async Task<ServerAudioStatus> SetSettingsAsync(ServerAudioSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        guard.RecentAuthentication();
        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        if (name is { Length: > NameMaxLength })
            throw ArcanistException.Field("name", ErrorCodes.InvalidFormat);
        var device = string.IsNullOrWhiteSpace(input.DeviceId) ? null : input.DeviceId;
        var config = await Player.ConfigAsync(cancellationToken);
        await Player.SaveConfigAsync(config with { DeviceId = device, Name = name }, cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Plays a short tone on the device, to check it; refused while a queue plays.</summary>
    public async Task<bool> TestAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        if (!await Player.TestToneAsync(cancellationToken))
            throw new ArcanistException.Conflict(ErrorCodes.RendererInUse);
        return true;
    }

    /// <summary>Whether the caller sees the server among the renderers.</summary>
    internal bool Visible(UserAccess access) => Player.Enabled && Player.Available && CanPlay(access);

    internal DlnaRenderer Renderer() => new(RendererId, Player.DisplayName, "Arcanist", "Server audio", "", false);

    private static bool CanPlay(UserAccess access) => access.IsAdmin || access.Has(Permissions.PlayOnServer);

    private ServerAudioSnapshot Require(UserAccess access, ServerAudioSnapshot? snapshot = null, bool control = false)
    {
        if (!Player.Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.ServerAudioDisabled);
        if (!CanPlay(access))
            throw new ArcanistException.Forbidden();
        // Another user's queue playing is theirs to control (or an administrator's).
        if (control && snapshot is { Active: true } && snapshot.UserId != access.UserId && !access.IsAdmin)
            throw new ArcanistException.Conflict(ErrorCodes.RendererInUse);
        return snapshot!;
    }

    internal async Task<DlnaRendererState> StateAsync(UserAccess access, CancellationToken cancellationToken)
    {
        Require(access);
        var snapshot = await Player.SnapshotAsync(cancellationToken);
        Track? track = null;
        if (snapshot.Current is { } current)
            track = (await catalog.VisibleTracksAsync(access, [current.TrackId], cancellationToken)).GetValueOrDefault(current.TrackId);
        return new DlnaRendererState(
            Renderer(),
            snapshot.State switch
            {
                "Playing" => DlnaTransportState.Playing,
                "Paused" => DlnaTransportState.Paused,
                "Buffering" => DlnaTransportState.Transitioning,
                _ => DlnaTransportState.Stopped,
            },
            track,
            (int)snapshot.PositionMs,
            snapshot.Current?.DurationMs ?? 0,
            snapshot.Volume,
            snapshot.Queue.Select(e => e.TrackId).ToList(),
            snapshot.Index,
            snapshot.UserId == access.UserId);
    }

    internal async Task<DlnaRendererState> PlayAsync(UserAccess access, IReadOnlyList<Guid> trackIds, int? startIndex, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        var queue = await EntriesAsync(access, trackIds, cancellationToken);
        await Player.StartAsync(access.UserId, queue, startIndex ?? 0, 0, cancellationToken);
        return await StateAsync(access, cancellationToken);
    }

    internal async Task<DlnaRendererState> ControlAsync(UserAccess access, DlnaRendererAction action, int? positionMs, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        switch (action)
        {
            case DlnaRendererAction.Play:
                await Player.SetPausedAsync(false, cancellationToken);
                break;
            case DlnaRendererAction.Pause:
                await Player.SetPausedAsync(true, cancellationToken);
                break;
            case DlnaRendererAction.Stop:
                await Player.StopPlaybackAsync(cancellationToken);
                break;
            case DlnaRendererAction.Next:
                await Player.NextAsync(cancellationToken);
                break;
            case DlnaRendererAction.Previous:
                await Player.PreviousAsync(cancellationToken);
                break;
            case DlnaRendererAction.Seek:
                await Player.SeekAsync(positionMs ?? throw ArcanistException.Field("positionMs", ErrorCodes.Required), cancellationToken);
                break;
            default:
                throw ArcanistException.Field("action", ErrorCodes.InvalidFormat);
        }

        return await StateAsync(access, cancellationToken);
    }

    internal async Task<DlnaRendererState> SetVolumeAsync(UserAccess access, int volume, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        await Player.SetVolumeAsync(volume, null, cancellationToken);
        return await StateAsync(access, cancellationToken);
    }

    /// <summary>Mutes or unmutes it; for Ultimate Connect, whose controllers have their own button.</summary>
    internal async Task SetMutedAsync(UserAccess access, bool muted, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        await Player.SetVolumeAsync(null, muted, cancellationToken);
    }

    internal async Task SetShuffleAsync(UserAccess access, bool shuffle, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        await Player.SetShuffleAsync(shuffle, cancellationToken);
    }

    internal async Task SetRepeatAsync(UserAccess access, ServerAudioRepeat repeat, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        await Player.SetRepeatAsync(repeat, cancellationToken);
    }

    internal async Task<DlnaRendererState> EditQueueAsync(UserAccess access, EditDlnaQueueInput input, CancellationToken cancellationToken)
    {
        Require(access, await Player.SnapshotAsync(cancellationToken), control: true);
        if (input.Action == DlnaQueueAction.Jump)
        {
            var snapshot = await Player.SnapshotAsync(cancellationToken);
            if (input.Index is not { } jump || jump < 0 || jump >= snapshot.Queue.Count)
                throw ArcanistException.Field("index", ErrorCodes.InvalidFormat);
            await Player.GoToAsync(jump, cancellationToken);
            return await StateAsync(access, cancellationToken);
        }

        var added = input.Action is DlnaQueueAction.Append or DlnaQueueAction.InsertNext
            ? await EntriesAsync(access, input.TrackIds ?? [], cancellationToken)
            : [];
        string? error = null;
        await Player.EditAsync((queue, index) =>
        {
            switch (input.Action)
            {
                case DlnaQueueAction.Append or DlnaQueueAction.InsertNext:
                    if (queue.Count + added.Count > DlnaService.MaxQueue) { error = "trackIds"; return (false, index); }
                    queue.InsertRange(input.Action == DlnaQueueAction.Append ? queue.Count : Math.Min(index + 1, queue.Count), added);
                    return (true, index);
                case DlnaQueueAction.Remove:
                    if (input.Index is not { } i || i < 0 || i >= queue.Count || i == index) { error = "index"; return (false, index); }
                    queue.RemoveAt(i);
                    return (true, i < index ? index - 1 : index);
                case DlnaQueueAction.Move:
                    if (input.Index is not { } from || from < 0 || from >= queue.Count) { error = "index"; return (false, index); }
                    if (input.ToIndex is not { } to || to < 0 || to >= queue.Count) { error = "toIndex"; return (false, index); }
                    var entry = queue[from];
                    queue.RemoveAt(from);
                    queue.Insert(to, entry);
                    return (true, from == index ? to : from < index && to >= index ? index - 1 : from > index && to <= index ? index + 1 : index);
                default:
                    error = "action";
                    return (false, index);
            }
        }, cancellationToken);
        if (error is not null)
            throw ArcanistException.Field(error, error == "trackIds" ? ErrorCodes.TooMany : ErrorCodes.InvalidFormat);
        return await StateAsync(access, cancellationToken);
    }

    /// <summary>Queue entries for the tracks the caller can see, each with its best copy.</summary>
    internal async Task<List<ServerAudioEntry>> EntriesAsync(UserAccess access, IReadOnlyList<Guid> trackIds, CancellationToken cancellationToken)
    {
        var ids = trackIds.Take(DlnaService.MaxQueue + 1).ToList();
        if (ids.Count > DlnaService.MaxQueue)
            throw ArcanistException.Field("trackIds", ErrorCodes.TooMany);
        var visible = await catalog.VisibleTracksAsync(access, ids.Distinct().ToList(), cancellationToken);
        var entries = ids.Where(visible.ContainsKey).Select(id => visible[id]).Where(t => t.Files.Count > 0)
            .Select(t => new ServerAudioEntry(t.Id, t.Files[0].Id, t.DurationMs)).ToList();
        if (entries.Count == 0)
            throw ArcanistException.Field("trackIds", ErrorCodes.Required);
        return entries;
    }
}
