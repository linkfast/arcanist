namespace Arcanist.Core.P2P;

/// <summary>The URL of Ultimate's iroh relay, as Ultimate Cloud last gave it (Cloud plan); null without one.</summary>
public sealed class P2PUltimateRelay
{
    private volatile string? _url;

    public string? Url
    {
        get => _url;
        set => _url = value;
    }
}
