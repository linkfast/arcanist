using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Analysis;

/// <summary>
/// Album loudness per EBU R128 / ITU-R BS.1770: the gated loudness of all the album's gating blocks together,
/// combined from the per-track histograms the analyzer stores (0.1 LU bins from −70 LUFS), so tracks are never
/// decoded again. ReplayGain 2.0 album gain is then −18 LUFS minus this value.
/// </summary>
public static class AlbumLoudness
{
    private const int Bins = 1000;

    /// <summary>Gated integrated loudness of the combined histograms, or null when they hold no audible block.</summary>
    public static double? Integrate(IEnumerable<string> histograms)
    {
        var counts = new long[Bins];
        foreach (var histogram in histograms)
        {
            var bytes = Convert.FromBase64String(histogram);
            for (var i = 0; i + 6 <= bytes.Length; i += 6)
            {
                var bin = BitConverter.ToUInt16(bytes, i);
                if (bin < Bins)
                    counts[bin] += BitConverter.ToUInt32(bytes, i + 2);
            }
        }

        // Every stored block already passed the absolute gate (−70 LUFS).
        var (total, energy) = Sum(counts, firstBin: 0);
        if (total == 0)
            return null;

        var relativeGate = Loudness(energy / total) - 10;
        var firstBin = Math.Clamp((int)Math.Ceiling((relativeGate + 70) * 10 - 0.5), 0, Bins - 1);
        (total, energy) = Sum(counts, firstBin);
        return total == 0 ? null : Loudness(energy / total);
    }

    /// <summary>
    /// Recomputes <c>Album.Loudness</c> and <c>Album.TruePeak</c> for the given albums from the best present copy of
    /// each track. Left null until every track has been analyzed, so a partial album never gets a misleading gain.
    /// </summary>
    public static async Task UpdateAsync(ArcanistDbContext db, IReadOnlyCollection<Guid> albumIds, CancellationToken cancellationToken)
    {
        if (albumIds.Count == 0)
            return;

        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && albumIds.Contains(f.Track.AlbumId))
            .Select(f => new { f.TrackId, f.Track.AlbumId, f.ContentHash, f.BitDepth, f.SampleRate, f.BitRate })
            .ToListAsync(cancellationToken);
        var hashes = files.Select(f => f.ContentHash).Distinct().ToList();
        var analyses = await db.AudioAnalyses.AsNoTracking()
            .Where(a => hashes.Contains(a.ContentHash) && a.LoudnessHistogram != null)
            .Select(a => new { a.ContentHash, a.LoudnessHistogram, a.TruePeak })
            .ToDictionaryAsync(a => a.ContentHash, cancellationToken);

        var albums = await db.Albums.Where(a => albumIds.Contains(a.Id)).ToListAsync(cancellationToken);
        foreach (var album in albums)
        {
            // The same choice as playback: the best quality copy of each track.
            var best = files.Where(f => f.AlbumId == album.Id)
                .GroupBy(f => f.TrackId)
                .Select(g => g.OrderByDescending(f => f.BitDepth ?? 0).ThenByDescending(f => f.SampleRate ?? 0).ThenByDescending(f => f.BitRate ?? 0).First())
                .ToList();

            var measured = best.Select(f => analyses.GetValueOrDefault(f.ContentHash)).ToList();
            if (measured.Count == 0 || measured.Any(a => a is null))
            {
                album.Loudness = null;
                album.TruePeak = null;
                continue;
            }

            album.Loudness = Integrate(measured.Select(a => a!.LoudnessHistogram!));
            album.TruePeak = measured.Max(a => a!.TruePeak);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static (long Total, double Energy) Sum(long[] counts, int firstBin)
    {
        long total = 0;
        double energy = 0;
        for (var bin = firstBin; bin < Bins; bin++)
        {
            if (counts[bin] == 0)
                continue;
            total += counts[bin];
            energy += counts[bin] * Energy(-70 + (bin + 0.5) * 0.1);
        }

        return (total, energy);
    }

    private static double Energy(double loudness) => Math.Pow(10, (loudness + 0.691) / 10);

    private static double Loudness(double energy) => -0.691 + 10 * Math.Log10(energy);
}
