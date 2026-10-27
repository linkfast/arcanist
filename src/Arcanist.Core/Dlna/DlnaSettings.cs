using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Microsoft.Extensions.Options;

namespace Arcanist.Core.Dlna;

/// <summary>What the DLNA tree shows at its top level, in the order chosen. Part of the API contract.</summary>
// Stored by name, so the settings survive reordering the values.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DlnaSection>))]
public enum DlnaSection
{
    Artists,
    Albums,
    Genres,
    Decades,
    /// <summary>The libraries' folders, as on disk.</summary>
    Folders,
    /// <summary>Public playlists.</summary>
    Playlists,
    RecentlyAdded,
    /// <summary>Genre and decade mixes.</summary>
    Mixes,

    /// <summary>StarTracks: the starred tracks (five stars) of the user whose ratings DLNA shows.</summary>
    Starred,

    /// <summary>Tracks by rating, five stars to one. Only with the five-star rating mode.</summary>
    Rated,

    /// <summary>Each published library on its own: its artists, albums and folders.</summary>
    Libraries,

    /// <summary>The music videos of the published libraries, with the audio the server plays by default.</summary>
    MusicVideos,
}

/// <summary>DLNA settings, kept in the database.</summary>
/// <param name="FriendlyName">The name devices show; null for "Arcanist (machine)".</param>
/// <param name="LibraryIds">Libraries published on the local network. None until an administrator picks them.</param>
/// <param name="RatingsUserId">Whose stars and ratings the StarTracks and rated sections show (DLNA has no sign-in); the
/// first administrator when null.</param>
/// <param name="RecordPlays">"Play to" counts the plays for their users; paused while Arcanist is, for example, the source of a migration.</param>
/// <param name="StarTracksUserIds">Users whose StarTracks the StarTracks section shows, one folder each (chosen by an
/// administrator, or by each user for themselves); when none, it shows those of <paramref name="RatingsUserId"/>.</param>
internal sealed record DlnaConfig(
    string? FriendlyName, IReadOnlyList<Guid> LibraryIds, IReadOnlyList<DlnaSection> Sections, Guid? RatingsUserId = null, bool RecordPlays = true,
    IReadOnlyList<Guid>? StarTracksUserIds = null)
{
    public static readonly DlnaConfig Default = new(null, [],
        [DlnaSection.Artists, DlnaSection.Albums, DlnaSection.Genres, DlnaSection.Folders, DlnaSection.Playlists, DlnaSection.Starred]);

    public string Name => string.IsNullOrWhiteSpace(FriendlyName) ? $"Arcanist ({Environment.MachineName})" : FriendlyName;
}

/// <summary>Bound from <c>Arcanist:Dlna</c>, for advanced setups and tests.</summary>
public sealed class DlnaOptions
{
    public const string Section = "Arcanist:Dlna";

    /// <summary>Announce the server and find renderers with SSDP (UDP multicast). Off in tests.</summary>
    public bool Ssdp { get; set; } = true;

    /// <summary>The HTTP port devices reach the server on; by default the first http address the server listens on.</summary>
    public int? Port { get; set; }
}

/// <summary>
/// The DLNA settings and the device's identity, cached: SSDP and every DLNA request read them. The device id
/// (its UPnP UDN) is created once and never changes, so devices keep recognizing the server.
/// </summary>
internal sealed class DlnaSettings(SettingsStore settings, IArcanistDbContextFactory databases, IOptionsMonitor<SetupOptions> setup)
{
    private volatile DlnaConfig? _config;
    private Guid? _deviceId;

    public DlnaConfig Current => _config ?? DlnaConfig.Default;

    public async Task<DlnaConfig> GetAsync(CancellationToken cancellationToken)
    {
        if (_config is null && setup.CurrentValue.Completed && databases.IsConfigured)
            _config = await settings.GetAsync(SettingKeys.Dlna, DlnaConfig.Default, cancellationToken);
        return Current;
    }

    public async Task SetAsync(DlnaConfig config, CancellationToken cancellationToken)
    {
        await settings.SetAsync(SettingKeys.Dlna, config, cancellationToken);
        _config = config;
    }

    public async Task<Guid> DeviceIdAsync(CancellationToken cancellationToken)
    {
        if (_deviceId is { } id)
            return id;
        var stored = await settings.GetAsync<Guid?>(SettingKeys.DlnaDeviceId, null, cancellationToken);
        if (stored is null)
        {
            stored = Guid.NewGuid();
            await settings.SetAsync(SettingKeys.DlnaDeviceId, stored, cancellationToken);
        }

        _deviceId = stored;
        return stored.Value;
    }
}

/// <summary>Who DLNA devices are: nobody signs in, so they see the published libraries and nothing personal.</summary>
internal static class DlnaAccess
{
    public static UserAccess For(DlnaConfig config) =>
        new(Guid.Empty, "dlna", "DLNA", UserRole.User, Permissions.None, "", false, config.LibraryIds.ToHashSet());

    /// <summary>Makes the request act as the DLNA identity, for the application services.</summary>
    public static void SignIn(HttpContext context, UserAccess access)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ArcanistClaims.UserId, access.UserId.ToString()),
            new Claim(ArcanistClaims.SessionId, Guid.Empty.ToString()),
        ], "dlna"));
        context.Items[typeof(UserAccess)] = access;
    }
}

/// <summary>The local network: DLNA has no sign-in, so it only answers there.</summary>
internal static class LocalNetwork
{
    public static bool Contains(IPAddress? address)
    {
        if (address is null)
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        // Link-local and unique local addresses.
        return address.IsIPv6LinkLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }

    /// <summary>The server's own IPv4 addresses on local networks, with their masks.</summary>
    public static IReadOnlyList<(IPAddress Address, IPAddress Mask, int Interface)> Addresses()
    {
        var addresses = new List<(IPAddress, IPAddress, int)>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up || !network.SupportsMulticast
                || network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var properties = network.GetIPProperties();
                // Interfaces without IPv4 throw here rather than answering null.
                var index = properties.GetIPv4Properties()?.Index ?? 0;
                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address) && Contains(unicast.Address))
                        addresses.Add((unicast.Address, unicast.IPv4Mask, index));
                }
            }
            catch (NetworkInformationException)
            {
            }
        }

        return addresses;
    }

    /// <summary>The server's address on the same network as <paramref name="peer"/>, or its first local address.</summary>
    public static IPAddress? AddressFacing(IPAddress peer)
    {
        var addresses = Addresses();
        foreach (var (address, mask, _) in addresses)
        {
            if (SameSubnet(address, peer, mask))
                return address;
        }

        return addresses.Count > 0 ? addresses[0].Address : null;
    }

    public static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        if (a.AddressFamily != AddressFamily.InterNetwork || b.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var (x, y, m) = (a.GetAddressBytes(), b.GetAddressBytes(), mask.GetAddressBytes());
        for (var i = 0; i < 4; i++)
        {
            if ((x[i] & m[i]) != (y[i] & m[i]))
                return false;
        }

        return true;
    }
}
