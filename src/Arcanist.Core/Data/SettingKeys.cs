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

    /// <summary>The administrator's own AcoustID application key, used instead of the project's.</summary>
    public const string AcoustIdKey = "online.acoustIdKey";
}
