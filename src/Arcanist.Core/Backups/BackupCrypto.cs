using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Application;

namespace Arcanist.Core.Backups;

/// <summary>
/// Encrypts the server's part of a backup with a password: AES-256-GCM over 1 MiB records, with a key derived by
/// PBKDF2-SHA256. The file is <c>ARCB1</c>, the iterations and salt, then records of a flag (last or not), a length,
/// the ciphertext and its tag; each record's nonce is its number and its associated data the header, its number and
/// its flag, so records cannot be reordered, dropped or cut off without it showing. A wrong password fails on the
/// first record.
/// </summary>
internal static class BackupCrypto
{
    public const int Iterations = 600_000;
    private const int RecordSize = 1024 * 1024;
    private const int SaltSize = 16;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "ARCB1"u8.ToArray();

    public static async Task EncryptAsync(Stream input, Stream output, string password, CancellationToken cancellationToken, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var header = Header(iterations, salt);
        await output.WriteAsync(header, cancellationToken);
        using var aes = new AesGcm(Key(password, salt, iterations), TagSize);

        var current = new byte[RecordSize];
        var next = new byte[RecordSize];
        var currentLength = await FillAsync(input, current, cancellationToken);
        var cipher = new byte[RecordSize];
        var tag = new byte[TagSize];
        for (long record = 0; ; record++)
        {
            var nextLength = currentLength == RecordSize ? await FillAsync(input, next, cancellationToken) : 0;
            var last = nextLength == 0;
            aes.Encrypt(Nonce(record), current.AsSpan(0, currentLength), cipher.AsSpan(0, currentLength), tag, Associated(header, record, last));

            var prefix = new byte[5];
            prefix[0] = last ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(1), currentLength);
            await output.WriteAsync(prefix, cancellationToken);
            await output.WriteAsync(cipher.AsMemory(0, currentLength), cancellationToken);
            await output.WriteAsync(tag, cancellationToken);
            if (last)
                return;
            (current, next) = (next, current);
            currentLength = nextLength;
        }
    }

    /// <summary>Decrypts into <paramref name="output"/>; a wrong password or damaged file is <c>backup_password_invalid</c>.</summary>
    public static async Task DecryptAsync(Stream input, Stream output, string password, CancellationToken cancellationToken)
    {
        var (header, aes) = await OpenAsync(input, password, cancellationToken);
        using (aes)
        {
            var prefix = new byte[5];
            var cipher = new byte[RecordSize];
            var plain = new byte[RecordSize];
            var tag = new byte[TagSize];
            for (long record = 0; ; record++)
            {
                if (await FillAsync(input, prefix, cancellationToken) != prefix.Length)
                    throw Invalid();
                var last = prefix[0] == 1;
                var length = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(1));
                if (prefix[0] > 1 || length is < 0 or > RecordSize
                    || await FillAsync(input, cipher.AsMemory(0, length), cancellationToken) != length
                    || await FillAsync(input, tag, cancellationToken) != TagSize)
                    throw Invalid();
                try
                {
                    aes.Decrypt(Nonce(record), cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), Associated(header, record, last));
                }
                catch (CryptographicException)
                {
                    throw Invalid();
                }

                await output.WriteAsync(plain.AsMemory(0, length), cancellationToken);
                if (last)
                {
                    // Nothing may follow the last record.
                    if (await input.ReadAsync(new byte[1], cancellationToken) != 0)
                        throw Invalid();
                    return;
                }
            }
        }
    }

    /// <summary>Whether the password opens the file (only its first record is read).</summary>
    public static async Task<bool> CheckAsync(Stream input, string password, CancellationToken cancellationToken)
    {
        try
        {
            var (header, aes) = await OpenAsync(input, password, cancellationToken);
            using (aes)
            {
                var prefix = new byte[5];
                if (await FillAsync(input, prefix, cancellationToken) != prefix.Length)
                    return false;
                var length = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(1));
                if (length is < 0 or > RecordSize)
                    return false;
                var cipher = new byte[length];
                var tag = new byte[TagSize];
                if (await FillAsync(input, cipher, cancellationToken) != length || await FillAsync(input, tag, cancellationToken) != TagSize)
                    return false;
                aes.Decrypt(Nonce(0), cipher, tag, new byte[length], Associated(header, 0, prefix[0] == 1));
                return true;
            }
        }
        catch (Exception e) when (e is CryptographicException or ArcanistException)
        {
            return false;
        }
    }

    private static async Task<(byte[] Header, AesGcm Aes)> OpenAsync(Stream input, string password, CancellationToken cancellationToken)
    {
        var header = new byte[Magic.Length + 4 + SaltSize];
        if (await FillAsync(input, header, cancellationToken) != header.Length || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(Magic.Length));
        if (iterations is < 10_000 or > 10_000_000)
            throw new ArcanistException.Conflict(ErrorCodes.BackupFormatUnsupported);
        var salt = header.AsSpan(Magic.Length + 4, SaltSize).ToArray();
        return (header, new AesGcm(Key(password, salt, iterations), TagSize));
    }

    private static byte[] Header(int iterations, byte[] salt)
    {
        var header = new byte[Magic.Length + 4 + SaltSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length), iterations);
        salt.CopyTo(header, Magic.Length + 4);
        return header;
    }

    private static byte[] Key(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Nonce(long record)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), record);
        return nonce;
    }

    private static byte[] Associated(byte[] header, long record, bool last)
    {
        var data = new byte[header.Length + 9];
        header.CopyTo(data, 0);
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(header.Length), record);
        data[^1] = last ? (byte)1 : (byte)0;
        return data;
    }

    private static Task<int> FillAsync(Stream input, byte[] buffer, CancellationToken cancellationToken) =>
        FillAsync(input, buffer.AsMemory(), cancellationToken);

    private static async Task<int> FillAsync(Stream input, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }

    private static ArcanistException Invalid() => new ArcanistException.Conflict(ErrorCodes.BackupPasswordInvalid);
}
