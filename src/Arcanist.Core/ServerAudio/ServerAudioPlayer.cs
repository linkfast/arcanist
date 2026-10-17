using Arcanist.Core.Application;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServerAudioExtension = Arcanist.Core.Extensions.ServerAudio.ServerAudioExtension;

namespace Arcanist.Core.ServerAudio;

/// <param name="DeviceId">The output device; the system's default when null (or when it is gone).</param>
/// <param name="Name">The name it shows in "play to" and Ultimate Connect; <c>Arcanist (machine)</c> when null.</param>
/// <param name="Volume">0–100, kept across restarts.</param>
/// <param name="ConnectDeviceId">Its id as an Ultimate Connect device, created once.</param>
internal sealed record ServerAudioConfig(string? DeviceId, string? Name, int Volume, bool Muted, Guid ConnectDeviceId)
{
    public static ServerAudioConfig Default => new(null, null, 70, false, Guid.Empty);

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Arcanist ({Environment.MachineName})" : Name;
}

internal enum ServerAudioRepeat
{
    Off,
    All,
    One,
}

/// <summary>An entry of the queue: a track and the copy of it that plays.</summary>
internal sealed class ServerAudioEntry(Guid trackId, Guid fileId, int durationMs)
{
    public Guid TrackId { get; } = trackId;

    public Guid FileId { get; } = fileId;

    public int DurationMs { get; } = durationMs;
}

/// <summary>What the server's audio is doing.</summary>
/// <param name="State">Stopped, Buffering, Playing or Paused.</param>
/// <param name="UserId">Who started the queue: they (or an administrator) control it, and the plays are theirs.</param>
/// <param name="Device">The device open; null when none is (see <paramref name="DeviceError"/>).</param>
internal sealed record ServerAudioSnapshot(
    string State,
    Guid? UserId,
    ServerAudioEntry? Current,
    long PositionMs,
    IReadOnlyList<ServerAudioEntry> Queue,
    int Index,
    int Volume,
    bool Muted,
    bool Shuffle,
    ServerAudioRepeat Repeat,
    string? Device,
    string? DeviceError)
{
    /// <summary>Something of the queue is on the device (not stopped nor run out): who started it decides what plays.</summary>
    public bool Active => State is "Playing" or "Paused" or "Buffering";
}

/// <summary>
/// Server audio: one queue played on an audio device of the server, through the native player. It opens the chosen
/// device while the extension is on, hands the player the current item and the next one ahead (so tracks follow with
/// no gap), follows it a few times a second, moves on as items end and records each play for the user who started the
/// queue. Every change goes through one gate; <see cref="Changed"/> tells Ultimate Connect.
/// </summary>
internal sealed class ServerAudioPlayer(
    IServerAudioOutput output,
    Extensions.ExtensionHost extensions,
    SettingsStore settings,
    IArcanistDbContextFactory databases,
    IServiceScopeFactory scopes,
    ILibraryStorageResolver storages,
    Transcoder transcoder,
    DatabaseMaintenance maintenance,
    IOptionsMonitor<Setup.SetupOptions> setup,
    TimeProvider time,
    ILogger<ServerAudioPlayer> logger) : BackgroundService
{
    /// <summary>Formats the native player decodes; the others go through ffmpeg (converted to FLAC on the fly).</summary>
    private static readonly HashSet<string> NativeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "mp3", "mp2", "m4a", "m4b", "mp4", "aac", "wav", "wave", "aif", "aiff", "aifc", "caf", "ogg", "oga", "ape", "mka",
    };

    internal static TimeSpan FollowInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IServerAudioSink? _sink;
    private string? _deviceError;
    private DateTimeOffset _retryAt;
    private ServerAudioConfig? _config;

    private Guid? _userId;
    private List<ServerAudioEntry> _queue = [];
    private List<ServerAudioEntry>? _unshuffled;
    private int _index;
    private bool _stopped = true;
    private bool _paused;
    private bool _shuffle;
    private ServerAudioRepeat _repeat;
    private Playing? _current;
    private Playing? _next;
    private ulong _lastToken;
    private string _state = "Stopped";
    private long _position;

    /// <summary>An item handed to the player.</summary>
    /// <param name="Offset">Where its stream starts in the track (a conversion started at a position).</param>
    private sealed record Playing(ulong Token, ServerAudioEntry Entry, bool Seekable, long Offset);

    /// <summary>Something changed: the queue, the track, the state, the volume or the device.</summary>
    public event Action? Changed;

    public bool Enabled => extensions.IsEnabled(ServerAudioExtension.Id);

    /// <summary>The name it shows (loaded with the settings at the first turn of the loop).</summary>
    public string DisplayName => (_config ?? ServerAudioConfig.Default).DisplayName;

    public bool Available => output.IsAvailable;

    public string? UnavailableReason => output.UnavailableReason;

    public IReadOnlyList<AudioDevice> Devices() => output.IsAvailable ? output.Devices() : [];

    public async Task<ServerAudioConfig> ConfigAsync(CancellationToken cancellationToken)
    {
        if (_config is { } cached)
            return cached;
        var config = await settings.GetAsync(SettingKeys.ServerAudio, ServerAudioConfig.Default, cancellationToken);
        if (config.ConnectDeviceId == Guid.Empty)
        {
            config = config with { ConnectDeviceId = Guid.NewGuid() };
            await settings.SetAsync(SettingKeys.ServerAudio, config, cancellationToken);
        }

        return _config = config;
    }

    /// <summary>Saves the settings; a new device is opened at once (what was playing stops).</summary>
    public async Task SaveConfigAsync(ServerAudioConfig config, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await settings.SetAsync(SettingKeys.ServerAudio, config, cancellationToken);
            var deviceChanged = _config?.DeviceId != config.DeviceId;
            _config = config;
            if (deviceChanged)
            {
                CloseSink();
                _retryAt = default;
            }
            else
            {
                _sink?.SetVolume(config.Volume / 100f, config.Muted);
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    public async Task<ServerAudioSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return Snapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private ServerAudioSnapshot Snapshot()
    {
        var config = _config ?? ServerAudioConfig.Default;
        return new ServerAudioSnapshot(_current is null || _stopped ? "Stopped" : _state, _userId, _current?.Entry ?? (_queue.Count > _index ? _queue[_index] : null),
            _current is null ? 0 : _position, _queue.ToList(), _index, config.Volume, config.Muted, _shuffle, _repeat, _sink?.Device, _deviceError);
    }

    /// <summary>Plays a queue for a user from one of its entries (and a position in it).</summary>
    public Task StartAsync(Guid userId, IReadOnlyList<ServerAudioEntry> queue, int index, long positionMs, CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            _userId = userId;
            _queue = [.. queue];
            _unshuffled = null;
            _shuffle = false;
            if (_queue.Count == 0)
            {
                sink.Stop();
                _current = _next = null;
                _stopped = true;
                return;
            }

            await PlayIndexAsync(sink, Math.Clamp(index, 0, _queue.Count - 1), positionMs, cancellationToken);
        }, cancellationToken);

    public Task GoToAsync(int index, CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            if (index < 0 || index >= _queue.Count)
                return;
            await RecordSkipAsync();
            await PlayIndexAsync(sink, index, 0, cancellationToken);
        }, cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            if (_queue.Count == 0)
                throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
            var next = _index + 1 < _queue.Count ? _index + 1 : _repeat == ServerAudioRepeat.All ? 0 : (int?)null;
            if (next is null)
                return;
            await RecordSkipAsync();
            await PlayIndexAsync(sink, next.Value, 0, cancellationToken);
        }, cancellationToken);

    /// <summary>The previous entry, or the start of this one after its first seconds (as players do).</summary>
    public Task PreviousAsync(CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            if (_queue.Count == 0)
                throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
            var target = _position > 3000 || _index == 0 ? _index : _index - 1;
            await RecordSkipAsync();
            await PlayIndexAsync(sink, target, 0, cancellationToken);
        }, cancellationToken);

    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            if (!paused && (_stopped || _current is null))
            {
                // Play after a stop (or the end): from where the queue was.
                if (_queue.Count == 0)
                    throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
                await PlayIndexAsync(sink, Math.Clamp(_index, 0, _queue.Count - 1), 0, cancellationToken);
                return;
            }

            _paused = paused;
            sink.SetPaused(paused);
            _state = paused ? "Paused" : "Playing";
        }, cancellationToken);

    public Task StopPlaybackAsync(CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            await RecordSkipAsync();
            sink.Stop();
            _current = _next = null;
            _stopped = true;
            _paused = false;
            _position = 0;
        }, cancellationToken);

    public Task SeekAsync(long positionMs, CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var sink = RequireSink();
            if (_current is not { } current)
                throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
            // Never to the very end (half a second before it, or half of a very short track); unknown durations are not clamped.
            var duration = current.Entry.DurationMs;
            positionMs = Math.Clamp(positionMs, 0, duration > 0 ? duration - Math.Min(500, duration / 2) : long.MaxValue);
            if (current.Seekable && current.Offset == 0 && sink.Seek(current.Token, positionMs))
            {
                _position = positionMs;
                return;
            }

            // A conversion (or the next item already decoding): play it again from there.
            await PlayIndexAsync(sink, _index, positionMs, cancellationToken);
        }, cancellationToken);

    public Task SetVolumeAsync(int? volume, bool? muted, CancellationToken cancellationToken) =>
        WithGateAsync(async () =>
        {
            var config = await ConfigAsync(cancellationToken);
            config = config with { Volume = Math.Clamp(volume ?? config.Volume, 0, 100), Muted = muted ?? config.Muted };
            await settings.SetAsync(SettingKeys.ServerAudio, config, cancellationToken);
            _config = config;
            _sink?.SetVolume(config.Volume / 100f, config.Muted);
        }, cancellationToken);

    public Task SetShuffleAsync(bool shuffle, CancellationToken cancellationToken) =>
        WithGateAsync(() =>
        {
            if (shuffle == _shuffle)
                return Task.CompletedTask;
            var current = _index < _queue.Count ? _queue[_index] : null;
            if (shuffle)
            {
                // The entry playing first, the rest in a random order.
                _unshuffled = [.. _queue];
                var rest = _queue.Where(e => e != current).OrderBy(_ => Random.Shared.Next()).ToList();
                _queue = current is null ? rest : [current, .. rest];
            }
            else if (_unshuffled is not null)
            {
                _queue = _unshuffled;
                _unshuffled = null;
            }

            _shuffle = shuffle;
            _index = current is null ? 0 : Math.Max(0, _queue.IndexOf(current));
            ForgetNext();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task SetRepeatAsync(ServerAudioRepeat repeat, CancellationToken cancellationToken) =>
        WithGateAsync(() =>
        {
            _repeat = repeat;
            ForgetNext();
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>
    /// Changes the queue (<paramref name="edit"/> runs under the gate with the queue and the index, and returns false to
    /// change nothing). The entry playing goes on; the next one is handed to the player again if it changed.
    /// </summary>
    public async Task<bool> EditAsync(Func<List<ServerAudioEntry>, int, (bool Changed, int Index)> edit, CancellationToken cancellationToken)
    {
        var changed = false;
        await WithGateAsync(() =>
        {
            if (_queue.Count == 0)
                throw new ArcanistException.Conflict(ErrorCodes.NothingQueued);
            var (done, index) = edit(_queue, _index);
            changed = done;
            if (done)
            {
                _index = index;
                _unshuffled = null;
                ForgetNext();
            }

            return Task.CompletedTask;
        }, cancellationToken);
        return changed;
    }

    /// <summary>Plays a short tone (to check the device) when nothing of a queue is playing; false otherwise.</summary>
    public async Task<bool> TestToneAsync(CancellationToken cancellationToken)
    {
        var played = false;
        await WithGateAsync(() =>
        {
            var sink = RequireSink();
            if (Snapshot().Active)
                return Task.CompletedTask;
            _current = _next = null;
            _stopped = true;
            sink.SetPaused(false);
            sink.Play(new SinkSource(new MemoryStream(Tone()), "wav", ++_lastToken, 0, 0), 0);
            played = true;
            return Task.CompletedTask;
        }, cancellationToken);
        return played;
    }

    private async Task WithGateAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    private IServerAudioSink RequireSink()
    {
        if (!Enabled)
            throw new ArcanistException.Conflict(ErrorCodes.ServerAudioDisabled);
        return _sink ?? throw new ArcanistException.Conflict(ErrorCodes.ServerAudioUnavailable);
    }

    /// <summary>The next item is to be handed to the player again (the queue or the order changed).</summary>
    private void ForgetNext()
    {
        if (_next is null)
            return;
        _sink?.ClearQueue();
        _next = null;
    }

    private int? After(int index) =>
        _repeat == ServerAudioRepeat.One ? index
        : index + 1 < _queue.Count ? index + 1
        : _repeat == ServerAudioRepeat.All && _queue.Count > 0 ? 0
        : null;

    /// <summary>Plays an entry; one that cannot be opened is skipped (a few at most).</summary>
    private async Task PlayIndexAsync(IServerAudioSink sink, int index, long positionMs, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5 && index >= 0 && index < _queue.Count; attempt++)
        {
            var entry = _queue[index];
            try
            {
                var (source, nativePosition, seekable, offset) = await OpenAsync(entry, positionMs, cancellationToken);
                sink.SetPaused(false);
                sink.Play(source, nativePosition);
                _current = new Playing(source.Token, entry, seekable, offset);
                _next = null;
                _index = index;
                _stopped = false;
                _paused = false;
                _state = "Buffering";
                _position = positionMs;
                return;
            }
            catch (Exception e) when (e is ArcanistException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Server audio could not open track {Track}: {Error}", entry.TrackId, e.Message);
                positionMs = 0;
                if (After(index) is not { } following || following == index)
                    break;
                index = following;
            }
        }

        sink.Stop();
        _current = _next = null;
        _stopped = true;
    }

    /// <summary>Opens a copy for the player: the file itself when the player decodes it, or a conversion to FLAC.</summary>
    private async Task<(SinkSource Source, long NativePosition, bool Seekable, long Offset)> OpenAsync(
        ServerAudioEntry entry, long positionMs, CancellationToken cancellationToken)
    {
        MediaFile file;
        await using (var db = databases.CreateDbContext())
        {
            file = await db.MediaFiles.AsNoTracking().Include(f => f.LibrarySource)
                       .SingleOrDefaultAsync(f => f.Id == entry.FileId && f.MissingSince == null, cancellationToken)
                   ?? throw new ArcanistException.NotFound();
        }

        if (file.LibrarySource.Kind == LibrarySourceKind.UltimateCloud
            && !extensions.IsEnabled(Extensions.UltimateCloud.UltimateCloudExtension.Id))
            throw new ArcanistException.Conflict(ErrorCodes.CloudDisabled);

        var storage = storages.For(file.LibrarySource.Kind);
        var location = StorageLocation.From(file.LibrarySource);
        var local = storage.TryGetLocalPath(location, file.RelativePath);
        var extension = Path.GetExtension(file.RelativePath).TrimStart('.');
        var token = ++_lastToken;

        if (NativeFormats.Contains(extension))
        {
            Stream stream = local is not null
                ? new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536)
                : await storage.OpenReadAsync(location, file.RelativePath, cancellationToken);
            return (new SinkSource(stream, extension.ToLowerInvariant(), token, file.StartMs ?? 0, file.EndMs ?? 0), positionMs, stream.CanSeek, 0);
        }

        // Opus, WMA, DSD…: ffmpeg decodes them, from the position asked.
        var segment = file.StartMs is not null;
        var session = await transcoder.StartAsync(
            local,
            () => storage.OpenReadAsync(location, file.RelativePath, cancellationToken),
            TranscodeProfile.For(TranscodeFormat.Flac),
            0,
            ((file.StartMs ?? 0) + positionMs) / 1000.0,
            cancellationToken,
            segment ? Math.Max(0, (file.DurationMs - positionMs) / 1000.0) : null)
            ?? throw new ArcanistException.Conflict(ErrorCodes.TranscodingUnavailable);
        return (new SinkSource(new OwnedStream(session.Output, session), "flac", token, 0, 0), 0, false, positionMs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(FollowInterval, time, stoppingToken);
                if (!databases.IsConfigured || !setup.CurrentValue.Completed)
                    continue;

                var changed = false;
                await _gate.WaitAsync(stoppingToken);
                try
                {
                    changed = await TickAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Server audio failed");
                }
                finally
                {
                    _gate.Release();
                }

                if (changed)
                    Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            CloseSink();
        }
    }

    /// <summary>Opens or closes the device as the extension and the settings say, then follows the player.</summary>
    /// <returns>Whether something changed.</returns>
    private async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        if (!Enabled || !output.IsAvailable)
        {
            if (_sink is null && _current is null)
                return false;
            CloseSink();
            return true;
        }

        var config = await ConfigAsync(cancellationToken);
        if (_sink is null)
        {
            if (time.GetUtcNow() < _retryAt)
                return false;
            try
            {
                _sink = output.Open(config.DeviceId);
                _deviceError = null;
                _sink.SetVolume(config.Volume / 100f, config.Muted);
                logger.LogInformation("Server audio plays on {Device}", _sink.Device);
            }
            catch (Exception e) when (e is ServerAudioDeviceException or ArcanistException)
            {
                _deviceError = e.Message;
                _retryAt = time.GetUtcNow().AddSeconds(30);
                logger.LogWarning("Server audio could not open the device: {Error}", e.Message);
            }

            return true;
        }

        return await FollowAsync(_sink, cancellationToken);
    }

    /// <summary>Notices an item that ended, records it and moves on; hands the player the next item ahead.</summary>
    private async Task<bool> FollowAsync(IServerAudioSink sink, CancellationToken cancellationToken)
    {
        var status = sink.Status();
        var changed = false;

        if (status.DeviceError is { } deviceError)
        {
            // Unplugged or taken: the queue stops; the device (or the default) is opened again shortly.
            logger.LogWarning("Server audio device failed: {Error}", deviceError);
            _deviceError = deviceError;
            CloseSink();
            _retryAt = time.GetUtcNow().AddSeconds(5);
            return true;
        }

        var failed = status.Errors.Select(e => e.Token).ToHashSet();
        foreach (var error in status.Errors)
            logger.LogWarning("Server audio could not play an item: {Error}", error.Error);

        if (_current is { } current && !_stopped)
        {
            if (status.Token != current.Token)
            {
                if (_next is { } next && status.Token == next.Token)
                {
                    // The player moved on by itself, with no gap.
                    if (!failed.Contains(current.Token))
                        await RecordAsync(current.Entry, current.Entry.DurationMs, cancellationToken);
                    _current = next;
                    _next = null;
                    _index = Math.Max(0, _queue.IndexOf(next.Entry));
                    changed = true;
                }
                else if (status.State == "idle")
                {
                    // The item ended (or failed) with nothing ahead: the next entry, or the end of the queue.
                    if (!failed.Contains(current.Token))
                        await RecordAsync(current.Entry, current.Entry.DurationMs, cancellationToken);
                    var following = After(_index);
                    _current = _next = null;
                    if (following is { } index)
                        await PlayIndexAsync(sink, index, 0, cancellationToken);
                    else
                        _stopped = true;
                    changed = true;
                }
            }

            if (_current is { } heard && status.Token == heard.Token)
            {
                _position = heard.Offset + status.PositionMs;
                var state = status.State switch { "playing" => "Playing", "paused" => "Paused", _ => "Buffering" };
                if (state != _state)
                {
                    _state = state;
                    changed = true;
                }
            }

            // The next entry ahead, once the current one is decoding and nothing is queued after it.
            if (_current is { } playing && _next is null && status.Decoding == playing.Token && status.Queued is null && After(_index) is { } ahead)
            {
                var entry = _queue[ahead];
                try
                {
                    var (source, _, seekable, _) = await OpenAsync(entry, 0, cancellationToken);
                    sink.Queue(source);
                    _next = new Playing(source.Token, entry, seekable, 0);
                }
                catch (Exception e) when (e is ArcanistException or IOException or UnauthorizedAccessException)
                {
                    // Tried again when the current one ends.
                    logger.LogDebug("Server audio could not open the next track {Track}: {Error}", entry.TrackId, e.Message);
                }
            }
        }

        return changed;
    }

    /// <summary>Leaving an entry before its end: what was heard counts (or not) as a play or a skip.</summary>
    private Task RecordSkipAsync() =>
        _current is { } current && !_stopped ? RecordAsync(current.Entry, (int)Math.Min(_position, current.Entry.DurationMs), CancellationToken.None) : Task.CompletedTask;

    private async Task RecordAsync(ServerAudioEntry entry, int playedMs, CancellationToken cancellationToken)
    {
        if (_userId is not { } userId || playedMs <= 0)
            return;
        try
        {
            using var work = await maintenance.WorkAsync(cancellationToken);
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<UserAccessCache>().GetUserAsync(userId, cancellationToken) is not { } access)
                return;
            var name = (await ConfigAsync(cancellationToken)).DisplayName;
            await scope.ServiceProvider.GetRequiredService<ListeningService>()
                .RecordPlayAsync(access, new RecordPlayInput(entry.TrackId, playedMs, null, $"Server audio: {name}"), cancellationToken);
        }
        catch (Exception e) when (e is ArcanistException or InvalidOperationException)
        {
            logger.LogDebug("Could not record a play of server audio: {Error}", e.Message);
        }
    }

    private void CloseSink()
    {
        _sink?.Dispose();
        _sink = null;
        _current = _next = null;
        _stopped = true;
        _paused = false;
    }

    /// <summary>A second and a half of a soft chime (A5 fading out), as a WAV.</summary>
    private static byte[] Tone()
    {
        const int rate = 44100;
        var frames = rate * 3 / 2;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 4);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(rate);
        writer.Write(rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 4);
        for (var i = 0; i < frames; i++)
        {
            var fade = Math.Exp(-3.0 * i / frames);
            var sample = (short)(Math.Sin(2 * Math.PI * 880 * i / rate) * fade * 0.3 * short.MaxValue);
            writer.Write(sample);
            writer.Write(sample);
        }

        writer.Flush();
        return memory.ToArray();
    }
}

/// <summary>A stream that also disposes what produces it (a conversion's ffmpeg process).</summary>
internal sealed class OwnedStream(Stream inner, IAsyncDisposable owner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            // Not waited for: this runs on the player's thread.
            _ = Task.Run(async () => await owner.DisposeAsync());
        base.Dispose(disposing);
    }
}
