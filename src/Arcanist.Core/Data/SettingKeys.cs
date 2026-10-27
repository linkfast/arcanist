namespace Arcanist.Core.Data;

/// <summary>Keys of the <c>Settings</c> table (runtime settings edited from the admin panel).</summary>
public static class SettingKeys
{
    /// <summary>Whether anonymous play counts are contributed to Ultimate Cloud's global statistics.</summary>
    public const string ShareAnonymousStatistics = "privacy.shareAnonymousStatistics";

    /// <summary>The mStream import the setup wizard started: the panel waits for it (<c>SetupStatus.Migrating</c>).</summary>
    public const string SetupMigration = "setup.migration";

    /// <summary>No telemetry, and which outside services may be used (<c>Privacy</c>).</summary>
    public const string Privacy = "privacy.settings";

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

    /// <summary>Where uploads that name no library go (<c>StoredUploadSettings</c>).</summary>
    public const string Uploads = "uploads.settings";

    /// <summary>The library a user's uploads go to when they name none: <c>uploads.user.&lt;id&gt;</c>.</summary>
    public static string UserUploads(Guid userId) => $"uploads.user.{userId:N}";

    /// <summary>The terms accepted while setting the server up (<c>LegalAcceptance</c>).</summary>
    public const string LegalAcceptance = "legal.acceptance";

    /// <summary>The connect secret of mStream's quick connect, protected with Data Protection.</summary>
    public const string MstreamQuickConnect = "mstream.quickConnect";

    /// <summary>The DLNA device's UUID, created once so devices keep recognizing the server.</summary>
    public const string DlnaDeviceId = "dlna.deviceId";

    /// <summary>Server audio: the device, the name it shows, its volume and its Ultimate Connect id (<c>ServerAudioConfig</c>).</summary>
    public const string ServerAudio = "serverAudio.settings";

    /// <summary>The AI extension's settings (<c>AiConfig</c>).</summary>
    public const string Ai = "ai.settings";

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

    /// <summary>Per user who linked their own Ultimate Cloud account: <c>ultimateCloud.user.&lt;id&gt;.link</c>, <c>.plan</c>,
    /// <c>.services</c> and <c>.stars</c>, like the server's.</summary>
    public static string UltimateCloudUser(Guid userId, string what) => $"{UltimateCloudUserPrefix}{userId:N}.{what}";

    public const string UltimateCloudUserPrefix = "ultimateCloud.user.";

    /// <summary>Which of Ultimate Cloud's online services this server takes part in (<c>UltimateCloudServicesConfig</c>).</summary>
    public const string UltimateCloudServices = "ultimateCloud.services";

    /// <summary>The last play event reported to Ultimate Cloud (plays are sent from there on).</summary>
    public const string UltimateCloudPlaysCursor = "ultimateCloud.playsCursor";

    /// <summary>The StarTracks this server and the account last agreed on (AcoustIDs), to merge changes from both sides.</summary>
    public const string UltimateCloudStars = "ultimateCloud.stars";

    /// <summary>Whether to check for newer versions of Arcanist (true by default).</summary>
    public const string UpdatesEnabled = "updates.enabled";

    /// <summary>The last check for a newer Arcanist and what it found.</summary>
    public const string UpdatesLastCheck = "updates.lastCheck";

    /// <summary>How converted audio is kept: its cache and the versions made ahead of time.</summary>
    public const string Transcoding = "transcoding.settings";

    /// <summary>The quality each kind of Ultimate client gets by default (<c>StreamingDefaults</c>).</summary>
    public const string StreamingDefaults = "transcoding.clients";

    /// <summary>How libraries shared by other servers are played here: their cache and where they are converted.</summary>
    public const string RemoteLibraries = "p2p.remoteLibraries";

    /// <summary>The last play sent to the servers that count this server's plays.</summary>
    public const string P2PPlaysCursor = "p2p.playsCursor";

    /// <summary>Music videos: whose audio plays by default, their cache and the versions made ahead of time.</summary>
    public const string Videos = "videos.settings";

    /// <summary>A user's own choice of whose audio plays with music videos.</summary>
    public static string UserVideos(Guid userId) => $"videos.user.{userId:N}";
}
