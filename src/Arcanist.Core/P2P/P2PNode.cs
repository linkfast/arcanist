using System.Collections.Concurrent;
using System.Security.Cryptography;
using Arcanist.Core.Data;
using Arcanist.Core.Hosting;
using Arcanist.Core.Setup;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.P2P;

/// <summary>Bound from <c>Arcanist:P2P</c>, for advanced setups and tests.</summary>
public sealed class P2POptions
{
    public const string Section = "Arcanist:P2P";

    /// <summary>Overrides the relay chosen in the panel (tests use <see cref="P2PRelay.Local"/>).</summary>
    public P2PRelay? Relay { get; set; }

    /// <summary>The UDP port the node binds; any when null.</summary>
    public int? BindPort { get; set; }

    /// <summary>The HTTP port tunnels reach on loopback; by default the first http address the server listens on.</summary>
    public int? Port { get; set; }
}

/// <summary>The P2P settings, kept in the database. Off until an administrator turns them on.</summary>
/// <param name="Name">The name other servers and devices see; null for "Arcanist (machine)".</param>
/// <param name="BandwidthKbps">kbit/s for everything all peers receive together; null for no limit.</param>
/// <param name="MaxStreams">Audio streams all peers may receive at once; null for no limit.</param>
public sealed record P2PConfig(bool Enabled, P2PRelay Relay, string? RelayUrl, string? Name, int? BandwidthKbps, int? MaxStreams)
{
    public static readonly P2PConfig Default = new(false, P2PRelay.N0, null, null, null, null);

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Arcanist ({Environment.MachineName})" : Name.Trim();
}

/// <summary>The settings, cached: every tunnelled request reads the global limits.</summary>
public sealed class P2PSettings(SettingsStore settings, IArcanistDbContextFactory databases, IOptionsMonitor<SetupOptions> setup)
{
    private volatile P2PConfig? _config;

    public P2PConfig Current => _config ?? P2PConfig.Default;

    public async Task<P2PConfig> GetAsync(CancellationToken cancellationToken)
    {
        if (_config is null && setup.CurrentValue.Completed && databases.IsConfigured)
            _config = await settings.GetAsync(SettingKeys.P2P, P2PConfig.Default, cancellationToken);
        return Current;
    }

    public async Task SetAsync(P2PConfig config, CancellationToken cancellationToken)
    {
        await settings.SetAsync(SettingKeys.P2P, config, cancellationToken);
        _config = config;
    }
}

/// <summary>
/// The node's identity: an Ed25519 secret key created once in the data directory's <c>keys/</c>, so the node id
/// (what devices and other servers know this server by) never changes. Also a secret for this process only,
/// which proves to the server that a tunnelled connection comes from its own node.
/// </summary>
public sealed class P2PKeys(ArcanistPaths paths)
{
    private const string FileName = "p2p-node.key";

    private readonly Lazy<byte[]> _secretKey = new(() =>
    {
        var path = Path.Combine(paths.Keys, FileName);
        Directory.CreateDirectory(paths.Keys);
        if (!File.Exists(path))
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
        var key = File.ReadAllBytes(path);
        return key.Length == 32 ? key : throw new InvalidDataException($"{path} is not a P2P key.");
    });

    public ReadOnlySpan<byte> SecretKey => _secretKey.Value;

    public byte[] Preamble { get; } = RandomNumberGenerator.GetBytes(32);
}

/// <summary>What the node is doing, for the panel.</summary>
public sealed record P2PNodeState(bool Running, string? ErrorCode, string? NodeId, NodeTicket? Ticket, string? Relay, bool RelayConnected, IReadOnlyList<string> Connected);

/// <summary>
/// Runs the native node while P2P is on: started once the server listens (tunnels need its HTTP port) and
/// restarted when the settings change. Also opens tunnels to other nodes, one local port per node, reused.
/// </summary>
public sealed class P2PNode(
    P2PSettings settings,
    P2PKeys keys,
    IOptionsMonitor<P2POptions> options,
    IServer server,
    IHostApplicationLifetime lifetime,
    ILogger<P2PNode> logger) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _changed = new(0);
    private readonly ConcurrentDictionary<string, int> _tunnels = new(StringComparer.Ordinal);
    private NativeP2P.Node? _node;
    private NodeConfig? _running;
    private string? _error;

    /// <summary>Applies new settings now.</summary>
    public void Changed()
    {
        if (_changed.CurrentCount == 0)
            _changed.Release();
    }

    public bool IsRunning => _node is not null;

    public P2PNodeState State
    {
        get
        {
            var node = _node;
            if (node is null)
                return new P2PNodeState(false, _error ?? (NativeP2P.IsAvailable ? null : "p2p_unavailable"), null, null, null, false, []);
            try
            {
                var info = node.Info();
                return new P2PNodeState(true, null, info.Id, info.Ticket, info.Relay, info.RelayConnected, info.Connected);
            }
            catch (Exception e) when (e is P2PException or ObjectDisposedException)
            {
                return new P2PNodeState(false, _error, null, null, null, false, []);
            }
        }
    }

    /// <summary>
    /// A local port that reaches <paramref name="ticket"/>'s node through the P2P network: HTTP to
    /// <c>http://127.0.0.1:port</c> is HTTP to that server.
    /// </summary>
    /// <exception cref="P2PException"><c>p2p_disabled</c>, <c>unreachable</c>, <c>invalid_ticket</c>…</exception>
    public async Task<int> TunnelAsync(NodeTicket ticket, CancellationToken cancellationToken)
    {
        if (_tunnels.TryGetValue(ticket.Id, out var port))
            return port;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_tunnels.TryGetValue(ticket.Id, out port))
                return port;
            var node = _node ?? throw new P2PException("p2p_disabled");
            port = await Task.Run(() => node.Connect(ticket, TimeSpan.FromSeconds(30)), cancellationToken);
            _tunnels[ticket.Id] = port;
            return port;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the tunnel to a node, so the next request connects again (with a fresher ticket).</summary>
    public void CloseTunnel(string nodeId)
    {
        if (_tunnels.TryRemove(nodeId, out var port))
            _node?.Disconnect(port);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Tunnels go to the HTTP port, known once the server listens.
        var started = new TaskCompletionSource();
        await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        await using (stoppingToken.Register(() => started.TrySetCanceled()))
        {
            try
            {
                await started.Task;
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ApplyAsync(stoppingToken);
                await _changed.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "The P2P node could not be updated");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            StopNode();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var config = await settings.GetAsync(cancellationToken);
        var wanted = config.Enabled && NativeP2P.IsAvailable ? Desired(config) : null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (wanted == _running && (_node is not null || wanted is null))
                return;

            StopNode();
            _error = config.Enabled && !NativeP2P.IsAvailable ? "p2p_unavailable" : null;
            if (wanted is null)
                return;

            try
            {
                _node = NativeP2P.Node.Start(wanted, keys.SecretKey);
                _running = wanted;
                logger.LogInformation("P2P node {NodeId} started ({Relay} relay)", _node.Info().Id, wanted.Relay);
            }
            catch (P2PException e)
            {
                _error = e.Code.StartsWith("bind_failed", StringComparison.Ordinal) ? "p2p_bind_failed" : e.Code;
                logger.LogWarning("The P2P node could not start: {Error}", e.Code);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private NodeConfig Desired(P2PConfig config)
    {
        var relay = options.CurrentValue.Relay ?? config.Relay;
        return new NodeConfig(relay, relay == P2PRelay.Custom ? config.RelayUrl : null, HttpPort(),
            Convert.ToHexStringLower(keys.Preamble), options.CurrentValue.BindPort);
    }

    /// <summary><see cref="P2POptions.Port"/>, or the first http address the server listens on.</summary>
    private int? HttpPort()
    {
        if (options.CurrentValue.Port is { } configured)
            return configured;
        foreach (var address in server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
        {
            if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                continue;
            var parsable = address.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost");
            if (Uri.TryCreate(parsable, UriKind.Absolute, out var uri))
                return uri.Port;
        }

        return null;
    }

    private void StopNode()
    {
        _tunnels.Clear();
        var node = _node;
        _node = null;
        _running = null;
        if (node is null)
            return;
        node.Dispose();
        logger.LogInformation("P2P node stopped");
    }
}
