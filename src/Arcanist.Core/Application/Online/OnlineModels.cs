namespace Arcanist.Core.Application.Online;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public enum OnlineService
{
    /// <summary>Identifies albums (releases), their year and album artist.</summary>
    MusicBrainz,
    /// <summary>Identifies recordings from their acoustic fingerprint.</summary>
    AcoustId,
    /// <summary>Front covers of albums identified on MusicBrainz.</summary>
    CoverArtArchive,
    /// <summary>Lyrics from LRCLib, for tracks without their own.</summary>
    Lyrics,
}

/// <summary>Which online services the server may use. All are off unless an administrator turns them on.</summary>
public sealed record OnlineServicesSettings(bool MusicBrainz, bool AcoustId, bool CoverArtArchive, bool Lyrics)
{
    public static readonly OnlineServicesSettings None = new(false, false, false, false);

    public bool IsEnabled(OnlineService service) => service switch
    {
        OnlineService.MusicBrainz => MusicBrainz,
        OnlineService.AcoustId => AcoustId,
        OnlineService.CoverArtArchive => CoverArtArchive,
        _ => Lyrics,
    };
}

/// <param name="Ready">Enabled and able to work (AcoustID also needs an application key).</param>
/// <param name="Pending">Items still to look up.</param>
/// <param name="Found">Items for which the service had an answer.</param>
/// <param name="LastError">Error code of the last failed request, if the latest one failed.</param>
public sealed record OnlineServiceStatus(
    OnlineService Service,
    bool Enabled,
    bool Ready,
    int Pending,
    int Found,
    int NotFound,
    int Failed,
    DateTime? LastRequestAt,
    string? LastError);

/// <param name="HasAcoustIdKey">Whether AcoustID has an application key: the administrator's own or the project's.</param>
/// <param name="HasOwnAcoustIdKey">Whether the administrator set their own key.</param>
public sealed record OnlineServicesStatus(
    OnlineServicesSettings Settings,
    bool HasAcoustIdKey,
    bool HasOwnAcoustIdKey,
    IReadOnlyList<OnlineServiceStatus> Services);
