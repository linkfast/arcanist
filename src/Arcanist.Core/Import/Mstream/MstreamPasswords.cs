using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace Arcanist.Core.Import.Mstream;

/// <summary>
/// Passwords of users imported from mStream, kept in <see cref="User.PasswordHash"/> until they sign in:
/// <c>mstream$&lt;salt&gt;$&lt;hash&gt;</c> holds mStream's own hash (from its files), and <c>mstream-server$&lt;import&gt;</c>
/// says to ask the mStream server of that import (through its API, where hashes cannot be read).
/// </summary>
internal static class MstreamPasswords
{
    public const string HashPrefix = "mstream$";
    public const string ServerPrefix = "mstream-server$";

    public static string Hash(string salt, string hash) => $"{HashPrefix}{salt}${hash}";

    public static string Server(Guid importId) => $"{ServerPrefix}{importId:N}";

    public static Guid? ServerImport(string passwordHash) =>
        passwordHash.StartsWith(ServerPrefix, StringComparison.Ordinal) && Guid.TryParseExact(passwordHash[ServerPrefix.Length..], "N", out var id) ? id : null;

    /// <summary>
    /// mStream's scheme: PBKDF2 with HMAC-SHA-512, 15000 iterations and a 32-byte key, all in base64. The salt is
    /// used as the text of its base64, not decoded.
    /// </summary>
    public static bool Verify(string passwordHash, string password)
    {
        var parts = passwordHash[HashPrefix.Length..].Split('$');
        if (parts is not [var salt, var expected])
            return false;
        var derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), 15000, HashAlgorithmName.SHA512, 32);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Convert.ToBase64String(derived)), Encoding.ASCII.GetBytes(expected));
    }
}

/// <summary>
/// Arcanist's password hasher, which also checks the mStream hashes of imported users. A match asks for a rehash,
/// so the first sign-in replaces it with Arcanist's own.
/// </summary>
internal sealed class ArcanistPasswordHasher : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _inner = new();

    public string HashPassword(User user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        if (hashedPassword.StartsWith(MstreamPasswords.HashPrefix, StringComparison.Ordinal))
        {
            return MstreamPasswords.Verify(hashedPassword, providedPassword)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        // Checked against the mStream server at sign-in (see MstreamServerSignIn); nothing to compare here.
        if (hashedPassword.StartsWith(MstreamPasswords.ServerPrefix, StringComparison.Ordinal) || hashedPassword.Length == 0)
            return PasswordVerificationResult.Failed;

        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
