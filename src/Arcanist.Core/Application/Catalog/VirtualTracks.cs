using Arcanist.Core.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Data.Search;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// Tracks whose metadata lives only in the catalog: they play a file whose tags describe another track
/// (<see cref="MediaFile.IsSharedCopy"/>). The same audio with other metadata costs no space and no file is rewritten for
/// it. They go through the catalog writer like a scanned file would, so they join albums, artists and genres as any other.
/// </summary>
internal static class VirtualTracks
{
    /// <summary>What the catalog says of a track, in the terms of an edit. <paramref name="Featured"/> has no field of its own there.</summary>
    public sealed record Description(TrackEditInput Input, IReadOnlyList<string> Featured, bool IsCompilation);

    public static async Task<Description> DescribeAsync(ArcanistDbContext db, Guid trackId, CancellationToken cancellationToken)
    {
        var facts = await db.Tracks.AsNoTracking().Where(t => t.Id == trackId)
            .Select(t => new
            {
                t.Title,
                t.TrackNumber,
                t.DiscNumber,
                t.Comment,
                t.TaggedBpm,
                Album = t.Album.Title,
                AlbumArtist = t.Album.AlbumArtist.Name,
                t.Album.Year,
                t.Album.IsCompilation,
                Release = t.Album.MusicBrainzReleaseId,
                Recording = t.Recording.MusicBrainzId,
            })
            .SingleAsync(cancellationToken);
        var credits = await db.TrackArtists.AsNoTracking().Where(a => a.TrackId == trackId)
            .OrderBy(a => a.Position).Select(a => new { a.Role, a.Artist.Name }).ToListAsync(cancellationToken);
        var genres = await db.TrackGenres.AsNoTracking().Where(g => g.TrackId == trackId).Select(g => g.Genre.Name).ToListAsync(cancellationToken);
        var lyrics = await db.Lyrics.AsNoTracking().Where(l => l.TrackId == trackId).Select(l => l.Synced ?? l.Plain).FirstOrDefaultAsync(cancellationToken);
        List<string> Named(ArtistRole role) => credits.Where(c => c.Role == role).Select(c => c.Name).ToList();

        return new Description(
            new TrackEditInput(
                facts.Title, Named(ArtistRole.Primary), facts.AlbumArtist, facts.Album, genres, facts.Year, facts.TrackNumber, facts.DiscNumber,
                Named(ArtistRole.Composer), Named(ArtistRole.Lyricist), Named(ArtistRole.Conductor).FirstOrDefault(), facts.Comment, facts.TaggedBpm,
                lyrics, facts.Recording, facts.Release),
            Named(ArtistRole.Featured),
            facts.IsCompilation);
    }

    /// <summary>The track's metadata with the edit's changes (null keeps a field; "", [], 0 or the nil UUID empty it).</summary>
    private static AudioMetadata Metadata(Description description, TrackEditInput changes, MediaFile file)
    {
        var d = description.Input;
        static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static int? Number(int? value) => value > 0 ? value : null;
        static Guid? Id(Guid? value) => value == Guid.Empty ? null : value;

        return new AudioMetadata
        {
            Title = Text(changes.Title ?? d.Title),
            Artists = changes.Artists ?? d.Artists ?? [],
            FeaturedArtists = changes.Artists is null ? description.Featured : [],
            AlbumArtist = Text(changes.AlbumArtist ?? d.AlbumArtist),
            Album = Text(changes.Album ?? d.Album),
            Genres = changes.Genres ?? d.Genres ?? [],
            Year = Number(changes.Year ?? d.Year),
            TrackNumber = Number(changes.TrackNumber ?? d.TrackNumber),
            DiscNumber = Number(changes.DiscNumber ?? d.DiscNumber),
            Composers = changes.Composers ?? d.Composers ?? [],
            Lyricists = changes.Lyricists ?? d.Lyricists ?? [],
            Conductor = Text(changes.Conductor ?? d.Conductor),
            Comment = Text(changes.Comment ?? d.Comment),
            Bpm = (changes.Bpm ?? d.Bpm) is > 0 and var bpm ? bpm : null,
            RecordingMbid = Id(changes.MusicBrainzRecordingId ?? d.MusicBrainzRecordingId),
            ReleaseMbid = Id(changes.MusicBrainzReleaseId ?? d.MusicBrainzReleaseId),
            IsCompilation = description.IsCompilation,
            Format = file.Format,
            BitRate = file.BitRate,
            SampleRate = file.SampleRate,
            BitDepth = file.BitDepth,
            Channels = file.Channels,
            DurationMs = file.DurationMs,
            ReplayGainTrackGain = file.ReplayGainTrackGain,
            ReplayGainTrackPeak = file.ReplayGainTrackPeak,
            ReplayGainAlbumGain = file.ReplayGainAlbumGain,
            ReplayGainAlbumPeak = file.ReplayGainAlbumPeak,
        };
    }

    /// <summary>Applies an edit to a track that only plays another's file: nothing is written but the catalog.</summary>
    public static async Task EditAsync(IArcanistDbContextFactory databases, TimeProvider time, Guid fileId, TrackEditInput changes, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var file = await db.MediaFiles.SingleAsync(f => f.Id == fileId, cancellationToken);
        var metadata = Metadata(await DescribeAsync(db, file.TrackId, cancellationToken), changes, file);

        if (changes.Lyrics is { } lyrics)
        {
            // The writer keeps lyrics it trusts more than an edit's; these belong to no file, so the edit decides.
            await db.Lyrics.Where(l => l.TrackId == file.TrackId).ExecuteDeleteAsync(cancellationToken);
            var text = lyrics.Replace("\r\n", "\n").Trim();
            if (text.Length > 0)
            {
                metadata = Lrc.LooksTimed(text)
                    ? metadata with { SyncedLyrics = text, Lyrics = string.Join('\n', Lrc.Parse(text).Select(l => l.Text)), LyricsSource = LyricsSource.Embedded }
                    : metadata with { Lyrics = text, LyricsSource = LyricsSource.Embedded };
            }
        }

        // A locked file does not get to change its track, and this is that change.
        file.TrackLocked = false;
        await db.SaveChangesAsync(cancellationToken);
        await WriteAsync(db, time, new ScannedFile(file.LibrarySourceId, file.Id, file.RelativePath, file.Size, file.ModifiedAt, file.ContentHash, metadata, null, file.CueTrack), cancellationToken);
    }

    /// <summary>
    /// Audio the library has, arriving with the metadata of another album: a track of that album that plays the file
    /// already there, instead of a second copy. Null when there is no such file or the album has this audio already.
    /// </summary>
    public static async Task<Guid?> AddAsync(
        IArcanistDbContextFactory databases, TimeProvider time, Guid libraryId, string contentHash, AudioMetadata metadata, StoredArtwork? artwork, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Album))
            return null;

        await using var db = databases.CreateDbContext();
        var rows = await db.MediaFiles.AsNoTracking()
            .Where(f => f.ContentHash == contentHash && f.MissingSince == null && f.LibrarySource.LibraryId == libraryId && f.CueTrack <= 0)
            .Select(f => new
            {
                File = f,
                f.LibrarySource.Kind,
                Album = f.Track.Album.NormalizedTitle,
                AlbumArtist = f.Track.Album.AlbumArtist.NormalizedName,
                f.Track.Album.IsCompilation,
            })
            .ToListAsync(cancellationToken);
        var owner = rows.FirstOrDefault(r => r.File.CueTrack == 0 && r.Kind is LibrarySourceKind.Local or LibrarySourceKind.S3);
        if (owner is null)
            return null;

        var album = TextNormalizer.Normalize(metadata.Album);
        var albumArtist = metadata.AlbumArtist ?? metadata.Artists.FirstOrDefault();
        if (rows.Any(r => r.Album == album && (r.IsCompilation || albumArtist is null || r.AlbumArtist == TextNormalizer.Normalize(albumArtist))))
            return null;

        var file = owner.File;
        var slots = await db.MediaFiles.AsNoTracking().Where(f => f.LibrarySourceId == file.LibrarySourceId && f.RelativePath == file.RelativePath)
            .Select(f => f.CueTrack).ToListAsync(cancellationToken);
        var slot = SharedFiles.NextSlot(slots);
        // Its tags are not in any file: what describes it is the catalog, and it has what the file's would have.
        var described = metadata with
        {
            EmbeddedPicture = null,
            AlbumArtist = metadata.IsCompilation ? metadata.AlbumArtist : albumArtist,
            Format = file.Format,
            BitRate = file.BitRate,
            SampleRate = file.SampleRate,
            BitDepth = file.BitDepth,
            Channels = file.Channels,
            DurationMs = file.DurationMs,
        };
        await WriteAsync(db, time, new ScannedFile(file.LibrarySourceId, null, file.RelativePath, file.Size, file.ModifiedAt, file.ContentHash, described, artwork, slot), cancellationToken);
        return await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibrarySourceId == file.LibrarySourceId && f.RelativePath == file.RelativePath && f.CueTrack == slot)
            .Select(f => f.TrackId).SingleAsync(cancellationToken);
    }

    private static async Task WriteAsync(ArcanistDbContext db, TimeProvider time, ScannedFile file, CancellationToken cancellationToken)
    {
        var writer = new CatalogWriter(db, time);
        await writer.InitializeAsync(cancellationToken);
        await writer.ApplyAsync([file], cancellationToken);
        await SearchIndexer.RefreshAsync(db, cancellationToken);

        db.ChangeTracker.Clear();
        await db.MediaFiles.Where(f => f.LibrarySourceId == file.SourceId && f.RelativePath == file.RelativePath && f.CueTrack < 0)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.TrackLocked, true), cancellationToken);
        await SharedFiles.SyncAsync(db, file.SourceId, cancellationToken);
    }
}
