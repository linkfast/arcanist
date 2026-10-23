using Arcanist.Core.Extensions;

namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>Whether the official extension that times lyrics on this server is here and working.</summary>
public enum LyricsTimingExtension
{
    /// <summary>Not installed: the panel offers it from the marketplace when there are lyrics to time.</summary>
    NotInstalled,
    /// <summary>Installed, but off or waiting for a restart.</summary>
    Installed,
    Running,
}

/// <param name="WithLyrics">Songs with lyrics.</param>
/// <param name="Timed">Of those, with timing (from their files, LRCLib or worked out here).</param>
/// <param name="Aligned">Of those, worked out here.</param>
/// <param name="Untimed">Songs whose plain lyrics an extension can time now.</param>
/// <param name="Waiting">Songs with plain lyrics not ready for that yet: LRCLib has not looked for them, or there is no copy on this server's disks.</param>
public sealed record LyricsTimingStatus(int WithLyrics, int Timed, int Aligned, int Untimed, int Waiting, string ExtensionId, LyricsTimingExtension Extension);

/// <summary>How many lyrics have timing and whether the extension that works it out is here, for the admin panel.</summary>
public sealed class LyricsTimingService(AccessGuard guard, LyricsTiming timing, ExtensionHost extensions)
{
    /// <summary>The official extension that times lyrics (Lyrics Sync), in the marketplace.</summary>
    public const string ExtensionId = "lyrics-sync";

    public async Task<LyricsTimingStatus> StatusAsync(CancellationToken cancellationToken)
    {
        guard.Admin();
        var counts = await timing.CountAsync(cancellationToken);
        var extension = extensions.Find(ExtensionId) switch
        {
            null => LyricsTimingExtension.NotInstalled,
            { Status: ExtensionStatus.Running } when extensions.IsEnabled(ExtensionId) => LyricsTimingExtension.Running,
            _ => LyricsTimingExtension.Installed,
        };
        return new LyricsTimingStatus(counts.WithLyrics, counts.Timed, counts.Aligned, counts.Untimed, counts.Waiting, ExtensionId, extension);
    }
}
