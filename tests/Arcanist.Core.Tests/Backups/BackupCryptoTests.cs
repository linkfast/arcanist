using System.Security.Cryptography;
using Arcanist.Core.Application;
using Arcanist.Core.Backups;

namespace Arcanist.Core.Tests.Backups;

/// <summary>The server's part of a backup: only its password opens it, and any change to the file shows.</summary>
public sealed class BackupCryptoTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Fewer iterations than real backups: the derivation is not what these tests check.
    private const int Iterations = 10_000;

    private static async Task<byte[]> EncryptAsync(byte[] plain, string password)
    {
        using var output = new MemoryStream();
        await BackupCrypto.EncryptAsync(new MemoryStream(plain), output, password, Cancel, Iterations);
        return output.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(byte[] encrypted, string password)
    {
        using var output = new MemoryStream();
        await BackupCrypto.DecryptAsync(new MemoryStream(encrypted), output, password, Cancel);
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(1024 * 1024)]
    [InlineData(3 * 1024 * 1024 + 17)]
    public async Task What_is_encrypted_comes_back_with_the_password(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        var encrypted = await EncryptAsync(plain, "correct horse battery");
        Assert.Equal(plain, await DecryptAsync(encrypted, "correct horse battery"));
        Assert.True(await BackupCrypto.CheckAsync(new MemoryStream(encrypted), "correct horse battery", Cancel));
    }

    [Fact]
    public async Task Another_password_does_not_open_it()
    {
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(5000), "correct horse battery");
        Assert.False(await BackupCrypto.CheckAsync(new MemoryStream(encrypted), "wrong horse battery", Cancel));
        var error = await Assert.ThrowsAsync<ArcanistException.Conflict>(() => DecryptAsync(encrypted, "wrong horse battery"));
        Assert.Equal(ErrorCodes.BackupPasswordInvalid, error.Code);
    }

    [Fact]
    public async Task A_changed_cut_or_extended_file_does_not_open()
    {
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 5), "correct horse battery");

        var changed = encrypted.ToArray();
        changed[^100] ^= 1;
        await Assert.ThrowsAsync<ArcanistException.Conflict>(() => DecryptAsync(changed, "correct horse battery"));

        // Without its last record: the one before it does not say it is the last.
        var cut = encrypted[..^(5 + 5 + 16)];
        await Assert.ThrowsAsync<ArcanistException.Conflict>(() => DecryptAsync(cut, "correct horse battery"));

        var extended = encrypted.Concat(new byte[] { 0 }).ToArray();
        await Assert.ThrowsAsync<ArcanistException.Conflict>(() => DecryptAsync(extended, "correct horse battery"));
    }
}
