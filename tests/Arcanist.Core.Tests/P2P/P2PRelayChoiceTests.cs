using Arcanist.Core.P2P;

namespace Arcanist.Core.Tests.P2P;

/// <summary>The P2P node's relay: an extension's (Ultimate Cloud Plus) replaces n0's public relays, never the administrator's choice.</summary>
public sealed class P2PRelayChoiceTests
{
    private static P2PConfig Config(P2PRelay relay, string? url = null) => P2PConfig.Default with { Enabled = true, Relay = relay, RelayUrl = url };

    [Fact]
    public void An_extensions_relay_replaces_n0s()
    {
        Assert.Equal((P2PRelay.Custom, "https://relay.example.com"),
            P2PNode.ChooseRelay(null, Config(P2PRelay.N0), " https://relay.example.com "));
        Assert.Equal((P2PRelay.N0, (string?)null), P2PNode.ChooseRelay(null, Config(P2PRelay.N0), null));
    }

    [Fact]
    public void The_administrators_relay_and_the_override_stay()
    {
        Assert.Equal((P2PRelay.Custom, "https://own.example.org"),
            P2PNode.ChooseRelay(null, Config(P2PRelay.Custom, "https://own.example.org"), "https://relay.example.com"));
        Assert.Equal((P2PRelay.Direct, (string?)null), P2PNode.ChooseRelay(null, Config(P2PRelay.Direct), "https://relay.example.com"));
        Assert.Equal((P2PRelay.Local, (string?)null), P2PNode.ChooseRelay(P2PRelay.Local, Config(P2PRelay.N0), "https://relay.example.com"));
    }
}
