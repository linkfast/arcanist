using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Media;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Media;

/// <summary>The conversion cache and the versions made ahead of time. Part of the API contract.</summary>
/// <param name="CacheBytes">What the cache takes now.</param>
/// <param name="Working">Making versions right now.</param>
/// <param name="FfmpegAvailable">Without ffmpeg nothing is converted.</param>
public sealed record TranscodingStatus(
    TranscodingConfig Settings,
    long CacheBytes,
    int CacheFiles,
    IReadOnlyList<PregenerateProgress> Pregenerated,
    bool Working,
    bool FfmpegAvailable);

public sealed record TranscodeTargetInput(TranscodeFormat Format, int BitRate);

public sealed record SetTranscodingSettingsInput(
    bool CacheEnabled,
    long CacheMaxBytes,
    int CacheMaxAgeDays,
    IReadOnlyList<TranscodeTargetInput> Pregenerate,
    IReadOnlyList<Guid>? PregenerateLibraries,
    bool PregenerateOnlyLossless);

/// <summary>How converted audio is kept. Requires MANAGE_SETTINGS.</summary>
public sealed class TranscodingService(
    AccessGuard guard,
    ArcanistDbContext db,
    TranscodingSettings settings,
    TranscodeStore store,
    PretranscodeWorker worker,
    FfmpegLocator ffmpeg)
{
    public async Task<TranscodingStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Permission(Permissions.ManageSettings);
        var (bytes, files) = store.Cache.Usage();
        return new TranscodingStatus(await settings.GetAsync(cancellationToken), bytes, files, worker.Progress, worker.Working,
            await ffmpeg.GetAsync(cancellationToken) is not null);
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
