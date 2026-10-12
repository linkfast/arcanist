using Arcanist.Core.Application.Analysis;

namespace Arcanist.Core.Api.Rest;

public sealed record SetAnalysisEnabledInput(bool Enabled);

public static class AnalysisEndpoints
{
    public static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder rest)
    {
        var analysis = rest.MapGroup("/analysis").WithTags("Analysis");

        analysis.MapGet("", (AnalysisService service) => service.Progress())
            .WithName("AnalysisProgress")
            .WithSummary("Progress of the background audio analysis (loudness, fingerprints, waveforms, tempo and key). Poll it to follow progress. Requires MANAGE_LIBRARIES.");

        analysis.MapPut("/enabled", (SetAnalysisEnabledInput input, AnalysisService service, CancellationToken ct) => service.SetEnabledAsync(input.Enabled, ct))
            .WithName("SetAnalysisEnabled")
            .WithSummary("Turns the background audio analysis on or off. Requires MANAGE_SETTINGS.");

        return rest;
    }
}
