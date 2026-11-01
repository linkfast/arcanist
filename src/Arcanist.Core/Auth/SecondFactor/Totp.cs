using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Arcanist.Core.Auth.SecondFactor;

/// <summary>The codes of authenticator apps (RFC 6238): HMAC-SHA1, six digits, 30 seconds, one step of tolerance either way.</summary>
public static class Totp
{
    public const int Period = 30;
    private const string Issuer = "Arcanist";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / Period;

    public static string Code(byte[] secret, long step, int digits = 6)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, hash);

        var offset = hash[^1] & 0x0f;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7fffffff;
        return (value % (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');
    }

    /// <summary>The step a code belongs to, near this moment and later than the last one used; null if none.</summary>
    public static long? Match(byte[] secret, string code, DateTimeOffset now, long? lastStep)
    {
        var current = StepAt(now);
        var given = Encoding.ASCII.GetBytes(code);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step > (lastStep ?? long.MinValue) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), given))
                return step;
        }

        return null;
    }

    /// <summary>What authenticator apps show and take by hand.</summary>
    public static string Base32(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            result.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    /// <summary>What the QR code holds.</summary>
    public static string Uri(string base32Secret, string userName) =>
        $"otpauth://totp/{Issuer}:{System.Uri.EscapeDataString(userName)}?secret={base32Secret}&issuer={Issuer}&digits=6&period={Period}";
}
