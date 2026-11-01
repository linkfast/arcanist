using System.Formats.Cbor;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arcanist.Core.Tests.Auth;

/// <summary>A passkey in software, as a browser and a security key would answer: ES256 and attestation <c>none</c>.</summary>
public sealed class SoftAuthenticator(string origin = "http://localhost")
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _id = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _userHandle = new byte[16];

    /// <summary>Its signature counter: what a cloned key would send backwards.</summary>
    public uint Counter { get; set; } = 1;

    private string RpId => new Uri(origin).Host;

    /// <summary>The browser's answer to <c>navigator.credentials.create</c>.</summary>
    public string Create(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        Convert.FromBase64String(Pad(options.RootElement.GetProperty("user").GetProperty("id").GetString()!)).CopyTo(_userHandle, 0);

        var parameters = _key.ExportParameters(false);
        var publicKey = new CborWriter();
        publicKey.WriteStartMap(5);
        publicKey.WriteInt32(1);
        publicKey.WriteInt32(2);
        publicKey.WriteInt32(3);
        publicKey.WriteInt32(-7);
        publicKey.WriteInt32(-1);
        publicKey.WriteInt32(1);
        publicKey.WriteInt32(-2);
        publicKey.WriteByteString(parameters.Q.X!);
        publicKey.WriteInt32(-3);
        publicKey.WriteByteString(parameters.Q.Y!);
        publicKey.WriteEndMap();

        var length = new[] { (byte)(_id.Length >> 8), (byte)_id.Length };
        byte[] authenticatorData = [.. RpHash(), 0x45, .. CounterBytes(), .. new byte[16], .. length, .. _id, .. publicKey.Encode()];

        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authenticatorData);
        attestation.WriteEndMap();

        return JsonSerializer.Serialize(new
        {
            id = Base64Url(_id),
            rawId = Base64Url(_id),
            type = "public-key",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url(ClientData("webauthn.create", options)),
                attestationObject = Base64Url(attestation.Encode()),
                transports = new[] { "internal" },
            },
        });
    }

    /// <summary>The browser's answer to <c>navigator.credentials.get</c>.</summary>
    public string Get(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var clientData = ClientData("webauthn.get", options);
        byte[] authenticatorData = [.. RpHash(), 0x05, .. CounterBytes()];
        byte[] signed = [.. authenticatorData, .. SHA256.HashData(clientData)];

        return JsonSerializer.Serialize(new
        {
            id = Base64Url(_id),
            rawId = Base64Url(_id),
            type = "public-key",
            clientExtensionResults = new { },
            response = new
            {
                authenticatorData = Base64Url(authenticatorData),
                clientDataJSON = Base64Url(clientData),
                signature = Base64Url(_key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)),
                userHandle = Base64Url(_userHandle),
            },
        });
    }

    private byte[] ClientData(string type, JsonDocument options) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        type,
        challenge = options.RootElement.GetProperty("challenge").GetString(),
        origin,
        crossOrigin = false,
    }));

    private byte[] RpHash() => SHA256.HashData(Encoding.UTF8.GetBytes(RpId));

    private byte[] CounterBytes()
    {
        var bytes = BitConverter.GetBytes(Counter);
        Array.Reverse(bytes);
        return bytes;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Pad(string base64Url)
    {
        var text = base64Url.Replace('-', '+').Replace('_', '/');
        return text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
    }
}
