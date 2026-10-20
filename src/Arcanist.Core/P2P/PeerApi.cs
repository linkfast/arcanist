using System.Security.Claims;
using Arcanist.Core.Application;
using Arcanist.Core.Auth;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Arcanist.Core.P2P;

// The peer API: what a server that receives a share asks the server that shares, through the P2P network. Its
// types are part of the server-to-server contract (in the OpenAPI document, tag "Peer"); players never use it.

/// <summary>A shared library, as the receiving server sees it.</summary>
public sealed record PeerLibrary(Guid Id, string Name);

/// <summary>What the sharing server tells about itself and the share.</summary>
public sealed record PeerShareInfo(string Name, P2PRules Rules, IReadOnlyList<PeerLibrary> Libraries, DateTime? ExpiresAt);

/// <summary>
/// One shared file, described so the receiving server can catalog it without reading it. <see cref="Path"/> starts
/// with the name of its library (and <c>-2</c>, <c>-3</c>… for further folders of it), so paths never collide.
/// </summary>
public sealed record PeerFile(
    Guid Id,
    string Path,
    int CueTrack,
    int? StartMs,
    int? EndMs,
    long Size,
    DateTime ModifiedAt,
    string ContentHash,
    string? Artwork,
    AudioMetadata Metadata);

public sealed record PeerFilePage(IReadOnlyList<PeerFile> Items, Guid? NextCursor);

/// <summary>Content hashes of shared files whose analyses the receiving server wants (up to 200).</summary>
public sealed record PeerAnalysesRequest(IReadOnlyList<string> Hashes);

/// <summary>
/// The sharing server's analysis of a shared file's audio (loudness, tempo, key, fingerprints, waveform), so the
/// receiving server has them without hearing it. Byte arrays travel as base64.
/// </summary>
public sealed record PeerAnalysis(
    string ContentHash,
    int AnalyzerVersion,
    DateTime AnalyzedAt,
    int DurationMs,
    double? Loudness,
    double? LoudnessRange,
    double? TruePeak,
    string? LoudnessHistogram,
    float? Bpm,
    float? BpmConfidence,
    int? KeyTonic,
    bool? KeyMinor,
    float? KeyConfidence,
    string? AcoustIdFingerprint,
    byte[]? Fingerprint,
    int? FingerprintDurationMs,
    byte[]? Waveform);

/// <summary>A play of a shared song by a user of the receiving server.</summary>
/// <param name="Id">Its id there, so a play sent again is recognized.</param>
/// <param name="User">The user's name there.</param>
public sealed record PeerPlay(string Id, string Path, int CueTrack, string User, DateTime PlayedAt, int PlayedMs, bool Counted, bool Skipped);

/// <summary>Plays of shared songs (up to 500), for a server that counts them (its rule <c>countPlays</c>).</summary>
public sealed record PeerPlaysRequest(IReadOnlyList<PeerPlay> Plays);

/// <param name="Recorded">New plays; those sent before are not counted again.</param>
public sealed record PeerPlaysResult(int Recorded);

/// <summary>Serves the peer API to server peers, within the libraries shared with each.</summary>
public sealed class PeerApi(IArcanistDbContextFactory databases, P2PSettings settings, TimeProvider time)
{
    public const int PageSize = 500;

    /// <summary>Makes the request act as the peer, for the application services: it sees the shared libraries only.</summary>
    public static UserAccess SignIn(HttpContext context, P2PPeerSnapshot peer)
    {
        var access = new UserAccess(peer.Id, "peer:" + peer.NodeId[..12], peer.Name, UserRole.User,
            peer.Rules.AllowDownload ? Permissions.Download : Permissions.None, "", false, peer.LibraryIds);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ArcanistClaims.UserId, access.UserId.ToString()),
            new Claim(ArcanistClaims.SessionId, Guid.Empty.ToString()),
        ], "p2p"));
        context.Items[typeof(UserAccess)] = access;
        return access;
    }

    public async Task<PeerShareInfo> InfoAsync(P2PPeerSnapshot peer, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var libraries = await db.Libraries.AsNoTracking().Where(l => peer.LibraryIds.Contains(l.Id))
            .OrderBy(l => l.Number).Select(l => new PeerLibrary(l.Id, l.Name)).ToListAsync(cancellationToken);
        var config = await settings.GetAsync(cancellationToken);
        return new PeerShareInfo(config.DisplayName, peer.Rules, libraries, peer.ExpiresAt);
    }

    /// <summary>The shared files present now, in a stable order, a page at a time.</summary>
    public async Task<PeerFilePage> FilesAsync(P2PPeerSnapshot peer, Guid? after, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var prefixes = await PrefixesAsync(db, peer, cancellationToken);
        var query = db.MediaFiles.AsNoTracking()
            .Where(f => f.MissingSince == null && prefixes.Keys.Contains(f.LibrarySourceId));
        if (after is { } cursor)
            query = query.Where(f => f.Id.CompareTo(cursor) > 0);

        var files = await query.OrderBy(f => f.Id).Take(PageSize + 1)
            .Include(f => f.Track).ThenInclude(t => t.Album).ThenInclude(a => a.AlbumArtist)
            .Include(f => f.Track).ThenInclude(t => t.Album).ThenInclude(a => a.Artwork)
            .Include(f => f.Track).ThenInclude(t => t.Artwork)
            .Include(f => f.Track).ThenInclude(t => t.Recording)
            .Include(f => f.Track).ThenInclude(t => t.Artists.OrderBy(a => a.Position)).ThenInclude(a => a.Artist)
            .Include(f => f.Track).ThenInclude(t => t.Genres).ThenInclude(g => g.Genre)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var page = files.Take(PageSize).ToList();
        var trackIds = page.Select(f => f.TrackId).Distinct().ToList();
        var lyrics = await db.Lyrics.AsNoTracking().Where(l => trackIds.Contains(l.TrackId)).ToDictionaryAsync(l => l.TrackId, cancellationToken);

        return new PeerFilePage(
            page.Select(f => Describe(f, prefixes[f.LibrarySourceId], lyrics.GetValueOrDefault(f.TrackId))).ToList(),
            files.Count > PageSize ? page[^1].Id : null);
    }

    /// <summary>
    /// The file a peer path names, if it is shared with the peer. With <paramref name="cueTrack"/> null, any row of the
    /// path (a whole file split by a cue sheet, for downloads).
    /// </summary>
    public async Task<MediaFile?> FindAsync(P2PPeerSnapshot peer, string path, int? cueTrack, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var prefixes = await PrefixesAsync(db, peer, cancellationToken);
        foreach (var (sourceId, prefix) in prefixes)
        {
            if (!path.StartsWith(prefix + "/", StringComparison.Ordinal))
                continue;
            var relative = path[(prefix.Length + 1)..];
            return await db.MediaFiles.AsNoTracking()
                .Where(f => f.LibrarySourceId == sourceId && f.RelativePath == relative && f.MissingSince == null
                            && (cueTrack == null || f.CueTrack == cueTrack))
                .OrderBy(f => f.CueTrack)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    public const int MaxAnalyses = 200;
    public const int MaxPlays = 500;

    /// <summary>The analyses of the audio of files shared with the peer, among <paramref name="hashes"/>.</summary>
    public async Task<IReadOnlyList<PeerAnalysis>> AnalysesAsync(P2PPeerSnapshot peer, IReadOnlyList<string> hashes, CancellationToken cancellationToken)
    {
        if (hashes.Count > MaxAnalyses)
            throw ArcanistException.Field("hashes", ErrorCodes.TooMany);
        await using var db = databases.CreateDbContext();
        var prefixes = await PrefixesAsync(db, peer, cancellationToken);
        var shared = db.MediaFiles.Where(f => f.MissingSince == null && prefixes.Keys.Contains(f.LibrarySourceId) && hashes.Contains(f.ContentHash))
            .Select(f => f.ContentHash);
        return await db.AudioAnalyses.AsNoTracking()
            .Where(a => a.State == AnalysisState.Analyzed && shared.Contains(a.ContentHash))
            .Select(a => new PeerAnalysis(a.ContentHash, a.AnalyzerVersion, a.AnalyzedAt, a.DurationMs, a.Loudness, a.LoudnessRange, a.TruePeak,
                a.LoudnessHistogram, a.Bpm, a.BpmConfidence, a.KeyTonic, a.KeyMinor, a.KeyConfidence, a.AcoustIdFingerprint, a.Fingerprint,
                a.FingerprintDurationMs, a.Waveform))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Records plays of shared songs by the peer's users, when its rules say they count here. Each keeps the user's
    /// name there; plays older than 14 days, of songs not shared, or sent before are left out.
    /// </summary>
    public async Task<PeerPlaysResult> RecordPlaysAsync(P2PPeerSnapshot peer, IReadOnlyList<PeerPlay> plays, CancellationToken cancellationToken)
    {
        if (!peer.Rules.CountPlays)
            throw new ArcanistException.Forbidden(ErrorCodes.PlaysNotAccepted);
        if (plays.Count > MaxPlays)
            throw ArcanistException.Field("plays", ErrorCodes.TooMany);

        var now = time.GetUtcNow().UtcDateTime;
        await using var db = databases.CreateDbContext();
        var ids = plays.Select(p => p.Id).ToList();
        var known = (await db.RemotePlayEvents.Where(p => p.PeerId == peer.Id && ids.Contains(p.ExternalId)).Select(p => p.ExternalId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var recorded = 0;
        foreach (var play in plays)
        {
            var playedAt = DateTime.SpecifyKind(play.PlayedAt, DateTimeKind.Utc);
            if (known.Contains(play.Id) || play.Id.Length is 0 or > 128 || string.IsNullOrWhiteSpace(play.User)
                || playedAt < now.AddDays(-14) || playedAt > now.AddDays(1) || play.PlayedMs < 0)
                continue;
            if (await FindAsync(peer, play.Path, play.CueTrack, cancellationToken) is not { } file)
                continue;
            db.RemotePlayEvents.Add(new RemotePlayEvent
            {
                PeerId = peer.Id,
                RemoteUser = play.User.Trim()[..Math.Min(play.User.Trim().Length, 128)],
                TrackId = file.TrackId,
                PlayedAt = playedAt,
                PlayedMs = play.PlayedMs,
                Counted = play.Counted,
                Skipped = play.Skipped,
                ExternalId = play.Id,
            });
            known.Add(play.Id);
            recorded++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PeerPlaysResult(recorded);
    }

    /// <summary>The artwork a peer may see: of a track in its shared libraries.</summary>
    public async Task<Artwork?> ArtworkAsync(P2PPeerSnapshot peer, string hash, CancellationToken cancellationToken)
    {
        await using var db = databases.CreateDbContext();
        var artwork = await db.Artworks.AsNoTracking().SingleOrDefaultAsync(a => a.Hash == hash, cancellationToken);
        if (artwork is null)
            return null;
        var shared = await db.MediaFiles.AnyAsync(f => peer.LibraryIds.Contains(f.LibrarySource.LibraryId)
                                                       && (f.Track.ArtworkId == artwork.Id || f.Track.Album.ArtworkId == artwork.Id), cancellationToken);
        return shared ? artwork : null;
    }

    /// <summary>The path prefix of each folder of the shared libraries: the library's name, then "-2", "-3"….</summary>
    private static async Task<Dictionary<Guid, string>> PrefixesAsync(ArcanistDbContext db, P2PPeerSnapshot peer, CancellationToken cancellationToken)
    {
        var sources = await db.LibrarySources.AsNoTracking()
            .Where(s => peer.LibraryIds.Contains(s.LibraryId) && s.Kind == LibrarySourceKind.Local)
            .Select(s => new { s.Id, s.Library.Name, s.Library.Number })
            .ToListAsync(cancellationToken);
        return sources.OrderBy(s => s.Number).ThenBy(s => s.Id).GroupBy(s => s.Name)
            .SelectMany(g => g.Select((s, i) => (s.Id, Prefix: i == 0 ? Clean(g.Key) : $"{Clean(g.Key)}-{i + 1}")))
            .ToDictionary(s => s.Id, s => s.Prefix);

        static string Clean(string name) => name.Replace('/', '-').Trim();
    }

    private static PeerFile Describe(MediaFile file, string prefix, Lyrics? lyrics)
    {
        var track = file.Track;
        var artists = track.Artists.Where(a => a.Role == ArtistRole.Primary).Select(a => a.Artist).ToList();
        string[] Named(ArtistRole role) => track.Artists.Where(a => a.Role == role).Select(a => a.Artist.Name).ToArray();
        var metadata = new AudioMetadata
        {
            Title = track.Title,
            Artists = artists.Select(a => a.Name).ToArray(),
            FeaturedArtists = Named(ArtistRole.Featured),
            Composers = Named(ArtistRole.Composer),
            Lyricists = Named(ArtistRole.Lyricist),
            Conductor = Named(ArtistRole.Conductor).FirstOrDefault(),
            AlbumArtist = track.Album.AlbumArtist.Name,
            SortAlbumArtist = track.Album.AlbumArtist.SortName,
            SortArtist = artists.FirstOrDefault()?.SortName,
            Album = track.Album.Title,
            Genres = track.Genres.Select(g => g.Genre.Name).ToArray(),
            Year = track.Album.Year,
            TrackNumber = track.TrackNumber,
            DiscNumber = track.DiscNumber,
            IsCompilation = track.Album.IsCompilation,
            RecordingMbid = track.Recording.MusicBrainzId,
            ReleaseMbid = track.Album.MusicBrainzReleaseId,
            ArtistMbid = artists.FirstOrDefault()?.MusicBrainzId,
            AlbumArtistMbid = track.Album.AlbumArtist.MusicBrainzId,
            ReplayGainTrackGain = file.ReplayGainTrackGain,
            ReplayGainTrackPeak = file.ReplayGainTrackPeak,
            ReplayGainAlbumGain = file.ReplayGainAlbumGain,
            ReplayGainAlbumPeak = file.ReplayGainAlbumPeak,
            Format = file.Format,
            BitRate = file.BitRate,
            SampleRate = file.SampleRate,
            BitDepth = file.BitDepth,
            Channels = file.Channels,
            DurationMs = file.DurationMs,
            Lyrics = lyrics?.Plain,
            SyncedLyrics = lyrics?.Synced,
            LyricsSource = lyrics?.Source,
        };
        var artwork = track.Artwork?.Hash ?? track.Album.Artwork?.Hash;
        return new PeerFile(file.Id, $"{prefix}/{file.RelativePath}", file.CueTrack, file.StartMs, file.EndMs, file.Size, file.ModifiedAt,
            file.ContentHash, artwork, metadata);
    }
}
