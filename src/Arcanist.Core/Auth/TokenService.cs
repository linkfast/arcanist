using System.Security.Claims;
using System.Security.Cryptography;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arcanist.Core.Auth;

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public sealed class TokenService(IOptions<AuthOptions> options, SigningKey signingKey, TimeProvider time)
{
    public const string Issuer = "arcanist";
    public const string Audience = "arcanist";

    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(User user, UserSession session)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now + options.Value.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(ArcanistClaims.UserId, user.Id.ToString()),
                new Claim(ArcanistClaims.SessionId, session.Id.ToString()),
                new Claim(ArcanistClaims.SecurityStamp, user.SecurityStamp),
                new Claim(ArcanistClaims.Name, user.UserName),
                new Claim(ArcanistClaims.AuthenticatedAt,
                    new DateTimeOffset(session.AuthenticatedAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            ]),
        });

        return new AccessToken(token, expires);
    }

    /// <summary>A new opaque refresh token. Only its hash is stored.</summary>
    public static string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken)));
}

/// <summary>HMAC key for access tokens, created once and kept in the data directory's <c>keys/</c>.</summary>
public sealed class SigningKey
{
    private const string FileName = "access-token-signing.key";

    public SigningKey(ArcanistPaths paths)
    {
        var path = Path.Combine(paths.Keys, FileName);
        if (!File.Exists(path))
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(64));

        Key = new SymmetricSecurityKey(File.ReadAllBytes(path)) { KeyId = "arcanist-1" };
    }

    public SymmetricSecurityKey Key { get; }
}
