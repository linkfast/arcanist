using System.Buffers.Binary;
using Arcanist.Core.Application;
using Arcanist.Core.Application.Ai;
using Arcanist.Core.Application.Listening;
using Arcanist.Core.Application.Streaming;
using Arcanist.Core.Media;
using Arcanist.Core.Storage;

namespace Arcanist.Core.Api.Rest;

public static class AiEndpoints
{
    /// <summary>The longest audio window, in seconds.</summary>
    public const double MaxWindowSeconds = 8;

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder rest)
    {
        rest.MapGet("/tracks/{trackId:guid}/analysis", async (Guid trackId, string? include, TrackAnalysisService service, CancellationToken ct) =>
            {
                var result = await service.AnalysisAsync(trackId, include, ct);
                return result.Status == TrackAnalysisStatus.Pending ? Results.Json(result, statusCode: StatusCodes.Status202Accepted) : Results.Ok(result);
            })
            .WithTags("AI")
            .WithName("TrackAnalysis")
            .WithSummary("A song's analysis for DJ software (AI extension): beat grid, sections, energy, voice, mix and scratch points… `include` picks blocks (beats, onsets, energy, vocals, sections, silences, cues, loudness, key, stats; all but stats by default). 202 with status PENDING and its position in line until it is ready.")
            .Produces<TrackAnalysisResult>()
            .Produces<TrackAnalysisResult>(StatusCodes.Status202Accepted);

        rest.MapPost("/tracks/{trackId:guid}/analysis/prioritize", (Guid trackId, TrackAnalysisService service, CancellationToken ct) =>
                service.PrioritizeAsync(trackId, ct))
            .WithTags("AI")
            .WithName("PrioritizeTrackAnalysis")
            .WithSummary("Puts a song first in line for its analysis (it is going to be played soon).");

        rest.MapGet("/tracks/analysis", (DateTime? updatedSince, string? after, int? first, string? include, TrackAnalysisService service, CancellationToken ct) =>
                service.AnalysesAsync(updatedSince, after, first, include, ct))
            .WithTags("AI")
            .WithName("TrackAnalyses")
            .WithSummary("Analyses of the songs the user sees, from the least to the most recently analyzed (at most 200 a page): keep a cache with updatedSince set to the last analyzedAt held.");

        rest.MapGet("/tracks/{trackId:guid}/audio-window", AudioWindowAsync)
            .WithTags("AI")
            .WithName("TrackAudioWindow")
            .WithSummary("A short piece of a song, decoded (AI extension): `start` and `duration` (at most 8) in seconds, as a WAV file of 16-bit stereo at 44.1 kHz " +
                         "(`format=pcm16`, the only one). For scratching or previewing a part without downloading the song. Authenticate like /stream " +
                         "(Bearer, or `st` of a streamUrl). 503 transcoding_unavailable without ffmpeg.")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "audio/wav");

        // The scope is parsed here rather than bound: query binding of enums is case-sensitive, and the API spells them SERVER.
        rest.MapGet("/stats/tracks", (string? scope, DateTime? updatedSince, string? after, int? first, string? timeZone, TrackStatsService service,
                CancellationToken ct) => service.StatsAsync(Scope(scope), updatedSince, after, first, timeZone, ct))
            .WithTags("Listening")
            .WithName("TrackStats")
            .WithSummary("Listening statistics per song (plays, skips and where, completions, how much is listened to, hours of the day): the user's, or the server's (everyone's together). From the least to the most recently played.");

        rest.MapGet("/ai", (AiAdminService service, CancellationToken ct) => service.StatusAsync(ct))
            .WithTags("AI")
            .WithName("AiStatus")
            .WithSummary("What the AI extension's analysis is doing, its progress and settings. Administrators only.");

        rest.MapPut("/ai/settings", (SetAiSettingsInput input, AiAdminService service, CancellationToken ct) => service.SetSettingsAsync(input, ct))
            .WithTags("AI")
            .WithName("SetAiSettings")
            .WithSummary("Changes the AI extension's settings. Administrators only, with a recent authentication.");

        rest.MapPost("/ai/reanalyze", (AiAdminService service, CancellationToken ct) => service.ReanalyzeAsync(ct))
            .WithTags("AI")
            .WithName("ReanalyzeTracks")
            .WithSummary("Analyzes every song again; the current analyses are served (as stale) until each is redone. Administrators only, with a recent authentication.");

        return rest;
    }

    private static TrackStatsScope? Scope(string? scope) =>
        string.IsNullOrEmpty(scope) ? null
        : Enum.TryParse<TrackStatsScope>(scope, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
        : throw ArcanistException.Field("scope", ErrorCodes.InvalidFormat);

    private static async Task<IResult> AudioWindowAsync(Guid trackId, double start, double duration, string? format, HttpContext context, CancellationToken cancellationToken)
    {
        await MediaEndpoints.AuthenticateStreamTokenAsync(context, cancellationToken);
        context.RequestServices.GetRequiredService<TrackAnalysisService>().RequireEnabled();
        if (format is not null && !string.Equals(format, "pcm16", StringComparison.OrdinalIgnoreCase))
            throw ArcanistException.Field("format", ErrorCodes.InvalidFormat);
        if (start < 0 || double.IsNaN(start))
            throw ArcanistException.Field("start", ErrorCodes.InvalidFormat);
        if (duration <= 0 || duration > MaxWindowSeconds || double.IsNaN(duration))
            throw ArcanistException.Field("duration", ErrorCodes.InvalidFormat);

        var target = await context.RequestServices.GetRequiredService<StreamService>().ResolveAsync(trackId, null, false, cancellationToken);
        if (start >= target.DurationMs / 1000.0)
            throw ArcanistException.Field("start", ErrorCodes.InvalidFormat);
        var storage = context.RequestServices.GetRequiredService<ILibraryStorageResolver>().For(target.Location.Kind);
        // Another server's songs are read as it streams them (no permission to download needed).
        Func<Task<Stream>> open = storage is P2P.PeerStorage peer
            ? () => peer.OpenStreamAsync(target.Location, target.RelativePath, cancellationToken)
            : () => storage.OpenReadAsync(target.Location, target.RelativePath, cancellationToken);
        var offset = (target.StartMs ?? 0) / 1000.0 + start;
        var length = Math.Min(duration, target.DurationMs / 1000.0 - start);

        var transcoder = context.RequestServices.GetRequiredService<Transcoder>();
        await using var session = await transcoder.StartAsync(storage.TryGetLocalPath(target.Location, target.RelativePath), open, TranscodeProfile.Pcm16, 0,
                                      offset, cancellationToken, length)
                                  ?? throw new ArcanistException.Unavailable(ErrorCodes.TranscodingUnavailable);

        // At most 8 seconds: ~1.4 MB, read whole so the WAV header can say how long it is.
        var limit = (int)(MaxWindowSeconds * 44100 * 4) + 4096;
        var samples = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await session.Output.ReadAsync(buffer, cancellationToken)) > 0 && samples.Length < limit)
            samples.Write(buffer, 0, Math.Min(read, limit - (int)samples.Length));

        var data = (int)samples.Length - (int)samples.Length % 4;
        var wav = new byte[44 + data];
        WriteHeader(wav, data);
        samples.GetBuffer().AsSpan(0, data).CopyTo(wav.AsSpan(44));
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(wav, "audio/wav");
    }

    /// <summary>The 44-byte header of a WAV file of 16-bit stereo PCM at 44.1 kHz.</summary>
    internal static void WriteHeader(Span<byte> header, int dataBytes)
    {
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 44100);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], 44100 * 4);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 4);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataBytes);
    }
}
