using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Data;
using Arcanist.Core.P2P;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Arcanist.Core.Extensions.Mstream;

/// <summary>
/// mStream's quick connect: the mStream apps scan a pairing code (<c>mstr1:</c> + base64url of the node's iroh ticket
/// and a 32-byte connect secret) and reach this server through the P2P network, over their own tunnel
/// (<c>mstream/tunnel/2</c>). The secret opens the tunnel; behind it only mStream's routes answer, with their own
/// sign-in. Changing the secret disconnects every app that had it. Needs the P2P network.
/// </summary>
public sealed class MstreamQuickConnect(
    MstreamSettings settings,
    SettingsStore store,
    ExtensionHost extensions,
    P2PNode node,
    IDataProtectionProvider protection) : IAppTunnels
{
    private IDataProtector Protector => protection.CreateProtector("Arcanist.Mstream.QuickConnect");

    private byte[]? _secret;

    public async Task<bool> MstreamAllowedAsync(CancellationToken cancellationToken) =>
        extensions.IsEnabled(MstreamExtension.Id) && (await settings.GetAsync(cancellationToken)).QuickConnect;

    public async Task<string?> MstreamSecretAsync(CancellationToken cancellationToken) =>
        await MstreamAllowedAsync(cancellationToken) ? Convert.ToHexStringLower(await SecretAsync(cancellationToken)) : null;

    /// <summary>
    /// The code the apps scan: null while the quick connect is off or the node is not running (with the new secret:
    /// a code made right after a change would not open the tunnel until the node restarts with it).
    /// </summary>
    public async Task<string?> PairingCodeAsync(CancellationToken cancellationToken)
    {
        if (!await MstreamAllowedAsync(cancellationToken) || node.State is not { Running: true, EndpointTicket: { } ticket })
            return null;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { t = ticket, s = Convert.ToBase64String(await SecretAsync(cancellationToken)) });
        return "mstr1:" + WebEncoders.Base64UrlEncode(payload);
    }

    /// <summary>A new secret: apps that paired with the old one are disconnected and must scan the new code.</summary>
    public async Task RotateAsync(CancellationToken cancellationToken)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        await store.SetAsync(SettingKeys.MstreamQuickConnect, Protector.Protect(Convert.ToBase64String(secret)), cancellationToken);
        _secret = secret;
        node.Changed();
    }

    private async Task<byte[]> SecretAsync(CancellationToken cancellationToken)
    {
        if (_secret is { } secret)
            return secret;
        if (await store.GetAsync<string?>(SettingKeys.MstreamQuickConnect, null, cancellationToken) is { } stored)
        {
            try
            {
                return _secret = Convert.FromBase64String(Protector.Unprotect(stored));
            }
            catch (CryptographicException)
            {
                // Keys from another installation: a new secret follows.
            }
        }

        await RotateAsync(cancellationToken);
        return _secret!;
    }

    /// <summary>The scanned code read back, for tests: the ticket and the secret.</summary>
    internal static (string Ticket, byte[] Secret) Read(string code)
    {
        var json = JsonDocument.Parse(WebEncoders.Base64UrlDecode(code["mstr1:".Length..])).RootElement;
        return (json.GetProperty("t").GetString()!, Convert.FromBase64String(json.GetProperty("s").GetString()!));
    }
}
