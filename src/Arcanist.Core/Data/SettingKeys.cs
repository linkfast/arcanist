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
}
