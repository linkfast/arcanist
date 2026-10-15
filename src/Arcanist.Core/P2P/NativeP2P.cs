using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcanist.Core.P2P;

/// <summary>
/// Bindings to <c>native/arcanist-p2p</c> (Rust): <c>arcanist_p2p.dll</c>, <c>libarcanist_p2p.so</c> or
/// <c>libarcanist_p2p.dylib</c> next to the server. Calls block on the library's own runtime.
/// </summary>
internal static partial class NativeP2P
{
    private const string Library = "arcanist_p2p";

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_version")]
    private static partial nint Version();

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_start", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int Start(string config, byte* secretKey, out nint node, out nint error);

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_info")]
    private static partial int Info(nint node, out nint info, out nint error);

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_connect", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Connect(nint node, string ticket, uint timeoutMs, out ushort port, out nint error);

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_disconnect")]
    private static partial int Disconnect(nint node, ushort port);

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_stop")]
    private static partial void Stop(nint node);

    [LibraryImport(Library, EntryPoint = "arcanist_p2p_free_string")]
    private static partial void FreeString(nint text);

    private static string Take(nint text)
    {
        if (text == 0)
            return "";
        try
        {
            return Marshal.PtrToStringUTF8(text) ?? "";
        }
        finally
        {
            FreeString(text);
        }
    }

    /// <summary>Whether the library could be loaded; without it the P2P network is unavailable.</summary>
    public static bool IsAvailable => Availability.Value.Available;

    public static string? LoadError => Availability.Value.Error;

    private static readonly Lazy<(bool Available, string? Error)> Availability = new(() =>
    {
        try
        {
            _ = Marshal.PtrToStringUTF8(Version());
            return (true, null);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return (false, e.Message);
        }
    });

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A running node. Thread-safe; dispose to stop it.</summary>
    public sealed class Node : IDisposable
    {
        private nint _handle;
        private readonly Lock _lock = new();

        private Node(nint handle) => _handle = handle;

        /// <exception cref="P2PException">The node could not start (for example, a bad relay URL).</exception>
        public static unsafe Node Start(NodeConfig config, ReadOnlySpan<byte> secretKey)
        {
            if (secretKey.Length != 32)
                throw new ArgumentException("The secret key must be 32 bytes.", nameof(secretKey));
            fixed (byte* key = secretKey)
            {
                var status = NativeP2P.Start(JsonSerializer.Serialize(config, Json), key, out var handle, out var error);
                return status == 0 ? new Node(handle) : throw new P2PException(Take(error));
            }
        }

        public NodeInfo Info()
        {
            lock (_lock)
            {
                var status = NativeP2P.Info(Handle, out var info, out var error);
                return status == 0
                    ? JsonSerializer.Deserialize<NodeInfo>(Take(info), Json)!
                    : throw new P2PException(Take(error));
            }
        }

        /// <summary>Opens a local port tunnelling to the node in <paramref name="ticket"/>.</summary>
        /// <exception cref="P2PException"><c>unreachable</c>, <c>invalid_ticket</c>, <c>own_ticket</c>…</exception>
        public int Connect(NodeTicket ticket, TimeSpan timeout)
        {
            // Not under the lock: connecting may take seconds, and the library is thread-safe.
            var status = NativeP2P.Connect(Handle, JsonSerializer.Serialize(ticket, Json), (uint)timeout.TotalMilliseconds, out var port, out var error);
            return status == 0 ? port : throw new P2PException(Take(error));
        }

        public bool Disconnect(int port) => NativeP2P.Disconnect(Handle, (ushort)port) == 0;

        private nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(Node));

        public void Dispose()
        {
            nint handle;
            lock (_lock)
            {
                handle = _handle;
                _handle = 0;
            }

            if (handle != 0)
                Stop(handle);
        }
    }
}

/// <summary>A failure of the P2P library, as its short code.</summary>
public sealed class P2PException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>How the node reaches others. Mirrors <c>Relay</c> in <c>lib.rs</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<P2PRelay>))]
public enum P2PRelay
{
    /// <summary>n0's public relays: works behind any router. Traffic is end-to-end encrypted.</summary>
    [JsonStringEnumMemberName("n0")]
    N0,

    /// <summary>The administrator's own iroh relay.</summary>
    [JsonStringEnumMemberName("custom")]
    Custom,

    /// <summary>Direct connections only: no relay (some networks will not connect).</summary>
    [JsonStringEnumMemberName("direct")]
    Direct,

    /// <summary>Nothing outside this network, not even address lookup: for tests and closed networks.</summary>
    [JsonStringEnumMemberName("local")]
    Local,
}

/// <param name="MstreamSecret">With it, the node also speaks the mStream apps' tunnel (quick connect).</param>
internal sealed record NodeConfig(P2PRelay Relay, string? RelayUrl, int? ForwardPort, string? PreambleSecret, int? BindPort, string? MstreamSecret = null);

/// <summary>How to reach a node: its id and its known addresses. Serialized as in tickets.</summary>
public sealed record NodeTicket(string Id, IReadOnlyList<string>? Relays = null, IReadOnlyList<string>? Addrs = null);

internal sealed record NodeInfo(
    string Id,
    NodeTicket Ticket,
    string? Relay,
    bool RelayConnected,
    IReadOnlyList<string> Connected,
    IReadOnlyList<NodeTunnel> Tunnels,
    string? EndpointTicket = null);

internal sealed record NodeTunnel(int Port, string Id);
