using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Dlna;
using Arcanist.Core.Application.Playlists;
using Arcanist.Core.Application.ServerAudio;
using Arcanist.Core.Auth;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.ServerAudio;
using Arcanist.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Track = Arcanist.Core.Application.Catalog.Track;

namespace Arcanist.Core.Cloud;

/// <summary>
/// Server audio as an Ultimate Connect device: while the extension is on and the server can play, it is registered in
/// each linked account (the server's, and the users' own who may play on the server) with its own device id, so the
/// account's apps list it and control it like any other target. Commands arrive on <c>deviceCommands</c> (GraphQL over
/// WebSocket, as <see cref="UltimateCloudEvents"/>) and run as the account's user: the administrator who linked the
/// server's account (or the first), or the user whose own account it is, with the same rules as "play to". The playback
/// state is published on every change and every few seconds while playing, with the last command's id and error.
/// </summary>
internal sealed class ServerAudioConnect(
    UltimateCloudAuth auth,
    UserCloudAccounts accounts,
    UltimateCloudClient client,
    ServerAudioPlayer player,
    UserAccessCache users,
    SettingsStore settings,
    IServiceScopeFactory scopes,
    IArcanistDbContextFactory databases,
    IOptionsMonitor<UltimateCloudOptions> options,
    IOptionsMonitor<SetupOptions> setup,
    ILogger<ServerAudioConnect> logger) : BackgroundService
{
    public const string ClientApp = "arcanist-server-audio";

    internal const string Subscription =
        "subscription { deviceCommands { commandId type positionSeconds volume muted shuffle repeat " +
        "items { cloudTrackId acoustId title artist localTrackId } playlistId startIndex localPlaylistId } }";

    internal static readonly string[] Capabilities =
    [
        "PLAY", "PAUSE", "TOGGLE_PLAY", "NEXT", "PREVIOUS", "SEEK", "SET_VOLUME", "SET_MUTED", "SET_SHUFFLE", "SET_REPEAT",
        "PLAY_ITEMS", "PLAY_PLAYLIST", "ENQUEUE_NEXT", "SKIP_TO_INDEX",
    ];

    /// <summary>Presence expires after 45 s without a heartbeat or a state.</summary>
    internal static TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How often the state is published while playing, for the controllers' position.</summary>
    internal static TimeSpan PlayingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Changes that come together are published once, this long after the first.</summary>
    internal static TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(250);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _online = new();

    private ServerAudioPlayer Player => player;

    private ILogger Logger => logger;

    private UltimateCloudAuth Auth => auth;

    /// <summary>Whether the device is online in an account (a user's own, or the server's with null).</summary>
    public bool IsOnline(Guid? account) => _online.GetValueOrDefault(account ?? Guid.Empty);

    /// <summary>How many accounts it is online in now.</summary>
    public int OnlineAccounts => _online.Count(o => o.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new Dictionary<Guid, (Task Task, CancellationTokenSource Cancel)>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wanted = await WantedAsync(stoppingToken);
                foreach (var (key, listener) in running.Where(r => !wanted.Contains(r.Key) || r.Value.Task.IsCompleted).ToList())
                {
                    await listener.Cancel.CancelAsync();
                    await listener.Task;
                    listener.Cancel.Dispose();
                    running.Remove(key);
                }

                foreach (var key in wanted.Where(k => !running.ContainsKey(k)))
                {
                    var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    running[key] = (RunAsync(key == Guid.Empty ? null : key, cancel.Token), cancel);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var listener in running.Values)
                await listener.Cancel.CancelAsync();
            await Task.WhenAll(running.Values.Select(r => r.Task));
        }
    }

    /// <summary>The accounts the device should be online in now.</summary>
    private async Task<HashSet<Guid>> WantedAsync(CancellationToken cancellationToken)
    {
        var wanted = new HashSet<Guid>();
        if (!setup.CurrentValue.Completed || !databases.IsConfigured || !player.Enabled || !player.Available
            || UltimateCloudEvents.EventsUri(options.CurrentValue) is null)
            return wanted;
        try
        {
            if (accounts.TierOf(null) != CloudTier.None)
                wanted.Add(Guid.Empty);
            foreach (var user in accounts.Linked.Where(u => accounts.TierOf(u) != CloudTier.None))
                if (await users.GetUserAsync(user, cancellationToken) is { IsDisabled: false } access && CanPlay(access))
                    wanted.Add(user);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogDebug(e, "Server audio could not read the linked accounts");
        }

        return wanted;
    }

    private static bool CanPlay(UserAccess access) => access.IsAdmin || access.Has(Permissions.PlayOnServer);

    /// <summary>One account's device, connected again after each end, until it is no longer wanted.</summary>
    private async Task RunAsync(Guid? account, CancellationToken stoppingToken)
    {
        await Task.Yield();
        var wait = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested && UltimateCloudEvents.EventsUri(options.CurrentValue) is { } uri)
        {
            try
            {
                await new Listener(this, account).RunAsync(uri, stoppingToken);
                wait = TimeSpan.FromSeconds(5);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Server audio's Ultimate Connect dropped");
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, TimeSpan.FromMinutes(5).Ticks));
            }
            finally
            {
                _online[account ?? Guid.Empty] = false;
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await GoOfflineAsync(account);
    }

    private async Task GoOfflineAsync(Guid? account)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await SendAsync(account, "mutation { goOfflineForConnect }", null, timeout.Token);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Server audio could not go offline in Ultimate Connect");
        }
    }

    private async Task<string> DeviceIdAsync(CancellationToken cancellationToken) =>
        $"arcanist-audio-{(await player.ConfigAsync(cancellationToken)).ConnectDeviceId:N}";

    /// <summary>A call as the audio device, with an account.</summary>
    private async Task<JsonNode> SendAsync(Guid? account, string query, object? variables, CancellationToken cancellationToken)
    {
        var device = await DeviceIdAsync(cancellationToken);
        using (UltimateCloudClient.As(account))
            return await client.SendAsDeviceAsync(device, ClientApp, query, variables, cancellationToken);
    }

    /// <summary>The user an account's commands run as: its own user, or for the server's, who linked it (or the first administrator).</summary>
    private async Task<UserAccess?> ActingUserAsync(Guid? account, CancellationToken cancellationToken)
    {
        Guid? id = account;
        if (id is null)
        {
            await using var db = databases.CreateDbContext();
            if ((await auth.LinkAsync(cancellationToken))?.LinkedByUserId is { } linkedBy
                && await db.Users.AnyAsync(u => u.Id == linkedBy && !u.IsDisabled, cancellationToken))
                id = linkedBy;
            else
                id = await db.Users.Where(u => u.Role == UserRole.Admin && !u.IsDisabled).OrderBy(u => u.CreatedAt)
                    .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
        }

        return id is { } user && await users.GetUserAsync(user, cancellationToken) is { IsDisabled: false } access ? access : null;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Runs a command; returns the error for <c>lastCommandError</c>, or null.</summary>
    internal async Task<string?> ExecuteAsync(Guid? account, JsonNode command, CancellationToken cancellationToken)
    {
        if (await ActingUserAsync(account, cancellationToken) is not { } access || !CanPlay(access))
            return await MessageAsync(ErrorCodes.Forbidden, cancellationToken);

        await using var scope = scopes.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ServerAudioService>();
        try
        {
            var type = command["type"]?.GetValue<string>();
            switch (type)
            {
                case "PLAY":
                    await service.ControlAsync(access, DlnaRendererAction.Play, null, cancellationToken);
                    break;
                case "PAUSE":
                    await service.ControlAsync(access, DlnaRendererAction.Pause, null, cancellationToken);
                    break;
                case "TOGGLE_PLAY":
                    var playing = (await player.SnapshotAsync(cancellationToken)).State == "Playing";
                    await service.ControlAsync(access, playing ? DlnaRendererAction.Pause : DlnaRendererAction.Play, null, cancellationToken);
                    break;
                case "NEXT":
                    await service.ControlAsync(access, DlnaRendererAction.Next, null, cancellationToken);
                    break;
                case "PREVIOUS":
                    await service.ControlAsync(access, DlnaRendererAction.Previous, null, cancellationToken);
                    break;
                case "SEEK":
                    var seconds = command["positionSeconds"]?.GetValue<double>() ?? 0;
                    await service.ControlAsync(access, DlnaRendererAction.Seek, (int)Math.Round(seconds * 1000), cancellationToken);
                    break;
                case "SET_VOLUME":
                    var volume = command["volume"]?.GetValue<double>() ?? 0;
                    await service.SetVolumeAsync(access, (int)Math.Round(Math.Clamp(volume, 0, 1) * 100), cancellationToken);
                    break;
                case "SET_MUTED":
                    await service.SetMutedAsync(access, command["muted"]?.GetValue<bool>() ?? false, cancellationToken);
                    break;
                case "SET_SHUFFLE":
                    await service.SetShuffleAsync(access, command["shuffle"]?.GetValue<bool>() ?? false, cancellationToken);
                    break;
                case "SET_REPEAT":
                    var repeat = command["repeat"]?.GetValue<string>() switch
                    {
                        "ALL" => ServerAudioRepeat.All,
                        "ONE" => ServerAudioRepeat.One,
                        _ => ServerAudioRepeat.Off,
                    };
                    await service.SetRepeatAsync(access, repeat, cancellationToken);
                    break;
                case "PLAY_ITEMS":
                {
                    var (tracks, start) = await ResolveAsync(scope.ServiceProvider, access, account, command["items"] as JsonArray,
                        command["startIndex"]?.GetValue<int>() ?? 0, cancellationToken);
                    if (tracks.Count == 0)
                        return await MessageAsync(ErrorCodes.NotFound, cancellationToken);
                    await service.PlayAsync(access, tracks, start, cancellationToken);
                    break;
                }
                case "PLAY_PLAYLIST":
                {
                    if (!Guid.TryParse(command["localPlaylistId"]?.GetValue<string>(), out var playlistId))
                        return await MessageAsync("unsupported", cancellationToken);
                    var playlists = scope.ServiceProvider.GetRequiredService<PlaylistService>();
                    var tracks = await playlists.VisibleQuery(access).Where(p => p.Id == playlistId)
                        .SelectMany(p => p.Entries).OrderBy(e => e.Position).Select(e => e.TrackId)
                        .ToListAsync(cancellationToken);
                    var visible = await scope.ServiceProvider.GetRequiredService<CatalogService>().Visible(access)
                        .Where(t => tracks.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
                    var playable = tracks.Where(visible.Contains).ToList();
                    if (playable.Count == 0)
                        return await MessageAsync(ErrorCodes.NotFound, cancellationToken);
                    var start = Math.Clamp(command["startIndex"]?.GetValue<int>() ?? 0, 0, playable.Count - 1);
                    await service.PlayAsync(access, playable, start, cancellationToken);
                    break;
                }
                case "ENQUEUE_NEXT":
                {
                    var (tracks, _) = await ResolveAsync(scope.ServiceProvider, access, account, command["items"] as JsonArray, 0, cancellationToken);
                    if (tracks.Count == 0)
                        return await MessageAsync(ErrorCodes.NotFound, cancellationToken);
                    if ((await player.SnapshotAsync(cancellationToken)).Queue.Count == 0)
                        await service.PlayAsync(access, tracks, 0, cancellationToken);
                    else
                        await service.EditQueueAsync(access, new EditDlnaQueueInput(ServerAudioService.RendererId, DlnaQueueAction.InsertNext, tracks, null, null),
                            cancellationToken);
                    break;
                }
                case "SKIP_TO_INDEX":
                    await service.EditQueueAsync(access, new EditDlnaQueueInput(ServerAudioService.RendererId, DlnaQueueAction.Jump, null,
                        command["startIndex"]?.GetValue<int>(), null), cancellationToken);
                    break;
                default:
                    return await MessageAsync("unsupported", cancellationToken);
            }

            return null;
        }
        catch (ArcanistException e)
        {
            return await MessageAsync(e.Code, cancellationToken);
        }
    }

    /// <summary>
    /// The tracks of this server the items are, in their order (those it does not have are left out), and where the
    /// item at <paramref name="startIndex"/> (or the next one found) is among them. By this server's track id, then the
    /// AcoustID, the song of the account's Ultimate Cloud library, and last title and artist.
    /// </summary>
    private static async Task<(List<Guid> Tracks, int Start)> ResolveAsync(
        IServiceProvider services, UserAccess access, Guid? account, JsonArray? items, int startIndex, CancellationToken cancellationToken)
    {
        var list = items?.OfType<JsonObject>().Take(DlnaService.MaxQueue).ToList() ?? [];
        var catalog = services.GetRequiredService<CatalogService>();
        var visible = catalog.Visible(access);
        var found = new Dictionary<int, Guid>();

        string? Text(JsonObject item, string name) => item[name]?.GetValue<string>() is { Length: > 0 } text ? text : null;

        // This server's own tracks.
        var local = list.Select((item, i) => (i, Id: Guid.TryParse(Text(item, "localTrackId"), out var id) ? id : Guid.Empty))
            .Where(x => x.Id != Guid.Empty).ToList();
        if (local.Count > 0)
        {
            var ids = local.Select(x => x.Id).ToList();
            var known = (await visible.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
            foreach (var (i, id) in local.Where(x => known.Contains(x.Id)))
                found[i] = id;
        }

        // The same recording.
        var byAcoustId = await TrackMatchService.FindAsync(visible, list.Select((item, i) => (item, i))
            .Where(x => !found.ContainsKey(x.i) && Text(x.item, "acoustId") is not null)
            .Select(x => new TrackCandidate(x.i.ToString(), null, Text(x.item, "acoustId"), null, null, null, null, null, null, null))
            .ToList(), cancellationToken);
        foreach (var (key, match) in byAcoustId)
            found[int.Parse(key)] = match.TrackId;

        // The account's own song in Ultimate Cloud, catalogued here from its library (files named by its id).
        var cloud = list.Select((item, i) => (i, Id: Text(item, "cloudTrackId"))).Where(x => !found.ContainsKey(x.i) && x.Id is not null).ToList();
        foreach (var (i, id) in cloud)
        {
            var prefix = id + ".";
            var files = await catalog.VisibleFiles(access)
                .Where(f => f.LibrarySource.Kind == LibrarySourceKind.UltimateCloud && f.RelativePath.StartsWith(prefix))
                .Select(f => new { f.TrackId, f.LibrarySource.Settings })
                .ToListAsync(cancellationToken);
            if (files.FirstOrDefault(f => UserCloudAccounts.UserOf(f.Settings) == account) is { } file)
                found[i] = file.TrackId;
        }

        // Last, the same title by the same artist.
        var byTags = await TrackMatchService.FindAsync(visible, list.Select((item, i) => (item, i))
            .Where(x => !found.ContainsKey(x.i) && Text(x.item, "title") is not null)
            .Select(x => new TrackCandidate(x.i.ToString(), null, null, null, Text(x.item, "title"), Text(x.item, "artist"), null, null, null, null))
            .ToList(), cancellationToken);
        foreach (var (key, match) in byTags)
            found[int.Parse(key)] = match.TrackId;

        var order = found.Keys.Order().ToList();
        var start = order.FindIndex(i => i >= startIndex);
        return (order.Select(i => found[i]).ToList(), Math.Max(0, start));
    }

    /// <summary>What a controller shows for an error, in the server's language.</summary>
    private async Task<string> MessageAsync(string code, CancellationToken cancellationToken)
    {
        var spanish = (await settings.GetAsync<string?>(SettingKeys.DefaultLanguage, null, cancellationToken))?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true;
        return code switch
        {
            ErrorCodes.RendererInUse => spanish
                ? "Otra persona está reproduciendo en este dispositivo."
                : "Someone else is playing on this device.",
            ErrorCodes.NothingQueued => spanish ? "No hay nada en cola." : "Nothing is queued.",
            ErrorCodes.NotFound => spanish
                ? "Este servidor no tiene esas canciones."
                : "This server does not have those songs.",
            ErrorCodes.Forbidden => spanish
                ? "Tu usuario de Arcanist no puede reproducir en el servidor."
                : "Your Arcanist user cannot play on the server.",
            ErrorCodes.ServerAudioDisabled or ErrorCodes.ServerAudioUnavailable => spanish
                ? "El audio del servidor no está disponible."
                : "Server audio is not available.",
            "unsupported" => spanish ? "Este dispositivo no admite esa orden." : "This device does not support that command.",
            _ => spanish ? $"No se pudo ({code})." : $"It could not be done ({code}).",
        };
    }

    // ── State ────────────────────────────────────────────────────────────────

    private static string Status(string state) => state switch
    {
        "Playing" => "PLAYING",
        "Paused" => "PAUSED",
        "Buffering" => "BUFFERING",
        _ => "STOPPED",
    };

    /// <summary>The playback state as Ultimate Connect takes it.</summary>
    private async Task<object> StateAsync(ServerAudioSnapshot snapshot, string? lastCommandId, string? lastCommandError, CancellationToken cancellationToken)
    {
        var upNext = snapshot.Queue.Skip(snapshot.Index + 1).Take(5).ToList();
        var ids = upNext.Select(e => e.TrackId).Append(snapshot.Current?.TrackId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
        Dictionary<Guid, Track> tracks = [];
        if (ids.Count > 0)
        {
            await using var scope = scopes.CreateAsyncScope();
            // Described as the queue's owner sees them (they started it), or as an administrator would.
            var access = snapshot.UserId is { } owner ? await users.GetUserAsync(owner, cancellationToken) : null;
            if (access is not null)
                tracks = await scope.ServiceProvider.GetRequiredService<CatalogService>().VisibleTracksAsync(access, ids, cancellationToken);
        }

        object? Describe(ServerAudioEntry? entry) => entry is not null && tracks.GetValueOrDefault(entry.TrackId) is { } track
            ? new
            {
                title = track.Title,
                artist = string.Join(", ", track.Artists.Where(a => a.Role is ArtistRole.Primary or ArtistRole.Featured).Select(a => a.Name)) is { Length: > 0 } names
                    ? names
                    : null,
                album = track.Album.Title,
                durationSeconds = track.DurationMs / 1000.0,
                acoustId = track.AcoustId?.ToString(),
            }
            : null;

        return new
        {
            status = Status(snapshot.State),
            track = Describe(snapshot.Current),
            positionSeconds = snapshot.PositionMs / 1000.0,
            volume = snapshot.Volume / 100.0,
            muted = snapshot.Muted,
            shuffle = snapshot.Shuffle,
            repeat = snapshot.Repeat switch { ServerAudioRepeat.All => "ALL", ServerAudioRepeat.One => "ONE", _ => "OFF" },
            queueIndex = snapshot.Queue.Count > 0 ? snapshot.Index : (int?)null,
            queueLength = snapshot.Queue.Count,
            upNext = upNext.Select(Describe).OfType<object>().ToList(),
            lastCommandId,
            lastCommandError,
        };
    }

    /// <summary>One connection of the device to one account.</summary>
    private sealed class Listener(ServerAudioConnect connect, Guid? account)
    {
        private readonly SemaphoreSlim _changed = new(0, 1);
        private readonly SemaphoreSlim _publishing = new(1, 1);
        private readonly LinkedList<string> _seen = new();
        private string? _lastCommandId;
        private string? _lastCommandError;
        private string? _published;
        private long _publishedPosition;
        private DateTime _publishedAt;
        private DateTime _contactAt;
        private string? _name;

        private void Signal()
        {
            try
            {
                _changed.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public async Task RunAsync(Uri uri, CancellationToken stoppingToken)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lifetime.CancelAfter(UltimateCloudEvents.Lifetime);
            var cancellationToken = lifetime.Token;

            await TouchAsync(cancellationToken);

            using var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("graphql-transport-ws");
            await socket.ConnectAsync(uri, cancellationToken);
            await UltimateCloudEvents.SendAsync(socket, new
            {
                type = "connection_init",
                payload = new
                {
                    authorization = "Bearer " + await connect.Auth.AccessTokenAsync(cancellationToken, account),
                    deviceId = await connect.DeviceIdAsync(cancellationToken),
                    clientApp = ClientApp,
                },
            }, cancellationToken);

            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(15));
                while ((await UltimateCloudEvents.ReceiveAsync(socket, handshake.Token))?["type"]?.GetValue<string>() is { } type && type != "connection_ack")
                {
                    if (type == "ping")
                        await UltimateCloudEvents.SendAsync(socket, new { type = "pong" }, cancellationToken);
                }
            }

            await UltimateCloudEvents.SendAsync(socket, new { id = "commands", type = "subscribe", payload = new { query = Subscription } }, cancellationToken);
            await HeartbeatAsync(cancellationToken);
            await PublishAsync(force: true, cancellationToken);
            connect._online[account ?? Guid.Empty] = true;
            connect.Logger.LogInformation("Server audio is an Ultimate Connect device ({Account})", account?.ToString() ?? "server");

            var commands = Channel.CreateUnbounded<JsonNode>();
            connect.Player.Changed += Signal;
            var follow = FollowAsync(cancellationToken);
            var run = RunCommandsAsync(commands.Reader, cancellationToken);
            try
            {
                while (await UltimateCloudEvents.ReceiveAsync(socket, cancellationToken) is { } message)
                {
                    switch (message["type"]?.GetValue<string>())
                    {
                        case "ping":
                            await UltimateCloudEvents.SendAsync(socket, new { type = "pong" }, cancellationToken);
                            break;
                        case "next" when (message["payload"]?["data"]?["deviceCommands"]) is { } command:
                            commands.Writer.TryWrite(command.DeepClone());
                            break;
                        case "error":
                        case "complete":
                            connect.Logger.LogInformation("Ultimate Cloud ended server audio's commands: {Message}", message["payload"]?.ToJsonString());
                            return;
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The half hour is over: a new connection follows at once.
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            finally
            {
                connect.Player.Changed -= Signal;
                commands.Writer.TryComplete();
                await lifetime.CancelAsync();
                await Task.WhenAll(follow, run).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        /// <summary>Registers the device in the account, again when its name changes.</summary>
        private async Task TouchAsync(CancellationToken cancellationToken)
        {
            var name = connect.Player.DisplayName;
            await connect.SendAsync(account,
                """
                mutation Touch($deviceId: String!, $deviceName: String!, $platform: String, $appVersion: String) {
                  touchSyncedDevice(deviceId: $deviceId, deviceName: $deviceName, deviceType: SERVER, platform: $platform, appVersion: $appVersion)
                }
                """,
                new
                {
                    deviceId = await connect.DeviceIdAsync(cancellationToken),
                    deviceName = name,
                    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    appVersion = typeof(ServerAudioConnect).Assembly.GetName().Version?.ToString(3),
                },
                cancellationToken);
            _name = name;
        }

        private async Task HeartbeatAsync(CancellationToken cancellationToken)
        {
            await connect.SendAsync(account, "mutation Heartbeat($c: [ConnectCommandType!]!) { connectHeartbeat(capabilities: $c) }",
                new { c = Capabilities }, cancellationToken);
            _contactAt = DateTime.UtcNow;
        }

        private async Task RunCommandsAsync(ChannelReader<JsonNode> commands, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var command in commands.ReadAllAsync(cancellationToken))
                {
                    var id = command["commandId"]?.GetValue<string>();
                    if (id is null || _seen.Contains(id))
                        continue;
                    _seen.AddLast(id);
                    if (_seen.Count > 64)
                        _seen.RemoveFirst();

                    string? error;
                    try
                    {
                        error = await connect.ExecuteAsync(account, command, cancellationToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        connect.Logger.LogWarning(e, "Server audio could not run an Ultimate Connect command");
                        error = await connect.MessageAsync("failed", cancellationToken);
                    }

                    if (error is not null)
                        connect.Logger.LogInformation("Ultimate Connect command {Type} refused: {Error}", command["type"]?.GetValue<string>(), error);
                    (_lastCommandId, _lastCommandError) = (id, error);
                    // Acknowledged at once.
                    await PublishAsync(force: true, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>Publishes changes as they happen, the position while playing, and a heartbeat while idle.</summary>
        private async Task FollowAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (await _changed.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
                        await Task.Delay(Debounce, cancellationToken);
                    try
                    {
                        if (connect.Player.DisplayName != _name)
                            await TouchAsync(cancellationToken);
                        if (!await PublishAsync(force: false, cancellationToken) && DateTime.UtcNow - _contactAt >= HeartbeatInterval)
                            await HeartbeatAsync(cancellationToken);
                    }
                    catch (UltimateCloudException e)
                    {
                        connect.Logger.LogDebug("Server audio could not reach Ultimate Connect: {Error}", e.Code);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        // Tried again in a second; the commands go on meanwhile.
                        connect.Logger.LogWarning(e, "Server audio could not publish its state to Ultimate Connect");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <returns>Whether it was published.</returns>
        private async Task<bool> PublishAsync(bool force, CancellationToken cancellationToken)
        {
            await _publishing.WaitAsync(cancellationToken);
            try
            {
                var snapshot = await connect.Player.SnapshotAsync(cancellationToken);
                var signature = string.Join('|', snapshot.State, snapshot.Current?.TrackId, snapshot.Index, snapshot.Queue.Count, snapshot.Volume,
                    snapshot.Muted, snapshot.Shuffle, snapshot.Repeat, _lastCommandId, _lastCommandError);
                var now = DateTime.UtcNow;
                var playing = snapshot.State == "Playing";
                var expected = _publishedPosition + (playing ? (long)(now - _publishedAt).TotalMilliseconds : 0);
                var due = force
                          || signature != _published
                          || Math.Abs(snapshot.PositionMs - expected) > 2000
                          || (playing && now - _publishedAt >= PlayingInterval)
                          || now - _contactAt >= HeartbeatInterval;
                if (!due)
                    return false;

                var state = await connect.StateAsync(snapshot, _lastCommandId, _lastCommandError, cancellationToken);
                await connect.SendAsync(account, "mutation Publish($s: ConnectPlaybackStateInput!) { publishPlaybackState(state: $s) { status } }",
                    new { s = state }, cancellationToken);
                (_published, _publishedPosition, _publishedAt, _contactAt) = (signature, snapshot.PositionMs, now, now);
                return true;
            }
            finally
            {
                _publishing.Release();
            }
        }
    }
}
