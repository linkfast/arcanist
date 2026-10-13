using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.Scanning;

/// <summary>A file read by the scanner, ready to be written to the catalog.</summary>
/// <param name="ExistingFileId">The catalog row of this path when the file was already known.</param>
public sealed record ScannedFile(
    Guid SourceId,
    Guid? ExistingFileId,
    string RelativePath,
    long Size,
    DateTime ModifiedAt,
    string ContentHash,
    AudioMetadata Metadata,
    StoredArtwork? Artwork,
    int CueTrack = 0,
    int? StartMs = null,
    int? EndMs = null);

public sealed record BatchResult(int Added, int Updated);

/// <summary>
/// Turns scanned files into catalog rows. The only writer during a scan: files arrive in batches, each applied in
/// one transaction. Lookups of artists, albums, recordings, genres and artwork are cached for the whole scan.
/// <para>
/// Duplicates collapse instead of multiplying: a file joins an existing track of its album when the MusicBrainz
/// recording matches, or else disc, number and title match (so a FLAC and an MP3 of the same song, or copies in two
/// libraries, are one track with two files). A re-tagged file keeps its track when it is the track's only file, so
/// user data attached to the track follows it.
/// </para>
/// </summary>
internal sealed class CatalogWriter(ArcanistDbContext db, TimeProvider time)
{
    public const string UnknownArtist = "Unknown Artist";
    public const string VariousArtists = "Various Artists";
    public const string UnknownAlbum = "Unknown Album";

    private readonly Dictionary<Guid, Guid> _artistByMbid = [];
    private readonly Dictionary<string, Guid> _artistByName = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _artistsWithoutMbid = [];
    private readonly Dictionary<Guid, AlbumState> _albumByRelease = [];
    private readonly Dictionary<(Guid ArtistId, string Title), AlbumState> _albumByKey = [];
    private readonly Dictionary<Guid, AlbumState> _albumById = [];
    // Albums with files directly in a folder, by title: how a folder of several artists' songs is recognized as a compilation.
    private readonly Dictionary<(Guid SourceId, string Folder, string Title), AlbumState> _albumByFolder = [];
    private readonly HashSet<(Guid SourceId, string Folder)> _foldersLoaded = [];
    // Albums merged into a compilation in this batch: their tracks move after saving.
    private readonly Dictionary<Guid, Guid> _albumMoves = [];
    private readonly Dictionary<Guid, Guid> _recordingByMbid = [];
    private readonly Dictionary<Guid, Guid> _mbidByRecording = [];
    private readonly Dictionary<string, Guid> _genreByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _artworkByHash = new(StringComparer.Ordinal);

    // Changes to rows created in earlier batches, applied with set-based updates.
    private readonly Dictionary<Guid, Guid> _pendingArtistMbids = [];
    private readonly HashSet<AlbumState> _dirtyAlbums = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var artists = await db.Artists.AsNoTracking().Select(a => new { a.Id, a.NormalizedName, a.MusicBrainzId }).ToListAsync(cancellationToken);
        foreach (var artist in artists)
        {
            _artistByName.TryAdd(artist.NormalizedName, artist.Id);
            if (artist.MusicBrainzId is { } mbid)
                _artistByMbid.TryAdd(mbid, artist.Id);
            else
                _artistsWithoutMbid.Add(artist.Id);
        }

        var albums = await db.Albums.AsNoTracking()
            .Select(a => new { a.Id, a.AlbumArtistId, a.NormalizedTitle, a.MusicBrainzReleaseId, a.ArtworkId, a.Year, a.IsCompilation })
            .ToListAsync(cancellationToken);
        foreach (var album in albums)
        {
            var state = new AlbumState(album.Id, null)
            {
                ArtworkId = album.ArtworkId,
                Year = album.Year,
                ReleaseMbid = album.MusicBrainzReleaseId,
                ArtistId = album.AlbumArtistId,
                IsCompilation = album.IsCompilation,
            };
            _albumByKey.TryAdd((album.AlbumArtistId, album.NormalizedTitle), state);
            _albumById[album.Id] = state;
            if (album.MusicBrainzReleaseId is { } release)
                _albumByRelease.TryAdd(release, state);
        }

        foreach (var recording in await db.Recordings.AsNoTracking().Where(r => r.MusicBrainzId != null)
                     .Select(r => new { r.Id, r.MusicBrainzId }).ToListAsync(cancellationToken))
        {
            _recordingByMbid.TryAdd(recording.MusicBrainzId!.Value, recording.Id);
            _mbidByRecording[recording.Id] = recording.MusicBrainzId.Value;
        }

        foreach (var genre in await db.Genres.AsNoTracking().Select(g => new { g.Id, g.NormalizedName }).ToListAsync(cancellationToken))
            _genreByName.TryAdd(genre.NormalizedName, genre.Id);

        foreach (var artwork in await db.Artworks.AsNoTracking().Select(a => new { a.Id, a.Hash }).ToListAsync(cancellationToken))
            _artworkByHash.TryAdd(artwork.Hash, artwork.Id);
    }

    public async Task<BatchResult> ApplyAsync(IReadOnlyList<ScannedFile> files, CancellationToken cancellationToken)
    {
        files = files.Select(WithFolderArtist).ToList();
        var now = time.GetUtcNow().UtcDateTime;
        int added = 0, updated = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var existingIds = files.Where(f => f.ExistingFileId is not null).Select(f => f.ExistingFileId!.Value).ToList();
        var existingFiles = await db.MediaFiles.Where(f => existingIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        await LoadFolderAlbumsAsync(files, cancellationToken);

        // Albums first, so the tracks they may share are loaded in one query.
        var resolved = files.Select(f => (File: f, Album: ResolveAlbum(f))).ToList();
        var albumIds = resolved.Select(r => r.Album.Id).Distinct().ToList();
        var currentTrackIds = existingFiles.Values.Select(f => f.TrackId).Distinct().ToList();

        var tracks = await db.Tracks.Where(t => albumIds.Contains(t.AlbumId) || currentTrackIds.Contains(t.Id)).ToListAsync(cancellationToken);
        var tracksById = tracks.ToDictionary(t => t.Id);
        var tracksByAlbum = tracks.GroupBy(t => t.AlbumId).ToDictionary(g => g.Key, g => g.ToList());
        var trackIds = tracks.Select(t => t.Id).ToList();
        var fileCounts = await db.MediaFiles.Where(f => trackIds.Contains(f.TrackId))
            .GroupBy(f => f.TrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TrackId, x => x.Count, cancellationToken);

        var lyrics = await db.Lyrics.Where(l => trackIds.Contains(l.TrackId)).ToDictionaryAsync(l => l.TrackId, cancellationToken);

        var existingTracksToRelink = new HashSet<Guid>();
        var links = new Dictionary<Guid, (List<TrackArtist> Artists, List<TrackGenre> Genres)>();

        foreach (var (file, album) in resolved)
        {
            var meta = file.Metadata;
            var title = meta.Title ?? Path.GetFileNameWithoutExtension(file.RelativePath);
            var normalizedTitle = TextNormalizer.Normalize(title);
            MediaFile? mediaFile = file.ExistingFileId is { } fileId ? existingFiles.GetValueOrDefault(fileId) : null;

            if (!tracksByAlbum.TryGetValue(album.Id, out var albumTracks))
                tracksByAlbum[album.Id] = albumTracks = [];

            // A file merged into its track by duplicate detection or an administrator stays there while its album is
            // the same, and does not get to change the track (the track's own files describe it).
            if (mediaFile is { TrackLocked: true } && tracksById.TryGetValue(mediaFile.TrackId, out var locked) && locked.AlbumId == album.Id)
            {
                UpdateFile(mediaFile, file, meta, now);
                updated++;
                continue;
            }

            if (mediaFile is not null)
                mediaFile.TrackLocked = false;

            var track = Match(albumTracks, meta, normalizedTitle);

            // Re-tagged file that is alone on its track: keep the track, so what users attached to it follows.
            if (track is null && mediaFile is not null && tracksById.TryGetValue(mediaFile.TrackId, out var current)
                && fileCounts.GetValueOrDefault(current.Id) <= 1)
            {
                track = current;
            }

            if (track is null)
            {
                track = new Track
                {
                    Title = title,
                    NormalizedTitle = normalizedTitle,
                    AlbumId = album.Id,
                    RecordingId = ResolveRecording(meta.RecordingMbid),
                    AddedAt = now,
                };
                db.Tracks.Add(track);
                tracksById[track.Id] = track;
                fileCounts[track.Id] = 0;
            }
            else if (db.Entry(track).State != EntityState.Added)
            {
                existingTracksToRelink.Add(track.Id);
            }

            if (track.AlbumId != album.Id)
            {
                tracksByAlbum.GetValueOrDefault(track.AlbumId)?.Remove(track);
                track.AlbumId = album.Id;
            }

            if (!albumTracks.Contains(track))
                albumTracks.Add(track);

            track.Title = title;
            track.NormalizedTitle = normalizedTitle;
            track.DiscNumber = meta.DiscNumber;
            track.TrackNumber = meta.TrackNumber;
            track.DurationMs = meta.DurationMs;
            track.UpdatedAt = now;
            track.SearchText = null;
            if (meta.RecordingMbid is { } recordingMbid && _mbidByRecording.GetValueOrDefault(track.RecordingId) != recordingMbid)
                track.RecordingId = ResolveRecording(recordingMbid);

            links[track.Id] = (BuildArtists(track.Id, meta), BuildGenres(track.Id, meta));

            if (mediaFile is null)
            {
                mediaFile = new MediaFile
                {
                    TrackId = track.Id,
                    LibrarySourceId = file.SourceId,
                    RelativePath = file.RelativePath,
                    CueTrack = file.CueTrack,
                    ContentHash = file.ContentHash,
                    AddedAt = now,
                };
                db.MediaFiles.Add(mediaFile);
                fileCounts[track.Id] = fileCounts.GetValueOrDefault(track.Id) + 1;
                added++;
            }
            else
            {
                if (mediaFile.TrackId != track.Id)
                {
                    fileCounts[mediaFile.TrackId] = fileCounts.GetValueOrDefault(mediaFile.TrackId) - 1;
                    fileCounts[track.Id] = fileCounts.GetValueOrDefault(track.Id) + 1;
                    mediaFile.TrackId = track.Id;
                }

                updated++;
            }

            UpdateFile(mediaFile, file, meta, now);
            SetLyrics(lyrics, track.Id, meta, now);

            if (file.Artwork is { } artwork && album.ArtworkId is null)
                SetAlbum(album, a => a.ArtworkId = ResolveArtwork(artwork));
            if (meta.Year is { } year && album.Year is null)
                SetAlbum(album, a => a.Year = year);
        }

        if (existingTracksToRelink.Count > 0)
        {
            var ids = existingTracksToRelink.ToList();
            await db.TrackArtists.Where(x => ids.Contains(x.TrackId)).ExecuteDeleteAsync(cancellationToken);
            await db.TrackGenres.Where(x => ids.Contains(x.TrackId)).ExecuteDeleteAsync(cancellationToken);
        }

        foreach (var (artists, genres) in links.Values)
        {
            db.TrackArtists.AddRange(artists);
            db.TrackGenres.AddRange(genres);
        }

        await db.SaveChangesAsync(cancellationToken);
        await RemoveObsoleteCueTracksAsync(files, cancellationToken);
        await FlushPendingUpdatesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return new BatchResult(added, updated);
    }

    /// <summary>Keeps the file's lyrics unless the track already has more trusted ones (see <see cref="LyricsSource"/>).</summary>
    private void SetLyrics(Dictionary<Guid, Lyrics> lyrics, Guid trackId, AudioMetadata meta, DateTime now)
    {
        if (meta.LyricsSource is not { } source)
            return;

        if (!lyrics.TryGetValue(trackId, out var current))
        {
            current = new Lyrics { TrackId = trackId };
            db.Lyrics.Add(current);
            lyrics[trackId] = current;
        }
        else if (current.Source < source)
        {
            return;
        }

        current.Plain = meta.Lyrics;
        current.Synced = meta.SyncedLyrics;
        current.Source = source;
        current.Instrumental = false;
        current.UpdatedAt = now;
    }

    /// <summary>
    /// A file read in this batch is described by exactly its rows here (all its cue tracks travel together): rows
    /// left from an earlier cue sheet, or the whole-file row of a file that now has one, go away.
    /// </summary>
    private async Task RemoveObsoleteCueTracksAsync(IReadOnlyList<ScannedFile> files, CancellationToken cancellationToken)
    {
        var current = files.Select(f => (f.SourceId, f.RelativePath, f.CueTrack)).ToHashSet();
        var sources = files.Select(f => f.SourceId).Distinct().ToList();
        var paths = files.Select(f => f.RelativePath).Distinct().ToList();

        var rows = await db.MediaFiles.AsNoTracking()
            .Where(f => sources.Contains(f.LibrarySourceId) && paths.Contains(f.RelativePath))
            .Select(f => new { f.Id, f.LibrarySourceId, f.RelativePath, f.CueTrack })
            .ToListAsync(cancellationToken);
        var obsolete = rows.Where(r => !current.Contains((r.LibrarySourceId, r.RelativePath, r.CueTrack))).Select(r => r.Id).ToList();
        if (obsolete.Count > 0)
            await db.MediaFiles.Where(f => obsolete.Contains(f.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private static void UpdateFile(MediaFile mediaFile, ScannedFile file, AudioMetadata meta, DateTime now)
    {
        mediaFile.Size = file.Size;
        mediaFile.ModifiedAt = file.ModifiedAt;
        mediaFile.StartMs = file.StartMs;
        mediaFile.EndMs = file.EndMs;
        mediaFile.ContentHash = file.ContentHash;
        mediaFile.Format = meta.Format;
        mediaFile.BitRate = meta.BitRate;
        mediaFile.SampleRate = meta.SampleRate;
        mediaFile.BitDepth = meta.BitDepth;
        mediaFile.Channels = meta.Channels;
        mediaFile.DurationMs = meta.DurationMs;
        mediaFile.ReplayGainTrackGain = meta.ReplayGainTrackGain;
        mediaFile.ReplayGainTrackPeak = meta.ReplayGainTrackPeak;
        mediaFile.ReplayGainAlbumGain = meta.ReplayGainAlbumGain;
        mediaFile.ReplayGainAlbumPeak = meta.ReplayGainAlbumPeak;
        mediaFile.ScannedAt = now;
        mediaFile.MissingSince = null;
    }

    private Track? Match(List<Track> albumTracks, AudioMetadata meta, string normalizedTitle)
    {
        if (meta.RecordingMbid is { } mbid
            && albumTracks.FirstOrDefault(t => _mbidByRecording.TryGetValue(t.RecordingId, out var m) && m == mbid) is { } byRecording)
        {
            return byRecording;
        }

        return albumTracks.FirstOrDefault(t =>
            (t.DiscNumber ?? 1) == (meta.DiscNumber ?? 1)
            && t.TrackNumber == meta.TrackNumber
            && t.NormalizedTitle == normalizedTitle);
    }

    private AlbumState ResolveAlbum(ScannedFile file)
    {
        var meta = file.Metadata;
        var title = meta.Album ?? FolderName(file.RelativePath) ?? UnknownAlbum;

        var (artistName, artistMbid) = meta.AlbumArtist is not null
            ? (meta.AlbumArtist, meta.AlbumArtistMbid)
            : meta.IsCompilation
                ? (VariousArtists, (Guid?)null)
                : (meta.Artists.FirstOrDefault() ?? UnknownArtist, meta.ArtistMbid);
        var artistId = ResolveArtist(artistName, artistMbid, meta.SortAlbumArtist);

        if (meta.ReleaseMbid is { } release && _albumByRelease.TryGetValue(release, out var byRelease))
            return byRelease;

        var key = (artistId, TextNormalizer.Normalize(title));

        // Songs of several artists in one folder, under one album title and without album artist tags: a compilation
        // (its tags did not say so). Without this rule each artist got an album of their own.
        var folderKey = (file.SourceId, AudioFormats.Folder(file.RelativePath), key.Item2);
        var untagged = meta.AlbumArtist is null && !meta.IsCompilation && meta.Album is not null && folderKey.Item2.Length > 0;
        if (untagged && _albumByFolder.TryGetValue(folderKey, out var neighbor) && neighbor.ArtistId != artistId)
        {
            var compilation = ToCompilation(neighbor, key.Item2, title);
            _albumByFolder[folderKey] = compilation;
            return compilation;
        }

        if (_albumByKey.TryGetValue(key, out var state))
        {
            if (meta.ReleaseMbid is { } adopted && state.ReleaseMbid is null)
            {
                SetAlbum(state, a => a.MusicBrainzReleaseId = adopted);
                _albumByRelease.TryAdd(adopted, state);
            }

            if (untagged)
                _albumByFolder.TryAdd(folderKey, state);
            return state;
        }

        var album = new Album
        {
            Title = title,
            NormalizedTitle = key.Item2,
            AlbumArtistId = artistId,
            Year = meta.Year,
            IsCompilation = meta.IsCompilation || artistName == VariousArtists,
            MusicBrainzReleaseId = meta.ReleaseMbid,
        };
        db.Albums.Add(album);

        state = new AlbumState(album.Id, album)
        {
            Year = album.Year,
            ReleaseMbid = album.MusicBrainzReleaseId,
            ArtistId = album.AlbumArtistId,
            IsCompilation = album.IsCompilation,
        };
        _albumByKey[key] = state;
        _albumById[album.Id] = state;
        if (untagged)
            _albumByFolder.TryAdd(folderKey, state);
        if (meta.ReleaseMbid is { } newRelease)
            _albumByRelease.TryAdd(newRelease, state);

        return state;
    }

    /// <summary>
    /// Makes <paramref name="album"/> a Various Artists compilation, or merges it into the one that already exists
    /// with that title (its tracks move there after saving).
    /// </summary>
    private AlbumState ToCompilation(AlbumState album, string normalizedTitle, string title)
    {
        var various = ResolveArtist(VariousArtists, null, null);
        if (album.ArtistId == various)
            return album;

        if (_albumByKey.TryGetValue((various, normalizedTitle), out var existing) && existing != album)
        {
            _albumMoves[album.Id] = existing.Id;
            foreach (var folder in _albumByFolder.Where(f => f.Value == album).Select(f => f.Key).ToList())
                _albumByFolder[folder] = existing;
            return existing;
        }

        _albumByKey.Remove((album.ArtistId, normalizedTitle));
        SetAlbum(album, a =>
        {
            a.AlbumArtistId = various;
            a.IsCompilation = true;
        });
        _albumByKey[(various, normalizedTitle)] = album;
        return album;
    }

    /// <summary>Albums with files already in the batch's folders, for compilations that grow file by file.</summary>
    private async Task LoadFolderAlbumsAsync(IReadOnlyList<ScannedFile> files, CancellationToken cancellationToken)
    {
        var folders = files
            .Where(f => f.Metadata.AlbumArtist is null && !f.Metadata.IsCompilation && f.Metadata.Album is not null)
            .Select(f => (f.SourceId, Folder: AudioFormats.Folder(f.RelativePath)))
            .Where(f => f.Folder.Length > 0 && _foldersLoaded.Add(f))
            .ToList();

        foreach (var (sourceId, folder) in folders)
        {
            var prefix = folder + "/";
            var known = await db.MediaFiles.AsNoTracking()
                .Where(f => f.LibrarySourceId == sourceId && f.MissingSince == null && f.RelativePath.StartsWith(prefix))
                .Select(f => new { f.RelativePath, f.Track.AlbumId, f.Track.Album.NormalizedTitle })
                .ToListAsync(cancellationToken);

            foreach (var file in known.Where(f => f.RelativePath.IndexOf('/', prefix.Length) < 0))
            {
                if (_albumById.TryGetValue(file.AlbumId, out var state))
                    _albumByFolder.TryAdd((sourceId, folder, file.NormalizedTitle), state);
            }
        }
    }

    private Guid ResolveArtist(string name, Guid? mbid, string? sortName)
    {
        var normalized = TextNormalizer.Normalize(name);

        if (mbid is { } id)
        {
            if (_artistByMbid.TryGetValue(id, out var byMbid))
                return byMbid;

            // Same name without an ID yet: adopt the ID rather than creating a twin.
            if (_artistByName.TryGetValue(normalized, out var byName) && _artistsWithoutMbid.Remove(byName))
            {
                _artistByMbid[id] = byName;
                _pendingArtistMbids[byName] = id;
                return byName;
            }
        }
        else if (_artistByName.TryGetValue(normalized, out var byName))
        {
            return byName;
        }

        var artist = new Artist { Name = name, NormalizedName = normalized, SortName = sortName, MusicBrainzId = mbid };
        db.Artists.Add(artist);
        _artistByName.TryAdd(normalized, artist.Id);
        if (mbid is { } newId)
            _artistByMbid[newId] = artist.Id;
        else
            _artistsWithoutMbid.Add(artist.Id);

        return artist.Id;
    }

    private Guid ResolveRecording(Guid? mbid)
    {
        if (mbid is { } id && _recordingByMbid.TryGetValue(id, out var existing))
            return existing;

        var recording = new Recording { MusicBrainzId = mbid };
        db.Recordings.Add(recording);
        if (mbid is { } newId)
        {
            _recordingByMbid[newId] = recording.Id;
            _mbidByRecording[recording.Id] = newId;
        }

        return recording.Id;
    }

    private Guid ResolveArtwork(StoredArtwork stored)
    {
        if (_artworkByHash.TryGetValue(stored.Hash, out var id))
            return id;

        var artwork = new Artwork
        {
            Hash = stored.Hash,
            MimeType = stored.MimeType,
            Width = stored.Width,
            Height = stored.Height,
            Source = ArtworkSource.Embedded,
        };
        db.Artworks.Add(artwork);
        _artworkByHash[stored.Hash] = artwork.Id;
        return artwork.Id;
    }

    private Guid ResolveGenre(string name)
    {
        var normalized = TextNormalizer.Normalize(name);
        if (_genreByName.TryGetValue(normalized, out var id))
            return id;

        var genre = new Genre { Name = name, NormalizedName = normalized };
        db.Genres.Add(genre);
        _genreByName[normalized] = genre.Id;
        return genre.Id;
    }

    private List<TrackArtist> BuildArtists(Guid trackId, AudioMetadata meta)
    {
        var credits = new List<(string Name, Guid? Mbid, ArtistRole Role)>();
        var primary = meta.Artists.Count > 0 ? meta.Artists : [meta.AlbumArtist ?? UnknownArtist];
        credits.AddRange(primary.Select((name, i) => (name, i == 0 ? meta.ArtistMbid : null, ArtistRole.Primary)));
        credits.AddRange(meta.FeaturedArtists.Select(name => (name, (Guid?)null, ArtistRole.Featured)));
        credits.AddRange(meta.Composers.Select(name => (name, (Guid?)null, ArtistRole.Composer)));
        credits.AddRange(meta.Lyricists.Select(name => (name, (Guid?)null, ArtistRole.Lyricist)));
        if (meta.Conductor is not null)
            credits.Add((meta.Conductor, null, ArtistRole.Conductor));

        var result = new List<TrackArtist>();
        foreach (var (name, mbid, role) in credits)
        {
            var artistId = ResolveArtist(name, mbid, role == ArtistRole.Primary && result.Count == 0 ? meta.SortArtist : null);
            if (result.All(r => r.ArtistId != artistId || r.Role != role))
                result.Add(new TrackArtist { TrackId = trackId, ArtistId = artistId, Role = role, Position = result.Count });
        }

        return result;
    }

    private List<TrackGenre> BuildGenres(Guid trackId, AudioMetadata meta) =>
        meta.Genres.Select(ResolveGenre).Distinct().Select(genreId => new TrackGenre { TrackId = trackId, GenreId = genreId }).ToList();

    private void SetAlbum(AlbumState state, Action<Album> change)
    {
        if (state.Entity is not null)
        {
            change(state.Entity);
            state.Sync(state.Entity);
            return;
        }

        var snapshot = new Album
        {
            Title = "",
            NormalizedTitle = "",
            ArtworkId = state.ArtworkId,
            Year = state.Year,
            MusicBrainzReleaseId = state.ReleaseMbid,
            AlbumArtistId = state.ArtistId,
            IsCompilation = state.IsCompilation,
        };
        change(snapshot);
        state.Sync(snapshot);
        _dirtyAlbums.Add(state);
    }

    private async Task FlushPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        foreach (var (artistId, mbid) in _pendingArtistMbids)
            await db.Artists.Where(a => a.Id == artistId).ExecuteUpdateAsync(s => s.SetProperty(a => a.MusicBrainzId, mbid), cancellationToken);
        _pendingArtistMbids.Clear();

        foreach (var album in _dirtyAlbums)
        {
            await db.Albums.Where(a => a.Id == album.Id).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ArtworkId, album.ArtworkId)
                .SetProperty(a => a.Year, album.Year)
                .SetProperty(a => a.MusicBrainzReleaseId, album.ReleaseMbid)
                .SetProperty(a => a.AlbumArtistId, album.ArtistId)
                .SetProperty(a => a.IsCompilation, album.IsCompilation), cancellationToken);
        }
        _dirtyAlbums.Clear();

        // The emptied albums are removed by the cleaner at the end of the scan.
        foreach (var (from, to) in _albumMoves)
            await db.Tracks.Where(t => t.AlbumId == from).ExecuteUpdateAsync(s => s.SetProperty(t => t.AlbumId, to), cancellationToken);
        _albumMoves.Clear();

        // Albums created in this batch are now rows like any other.
        foreach (var state in _albumByKey.Values)
            state.Entity = null;
    }

    /// <summary>
    /// Untagged files in the common "Artist/Album/Track" layout take the artist from the folder, so they do not all
    /// pile up under "Unknown Artist". Only applies with no artist tags at all and at least two folder levels.
    /// </summary>
    private static ScannedFile WithFolderArtist(ScannedFile file)
    {
        var meta = file.Metadata;
        if (meta.Artists.Count > 0 || meta.AlbumArtist is not null || meta.IsCompilation)
            return file;

        var folders = AudioFormats.Folder(file.RelativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return folders.Length >= 2 ? file with { Metadata = meta with { Artists = [folders[^2]] } } : file;
    }

    private static string? FolderName(string relativePath)
    {
        var folder = AudioFormats.Folder(relativePath);
        var name = folder.Length == 0 ? "" : folder[(folder.LastIndexOf('/') + 1)..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>What the writer needs to know about an album between batches.</summary>
    private sealed class AlbumState(Guid id, Album? entity)
    {
        public Guid Id { get; } = id;

        /// <summary>Set while the album is new in the current batch and tracked by the context.</summary>
        public Album? Entity { get; set; } = entity;

        public Guid? ArtworkId { get; set; }

        public int? Year { get; set; }

        public Guid? ReleaseMbid { get; set; }

        public Guid ArtistId { get; set; }

        public bool IsCompilation { get; set; }

        public void Sync(Album album)
        {
            ArtworkId = album.ArtworkId;
            Year = album.Year;
            ReleaseMbid = album.MusicBrainzReleaseId;
            ArtistId = album.AlbumArtistId;
            IsCompilation = album.IsCompilation;
        }
    }
}
