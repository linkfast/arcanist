using System.ComponentModel;
using Arcanist.Core.Application.Analysis;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Query
{
    [Name("analysisProgress")]
    [Description("Progress of the background audio analysis (loudness, fingerprints, waveforms, tempo and key). Requires MANAGE_LIBRARIES.")]
    public static AnalysisProgress AnalysisProgress([FromServices] AnalysisService analysis) => analysis.Progress();
}

public sealed partial class Mutation
{
    [Name("setAnalysisEnabled")]
    [Description("Turns the background audio analysis on or off. Requires MANAGE_SETTINGS.")]
    public static Task<AnalysisProgress> SetAnalysisEnabled(bool enabled, [FromServices] AnalysisService analysis, CancellationToken cancellationToken) =>
        analysis.SetEnabledAsync(enabled, cancellationToken);
}

public sealed partial class Subscription
{
    [Name("analysisProgressChanged")]
    [Description("Progress of the background audio analysis, at most once a second. Requires MANAGE_LIBRARIES.")]
    public static IObservable<AnalysisProgress> AnalysisProgressChanged([FromServices] AnalysisService analysis) => analysis.Watch();
}
