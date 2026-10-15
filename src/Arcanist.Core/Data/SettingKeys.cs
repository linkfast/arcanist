namespace Arcanist.Core.Data;

/// <summary>Keys of the <c>Settings</c> table (runtime settings edited from the admin panel).</summary>
public static class SettingKeys
{
    /// <summary>Whether anonymous play counts are contributed to Ultimate Cloud's global statistics.</summary>
    public const string ShareAnonymousStatistics = "privacy.shareAnonymousStatistics";

    /// <summary>Default UI language for new users, as a BCP 47 tag.</summary>
    public const string DefaultLanguage = "ui.defaultLanguage";

    /// <summary>Whether audio is analyzed in the background (loudness, fingerprints, waveform, tempo, key). Default true.</summary>
    public const string AnalysisEnabled = "analysis.enabled";

    /// <summary>Path to ffmpeg chosen in the setup wizard or the admin panel; null to find it automatically.</summary>
    public const string FfmpegPath = "media.ffmpegPath";

    /// <summary>Which online services are on (<c>OnlineServicesSettings</c>); all off when absent.</summary>
    public const string OnlineServices = "online.services";

    /// <summary>How users rate tracks (<c>RatingMode</c>): five stars, or a single star. Default five stars.</summary>
    public const string RatingMode = "ratings.mode";

    /// <summary>Highest library number ever given, so numbers of deleted libraries are not reused.</summary>
    public const string LastLibraryNumber = "libraries.lastNumber";

    /// <summary>Id of the installed frontend served at <c>/</c>; the bundled player when absent.</summary>
    public const string DefaultFrontend = "frontends.default";

    /// <summary>The administrator's own AcoustID application key, used instead of the project's.</summary>
    public const string AcoustIdKey = "online.acoustIdKey";

    /// <summary>DLNA: the device's name, the libraries it publishes and the sections of its tree.</summary>
    public const string Dlna = "dlna.settings";

    /// <summary>mStream apps: the star threshold, sign-in with the user's password and their own functions (<c>MstreamConfig</c>).</summary>
    public const string Mstream = "mstream.settings";

    /// <summary>The DLNA device's UUID, created once so devices keep recognizing the server.</summary>
    public const string DlnaDeviceId = "dlna.deviceId";

    /// <summary>P2P network: on or off, relay, name and global limits (<c>P2PConfig</c>).</summary>
    public const string P2P = "p2p.settings";

    /// <summary>The SQLite file left by a migration to PostgreSQL, until the administrator decides about it.</summary>
    public const string PreviousSqlite = "database.previousSqlite";

    /// <summary>The linked Ultimate Cloud account and its protected refresh token (<c>StoredCloudLink</c>).</summary>
    public const string UltimateCloudLink = "ultimateCloud.link";

    /// <summary>The account's plan as last checked (<c>CloudPlanSnapshot</c>), so the tier survives restarts while offline.</summary>
    public const string UltimateCloudPlan = "ultimateCloud.plan";

    /// <summary>The id this server uses as its device in Ultimate Cloud, created once.</summary>
    public const string UltimateCloudDeviceId = "ultimateCloud.deviceId";
}
