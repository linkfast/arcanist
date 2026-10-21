using System.Security.Cryptography;
using Arcanist.Core.Storage.S3;

namespace Arcanist.Core.Tests.Storage;

/// <summary>The format of encrypted S3 libraries, without an S3 server.</summary>
public sealed class S3EncryptionTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly S3Encryption Encryption = new(S3Encryption.ParseKey(S3Encryption.NewKey())!);

    [Fact]
    public void Names_are_encrypted_per_segment_the_same_way_every_time_and_only_with_their_key()
    {
        const string path = "Sigur Rós/Ágætis byrjun/01 Svefn-g-englar.flac";
        var encrypted = Encryption.EncryptPath(path);

        Assert.Equal(3, encrypted.Split('/').Length);
        Assert.Equal(encrypted, Encryption.EncryptPath(path));
        Assert.DoesNotContain("Sigur", encrypted);
        Assert.All(encrypted, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/'));
        Assert.Equal(path, Encryption.DecryptPath(encrypted));
        // The folder of two files is the same encrypted folder: prefixes keep working.
        Assert.StartsWith(Encryption.EncryptPath("Sigur Rós/Ágætis byrjun") + "/", Encryption.EncryptPath("Sigur Rós/Ágætis byrjun/02 Olsen Olsen.flac"));

        var other = new S3Encryption(S3Encryption.ParseKey(S3Encryption.NewKey())!);
        Assert.Null(other.DecryptPath(encrypted));
        Assert.Null(Encryption.DecryptPath("not-ours.wav"));
        Assert.Null(Encryption.DecryptPath(".arcanist-write-test-1234"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(S3Encryption.BlockSize - 1)]
    [InlineData(S3Encryption.BlockSize)]
    [InlineData(S3Encryption.BlockSize + 1)]
    [InlineData(3 * S3Encryption.BlockSize + 1234)]
    public async Task Contents_read_back_whole_and_from_any_position(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        var (key, metadata) = Encryption.NewFile();
        var cipher = await SealAsync(key, plain);

        Assert.Equal(S3Encryption.CipherLength(size), cipher.Length);
        Assert.Equal(size, S3Encryption.PlainLength(cipher.Length));

        var opened = Encryption.OpenFile(metadata)!;
        await using var stream = new S3DecryptingStream(new MemoryStream(cipher), opened, size);
        var all = new MemoryStream();
        await stream.CopyToAsync(all, Cancel);
        Assert.Equal(plain, all.ToArray());

        // Seeks anywhere, reads across block boundaries.
        foreach (var start in new[] { 0, size / 2, Math.Max(0, size - 5), Math.Max(0, S3Encryption.BlockSize - 3) }.Where(s => s < size))
        {
            stream.Position = start;
            var buffer = new byte[Math.Min(10_000, size - start)];
            await stream.ReadExactlyAsync(buffer, Cancel);
            Assert.Equal(plain.AsSpan(start, buffer.Length).ToArray(), buffer);
        }
    }

    [Fact]
    public async Task Changed_cut_or_swapped_contents_are_refused()
    {
        var plain = RandomNumberGenerator.GetBytes(2 * S3Encryption.BlockSize + 10);
        var (key, metadata) = Encryption.NewFile();
        var opened = Encryption.OpenFile(metadata)!;
        var cipher = await SealAsync(key, plain);

        async Task ReadAllAsync(byte[] bytes, long plainLength)
        {
            await using var stream = new S3DecryptingStream(new MemoryStream(bytes), opened, plainLength);
            await stream.CopyToAsync(Stream.Null, Cancel);
        }

        var changed = (byte[])cipher.Clone();
        changed[100] ^= 1;
        await Assert.ThrowsAsync<IOException>(() => ReadAllAsync(changed, plain.Length));

        // Without its last block, the one before it was not sealed as the last.
        var cut = cipher[..(2 * (S3Encryption.BlockSize + S3Encryption.TagSize))];
        await Assert.ThrowsAsync<IOException>(() => ReadAllAsync(cut, S3Encryption.PlainLength(cut.Length)!.Value));

        // Another file's blocks (another key) do not pass for these.
        var (otherKey, _) = Encryption.NewFile();
        var other = await SealAsync(otherKey, plain);
        await Assert.ThrowsAsync<IOException>(() => ReadAllAsync(other, plain.Length));

        // Metadata written with another owner's key does not open.
        Assert.Null(new S3Encryption(S3Encryption.ParseKey(S3Encryption.NewKey())!).OpenFile(metadata));
        Assert.Null(Encryption.OpenFile("1.bad.data"));
    }

    [Fact]
    public void Keys_are_256_bits_in_base64_or_base64url()
    {
        var key = S3Encryption.NewKey();
        Assert.Equal(32, S3Encryption.ParseKey(key)!.Length);
        Assert.Equal(32, S3Encryption.ParseKey(key.TrimEnd('=').Replace('+', '-').Replace('/', '_'))!.Length);
        Assert.Null(S3Encryption.ParseKey("too short"));
        Assert.Null(S3Encryption.ParseKey(Convert.ToBase64String(new byte[16])));
    }

    private static async Task<byte[]> SealAsync(FileKey key, byte[] plain)
    {
        var output = new MemoryStream();
        await S3Encryption.EncryptAsync(key, new MemoryStream(plain), plain.Length, output, Cancel);
        return output.ToArray();
    }
}
