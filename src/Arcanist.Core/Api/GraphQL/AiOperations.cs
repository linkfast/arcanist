using System.ComponentModel;
using Arcanist.Core.Application.Ai;
using Arcanist.Core.Application.Listening;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("trackAnalysis")]
    [Description("A song's analysis for DJ software (AI extension): beat grid, sections, energy, voice, mix and scratch points… `include` picks blocks (beats, onsets, energy, vocals, sections, silences, cues, loudness, key, stats; all but stats by default). PENDING with its position in line until it is ready.")]
    public static Task<TrackAnalysisResult> TrackAnalysis([Id] Guid trackId, string? include, [FromServices] TrackAnalysisService analyses, CancellationToken cancellationToken) =>
        analyses.AnalysisAsync(trackId, include, cancellationToken);

    [Name("trackAnalyses")]
    [Description("Analyses of the songs the user sees, from the least to the most recently analyzed (at most 200 a page): keep a cache with updatedSince set to the last analyzedAt held.")]
    public static Task<TrackAnalysisPage> TrackAnalyses(DateTime? updatedSince, string? after, int? first, string? include, [FromServices] TrackAnalysisService analyses,
        CancellationToken cancellationToken) =>
        analyses.AnalysesAsync(updatedSince, after, first, include, cancellationToken);

    [Name("trackStats")]
    [Description("Listening statistics per song (plays, skips and where, completions, how much is listened to, hours of the day): the user's, or the server's (everyone's together). From the least to the most recently played.")]
    public static Task<TrackStatsPage> TrackStats(TrackStatsScope? scope, DateTime? updatedSince, string? after, int? first, string? timeZone,
        [FromServices] TrackStatsService stats, CancellationToken cancellationToken) =>
        stats.StatsAsync(scope, updatedSince, after, first, timeZone, cancellationToken);

    [Name("aiStatus")]
    [Description("What the AI extension's analysis is doing, its progress and settings. Administrators only.")]
    public static Task<AiStatus> AiStatus([FromServices] AiAdminService ai, CancellationToken cancellationToken) => ai.StatusAsync(cancellationToken);
}

public sealed partial class Mutation
{
    [Name("prioritizeTrackAnalysis")]
    [Description("Puts a song first in line for its analysis (it is going to be played soon).")]
    public static Task<TrackAnalysisResult> PrioritizeTrackAnalysis([Id] Guid trackId, [FromServices] TrackAnalysisService analyses, CancellationToken cancellationToken) =>
        analyses.PrioritizeAsync(trackId, cancellationToken);

    [Name("setAiSettings")]
    [Description("Changes the AI extension's settings. Administrators only, with a recent authentication.")]
    public static Task<AiStatus> SetAiSettings(SetAiSettingsInput input, [FromServices] AiAdminService ai, CancellationToken cancellationToken) =>
        ai.SetSettingsAsync(input, cancellationToken);

    [Name("reanalyzeTracks")]
    [Description("Analyzes every song again; the current analyses are served (as stale) until each is redone. Administrators only, with a recent authentication.")]
    public static Task<AiStatus> ReanalyzeTracks([FromServices] AiAdminService ai, CancellationToken cancellationToken) => ai.ReanalyzeAsync(cancellationToken);
}

public sealed partial class Subscription
{
    [Name("trackAnalysisReady")]
    [Description("A song the user sees has its analysis (new, or redone by a newer analyzer).")]
    public static IObservable<TrackAnalysisReady> TrackAnalysisReady([FromServices] TrackAnalysisService analyses) => analyses.WatchReady();

    [Name("aiStatusChanged")]
    [Description("The AI extension's status as it changes, at most once a second. Administrators only.")]
    public static IObservable<AiStatus> AiStatusChanged([FromServices] AiAdminService ai) => ai.Watch();
}
