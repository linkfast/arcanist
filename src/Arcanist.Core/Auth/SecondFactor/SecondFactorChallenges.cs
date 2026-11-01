using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanist.Core.Data.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace Arcanist.Core.Auth.SecondFactor;

public enum ChallengePurpose
{
    /// <summary>The password was right: a factor the user has finishes the sign-in.</summary>
    Verify,

    /// <summary>The password was right, and the user must pair a factor to finish the sign-in.</summary>
    Setup,
}

/// <summary>What a challenge says: who passed the password check, for what, and how the sign-in was asked for.</summary>
public sealed record ChallengeData(Guid UserId, ChallengePurpose Purpose, string? DeviceName, bool UseCookie, string Fingerprint, DateTime ExpiresAt);

/// <summary>
/// Sign-in challenges: proof that the password was right, for the next five minutes, handed to the client instead of
/// tokens. Sealed with Data Protection, so nothing is stored. They stop working if the user's password or security
/// stamp change.
/// </summary>
public sealed class SecondFactorChallenges(IDataProtectionProvider protection, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private IDataProtector Protector => protection.CreateProtector("Arcanist.Auth.SecondFactorChallenge");

    public string Create(User user, ChallengePurpose purpose, string? deviceName, bool useCookie) =>
        Protector.Protect(JsonSerializer.Serialize(new ChallengeData(user.Id, purpose, deviceName, useCookie, Fingerprint(user),
            time.GetUtcNow().UtcDateTime + Lifetime)));

    /// <summary>The challenge, or null if it is not one of ours or it expired.</summary>
    public ChallengeData? Read(string? challenge)
    {
        if (string.IsNullOrEmpty(challenge))
            return null;
        try
        {
            var data = JsonSerializer.Deserialize<ChallengeData>(Protector.Unprotect(challenge));
            return data is not null && data.ExpiresAt > time.GetUtcNow().UtcDateTime ? data : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    public static string Fingerprint(User user) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp + "\n" + user.PasswordHash)))[..32];
}
