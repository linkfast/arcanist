namespace Arcanist.Core.P2P;

/// <summary>
/// Other apps' tunnels the node also speaks, given by whatever offers them (the mStream extension's quick connect).
/// </summary>
public interface IAppTunnels
{
    /// <summary>The connect secret of mStream's tunnel (hex, 32 bytes) while it is on; null otherwise.</summary>
    Task<string?> MstreamSecretAsync(CancellationToken cancellationToken);

    /// <summary>Whether connections through mStream's tunnel are answered now (they reach only mStream's routes).</summary>
    Task<bool> MstreamAllowedAsync(CancellationToken cancellationToken);
}
