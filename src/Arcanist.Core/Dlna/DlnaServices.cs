using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Dlna;

/// <summary>Where devices on the local network reach the server over plain HTTP.</summary>
internal sealed class DlnaEndpoint(IServer server, IOptions<DlnaOptions> options)
{
    /// <summary>The HTTP port: <see cref="DlnaOptions.Port"/>, or the first http address the server listens on.</summary>
    public int? Port
    {
        get
        {
            if (options.Value.Port is { } configured)
                return configured;
            foreach (var address in server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            {
                if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    continue;
                var parsable = address.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://[::]", "://localhost");
                if (Uri.TryCreate(parsable, UriKind.Absolute, out var uri))
                    return uri.Port;
            }

            return null;
        }
    }

    /// <summary>The server's base URL as a device at <paramref name="peer"/> reaches it.</summary>
    public string? BaseFor(IPAddress? peer)
    {
        if (Port is not { } port)
            return null;
        var address = (peer is null ? null : LocalNetwork.AddressFacing(peer)) ?? IPAddress.Loopback;
        return $"http://{address}:{port}";
    }

    public IReadOnlyList<string> DescriptionUrls() =>
        Port is { } port
            ? LocalNetwork.Addresses().Select(a => $"http://{a.Address}:{port}{Extensions.Dlna.DlnaExtension.DescriptionPath}").ToList()
            : [];
}

/// <summary>What a "play to" URL gives a renderer: one copy of one track, as the user who queued it may play it.</summary>
internal sealed record DlnaPlayGrant(Guid UserId, Guid TrackId, Guid FileId, DlnaFormat Format);

/// <summary>
/// The tokens in "play to" URLs. Renderers cannot sign in, so each URL carries a grant, protected with the
/// server's keys, valid for a week (long enough for any queue).
/// </summary>
internal sealed class DlnaPlayTokens(IDataProtectionProvider protection)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("Arcanist.Dlna.Play").ToTimeLimitedDataProtector();

    public string Create(DlnaPlayGrant grant) =>
        Protector.Protect($"{grant.UserId:N}|{grant.TrackId:N}|{grant.FileId:N}|{DlnaFormats.Name(grant.Format)}", Lifetime);

    public DlnaPlayGrant? Read(string token)
    {
        try
        {
            var parts = Protector.Unprotect(token).Split('|');
            return parts.Length == 4 && Guid.TryParseExact(parts[0], "N", out var user) && Guid.TryParseExact(parts[1], "N", out var track)
                   && Guid.TryParseExact(parts[2], "N", out var file) && DlnaFormats.Parse(parts[3]) is { } format
                ? new DlnaPlayGrant(user, track, file, format)
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

public static class DlnaServiceCollectionExtensions
{
    public static IServiceCollection AddDlna(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DlnaOptions>(configuration.GetSection(DlnaOptions.Section));
        services.AddSingleton<DlnaSettings>();
        services.AddSingleton<DlnaEndpoint>();
        services.AddSingleton<DlnaPlayTokens>();
        services.AddSingleton<RendererRegistry>();
        services.AddSingleton<RendererSessions>();
        services.AddHostedService(provider => provider.GetRequiredService<RendererSessions>());
        services.AddSingleton<SsdpService>();
        services.AddHostedService(provider => provider.GetRequiredService<SsdpService>());
        services.AddHttpClient(RendererRegistry.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<Application.Dlna.DlnaService>();

        // Copying music from other media servers works with the DLNA extension off.
        services.AddSingleton<MediaServerClient>();
        services.AddSingleton<Import.Dlna.DlnaImportWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<Import.Dlna.DlnaImportWorker>());
        services.AddHttpClient(MediaServerClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = 32 * 1024 * 1024;
        });
        // Streams last as long as the file: a slow server or a big file must not hit a timeout.
        services.AddHttpClient(MediaServerClient.DownloadClientName, client => client.Timeout = TimeSpan.FromHours(2));
        services.AddScoped<Application.Import.DlnaImportService>();
        return services;
    }
}
