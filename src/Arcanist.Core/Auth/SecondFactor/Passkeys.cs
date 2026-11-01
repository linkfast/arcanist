using System.Text.Json;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Sharing;
using Arcanist.Core.Data.Entities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Caching.Memory;

namespace Arcanist.Core.Auth.SecondFactor;

/// <summary>A credential a browser just created, checked.</summary>
public sealed record NewPasskey(byte[] CredentialId, byte[] PublicKey, uint SignCount, Guid AaGuid, string? Transports, string RpId);

/// <summary>
/// WebAuthn with Fido2NetLib: the relying party is the host the server was opened with, and the origins accepted are
/// that one and the server's public URL. What is being asked waits in memory for five minutes, for one answer.
/// </summary>
public sealed class Passkeys(IHttpContextAccessor http, SharingSettings sharing, IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>The domain passkeys belong to on this request.</summary>
    public async Task<string> RpIdAsync(CancellationToken cancellationToken) => (await ConfigurationAsync(cancellationToken)).ServerDomain;

    public async Task<string> RegistrationOptionsAsync(User user, IReadOnlyList<UserPasskey> existing, string askedBy, CancellationToken cancellationToken)
    {
        var configuration = await ConfigurationAsync(cancellationToken);
        var options = new Fido2(configuration).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.UserName, DisplayName = user.DisplayName ?? user.UserName },
            ExcludeCredentials = existing.Where(p => p.RpId == configuration.ServerDomain).Select(Descriptor).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = options.ToJson();
        cache.Set(Key(askedBy), json, Lifetime);
        return json;
    }

    public async Task<NewPasskey> FinishRegistrationAsync(string askedBy, string? attestation, CancellationToken cancellationToken)
    {
        var options = CredentialCreateOptions.FromJson(Take(askedBy));
        var configuration = await ConfigurationAsync(cancellationToken);
        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestation ?? "")
                           ?? throw new ArcanistException.Conflict(ErrorCodes.PasskeyFailed);
            var credential = await new Fido2(configuration).MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = options,
                // The unique index has the last word, but this answers better.
                IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true),
            }, cancellationToken);
            return new NewPasskey(credential.Id, credential.PublicKey, credential.SignCount, credential.AaGuid,
                credential.Transports is { Length: > 0 } transports ? string.Join(',', transports) : null, configuration.ServerDomain);
        }
        catch (Exception e) when (IsBadAnswer(e))
        {
            throw new ArcanistException.Conflict(ErrorCodes.PasskeyFailed);
        }
    }

    public async Task<string> AssertionOptionsAsync(IReadOnlyList<UserPasskey> passkeys, string askedBy, CancellationToken cancellationToken)
    {
        var configuration = await ConfigurationAsync(cancellationToken);
        var here = passkeys.Where(p => p.RpId == configuration.ServerDomain).Select(Descriptor).ToList();
        if (here.Count == 0)
            throw new ArcanistException.Conflict(ErrorCodes.PasskeyOtherDomain);
        var json = new Fido2(configuration).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = here,
            UserVerification = UserVerificationRequirement.Preferred,
        }).ToJson();
        cache.Set(Key(askedBy), json, Lifetime);
        return json;
    }

    /// <summary>The passkey of this user a browser's answer comes from, its counter moved on; null if none.</summary>
    public async Task<UserPasskey?> VerifyAssertionAsync(User user, IReadOnlyList<UserPasskey> passkeys, string askedBy, string? assertion, CancellationToken cancellationToken)
    {
        var options = AssertionOptions.FromJson(Take(askedBy));
        var configuration = await ConfigurationAsync(cancellationToken);
        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertion ?? "");
            var passkey = response is null ? null : passkeys.FirstOrDefault(p => p.RpId == configuration.ServerDomain && p.CredentialId.SequenceEqual(response.RawId));
            if (passkey is null)
                return null;

            var result = await new Fido2(configuration).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response!,
                OriginalOptions = options,
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = (uint)passkey.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (found, _) => Task.FromResult(found.UserHandle.SequenceEqual(user.Id.ToByteArray())),
            }, cancellationToken);
            passkey.SignCount = result.SignCount;
            return passkey;
        }
        catch (Exception e) when (IsBadAnswer(e))
        {
            return null;
        }
    }

    private string Take(string askedBy)
    {
        if (!cache.TryGetValue(Key(askedBy), out string? json) || json is null)
            throw new ArcanistException.Conflict(ErrorCodes.SecondFactorExpired);
        cache.Remove(Key(askedBy));
        return json;
    }

    private static string Key(string askedBy) => "webauthn:" + askedBy;

    private static PublicKeyCredentialDescriptor Descriptor(UserPasskey passkey) =>
        new(PublicKeyCredentialType.PublicKey, passkey.CredentialId, Transports(passkey.Transports));

    private static AuthenticatorTransport[]? Transports(string? transports) =>
        transports?.Split(',').Select(t => Enum.TryParse<AuthenticatorTransport>(t, out var value) ? value : (AuthenticatorTransport?)null)
            .OfType<AuthenticatorTransport>().ToArray();

    private static bool IsBadAnswer(Exception e) =>
        e is Fido2VerificationException or JsonException or ArgumentException or InvalidOperationException or FormatException or NotSupportedException;

    private async Task<Fido2Configuration> ConfigurationAsync(CancellationToken cancellationToken)
    {
        var request = http.HttpContext?.Request ?? throw new InvalidOperationException("Passkeys are used from a request.");
        var host = request.Host.Host;
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"{request.Scheme}://{request.Host}" };

        var publicUrl = await sharing.PublicUrlAsync(cancellationToken);
        var publicUri = publicUrl is null ? null : new Uri(publicUrl);
        if (publicUri is not null)
            origins.Add(publicUri.GetLeftPart(UriPartial.Authority));

        // Behind a proxy that rewrites the host, or the panel's development server, the browser's address is the one that counts.
        var rpId = host;
        if (Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
            && (origin.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || origin.Host.Equals(publicUri?.Host, StringComparison.OrdinalIgnoreCase)))
        {
            rpId = origin.Host;
            origins.Add(origin.GetLeftPart(UriPartial.Authority));
        }

        return new Fido2Configuration { ServerDomain = rpId, ServerName = "Arcanist", Origins = origins };
    }
}
