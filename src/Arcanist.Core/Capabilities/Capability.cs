namespace Arcanist.Core.Capabilities;

/// <summary>
/// Features that depend on a linked Ultimate Cloud account. Everything that runs locally is always
/// available and therefore has no entry here.
/// </summary>
public enum Capability
{
    /// <summary>Use music stored in Ultimate Cloud as a library source. Any linked account, including free.</summary>
    UltimateCloudSource,

    /// <summary>Keep local libraries in sync with Ultimate Cloud. Paid tier.</summary>
    CloudSync,

    /// <summary>Third-party storage such as S3-compatible buckets as library source or sync target. Paid tier.</summary>
    ThirdPartyStorage,

    /// <summary>Clients reach this server over iroh without pairing, coordinated by Ultimate Cloud. Paid tier.</summary>
    CloudConnect,
}

public enum CloudTier
{
    /// <summary>No Ultimate Cloud account linked.</summary>
    None,
    Free,
    Paid,
}
