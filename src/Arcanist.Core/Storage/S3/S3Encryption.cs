using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Arcanist.Core.Storage.S3;

/// <summary>
/// Client-side encryption of an S3 library with a key its owner keeps (256 bits): the service storing it sees neither
/// the audio nor the names, only how many objects there are, their sizes and the shape of the folders. Format 1:
/// <list type="bullet">
/// <item><b>Names:</b> each path segment is encrypted deterministically (a synthetic IV: the first 12 bytes of an
/// HMAC-SHA256 of the segment, which is also its authenticity check, then AES-256-CTR) and written in base64url, so a
/// path always gives the same key and prefixes (folders) still work.</item>
/// <item><b>Contents:</b> every object has a random key (DEK) and a random 8-byte nonce prefix; its plaintext is cut in
/// blocks of 64 KiB, each sealed with AES-256-GCM (nonce: the prefix and the block number; associated data: the
/// block number and whether it is the last), so a range of the file reads only its blocks and blocks cannot be
/// dropped, reordered or swapped between files.</item>
/// <item><b>The DEK</b> is wrapped with AES-256-GCM under a key derived from the owner's (KEK) and stored, with the
/// nonce prefix, in the object's metadata (<c>x-amz-meta-arcanist-enc</c>): changing the owner's key only rewrites
/// metadata.</item>
/// </list>
/// The owner's key is the only secret: with it (and this documented format) the library can be decrypted anywhere.
/// </summary>
internal sealed class S3Encryption
{
    public const int BlockSize = 64 * 1024;
    public const int TagSize = 16;
    public const string MetadataHeader = "x-amz-meta-arcanist-enc";
    private const string Version = "1";
    private const int SyntheticIvSize = 12;

    private readonly byte[] _nameMac;
    private readonly byte[] _nameCipher;
    private readonly byte[] _keyWrap;
    private readonly byte[] _check;

    public S3Encryption(byte[] ownerKey)
    {
        if (ownerKey.Length != 32)
            throw new ArgumentException("The key must be 256 bits.", nameof(ownerKey));
        _nameMac = HKDF.DeriveKey(HashAlgorithmName.SHA256, ownerKey, 32, info: "arcanist-s3-names-mac-v1"u8.ToArray());
        _nameCipher = HKDF.DeriveKey(HashAlgorithmName.SHA256, ownerKey, 32, info: "arcanist-s3-names-cipher-v1"u8.ToArray());
        _keyWrap = HKDF.DeriveKey(HashAlgorithmName.SHA256, ownerKey, 32, info: "arcanist-s3-key-wrap-v1"u8.ToArray());
        _check = HKDF.DeriveKey(HashAlgorithmName.SHA256, ownerKey, 16, info: "arcanist-s3-key-check-v1"u8.ToArray());
    }

    /// <summary>
    /// What the location's key marker holds for this key: says whether a location was encrypted with it, and nothing
    /// about the key itself.
    /// </summary>
    public string KeyCheck => Version + "." + Base64Url(_check);

    /// <summary>A new owner's key, as the text people keep (base64).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The owner's key from its text: base64 or base64url of 32 bytes; null when it is not one.</summary>
    public static byte[]? ParseKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var normalized = text.Trim().Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        try
        {
            var key = Convert.FromBase64String(normalized);
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ---- Names --------------------------------------------------------------------------------------------------

    /// <summary>A relative path with each segment encrypted.</summary>
    public string EncryptPath(string relativePath) => string.Join('/', relativePath.Split('/').Select(EncryptSegment));

    /// <summary>The relative path of an encrypted key (under the library's prefix); null when it is not one of ours.</summary>
    public string? DecryptPath(string encrypted)
    {
        var segments = encrypted.Split('/');
        var plain = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (DecryptSegment(segments[i]) is not { } segment)
                return null;
            plain[i] = segment;
        }

        return string.Join('/', plain);
    }

    private string EncryptSegment(string segment)
    {
        var plain = Encoding.UTF8.GetBytes(segment);
        var iv = HMACSHA256.HashData(_nameMac, plain).AsSpan(0, SyntheticIvSize).ToArray();
        var output = new byte[SyntheticIvSize + plain.Length];
        iv.CopyTo(output, 0);
        Ctr(_nameCipher, iv, plain, output.AsSpan(SyntheticIvSize));
        return Base64Url(output);
    }

    private string? DecryptSegment(string text)
    {
        if (FromBase64Url(text) is not { Length: >= SyntheticIvSize } data)
            return null;
        var iv = data.AsSpan(0, SyntheticIvSize);
        var plain = new byte[data.Length - SyntheticIvSize];
        Ctr(_nameCipher, iv, data.AsSpan(SyntheticIvSize), plain);
        var expected = HMACSHA256.HashData(_nameMac, plain).AsSpan(0, SyntheticIvSize);
        if (!CryptographicOperations.FixedTimeEquals(expected, iv))
            return null;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(plain);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>AES-256-CTR: the counter block is the 12-byte IV and a 32-bit big-endian block number.</summary>
    private static void Ctr(byte[] key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> input, Span<byte> output)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        Span<byte> counter = stackalloc byte[16];
        Span<byte> stream = stackalloc byte[16];
        iv.CopyTo(counter);
        for (var offset = 0; offset < input.Length; offset += 16)
        {
            BinaryPrimitives.WriteUInt32BigEndian(counter[12..], (uint)(offset / 16));
            aes.EncryptEcb(counter, stream, PaddingMode.None);
            var count = Math.Min(16, input.Length - offset);
            for (var i = 0; i < count; i++)
                output[offset + i] = (byte)(input[offset + i] ^ stream[i]);
        }
    }

    // ---- Contents -----------------------------------------------------------------------------------------------

    /// <summary>The size of an object holding <paramref name="plainLength"/> bytes (an empty file is one empty block).</summary>
    public static long CipherLength(long plainLength) => plainLength + TagSize * Blocks(plainLength);

    /// <summary>The plaintext size of an object of <paramref name="cipherLength"/> bytes; null when no plaintext gives it.</summary>
    public static long? PlainLength(long cipherLength)
    {
        if (cipherLength < TagSize)
            return null;
        var blocks = (cipherLength + BlockSize + TagSize - 1) / (BlockSize + TagSize);
        var plain = cipherLength - TagSize * blocks;
        return plain >= 0 && CipherLength(plain) == cipherLength ? plain : null;
    }

    private static long Blocks(long plainLength) => Math.Max(1, (plainLength + BlockSize - 1) / BlockSize);

    /// <summary>A new file key and its metadata (the wrapped key and the nonce prefix).</summary>
    public (FileKey Key, string Metadata) NewFile()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var prefix = RandomNumberGenerator.GetBytes(8);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var wrapped = new byte[32];
        var tag = new byte[TagSize];
        using (var gcm = new AesGcm(_keyWrap, TagSize))
            gcm.Encrypt(nonce, dek, wrapped, tag, "arcanist-dek-v1"u8);
        var metadata = $"{Version}.{Base64Url(prefix)}.{Base64Url([.. nonce, .. wrapped, .. tag])}";
        return (new FileKey(dek, prefix), metadata);
    }

    /// <summary>The file key of an object from its metadata; null when it was not written with this owner's key.</summary>
    public FileKey? OpenFile(string? metadata)
    {
        var parts = metadata?.Split('.');
        if (parts is not [Version, var prefixText, var wrappedText] || FromBase64Url(prefixText) is not { Length: 8 } prefix
            || FromBase64Url(wrappedText) is not { Length: 12 + 32 + TagSize } sealedKey)
            return null;
        var dek = new byte[32];
        try
        {
            using var gcm = new AesGcm(_keyWrap, TagSize);
            gcm.Decrypt(sealedKey.AsSpan(0, 12), sealedKey.AsSpan(12, 32), sealedKey.AsSpan(44), dek, "arcanist-dek-v1"u8);
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }

        return new FileKey(dek, prefix);
    }

    /// <summary>Encrypts a whole file to <paramref name="output"/>.</summary>
    public static async Task EncryptAsync(FileKey key, Stream input, long plainLength, Stream output, CancellationToken cancellationToken)
    {
        var blocks = Blocks(plainLength);
        var plain = new byte[BlockSize];
        var cipher = new byte[BlockSize + TagSize];
        using var gcm = new AesGcm(key.Dek, TagSize);
        for (long block = 0; block < blocks; block++)
        {
            var size = (int)Math.Min(BlockSize, plainLength - block * BlockSize);
            await input.ReadExactlyAsync(plain.AsMemory(0, size), cancellationToken);
            gcm.Encrypt(key.Nonce(block), plain.AsSpan(0, size), cipher.AsSpan(0, size), cipher.AsSpan(size, TagSize),
                Associated(block, block == blocks - 1));
            await output.WriteAsync(cipher.AsMemory(0, size + TagSize), cancellationToken);
        }
    }

    /// <summary>Decrypts one block (<paramref name="cipher"/> with its tag) into <paramref name="plain"/>.</summary>
    public static void DecryptBlock(FileKey key, long block, bool last, ReadOnlySpan<byte> cipher, Span<byte> plain)
    {
        using var gcm = new AesGcm(key.Dek, TagSize);
        gcm.Decrypt(key.Nonce(block), cipher[..^TagSize], cipher[^TagSize..], plain, Associated(block, last));
    }

    private static byte[] Associated(long block, bool last)
    {
        var data = new byte[9];
        BinaryPrimitives.WriteInt64BigEndian(data, block);
        data[8] = last ? (byte)1 : (byte)0;
        return data;
    }

    private static string Base64Url(ReadOnlySpan<byte> data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string text)
    {
        if (text.Length == 0 || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return null;
        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>A file's own key and nonce prefix.</summary>
internal sealed record FileKey(byte[] Dek, byte[] NoncePrefix)
{
    public byte[] Nonce(long block)
    {
        var nonce = new byte[12];
        NoncePrefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), checked((uint)block));
        return nonce;
    }
}

/// <summary>
/// The plaintext of an encrypted object, seekable: reads only the blocks it needs from the object (through
/// <see cref="S3ReadStream"/>, which fetches ranges) and checks each one.
/// </summary>
internal sealed class S3DecryptingStream(Stream cipher, FileKey key, long plainLength) : Stream
{
    private readonly byte[] _cipherBlock = new byte[S3Encryption.BlockSize + S3Encryption.TagSize];
    private readonly byte[] _plainBlock = new byte[S3Encryption.BlockSize];
    private long _cachedBlock = -1;
    private int _cachedLength;
    private long _position;

    private long Blocks => Math.Max(1, (plainLength + S3Encryption.BlockSize - 1) / S3Encryption.BlockSize);

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => plainLength;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => plainLength + offset,
        };
        if (target < 0)
            throw new IOException("Seek before the start of the file.");
        _position = target;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= plainLength || buffer.Length == 0)
            return 0;

        var block = _position / S3Encryption.BlockSize;
        if (block != _cachedBlock)
        {
            var last = block == Blocks - 1;
            var size = (int)Math.Min(S3Encryption.BlockSize, plainLength - block * S3Encryption.BlockSize);
            cipher.Position = block * (S3Encryption.BlockSize + S3Encryption.TagSize);
            await cipher.ReadExactlyAsync(_cipherBlock.AsMemory(0, size + S3Encryption.TagSize), cancellationToken);
            try
            {
                S3Encryption.DecryptBlock(key, block, last, _cipherBlock.AsSpan(0, size + S3Encryption.TagSize), _plainBlock.AsSpan(0, size));
            }
            catch (AuthenticationTagMismatchException e)
            {
                throw new IOException("An encrypted block does not check out: the object was changed or is not this file.", e);
            }

            _cachedBlock = block;
            _cachedLength = size;
        }

        var start = (int)(_position - block * S3Encryption.BlockSize);
        var count = Math.Min(buffer.Length, _cachedLength - start);
        _plainBlock.AsMemory(start, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            cipher.Dispose();
        base.Dispose(disposing);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
