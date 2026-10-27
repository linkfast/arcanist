using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Media;

/// <summary>The conversion cache and the versions made ahead of time. Part of the API contract.</summary>
/// <param name="CacheBytes">What the cache takes now.</param>
/// <param name="Working">Making versions right now.</param>
/// <param name="FfmpegAvailable">Without ffmpeg nothing is converted.</param>
/// <param name="Formats">The formats the ffmpeg in use can make (HE-AAC only with an encoder for it).</param>
public sealed record TranscodingStatus(
    TranscodingConfig Settings,
    long CacheBytes,
    int CacheFiles,
    IReadOnlyList<PregenerateProgress> Pregenerated,
    bool Working,
    bool FfmpegAvailable,
    IReadOnlyList<TranscodeFormat> Formats);

public sealed record TranscodeTargetInput(TranscodeFormat Format, int BitRate);

public sealed record SetTranscodingSettingsInput(
    bool CacheEnabled,
    long CacheMaxBytes,
    int CacheMaxAgeDays,
    IReadOnlyList<TranscodeTargetInput> Pregenerate,
    IReadOnlyList<Guid>? PregenerateLibraries,
    bool PregenerateOnlyLossless);

public sealed record ClientStreamingQualityInput(TranscodeFormat? Format, int? MaxBitRate);

public sealed record SetStreamingDefaultsInput(ClientStreamingQualityInput Desktop, ClientStreamingQualityInput Web, ClientStreamingQualityInput Mobile);

/// <summary>The quality each kind of Ultimate client gets by default: any user reads it (their apps do), managers change it.</summary>
public sealed class StreamingDefaultsService(AccessGuard guard, StreamingDefaultsSettings settings)
{
    public Task<StreamingDefaults> GetAsync(CancellationToken cancellationToken)
    {
        guard.Authenticated();
        return settings.GetAsync(cancellationToken);
    }

    public Task<StreamingDefaults> SetAsync(SetStreamingDefaultsInput input, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        var errors = new ValidationErrors();
        foreach (var (field, quality) in new[] { ("desktop", input.Desktop), ("web", input.Web), ("mobile", input.Mobile) })
        {
            if (quality is null)
                errors.Add(field, ErrorCodes.Required);
            else if (quality.MaxBitRate is < 32 or > 320)
                errors.Add(field + ".maxBitRate", ErrorCodes.InvalidFormat);
        }

        errors.ThrowIfAny();
        return settings.SetAsync(new StreamingDefaults(Quality(input.Desktop), Quality(input.Web), Quality(input.Mobile)), cancellationToken);
    }

    private static ClientStreamingQuality Quality(ClientStreamingQualityInput input) => new(input.Format, input.MaxBitRate);
}

/// <summary>How converted audio is kept. Requires MANAGE_SETTINGS.</summary>
public sealed class TranscodingService(
    AccessGuard guard,
    ArcanistDbContext db,
    TranscodingSettings settings,
    TranscodeStore store,
    PretranscodeWorker worker,
    FfmpegLocator ffmpeg,
    Transcoder transcoder)
{
    public async Task<TranscodingStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        var (bytes, files) = store.Cache.Usage();
        var installation = await ffmpeg.GetAsync(cancellationToken);
        return new TranscodingStatus(await settings.GetAsync(cancellationToken), bytes, files, worker.Progress, worker.Working,
            installation is not null, transcoder.Formats(installation));
    }

    public async Task<TranscodingStatus> SetAsync(SetTranscodingSettingsInput input, CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        var errors = new ValidationErrors();
        if (input.CacheMaxBytes < TranscodingConfig.MinCacheBytes)
            errors.Add("cacheMaxBytes", ErrorCodes.InvalidFormat);
        if (input.CacheMaxAgeDays is < 1 or > 3650)
            errors.Add("cacheMaxAgeDays", ErrorCodes.InvalidFormat);
        if (input.Pregenerate.Count > 6)
            errors.Add("pregenerate", ErrorCodes.TooMany);
        // HE-AAC only with an encoder for it: most ffmpeg builds have none, and versions it cannot make never come.
        var installation = await ffmpeg.GetAsync(cancellationToken);
        if (input.Pregenerate.Any(t => t.Format == TranscodeFormat.HeAac
                                       && (installation is null || TranscodeProfile.For(t.Format).EncoderIn(installation.Encoders) is null)))
            errors.Add("pregenerate", ErrorCodes.TranscodingUnavailable);
        if (input.PregenerateLibraries is { Count: > 0 } libraries
            && await db.Libraries.CountAsync(l => libraries.Contains(l.Id), cancellationToken) != libraries.Distinct().Count())
            errors.Add("pregenerateLibraries", ErrorCodes.NotFound);
        errors.ThrowIfAny();

        await settings.SetAsync(new TranscodingConfig(input.CacheEnabled, input.CacheMaxBytes, input.CacheMaxAgeDays,
            input.Pregenerate.Select(t => new TranscodeTarget(t.Format, t.BitRate)).ToList(),
            input.PregenerateLibraries is { Count: > 0 } chosen ? chosen : null, input.PregenerateOnlyLossless), cancellationToken);
        if (!input.CacheEnabled)
            store.Cache.Clear();
        store.Cache.Trim();
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Empties the conversion cache (the versions made ahead of time stay).</summary>
    public async Task<TranscodingStatus> ClearCacheAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        store.Cache.Clear();
        return await StatusAsync(cancellationToken);
    }
}
