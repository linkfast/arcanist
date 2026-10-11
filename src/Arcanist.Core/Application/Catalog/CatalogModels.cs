using System.Text;
using Arcanist.Core.Data.Entities;

namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

public sealed record Artist(Guid Id, string Name, string? SortName, Guid? MusicBrainzId);

public sealed record ArtistReference(Guid Id, string Name);

/// <param name="ArtworkId">Cover image, served by the artwork endpoint.</param>
/// <param name="TrackCount">Tracks of the album the user can play.</param>
public sealed record Album(
    Guid Id,
    string Title,
    ArtistReference Artist,
    int? Year,
    bool IsCompilation,
    Guid? MusicBrainzId,
    Guid? ArtworkId,
    int TrackCount,
    long DurationMs);

public sealed record AlbumReference(Guid Id, string Title);

public sealed record ArtistCredit(Guid ArtistId, string Name, ArtistRole Role);

/// <summary>One stored copy of a track. A track can have several (e.g. FLAC and MP3).</summary>
public sealed record TrackFile(
    Guid Id,
    string? Format,
    int? BitRate,
    int? SampleRate,
    int? BitDepth,
    int? Channels,
    long Size,
    int DurationMs,
    double? ReplayGainTrackGain,
    double? ReplayGainAlbumGain);

/// <param name="ArtworkId">The track's own image, or else its album's.</param>
/// <param name="Files">Copies of the track in libraries the user can access, best quality first.</param>
public sealed record Track(
    Guid Id,
    string Title,
    AlbumReference Album,
    IReadOnlyList<ArtistCredit> Artists,
    int? DiscNumber,
    int? TrackNumber,
    int DurationMs,
    Guid? ArtworkId,
    IReadOnlyList<string> Genres,
    DateTime AddedAt,
    IReadOnlyList<TrackFile> Files);

/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record ArtistPage(IReadOnlyList<Artist> Items, string? NextCursor, int TotalCount);

public sealed record AlbumPage(IReadOnlyList<Album> Items, string? NextCursor, int TotalCount);

public sealed record TrackPage(IReadOnlyList<Track> Items, string? NextCursor, int TotalCount);

/// <summary>Opaque keyset cursor: the sort key and id of the last item of a page.</summary>
internal static class Cursor
{
    public static string Encode(string key, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id:N}{key}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (string Key, Guid Id) Decode(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return (text[32..], Guid.ParseExact(text[..32], "N"));
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
        {
            throw ArcanistException.Field("after", ErrorCodes.InvalidFormat);
        }
    }
}
