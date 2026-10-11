using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanist.Core.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Arcanist.Core.Auth;

/// <summary>
/// Short signed tokens that authorize audio URLs (<c>?st=…</c>), because players such as the HTML audio element
/// cannot send an Authorization header. A token carries the user and session, so signing out or revoking the
/// session invalidates it; it expires after <see cref="Lifetime"/>.
/// </summary>
public sealed class StreamTokens
{
    public const string QueryParameter = "st";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private const int PayloadLength = 16 + 16 + 8;
    private const int MacLength = 16;
    private const string KeyFile = "stream-token.key";

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public StreamTokens(ArcanistPaths paths, TimeProvider time)
    {
        _time = time;
        var path = Path.Combine(paths.Keys, KeyFile);
        if (!File.Exists(path))
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
        _key = File.ReadAllBytes(path);
    }

    public string Create(Guid userId, Guid sessionId)
    {
        Span<byte> token = stackalloc byte[PayloadLength + MacLength];
        userId.TryWriteBytes(token[..16]);
        sessionId.TryWriteBytes(token[16..32]);
        BinaryPrimitives.WriteInt64BigEndian(token[32..40], (_time.GetUtcNow() + Lifetime).ToUnixTimeSeconds());
        HMACSHA256.HashData(_key, token[..PayloadLength])[..MacLength].CopyTo(token[PayloadLength..]);
        return Base64UrlEncoder.Encode(token.ToArray());
    }

    /// <summary>Returns the user and session of a valid, unexpired token. Session state is checked by the caller.</summary>
    public (Guid UserId, Guid SessionId)? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
            return null;

        byte[] bytes;
        try
        {
            bytes = Base64UrlEncoder.DecodeBytes(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length != PayloadLength + MacLength)
            return null;

        var expected = HMACSHA256.HashData(_key, bytes.AsSpan(0, PayloadLength))[..MacLength];
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(PayloadLength)))
            return null;

        var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(32, 8)));
        if (_time.GetUtcNow() >= expires)
            return null;

        return (new Guid(bytes.AsSpan(0, 16)), new Guid(bytes.AsSpan(16, 16)));
    }
}
