using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A track of an app's own library, described as the app knows it.</summary>
/// <param name="Key">The app's own id for it, returned with the match.</param>
/// <param name="ContentHash">Arcanist's hash of the audio, when the app learnt it (an upload returns it).</param>
/// <param name="AcoustId">The AcoustID the app identified it as.</param>
public sealed record TrackCandidate(
    string Key,
    string? ContentHash,
    string? AcoustId,
    Guid? MusicBrainzRecordingId,
    string? Title,
    string? Artist,
    string? Album,
    int? DiscNumber,
    int? TrackNumber,
    int? DurationMs);

/// <param name="LibraryId">Only tracks with a copy in this library.</param>
public sealed record MatchTracksInput(IReadOnlyList<TrackCandidate> Candidates, Guid? LibraryId);

/// <param name="TrackId">The server's track; null when it has none like it.</param>
/// <param name="MatchedBy">What made them the same track.</param>
/// <param name="Pending">Not found yet, but it may be soon: its audio was uploaded and the library has not been scanned
/// since, or a scan is going on. Ask again later.</param>
public sealed record TrackMatch(string Key, Guid? TrackId, TrackMatchKind? MatchedBy, bool Pending = false);

public enum TrackMatchKind
{
    /// <summary>The same audio.</summary>
    ContentHash,

    /// <summary>The same MusicBrainz recording.</summary>
    MusicBrainz,

    /// <summary>The same AcoustID.</summary>
    AcoustId,

    /// <summary>The same title, artist and album (and duration, give or take three seconds).</summary>
    Tags,
}

/// <summary>
/// Finds the server's tracks for an app's own (migrating a library, playlists that name local tracks…), among those
/// the caller can play: by audio, MusicBrainz recording, AcoustID and, last, title, artist and album.
/// </summary>
public sealed class TrackMatchService(ArcanistDbContext db, AccessGuard guard, CatalogService catalog, Libraries.UploadScans uploads)
{
    public const int MaxCandidates = 1000;

    private static readonly int DurationTolerance = 3_000;

    public async Task<IReadOnlyList<TrackMatch>> MatchAsync(MatchTracksInput input, CancellationToken cancellationToken)
    {
        var access = guard.Authenticated();
        if (input.Candidates.Count > MaxCandidates)
            throw ArcanistException.Field("candidates", ErrorCodes.TooMany);

        var visible = catalog.Visible(access, input.LibraryId);
        var found = await FindAsync(visible, input.Candidates, cancellationToken);

        // Uploaded audio becomes a track once its library is scanned: until then, say so.
        var scanning = false;
        if (input.Candidates.Any(c => c.ContentHash is not null && !found.ContainsKey(c.Key)))
        {
            var libraries = input.LibraryId is { } one ? [one] : access.IsAdmin ? null : access.LibraryIds.ToList();
            scanning = await db.ScanJobs.AnyAsync(j => (j.Status == Data.Entities.ScanStatus.Queued || j.Status == Data.Entities.ScanStatus.Running)
                                                       && (libraries == null || libraries.Contains(j.LibraryId)), cancellationToken);
        }

        return input.Candidates
            .Select(c => found.TryGetValue(c.Key, out var m)
                ? new TrackMatch(c.Key, m.TrackId, m.By)
                : new TrackMatch(c.Key, null, null, c.ContentHash is { } hash && (scanning || uploads.IsPendingAnywhere(hash))))
            .ToList();
    }

    /// <summary>
    /// The matching itself, among <paramref name="tracks"/>: by audio, MusicBrainz recording, AcoustID and, last,
    /// title, artist and album. Candidates may share a key (several hashes of one track): the first match counts.
    /// Also restoring backups, which matches every track of the server.
    /// </summary>
    internal static async Task<Dictionary<string, (Guid TrackId, TrackMatchKind By)>> FindAsync(
        IQueryable<Data.Entities.Track> visible, IReadOnlyList<TrackCandidate> candidates, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, (Guid TrackId, TrackMatchKind By)>(StringComparer.Ordinal);

        var hashes = candidates.Select(c => c.ContentHash).OfType<string>().Distinct().ToList();
        if (hashes.Count > 0)
        {
            var byHash = (await visible.SelectMany(t => t.Files)
                    .Where(f => hashes.Contains(f.ContentHash) && f.MissingSince == null)
                    .Select(f => new { f.ContentHash, f.TrackId })
                    .ToListAsync(cancellationToken))
                .ToLookup(f => f.ContentHash, f => f.TrackId);
            foreach (var c in candidates)
                if (c.ContentHash is { } hash && byHash[hash].FirstOrDefault() is var id && id != Guid.Empty)
                    found.TryAdd(c.Key, (id, TrackMatchKind.ContentHash));
        }

        var mbids = candidates.Where(c => !found.ContainsKey(c.Key)).Select(c => c.MusicBrainzRecordingId).OfType<Guid>().Distinct().ToList();
        if (mbids.Count > 0)
        {
            var byMbid = (await visible.Where(t => t.Recording != null && t.Recording.MusicBrainzId != null && mbids.Contains(t.Recording.MusicBrainzId.Value))
                    .Select(t => new { Mbid = t.Recording!.MusicBrainzId!.Value, t.Id })
                    .ToListAsync(cancellationToken))
                .ToLookup(t => t.Mbid, t => t.Id);
            foreach (var c in candidates)
                if (c.MusicBrainzRecordingId is { } mbid && byMbid[mbid].FirstOrDefault() is var id && id != Guid.Empty)
                    found.TryAdd(c.Key, (id, TrackMatchKind.MusicBrainz));
        }

        var acoustIds = candidates.Where(c => !found.ContainsKey(c.Key))
            .Select(c => Guid.TryParse(c.AcoustId, out var a) ? a : (Guid?)null).OfType<Guid>().Distinct().ToList();
        if (acoustIds.Count > 0)
        {
            var byAcoustId = (await visible.Where(t => t.Recording != null && t.Recording.AcoustId != null && acoustIds.Contains(t.Recording.AcoustId.Value))
                    .Select(t => new { AcoustId = t.Recording!.AcoustId!.Value, t.Id })
                    .ToListAsync(cancellationToken))
                .ToLookup(t => t.AcoustId, t => t.Id);
            foreach (var c in candidates)
                if (Guid.TryParse(c.AcoustId, out var acoustId) && byAcoustId[acoustId].FirstOrDefault() is var id && id != Guid.Empty)
                    found.TryAdd(c.Key, (id, TrackMatchKind.AcoustId));
        }

        var byTitle = candidates.Where(c => !found.ContainsKey(c.Key) && !string.IsNullOrWhiteSpace(c.Title))
            .ToLookup(c => TextNormalizer.Normalize(c.Title!));
        var titles = byTitle.Select(g => g.Key).Where(t => t.Length > 0).ToList();
        if (titles.Count > 0)
        {
            var rows = await visible.Where(t => titles.Contains(t.NormalizedTitle))
                .Select(t => new
                {
                    t.Id,
                    t.NormalizedTitle,
                    Album = t.Album.NormalizedTitle,
                    AlbumArtist = t.Album.AlbumArtist.NormalizedName,
                    Artists = t.Artists.Select(a => a.Artist.NormalizedName).ToList(),
                    t.DiscNumber,
                    t.TrackNumber,
                    t.DurationMs,
                })
                .ToListAsync(cancellationToken);

            foreach (var group in byTitle)
            foreach (var c in group)
            {
                var artist = Normalized(c.Artist);
                var album = Normalized(c.Album);
                var match = rows
                    .Where(r => r.NormalizedTitle == group.Key)
                    .Where(r => artist is null || r.AlbumArtist == artist || r.Artists.Contains(artist) || r.Artists.Any(a => artist.Contains(a)))
                    .Where(r => album is null || r.Album == album)
                    .Where(r => c.DurationMs is not { } ms || Math.Abs(r.DurationMs - ms) <= DurationTolerance)
                    // The same disc and number first, when the app knows them.
                    .OrderBy(r => c.TrackNumber is { } n && r.TrackNumber == n && (c.DiscNumber ?? 1) == (r.DiscNumber ?? 1) ? 0 : 1)
                    .FirstOrDefault();
                if (match is not null)
                    found.TryAdd(c.Key, (match.Id, TrackMatchKind.Tags));
            }
        }

        return found;
    }

    private static string? Normalized(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : TextNormalizer.Normalize(text) is { Length: > 0 } n ? n : null;
}
