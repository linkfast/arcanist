using System.Text.Json;
using Arcanist.Core.Application;

namespace Arcanist.Core.ServerAudio;

/// <summary>An audio output device of the server.</summary>
/// <param name="Id">What identifies it (its name, for the system's audio layer).</param>
/// <param name="IsDefault">The system's default device.</param>
public sealed record AudioDevice(string Id, string Name, bool IsDefault, int? SampleRate, int? Channels);

/// <summary>What the device is doing: <c>idle</c>, <c>buffering</c>, <c>playing</c> or <c>paused</c>.</summary>
/// <param name="Token">The item heard.</param>
/// <param name="Decoding">The item being decoded (the next one, once the current one is decoded).</param>
/// <param name="Queued">The item queued after it.</param>
/// <param name="Errors">Items that could not be played since the last status.</param>
/// <param name="DeviceError">The device failed (unplugged, taken by another program).</param>
internal sealed record SinkStatus(
    string State, ulong? Token, long PositionMs, ulong? Decoding, ulong? Queued, IReadOnlyList<SinkError> Errors, string? DeviceError);

internal sealed record SinkError(ulong Token, string Error);

/// <summary>Something to play: a stream (closed by the sink when done), its extension and its part (a cue track).</summary>
internal sealed record SinkSource(Stream Stream, string? Extension, ulong Token, long StartMs, long EndMs);

/// <summary>The system's audio devices: the native player, or a fake in the tests.</summary>
internal interface IServerAudioOutput
{
    bool IsAvailable { get; }

    /// <summary>Why it is not (the native library or the system's audio could not be loaded).</summary>
    string? UnavailableReason { get; }

    IReadOnlyList<AudioDevice> Devices();

    /// <summary>Opens a device (the default when null or not found).</summary>
    /// <exception cref="ArcanistException"><c>server_audio_unavailable</c>.</exception>
    IServerAudioSink Open(string? deviceId);
}

/// <summary>An open device and its player: a current item, at most one queued after it (with no gap), pause and volume.</summary>
internal interface IServerAudioSink : IDisposable
{
    /// <summary>The device actually opened.</summary>
    string Device { get; }

    /// <summary>Plays now, from a position of the item, dropping what was playing and what was queued.</summary>
    void Play(SinkSource source, long positionMs);

    /// <summary>Queues the item after the current one (replacing the queued one, if it has not started).</summary>
    void Queue(SinkSource source);

    void ClearQueue();

    /// <summary>Seeks the item heard; false when the next one has started decoding (play it again instead).</summary>
    bool Seek(ulong token, long positionMs);

    void SetPaused(bool paused);

    /// <param name="volume">0–1.</param>
    void SetVolume(float volume, bool muted);

    void Stop();

    SinkStatus Status();
}

/// <summary>The native player (<c>native/arcanist-player</c>).</summary>
internal sealed class NativeServerAudioOutput : IServerAudioOutput
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsAvailable => NativePlayer.IsAvailable;

    public string? UnavailableReason => NativePlayer.LoadError;

    public IReadOnlyList<AudioDevice> Devices()
    {
        if (!IsAvailable)
            return [];
        var status = NativePlayer.Devices(out var json);
        var text = NativePlayer.TakeString(json);
        return status == NativePlayer.StatusOk ? JsonSerializer.Deserialize<List<AudioDevice>>(text, Json) ?? [] : [];
    }

    public IServerAudioSink Open(string? deviceId)
    {
        if (!IsAvailable)
            throw new ArcanistException.Conflict(ErrorCodes.ServerAudioUnavailable);
        var status = NativePlayer.Open(deviceId, 500, out var handle, out var json);
        var text = NativePlayer.TakeString(json);
        if (status != NativePlayer.StatusOk || handle == 0)
            throw new ServerAudioDeviceException(text);
        var info = JsonSerializer.Deserialize<JsonElement>(text);
        return new NativeSink(handle, info.TryGetProperty("device", out var device) ? device.GetString() ?? "" : "");
    }

    private sealed unsafe class NativeSink(nint handle, string device) : IServerAudioSink
    {
        private nint _handle = handle;

        public string Device { get; } = device;

        public void Play(SinkSource source, long positionMs) =>
            Hand(source, s => NativePlayer.Play(_handle, (NativePlayer.Source*)s, source.Extension, source.Token, (ulong)source.StartMs, (ulong)source.EndMs, (ulong)positionMs));

        public void Queue(SinkSource source) =>
            Hand(source, s => NativePlayer.Queue(_handle, (NativePlayer.Source*)s, source.Extension, source.Token, (ulong)source.StartMs, (ulong)source.EndMs));

        /// <summary>Gives a source to the library, which releases it when done; one it refuses is released here.</summary>
        private static void Hand(SinkSource source, Func<nint, int> call)
        {
            var native = NativePlayer.SourceFor(source.Stream);
            var status = call((nint)(&native));
            if (status != NativePlayer.StatusOk)
                NativePlayer.Release(native.Context);
        }

        public void ClearQueue() => NativePlayer.ClearQueue(_handle);

        public bool Seek(ulong token, long positionMs) => NativePlayer.Seek(_handle, token, (ulong)Math.Max(0, positionMs)) == NativePlayer.StatusOk;

        public void SetPaused(bool paused) => NativePlayer.Pause(_handle, paused ? 1 : 0);

        public void SetVolume(float volume, bool muted) => NativePlayer.Volume(_handle, Math.Clamp(volume, 0, 1), muted ? 1 : 0);

        public void Stop() => NativePlayer.Stop(_handle);

        public SinkStatus Status()
        {
            NativePlayer.Status(_handle, out var json);
            var status = JsonSerializer.Deserialize<NativeStatus>(NativePlayer.TakeString(json), Json);
            return status is null
                ? new SinkStatus("idle", null, 0, null, null, [], null)
                : new SinkStatus(status.State, status.Token, status.PositionMs, status.Decoding, status.Queued,
                    status.Errors?.Select(e => new SinkError(e.Token, e.Error)).ToList() ?? [], status.DeviceError);
        }

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _handle, 0);
            if (handle != 0)
                NativePlayer.Close(handle);
        }

        private sealed record NativeStatus(string State, ulong? Token, long PositionMs, ulong? Decoding, ulong? Queued, List<NativeError>? Errors, string? DeviceError);

        private sealed record NativeError(ulong Token, string Error);
    }
}

/// <summary>A device could not be opened; the message is the system's.</summary>
internal sealed class ServerAudioDeviceException(string message) : Exception(message);
